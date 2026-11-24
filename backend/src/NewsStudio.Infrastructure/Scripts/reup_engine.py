#!/usr/bin/env python3
"""Reup Video engine — runs on the owner's PC only (CPU, or the PC's own graphics card through DirectML).

Free, offline after setup: ONNX Runtime + OpenCV + FFmpeg. Frames stream through pipes (ffmpeg decode → numpy →
ffmpeg encode) one at a time, so a long video never fills RAM or the disk.

Commands (results / progress are JSON lines on stdout; anything else goes to stderr):
  probe      --home DIR                       what is installed (libraries, providers, models)
  bench      --home DIR [--provider P]        frames/s of background replacement at 720p and 1080p
  check-face --home DIR --image F [--thumb T] does the picture show exactly one clear face (character photos)
  render     --job job.json                   process a video (face swap and/or background) into an H.264 MP4 without audio

Models (licences checked; see scripts/pc/setup-reup-engine.ps1):
  YuNet face detector (MIT), SFace face recogniser (Apache-2.0), Robust Video Matting MobileNetV3 (GPL-3.0, internal use).
  Face swap needs models/licensed/swapper.onnx + identity.onnx that the owner holds a COMMERCIAL licence for; they are
  never downloaded by the tool.
"""
import argparse
import json
import os
import subprocess
import sys
import threading
import time
import queue

import numpy as np
import cv2

YUNET = "face_detection_yunet_2023mar.onnx"
SFACE = "face_recognition_sface_2021dec.onnx"
RVM = "rvm_mobilenetv3_fp32.onnx"
RVM_HQ = "rvm_resnet50_fp32.onnx"  # optional, "high" quality
SWAPPER = os.path.join("licensed", "swapper.onnx")
IDENTITY = os.path.join("licensed", "identity.onnx")
SFACE_MATCH = 0.363  # OpenCV's cosine threshold for "same person"

# ArcFace 5-point template (112×112): left eye, right eye (as seen in the image), nose, mouth corners — YuNet's order
ARCFACE_DST = np.array([[38.2946, 51.6963], [73.5318, 51.5014], [56.0252, 71.7366],
                        [41.5493, 92.3655], [70.7299, 92.2041]], dtype=np.float32)


def emit(**payload):
    sys.stdout.write(json.dumps(payload, ensure_ascii=False) + "\n")
    sys.stdout.flush()


def fail(message, code=1):
    emit(type="error", message=message)
    sys.exit(code)


def read_image(path):
    data = np.fromfile(path, dtype=np.uint8)  # works with Vietnamese / unicode paths on Windows
    img = cv2.imdecode(data, cv2.IMREAD_COLOR)
    if img is None:
        raise ValueError("Không đọc được ảnh (định dạng hỗ trợ: JPG, PNG, WEBP, BMP).")
    return img


def write_jpeg(path, img, quality=88):
    ok, buf = cv2.imencode(".jpg", img, [cv2.IMWRITE_JPEG_QUALITY, quality])
    if not ok:
        raise ValueError("Không lưu được ảnh JPEG.")
    buf.tofile(path)


# ───────────────────────── ONNX Runtime ─────────────────────────

def make_session(path, threads, provider):
    import onnxruntime as ort
    so = ort.SessionOptions()
    so.intra_op_num_threads = max(1, int(threads))
    so.inter_op_num_threads = 1
    so.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
    providers = ["CPUExecutionProvider"]
    if provider == "directml" and "DmlExecutionProvider" in ort.get_available_providers():
        so.enable_mem_pattern = False
        so.execution_mode = ort.ExecutionMode.ORT_SEQUENTIAL
        providers = ["DmlExecutionProvider", "CPUExecutionProvider"]
    return ort.InferenceSession(path, sess_options=so, providers=providers)


def default_threads():
    # measured on a 2×14-core Xeon: ONNX Runtime gains nothing past ~14 threads (memory bound, two sockets)
    return max(1, min(14, (os.cpu_count() or 4) - 2))


# ───────────────────────── geometry helpers ─────────────────────────

def umeyama(src, dst):
    """Similarity transform (rotation + uniform scale + shift) mapping src points onto dst, least squares."""
    src = np.asarray(src, dtype=np.float64)
    dst = np.asarray(dst, dtype=np.float64)
    n = src.shape[0]
    src_mean, dst_mean = src.mean(0), dst.mean(0)
    src_d, dst_d = src - src_mean, dst - dst_mean
    a = dst_d.T @ src_d / n
    d = np.ones(2)
    if np.linalg.det(a) < 0:
        d[1] = -1
    u, s, vt = np.linalg.svd(a)
    r = u @ np.diag(d) @ vt
    scale = (s @ d) / src_d.var(0).sum()
    t = dst_mean - scale * (r @ src_mean)
    m = np.zeros((2, 3), dtype=np.float32)
    m[:, :2] = scale * r
    m[:, 2] = t
    return m


def align_matrix(kps, size):
    if size % 112 == 0:
        ratio, dx = size / 112.0, 0.0
    else:
        ratio = size / 128.0
        dx = 8.0 * ratio
    dst = ARCFACE_DST * ratio
    dst[:, 0] += dx
    return umeyama(kps, dst)


