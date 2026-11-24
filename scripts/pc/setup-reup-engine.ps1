# Cài "Reup Video" engine trên máy tính (một lần): Python venv + ONNX Runtime + OpenCV + các model miễn phí được phép
# dùng thương mại. Sau khi cài, xử lý video chạy hoàn toàn offline (không gọi API, không GPU cloud).
#
#   scripts\pc\setup-reup-engine.bat            → CPU (mặc định)
#   scripts\pc\setup-reup-engine.bat -DirectML  → dùng thêm card đồ hoạ sẵn có trong máy (GTX/RTX/Radeon) qua DirectML
#
# Thư mục: D:\TramTinAI (hoặc %USERPROFILE%\TramTinAI khi không có ổ D:), đổi bằng biến REUP_HOME.
param([switch]$DirectML)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue' # Invoke-WebRequest is many times slower with the progress bar
function Say($text, $color = 'Gray') { Write-Host "[$(Get-Date -Format 'HH:mm:ss')] $text" -ForegroundColor $color }
# native programs (pip, python) write warnings to stderr: never treat those as failures, only the exit code
function Run-Native { $ErrorActionPreference = 'Continue'; & $args[0] @($args | Select-Object -Skip 1) 2>&1 | ForEach-Object { "$_" } | Where-Object { $_ -notmatch '^\s*$' } | Out-Host }

