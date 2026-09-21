@echo off
REM Builds the two deployable artifacts into publish\.
REM
REM   publish\server\  the Compass server, ready to copy to wherever it will run
REM   publish\plugin\  the Dalamud plugin, including the zip Dalamud repositories serve
REM
REM Neither is committed; publish\ is ignored.

cd /d "%~dp0"

echo.
echo   Building Compass for release
echo   ----------------------------
echo.

if exist "publish" rmdir /s /q "publish"

dotnet publish "src\Compass.Server" -c Release -o "publish\server" --nologo
if errorlevel 1 goto failed

dotnet build "src\Compass.Plugin" -c Release --nologo
if errorlevel 1 goto failed

mkdir "publish\plugin" 2>nul
xcopy "src\Compass.Plugin\bin\Release\*" "publish\plugin\" /E /I /Y /Q >nul
if errorlevel 1 goto failed

echo.
echo   Done.
echo.
echo     Server : %~dp0publish\server\Compass.Server.exe
echo     Plugin : %~dp0publish\plugin\
echo.
echo   Read docs\DEPLOYMENT.md before running the server anywhere public.
echo   In particular: set an absolute Compass__DataDirectory, and put it behind TLS.
echo.
pause
exit /b 0

:failed
echo.
echo   BUILD FAILED. Nothing was published.
echo.
pause
exit /b 1
