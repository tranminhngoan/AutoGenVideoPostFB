"""
Film GPU worker — runs on a free Kaggle GPU (T4 16 GB) or a rented GPU (e.g. RTX 3090 / 4090 24 GB on RunPod).

Modes:
  1. Standalone/Batch mode (default on Kaggle):
     python film_gpu_worker.py
     Reads jobs.json from IN_DIR, writes outputs to OUT_DIR.
  2. Server mode (on RunPod / cloud GPU):
     python film_gpu_worker.py --server --port 8000
     Listens for HTTP POST /submit (zip of bundle), GET /status, GET /download.

Job types:
  image   : RealVisXL (SDXL) still frame, optional face reference (IP-Adapter Plus Face),
            then 2x Real-ESRGAN and a Lanczos fit to target size -> sharp JPEG.
  motion  : image -> short video clip.
            Wan 2.2 / 2.1 Image-to-Video when >= 23 GB VRAM (RTX 3090/4090);
            LTX-Video on 16 GB cards (Kaggle T4).
  lipsync : motion clip + approved master voice -> lip-synced clip (LatentSync 1.6, run in its own
            venv from setup_lipsync.sh), then a SyncNet confidence/offset check when its weights exist.
            Runs after the motion model is unloaded, so the two never share VRAM.

Every job result records wall time, peak VRAM and average GPU utilisation (nvidia-smi sampling),
so batches double as benchmarks.
"""
import gc
import glob
import json
import os
import shutil
import subprocess
import sys
import time
import traceback
import zipfile
from http.server import HTTPServer, BaseHTTPRequestHandler
from socketserver import ThreadingMixIn
import urllib.parse
import threading
import queue

os.environ.setdefault("PYTORCH_CUDA_ALLOC_CONF", "expandable_segments:True")

# Route HuggingFace cache and temp directories to /workspace to avoid filling the small container root disk
WORKSPACE_DIR = os.environ.get("WORKSPACE_DIR", "/workspace" if os.path.isdir("/workspace") else os.getcwd())
CACHE_DIR = os.path.join(WORKSPACE_DIR, ".cache")
try:
    os.makedirs(os.path.join(CACHE_DIR, "huggingface", "hub"), exist_ok=True)
    os.makedirs(os.path.join(CACHE_DIR, "torch"), exist_ok=True)
    os.makedirs(os.path.join(CACHE_DIR, "tmp"), exist_ok=True)
    os.environ["HF_HOME"] = os.path.join(CACHE_DIR, "huggingface")
    os.environ["HF_HUB_CACHE"] = os.path.join(CACHE_DIR, "huggingface", "hub")
    os.environ["TORCH_HOME"] = os.path.join(CACHE_DIR, "torch")
    os.environ["TMPDIR"] = os.path.join(CACHE_DIR, "tmp")
    os.environ["TEMP"] = os.path.join(CACHE_DIR, "tmp")
    os.environ["TMP"] = os.path.join(CACHE_DIR, "tmp")
    import tempfile
    tempfile.tempdir = os.path.join(CACHE_DIR, "tmp")
except Exception:
    pass


def log(*a):
    print(time.strftime("%H:%M:%S"), *a, flush=True)


def pip(*pkgs):
    subprocess.run([sys.executable, "-m", "pip", "install", "-q", "-U", *pkgs], check=True)


# Install required packages on worker startup (FILM_WORKER_SKIP_PIP=1 skips it: offline tests, pinned images)
if os.environ.get("FILM_WORKER_SKIP_PIP") != "1":
    log("Checking / installing AI dependencies...")
    pip("diffusers>=0.35.0", "transformers>=4.49", "accelerate", "sentencepiece", "peft",
        "imageio", "imageio-ffmpeg", "spandrel", "ftfy", "tiktoken", "protobuf", "einops")

import numpy as np
import torch
from PIL import Image

if torch.cuda.is_available():
    gpu = torch.cuda.get_device_properties(0)
    VRAM_GB = gpu.total_memory / 2**30
    DTYPE = torch.bfloat16 if gpu.major >= 8 else torch.float16
    log("CUDA GPU detected:", gpu.name, f"{VRAM_GB:.1f} GB VRAM", f"dtype={DTYPE}")
    # Hardware acceleration flags for Blackwell / Ada / Ampere
    try:
        torch.backends.cuda.matmul.allow_tf32 = True
        torch.backends.cudnn.allow_tf32 = True
        torch.backends.cudnn.benchmark = True
        n_threads = min(16, os.cpu_count() or 4)
        torch.set_num_threads(n_threads)
        log(f"Configured PyTorch: TF32=ON, cuDNN Benchmark=ON, CPU Threads={n_threads}")
    except Exception:
        pass
else:
    gpu = None
    VRAM_GB = 0
    DTYPE = torch.float32
    log("WARNING: No CUDA GPU detected, running in CPU mode.")


