# Cài bộ phân tích video của "AI Video Studio" trên máy tính (một lần). Sau khi cài, phân tích chạy hoàn toàn trên máy:
# không gọi API trả phí, không GPU cloud. Mọi file tải về đều cố định phiên bản và kiểm tra SHA-256.
#
#   scripts\pc\setup-ai-studio.bat            → model 4B (mặc định, cân bằng) + nghe lời nói (whisper)
#   scripts\pc\setup-ai-studio.bat -With8B    → thêm model 8B (chính xác hơn, chậm ~4 lần, thêm ~5,8 GB)
#   scripts\pc\setup-ai-studio.bat -NoWhisper → bỏ phần nghe lời nói (~0,5 GB)
#
# Thành phần (giấy phép):
#   llama.cpp b12703 (MIT) — chạy model trên card đồ hoạ qua Vulkan, tự lùi về CPU
#   Qwen3-VL 4B / 8B Instruct GGUF Q4_K_M (Apache-2.0, bản chính thức của Qwen trên Hugging Face)
#   whisper.cpp b5907 + ggml-small (MIT)
#   OpenCV (Apache-2.0), NumPy (BSD) trong môi trường Python riêng
#
# Thư mục: D:\TramTinAI (hoặc %USERPROFILE%\TramTinAI khi không có ổ D:), đổi bằng biến AI_STUDIO_HOME.
param([switch]$With8B, [switch]$NoWhisper)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue' # Invoke-WebRequest is many times slower with the progress bar
function Say($text, $color = 'Gray') { Write-Host "[$(Get-Date -Format 'HH:mm:ss')] $text" -ForegroundColor $color }
# native programs (pip, python) write warnings to stderr: never treat those as failures, only the exit code
function Run-Native { $ErrorActionPreference = 'Continue'; & $args[0] @($args | Select-Object -Skip 1) 2>&1 | ForEach-Object { "$_" } | Where-Object { $_ -notmatch '^\s*$' } | Out-Host }