def iou(a, b):
    ax0, ay0, ax1, ay1 = a[0], a[1], a[0] + a[2], a[1] + a[3]
    bx0, by0, bx1, by1 = b[0], b[1], b[0] + b[2], b[1] + b[3]
    iw = max(0.0, min(ax1, bx1) - max(ax0, bx0))
    ih = max(0.0, min(ay1, by1) - max(ay0, by0))
    inter = iw * ih
    union = a[2] * a[3] + b[2] * b[3] - inter
    return inter / union if union > 0 else 0.0


def face_pose(kps):
    """(eye distance, yaw ratio): yaw ≈ how far the nose sits off the eye centre along the eye line (0 = frontal)."""
    le, re, nose = kps[0], kps[1], kps[2]
    axis = re - le
    dist = float(np.linalg.norm(axis))
    if dist < 1e-3:
        return 0.0, 1.0
    unit = axis / dist
    yaw = float(np.dot(nose - (le + re) / 2.0, unit) / dist)
    return dist, yaw


# ───────────────────────── face detection / recognition ─────────────────────────

class FaceFinder:
    def __init__(self, models_dir, det_size=640):
        self.det = cv2.FaceDetectorYN.create(os.path.join(models_dir, YUNET), "", (320, 320), 0.5, 0.3, 50)
        self.rec = cv2.FaceRecognizerSF.create(os.path.join(models_dir, SFACE), "")
        self.det_size = det_size

    def detect(self, img):
        """Faces as rows [x, y, w, h, 10 landmark coords, score] in img's pixel space (detected on a ≤640 px copy)."""
        h, w = img.shape[:2]
        scale = min(1.0, self.det_size / float(max(h, w)))
        small = img if scale == 1.0 else cv2.resize(img, (int(round(w * scale)), int(round(h * scale))), interpolation=cv2.INTER_AREA)
        self.det.setInputSize((small.shape[1], small.shape[0]))
        _, faces = self.det.detect(small)
        if faces is None:
            return []
        faces = faces.copy()
        faces[:, :14] /= scale
        return [f for f in faces]

    def feature(self, img, face):
        aligned = self.rec.alignCrop(img, face)
        return self.rec.feature(aligned)

    def same(self, f1, f2):
        return float(self.rec.match(f1, f2, cv2.FaceRecognizerSF_FR_COSINE))


def kps_of(face):
    return np.asarray(face[4:14], dtype=np.float32).reshape(5, 2)


# ───────────────────────── face swap (licensed model only) ─────────────────────────

class FaceSwapper:
    def __init__(self, models_dir, threads, provider):
        swapper_path = os.path.join(models_dir, SWAPPER)
        identity_path = os.path.join(models_dir, IDENTITY)
        if not (os.path.isfile(swapper_path) and os.path.isfile(identity_path)):
            raise RuntimeError("Chưa có model thay mặt có giấy phép thương mại (models/licensed/swapper.onnx + identity.onnx).")
        import onnx
        from onnx import numpy_helper
        graph = onnx.load(swapper_path).graph
        self.emap = numpy_helper.to_array(graph.initializer[-1]).astype(np.float32)
        if self.emap.shape != (512, 512):
            raise RuntimeError("swapper.onnx không phải loại inswapper (thiếu ma trận emap 512×512).")
        del graph
        self.swap = make_session(swapper_path, threads, provider)
        self.ident = make_session(identity_path, threads, provider)
        ins = self.swap.get_inputs()
        self.target_name = next(i.name for i in ins if len(i.shape) == 4)
        self.source_name = next(i.name for i in ins if len(i.shape) == 2)
        shape = next(i.shape for i in ins if len(i.shape) == 4)
        self.size = shape[2] if isinstance(shape[2], int) else 128
        self.ident_name = self.ident.get_inputs()[0].name
        m = np.zeros((self.size, self.size), np.float32)
        b = max(2, int(self.size * 0.1))
        m[b:-b, b:-b] = 1.0
        self.mask = cv2.GaussianBlur(m, (0, 0), self.size * 0.05)
        self.latent = None

    def set_source(self, img, kps):
        m = align_matrix(kps, 112)
        crop = cv2.warpAffine(img, m, (112, 112), borderValue=0.0)
        blob = cv2.dnn.blobFromImage(crop, 1.0 / 127.5, (112, 112), (127.5, 127.5, 127.5), swapRB=True)
        emb = self.ident.run(None, {self.ident_name: blob})[0].reshape(-1)
        emb = emb / (np.linalg.norm(emb) + 1e-8)
        latent = emb.reshape(1, -1) @ self.emap
        self.latent = (latent / (np.linalg.norm(latent) + 1e-8)).astype(np.float32)

    def apply(self, frame, kps, weight, color_match=0.5):
        size = self.size
        m = align_matrix(kps, size)
        crop = cv2.warpAffine(frame, m, (size, size), borderValue=0.0)
        blob = cv2.dnn.blobFromImage(crop, 1.0 / 255.0, (size, size), (0, 0, 0), swapRB=True)
        pred = self.swap.run(None, {self.target_name: blob, self.source_name: self.latent})[0]
        fake = np.clip(pred[0].transpose(1, 2, 0) * 255.0, 0, 255).astype(np.uint8)[:, :, ::-1]
        if color_match > 0:
            fake = match_color(fake, crop, self.mask > 0.5, color_match)
        h, w = frame.shape[:2]
        im = cv2.invertAffineTransform(m)
        corners = np.array([[0, 0, 1], [size, 0, 1], [0, size, 1], [size, size, 1]], np.float32) @ im.T
        x0, y0 = np.floor(corners.min(0)).astype(int)
        x1, y1 = np.ceil(corners.max(0)).astype(int)
        x0, y0, x1, y1 = max(0, x0), max(0, y0), min(w, x1), min(h, y1)
        if x1 - x0 < 4 or y1 - y0 < 4:
            return False
        im_roi = im.copy()
        im_roi[:, 2] -= (x0, y0)
        fake_roi = cv2.warpAffine(fake, im_roi, (x1 - x0, y1 - y0), flags=cv2.INTER_LINEAR, borderMode=cv2.BORDER_REPLICATE)
        mask_roi = cv2.warpAffine(self.mask, im_roi, (x1 - x0, y1 - y0), flags=cv2.INTER_LINEAR, borderValue=0.0)
        a = (mask_roi * float(weight))[:, :, None]
        roi = frame[y0:y1, x0:x1].astype(np.float32)
        frame[y0:y1, x0:x1] = np.clip(roi * (1.0 - a) + fake_roi.astype(np.float32) * a, 0, 255).astype(np.uint8)
        return True


