#!/bin/bash
# ==============================================================================
# AI Film Factory — RunPod GPU Worker Startup Script
# Supports: NVIDIA RTX 3090 / RTX 4090 / A5000 (24GB VRAM)
# ==============================================================================
set -e

PORT=${1:-8888}
echo "=========================================================="
echo " Starting AI Film Factory GPU Worker on port $PORT"
echo "=========================================================="

# Free port 8888 (Jupyter) so RunPod HTTP proxy routes directly to our film worker
fuser -k 8888/tcp 2>/dev/null || pkill -9 -f jupyter 2>/dev/null || true

# Bulletproof storage: wipe leftover root cache & link /root/.cache -> /workspace/.cache
rm -rf /root/.cache /tmp/* 2>/dev/null || true
rm -rf /workspace/.cache/huggingface/hub/models--Wan-AI* 2>/dev/null || true
rm -rf /workspace/.cache/tmp/* 2>/dev/null || true
mkdir -p /workspace/.cache/huggingface/hub /workspace/.cache/torch /workspace/.cache/tmp
ln -sfn /workspace/.cache /root/.cache
export HF_HOME=/workspace/.cache/huggingface
export HF_HUB_CACHE=/workspace/.cache/huggingface/hub
export TORCH_HOME=/workspace/.cache/torch
export TMPDIR=/workspace/.cache/tmp

# Check GPU
nvidia-smi

# Check Python and CUDA
python3 -c "import torch; print('PyTorch:', torch.__version__, '| CUDA Available:', torch.cuda.is_available(), '| Device:', torch.cuda.get_device_name(0) if torch.cuda.is_available() else 'None')"
pip install -q -U tiktoken protobuf einops

# Start server
python3 film_gpu_worker.py --server --port "$PORT"
