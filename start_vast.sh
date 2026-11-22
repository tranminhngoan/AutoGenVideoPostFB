#!/bin/bash
# ==============================================================================
# AI Film Factory — Vast.ai GPU Worker Startup Script
# Optimized for: NVIDIA RTX 3090 / RTX 4090 / A100 / H100
# Pricing on Vast.ai: ~$0.20 - $0.35/hr (RTX 3090/4090), ~$1.00/hr (A100)
# ==============================================================================
set -e

PORT=${1:-8888}
echo "=========================================================="
echo " Starting AI Film Factory GPU Worker on Vast.ai (Port: $PORT)"
echo "=========================================================="

cd /workspace 2>/dev/null || cd /root 2>/dev/null || cd ~

# 1. Giải phóng cổng 8888 nếu có tiến trình khác đang chiếm giữ
echo ">>> Giải phóng cổng $PORT..."
fuser -k ${PORT}/tcp 2>/dev/null || true
pkill -9 -f film_gpu_worker 2>/dev/null || true
pkill -9 -f jupyter 2>/dev/null || true
sleep 1

# 2. Cấu hình thư mục bộ nhớ đệm (Cache) để không làm đầy ổ đĩa hệ thống
CACHE_DIR="/workspace/.cache"
if [ ! -d "/workspace" ]; then
    CACHE_DIR="/root/.cache"
fi
mkdir -p "$CACHE_DIR/huggingface/hub" "$CACHE_DIR/torch" "$CACHE_DIR/tmp"
export HF_HOME="$CACHE_DIR/huggingface"
export HF_HUB_CACHE="$CACHE_DIR/huggingface/hub"
export TORCH_HOME="$CACHE_DIR/torch"
export TMPDIR="$CACHE_DIR/tmp"
export WORKSPACE_DIR="$(pwd)"

# 3. Kiểm tra GPU & VRAM
echo ">>> Kiểm tra phần cứng GPU:"
nvidia-smi --query-gpu=name,memory.total,driver_version --format=csv,noheader || nvidia-smi || true

# 4. Tải file mã nguồn GPU Worker mới nhất từ Railway API hoặc GitHub
echo ">>> Đồng bộ mã nguồn film_gpu_worker.py mới nhất..."
if curl -fsSL -m 10 "https://postnewsauto.up.railway.app/api/film-factory/gpu/worker-script" -o film_gpu_worker.py 2>/dev/null; then
    echo "  (Tải thành công từ Railway API)"
else
    echo "  (Đang tải từ GitHub repository fallback...)"
    curl -fsSL -L "https://raw.githubusercontent.com/tranminhngoan/AutoGenVideoPostFB/main/backend/src/NewsStudio.Infrastructure/FilmFactory/GpuWorker/film_gpu_worker.py" -o film_gpu_worker.py
fi

# 5. Cài đặt các thư viện AI cần thiết
echo ">>> Kiểm tra và cài đặt thư viện AI..."
pip install -q -U "diffusers>=0.35.0" "transformers>=4.49" "accelerate" "sentencepiece" "peft" "imageio" "imageio-ffmpeg" "spandrel" "ftfy" "tiktoken" "protobuf" "einops"

# 6. Thiết lập tự động khởi động lại nếu Instance bị tắt rồi bật lại
if [ -d "/etc/supervisor/conf.d" ]; then
    cat << EOF > /etc/supervisor/conf.d/film_gpu_worker.conf
[program:film_gpu_worker]
command=python3 $(pwd)/film_gpu_worker.py --server --port $PORT
directory=$(pwd)
autostart=true
autorestart=true
stdout_logfile=$(pwd)/worker.log
stderr_logfile=$(pwd)/worker_err.log
priority=999
EOF
    supervisorctl reread 2>/dev/null || true
    supervisorctl update 2>/dev/null || true
fi

# Fallback tự bật khi mở shell nếu supervisor không có
if ! grep -q "film_gpu_worker" ~/.bashrc 2>/dev/null; then
    echo "pgrep -f film_gpu_worker >/dev/null || nohup python3 $(pwd)/film_gpu_worker.py --server --port $PORT > $(pwd)/worker.log 2>&1 &" >> ~/.bashrc
fi

# 7. Khởi động Worker chạy ngầm
echo ">>> Khởi chạy film_gpu_worker.py trên cổng $PORT..."
nohup python3 film_gpu_worker.py --server --port "$PORT" > worker.log 2>&1 &

# 8. Chờ kiểm tra trạng thái Health Check
echo ">>> Chờ worker khởi động xong..."
READY=0
for i in {1..20}; do
    sleep 2
    if curl -s "http://127.0.0.1:${PORT}/health" 2>/dev/null | grep -q '"status": "ok"'; then
        READY=1
        break
    fi
    echo "  (Đang khởi tạo môi trường... ${i}s)"
done

if [ $READY -eq 1 ]; then
    echo "=========================================================="
    echo " AI FILM FACTORY WORKER SẴN SÀNG TRÊN VAST.AI!"
    echo "=========================================================="
    curl -s "http://127.0.0.1:${PORT}/health"
    echo ""
    echo "Worker đang chạy ngầm. Theo dõi tiến trình: tail -f worker.log"
else
    echo "Worker đã được kích hoạt ngầm. Xem chi tiết khởi động: tail -n 20 worker.log"
fi
