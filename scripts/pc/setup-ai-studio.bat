@echo off
rem Cai bo phan tich video cua AI Video Studio (mot lan): llama.cpp + Qwen3-VL + whisper + OpenCV. Them -With8B de co model 8B.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup-ai-studio.ps1" %*
pause
