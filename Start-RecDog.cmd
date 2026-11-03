@echo off
setlocal
if exist "D:\DevTools\dotnet\dotnet.exe" set "DOTNET_ROOT=D:\DevTools\dotnet"
if not exist "%~dp0artifacts\app-recording-workflow\RecDog.exe" (
  echo RecDog has not been built. Run scripts\build.ps1 first.
  pause
  exit /b 1
)
start "" "%~dp0artifacts\app-recording-workflow\RecDog.exe" %*
