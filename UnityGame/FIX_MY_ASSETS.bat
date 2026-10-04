@echo off
title SCREAMER - Fix My Assets
echo.
echo  SCREAMER - fixing compile errors from imported asset packs...
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Tools\FixMyAssets.ps1"
echo.
echo  Done. Go back to Unity (it refreshes by itself), then: Screamer ^> Build Everything ^> Play.
echo.
pause