def match_color(src, ref, region, strength):
    """Shift src's average colour (LAB) toward ref's inside region — keeps the original skin tone and lighting."""
    s = cv2.cvtColor(src, cv2.COLOR_BGR2LAB).astype(np.float32)
    r = cv2.cvtColor(ref, cv2.COLOR_BGR2LAB).astype(np.float32)
    if region.sum() < 16:
        return src
    shift = (r[region].mean(0) - s[region].mean(0)) * strength
    s += shift
    return cv2.cvtColor(np.clip(s, 0, 255).astype(np.uint8), cv2.COLOR_LAB2BGR)


class FaceTrack:
    """Follows the one target person through the video: lock, smooth, gate, fade. Faces that fail the checks
    (turned away, too small, unsure, back of the head…) keep the ORIGINAL frame instead of a broken swap."""

    def __init__(self, finder, swapper, opts):
        self.finder = finder
        self.swapper = swapper
        self.min_conf = float(opts.get("minConfidence", 0.6))
        self.max_yaw = float(opts.get("maxYaw", 0.38))
        self.min_eye = float(opts.get("minEyeDistance", 14))
        self.color = float(opts.get("colorMatch", 0.5))
        self.fade_step = 1.0 / max(1, int(opts.get("fadeFrames", 3)))
        self.ref = None
        self.last_box = None
        self.last_kps = None
        self.missing = 0
        self.weight = 0.0
        self.swapped = 0
        self.kept = 0
        self.no_face = 0

    def process(self, frame):
        faces = self.finder.detect(frame)
        target = None
        if self.last_box is not None and faces:
            best = max(faces, key=lambda f: iou(f[:4], self.last_box))
            if iou(best[:4], self.last_box) > 0.3:
                target = best
        if target is None and faces:
            if self.ref is None:
                big = [f for f in faces if f[14] >= 0.8]
                if big:
                    target = max(big, key=lambda f: f[2] * f[3])
                    self.ref = self.finder.feature(frame, target)
            else:
                scored = [(self.finder.same(self.ref, self.finder.feature(frame, f)), f) for f in faces if f[14] >= 0.5]
                scored = [x for x in scored if x[0] >= SFACE_MATCH]
                if scored:
                    target = max(scored, key=lambda x: x[0])[1]

        if target is None:
            self.missing += 1
            if self.missing > 3:
                self.last_box, self.last_kps = None, None
            self.weight = 0.0
            self.no_face += 1
            return
        self.missing = 0
        kps = kps_of(target)
        eye, yaw = face_pose(kps)
        if self.last_kps is not None and self.last_box is not None:
            motion = float(np.abs(kps - self.last_kps).mean()) / max(eye, 1.0)
            a = 0.55 if motion < 0.04 else 0.9  # steady → smooth jitter; fast move → follow at once
            kps = a * kps + (1.0 - a) * self.last_kps
        self.last_kps = kps
        self.last_box = np.asarray(target[:4], dtype=np.float32)
        ok = target[14] >= self.min_conf and eye >= self.min_eye and abs(yaw) <= self.max_yaw
        goal = 1.0 if ok else 0.0
        self.weight = min(goal, self.weight + self.fade_step) if goal > self.weight else max(goal, self.weight - self.fade_step)
        if self.weight > 0.0 and self.swapper.apply(frame, kps, self.weight, self.color):
            self.swapped += 1
        else:
            self.kept += 1


# ───────────────────────── background replacement ─────────────────────────

def cover_fit(img, w, h):
    ih, iw = img.shape[:2]
    s = max(w / iw, h / ih)
    nw, nh = max(w, int(round(iw * s))), max(h, int(round(ih * s)))
    r = cv2.resize(img, (nw, nh), interpolation=cv2.INTER_AREA if s < 1 else cv2.INTER_CUBIC)
    x, y = (nw - w) // 2, (nh - h) // 2
    return r[y:y + h, x:x + w].copy()


