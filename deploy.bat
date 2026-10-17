@echo off
title Mail – Build, Commit, Push ^& Release
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy.ps1"
pause
