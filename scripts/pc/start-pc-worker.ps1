# Máy tính làm "máy làm việc" cho tool: lấy bản code mới nhất, build và chạy bằng Docker, rồi cứ 5 phút kiểm tra bản mới.
# Máy chạy đúng bản như Railway thì nhận việc nặng; tắt máy (hoặc tắt cửa sổ này + chạy stop-pc-worker) thì Railway tự làm.
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot\..\..").Path
$work = Join-Path $env:USERPROFILE 'TramTinPcWorker'

function Say($text, $color = 'Gray') { Write-Host "[$(Get-Date -Format 'HH:mm:ss')] $text" -ForegroundColor $color }

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { Say 'Chưa cài Docker Desktop (https://www.docker.com/products/docker-desktop/). Cài xong mở Docker Desktop rồi chạy lại.' Red; exit 1 }
if (-not (Get-Command git -ErrorAction SilentlyContinue)) { Say 'Chưa cài Git (https://git-scm.com/download/win).' Red; exit 1 }
if (-not (Test-Path (Join-Path $repo '.env.pc'))) { Say "Chưa có file .env.pc trong $repo — làm theo docs\pc-worker.md bước 3." Red; exit 1 }

# a separate copy of the code for the worker, so it never touches the folder being edited
if (-not (Test-Path (Join-Path $work '.git'))) {
    $origin = (git -C $repo remote get-url origin).Trim()
    Say "Tải code về $work…"
    git clone --quiet $origin $work
}

function Start-Worker {
    Copy-Item (Join-Path $repo '.env.pc') (Join-Path $work '.env.pc') -Force
    $env:APP_VERSION = (git -C $work rev-parse HEAD).Trim()
    Say "Build và chạy bản $($env:APP_VERSION.Substring(0, 7)) (lần đầu mất 5–10 phút)…" Cyan
    docker compose -f (Join-Path $work 'docker\docker-compose.pc.yml') up -d --build
    if ($LASTEXITCODE -ne 0) { Say 'Docker chạy lỗi — xem thông báo ở trên (Docker Desktop đã mở chưa?).' Red }
    else { Say 'Máy tính đang làm việc nền. Để cửa sổ này mở để tự cập nhật; tắt máy thì Railway tự nhận lại việc.' Green }
}

git -C $work pull --ff-only --quiet
Start-Worker
while ($true) {
    Start-Sleep -Seconds 300
    try {
        git -C $work fetch --quiet origin main
        $local = (git -C $work rev-parse HEAD).Trim()
        $remote = (git -C $work rev-parse origin/main).Trim()
        if ($local -ne $remote) {
            Say 'Có bản code mới — cập nhật…' Yellow
            git -C $work pull --ff-only --quiet
            Start-Worker
        }
    } catch {
        Say "Kiểm tra bản mới lỗi: $($_.Exception.Message)" Yellow
    }
}
