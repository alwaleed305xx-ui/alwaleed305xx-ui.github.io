@echo off
title SCREAMER - Fix My Assets
echo.
echo  SCREAMER - fixing compile errors from imported asset packs...
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Tools\FixMyAssets.ps1"
echo.
if errorlevel 1 (
  echo  Could not finish - read the message above.
) else (
  echo  Done. Go back to Unity ^(it refreshes by itself^), then: Screamer ^> Build Everything ^> Play.
)
echo.
pause
