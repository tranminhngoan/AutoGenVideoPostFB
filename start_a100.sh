#!/bin/bash
set -e
echo "=== KHOI DONG GPU WORKER CHO A100 SXM 80GB (WAN 2.1 14B) ==="
cd /workspace 2>/dev/null || cd /root

# Stop any conflicting process on port 8888
pkill -9 -f film_gpu_worker 2>/dev/null || true
fuser -k 8888/tcp 2>/dev/null || true
supervisorctl stop jupyter 2>/dev/null || true

# Download latest worker script
echo ">>> Tai ma nguon GPU worker moi nhat..."
curl -s -O https://raw.githubusercontent.com/tranminhngoan/AutoGenVideoPostFB/main/backend/src/NewsStudio.Infrastructure/FilmFactory/GpuWorker/film_gpu_worker.py

# Install packages
echo ">>> Kiem tra dependencies AI..."
pip install -q -U "diffusers>=0.35.0" "transformers>=4.49" "accelerate" "sentencepiece" "peft" "imageio" "imageio-ffmpeg" "spandrel" "ftfy" "tiktoken" "protobuf" "einops"

# Run in background
echo ">>> Khoi dong worker tren port 8888..."
nohup python3 film_gpu_worker.py --server --port 8888 > worker.log 2>&1 &

sleep 3
echo "=== HOAN TAT: Worker dang lang nghe tren port 8888! ==="
ps aux | grep film_gpu_worker | grep -v grep
