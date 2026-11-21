#!/bin/bash
set -e
echo "=== KHOI DONG GPU WORKER CHO A100 SXM 80GB (WAN 2.1 14B) ==="
cd /workspace 2>/dev/null || cd /root

# Dung Jupyter Lab hoac bat ky process nao chiem port 8888
supervisorctl stop jupyter 2>/dev/null || true
pkill -9 -f jupyter 2>/dev/null || true
pkill -9 -f film_gpu_worker 2>/dev/null || true
fuser -k 8888/tcp 2>/dev/null || true
lsof -ti:8888 | xargs -r kill -9 2>/dev/null || true

# Tai ma nguon GPU worker moi nhat
echo ">>> Tai ma nguon GPU worker moi nhat..."
curl -s -L -o film_gpu_worker.py https://raw.githubusercontent.com/tranminhngoan/AutoGenVideoPostFB/main/backend/src/NewsStudio.Infrastructure/FilmFactory/GpuWorker/film_gpu_worker.py

# Cai dat / nang cap thu vien AI
echo ">>> Kiem tra dependencies AI..."
pip install -q -U "diffusers>=0.35.0" "transformers>=4.49" "accelerate" "sentencepiece" "peft" "imageio" "imageio-ffmpeg" "spandrel" "ftfy" "tiktoken" "protobuf" "einops"

# Cai dat tu dong khoi dong Worker moi khi bat Pod (khong can vao Terminal nua)
if [ -d "/etc/supervisor/conf.d" ]; then
    cat << 'EOF' > /etc/supervisor/conf.d/film_gpu_worker.conf
[program:film_gpu_worker]
command=python3 /workspace/film_gpu_worker.py --server --port 8888
directory=/workspace
autostart=true
autorestart=true
stdout_logfile=/workspace/worker.log
stderr_logfile=/workspace/worker_err.log
priority=999
EOF
    # Tat tu dong khoi dong cua Jupyter de khong chiem port 8888
    for f in /etc/supervisor/conf.d/*jupyter*.conf; do
        if [ -f "$f" ]; then
            sed -i 's/autostart=true/autostart=false/g' "$f" 2>/dev/null || true
        fi
    done
    supervisorctl reread 2>/dev/null || true
    supervisorctl update 2>/dev/null || true
fi

# Fallback tu dong bat neu khoi dong shell
if ! grep -q "film_gpu_worker" /root/.bashrc 2>/dev/null; then
    echo "pgrep -f film_gpu_worker >/dev/null || nohup python3 /workspace/film_gpu_worker.py --server --port 8888 > /workspace/worker.log 2>&1 &" >> /root/.bashrc
fi

# Khoi dong worker ngam tren port 8888
echo ">>> Khoi dong worker tren port 8888..."
nohup python3 film_gpu_worker.py --server --port 8888 > worker.log 2>&1 &

echo ">>> Dang cho worker san sang..."
for i in {1..15}; do
    sleep 2
    if curl -s http://127.0.0.1:8888/health | grep -q '"status": "ok"'; then
        echo "=== HOAN TAT: Worker da khoi dong thanh cong tren A100 SXM! ==="
        curl -s http://127.0.0.1:8888/health
        exit 0
    fi
    echo "  (dang khoi dong he thong... giay $i)"
done

echo "=== Worker da chay ngam! Xem log bang lenh: tail -f worker.log ==="
ps aux | grep film_gpu_worker | grep -v grep