$home_ = if ($env:AI_STUDIO_HOME) { $env:AI_STUDIO_HOME } elseif (Test-Path 'D:\') { 'D:\TramTinAI' } else { Join-Path $env:USERPROFILE 'TramTinAI' }
$venv = Join-Path $home_ 'venv'
$llm = Join-Path $home_ 'llm'
$whisper = Join-Path $home_ 'whisper'
New-Item -ItemType Directory -Force $home_, $llm, (Join-Path $llm 'models'), (Join-Path $llm 'dl'), (Join-Path $home_ 'work') | Out-Null
Say "Thư mục: $home_" Cyan
$drive = (Get-Item $home_).PSDrive
$needGb = 4 + $(if ($With8B) { 6 } else { 0 }) + $(if ($NoWhisper) { 0 } else { 1 })
if ($drive.Free / 1GB -lt $needGb) { Say "Ổ $($drive.Name): chỉ còn $([math]::Round($drive.Free / 1GB, 1)) GB — cần khoảng $needGb GB." Red; exit 1 }

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
function Get-Checked($url, $path, $sha) {
    $ok = (Test-Path $path) -and ((Get-FileHash $path -Algorithm SHA256).Hash.ToLower() -eq $sha)
    if ($ok) { Say "  ✓ $(Split-Path $path -Leaf) (đã có)" Green; return }
    Say "Tải $(Split-Path $path -Leaf)…"
    Invoke-WebRequest -Uri $url -OutFile "$path.part" -UseBasicParsing
    $hash = (Get-FileHash "$path.part" -Algorithm SHA256).Hash.ToLower()
    if ($hash -ne $sha) { Remove-Item "$path.part" -Force; Say "Sai SHA-256 cho $(Split-Path $path -Leaf) — không dùng file này." Red; exit 1 }
    Move-Item "$path.part" $path -Force
    Say "  ✓ $(Split-Path $path -Leaf)" Green
}

# 1) Python 3.10+ (đã có trên máy cho video tin tức) + OpenCV / NumPy trong venv riêng
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
Say 'Cài thư viện (opencv-python-headless, numpy)…'
Run-Native $venvPython -m pip install --disable-pip-version-check -q --upgrade pip
Run-Native $venvPython -m pip install --disable-pip-version-check -q 'opencv-python-headless==4.10.0.84' 'numpy==2.1.3'
if ($LASTEXITCODE -ne 0) { Say 'Cài thư viện lỗi — xem thông báo ở trên.' Red; exit 1 }

# 2) llama.cpp (Vulkan: card đồ hoạ có sẵn, kể cả GTX 1050 Ti với driver cũ; CPU: dự phòng)
$rel = 'https://github.com/ggml-org/llama.cpp/releases/download/b12703'
$builds = @(
    @{ Name = 'llama-b12703-bin-win-vulkan-x64.zip'; Sha = '7490d45c07ef4d2d17e516ccf85faf3830797ad9eb7875f83336b178da6bbe2a'; Dir = 'bin-vulkan' },
    @{ Name = 'llama-b12703-bin-win-cpu-x64.zip'; Sha = '7e4be77eff28955232ce676a91e05d08a34069bf44d0c98d6ca96ceae8bbc142'; Dir = 'bin-cpu' }
)
foreach ($b in $builds) {
    $zip = Join-Path $llm "dl\$($b.Name)"
    Get-Checked "$rel/$($b.Name)" $zip $b.Sha
    $dir = Join-Path $llm $b.Dir
    if (-not (Test-Path (Join-Path $dir 'llama-server.exe'))) {
        New-Item -ItemType Directory -Force $dir | Out-Null
        Expand-Archive -Path $zip -DestinationPath $dir -Force
    }
}

# 3) Qwen3-VL (Apache-2.0) — bản GGUF chính thức của Qwen
$hf = 'https://huggingface.co/Qwen'
$models = @(
    @{ Repo = 'Qwen3-VL-4B-Instruct-GGUF'; File = 'Qwen3VL-4B-Instruct-Q4_K_M.gguf'; Sha = '66358cb18bb6b3b1b6675aa412c7a88ef01d228f481184d13668e5201c730a0a' },
    @{ Repo = 'Qwen3-VL-4B-Instruct-GGUF'; File = 'mmproj-Qwen3VL-4B-Instruct-Q8_0.gguf'; Sha = '30ba2c7dd3127a4561b6cba9d13d0f711c91bdb38742e2f56d73c8cb596bd06d' }
)
if ($With8B) {
    $models += @{ Repo = 'Qwen3-VL-8B-Instruct-GGUF'; File = 'Qwen3VL-8B-Instruct-Q4_K_M.gguf'; Sha = '67d1659bfe71b89d50b45a4ad1a9e5b997e5bb16ce5da66a6a6167abd569e9e2' }
    $models += @{ Repo = 'Qwen3-VL-8B-Instruct-GGUF'; File = 'mmproj-Qwen3VL-8B-Instruct-Q8_0.gguf'; Sha = 'c6ba85508d82f42590e6eb77d5340369ab6fecf107a7561d809523d8aa5f3bfd' }
}
foreach ($m in $models) { Get-Checked "$hf/$($m.Repo)/resolve/main/$($m.File)" (Join-Path $llm "models\$($m.File)") $m.Sha }

# 4) whisper.cpp (MIT) — nghe lời nói trong video để AI hiểu cảnh hơn (không bắt buộc)
if (-not $NoWhisper) {
    New-Item -ItemType Directory -Force $whisper, (Join-Path $whisper 'dl'), (Join-Path $whisper 'bin') | Out-Null
    $wzip = Join-Path $whisper 'dl\whisper-bin-x64.zip'
    Get-Checked 'https://github.com/ggml-org/whisper.cpp/releases/download/b5907/whisper-bin-x64.zip' $wzip 'ecca07b277d943e9b9f4712856a144b857271a6b333104e511e1988e80db582b'
    if (-not (Test-Path (Join-Path $whisper 'bin\whisper-cli.exe'))) {
        $tmp = Join-Path $whisper 'dl\x'
        Expand-Archive -Path $wzip -DestinationPath $tmp -Force
        Copy-Item (Join-Path $tmp 'Release\*') (Join-Path $whisper 'bin') -Recurse -Force
        Remove-Item $tmp -Recurse -Force
    }
    Get-Checked 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin' (Join-Path $whisper 'ggml-small.bin') '1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b'
}

# 5) Tự kiểm tra
$engine = Join-Path (Resolve-Path "$PSScriptRoot\..\..").Path 'backend\src\NewsStudio.Infrastructure\Scripts\studio_analyzer.py'
Say 'Tự kiểm tra bộ phân tích…'
Run-Native $venvPython -I $engine probe --home $home_
if ($LASTEXITCODE -ne 0) { Say 'Bộ phân tích chưa chạy được — xem thông báo ở trên.' Red; exit 1 }
Say 'Đo tốc độ model 4B trên máy (khoảng 30 giây)…'
Run-Native $venvPython -I $engine bench --home $home_ --model 4b
Say 'Xong. Chạy lại scripts\pc\start-pc-worker.bat (nếu đang mở thì nó tự nhận bộ phân tích trong vòng vài phút).' Green
