"""AI Video Studio — local video analyzer (runs only on the owner's PC; nothing is sent to a paid / cloud AI).

    python -I studio_analyzer.py probe   --home D:\\TramTinAI
    python -I studio_analyzer.py bench   --home D:\\TramTinAI --model 4b
    python -I studio_analyzer.py analyze --job job.json

Pipeline of `analyze` (JSON lines on stdout: {"type": "progress"|"done"|"error", ...}):
  1. FFmpeg decodes the video once at a low resolution (≤ 10 fps): colour histograms, brightness and dense optical flow
     (OpenCV Farneback) per sampled frame.
  2. Scene cuts: adaptive content detector (a frame much more different than its neighbours) + fades to black.
     Shots longer than the video model's clip limit are split at the calmest moment; flashes shorter than 0.8 s merge.
  3. Measured facts per scene, no AI: camera movement (global flow → pan / tilt / push-in / pull-out / handheld /
     static), subject motion level, brightness, contrast, saturation, warm / cool, dominant colours (k-means in Lab).
  4. Adaptive keyframes: start, end, equal steps of accumulated visual change (action-based) and motion peaks.
  5. Optional speech transcript (whisper.cpp, if installed).
  6. A local vision-language model (Qwen3-VL GGUF through llama.cpp's llama-server, Vulkan GPU or CPU) reads each
     scene's keyframes with the measured facts and returns a schema-constrained JSON description; one text pass then
     writes the character / environment / visual-style bibles. Scene results are cached, so a cancelled or failed run
     resumes where it stopped.
"""
import argparse
import atexit
import base64
import hashlib
import json
import math
import os
import platform
import socket
import subprocess
import sys
import time
import wave
import urllib.error
import urllib.request

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
except Exception:  # noqa: BLE001
    pass

PROMPT_VERSION = 7  # bump when the scene prompt / schema changes (invalidates cached scene results)
MAX_CLIP = 12.0  # longest clip the video model makes (Seedance 1.0: 2–12 s)
SPLIT_TARGET = 10.0
MIN_SCENE = 0.8
ANALYSIS_FPS = 10.0
ANALYSIS_WIDTH = 320

MODELS = {
    # id: (weights, vision projector, GPU layers on a 4 GB card, context)
    "4b": ("Qwen3VL-4B-Instruct-Q4_K_M.gguf", "mmproj-Qwen3VL-4B-Instruct-Q8_0.gguf", 99, 8192),
    "8b": ("Qwen3VL-8B-Instruct-Q4_K_M.gguf", "mmproj-Qwen3VL-8B-Instruct-Q8_0.gguf", 20, 8192),
}

_server = None


def emit(**kw):
    print(json.dumps(kw, ensure_ascii=False), flush=True)


def progress(stage, message, done=0, total=0, percent=None):
    d = {"type": "progress", "stage": stage, "message": message, "done": done, "total": total}
    if percent is not None:
        d["percent"] = round(float(percent), 1)
    emit(**d)


def fail(message, code=1):
    emit(type="error", message=message)
    sys.exit(code)


# ───────────────────────────── paths / probe ─────────────────────────────

def llm_dir(home):
    return os.path.join(home, "llm")


def server_exe(home, backend):
    exe = "llama-server.exe" if os.name == "nt" else "llama-server"
    return os.path.join(llm_dir(home), f"bin-{backend}", exe)


def model_files(home, model):
    w, mm, _, _ = MODELS[model]
    return os.path.join(llm_dir(home), "models", w), os.path.join(llm_dir(home), "models", mm)


# the most accurate speech model present wins (Vietnamese needs at least "small"; large-v3-turbo is far better)
WHISPER_MODELS = ("ggml-large-v3-turbo.bin", "ggml-large-v3-turbo-q8_0.bin", "ggml-large-v3-turbo-q5_0.bin",
                  "ggml-medium.bin", "ggml-small.bin")


def whisper_files(home):
    exe = "whisper-cli.exe" if os.name == "nt" else "whisper-cli"
    root = os.path.join(home, "whisper")
    model = next((os.path.join(root, m) for m in WHISPER_MODELS if os.path.isfile(os.path.join(root, m))),
                 os.path.join(root, "ggml-small.bin"))
    return os.path.join(root, "bin", exe), model


def cmd_probe(a):
    out = {"type": "done", "python": platform.python_version(), "ok": False}
    try:
        import cv2  # noqa: F401
        import numpy  # noqa: F401
        out["opencv"] = cv2.__version__
        out["numpy"] = numpy.__version__
    except Exception as e:  # noqa: BLE001
        out["error"] = f"Thiếu thư viện Python: {e}"
        emit(**out)
        return
    out["backends"] = {b: os.path.isfile(server_exe(a.home, b)) for b in ("vulkan", "cpu")}
    out["models"] = {m: all(os.path.isfile(p) for p in model_files(a.home, m)) for m in MODELS}
    wexe, wmodel = whisper_files(a.home)
    out["whisper"] = os.path.isfile(wexe) and os.path.isfile(wmodel)
    out["whisperModel"] = os.path.basename(wmodel) if out["whisper"] else None
    if not any(out["backends"].values()):
        out["error"] = "Chưa có llama.cpp (llama-server) — chạy scripts\\pc\\setup-ai-studio.bat."
    elif not any(out["models"].values()):
        out["error"] = "Chưa có model Qwen3-VL — chạy scripts\\pc\\setup-ai-studio.bat."
    else:
        out["ok"] = True
    emit(**out)


# ───────────────────────────── llama-server ─────────────────────────────

def free_port():
    s = socket.socket()
    s.bind(("127.0.0.1", 0))
    port = s.getsockname()[1]
    s.close()
    return port


def stop_server():
    global _server
    if _server is not None:
        try:
            _server[0].kill()
            _server[0].wait(timeout=10)
        except Exception:  # noqa: BLE001
            pass
        _server = None


atexit.register(stop_server)


