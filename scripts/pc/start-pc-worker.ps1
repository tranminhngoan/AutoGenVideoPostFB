# Máy tính làm "máy làm việc" cho tool: lấy bản code mới nhất, build và chạy thẳng trên Windows (.NET 8, không cần Docker),
# rồi cứ 5 phút kiểm tra bản mới. Máy chạy đúng bản như Railway thì nhận việc nặng; tắt máy (hoặc chạy stop-pc-worker)
# thì khoảng 1 phút sau Railway tự làm.
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot\..\..").Path
$work = Join-Path $env:USERPROFILE 'TramTinPcWorker'
$bin = Join-Path $env:USERPROFILE 'TramTinPcWorker-bin'
$data = Join-Path $env:USERPROFILE 'TramTinPcWorker-data'
$pidFile = Join-Path $data 'worker.pid'
$envFile = Join-Path $repo '.env.pc'

function Say($text, $color = 'Gray') { Write-Host "[$(Get-Date -Format 'HH:mm:ss')] $text" -ForegroundColor $color }

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Say 'Chưa cài .NET 8 (https://dotnet.microsoft.com/download/dotnet/8.0 → SDK).' Red; exit 1 }
if (-not (Get-Command git -ErrorAction SilentlyContinue)) { Say 'Chưa cài Git (https://git-scm.com/download/win).' Red; exit 1 }
if (-not (Test-Path $envFile)) { Say "Chưa có file .env.pc trong $repo — làm theo docs\pc-worker.md." Red; exit 1 }
New-Item -ItemType Directory -Force $data, $bin | Out-Null

# .env.pc → biến môi trường cho chương trình. Chấp nhận dán thẳng từ Railway "Raw Editor":
# bỏ dòng ${{...}} (máy tính không hiểu), PORT, RAILWAY_*; DATABASE_PUBLIC_URL (nếu có) thay cho DATABASE_URL.
function Read-EnvPc {
    $vars = [ordered]@{}
    foreach ($raw in Get-Content $envFile -Encoding UTF8) {
        $line = $raw.Trim()
        if ($line -eq '' -or $line.StartsWith('#') -or $line -notmatch '^([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$') { continue }
        $name = $Matches[1]; $value = $Matches[2].Trim()
        if (($value.StartsWith('"') -and $value.EndsWith('"') -and $value.Length -ge 2) -or ($value.StartsWith("'") -and $value.EndsWith("'") -and $value.Length -ge 2)) { $value = $value.Substring(1, $value.Length - 2) }
        if ($value -eq '' -or $value.Contains('${{') -or $name -eq 'PORT' -or $name.StartsWith('RAILWAY_')) { continue }
        $vars[$name] = $value
    }
    if ($vars.Contains('DATABASE_PUBLIC_URL')) { $vars['DATABASE_URL'] = $vars['DATABASE_PUBLIC_URL'] }
    $db = $vars['DATABASE_URL']
    if (-not $db -or $db -match 'MAT_KHAU|xxx\.proxy') { throw '.env.pc chưa có DATABASE_URL thật (Railway → Postgres → Variables → DATABASE_PUBLIC_URL).' }
    if ($db -match 'railway\.internal') { throw 'DATABASE_URL trong .env.pc là địa chỉ nội bộ (railway.internal) — dùng DATABASE_PUBLIC_URL của Postgres.' }
    if (-not $vars['Storage__S3__AccessKey'] -or -not $vars['Storage__S3__SecretKey']) { throw '.env.pc thiếu Storage__S3__AccessKey / Storage__S3__SecretKey (giống trên Railway).' }
    return $vars
}

function Stop-Worker {
    if (Test-Path $pidFile) {
        $old = Get-Content $pidFile -ErrorAction SilentlyContinue
        if ($old) { Get-Process -Id $old -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue }
        Remove-Item $pidFile -ErrorAction SilentlyContinue
    }
}

function Test-WorkerAlive {
    if (-not (Test-Path $pidFile)) { return $false }
    $id = Get-Content $pidFile -ErrorAction SilentlyContinue
    return [bool]($id -and (Get-Process -Id $id -ErrorAction SilentlyContinue))
}

function Start-Worker {
    $sha = (git -C $work rev-parse HEAD).Trim()
    $out = Join-Path $bin $sha.Substring(0, 12)
    if (-not (Test-Path (Join-Path $out 'NewsStudio.Api.dll'))) {
        Say "Build bản $($sha.Substring(0, 7)) (lần đầu mất vài phút)…" Cyan
        dotnet publish (Join-Path $work 'backend\src\NewsStudio.Api\NewsStudio.Api.csproj') -c Release -o $out --nologo -v q | Out-Host
        if ($LASTEXITCODE -ne 0) { Say 'Build lỗi — xem thông báo ở trên.' Red; return }
    }
    $vars = Read-EnvPc
    Stop-Worker
    foreach ($k in $vars.Keys) { [Environment]::SetEnvironmentVariable($k, $vars[$k], 'Process') }
    $fixed = @{
        'Worker__Role' = 'pc'; 'APP_VERSION' = $sha; 'ASPNETCORE_ENVIRONMENT' = 'Production'
        'ASPNETCORE_URLS' = 'http://127.0.0.1:8090'; 'Hangfire__EmbeddedServer' = 'true'; 'Simulation__Enabled' = 'false'
        'Studio__MediaPath' = (Join-Path $data 'studio-media'); 'Storage__TempStoragePath' = (Join-Path $data 'newsstudio')
    }
    foreach ($k in $fixed.Keys) { [Environment]::SetEnvironmentVariable($k, $fixed[$k], 'Process') }
    $log = Join-Path $data 'worker.log'
    if ((Test-Path $log) -and (Get-Item $log).Length -gt 20MB) { Move-Item $log "$log.old" -Force }
    $p = Start-Process dotnet -ArgumentList "`"$(Join-Path $out 'NewsStudio.Api.dll')`"" -WorkingDirectory $out -WindowStyle Hidden `
        -RedirectStandardOutput $log -RedirectStandardError "$log.err" -PassThru
    Set-Content $pidFile $p.Id
    Say "Máy tính đang làm việc nền (bản $($sha.Substring(0, 7))). Để cửa sổ này mở; tắt máy thì Railway tự nhận lại việc." Green
    Say "Nhật ký: $log" DarkGray
    # dọn các bản build cũ, giữ 2 bản gần nhất
    Get-ChildItem $bin -Directory | Sort-Object LastWriteTime -Descending | Select-Object -Skip 2 | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}

# a separate copy of the code for the worker, so it never touches the folder being edited
if (-not (Test-Path (Join-Path $work '.git'))) {
    $origin = (git -C $repo remote get-url origin).Trim()
    Say "Tải code về $work…"
    git clone --quiet $origin $work
}

try { Read-EnvPc | Out-Null } catch { Say $_.Exception.Message Red; exit 1 }
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
        } elseif (-not (Test-WorkerAlive)) {
            Say 'Chương trình đã dừng — chạy lại…' Yellow
            Start-Worker
        }
    } catch {
        Say "Lỗi: $($_.Exception.Message)" Yellow
    }
}