class GpuSampler:
    """Samples nvidia-smi once a second while a job runs: peak memory used (whole GPU, so it also sees
    subprocesses such as LatentSync) and mean utilisation. Gives None values when nvidia-smi is absent."""

    def __init__(self, interval=1.0):
        self.interval = interval
        self.peak_mib = None
        self.util = []
        self._stop = threading.Event()
        self._thread = None

    def _poll(self):
        while not self._stop.is_set():
            try:
                out = subprocess.run(
                    ["nvidia-smi", "--query-gpu=memory.used,utilization.gpu", "--format=csv,noheader,nounits", "-i", "0"],
                    capture_output=True, text=True, timeout=5).stdout.strip()
                mem, util = [float(x) for x in out.split(",")[:2]]
                self.peak_mib = mem if self.peak_mib is None else max(self.peak_mib, mem)
                self.util.append(util)
            except Exception:
                return  # no nvidia-smi (CPU box / tests): stop sampling quietly
            self._stop.wait(self.interval)

    def __enter__(self):
        self._thread = threading.Thread(target=self._poll, daemon=True)
        self._thread.start()
        return self

    def __exit__(self, *exc):
        self._stop.set()
        if self._thread:
            self._thread.join(timeout=6)
        return False

    def metrics(self):
        return {
            "gpu_peak_mem_gb": round(self.peak_mib / 1024, 2) if self.peak_mib is not None else None,
            "gpu_util_avg": round(sum(self.util) / len(self.util), 1) if self.util else None,
        }


def run_cmd(cmd, cwd=None, timeout=None):
    """Runs a command; returns (code, stdout, stderr)."""
    p = subprocess.run(cmd, cwd=cwd, capture_output=True, text=True, timeout=timeout)
    return p.returncode, p.stdout, p.stderr