def soft_blur(img, sigma):
    """Gaussian blur done on a quarter-size copy (fast at 1080p)."""
    if sigma <= 0.5:
        return img
    h, w = img.shape[:2]
    small = cv2.resize(img, (max(1, w // 4), max(1, h // 4)), interpolation=cv2.INTER_AREA)
    small = cv2.GaussianBlur(small, (0, 0), sigma / 4.0)
    return cv2.resize(small, (w, h), interpolation=cv2.INTER_LINEAR)


class RawReader:
    """Reads fixed-size raw frames from a process's stdout on a thread (decoding runs ahead of processing)."""

    def __init__(self, proc, frame_bytes, depth=6):
        self.proc = proc
        self.n = frame_bytes
        self.q = queue.Queue(maxsize=depth)
        self.t = threading.Thread(target=self._run, daemon=True)
        self.t.start()

    def _run(self):
        try:
            while True:
                buf = self.proc.stdout.read(self.n)
                if not buf or len(buf) < self.n:
                    break
                self.q.put(buf)
        finally:
            self.q.put(None)

    def get(self):
        return self.q.get()


class StderrTail:
    def __init__(self, proc):
        self.lines = []
        self.t = threading.Thread(target=self._run, args=(proc,), daemon=True)
        self.t.start()

    def _run(self, proc):
        for raw in iter(proc.stderr.readline, b""):
            self.lines.append(raw.decode("utf-8", "replace").rstrip())
            if len(self.lines) > 30:
                self.lines.pop(0)

    def text(self):
        return " | ".join(self.lines[-6:])


def popen(args, stdin=None):
    flags = subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0
    return subprocess.Popen(args, stdin=stdin, stdout=subprocess.PIPE, stderr=subprocess.PIPE, creationflags=flags)


class Backdrop:
    """One backdrop (picture or video) at the output size; a video loops when shorter than its segment."""

    def __init__(self, seg, ffmpeg, w, h, fps, blur):
        self.kind = seg.get("kind", "image")
        self.w, self.h = w, h
        self.ken_burns = bool(seg.get("kenBurns", True)) and self.kind == "image"
        self.start, self.end = float(seg["start"]), float(seg["end"])
        self.proc = None
        if self.kind == "video":
            vf = f"fps={fps},scale={w}:{h}:force_original_aspect_ratio=increase:flags=bicubic,crop={w}:{h}"
            if blur > 0:
                vf += f",gblur=sigma={blur:.2f}"
            vf += ",format=bgr24"
            self.proc = popen([ffmpeg, "-v", "error", "-stream_loop", "-1", "-i", seg["path"], "-an", "-sn",
                               "-vf", vf, "-f", "rawvideo", "-pix_fmt", "bgr24", "-"])
            self.err = StderrTail(self.proc)
            self.reader = RawReader(self.proc, w * h * 3, depth=4)
            self.last = None
        else:
            img = cover_fit(read_image(seg["path"]), w, h)
            self.image = soft_blur(img, blur) if blur > 0 else img

    def frame(self, t):
        if self.kind == "video":
            buf = self.reader.get()
            if buf is None:
                if self.last is None:
                    raise RuntimeError("Không đọc được video nền: " + self.err.text())
                return self.last
            self.last = np.frombuffer(buf, np.uint8).reshape(self.h, self.w, 3)
            return self.last
        if not self.ken_burns:
            return self.image
        span = max(0.5, self.end - self.start)
        z = 1.0 + 0.06 * min(1.0, max(0.0, (t - self.start) / span))
        m = np.array([[z, 0, (1 - z) * self.w / 2], [0, z, (1 - z) * self.h / 2]], np.float32)
        return cv2.warpAffine(self.image, m, (self.w, self.h), flags=cv2.INTER_LINEAR, borderMode=cv2.BORDER_REFLECT)

    def close(self):
        if self.proc is not None:
            try:
                self.proc.kill()
            except Exception:
                pass


class BackdropTimeline:
    def __init__(self, segments, ffmpeg, w, h, fps, blur, crossfade):
        self.segs = sorted(segments, key=lambda s: float(s["start"]))
        if not self.segs:
            raise RuntimeError("Chưa chọn cảnh nền.")
        self.args = (ffmpeg, w, h, fps, blur)
        self.cf = max(0.0, float(crossfade))
        self.open = {}

    def _get(self, i):
        if i not in self.open:
            self.open[i] = Backdrop(self.segs[i], *self.args)
        return self.open[i]

    def frame(self, t):
        k = 0
        for i, s in enumerate(self.segs):
            if t >= float(s["start"]):
                k = i
        for i in [j for j in self.open if j < k]:
            self.open.pop(i).close()
        cur = self._get(k).frame(t)
        if k + 1 < len(self.segs) and self.cf > 0:
            end = float(self.segs[k + 1]["start"])
            if t >= end - self.cf:
                u = min(1.0, (t - (end - self.cf)) / self.cf)
                nxt = self._get(k + 1).frame(t)
                return cv2.addWeighted(cur, 1.0 - u, nxt, u, 0.0)
        return cur

    def close(self):
        for b in self.open.values():
            b.close()


class Matting:
    """Robust Video Matting: keeps a memory between frames, so hair and edges stay steady (no frame-by-frame flicker).
    quality "high" = the ResNet50 model at a finer internal resolution (cleaner hair, ~5x slower); "fast" = MobileNetV3.
    Runs at ≤1280 px (long side); for 1080p the mask is scaled up (colours come from the full-size frame anyway)."""

    MAX_SIDE = 1280

    def __init__(self, models_dir, threads, provider, w, h, quality="fast"):
        hq = quality == "high" and os.path.isfile(os.path.join(models_dir, RVM_HQ))
        self.quality = "high" if hq else "fast"
        self.sess = make_session(os.path.join(models_dir, RVM_HQ if hq else RVM), threads, provider)
        self.rec = [np.zeros((1, 1, 1, 1), np.float32)] * 4
        self.w, self.h = w, h
        s = min(1.0, self.MAX_SIDE / float(max(w, h)))
        self.mw, self.mh = int(round(w * s)) // 2 * 2, int(round(h * s)) // 2 * 2
        base = 0.5 if hq else 0.375
        self.ratio = np.array([min(1.0, base * 1280.0 / max(self.mw, self.mh))], np.float32)

    def run(self, frame_bgr):
        """Alpha (float32 0–1) of the people in the frame, at the frame's size."""
        small = frame_bgr if (self.mw, self.mh) == (self.w, self.h) else cv2.resize(frame_bgr, (self.mw, self.mh), interpolation=cv2.INTER_AREA)
        src = cv2.dnn.blobFromImage(small, 1.0 / 255.0, swapRB=True)  # 1×3×H×W RGB 0–1
        _, pha, *rec = self.sess.run(None, {"src": src, "r1i": self.rec[0], "r2i": self.rec[1], "r3i": self.rec[2],
                                             "r4i": self.rec[3], "downsample_ratio": self.ratio})
        self.rec = rec
        alpha = pha[0, 0]
        if (self.mw, self.mh) != (self.w, self.h):
            alpha = cv2.resize(alpha, (self.w, self.h), interpolation=cv2.INTER_LINEAR)
        return alpha


def estimate_foreground(img_u8, a):
    """The person's true colours at soft edges (hair, motion blur), without the old background mixed in:
    approximate fast foreground colour estimation (Forte & Pitié 2020), two blur-fusion passes. Worked out at 1/4 size
    and used only in the soft edge band (inside the person the original pixels are already right), so it stays fast."""
    h, w = a.shape
    hw, hh = max(2, w // 4), max(2, h // 4)
    I = cv2.multiply(cv2.resize(img_u8, (hw, hh), interpolation=cv2.INTER_AREA), 1.0 / 255.0, dtype=cv2.CV_32F)
    sa = cv2.resize(a, (hw, hh), interpolation=cv2.INTER_AREA)
    a3 = cv2.merge([sa, sa, sa])
    na3 = 1.0 - a3

    def fuse(F, B, r):
        ba = cv2.blur(sa, (r, r))
        ba3 = cv2.merge([ba, ba, ba])
        bF = cv2.divide(cv2.blur(cv2.multiply(F, a3), (r, r)), ba3 + 1e-5)
        bB = cv2.divide(cv2.blur(cv2.multiply(B, na3), (r, r)), (1.0 - ba3) + 1e-5)
        mix = cv2.add(cv2.multiply(a3, bF), cv2.multiply(na3, bB))
        return np.clip(cv2.add(bF, cv2.multiply(a3, cv2.subtract(I, mix))), 0.0, 1.0), bB

    F, bB = fuse(I, I, 23)
    F, _ = fuse(F, bB, 3)
    F = cv2.resize(F, (w, h), interpolation=cv2.INTER_LINEAR)
    full = cv2.multiply(img_u8, 1.0 / 255.0, dtype=cv2.CV_32F)
    edge = ((a > 0.02) & (a < 0.98)).astype(np.uint8)
    edge = cv2.dilate(edge, np.ones((5, 5), np.uint8))
    np.copyto(full, F, where=edge[:, :, None].astype(bool))
    return full


class Compositor:
    """Puts the cut-out person on the new scene: clean edge colours, brightness / contrast / tint matched to the scene
    (smoothed over time), scene light wrapping the edges, and one even film grain over the whole frame so person and
    scene look filmed by the same camera. Heavy steps use OpenCV (multi-threaded) rather than numpy."""

    def __init__(self, opts, w, h):
        self.w, self.h = w, h
        self.scale = float(opts.get("scale", 1.0))
        self.ox = float(opts.get("offsetX", 0.0))
        self.oy = float(opts.get("offsetY", 0.0))
        self.harmonize = float(opts.get("harmonize", 0.5))
        self.wrap = float(opts.get("lightWrap", 0.3))
        self.grain = float(opts.get("grain", 0.012))
        self.state = None
        self.move = None
        if abs(self.scale - 1.0) > 1e-3 or abs(self.ox) > 1e-3 or abs(self.oy) > 1e-3:
            s = self.scale  # anchored bottom-centre: feet stay on the ground when the person is resized
            self.move = np.array([[s, 0, (1 - s) * w / 2 + self.ox * w], [0, s, (1 - s) * h + self.oy * h]], np.float32)
        rng = np.random.default_rng(7)
        self.noise = [(rng.normal(0.0, 1.0, (h, w, 3)) * self.grain).astype(np.float32) for _ in range(3)] if self.grain > 0 else []
        self.n = 0

    def run(self, frame, alpha, bg_u8):
        """Original frame (uint8), alpha (float32 0–1), new scene (uint8) → composed uint8 BGR frame."""
        a = np.clip((alpha - 0.03) * (1.0 / 0.94), 0.0, 1.0).astype(np.float32)  # trims the faint halo of the old background
        F = estimate_foreground(frame, a)
        if self.move is not None:
            F = cv2.warpAffine(F, self.move, (self.w, self.h), flags=cv2.INTER_LINEAR, borderValue=0.0)
            a = cv2.warpAffine(a, self.move, (self.w, self.h), flags=cv2.INTER_LINEAR, borderValue=0.0)
        bg = cv2.multiply(bg_u8, 1.0 / 255.0, dtype=cv2.CV_32F)
        if self.harmonize > 0:
            F = self._harmonize(F, a, bg)
        out = cv2.blendLinear(F, bg, a, 1.0 - a)
        if self.wrap > 0:
            sigma = self.h * 0.012
            k = np.clip(self.wrap * 2.0 * a * (1.0 - soft_blur(a, sigma)), 0.0, 1.0).astype(np.float32)
            out = cv2.blendLinear(out, soft_blur(bg, sigma), 1.0 - k, k)
        if self.noise:
            out = cv2.add(out, self.noise[self.n % len(self.noise)])
            self.n += 1
        return cv2.convertScaleAbs(out, alpha=255.0)

    def _harmonize(self, F, a, bg):
        """Moves the person's brightness, contrast and tint part of the way toward the new scene (LAB statistics,
        smoothed over time so it never flickers)."""
        step = 4
        fl = cv2.cvtColor(np.ascontiguousarray(F[::step, ::step]), cv2.COLOR_BGR2LAB)
        bl = cv2.cvtColor(np.ascontiguousarray(bg[::step, ::step]), cv2.COLOR_BGR2LAB)
        m = a[::step, ::step] > 0.7
        if m.sum() < 100:
            return F
        cur = np.concatenate([fl[m].mean(0), fl[m].std(0) + 1e-3, bl.reshape(-1, 3).mean(0), bl.reshape(-1, 3).std(0) + 1e-3])
        self.state = cur if self.state is None else self.state * 0.92 + cur * 0.08
        fm, fs, bm, bs = np.split(self.state, 4)
        s = self.harmonize
        gain = 1.0 + s * np.array([0.5, 0.3, 0.3]) * (bs / fs - 1.0)
        target = fm + s * (bm - fm) * np.array([0.6, 0.5, 0.5])
        # lab' = lab * gain + (target - fm * gain), per channel: done exactly on a sample of the person's pixels, then
        # applied to the whole frame as the 3x4 colour matrix that fits it best (one fast cv2.transform)
        offset = target - fm * gain
        src = F[::step, ::step][m].reshape(-1, 1, 3).astype(np.float32)
        if len(src) > 4000:
            src = src[:: len(src) // 4000]
        lab = cv2.cvtColor(src, cv2.COLOR_BGR2LAB).reshape(-1, 3) * gain + offset
        dst = cv2.cvtColor(lab.astype(np.float32).reshape(-1, 1, 3), cv2.COLOR_LAB2BGR).reshape(-1, 3)
        X = np.hstack([src.reshape(-1, 3), np.ones((len(src), 1), np.float32)])
        M, *_ = np.linalg.lstsq(X, dst, rcond=None)
        return np.clip(cv2.transform(F, M.T.astype(np.float32)), 0.0, 1.0)


# ───────────────────────── commands ─────────────────────────

def cmd_probe(args):
    info = {"type": "probe", "ok": True, "python": sys.version.split()[0], "numpy": np.__version__, "opencv": cv2.__version__}
    try:
        import onnxruntime as ort
        info["onnxruntime"] = ort.__version__
        info["providers"] = ort.get_available_providers()
    except Exception as ex:  # noqa: BLE001
        info["ok"] = False
        info["error"] = f"onnxruntime: {ex}"
    try:
        import onnx  # noqa: F401
        info["onnx"] = onnx.__version__
    except Exception:
        info["onnx"] = None
    md = os.path.join(args.home, "models")
    info["models"] = {k: os.path.isfile(os.path.join(md, v)) for k, v in
                      {"yunet": YUNET, "sface": SFACE, "rvm": RVM, "rvmHigh": RVM_HQ, "swapper": SWAPPER, "identity": IDENTITY}.items()}
    info["home"] = args.home
    if not all(info["models"][k] for k in ("yunet", "sface", "rvm")):
        info["ok"] = False
        info.setdefault("error", "Thiếu model — chạy scripts\\pc\\setup-reup-engine.bat")
    emit(**info)


def cmd_bench(args):
    md = os.path.join(args.home, "models")
    threads = args.threads or default_threads()
    results = {}
    rng = np.random.default_rng(1)
    qualities = ["fast"] + (["high"] if os.path.isfile(os.path.join(md, RVM_HQ)) else [])
    for quality in qualities:
        for name, (w, h) in {"720p": (720, 1280), "1080p": (1080, 1920)}.items():
            mat = Matting(md, threads, args.provider, w, h, quality)
            comp = Compositor({}, w, h)
            bg = (rng.random((h, w, 3)) * 255).astype(np.uint8)
            frame = np.full((h, w, 3), 90, np.uint8)
            cv2.ellipse(frame, (w // 2, h // 2), (w // 5, h // 3), 0, 0, 360, (40, 160, 220), -1)
            for _ in range(2):
                mat.run(frame)
            n, tm, tc = 6, 0.0, 0.0
            for _ in range(n):
                frame = np.roll(frame, 3, axis=1)
                t0 = time.perf_counter()
                a = mat.run(frame)
                t1 = time.perf_counter()
                comp.run(frame, a, bg)
                tm += t1 - t0
                tc += time.perf_counter() - t1
            # the render runs the two stages on two threads at once: the slower one sets the pace
            results[name if quality == "fast" else f"{name}-high"] = round(n / max(tm, tc), 2)
    emit(type="bench", backgroundFps=results, threads=threads, provider=args.provider)


def cmd_check_face(args):
    md = os.path.join(args.home, "models")
    img = read_image(args.image)
    finder = FaceFinder(md, det_size=1280)
    faces = [f for f in finder.detect(img) if f[14] >= 0.6]
    res = {"type": "face", "ok": False, "faces": len(faces)}
    if not faces:
        res["reason"] = "Không thấy khuôn mặt nào rõ ràng trong ảnh."
        emit(**res)
        return
    faces.sort(key=lambda f: f[2] * f[3], reverse=True)
    main = faces[0]
    x, y, w, h = [float(v) for v in main[:4]]
    eye, yaw = face_pose(kps_of(main))
    crop = img[max(0, int(y)):int(y + h), max(0, int(x)):int(x + w)]
    sharp = float(cv2.Laplacian(cv2.cvtColor(cv2.resize(crop, (112, 112)), cv2.COLOR_BGR2GRAY), cv2.CV_64F).var()) if crop.size else 0.0
    res.update(score=round(float(main[14]), 3), faceWidth=round(w), sharpness=round(sharp, 1), yaw=round(yaw, 3),
               box=[round(x), round(y), round(w), round(h)])
    others = [f for f in faces[1:] if f[2] * f[3] > 0.15 * w * h]
    if others:
        res["reason"] = f"Ảnh có {len(others) + 1} khuôn mặt — cần ảnh chỉ có MỘT người."
    elif main[14] < 0.8:
        res["reason"] = "Khuôn mặt chưa đủ rõ (độ tin cậy thấp)."
    elif w < 112:
        res["reason"] = f"Khuôn mặt quá nhỏ ({int(w)} px) — cần ít nhất 112 px chiều ngang."
    elif sharp < 40:
        res["reason"] = "Ảnh mặt bị mờ — chọn ảnh nét hơn."
    elif abs(yaw) > 0.22:
        res["reason"] = "Mặt đang quay nghiêng — cần ảnh nhìn thẳng."
    else:
        res["ok"] = True
        res["embedding"] = [round(float(v), 5) for v in finder.feature(img, main).reshape(-1)]
    if args.thumb:
        cx, cy, side = x + w / 2, y + h / 2, max(w, h) * 1.9
        x0, y0 = int(max(0, cx - side / 2)), int(max(0, cy - side / 2))
        x1, y1 = int(min(img.shape[1], cx + side / 2)), int(min(img.shape[0], cy + side / 2))
        write_jpeg(args.thumb, cv2.resize(img[y0:y1, x0:x1], (320, 320), interpolation=cv2.INTER_AREA))
    emit(**res)


def cmd_render(args):
    with open(args.job, "r", encoding="utf-8") as fh:
        job = json.load(fh)
    ffmpeg = job["ffmpeg"]
    w, h, fps = int(job["width"]), int(job["height"]), str(job["fps"])
    num, _, den = fps.partition("/")
    fps_value = float(num) / float(den or 1)
    total = int(job.get("frames") or 0)
    threads = int(job.get("threads") or default_threads())
    provider = job.get("provider", "cpu")
    md = os.path.join(job["home"], "models")
    face_opts, bg_opts = job.get("faceSwap"), job.get("background")

    emit(type="stage", stage="loading", message="Đang nạp model…")
    track = None
    if face_opts:
        finder = FaceFinder(md)
        swapper = FaceSwapper(md, threads, provider)
        src = read_image(face_opts["characterImage"])
        faces = sorted(finder.detect(src), key=lambda f: f[2] * f[3], reverse=True)
        if not faces:
            fail("Ảnh nhân vật không có khuôn mặt rõ ràng.")
        swapper.set_source(src, kps_of(faces[0]))
        track = FaceTrack(finder, swapper, face_opts)
    matting = comp = timeline = None
    if bg_opts:
        matting = Matting(md, threads, provider, w, h, bg_opts.get("quality", "high"))
        comp = Compositor(bg_opts, w, h)
        timeline = BackdropTimeline(bg_opts["segments"], ffmpeg, w, h, fps, float(bg_opts.get("blur", 0)),
                                    float(bg_opts.get("crossfade", 0.5)))

    dec_args = [ffmpeg, "-v", "error"]
    if job.get("startSeconds"):
        dec_args += ["-ss", f"{float(job['startSeconds']):.3f}"]
    dec_args += ["-i", job["input"]]
    if job.get("durationSeconds"):
        dec_args += ["-t", f"{float(job['durationSeconds']):.3f}"]
    dec_args += ["-an", "-sn", "-dn", "-vf", f"fps={fps},scale={w}:{h}:flags=lanczos,format=bgr24",
                 "-f", "rawvideo", "-pix_fmt", "bgr24", "-"]
    enc_args = [ffmpeg, "-v", "error", "-y", "-f", "rawvideo", "-pix_fmt", "bgr24", "-s", f"{w}x{h}", "-framerate", fps,
                "-i", "-", "-an", "-c:v", "libx264", "-preset", job.get("preset", "medium"), "-crf", str(job.get("crf", 18)),
                "-pix_fmt", "yuv420p", "-r", fps, "-movflags", "+faststart", job["output"]]
    dec = popen(dec_args)
    dec_err = StderrTail(dec)
    enc = subprocess.Popen(enc_args, stdin=subprocess.PIPE, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE,
                           creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
    enc_err = StderrTail(enc)
    reader = RawReader(dec, w * h * 3)
    out_q = queue.Queue(maxsize=6)
    write_error = []

    def writer():
        try:
            while True:
                item = out_q.get()
                if item is None:
                    break
                enc.stdin.write(item)
        except Exception as ex:  # noqa: BLE001
            write_error.append(str(ex))
        finally:
            try:
                enc.stdin.close()
            except Exception:
                pass

    wt = threading.Thread(target=writer, daemon=True)
    wt.start()

    # 2-stage pipeline: this thread runs face swap + matting (in frame order: both carry state between frames), a
    # second thread puts each matted frame on its backdrop while the next one is being matted.
    comp_q = queue.Queue(maxsize=4)
    comp_error = []

    def composer():
        try:
            while True:
                item = comp_q.get()
                if item is None:
                    break
                idx, frame_, alpha = item
                out_q.put(comp.run(frame_, alpha, timeline.frame(idx / fps_value)).tobytes())
        except Exception as ex:  # noqa: BLE001
            comp_error.append(f"{type(ex).__name__}: {ex}")
            while comp_q.get() is not None:  # drain so the producer never blocks
                pass

    ct = None
    if matting is not None:
        ct = threading.Thread(target=composer, daemon=True)
        ct.start()

    emit(type="stage", stage="processing", message="Đang xử lý từng khung hình…")
    t0 = last_emit = time.perf_counter()
    i = 0
    try:
        while True:
            buf = reader.get()
            if buf is None:
                break
            frame = np.frombuffer(buf, np.uint8).reshape(h, w, 3).copy()
            if track is not None:
                track.process(frame)
            if matting is not None:
                alpha = matting.run(frame)
                comp_q.put((i, frame, alpha))
            else:
                out_q.put(frame.tobytes())
            if write_error or comp_error:
                break
            i += 1
            now = time.perf_counter()
            if now - last_emit >= 1.0:
                last_emit = now
                emit(type="progress", done=i, total=max(total, i), fps=round(i / (now - t0), 2))
    finally:
        if ct is not None:
            comp_q.put(None)
            ct.join()
        out_q.put(None)
        wt.join()
        if timeline is not None:
            timeline.close()
        if write_error or comp_error:
            for p_ in (dec, enc):
                try:
                    p_.kill()
                except Exception:
                    pass
    if comp_error:
        fail("Lỗi ghép cảnh nền: " + comp_error[0])
    dec.wait()
    enc.wait()
    if write_error or enc.returncode != 0:
        fail("FFmpeg không mã hoá được video: " + (enc_err.text() or "; ".join(write_error)))
    if dec.returncode not in (0, None) and i == 0:
        fail("FFmpeg không đọc được video nguồn: " + dec_err.text())
    if i == 0:
        fail("Video nguồn không có khung hình nào.")
    elapsed = time.perf_counter() - t0
    stats = {"frames": i, "seconds": round(elapsed, 1), "fps": round(i / elapsed, 2) if elapsed > 0 else 0}
    if track is not None:
        stats.update(faceSwapped=track.swapped, faceKeptOriginal=track.kept, noFace=track.no_face)
    emit(type="done", **stats)


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest="cmd", required=True)
    a = sub.add_parser("probe"); a.add_argument("--home", required=True)
    a = sub.add_parser("bench"); a.add_argument("--home", required=True); a.add_argument("--provider", default="cpu"); a.add_argument("--threads", type=int, default=0)
    a = sub.add_parser("check-face"); a.add_argument("--home", required=True); a.add_argument("--image", required=True); a.add_argument("--thumb")
    a = sub.add_parser("render"); a.add_argument("--job", required=True)
    args = p.parse_args()
    for stream in (sys.stdout, sys.stderr):  # the app reads UTF-8 JSON lines whatever the Windows code page
        stream.reconfigure(encoding="utf-8", errors="replace")
    cv2.setNumThreads(max(1, (os.cpu_count() or 4) // 2))
    try:
        {"probe": cmd_probe, "bench": cmd_bench, "check-face": cmd_check_face, "render": cmd_render}[args.cmd](args)
    except SystemExit:
        raise
    except Exception as ex:  # noqa: BLE001
        fail(f"{type(ex).__name__}: {ex}")


if __name__ == "__main__":
    main()
