@echo off
setlocal EnableExtensions
pushd "%~dp0"

if not exist "StrataController.cs" (
    echo [ERROR] "StrataController.cs" not found next to this script.
    pause & exit /b 1
)

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [ERROR] Windows .NET Framework 4.x csc.exe was not found.
    pause & exit /b 1
)

"%CSC%" /nologo /utf8output /target:winexe /platform:anycpu /optimize+ /out:"StrataController.exe" ^
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Management.dll ^
    "StrataController.cs"

if errorlevel 1 (
    echo.
    echo [ERROR] Compilation failed.
    pause & exit /b 1
)

if /i "%~1"=="--no-launch" exit /b 0
start "" "StrataController.exe"
exit /b 0