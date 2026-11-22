#!/bin/bash
# ==============================================================================
# AI Film Factory — one-time, opt-in LatentSync 1.6 install for the lip-sync stage.
# Not called by start_vast.sh: run it by hand once, after gpu_env_audit.sh.
#
#  * Separate Python 3.10 venv in $LATENTSYNC_DIR/.venv — never touches the worker's
#    own Python (LatentSync pins diffusers 0.32 / transformers 4.48; Wan needs newer).
#  * Upstream pins torch 2.5.1+cu121, which has NO kernels for Blackwell (sm_120,
#    RTX PRO 6000 / RTX 50xx). This script installs a CUDA 12.8 torch instead and
#    keeps every other upstream pin. That combination is NOT verified by upstream —
#    the smoke test at the end is what decides whether it works.
#  * Downloads only what inference + SyncNet QC need (~6-7 GB), not the training files.
#
#   bash setup_lipsync.sh
#   TORCH_SPEC="torch==2.7.1 torchvision==0.22.1" TORCH_INDEX=https://download.pytorch.org/whl/cu128 bash setup_lipsync.sh
# ==============================================================================
set -euo pipefail
WS=${WORKSPACE_DIR:-/workspace}; [ -d "$WS" ] || WS=$(pwd)
LS=${LATENTSYNC_DIR:-$WS/LatentSync}
TORCH_SPEC=${TORCH_SPEC:-"torch==2.7.1 torchvision==0.22.1"}
TORCH_INDEX=${TORCH_INDEX:-https://download.pytorch.org/whl/cu128}
export HF_HOME=${HF_HOME:-$WS/.cache/huggingface}

FREE_GB=$(df -BG --output=avail "$WS" | tail -1 | tr -dc '0-9')
echo ">>> Free disk on $WS: ${FREE_GB} GB"
if [ "$FREE_GB" -lt 20 ]; then
  echo "!! Need >= 20 GB free (venv ~8 GB + checkpoints ~7 GB + temp). Free space or attach a bigger volume first."
  exit 1
fi

if [ ! -d "$LS/.git" ]; then
  git clone --depth 1 https://github.com/bytedance/LatentSync "$LS"
fi
cd "$LS"
echo ">>> LatentSync commit: $(git rev-parse HEAD)" | tee -a "$LS/INSTALL_LOG.txt"

command -v uv >/dev/null || python3 -m pip install -q uv
[ -x .venv/bin/python ] || uv venv --python 3.10 .venv
PY="$LS/.venv/bin/python"

echo ">>> torch for this GPU: $TORCH_SPEC from $TORCH_INDEX"
uv pip install --python "$PY" $TORCH_SPEC --index-url "$TORCH_INDEX"
grep -v -E '^(torch|torchvision)==|extra-index-url' requirements.txt > requirements.blackwell.txt
uv pip install --python "$PY" -r requirements.blackwell.txt
uv pip install --python "$PY" "huggingface_hub[cli]"
command -v ffmpeg >/dev/null || (apt-get update -qq && apt-get install -y -qq ffmpeg libgl1)

echo ">>> Checkpoints (ByteDance/LatentSync-1.6)"
"$LS/.venv/bin/huggingface-cli" download ByteDance/LatentSync-1.6 \
  latentsync_unet.pt whisper/tiny.pt auxiliary/syncnet_v2.model auxiliary/sfd_face.pth \
  --local-dir checkpoints

echo ">>> Smoke test 1: torch sees the GPU with native kernels"
"$PY" - <<'PY'
import torch
cap = torch.cuda.get_device_capability(0)
arch = f"sm_{cap[0]}{cap[1]}"
print("torch", torch.__version__, "cuda", torch.version.cuda, torch.cuda.get_device_name(0), arch)
assert arch in torch.cuda.get_arch_list(), f"{arch} missing from {torch.cuda.get_arch_list()}"
x = torch.randn(1024, 1024, device="cuda", dtype=torch.float16); (x @ x).sum().item()
print("CUDA kernels OK")
PY
"$PY" -c "import onnxruntime as o; print('onnxruntime providers:', o.get_available_providers())"

echo ">>> Smoke test 2: upstream demo clip (measures time + SyncNet score)"
mkdir -p "$WS/lipsync_smoke"
START=$(date +%s)
"$PY" -m scripts.inference --unet_config_path configs/unet/stage2_512.yaml \
  --inference_ckpt_path checkpoints/latentsync_unet.pt --inference_steps 20 --guidance_scale 1.5 \
  --enable_deepcache --video_path assets/demo1_video.mp4 --audio_path assets/demo1_audio.wav \
  --video_out_path "$WS/lipsync_smoke/demo1_out.mp4"
echo "LatentSync demo: $(( $(date +%s) - START )) s for $(ffprobe -v error -show_entries format=duration -of csv=p=0 "$WS/lipsync_smoke/demo1_out.mp4") s of video" | tee -a "$LS/INSTALL_LOG.txt"
"$PY" -m eval.eval_sync_conf --video_path "$WS/lipsync_smoke/demo1_out.mp4" | tee -a "$LS/INSTALL_LOG.txt" || \
  echo "!! SyncNet eval failed — lip sync still works, but QC will report NOT_MEASURED"

echo "=========================================================="
echo " LatentSync ready at $LS. Watch $WS/lipsync_smoke/demo1_out.mp4 before trusting it."
echo " The worker finds it automatically (LATENTSYNC_DIR=$LS)."
echo "=========================================================="
