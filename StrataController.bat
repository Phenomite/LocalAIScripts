@echo off
setlocal EnableExtensions
pushd "%~dp0"

if not exist "StrataController\StrataController.cs" (echo [ERROR] StrataController.cs not found in "StrataController\".& goto :fail)
set "FX=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FX%\mscorlib.dll" set "FX=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
if not exist "%FX%\mscorlib.dll" (echo [ERROR] .NET Framework 4.x not found.& goto :fail)

set "RES="
if exist "StrataController\app.ico" set "RES=/win32icon:StrataController\app.ico"
if exist "StrataController\app.manifest" set "RES=%RES% /win32manifest:StrataController\app.manifest"

set "CSC="
for %%W in ("%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" "%ProgramFiles%\Microsoft Visual Studio\Installer\vswhere.exe") do if not defined CSC if exist %%W for /f "usebackq delims=" %%C in (`""%%~W" -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\Roslyn\csc.exe 2^>nul"`) do if not defined CSC if exist "%%~C" set "CSC=%%~C"

set "DN="
set "DLL="
if not defined CSC for /f "delims=" %%D in ('where dotnet 2^>nul') do if not defined DN set "DN=%%~fD"
if not defined CSC if not defined DN if exist "%ProgramFiles%\dotnet\dotnet.exe" set "DN=%ProgramFiles%\dotnet\dotnet.exe"
if not defined CSC if defined DN for %%P in ("%DN%") do for /d %%S in ("%%~dpPsdk\*") do if exist "%%~S\Roslyn\bincore\csc.dll" set "DLL=%%~S\Roslyn\bincore\csc.dll"
if not defined CSC if not defined DLL for /d %%S in ("%ProgramFiles%\dotnet\sdk\*") do if exist "%%~S\Roslyn\bincore\csc.dll" (set "DN=%ProgramFiles%\dotnet\dotnet.exe"& set "DLL=%%~S\Roslyn\bincore\csc.dll")

set ARGS=/nologo /noconfig /nostdlib+ /t:winexe /platform:x64 /optimize+ /lib:"%FX%" /out:"StrataController.exe" %RES% /r:"%FX%\mscorlib.dll" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Net.Http.dll /r:System.Web.Extensions.dll "StrataController\StrataController.cs"
set RARGS=/deterministic /highentropyva+ /subsystemversion:6.00 /sdkpath:"%FX%"

if defined CSC (
    echo [Roslyn] "%CSC%"
    "%CSC%" %RARGS% %ARGS%
) else if defined DLL (
    echo [SDK Roslyn] "%DLL%"
    "%DN%" exec "%DLL%" %RARGS% %ARGS%
) else if exist "%FX%\csc.exe" (
    echo [Legacy] "%FX%\csc.exe"
    "%FX%\csc.exe" %ARGS%
) else (
    echo [ERROR] No C# compiler found.& goto :fail
)

if errorlevel 1 goto :fail
if /i not "%~1"=="--no-launch" start "" "StrataController.exe"
popd & exit /b 0

:fail
if /i not "%~1"=="--no-launch" pause
popd & exit /b 1