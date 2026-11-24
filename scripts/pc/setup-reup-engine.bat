@echo off
rem Cai engine Reup Video (mot lan): Python venv + ONNX Runtime + OpenCV + model mien phi. Them -DirectML de dung card do hoa san co.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup-reup-engine.ps1" %*
pause
