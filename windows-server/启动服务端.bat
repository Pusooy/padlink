@echo off
chcp 65001 >nul
cd /d "%~dp0"
title PadLink 服务端
python server.py
pause
