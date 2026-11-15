@echo off
setlocal EnableExtensions
pushd "%~dp0"

if not exist "StrataController/StrataController.cs" (
    echo [ERROR] "StrataController.cs" not found in StrataController/
    pause & exit /b 1
)

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [ERROR] Windows .NET Framework 4.x csc.exe was not found.
    pause & exit /b 1
)

"%CSC%" /nologo /t:winexe /platform:x64 /optimize+ /out:"StrataController.exe" ^
    /win32icon:StrataController\app.ico /win32manifest:StrataController\app.manifest /r:System.Web.Extensions.dll ^
    "StrataController\StrataController.cs"

if errorlevel 1 (
    echo.
    echo [ERROR] Compilation failed.
    pause & exit /b 1
)

if /i "%~1"=="--no-launch" exit /b 0
start "" "StrataController.exe"
exit /b 0