$home_ = if ($env:REUP_HOME) { $env:REUP_HOME } elseif (Test-Path 'D:\') { 'D:\TramTinAI' } else { Join-Path $env:USERPROFILE 'TramTinAI' }
$venv = Join-Path $home_ 'venv'
$models = Join-Path $home_ 'models'
$licensed = Join-Path $models 'licensed'
New-Item -ItemType Directory -Force $home_, $models, $licensed, (Join-Path $home_ 'work') | Out-Null
Say "Thư mục engine: $home_" Cyan

# 1) Python 3.10+ (đã có trên máy cho video tin tức)
$pyExe = $null; $pyArgs = @()
foreach ($c in @(@('py', '-3'), @('python'), @('python3'))) {
    try {
        $rest = @($c | Select-Object -Skip 1)
        $v = & $c[0] @rest --version 2>&1
        if ("$v" -match 'Python 3\.(\d+)' -and [int]$Matches[1] -ge 10) { $pyExe = $c[0]; $pyArgs = $rest; break }
    } catch { }
}
if (-not $pyExe) { Say 'Chưa có Python 3.10+ (https://www.python.org/downloads/windows/ — tick "Add python.exe to PATH").' Red; exit 1 }

$venvPython = Join-Path $venv 'Scripts\python.exe'
if (-not (Test-Path $venvPython)) {
    Say 'Tạo môi trường Python riêng (venv)…'
    Run-Native $pyExe @pyArgs -m venv $venv
    if ($LASTEXITCODE -ne 0) { Say 'Không tạo được venv.' Red; exit 1 }
}

# 2) Thư viện (phiên bản cố định; tất cả MIT / Apache-2.0 / BSD)
$runtime = if ($DirectML) { 'onnxruntime-directml==1.20.1' } else { 'onnxruntime==1.20.1' }
$other = if ($DirectML) { 'onnxruntime' } else { 'onnxruntime-directml' }
Say "Cài thư viện ($runtime, opencv-python-headless, numpy, onnx)…"
Run-Native $venvPython -m pip install --disable-pip-version-check -q --upgrade pip
$ErrorActionPreference = 'Continue'; & $venvPython -m pip uninstall -y -q $other 2>&1 | Out-Null; $ErrorActionPreference = 'Stop'
Run-Native $venvPython -m pip install --disable-pip-version-check -q $runtime 'opencv-python-headless==4.10.0.84' 'numpy==2.1.3' 'onnx==1.17.0'
if ($LASTEXITCODE -ne 0) { Say 'Cài thư viện lỗi — xem thông báo ở trên.' Red; exit 1 }

# 3) Model miễn phí, được phép dùng thương mại (tải một lần, kiểm tra SHA-256)
$list = @(
    @{ Name = 'face_detection_yunet_2023mar.onnx'; Url = 'https://github.com/opencv/opencv_zoo/raw/main/models/face_detection_yunet/face_detection_yunet_2023mar.onnx'; Sha = '8f2383e4dd3cfbb4553ea8718107fc0423210dc964f9f4280604804ed2552fa4'; License = 'MIT (OpenCV Zoo YuNet)' },
    @{ Name = 'face_recognition_sface_2021dec.onnx'; Url = 'https://github.com/opencv/opencv_zoo/raw/main/models/face_recognition_sface/face_recognition_sface_2021dec.onnx'; Sha = '0ba9fbfa01b5270c96627c4ef784da859931e02f04419c829e83484087c34e79'; License = 'Apache-2.0 (OpenCV Zoo SFace)' },
    @{ Name = 'rvm_mobilenetv3_fp32.onnx'; Url = 'https://github.com/PeterL1n/RobustVideoMatting/releases/download/v1.0.0/rvm_mobilenetv3_fp32.onnx'; Sha = '88d4531297118f595bf2fd60f6f566aec2e559393802d1f436c380f0cbbd2828'; License = 'GPL-3.0 (Robust Video Matting) — dùng nội bộ, không phân phối lại' }
)
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
foreach ($m in $list) {
    $path = Join-Path $models $m.Name
    $ok = (Test-Path $path) -and ((Get-FileHash $path -Algorithm SHA256).Hash.ToLower() -eq $m.Sha)
    if (-not $ok) {
        Say "Tải $($m.Name)…"
        Invoke-WebRequest -Uri $m.Url -OutFile "$path.part" -UseBasicParsing
        $hash = (Get-FileHash "$path.part" -Algorithm SHA256).Hash.ToLower()
        if ($hash -ne $m.Sha) { Remove-Item "$path.part" -Force; Say "Sai SHA-256 cho $($m.Name) — không dùng file này." Red; exit 1 }
        Move-Item "$path.part" $path -Force
    }
    Say "  ✓ $($m.Name) — $($m.License)" Green
}

# 4) Model thay khuôn mặt: KHÔNG tự tải (các model phổ biến chỉ cho dùng phi thương mại)
$readme = Join-Path $licensed 'DOC-TRUOC.txt'
@"
Model thay khuôn mặt (Reup Video › Thay mặt)

Các model thay mặt phổ biến (inswapper_128, arcface w600k_r50 / buffalo_l của InsightFace, HyperSwap, SimSwap…) chỉ cho
dùng PHI THƯƠNG MẠI. Đăng Facebook có kiếm tiền là dùng thương mại, nên tool không tự tải các model này.

Khi đã có GIẤY PHÉP THƯƠNG MẠI (ví dụ mua của InsightFace: https://www.insightface.ai/services/models-commercial-licensing):
  1. Đặt 2 file vào thư mục này:
       swapper.onnx   — model thay mặt kiểu inswapper (đầu vào 128x128 + vector 512)
       identity.onnx  — model nhận dạng ArcFace đi kèm (đầu vào 112x112, ra vector 512)
  2. Trang web › Reup Video › Cài đặt › "Model thay mặt": ghi số / tên giấy phép và tick xác nhận.
Không có 2 bước này thì tool vẫn tách nền, thay cảnh, xử lý âm thanh bình thường; phần thay mặt bị khoá.
"@ | Set-Content -Path $readme -Encoding UTF8

# 5) Tự kiểm tra
$engine = Join-Path (Resolve-Path "$PSScriptRoot\..\..").Path 'backend\src\NewsStudio.Infrastructure\Scripts\reup_engine.py'
Say 'Tự kiểm tra engine…'
Run-Native $venvPython -I $engine probe --home $home_
if ($LASTEXITCODE -ne 0) { Say 'Engine chưa chạy được — xem thông báo ở trên.' Red; exit 1 }
Say 'Xong. Chạy lại scripts\pc\start-pc-worker.bat (nếu đang mở thì nó tự nhận engine trong vòng vài phút).' Green
