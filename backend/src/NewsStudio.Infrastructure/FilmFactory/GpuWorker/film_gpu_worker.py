"""
Film GPU worker — runs on a free Kaggle GPU (T4 16 GB) or a rented GPU (e.g. RTX 4090 24 GB).

Input : a folder containing jobs.json (+ any input files it references, e.g. voice .wav).
Output: OUT_DIR/<job outputs> + OUT_DIR/results.json  {"jobs": {id: {"status", "seconds", "error", "out"}}}

Job types, processed stage by stage so each model is loaded once:
  image   : RealVisXL (SDXL) still frame, optional face reference (IP-Adapter Plus Face),
            then 2x Real-ESRGAN and a Lanczos fit to the target size -> sharp JPEG
  motion  : image -> short video clip. LTX-Video on 16 GB cards; Wan 2.2 when >= 24 GB
            (set MOTION_MODEL=wan to force).
Every job reports its own result; one failure never stops the batch.
"""
import gc
import glob
import json
import os
import subprocess
import sys
import time
import traceback

os.environ.setdefault("PYTORCH_CUDA_ALLOC_CONF", "expandable_segments:True")

IN_DIR = os.environ.get("IN_DIR") or os.path.dirname((glob.glob("/kaggle/input/**/jobs.json", recursive=True) or ["./jobs.json"])[0])
OUT_DIR = os.environ.get("OUT_DIR", "/kaggle/working/out" if os.path.isdir("/kaggle/working") else "./out")
os.makedirs(OUT_DIR, exist_ok=True)
RESULTS = os.path.join(OUT_DIR, "results.json")
results = {"jobs": {}, "env": {}}


def log(*a):
    print(time.strftime("%H:%M:%S"), *a, flush=True)


def save_results():
    with open(RESULTS, "w", encoding="utf-8") as f:
        json.dump(results, f, ensure_ascii=False, indent=1)


def pip(*pkgs):
    subprocess.run([sys.executable, "-m", "pip", "install", "-q", "-U", *pkgs], check=True)


t_start = time.time()
pip("diffusers>=0.35.0", "transformers>=4.49", "accelerate", "sentencepiece", "peft",
    "imageio", "imageio-ffmpeg", "spandrel", "ftfy")
import numpy as np
import torch
from PIL import Image

gpu = torch.cuda.get_device_properties(0)
VRAM_GB = gpu.total_memory / 2**30
results["env"] = {"gpu": gpu.name, "vram_gb": round(VRAM_GB, 1), "torch": torch.__version__}
log("GPU", gpu.name, f"{VRAM_GB:.1f} GB")
# Turing (T4) only has memory-efficient attention for fp16; Ampere+ is fine with bf16.
DTYPE = torch.bfloat16 if gpu.major >= 8 else torch.float16

with open(os.path.join(IN_DIR, "jobs.json"), encoding="utf-8") as f:
    spec = json.load(f)
jobs = spec["jobs"]
results["batch_id"] = spec.get("batch_id")


def path_of(name):
    """Inputs may come with the bundle or be produced earlier in this run."""
    for d in (OUT_DIR, IN_DIR):
        p = os.path.join(d, name)
        if os.path.exists(p):
            return p
    raise FileNotFoundError(name)


def run_stage(kind, handler):
    todo = [j for j in jobs if j["type"] == kind]
    if not todo:
        return
    log(f"== stage {kind}: {len(todo)} jobs")
    for j in todo:
        t = time.time()
        try:
            handler(j)
            results["jobs"][j["id"]] = {"status": "ok", "seconds": round(time.time() - t, 1), "out": j["out"]}
            log(f"  {j['id']} ok in {time.time() - t:.0f}s")
        except Exception as e:
            results["jobs"][j["id"]] = {"status": "failed", "seconds": round(time.time() - t, 1),
                                        "error": f"{type(e).__name__}: {e}"[:800]}
            log(f"  {j['id']} FAILED: {traceback.format_exc()[-1200:]}")
            torch.cuda.empty_cache()
        save_results()