def start_server(home, model, threads, gpu, work):
    """Starts llama-server (Vulkan GPU when present and allowed, else CPU). Returns (port, backend)."""
    global _server
    weights, mmproj = model_files(home, model)
    if not (os.path.isfile(weights) and os.path.isfile(mmproj)):
        raise RuntimeError(f"Chưa tải model {model.upper()} — chạy scripts\\pc\\setup-ai-studio.bat.")
    _, _, gpu_layers, ctx = MODELS[model]
    order = []
    if gpu != "cpu" and os.path.isfile(server_exe(home, "vulkan")):
        order.append("vulkan")
    if os.path.isfile(server_exe(home, "cpu")):
        order.append("cpu")
    if not order:
        raise RuntimeError("Chưa có llama.cpp — chạy scripts\\pc\\setup-ai-studio.bat.")
    last = ""
    for backend in order:
        port = free_port()
        args = [server_exe(home, backend), "-m", weights, "--mmproj", mmproj, "--host", "127.0.0.1", "--port", str(port),
                "-c", str(ctx), "-t", str(threads), "-np", "1", "--no-webui", "--image-max-tokens", "640"]
        if backend == "vulkan":
            # the vision projector stays on the CPU: on a 4 GB card the language model's layers matter more
            args += ["-ngl", str(gpu_layers), "--no-mmproj-offload"]
        else:
            args += ["-ngl", "0"]
        log_path = os.path.join(work, f"llama-{backend}.log")
        log = open(log_path, "w", encoding="utf-8", errors="replace")
        proc = subprocess.Popen(args, stdout=log, stderr=subprocess.STDOUT,
                                creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        _server = (proc, port, backend)
        started = time.time()
        while time.time() - started < 300:
            if proc.poll() is not None:
                break
            try:
                with urllib.request.urlopen(f"http://127.0.0.1:{port}/health", timeout=2) as r:
                    if r.status == 200:
                        return port, backend
            except Exception:  # noqa: BLE001
                pass
            time.sleep(0.5)
        stop_server()
        log.close()
        try:
            with open(log_path, encoding="utf-8", errors="replace") as f:
                last = f.read()[-600:]
        except OSError:
            pass
    raise RuntimeError("Không khởi động được model AI trên máy: " + last.strip())


def chat(port, content, schema, max_tokens, temperature=0.1, timeout=1800):
    body = {
        "messages": [
            {"role": "system", "content": (
                "You are a meticulous film analyst. Describe ONLY what is visible in the frames. Never guess identities, "
                "names, brands or exact camera / lens specifications. Use short precise phrases. When something cannot be "
                "seen clearly, write \"uncertain\" instead of inventing it.")},
            {"role": "user", "content": content}],
        "temperature": temperature,
        "top_p": 0.9,
        "max_tokens": max_tokens,
        "response_format": {"type": "json_schema", "json_schema": {"name": "analysis", "schema": schema, "strict": True}},
    }
    req = urllib.request.Request(f"http://127.0.0.1:{port}/v1/chat/completions", json.dumps(body).encode("utf-8"),
                                 {"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=timeout) as r:
        out = json.load(r)
    text = out["choices"][0]["message"]["content"]
    return json.loads(text), out.get("timings", {})


# ───────────────────────────── schemas ─────────────────────────────

def _s():
    return {"type": "string"}


def _obj(fields, extra=None):
    props = {f: _s() for f in fields}
    if extra:
        props.update(extra)
    return {"type": "object", "properties": props, "required": list(props.keys()), "additionalProperties": False}


ANGLES = ["eye-level", "low angle", "high angle", "bird's-eye view", "over-the-shoulder", "uncertain"]
SIZES = ["extreme close-up", "close-up", "medium close-up", "medium shot", "medium wide shot", "wide shot",
         "extreme wide shot", "uncertain"]

SCENE_SCHEMA = _obj(["summary", "summary_vi", "purpose", "object_interaction"], {
    "characters": {"type": "array", "maxItems": 4, "items": _obj(
        ["label", "gender", "age_group", "apparent_ethnicity", "skin_tone", "build", "appearance", "hair", "clothing",
         "footwear", "accessories", "position", "posture", "gaze"])},
    "timeline": {"type": "array", "minItems": 1, "maxItems": 10, "items": {
        "type": "object", "additionalProperties": False,
        "required": ["segment", "action", "body", "confidence"],
        "properties": {"segment": {"type": "integer"}, "action": _s(), "body": _s(),
                       "confidence": {"type": "string", "enum": ["high", "medium", "low"]}}}},
    "body_movement": _obj(["head", "shoulders", "arms", "hands", "torso", "hips", "legs", "feet", "coordination"]),
    "facial_expression": _obj(["expression", "emotion", "eyes", "gaze_direction", "changes"]),
    "environment": _obj(["location", "space", "landscape", "architecture", "background", "foreground", "weather",
                         "time_of_day"], {"objects": {"type": "array", "maxItems": 8, "items": _s()}}),
    "camera": _obj(["movement_seen"], {"angle": {"type": "string", "enum": ANGLES},
                                       "shot_size": {"type": "string", "enum": SIZES}}),
    "composition": _obj(["subject_placement", "rule_of_thirds", "leading_lines", "foreground", "background",
                         "negative_space", "depth"]),
    "lighting": _obj(["source", "direction", "intensity", "shadows", "highlights", "color_temperature"]),
    "color": _obj(["dominant", "tone", "saturation", "contrast", "white_balance", "grading"]),
    "depth_of_field": _obj(["focus_subject", "background_blur", "foreground_blur", "focus_change", "lens_look"]),
    "motion_quality": _obj(["naturalness", "acceleration", "inertia", "balance", "cloth", "hair", "environment"]),
    "realism": _obj(["skin", "hair", "fabric", "surfaces", "reflections", "shadows", "physics", "overall"]),
    "confidence": {"type": "number"},
})

BIBLE_SCHEMA = {
    "type": "object", "additionalProperties": False, "required": ["overview", "characters", "environments", "style"],
    "properties": {
        "overview": _obj(["summary", "summary_vi", "scene_relations"]),
        "characters": {"type": "array", "maxItems": 6, "items": _obj(
            ["id", "label", "gender", "age_group", "apparent_ethnicity", "skin_tone", "build", "appearance", "hair",
             "clothing", "footwear", "accessories", "posture", "distinguishing", "consistency"],
            {"scenes": {"type": "array", "items": {"type": "integer"}}})},
        "environments": {"type": "array", "maxItems": 6, "items": _obj(
            ["id", "location", "layout", "background", "important_objects", "lighting", "weather", "time_of_day"],
            {"scenes": {"type": "array", "items": {"type": "integer"}}})},
        "style": _obj(["visual_style", "realism_level", "color_palette", "lighting_style", "camera_language",
                       "motion_style", "depth_of_field", "direction"]),
    },
}


# ───────────────────────────── decoding + measurements ─────────────────────────────

def ffprobe_json(ffprobe, path):
    r = subprocess.run([ffprobe, "-v", "error", "-print_format", "json", "-show_format", "-show_streams", path],
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    if r.returncode != 0:
        raise RuntimeError("Không đọc được video: " + r.stderr[-400:])
    return json.loads(r.stdout)


def decode_pass(ffmpeg, path, duration, width, height, on_progress):
    """Low-resolution decode: per sampled frame its time, HSV histogram, brightness and optical-flow statistics."""
    import cv2
    import numpy as np

    w = ANALYSIS_WIDTH
    h = max(2, int(round(height * w / width / 2)) * 2)
    cmd = [ffmpeg, "-hide_banner", "-v", "error", "-i", path, "-an", "-vf", f"fps={ANALYSIS_FPS},scale={w}:{h}",
           "-f", "rawvideo", "-pix_fmt", "bgr24", "-"]
    proc = subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
    size = w * h * 3
    frames = []
    prev_gray = None
    fw, fh = 160, max(2, int(round(h * 160 / w)))
    ys, xs = np.mgrid[0:fh, 0:fw].astype(np.float32)
    cx, cy = (fw - 1) / 2.0, (fh - 1) / 2.0
    rx, ry = xs - cx, ys - cy
    rn = np.sqrt(rx * rx + ry * ry) + 1e-6
    ux, uy = rx / rn, ry / rn
    ring = rn > min(fw, fh) * 0.15
    i = 0
    total_est = max(1, int(duration * ANALYSIS_FPS))
    while True:
        buf = proc.stdout.read(size)
        if len(buf) < size:
            break
        img = np.frombuffer(buf, np.uint8).reshape(h, w, 3)
        hsv = cv2.cvtColor(img, cv2.COLOR_BGR2HSV)
        hist = np.concatenate([
            cv2.calcHist([hsv], [0], None, [16], [0, 180]).ravel(),
            cv2.calcHist([hsv], [1], None, [8], [0, 256]).ravel(),
            cv2.calcHist([hsv], [2], None, [8], [0, 256]).ravel()])
        hist = hist / (hist.sum() / 3.0 + 1e-6)
        gray = cv2.cvtColor(cv2.resize(img, (fw, fh), interpolation=cv2.INTER_AREA), cv2.COLOR_BGR2GRAY)
        f = {"t": i / ANALYSIS_FPS, "hist": hist.astype(np.float32), "luma": float(hsv[..., 2].mean()),
             "sat": float(hsv[..., 1].mean()), "dx": 0.0, "dy": 0.0, "zoom": 0.0, "resid": 0.0, "pix": 0.0}
        if prev_gray is not None:
            flow = cv2.calcOpticalFlowFarneback(prev_gray, gray, None, 0.5, 3, 15, 3, 5, 1.2, 0)
            fx, fy = flow[..., 0], flow[..., 1]
            mdx, mdy = float(np.median(fx)), float(np.median(fy))
            radial = (fx - mdx) * ux + (fy - mdy) * uy
            zoom = float(np.median(radial[ring] / rn[ring]))  # relative expansion per frame
            rx_ = fx - mdx - zoom * rx
            ry_ = fy - mdy - zoom * ry
            resid = float(np.mean(np.sqrt(rx_ * rx_ + ry_ * ry_)))
            f.update(dx=mdx / fw, dy=mdy / fh, zoom=zoom, resid=resid / fw,
                     pix=float(np.mean(cv2.absdiff(prev_gray, gray))) / 255.0)
        prev_gray = gray
        frames.append(f)
        i += 1
        if i % 25 == 0:
            on_progress(min(i, total_est), total_est)
    proc.wait()
    if not frames:
        err = proc.stderr.read().decode("utf-8", "replace")[-400:]
        raise RuntimeError("FFmpeg không giải mã được video: " + err)
    return frames


def content_scores(frames):
    import cv2
    import numpy as np
    scores = [0.0]
    for a, b in zip(frames, frames[1:]):
        d = 0.0
        for lo, hi in ((0, 16), (16, 24), (24, 32)):
            d += cv2.compareHist(a["hist"][lo:hi], b["hist"][lo:hi], cv2.HISTCMP_BHATTACHARYYA)
        scores.append(float(0.65 * d / 3.0 + 0.35 * min(1.0, b["pix"] * 4.0)))
    return np.array(scores, np.float32)


def detect_cuts(frames, scores, duration):
    """Cut times (seconds): adaptive content detector + the ends of fades to / from black."""
    import numpy as np
    n = len(frames)
    cuts = []
    for i in range(1, n):
        lo, hi = max(1, i - 3), min(n, i + 4)
        neigh = [scores[j] for j in range(lo, hi) if j != i]
        avg = float(np.mean(neigh)) if neigh else 0.0
        s = float(scores[i])
        if s > 0.55 or (s > 0.28 and s > 3.0 * (avg + 0.01)):
            cuts.append(frames[i]["t"])
    # fades: a dark run ≥ 0.3 s in the middle of the video → cut at its end
    dark = [f["luma"] < 14 for f in frames]
    i = 0
    while i < n:
        if dark[i]:
            j = i
            while j < n and dark[j]:
                j += 1
            if (j - i) / ANALYSIS_FPS >= 0.3 and 0 < i and j < n:
                cuts.append(frames[j]["t"])
            i = j
        else:
            i += 1
    cuts = sorted(set(round(c, 2) for c in cuts if MIN_SCENE <= c <= duration - MIN_SCENE))
    merged = []
    for c in cuts:
        if not merged or c - merged[-1] >= MIN_SCENE:
            merged.append(c)
    return merged


def split_long(start, end, frames):
    """Shots longer than the clip limit are split at the calmest moments near equal steps."""
    import numpy as np
    dur = end - start
    if dur <= MAX_CLIP:
        return [(start, end, False)]
    parts = int(math.ceil(dur / SPLIT_TARGET))
    bounds = [start]
    for k in range(1, parts):
        target = start + dur * k / parts
        window = [f for f in frames if abs(f["t"] - target) <= 1.2 and start + MIN_SCENE < f["t"] < end - MIN_SCENE]
        if window:
            calm = min(window, key=lambda f: f["resid"] + 0.5 * abs(f["dx"]) + 0.5 * abs(f["dy"]))
            bounds.append(round(calm["t"], 2))
        else:
            bounds.append(round(target, 2))
    bounds.append(end)
    bounds = sorted(set(bounds))
    out = []
    for i in range(len(bounds) - 1):
        out.append((bounds[i], bounds[i + 1], i > 0))
    return out


def measure_scene(frames_in):
    """Camera movement and motion level from the optical flow of the scene's sampled frames (no AI)."""
    import numpy as np
    fr = frames_in[1:] if len(frames_in) > 1 else frames_in
    dur = max(0.1, len(fr) / ANALYSIS_FPS)
    dx = np.array([f["dx"] for f in fr])
    dy = np.array([f["dy"] for f in fr])
    zm = np.array([f["zoom"] for f in fr])
    rs = np.array([f["resid"] for f in fr])
    pan_speed = float(-dx.sum() / dur)  # content moving left = camera turning right
    tilt_speed = float(-dy.sum() / dur)
    zoom_rate = float(zm.sum() * ANALYSIS_FPS / max(1, len(fr)))
    jitter = float(np.sqrt(np.var(dx) + np.var(dy))) if len(fr) > 2 else 0.0
    steady_x = abs(dx.mean()) / (np.abs(dx).mean() + 1e-6)
    steady_y = abs(dy.mean()) / (np.abs(dy).mean() + 1e-6)
    moves = []
    if abs(pan_speed) > 0.035 and steady_x > 0.5:
        moves.append("pan right" if pan_speed > 0 else "pan left")
    if abs(tilt_speed) > 0.035 and steady_y > 0.5:
        moves.append("tilt down" if tilt_speed > 0 else "tilt up")
    if zoom_rate > 0.006:
        moves.append("push-in (zoom in / dolly in)")
    elif zoom_rate < -0.006:
        moves.append("pull-out (zoom out / dolly out)")
    handheld = jitter > 0.004 and not moves
    if handheld:
        moves.append("handheld (small natural shake)")
    if not moves:
        moves.append("static (locked-off)")
    subject = float(rs.mean() * ANALYSIS_FPS)  # share of frame width per second
    level = "high" if subject > 0.06 else "medium" if subject > 0.02 else "low"
    # confidence of the camera reading: clear when the global motion is well above the noise floor or tiny
    strength = max(abs(pan_speed), abs(tilt_speed), abs(zoom_rate) * 6)
    conf = 0.9 if strength > 0.06 or strength < 0.015 else 0.6
    return {
        "camera_movement": moves, "camera_confidence": conf,
        "pan_speed": round(pan_speed, 4), "tilt_speed": round(tilt_speed, 4), "zoom_rate": round(zoom_rate, 4),
        "shake": round(jitter, 5), "subject_motion": round(subject, 4), "motion_level": level,
        "brightness": round(float(np.mean([f["luma"] for f in frames_in])) / 255.0, 3),
        "saturation": round(float(np.mean([f["sat"] for f in frames_in])) / 255.0, 3),
    }


def pick_keyframes(start, end, frames_in, max_frames):
    """Start, end, equal steps of accumulated change (action-based) and motion peaks."""
    import numpy as np
    dur = end - start
    fr = [f for f in frames_in if start <= f["t"] < end] or frames_in
    level = np.array([f["resid"] + 0.6 * f["pix"] + 0.3 * (abs(f["dx"]) + abs(f["dy"])) for f in fr])
    n = int(max(3, min(max_frames, 2 + round(dur / 1.6) + (1 if level.mean() > 0.01 else 0))))
    times = [(start + min(0.15, dur / 4), "start"), (end - min(0.2, dur / 4), "end")]
    cum = np.cumsum(level) if level.sum() > 0 else np.arange(1, len(fr) + 1, dtype=np.float32)
    cum = cum / cum[-1]
    for k in range(1, n - 1):
        idx = int(np.searchsorted(cum, k / (n - 1)))
        idx = min(idx, len(fr) - 1)
        times.append((fr[idx]["t"], "change"))
    # motion peaks (fast actions) not already covered
    if len(fr) > 4:
        peaks = [i for i in range(1, len(fr) - 1) if level[i] >= level[i - 1] and level[i] >= level[i + 1] and level[i] > level.mean() * 1.8]
        for i in sorted(peaks, key=lambda i: -level[i])[:2]:
            times.append((fr[i]["t"], "peak"))
    times.sort()
    chosen = []
    for t, why in times:
        t = min(max(start, t), max(start, end - 0.05))
        if all(abs(t - c[0]) >= 0.35 for c in chosen):
            chosen.append((round(t, 2), why))
    if len(chosen) > max_frames:  # keep start, end and the evenly spread rest
        keep = [chosen[0], chosen[-1]]
        mid = chosen[1:-1]
        step = len(mid) / (max_frames - 2)
        keep += [mid[int(i * step)] for i in range(max_frames - 2)]
        chosen = sorted(set(keep))
    return chosen


def extract_frame(ffmpeg, path, t, out, long_side):
    vf = f"scale='if(gt(iw,ih),{long_side},-2)':'if(gt(iw,ih),-2,{long_side})'"
    r = subprocess.run([ffmpeg, "-hide_banner", "-v", "error", "-y", "-ss", f"{t:.3f}", "-i", path, "-frames:v", "1",
                        "-vf", vf, "-q:v", "3", out], capture_output=True,
                       creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
    if r.returncode != 0 or not os.path.isfile(out):
        if t > 0.2:
            return extract_frame(ffmpeg, path, max(0.0, t - 0.2), out, long_side)
        raise RuntimeError("Không cắt được khung hình: " + r.stderr.decode("utf-8", "replace")[-300:])


def color_facts(images):
    """Dominant colours (k-means in Lab), contrast, warm / cool — measured on the keyframes."""
    import cv2
    import numpy as np
    px = []
    for p in images:
        img = cv2.imread(p)
        if img is None:
            continue
        small = cv2.resize(img, (64, max(2, int(64 * img.shape[0] / img.shape[1]))), interpolation=cv2.INTER_AREA)
        px.append(small.reshape(-1, 3))
    if not px:
        return {}
    data = np.concatenate(px).astype(np.float32)
    lab = cv2.cvtColor(data.reshape(-1, 1, 3).astype(np.uint8), cv2.COLOR_BGR2LAB).reshape(-1, 3).astype(np.float32)
    k = 5
    crit = (cv2.TERM_CRITERIA_EPS + cv2.TERM_CRITERIA_MAX_ITER, 20, 1.0)
    _, labels, centers = cv2.kmeans(lab, k, None, crit, 3, cv2.KMEANS_PP_CENTERS)
    counts = np.bincount(labels.ravel(), minlength=k)
    order = np.argsort(-counts)
    cols = []
    for i in order:
        bgr = cv2.cvtColor(centers[i].reshape(1, 1, 3).astype(np.uint8), cv2.COLOR_LAB2BGR)[0, 0]
        cols.append({"hex": "#%02x%02x%02x" % (int(bgr[2]), int(bgr[1]), int(bgr[0])), "share": round(float(counts[i] / counts.sum()), 3)})
    L, A, B = lab[:, 0] * 100 / 255, lab[:, 1] - 128, lab[:, 2] - 128
    warmth = float(B.mean() + 0.5 * A.mean())
    contrast = float(L.std())
    return {
        "dominant_colors": cols,
        "tone": "warm" if warmth > 6 else "cool" if warmth < -4 else "neutral",
        "contrast": "high" if contrast > 24 else "low" if contrast < 13 else "medium",
        "contrast_value": round(contrast, 1), "warmth_value": round(warmth, 1),
    }


def sharpness(path):
    import cv2
    img = cv2.imread(path, cv2.IMREAD_GRAYSCALE)
    return 0.0 if img is None else float(cv2.Laplacian(img, cv2.CV_64F).var())


# ───────────────────────────── transcript ─────────────────────────────

HALLUCINATIONS = ("subscribe", "đăng ký kênh", "đăng kí kênh", "ghiền mì gõ", "like và share", "like, share",
                  "cảm ơn các bạn đã xem", "cảm ơn các bạn đã theo dõi", "hẹn gặp lại các bạn", "thanks for watching",
                  "thank you for watching", "subtitles by", "amara.org", "la la school", "nhấn chuông")


def hallucination(text):
    """Whisper writes these on music / silence (they are in its training captions, not in the video)."""
    t = text.lower()
    return any(h in t for h in HALLUCINATIONS)


def transcribe(a, job, work):
    exe, model = whisper_files(job["home"])
    if not (job.get("hasAudio") and job.get("transcribe", True) and os.path.isfile(exe) and os.path.isfile(model)):
        return None
    wav = os.path.join(work, "audio16k.wav")
    r = subprocess.run([job["ffmpeg"], "-hide_banner", "-v", "error", "-y", "-i", job["input"], "-vn", "-ac", "1", "-ar",
                        "16000", "-c:a", "pcm_s16le", wav], capture_output=True,
                       creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
    if r.returncode != 0:
        return None
    base = os.path.join(work, "transcript")
    lang = job.get("language") or "auto"
    # -ml 1 -sow: one segment per word with its own time (split only before a space, so a Vietnamese syllable's bytes
    # stay together); the words are grouped into sentences below. Recognition itself is the same.
    r = subprocess.run([exe, "-m", model, "-f", wav, "-l", lang, "-t", str(job.get("threads", 8)), "-ml", "1", "-sow",
                        "-oj", "-of", base, "-np"], capture_output=True, creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
    try:
        with open(base + ".json", encoding="utf-8", errors="replace") as f:
            data = json.load(f)
    except (OSError, ValueError):
        return None
    words = []
    for w in data.get("transcription", []):
        off = w.get("offsets", {})
        text = (w.get("text") or "").strip()
        if text and not text.startswith("[_"):
            words.append((off.get("from", 0) / 1000.0, off.get("to", 0) / 1000.0, text))
    segs = [l for l in sentences(words) if not l["text"].startswith("[") and not hallucination(l["text"])]
    lang = (data.get("result") or {}).get("language")
    out = {"language": lang, "model": os.path.basename(model), "segments": segs}
    try:
        out["voice"] = voice_profile(wav, segs)
    except Exception as e:  # noqa: BLE001 — the profile only tunes the narrator's style
        out["voice"] = {"error": str(e)[:200]}
    return out


def sentences(words, max_seconds=10.0, pause=0.7):
    """
    Word timings → lines: a line ends after . ? ! …, at a pause of 0.7 s or more, or (past 6 s) after a comma; a line
    never runs past 10 s. Each line keeps its words with their times, so the narration can be split between scenes
    exactly where the speaker said each word.
    """
    lines, cur = [], []

    def close():
        if cur:
            lines.append({"start": round(cur[0][0], 2), "end": round(cur[-1][1], 2), "text": " ".join(w[2] for w in cur),
                          "words": [[round(a, 2), round(b, 2), t] for a, b, t in cur]})
            cur.clear()

    for a, b, t in words:
        if cur and (a - cur[-1][1] >= pause or b - cur[0][0] > max_seconds):
            close()
        cur.append((a, b, t))
        long_ = cur[-1][1] - cur[0][0] > 6
        if t[-1] in ".?!…" or (long_ and t[-1] in ",;:"):
            close()
    close()
    return lines


def voice_profile(wav_path, segs):
    """
    How the speaker sounds, measured on the speech parts (no AI): median pitch (autocorrelation, 70–400 Hz), how fast
    they speak (Vietnamese words = syllables per second of speech) and how much their loudness moves (expressive vs
    even). Background music makes the pitch less reliable — it is a hint for choosing the narrator voice, not a fact.
    """
    import numpy as np
    with wave.open(wav_path, "rb") as w:
        rate = w.getframerate()
        x = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float32) / 32768.0
    speech = [(s["start"], s["end"]) for s in segs if s["end"] > s["start"]]
    words = sum(len(s["text"].split()) for s in segs)
    speech_sec = sum(b - a for a, b in speech)
    if not speech or speech_sec < 1:
        return {}
    win, hop = int(0.04 * rate), int(0.01 * rate)
    frames = []
    for a, b in speech:
        i0, i1 = int(a * rate), min(len(x), int(b * rate))
        for i in range(i0, i1 - win, hop):
            frames.append(x[i:i + win])
            if len(frames) >= 9000:  # ~90 s of speech is plenty
                break
    if not frames:
        return {}
    f = np.stack(frames)
    f = f - f.mean(axis=1, keepdims=True)
    rms = np.sqrt((f ** 2).mean(axis=1)) + 1e-9
    db = 20 * np.log10(rms)
    loud = db > (np.percentile(db, 90) - 25)
    f = f * np.hanning(win)
    spec = np.fft.rfft(f, n=2 * win, axis=1)
    ac = np.fft.irfft(np.abs(spec) ** 2, axis=1)[:, :win]
    lo, hi = int(rate / 400), int(rate / 70)
    lags = ac[:, lo:hi]
    best = lags.argmax(axis=1)
    peak = lags[np.arange(len(lags)), best] / (ac[:, 0] + 1e-9)
    voiced = loud & (peak > 0.45)
    out = {"syllables_per_second": round(words / speech_sec, 2), "speech_seconds": round(speech_sec, 1)}
    if voiced.sum() >= 20:
        f0 = rate / (best[voiced] + lo)
        out["pitch_hz"] = round(float(np.median(f0)), 1)
        out["pitch_spread_hz"] = round(float(np.percentile(f0, 90) - np.percentile(f0, 10)), 1)
        out["voiced_share"] = round(float(voiced.mean()), 2)
    out["loudness_std_db"] = round(float(db[loud].std()), 1) if loud.sum() >= 20 else None
    return out


TRANSLATE_SCHEMA = {
    "type": "object", "additionalProperties": False, "required": ["lines"],
    "properties": {"lines": {"type": "array", "items": {
        "type": "object", "additionalProperties": False, "required": ["i", "en"],
        "properties": {"i": {"type": "integer"}, "en": {"type": "string"}}}}},
}


def translate_pass(port, transcript):
    """English meaning of every spoken line (local model, text only) — it goes into the Seedance prompts as context."""
    segs = transcript.get("segments") or []
    if not segs:
        return
    numbered = "\n".join(f"{i}: {s['text']}" for i, s in enumerate(segs))
    content = [{"type": "text", "text": (
        "Below are numbered lines of narration / speech recognised in a video (automatic speech recognition, may contain "
        "small spelling mistakes). Translate EACH line into natural, simple English, keeping its meaning; fix obvious "
        "recognition mistakes from context. Do not add anything. Return one item per line with its number i.\n\n" + numbered)}]
    schema = json.loads(json.dumps(TRANSLATE_SCHEMA))
    schema["properties"]["lines"]["minItems"] = schema["properties"]["lines"]["maxItems"] = len(segs)  # an empty list is valid JSON too
    schema["properties"]["lines"]["items"]["properties"]["i"] = {"type": "integer", "minimum": 0, "maximum": len(segs) - 1}
    data, _ = chat(port, content, schema, min(3000, 120 + 60 * len(segs)), temperature=0.1)
    for item in data.get("lines", []):
        i = item.get("i")
        if isinstance(i, int) and 0 <= i < len(segs) and (item.get("en") or "").strip():
            segs[i]["en"] = item["en"].strip()[:400]


# ───────────────────────────── the scene pass ─────────────────────────────

def intervals(scene):
    """
    Timeline segments = the gaps between consecutive keyframes (Frame i → Frame j), the first from the scene start and
    the last to its end; gaps under 0.8 s merge with the next. Returns [(start, end, first_frame, last_frame)] (1-based).
    """
    ts = [fr["t"] for fr in scene["frames"]]
    n = len(ts)
    if n < 2:
        return [(scene["start"], scene["end"], 1, max(1, n))]
    bounds = [(scene["start"] if i == 0 else ts[i], scene["end"] if i == n - 2 else ts[i + 1], i + 1, i + 2) for i in range(n - 1)]
    segs = []
    for a, b, f0, f1 in bounds:
        if segs and segs[-1][1] - segs[-1][0] < 0.8:
            pa, _, pf0, _ = segs.pop()
            segs.append((pa, b, pf0, f1))
        else:
            segs.append((a, b, f0, f1))
    if len(segs) > 1 and segs[-1][1] - segs[-1][0] < 0.8:
        a, b, f0, f1 = segs.pop()
        pa, _, pf0, _ = segs.pop()
        segs.append((pa, b, pf0, f1))
    return [(round(a, 2), round(b, 2), f0, f1) for a, b, f0, f1 in segs]


def scene_schema(n):
    sch = json.loads(json.dumps(SCENE_SCHEMA))
    sch["properties"]["timeline"]["minItems"] = n
    sch["properties"]["timeline"]["maxItems"] = n
    sch["properties"]["timeline"]["items"]["properties"]["segment"] = {"type": "integer", "minimum": 1, "maximum": n}
    return sch


def facts_text(scene, transcript_lines):
    m = scene["measured"]
    c = scene.get("colors") or {}
    lines = [
        f"Scene {scene['index']}: {scene['start']:.1f}s to {scene['end']:.1f}s ({scene['end'] - scene['start']:.1f}s)"
        + (" — continues the previous scene without a cut (same shot)." if scene.get("continuation") else "."),
        "Measured by optical flow (trust these over guesses): camera movement = " + ", ".join(m["camera_movement"])
        + f"; subject motion level = {m['motion_level']}; brightness = {m['brightness']:.2f} (0 dark – 1 bright)"
        + (f"; colour tone = {c.get('tone')}, contrast = {c.get('contrast')}" if c else "") + ".",
    ]
    if m["brightness"] < 0.22:
        lines.append("The scene is dark / low light: describe only what is clearly visible.")
    if transcript_lines:
        lines.append("Speech heard in this scene (context only): " + " / ".join(transcript_lines)[:500])
    return "\n".join(lines)


def scene_content(scene, frames, transcript_lines):
    content = [{"type": "text", "text": facts_text(scene, transcript_lines) +
                "\nKeyframes of this scene in time order (absolute video time):"}]
    for k, fr in enumerate(frames, 1):
        with open(fr["path"], "rb") as f:
            b64 = base64.b64encode(f.read()).decode("ascii")
        content.append({"type": "text", "text": f"Frame {k} at {fr['t']:.2f}s:"})
        content.append({"type": "image_url", "image_url": {"url": f"data:image/jpeg;base64,{b64}"}})
    segs = intervals(scene)
    seg_text = "\n".join(f"Segment {i}: {a:.2f}s to {b:.2f}s = compare Frame {f0} with Frame {f1}"
                         for i, (a, b, f0, f1) in enumerate(segs, 1))
    content.append({"type": "text", "text": (
        "Analyse the scene. Write every field in ENGLISH except summary_vi.\n"
        f"timeline: exactly {len(segs)} items, one per segment below, in order (segment = its number). For each segment "
        "look closely at its two frames and describe what CHANGED from the first to the second (positions of hands, "
        "objects, body, head): the action, then the precise body movement (head, "
        "arms, hands, torso, hips, legs, feet, weight shifts, direction, speed). Do not repeat the same sentence for "
        "different segments; when nothing changes say so; confidence 'low' when the frames do not show it clearly. "
        "When no person is visible, describe what moves instead (camera, plants, water, light, objects) and write "
        "'no person' in body.\n"
        + seg_text + "\n"
        "Characters (needed to recreate the same-looking person, never a name or identity): label = boy / girl / man / "
        "woman…; gender as it appears; age_group (e.g. 'teenager', 'about 30 years old', 'elderly'); apparent_ethnicity = the visible "
        "regional appearance (e.g. 'East Asian', 'Southeast Asian / Vietnamese-looking', 'South Asian', 'Black', 'White', "
        "'Latino'; 'uncertain' if the face is not visible); skin_tone; build; hair = length, colour, style; clothing = EVERY "
        "garment with exact colour, material, pattern and fit (e.g. 'mustard-yellow linen shirt with rolled sleeves, faded light-blue "
        "slim jeans with a brown leather belt'); footwear ('barefoot' if so); accessories.\n"
        "Other rules: separate camera movement from subject movement; summary = one English sentence; summary_vi = one natural "
        "Vietnamese sentence; confidence = 0–1 for the whole description. Keep every field short (≤ 15 words).")})
    return content, segs


def analyze_scene_vlm(port, scene, frames, transcript_lines):
    content, segs = scene_content(scene, frames, transcript_lines)
    last = None
    for attempt in range(2):
        try:
            data, timings = chat(port, content, scene_schema(len(segs)), 2600, temperature=0.1 if attempt == 0 else 0.0)
            items = data.get("timeline") or []
            tl = []
            for i, (a, b, _, _) in enumerate(segs):  # the times are ours; the model only fills each segment
                it = items[i] if i < len(items) else {"action": "uncertain", "body": "uncertain", "confidence": "low"}
                tl.append({"start": a, "end": b, "action": it.get("action", ""), "body": it.get("body", ""),
                           "confidence": it.get("confidence", "medium")})
            data["timeline"] = tl
            return data, timings
        except (urllib.error.URLError, ValueError, KeyError, TimeoutError) as e:
            last = e
    raise RuntimeError(f"Model không trả được mô tả hợp lệ cho cảnh {scene['index']}: {last}")


def scene_confidence(scene):
    vlm = scene.get("analysis") or {}
    c = vlm.get("confidence")
    base = float(c) if isinstance(c, (int, float)) and 0 <= c <= 1 else 0.6
    tl = vlm.get("timeline") or []
    low = sum(1 for t in tl if t.get("confidence") == "low")
    share_ok = 1.0 - (low / len(tl) if tl else 0.5)
    # a timeline that says the same thing for every segment has not really seen the action
    actions = [(t.get("action") or "").strip().lower() for t in tl]
    unique = len(set(actions)) / len(actions) if actions else 0.5
    share_ok *= 0.5 + 0.5 * unique
    m = scene["measured"]
    light = 0.85 if m["brightness"] < 0.22 else 1.0
    coverage = min(1.0, len(scene["frames"]) / max(3.0, (scene["end"] - scene["start"]) / 1.8))
    return round(max(0.05, min(1.0, (0.55 * base + 0.3 * share_ok + 0.15 * coverage) * light)), 2)


def bibles_pass(port, scenes, overview_hint):
    compact = []
    for s in scenes:
        a = s.get("analysis") or {}
        compact.append({
            "scene": s["index"], "time": f"{s['start']:.1f}-{s['end']:.1f}s", "summary": a.get("summary"),
            "characters": a.get("characters"), "environment": a.get("environment"), "lighting": a.get("lighting"),
            "color": a.get("color"), "camera": {**(a.get("camera") or {}), "measured": s["measured"]["camera_movement"]},
            "dof": a.get("depth_of_field"), "motion": a.get("motion_quality"), "realism": (a.get("realism") or {}).get("overall"),
        })
    content = [{"type": "text", "text": (
        "Below are the per-scene analyses of ONE video. Merge them into production bibles used to recreate the video with "
        "a text-to-video model while keeping every scene consistent.\n"
        "- characters: one entry per distinct person (match the same person across scenes by clothing / hair / build); "
        "keep gender, age_group, apparent_ethnicity, skin_tone, hair and the full garment-by-garment clothing with colours; "
        "id = C1, C2…; scenes = scene numbers where they appear; consistency = what must stay identical between shots.\n"
        "- environments: one entry per distinct place; id = E1, E2…; scenes = scene numbers.\n"
        "- style: the overall look, camera language and motion style.\n"
        "- overview.summary_vi: 1–2 natural Vietnamese sentences. Use only facts present in the analyses.\n\n"
        + json.dumps(compact, ensure_ascii=False))}]
    last = None
    for attempt in range(2):  # a cut-off answer is retried once, colder
        try:
            return chat(port, content, BIBLE_SCHEMA, 3000, temperature=0.1 if attempt == 0 else 0.0)
        except (urllib.error.URLError, ValueError, KeyError, TimeoutError) as e:
            last = e
    raise RuntimeError(f"bible merge failed: {last}")


def fallback_bibles(scenes):
    """Used only when the model fails the merge: one character / place per distinct description."""
    chars, envs = [], []
    for s in scenes:
        a = s.get("analysis") or {}
        for c in a.get("characters") or []:
            key = (c.get("clothing") or "").lower()[:40]
            match = next((x for x in chars if x["_k"] == key), None)
            if match:
                match["scenes"].append(s["index"])
            else:
                chars.append({"_k": key, "id": f"C{len(chars) + 1}", "label": c.get("label", "person"),
                              "gender": c.get("gender", ""), "age_group": c.get("age_group", ""),
                              "apparent_ethnicity": c.get("apparent_ethnicity", ""), "skin_tone": c.get("skin_tone", ""),
                              "build": c.get("build", ""), "footwear": c.get("footwear", ""), "appearance": c.get("appearance", ""),
                              "hair": c.get("hair", ""), "clothing": c.get("clothing", ""), "accessories": c.get("accessories", ""),
                              "posture": c.get("posture", ""), "distinguishing": "", "consistency": "same face, hair and clothing in every shot",
                              "scenes": [s["index"]]})
        e = a.get("environment") or {}
        key = (e.get("location") or "").lower()[:40]
        match = next((x for x in envs if x["_k"] == key), None)
        if match:
            match["scenes"].append(s["index"])
        else:
            envs.append({"_k": key, "id": f"E{len(envs) + 1}", "location": e.get("location", ""), "layout": e.get("space", ""),
                         "background": e.get("background", ""), "important_objects": ", ".join(e.get("objects") or []),
                         "lighting": (a.get("lighting") or {}).get("source", ""), "weather": e.get("weather", ""),
                         "time_of_day": e.get("time_of_day", ""), "scenes": [s["index"]]})
    for x in chars + envs:
        x.pop("_k", None)
    first = (scenes[0].get("analysis") or {}) if scenes else {}
    return {
        "overview": {"summary": " ".join(filter(None, [(s.get("analysis") or {}).get("summary") for s in scenes]))[:600],
                     "summary_vi": " ".join(filter(None, [(s.get("analysis") or {}).get("summary_vi") for s in scenes]))[:600],
                     "scene_relations": "uncertain"},
        "characters": chars, "environments": envs,
        "style": {"visual_style": (first.get("realism") or {}).get("overall", ""), "realism_level": "photorealistic",
                  "color_palette": (first.get("color") or {}).get("dominant", ""), "lighting_style": (first.get("lighting") or {}).get("source", ""),
                  "camera_language": "", "motion_style": (first.get("motion_quality") or {}).get("naturalness", ""),
                  "depth_of_field": (first.get("depth_of_field") or {}).get("background_blur", ""), "direction": ""},
    }


def file_key(path, extra):
    h = hashlib.sha256()
    h.update(str(os.path.getsize(path)).encode())
    with open(path, "rb") as f:
        h.update(f.read(8 * 1024 * 1024))
    h.update(json.dumps(extra, sort_keys=True).encode())
    return h.hexdigest()[:24]


def cmd_analyze(a):
    with open(a.job, encoding="utf-8") as f:
        job = json.load(f)
    work = job["work"]
    os.makedirs(work, exist_ok=True)
    frames_dir = os.path.join(work, "frames")
    os.makedirs(frames_dir, exist_ok=True)
    src = job["input"]
    duration = float(job["duration"])
    model = job.get("model", "4b")
    if model not in MODELS:
        model = "4b"
    started = time.time()

    # 1–2. decode + cuts
    progress("decode", "Đang đọc video, đo chuyển động và màu…", 0, 1, 2)
    frames = decode_pass(job["ffmpeg"], src, duration, int(job["width"]), int(job["height"]),
                         lambda d, t: progress("decode", "Đang đọc video, đo chuyển động và màu…", d, t, 2 + 13 * d / max(1, t)))
    duration = max(duration, frames[-1]["t"] + 1.0 / ANALYSIS_FPS) if frames else duration
    scores = content_scores(frames)
    cuts = detect_cuts(frames, scores, duration)
    bounds = [0.0] + cuts + [round(duration, 3)]
    raw = []
    for i in range(len(bounds) - 1):
        raw.extend(split_long(bounds[i], bounds[i + 1], frames))
    scenes = []
    for (s, e, cont) in raw:
        if scenes and e - s < MIN_SCENE:
            scenes[-1]["end"] = e  # a flash too short to be its own clip
            continue
        scenes.append({"index": len(scenes) + 1, "start": round(s, 2), "end": round(e, 2), "continuation": cont})
    progress("scenes", f"Tìm được {len(scenes)} cảnh.", len(scenes), len(scenes), 16)

    # 3–4. measurements + keyframes
    max_frames = int(job.get("maxFramesPerScene", 8))
    for n, sc in enumerate(scenes, 1):
        inside = [f for f in frames if sc["start"] <= f["t"] < sc["end"]] or [min(frames, key=lambda f: abs(f["t"] - sc["start"]))]
        sc["measured"] = measure_scene(inside)
        chosen = pick_keyframes(sc["start"], sc["end"], inside, max_frames)
        sc["frames"] = []
        for k, (t, why) in enumerate(chosen):
            p = os.path.join(frames_dir, f"s{sc['index']:02d}_{k:02d}.jpg")
            extract_frame(job["ffmpeg"], src, t, p, 768)
            sc["frames"].append({"t": t, "reason": why, "path": p})
        sc["colors"] = color_facts([fr["path"] for fr in sc["frames"]])
        mid = (sc["start"] + sc["end"]) / 2
        best = max(sc["frames"], key=lambda fr: sharpness(fr["path"]) / (1 + abs(fr["t"] - mid)))
        sc["thumb"] = os.path.join(frames_dir, f"s{sc['index']:02d}_thumb.jpg")
        extract_frame(job["ffmpeg"], src, best["t"], sc["thumb"], 480)
        progress("keyframes", f"Chọn khung hình chính cảnh {n}/{len(scenes)}…", n, len(scenes), 16 + 12 * n / len(scenes))

    # 5. transcript
    transcript = None
    if job.get("hasAudio") and job.get("transcribe", True):
        progress("transcript", "Đang nghe lời nói trong video (whisper)…", 0, 1, 29)
        try:
            transcript = transcribe(a, job, work)
        except Exception as e:  # noqa: BLE001 — optional step
            transcript = {"error": str(e)[:300], "segments": []}

    def lines_for(sc):
        if not transcript:
            return []
        return [s["text"] for s in transcript.get("segments", []) if s["end"] > sc["start"] and s["start"] < sc["end"]]

    # 6. vision-language model
    cache = os.path.join(work, "cache", file_key(src, {"model": model, "v": PROMPT_VERSION}))
    os.makedirs(cache, exist_ok=True)
    progress("model", f"Đang nạp model AI {model.upper()} trên máy…", 0, len(scenes), 31)
    port, backend = start_server(job["home"], model, int(job.get("threads", 8)), job.get("gpu", "auto"), work)
    timings = []
    t_scenes = time.time()
    for n, sc in enumerate(scenes, 1):
        key = os.path.join(cache, f"scene_{sc['start']:.2f}_{sc['end']:.2f}_{len(sc['frames'])}.json")
        if os.path.isfile(key):
            with open(key, encoding="utf-8") as f:
                sc["analysis"] = json.load(f)
        else:
            avg = (time.time() - t_scenes) / max(1, n - 1) if n > 1 else None
            eta = f" — còn khoảng {int(avg * (len(scenes) - n + 1) // 60)} phút" if avg else ""
            progress("vision", f"AI đang xem cảnh {n}/{len(scenes)}{eta}…", n - 1, len(scenes), 32 + 58 * (n - 1) / len(scenes))
            data, tm = analyze_scene_vlm(port, sc, sc["frames"], lines_for(sc))
            sc["analysis"] = data
            timings.append(tm)
            with open(key, "w", encoding="utf-8") as f:
                json.dump(data, f, ensure_ascii=False)
        sc["confidence"] = scene_confidence(sc)
        sc["transcript"] = " ".join(lines_for(sc))[:1000] or None
        progress("vision", f"AI đã xem xong cảnh {n}/{len(scenes)}.", n, len(scenes), 32 + 58 * n / len(scenes))

    progress("bibles", "Đang tổng hợp nhân vật, bối cảnh và phong cách…", 0, 1, 92)
    bible_key = os.path.join(cache, f"bibles_{len(scenes)}.json")
    bibles_source = "model"
    if os.path.isfile(bible_key):
        with open(bible_key, encoding="utf-8") as f:
            bibles = json.load(f)
    else:
        try:
            bibles, tm = bibles_pass(port, scenes, None)
            timings.append(tm)
            with open(bible_key, "w", encoding="utf-8") as f:
                json.dump(bibles, f, ensure_ascii=False)
        except Exception:  # noqa: BLE001
            bibles = fallback_bibles(scenes)
            bibles_source = "merged"
    if transcript and transcript.get("segments"):
        progress("bibles", "Đang dịch lời bình sang tiếng Anh cho prompt…", 0, 1, 96)
        try:
            translate_pass(port, transcript)
        except Exception as e:  # noqa: BLE001 — the prompts then carry the original words
            transcript["translateError"] = str(e)[:200]
    stop_server()

    palette = {}
    for sc in scenes:
        for c in (sc.get("colors") or {}).get("dominant_colors", [])[:3]:
            palette[c["hex"]] = palette.get(c["hex"], 0) + c["share"] * (sc["end"] - sc["start"])
    measured_palette = [h for h, _ in sorted(palette.items(), key=lambda kv: -kv[1])[:6]]

    result = {
        "engine": {"model": model, "backend": backend, "promptVersion": PROMPT_VERSION, "bibles": bibles_source,
                   "seconds": round(time.time() - started, 1),
                   "genTokensPerSecond": round(sum(t.get("predicted_per_second", 0) for t in timings) / max(1, len(timings)), 1)},
        "duration": round(duration, 3), "analysisFps": ANALYSIS_FPS,
        "scenes": [{k: v for k, v in sc.items()} for sc in scenes],
        "bibles": bibles, "measuredPalette": measured_palette, "transcript": transcript,
    }
    out = os.path.join(work, "result.json")
    with open(out, "w", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False)
    emit(type="done", result=out, scenes=len(scenes), seconds=result["engine"]["seconds"], backend=backend)


def cmd_bench(a):
    """Loads the model and times one tiny text answer (shown on the page as the machine's speed)."""
    work = os.path.join(a.home, "work", "bench")
    os.makedirs(work, exist_ok=True)
    t0 = time.time()
    port, backend = start_server(a.home, a.model, a.threads, a.gpu, work)
    load = time.time() - t0
    schema = {"type": "object", "properties": {"words": {"type": "array", "maxItems": 30, "items": {"type": "string"}}},
              "required": ["words"], "additionalProperties": False}
    _, tm = chat(port, [{"type": "text", "text": "List 30 different single English nouns about cinema."}], schema, 600)
    stop_server()
    emit(type="done", model=a.model, backend=backend, loadSeconds=round(load, 1),
         genTokensPerSecond=round(tm.get("predicted_per_second", 0), 1))


def main():
    p = argparse.ArgumentParser()
    sub = p.add_subparsers(dest="cmd", required=True)
    pr = sub.add_parser("probe")
    pr.add_argument("--home", required=True)
    b = sub.add_parser("bench")
    b.add_argument("--home", required=True)
    b.add_argument("--model", default="4b", choices=list(MODELS))
    b.add_argument("--threads", type=int, default=max(2, min(14, (os.cpu_count() or 4) - 2)))
    b.add_argument("--gpu", default="auto")
    an = sub.add_parser("analyze")
    an.add_argument("--job", required=True)
    a = p.parse_args()
    try:
        {"probe": cmd_probe, "bench": cmd_bench, "analyze": cmd_analyze}[a.cmd](a)
    except SystemExit:
        raise
    except Exception as e:  # noqa: BLE001
        stop_server()
        fail(str(e)[:1500])


if __name__ == "__main__":
    main()
