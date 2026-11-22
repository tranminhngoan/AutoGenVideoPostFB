#!/bin/bash
# ==============================================================================
# AI Film Factory — GPU environment audit (Phase 1). Read-only: installs nothing,
# upgrades nothing. Measures what the machine really does instead of trusting the
# provider listing (RTX PRO 6000 Max-Q: 300 W variant, clocks differ from the 600 W card).
#
#   bash gpu_env_audit.sh            -> /workspace/gpu_audit_<time>.txt (+ .json)
#   SKIP_DISK=1 bash gpu_env_audit.sh   (skip the 4 GB disk speed test)
# ==============================================================================
set -u
WS=${WORKSPACE_DIR:-/workspace}; [ -d "$WS" ] || WS=$(pwd)
TS=$(date +%Y%m%d_%H%M%S)
OUT="$WS/gpu_audit_$TS.txt"
JSON="$WS/gpu_audit_$TS.json"
exec > >(tee "$OUT") 2>&1

section() { echo; echo "===== $* ====="; }

section "Host"
date -u; uname -a
echo "CPU: $(lscpu 2>/dev/null | grep -m1 'Model name' | sed 's/.*: *//') | cores (nproc): $(nproc)"
free -g | head -2
cat /etc/os-release 2>/dev/null | grep PRETTY

section "GPU (nvidia-smi)"
nvidia-smi
nvidia-smi --query-gpu=name,driver_version,memory.total,memory.used,power.limit,power.max_limit,clocks.max.sm,clocks.max.mem,pcie.link.gen.max,pcie.link.gen.current,pcie.link.width.max,pcie.link.width.current,temperature.gpu,compute_cap --format=csv
echo "(PCIe link gen/width often reads low while idle — check again under load.)"
command -v nvcc >/dev/null && nvcc --version | tail -2 || echo "nvcc: not installed (fine: PyTorch wheels ship their own CUDA runtime)"

section "Disk"
df -h "$WS" / 2>/dev/null
du -sh "$WS/.cache/huggingface" 2>/dev/null || echo "HF cache: none yet"
if [ "${SKIP_DISK:-0}" != "1" ]; then
  F="$WS/.disk_speed_test"
  echo "Sequential write 4 GiB (direct I/O):"
  dd if=/dev/zero of="$F" bs=1M count=4096 oflag=direct 2>&1 | tail -1
  sync; echo 3 > /proc/sys/vm/drop_caches 2>/dev/null || true
  echo "Sequential read 4 GiB (direct I/O):"
  dd if="$F" of=/dev/null bs=1M iflag=direct 2>&1 | tail -1
  rm -f "$F"
fi

section "FFmpeg"
ffmpeg -hide_banner -version 2>/dev/null | head -1 || echo "ffmpeg: MISSING"
ffmpeg -hide_banner -encoders 2>/dev/null | grep -E "h264_nvenc|hevc_nvenc" || echo "no NVENC encoders in this ffmpeg build"

section "Python / PyTorch / CUDA measurements"
python3 - "$JSON" <<'PY'
import json, sys, time, importlib
rep = {"python": sys.version.split()[0]}
for m in ("torch", "torchvision", "diffusers", "transformers", "accelerate", "xformers", "flash_attn", "sageattention", "onnxruntime"):
    try:
        mod = importlib.import_module(m)
        rep[m] = getattr(mod, "__version__", "present")
    except Exception as e:
        rep[m] = None
try:
    import torch
    rep["torch_cuda_build"] = torch.version.cuda
    rep["cuda_available"] = torch.cuda.is_available()
    if torch.cuda.is_available():
        p = torch.cuda.get_device_properties(0)
        cap = torch.cuda.get_device_capability(0)
        archs = torch.cuda.get_arch_list()
        rep.update(gpu=p.name, compute_capability=f"{cap[0]}.{cap[1]}", total_vram_gib=round(p.total_memory / 2**30, 2),
                   sm_count=p.multi_processor_count, torch_arch_list=archs,
                   arch_supported=f"sm_{cap[0]}{cap[1]}" in archs, bf16_supported=torch.cuda.is_bf16_supported())
        # Real allocatable VRAM (1 GiB blocks until OOM)
        blocks = []
        try:
            while True:
                blocks.append(torch.empty(2**30, dtype=torch.uint8, device="cuda"))
        except RuntimeError:
            pass
        rep["allocatable_vram_gib"] = len(blocks)
        del blocks; torch.cuda.empty_cache()
        # Sustained bf16 matmul throughput (Max-Q power limit shows up here)
        a = torch.randn(8192, 8192, device="cuda", dtype=torch.bfloat16); b = torch.randn_like(a)
        for _ in range(5): a @ b
        torch.cuda.synchronize(); n = 50; t = time.time()
        for _ in range(n): a @ b
        torch.cuda.synchronize(); dt = time.time() - t
        rep["bf16_matmul_tflops"] = round(2 * 8192**3 * n / dt / 1e12, 1)
        # Device memory bandwidth (copy 4 GiB)
        x = torch.empty(2**30, dtype=torch.float32, device="cuda"); y = torch.empty_like(x)
        y.copy_(x); torch.cuda.synchronize(); t = time.time()
        for _ in range(10): y.copy_(x)
        torch.cuda.synchronize(); dt = time.time() - t
        rep["device_copy_gbps"] = round(2 * x.numel() * 4 * 10 / dt / 1e9, 1)
        # Host -> device (PCIe) bandwidth, pinned memory
        h = torch.empty(2**28, dtype=torch.float32).pin_memory(); torch.cuda.synchronize(); t = time.time()
        for _ in range(5): h.to("cuda", non_blocking=True)
        torch.cuda.synchronize(); dt = time.time() - t
        rep["h2d_pinned_gbps"] = round(h.numel() * 4 * 5 / dt / 1e9, 1)
        # SDPA backend check
        try:
            q = torch.randn(1, 16, 4096, 128, device="cuda", dtype=torch.bfloat16)
            torch.nn.functional.scaled_dot_product_attention(q, q, q); torch.cuda.synchronize()
            rep["sdpa_ok"] = True
        except Exception as e:
            rep["sdpa_ok"] = f"FAILED: {e}"
except Exception as e:
    rep["torch_error"] = f"{type(e).__name__}: {e}"
print(json.dumps(rep, indent=1))
json.dump(rep, open(sys.argv[1], "w"), indent=1)
if rep.get("cuda_available") and not rep.get("arch_supported"):
    print("!! This PyTorch build has no kernels for this GPU's architecture — do NOT run models until fixed.")
PY

section "LatentSync install (setup_lipsync.sh)"
LS=${LATENTSYNC_DIR:-$WS/LatentSync}
if [ -x "$LS/.venv/bin/python" ]; then
  "$LS/.venv/bin/python" -c "import torch;print('LatentSync venv torch', torch.__version__, torch.version.cuda, torch.cuda.get_device_capability(0), 'sm_120' in torch.cuda.get_arch_list())"
  ls -la "$LS/checkpoints" "$LS/checkpoints/auxiliary" 2>/dev/null
else
  echo "not installed ($LS)"
fi

echo; echo "Report: $OUT"; echo "JSON:   $JSON"
