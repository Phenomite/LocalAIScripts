@echo off
setlocal EnableExtensions
pushd "%~dp0" || exit /b 1

if not exist "StrixController\StrixController.cs" (echo [ERROR] StrixController\StrixController.cs not found.& goto :fail)
set "FX=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"& set "PLAT=x64"
if not exist "%FX%\mscorlib.dll" (set "FX=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"& set "PLAT=x86")
if not exist "%FX%\mscorlib.dll" (echo [ERROR] .NET Framework 4.x not found.& goto :fail)

set "RES="& set "CSC="& set "DN="& set "DLL="& set "ERRORLEVEL="
if exist "StrixController\app.ico" set "RES=/win32icon:StrixController\app.ico"
if exist "StrixController\app.manifest" set "RES=%RES% /win32manifest:StrixController\app.manifest"

for %%W in ("%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" "%ProgramFiles%\Microsoft Visual Studio\Installer\vswhere.exe") do if not defined CSC if exist "%%~W" for /f "usebackq delims=" %%C in (`""%%~W" -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\Roslyn\csc.exe" 2^>nul`) do if not defined CSC if exist "%%~C" set "CSC=%%~C"
if not defined CSC for /f "delims=" %%D in ('where dotnet.exe 2^>nul ^& if exist "%ProgramFiles%\dotnet\dotnet.exe" echo %ProgramFiles%\dotnet\dotnet.exe') do if not defined DLL for /d %%S in ("%%~dpDsdk\*") do if exist "%%~S\Roslyn\bincore\csc.dll" (set "DN=%%~fD"& set "DLL=%%~S\Roslyn\bincore\csc.dll")

set ARGS=/nologo /noconfig /nostdlib+ /t:winexe /platform:%PLAT% /optimize+ /lib:"%FX%" /out:"StrixController\StrixController.exe" %RES% /r:mscorlib.dll /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Net.Http.dll StrixController\StrixController.cs
set RARGS=/deterministic /highentropyva+ /subsystemversion:6.00 /sdkpath:"%FX%"

if defined CSC (echo [Roslyn] "%CSC%"& "%CSC%" %RARGS% %ARGS%& goto :built)
if defined DLL (echo [SDK Roslyn] "%DLL%"& "%DN%" exec "%DLL%" %RARGS% %ARGS%& goto :built)
if exist "%FX%\csc.exe" (echo [Legacy] "%FX%\csc.exe"& "%FX%\csc.exe" %ARGS%& goto :built)
echo [ERROR] No C# compiler found.& goto :fail

:built
if %errorlevel% neq 0 goto :fail
if not exist "StrixController\StrixController.exe" goto :fail
if /i not "%~1"=="--no-launch" start "" "StrixController\StrixController.exe"
popd & exit /b 0

:fail
if /i not "%~1"=="--no-launch" pause
popd & exit /b 1