@echo off
rem Bat may tinh lam may lam viec cho tool (tu cap nhat code, chay bang .NET tren Windows). De cua so nay mo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0start-pc-worker.ps1"
pause