def probe_seconds(path):
    code, out, _ = run_cmd(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", path])
    try:
        return float(out.strip()) if code == 0 else 0.0
    except ValueError:
        return 0.0


def sync_verdict(conf, offset, qc):
    """PASS / WARNING / REJECT from SyncNet confidence and AV offset (frames @25 fps).
    Defaults are provisional until calibrated on benchmark Test A; jobs.json "qc" overrides them."""
    if conf is None or offset is None:
        return "NOT_MEASURED"
    if conf < qc.get("sync_conf_reject", 3.0) or abs(offset) > qc.get("sync_offset_reject", 3):
        return "REJECT"
    if conf < qc.get("sync_conf_pass", 6.0) or abs(offset) > qc.get("sync_offset_pass", 1):
        return "WARNING"
    return "PASS"


def parse_syncnet_output(text):
    """Reads 'SyncNet confidence: 7.12' and 'AV offset: -1' from LatentSync's eval_sync_conf output."""
    import re
    c = re.search(r"SyncNet confidence:\s*(-?[\d.]+)", text or "")
    o = re.search(r"AV offset:\s*(-?\d+)", text or "")
    return (float(c.group(1)) if c else None), (int(o.group(1)) if o else None)


def process_batch(in_dir, out_dir, batch_id=None, progress_cb=None):
    """Processes one complete batch (image and motion stages) from in_dir to out_dir."""
    t_start = time.time()
    os.makedirs(out_dir, exist_ok=True)
    results_path = os.path.join(out_dir, "results.json")
    results = {
        "batch_id": batch_id,
        "env": {
            "gpu": gpu.name if gpu else "cpu",
            "vram_gb": round(VRAM_GB, 1),
            "torch": torch.__version__
        },
        "jobs": {}
    }

    def save_results():
        with open(results_path, "w", encoding="utf-8") as f:
            json.dump(results, f, ensure_ascii=False, indent=1)

    save_results()

    jobs_file = os.path.join(in_dir, "jobs.json")
    if not os.path.exists(jobs_file):
        # Check recursively in case it was extracted inside a subfolder
        found = glob.glob(os.path.join(in_dir, "**/jobs.json"), recursive=True)
        if found:
            jobs_file = found[0]
            in_dir = os.path.dirname(jobs_file)
        else:
            raise FileNotFoundError(f"Cannot find jobs.json in {in_dir}")

    with open(jobs_file, encoding="utf-8") as f:
        spec = json.load(f)

    jobs = spec.get("jobs", [])
    if not batch_id:
        batch_id = spec.get("batch_id", f"batch_{int(time.time())}")
    results["batch_id"] = batch_id
    save_results()

    def path_of(name):
        for d in (out_dir, in_dir):
            p = os.path.join(d, name)
            if os.path.exists(p):
                return p
        raise FileNotFoundError(f"Input file not found: {name} in {in_dir} or {out_dir}")

    def run_stage(kind, handler, max_attempts=1):
        """Runs every job of one type. A handler may return a dict (settings used, QC) that is stored
        with the job result. Each job is tried at most max_attempts times (never loops on a bad input)."""
        todo = [j for j in jobs if j.get("type") == kind]
        if not todo:
            return
        stage_t = time.time()
        log(f"== Stage {kind}: {len(todo)} jobs ==")
        for idx, j in enumerate(todo):
            t = time.time()
            jid = j["id"]
            out_file = os.path.join(out_dir, j.get("out", ""))

            # Checkpoint Resume: If this exact output file already exists on disk and is non-empty, SKIP IT!
            if os.path.exists(out_file) and os.path.getsize(out_file) > 10240:
                log(f"  [{kind}] {jid} already completed on disk ({os.path.getsize(out_file):,d} bytes), skipping!")
                results["jobs"][jid] = {"status": "ok", "seconds": 0.0, "out": j["out"], "resumed": True}
                continue

            # Checkpoint Recovery: Check if previous batch of this same episode already rendered this exact file
            prefix = batch_id.split("-")[0] if batch_id else ""
            if prefix:
                found_prev = False
                for prev_path in glob.glob(os.path.join(WORKSPACE_DIR, "batches", f"{prefix}*", "out", j.get("out", ""))):
                    if prev_path != out_file and os.path.exists(prev_path) and os.path.getsize(prev_path) > 10240:
                        log(f"  [{kind}] {jid} recovered from previous batch ({os.path.getsize(prev_path):,d} bytes)!")
                        os.makedirs(os.path.dirname(out_file), exist_ok=True)
                        shutil.copy2(prev_path, out_file)
                        results["jobs"][jid] = {"status": "ok", "seconds": 0.0, "out": j["out"], "recovered": True}
                        found_prev = True
                        break
                if found_prev:
                    continue

            if progress_cb:
                progress_cb(f"Stage {kind}: {idx + 1}/{len(todo)} ({jid})")
            for attempt in range(1, max_attempts + 1):
                t = time.time()
                j["_attempt"] = attempt
                if torch.cuda.is_available():
                    torch.cuda.reset_peak_memory_stats()
                sampler = GpuSampler()
                try:
                    with sampler:
                        info = handler(j) or {}
                    entry = {"status": "ok", "seconds": round(time.time() - t, 1), "out": j["out"], "attempts": attempt}
                    entry.update(info)
                    log(f"  [{kind}] {jid} ok in {time.time() - t:.1f}s (attempt {attempt})")
                    ok = True
                except Exception as e:
                    err_msg = f"{type(e).__name__}: {e}"
                    entry = {"status": "failed", "seconds": round(time.time() - t, 1), "error": err_msg[:800], "attempts": attempt}
                    log(f"  [{kind}] {jid} FAILED (attempt {attempt}/{max_attempts}): {traceback.format_exc()[-600:]}")
                    ok = False
                    if torch.cuda.is_available() and "out of memory" in str(e).lower():
                        gc.collect()
                        torch.cuda.empty_cache()  # only after an OOM, where it actually helps the next job
                entry.update(sampler.metrics())
                if torch.cuda.is_available():
                    entry["torch_peak_alloc_gb"] = round(torch.cuda.max_memory_allocated() / 2**30, 2)
                results["jobs"][jid] = entry
                save_results()
                if ok:
                    break
        results.setdefault("stages", {}).setdefault(kind, {})["seconds"] = round(time.time() - stage_t, 1)
        save_results()

    # ─────────────────────────────── stage 1: still frames ───────────────────────────────
    def image_stage():
        if not any(j.get("type") == "image" for j in jobs):
            return
        from diffusers import StableDiffusionXLPipeline, DPMSolverMultistepScheduler
        import spandrel

        t = time.time()
        model_id = spec.get("image_model", "SG161222/RealVisXL_V5.0")
        hf_cache = os.path.join(CACHE_DIR, "huggingface", "hub")
        try:
            pipe = StableDiffusionXLPipeline.from_pretrained(model_id, torch_dtype=torch.float16, variant="fp16", cache_dir=hf_cache)
        except Exception:
            pipe = StableDiffusionXLPipeline.from_pretrained(model_id, torch_dtype=torch.float16, cache_dir=hf_cache)

        pipe.scheduler = DPMSolverMultistepScheduler.from_config(
            pipe.scheduler.config, use_karras_sigmas=True, algorithm_type="sde-dpmsolver++"
        )
        if torch.cuda.is_available():
            pipe.to("cuda")
        pipe.vae.enable_tiling()

        has_ip = False
        if any(j.get("face_ref") for j in jobs if j.get("type") == "image"):
            try:
                pipe.load_ip_adapter(
                    "h94/IP-Adapter",
                    subfolder="sdxl_models",
                    weight_name="ip-adapter-plus-face_sdxl_vit-h.safetensors",
                    image_encoder_folder="models/image_encoder",
                    cache_dir=hf_cache
                )
                has_ip = True
                log("IP-Adapter Plus Face loaded successfully.")
            except Exception as e:
                log(f"Warning: Could not load IP-Adapter ({e}), face reference disabled.")

        import urllib.request
        esr = os.path.join(CACHE_DIR, "RealESRGAN_x2plus.pth")
        if not os.path.exists(esr):
            os.makedirs(os.path.dirname(esr), exist_ok=True)
            urllib.request.urlretrieve("https://github.com/xinntao/Real-ESRGAN/releases/download/v0.2.1/RealESRGAN_x2plus.pth", esr)
        upscaler = spandrel.ModelLoader().load_from_file(esr).model.eval()
        if torch.cuda.is_available():
            upscaler = upscaler.cuda().half()
        log(f"Image pipeline & Real-ESRGAN loaded in {time.time() - t:.1f}s")
        results.setdefault("stages", {}).setdefault("image", {})["load_seconds"] = round(time.time() - t, 1)

        def chunk_ids(tok, text, n_chunks=None):
            ids = tok(text, truncation=False, add_special_tokens=False).input_ids
            chunks = [ids[i:i + 75] for i in range(0, len(ids), 75)] or [[]]
            while n_chunks and len(chunks) < n_chunks:
                chunks.append([])
            pad = tok.pad_token_id if tok.pad_token_id is not None else tok.eos_token_id
            return [[tok.bos_token_id] + c + [tok.eos_token_id] + [pad] * (75 - len(c)) for c in chunks]

        @torch.no_grad()
        def encode_long(text, n_chunks):
            dev = "cuda" if torch.cuda.is_available() else "cpu"
            parts, pooled = [], None
            for tok, enc, second in ((pipe.tokenizer, pipe.text_encoder, False), (pipe.tokenizer_2, pipe.text_encoder_2, True)):
                outs = []
                for i, c in enumerate(chunk_ids(tok, text, n_chunks)[:n_chunks]):
                    o = enc(torch.tensor([c], device=dev), output_hidden_states=True)
                    outs.append(o.hidden_states[-2])
                    if second and i == 0:
                        pooled = o[0]
                parts.append(torch.cat(outs, dim=1))
            return torch.cat(parts, dim=-1), pooled

        def upscale(img):
            x = torch.from_numpy(np.asarray(img, dtype=np.float32) / 255.0).permute(2, 0, 1)[None]
            if torch.cuda.is_available():
                x = x.cuda().half()
            with torch.no_grad():
                y = upscaler(x).clamp(0, 1)
            return Image.fromarray((y[0].permute(1, 2, 0).float().cpu().numpy() * 255).round().astype(np.uint8))

        def handle(j):
            w, h = j.get("width", 768), j.get("height", 1344)
            kwargs = {}
            if has_ip:
                if j.get("face_ref"):
                    pipe.set_ip_adapter_scale(j.get("face_strength", 0.45))
                    kwargs["ip_adapter_image"] = Image.open(path_of(j["face_ref"])).convert("RGB")
                else:
                    pipe.set_ip_adapter_scale(0.0)
                    kwargs["ip_adapter_image"] = Image.new("RGB", (224, 224))

            neg = j.get("negative", "blurry, lowres, deformed face, bad anatomy, extra fingers, text, watermark, cartoon, painting, 3d render")
            n = max(len(chunk_ids(pipe.tokenizer, j["prompt"])), len(chunk_ids(pipe.tokenizer, neg)))
            pe, pp = encode_long(j["prompt"], n)
            ne, npool = encode_long(neg, n)

            generator = torch.Generator("cuda" if torch.cuda.is_available() else "cpu").manual_seed(j.get("seed", 1))
            img = pipe(
                prompt_embeds=pe, pooled_prompt_embeds=pp,
                negative_prompt_embeds=ne, negative_pooled_prompt_embeds=npool,
                width=w, height=h, num_inference_steps=j.get("steps", 30), guidance_scale=j.get("cfg", 5.0),
                generator=generator, **kwargs
            ).images[0]

            big = upscale(img)
            tw, th = j.get("target_width", 1080), j.get("target_height", 1920)
            scale = max(tw / big.width, th / big.height)
            big = big.resize((round(big.width * scale), round(big.height * scale)), Image.LANCZOS)
            left, top = (big.width - tw) // 2, (big.height - th) // 2
            out_file = os.path.join(out_dir, j["out"])
            os.makedirs(os.path.dirname(out_file), exist_ok=True)
            big.crop((left, top, left + tw, top + th)).save(out_file, quality=95)

        run_stage("image", handle)
        del pipe, upscaler
        gc.collect()
        if torch.cuda.is_available():
            torch.cuda.empty_cache()

    # ─────────────────────────────── stage 2: motion clips ───────────────────────────────
    def motion_stage():
        if not any(j.get("type") == "motion" for j in jobs):
            return
        from diffusers.utils import export_to_video

        # Clean up any incomplete downloads from previous runs to recover disk space
        for inc in glob.glob(os.path.join(CACHE_DIR, "huggingface", "**", "*.incomplete"), recursive=True):
            try:
                os.remove(inc)
            except Exception:
                pass

        free_gb = shutil.disk_usage(WORKSPACE_DIR).free / (1024**3)
        forced_motion = os.environ.get("MOTION_MODEL")
        if forced_motion == "wan":
            use_wan = True
        elif forced_motion == "ltx":
            use_wan = False
        else:
            # Wan 14B model requires ~34 GB. Only attempt Wan if >= 45 GB free space is available!
            use_wan = (VRAM_GB >= 23 and free_gb >= 45.0)

        log(f"Motion model check: {free_gb:.1f} GB disk free. Selected: {'Wan 2.1 (14B)' if use_wan else 'LTX-Video (4.5B)'}")
        t = time.time()
        pipe = None

        hf_cache = os.path.join(CACHE_DIR, "huggingface", "hub")
        is_14b = False
        if use_wan:
            try:
                import tiktoken
            except ImportError:
                log("Installing missing tiktoken/protobuf for Wan tokenizer...")
                pip("tiktoken", "protobuf", "einops")
            from diffusers import WanImageToVideoPipeline

            # Smart Model Selection based on available VRAM:
            # - If VRAM >= 70 GB (A100/H100 80GB): default to Wan 14B for maximum cinematic quality
            # - If VRAM < 70 GB (RTX 3090/4090 24GB): default to Wan 1.3B for ultra-fast native VRAM (25s/clip)
            forced_wan = spec.get("wan_model") or os.environ.get("WAN_MODEL")
            if forced_wan:
                wan_candidates = [forced_wan]
            elif VRAM_GB >= 70:
                wan_candidates = [
                    "Wan-AI/Wan2.1-I2V-14B-480P-Diffusers",
                    "Wan-AI/Wan2.1-I2V-1.3B-480P-Diffusers"
                ]
            else:
                wan_candidates = [
                    "Wan-AI/Wan2.1-I2V-1.3B-480P-Diffusers",
                    "Wan-AI/Wan2.1-I2V-14B-480P-Diffusers"
                ]

            for candidate in wan_candidates:
                try:
                    log(f"Loading Wan video model: {candidate} ...")
                    pipe = WanImageToVideoPipeline.from_pretrained(candidate, torch_dtype=torch.bfloat16, cache_dir=hf_cache)
                    results["env"]["motion_model"] = candidate
                    is_14b = "14B" in candidate
                    break
                except Exception as e:
                    log(f"Warning: Failed to load {candidate} ({e})")
            if pipe is None:
                log("Falling back to LTX-Video-0.9.5 due to Wan loading failure...")
                use_wan = False

        if not use_wan:
            from diffusers import LTXImageToVideoPipeline
            ltx_model = spec.get("ltx_model", "Lightricks/LTX-Video-0.9.5")
            log(f"Loading LTX video model: {ltx_model} ...")
            pipe = LTXImageToVideoPipeline.from_pretrained(ltx_model, torch_dtype=DTYPE, cache_dir=hf_cache)
            pipe.vae.enable_tiling()
            results["env"]["motion_model"] = ltx_model

        if pipe is None:
            raise RuntimeError("Could not load any video motion model (Wan or LTX-Video).")

        # Hardware execution strategy:
        # - VRAM >= 85 GB (RTX PRO 6000 96GB): Entire Wan 14B fits natively in 96GB VRAM! Run pipe.to("cuda") with 0 CPU offload overhead (2x-3x faster!)
        # - 70 <= VRAM < 85 GB (A100 80GB): pipe.enable_model_cpu_offload()
        # - VRAM < 70 GB (RTX 3090/4090 24GB): sequential CPU offload for 14B, or native model offload for 1.3B
        if use_wan:
            if is_14b and VRAM_GB >= 85:
                try:
                    pipe.to("cuda")
                    log("⚡ Extreme Hardware Mode: Entire Wan 14B loaded 100% in 96GB GDDR7 VRAM (Zero PCIe transfer overhead)!")
                except Exception as e_to:
                    log(f"Warning on direct VRAM ({e_to}), falling back to model offload...")
                    pipe.enable_model_cpu_offload()
            elif is_14b and VRAM_GB < 70:
                try:
                    pipe.enable_sequential_cpu_offload()
                    log("Enabled sequential CPU offload for 14B model on 24GB VRAM.")
                except Exception as e_off:
                    log(f"Sequential offload warning ({e_off}), using model offload...")
                    pipe.enable_model_cpu_offload()
            else:
                try:
                    pipe.enable_model_cpu_offload()
                    log(f"Enabled model CPU offload (native VRAM execution for {'14B on 80GB' if is_14b else '1.3B on 24GB'}).")
                except Exception:
                    pipe.to("cuda")

            if hasattr(pipe, "vae"):
                if hasattr(pipe.vae, "enable_tiling"):
                    pipe.vae.enable_tiling()
                if hasattr(pipe.vae, "enable_slicing"):
                    pipe.vae.enable_slicing()
        else:
            pipe.enable_model_cpu_offload()
            if hasattr(pipe, "vae") and hasattr(pipe.vae, "enable_tiling"):
                pipe.vae.enable_tiling()

        fps = 16 if use_wan else 24
        w, h = 480, 832
        steps = 40 if is_14b else 30
        cfg = 4.0 if (use_wan and is_14b) else 3.5 if use_wan else 3.0
        wan_res_mode = spec.get("wan_resolution") or os.environ.get("WAN_RESOLUTION", "").lower()
        log(f"Motion model ({results['env'].get('motion_model')}) loaded in {time.time() - t:.1f}s, steps={steps}, cfg={cfg}, res_mode={wan_res_mode or 'default'}")
        results.setdefault("stages", {}).setdefault("motion", {})["load_seconds"] = round(time.time() - t, 1)
        model_is_720p = "720P" in str(results["env"].get("motion_model", ""))
        if wan_res_mode == "720p" and not model_is_720p:
            # A 480P checkpoint asked for 1280x720 renders outside its training size (worse faces, 2x slower).
            log("WARNING: 720p requested but the loaded checkpoint is 480P — keeping the job's 480p size.")

        def handle(j):
            jw, jh = j.get("width", w), j.get("height", h)
            # 720p only with a 720P checkpoint
            if use_wan and model_is_720p:
                jw, jh = (1280, 720) if jw >= jh else (720, 1280)

            # Ensure dimensions divisible by 16 for Wan / LTX
            jw = (jw // 16) * 16
            jh = (jh // 16) * 16

            image = Image.open(path_of(j["image"])).convert("RGB").resize((jw, jh), Image.LANCZOS)
            # Wan 2.1 is trained on 81 frames (5.06 s @16 fps); LTX stays at its previous 5 s cap.
            max_frames = 81 if use_wan else int(5.0 * fps) + 1
            frames = min(int(round(float(j.get("seconds", 4)) * fps)) + 1, max_frames)
            # Alignment rules: Wan requires 4k+1, LTX requires 8k+1
            mod = 4 if use_wan else 8
            frames = frames - (frames - 1) % mod

            neg_default = (
                "worst quality, blurry, jittery, distorted face, deformed face, morphing face, melting face, "
                "asymmetrical eyes, open mouth, screaming, grimace, deformed, extra limbs, morphing body, text, watermark, bad anatomy, "
                "cartoon, anime, 3d render, cgi, plastic skin, doll, artificial, drawing, illustration"
            )
            neg = j.get("negative", neg_default)
            generator = torch.Generator("cuda" if torch.cuda.is_available() else "cpu").manual_seed(j.get("seed", 42))

            out = pipe(
                image=image, prompt=j["prompt"],
                negative_prompt=neg,
                width=jw, height=jh, num_frames=frames,
                num_inference_steps=j.get("steps", steps), guidance_scale=cfg,
                generator=generator
            ).frames[0]

            out_file = os.path.join(out_dir, j["out"])
            os.makedirs(os.path.dirname(out_file), exist_ok=True)
            export_to_video(out, out_file, fps=fps)
            return {"model": results["env"].get("motion_model"), "width": jw, "height": jh, "fps": fps,
                    "frames": frames, "steps": j.get("steps", steps), "cfg": cfg, "seed": j.get("seed", 42)}

        run_stage("motion", handle)
        del pipe
        gc.collect()
        if torch.cuda.is_available():
            torch.cuda.empty_cache()

    # ─────────────────────────────── stage 3: lip sync + SyncNet QC ───────────────────────────────
    def lipsync_stage():
        todo = [j for j in jobs if j.get("type") == "lipsync"]
        if not todo:
            return
        ls_dir = os.environ.get("LATENTSYNC_DIR", os.path.join(WORKSPACE_DIR, "LatentSync"))
        default_py = os.path.join(ls_dir, ".venv", "Scripts" if os.name == "nt" else "bin", "python")
        ls_py = os.environ.get("LATENTSYNC_PYTHON", default_py)
        ckpt = os.path.join(ls_dir, "checkpoints", "latentsync_unet.pt")
        unet_cfg = os.path.join(ls_dir, "configs", "unet", "stage2_512.yaml")  # LatentSync 1.6 (512x512)
        syncnet = os.path.join(ls_dir, "checkpoints", "auxiliary", "syncnet_v2.model")
        cfg = spec.get("lipsync", {})
        qc = spec.get("qc", {})
        steps = int(cfg.get("steps", 20))           # upstream inference.sh default
        guidance = float(cfg.get("guidance", 1.5))  # upstream inference.sh default (README range 1.0-3.0)
        deepcache = bool(cfg.get("deepcache", True))

        missing = [p for p in (ls_py, ckpt, unet_cfg) if not os.path.exists(p)]
        if missing:
            msg = (f"LatentSync chưa được cài trên máy GPU (thiếu {', '.join(missing)}). "
                   "Chạy setup_lipsync.sh một lần rồi gửi lại batch.")
            log(msg)
            for j in todo:
                results["jobs"][j["id"]] = {"status": "failed", "seconds": 0.0, "error": msg}
            save_results()
            return
        results["env"]["lipsync_model"] = "LatentSync-1.6 (stage2_512)"

        def sync_qc(video, work):
            if not os.path.exists(syncnet):
                return {"status": "skipped", "reason": "SyncNet weights missing (checkpoints/auxiliary/syncnet_v2.model)"}
            t = time.time()
            code, out, err = run_cmd([ls_py, "-m", "eval.eval_sync_conf", "--video_path", video,
                                      "--temp_dir", os.path.join(work, "qc")], cwd=ls_dir, timeout=900)
            conf, offset = parse_syncnet_output(out + "\n" + err)
            res = {"seconds": round(time.time() - t, 1), "confidence": conf, "av_offset_frames": offset,
                   "verdict": sync_verdict(conf, offset, qc)}
            if conf is None:
                # e.g. "Face not detected" or a clip shorter than SyncNet's 50-frame face track
                res["status"] = "not_measured"
                res["reason"] = (err or out).strip()[-300:]
            else:
                res["status"] = "ok"
            return res

        def handle(j):
            video = path_of(j["video"])
            audio = path_of(j["audio"])
            work = os.path.join(out_dir, "_lipsync", j["id"])
            if os.path.isdir(work):
                shutil.rmtree(work, ignore_errors=True)
            os.makedirs(work, exist_ok=True)

            # Master voice -> 16 kHz mono PCM (LatentSync's audio rate). The master file itself is never
            # modified; the renderer muxes the original, so this copy only drives the mouth.
            wav = os.path.join(work, "voice_16k.wav")
            code, _, err = run_cmd(["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-i", audio,
                                    "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", wav])
            if code != 0 or not os.path.exists(wav):
                raise RuntimeError(f"ffmpeg audio prep failed: {err[-300:]}")

            synced = os.path.join(work, "synced.mp4")
            seed = int(j.get("seed", 1247)) + (j.get("_attempt", 1) - 1)  # a retry draws a new seed
            cmd = [ls_py, "-m", "scripts.inference",
                   "--unet_config_path", unet_cfg, "--inference_ckpt_path", ckpt,
                   "--inference_steps", str(steps), "--guidance_scale", str(guidance),
                   "--video_path", video, "--audio_path", wav, "--video_out_path", synced,
                   "--temp_dir", os.path.join(work, "tmp"), "--seed", str(seed)]
            if deepcache:
                cmd.append("--enable_deepcache")
            # LatentSync writes ./temp relative to its cwd, so jobs run strictly one at a time from ls_dir.
            code, out, err = run_cmd(cmd, cwd=ls_dir, timeout=int(cfg.get("timeout_seconds", 1800)))
            if code != 0 or not os.path.exists(synced) or os.path.getsize(synced) < 10240:
                raise RuntimeError(f"LatentSync exit {code}: {(err or out).strip()[-600:]}")

            v_sec, a_sec = probe_seconds(synced), probe_seconds(wav)
            if a_sec > 0 and abs(v_sec - a_sec) > 0.2:
                raise RuntimeError(f"Synced clip {v_sec:.2f}s does not match the voice {a_sec:.2f}s")

            out_file = os.path.join(out_dir, j["out"])
            os.makedirs(os.path.dirname(out_file), exist_ok=True)
            shutil.move(synced, out_file)
            info = {"model": "LatentSync-1.6", "steps": steps, "guidance": guidance, "deepcache": deepcache,
                    "seed": seed, "video_seconds": round(v_sec, 2), "audio_seconds": round(a_sec, 2),
                    "looped": a_sec > probe_seconds(video) + 0.05}
            info["qc_sync"] = sync_qc(out_file, work)
            shutil.rmtree(work, ignore_errors=True)
            return info

        run_stage("lipsync", handle, max_attempts=int(cfg.get("max_attempts", 2)))

    image_stage()
    motion_stage()
    lipsync_stage()
    results["total_seconds"] = round(time.time() - t_start, 1)
    save_results()
    log("BATCH FINISHED successfully in", results["total_seconds"], "s")
    return results


# ─────────────────────────────── Server Mode ───────────────────────────────
class ThreadedHTTPServer(ThreadingMixIn, HTTPServer):
    daemon_threads = True


class BatchServerState:
    def __init__(self):
        self.queue = queue.Queue()
        self.statuses = {}
        self.lock = threading.Lock()
        self.current_batch = None


server_state = BatchServerState()


def worker_loop():
    while True:
        try:
            batch_id, in_dir, out_dir = server_state.queue.get()
            with server_state.lock:
                server_state.current_batch = batch_id
                server_state.statuses[batch_id] = {
                    "batch_id": batch_id,
                    "state": "running",
                    "detail": "Processing batch...",
                    "start_time": time.time()
                }

            def on_progress(msg):
                with server_state.lock:
                    if batch_id in server_state.statuses:
                        server_state.statuses[batch_id]["detail"] = msg

            try:
                res = process_batch(in_dir, out_dir, batch_id=batch_id, progress_cb=on_progress)
                with server_state.lock:
                    server_state.statuses[batch_id] = {
                        "batch_id": batch_id,
                        "state": "complete",
                        "detail": f"Completed {len(res.get('jobs', {}))} jobs in {res.get('total_seconds', 0)}s",
                        "total_seconds": res.get("total_seconds", 0)
                    }
            except Exception as e:
                log(f"Batch {batch_id} failed with error: {e}")
                with server_state.lock:
                    server_state.statuses[batch_id] = {
                        "batch_id": batch_id,
                        "state": "failed",
                        "detail": f"{type(e).__name__}: {str(e)[:500]}"
                    }
            finally:
                with server_state.lock:
                    server_state.current_batch = None
                server_state.queue.task_done()
        except Exception as ex:
            log("Worker loop error:", ex)
            time.sleep(1)


class GpuRequestHandler(BaseHTTPRequestHandler):
    def send_json(self, code, data):
        payload = json.dumps(data, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)

    def do_GET(self):
        url = urllib.parse.urlparse(self.path)
        qs = urllib.parse.parse_qs(url.query)

        if url.path in ("/", "/health"):
            free_gb = round(shutil.disk_usage(WORKSPACE_DIR).free / (1024**3), 1)
            total_gb = round(shutil.disk_usage(WORKSPACE_DIR).total / (1024**3), 1)
            data = {
                "status": "ok",
                "gpu": gpu.name if gpu else "cpu",
                "vram_gb": round(VRAM_GB, 1),
                "disk_free_gb": free_gb,
                "disk_total_gb": total_gb,
                "current_batch": server_state.current_batch,
                "queued_batches": server_state.queue.qsize()
            }
            self.send_json(200, data)
            return

        if url.path == "/status":
            batch_id = qs.get("batch_id", [None])[0]
            if not batch_id:
                data = {
                    "status": "ok",
                    "gpu": gpu.name if gpu else "cpu",
                    "vram_gb": round(VRAM_GB, 1),
                    "state": "idle" if not server_state.current_batch else "busy",
                    "current_batch": server_state.current_batch
                }
                self.send_json(200, data)
                return

            with server_state.lock:
                st = server_state.statuses.get(batch_id)

            if not st:
                # Check on disk if results.json exists
                out_dir = os.path.join(WORKSPACE_DIR, "batches", batch_id, "out")
                res_file = os.path.join(out_dir, "results.json")
                if os.path.exists(res_file):
                    try:
                        with open(res_file, encoding="utf-8") as f:
                            data = json.load(f)
                        st = {
                            "batch_id": batch_id,
                            "state": "complete" if "total_seconds" in data else "running",
                            "detail": f"Found on disk ({len(data.get('jobs', {}))} jobs)"
                        }
                    except Exception:
                        st = {"batch_id": batch_id, "state": "running", "detail": "Reading results..."}
                else:
                    st = {"batch_id": batch_id, "state": "failed", "detail": "Batch not found on worker (worker was restarted or batch does not exist)."}

            self.send_json(200, st)
            return

        if url.path == "/download":
            batch_id = qs.get("batch_id", [None])[0]
            if not batch_id:
                self.send_json(400, {"error": "Missing batch_id query parameter"})
                return

            batch_dir = os.path.join(WORKSPACE_DIR, "batches", batch_id)
            out_dir = os.path.join(batch_dir, "out")
            zip_path = os.path.join(batch_dir, f"output_{batch_id}.zip")

            if not os.path.exists(out_dir):
                self.send_json(404, {"error": f"Batch output {batch_id} not found"})
                return

            # Create zip archive of out_dir if not created or updated
            if not os.path.exists(zip_path):
                log(f"Zipping results for batch {batch_id}...")
                with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zf:
                    for root, _, files in os.walk(out_dir):
                        for file in files:
                            full = os.path.join(root, file)
                            rel = os.path.relpath(full, out_dir)
                            zf.write(full, rel)

            file_size = os.path.getsize(zip_path)
            self.send_response(200)
            self.send_header("Content-Type", "application/zip")
            self.send_header("Content-Length", str(file_size))
            self.send_header("Content-Disposition", f"attachment; filename=results_{batch_id}.zip")
            self.end_headers()
            with open(zip_path, "rb") as f:
                while chunk := f.read(65536):
                    self.wfile.write(chunk)
            return

        self.send_json(404, {"error": "Endpoint not found"})

    def do_POST(self):
        url = urllib.parse.urlparse(self.path)
        qs = urllib.parse.parse_qs(url.query)

        if url.path == "/submit":
            batch_id = qs.get("batch_id", [None])[0] or f"batch_{int(time.time())}"
            content_len = int(self.headers.get("Content-Length", 0))
            if content_len == 0:
                self.send_json(400, {"error": "Empty request body"})
                return

            batch_dir = os.path.join(WORKSPACE_DIR, "batches", batch_id)
            in_dir = os.path.join(batch_dir, "in")
            out_dir = os.path.join(batch_dir, "out")
            os.makedirs(in_dir, exist_ok=True)
            os.makedirs(out_dir, exist_ok=True)
            zip_path = os.path.join(batch_dir, "bundle.zip")

            log(f"Receiving batch {batch_id} ({content_len / 1024 / 1024:.1f} MB)...")
            remaining = content_len
            with open(zip_path, "wb") as f:
                while remaining > 0:
                    chunk = self.rfile.read(min(remaining, 65536))
                    if not chunk:
                        break
                    f.write(chunk)
                    remaining -= len(chunk)

            log(f"Extracting batch {batch_id} bundle...")
            with zipfile.ZipFile(zip_path, "r") as zf:
                zf.extractall(in_dir)

            with server_state.lock:
                server_state.statuses[batch_id] = {
                    "batch_id": batch_id,
                    "state": "queued",
                    "detail": "Received and queued for processing"
                }
            server_state.queue.put((batch_id, in_dir, out_dir))
            log(f"Batch {batch_id} queued successfully.")
            self.send_json(200, {"batch_id": batch_id, "state": "queued"})
            return

        self.send_json(404, {"error": "Endpoint not found"})


def run_server(port=8888):
    ports = [port]
    for p in (8888, 8000):
        if p not in ports:
            ports.append(p)

    log(f"Starting Film GPU Worker HTTP Server on ports: {ports}...")
    t = threading.Thread(target=worker_loop, daemon=True)
    t.start()

    servers = []
    for p in ports:
        for attempt in range(2):
            try:
                srv = ThreadedHTTPServer(("0.0.0.0", p), GpuRequestHandler)
                servers.append(srv)
                log(f"Server is ready and listening on http://0.0.0.0:{p}")
                if len(servers) > 1:
                    th = threading.Thread(target=srv.serve_forever, daemon=True)
                    th.start()
                break
            except Exception as e:
                log(f"Port {p} busy or error (attempt {attempt+1}): {e}")
                if attempt == 0 and p == 8888:
                    try:
                        subprocess.run(["supervisorctl", "stop", "jupyter"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
                        subprocess.run(["fuser", "-k", "8888/tcp"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
                        time.sleep(1)
                    except Exception:
                        pass

    if servers:
        try:
            servers[0].serve_forever()
        except KeyboardInterrupt:
            log("Server shutting down...")
            for s in servers:
                s.server_close()


# ─────────────────────────────── Entry Point ───────────────────────────────
WORKSPACE_DIR = os.environ.get("WORKSPACE_DIR", "/workspace" if os.path.isdir("/workspace") else os.getcwd())

if __name__ == "__main__":
    is_server = "--server" in sys.argv or os.environ.get("RUNPOD_POD_ID") is not None
    port = 8000
    for idx, arg in enumerate(sys.argv):
        if arg == "--port" and idx + 1 < len(sys.argv):
            port = int(sys.argv[idx + 1])

    if is_server:
        run_server(port=port)
    else:
        # Standalone Kaggle or local CLI mode
        in_d = os.environ.get("IN_DIR") or os.path.dirname((glob.glob("/kaggle/input/**/jobs.json", recursive=True) or ["./jobs.json"])[0])
        out_d = os.environ.get("OUT_DIR", "/kaggle/working/out" if os.path.isdir("/kaggle/working") else "./out")
        process_batch(in_d, out_d)
