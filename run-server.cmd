@echo off
REM Launches the Compass server for local testing.
REM Double-click this file, or run it from a terminal. Close the window to stop the server.

cd /d "%~dp0"

REM Set explicitly rather than relying on launchSettings.json, so this behaves the same
REM however it is started -- double-clicked, from a terminal, or from a shortcut.
set ASPNETCORE_URLS=http://localhost:5215
set ASPNETCORE_ENVIRONMENT=Development

echo.
echo   Compass server
echo   --------------
echo   Address : %ASPNETCORE_URLS%
echo   Data    : %~dp0src\Compass.Server\var
echo.
echo   Leave this window open while you test. Close it to stop the server.
echo.

dotnet run --project "src\Compass.Server" --no-launch-profile

REM Keep the window open if the server exits or fails to start, so the error is readable
REM instead of vanishing with the console.
echo.
echo   The server has stopped (exit code %ERRORLEVEL%).
pause