# ─────────────────────────────── stage 1: still frames ───────────────────────────────
def image_stage():
    if not any(j["type"] == "image" for j in jobs):
        return
    from diffusers import StableDiffusionXLPipeline, DPMSolverMultistepScheduler
    import spandrel

    t = time.time()
    model_id = spec.get("image_model", "SG161222/RealVisXL_V5.0")
    try:
        pipe = StableDiffusionXLPipeline.from_pretrained(model_id, torch_dtype=torch.float16, variant="fp16")
    except Exception:
        pipe = StableDiffusionXLPipeline.from_pretrained(model_id, torch_dtype=torch.float16)
    pipe.scheduler = DPMSolverMultistepScheduler.from_config(pipe.scheduler.config, use_karras_sigmas=True,
                                                             algorithm_type="sde-dpmsolver++")
    pipe.to("cuda")
    pipe.vae.enable_tiling()
    has_ip = False
    if any(j.get("face_ref") for j in jobs if j["type"] == "image"):
        pipe.load_ip_adapter("h94/IP-Adapter", subfolder="sdxl_models",
                             weight_name="ip-adapter-plus-face_sdxl_vit-h.safetensors",
                             image_encoder_folder="models/image_encoder")
        has_ip = True
    import urllib.request
    esr = "/tmp/RealESRGAN_x2plus.pth"
    if not os.path.exists(esr):
        urllib.request.urlretrieve("https://github.com/xinntao/Real-ESRGAN/releases/download/v0.2.1/RealESRGAN_x2plus.pth", esr)
    upscaler = spandrel.ModelLoader().load_from_file(esr).model.cuda().eval().half()
    log(f"image models loaded in {time.time() - t:.0f}s")

    # SDXL's text encoders stop at 77 tokens, which cut off characters, lighting and style in long scene
    # descriptions. Long prompts are encoded in 75-token chunks (each wrapped in BOS/EOS) and concatenated.
    def chunk_ids(tok, text, n_chunks=None):
        ids = tok(text, truncation=False, add_special_tokens=False).input_ids
        chunks = [ids[i:i + 75] for i in range(0, len(ids), 75)] or [[]]
        while n_chunks and len(chunks) < n_chunks:
            chunks.append([])
        pad = tok.pad_token_id if tok.pad_token_id is not None else tok.eos_token_id
        return [[tok.bos_token_id] + c + [tok.eos_token_id] + [pad] * (75 - len(c)) for c in chunks]

    @torch.no_grad()
    def encode_long(text, n_chunks):
        parts, pooled = [], None
        for tok, enc, second in ((pipe.tokenizer, pipe.text_encoder, False), (pipe.tokenizer_2, pipe.text_encoder_2, True)):
            outs = []
            for i, c in enumerate(chunk_ids(tok, text, n_chunks)[:n_chunks]):
                o = enc(torch.tensor([c], device="cuda"), output_hidden_states=True)
                outs.append(o.hidden_states[-2])
                if second and i == 0:
                    pooled = o[0]
            parts.append(torch.cat(outs, dim=1))
        return torch.cat(parts, dim=-1), pooled

    def upscale(img):
        x = torch.from_numpy(np.asarray(img, dtype=np.float32) / 255.0).permute(2, 0, 1)[None].cuda().half()
        with torch.no_grad():
            y = upscaler(x).clamp(0, 1)
        return Image.fromarray((y[0].permute(1, 2, 0).float().cpu().numpy() * 255).round().astype(np.uint8))

    def handle(j):
        w, h = j.get("width", 768), j.get("height", 1344)
        kwargs = {}
        if has_ip:
            if j.get("face_ref"):
                pipe.set_ip_adapter_scale(j.get("face_strength", 0.6))
                kwargs["ip_adapter_image"] = Image.open(path_of(j["face_ref"])).convert("RGB")
            else:
                pipe.set_ip_adapter_scale(0.0)
                kwargs["ip_adapter_image"] = Image.new("RGB", (224, 224))
        neg = j.get("negative", "blurry, lowres, deformed face, bad anatomy, extra fingers, text, watermark, cartoon, painting, 3d render")
        # prompt and negative must have the same number of chunks
        n = max(len(chunk_ids(pipe.tokenizer, j["prompt"])), len(chunk_ids(pipe.tokenizer, neg)))
        pe, pp = encode_long(j["prompt"], n)
        ne, npool = encode_long(neg, n)
        img = pipe(prompt_embeds=pe, pooled_prompt_embeds=pp, negative_prompt_embeds=ne, negative_pooled_prompt_embeds=npool,
                   width=w, height=h, num_inference_steps=j.get("steps", 30), guidance_scale=j.get("cfg", 5.0),
                   generator=torch.Generator("cuda").manual_seed(j.get("seed", 1)), **kwargs).images[0]
        big = upscale(img)
        tw, th = j.get("target_width", 1080), j.get("target_height", 1920)
        # cover-fit then centre-crop to the exact target size
        scale = max(tw / big.width, th / big.height)
        big = big.resize((round(big.width * scale), round(big.height * scale)), Image.LANCZOS)
        left, top = (big.width - tw) // 2, (big.height - th) // 2
        big.crop((left, top, left + tw, top + th)).save(os.path.join(OUT_DIR, j["out"]), quality=95)

    run_stage("image", handle)
    del pipe, upscaler
    gc.collect(); torch.cuda.empty_cache()


