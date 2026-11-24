@echo off
rem Dung may lam viec: khoang 1 phut sau Railway tu nhan lai viec.
powershell -NoProfile -Command "$f = Join-Path $env:USERPROFILE 'TramTinPcWorker-data\worker.pid'; if (Test-Path $f) { Stop-Process -Id (Get-Content $f) -Force -ErrorAction SilentlyContinue; Remove-Item $f; 'Da dung may lam viec.' } else { 'May lam viec khong chay.' }"
pause