# ─────────────────────────────── stage 2: motion clips ───────────────────────────────
def motion_stage():
    if not any(j["type"] == "motion" for j in jobs):
        return
    from diffusers.utils import export_to_video
    use_wan = os.environ.get("MOTION_MODEL", "wan" if VRAM_GB >= 23 else "ltx") == "wan"
    t = time.time()
    if use_wan:
        from diffusers import WanImageToVideoPipeline
        pipe = WanImageToVideoPipeline.from_pretrained(spec.get("wan_model", "Wan-AI/Wan2.2-I2V-A14B-Diffusers"),
                                                       torch_dtype=torch.bfloat16)
        pipe.enable_model_cpu_offload()
        fps, w, h, steps, cfg = 16, 480, 832, 40, 3.5
    else:
        from diffusers import LTXImageToVideoPipeline
        pipe = LTXImageToVideoPipeline.from_pretrained(spec.get("ltx_model", "Lightricks/LTX-Video-0.9.5"), torch_dtype=DTYPE)
        pipe.enable_model_cpu_offload()
        pipe.vae.enable_tiling()
        fps, w, h, steps, cfg = 24, 480, 832, 40, 3.0
    results["env"]["motion_model"] = "wan2.2-a14b" if use_wan else "ltx-0.9.5"
    log(f"motion model ({results['env']['motion_model']}) loaded in {time.time() - t:.0f}s")

    def handle(j):
        jw, jh = j.get("width", w), j.get("height", h)
        image = Image.open(path_of(j["image"])).convert("RGB").resize((jw, jh), Image.LANCZOS)
        seconds = min(float(j.get("seconds", 4)), 5.0)
        frames = int(seconds * fps)
        frames = frames - (frames - 1) % (4 if use_wan else 8)  # model frame-count rule: 4k+1 / 8k+1
        out = pipe(image=image, prompt=j["prompt"],
                   negative_prompt=j.get("negative", "worst quality, blurry, jittery, distorted face, deformed face, morphing face, open mouth, screaming, grimace, deformed, extra limbs, morphing, text, watermark"),
                   width=jw, height=jh, num_frames=frames, num_inference_steps=j.get("steps", steps), guidance_scale=cfg,
                   generator=torch.Generator("cuda").manual_seed(j.get("seed", 42))).frames[0]
        export_to_video(out, os.path.join(OUT_DIR, j["out"]), fps=fps)

    run_stage("motion", handle)
    del pipe
    gc.collect(); torch.cuda.empty_cache()


image_stage()
motion_stage()
results["total_seconds"] = round(time.time() - t_start)
save_results()
log("DONE", results["total_seconds"], "s")
