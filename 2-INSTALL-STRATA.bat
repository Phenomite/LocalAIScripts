@echo off
setlocal EnableExtensions
title Install Strata - Latest Windows x64

echo ============================================================
echo   Strata Installer - Windows 11 x64
echo ============================================================
echo   [1] Auto-install Strata Terminal/CLI latest x64 (Default)
echo       (Required for StrataController; embeds Python, no admin needed)
echo   [2] Auto-install Strata Desktop App latest x64 (setup.exe)
echo   [3] Auto-install BOTH (Terminal/CLI + Desktop App)
echo   [4] Open GitHub Releases page in browser (manual download)
echo   [5] Exit
echo ============================================================
set "STRATA_CHOICE=1"
set /p "STRATA_CHOICE=Select option [1-5] (Default 1): "

if "%STRATA_CHOICE%"=="5" exit /b 0
if "%STRATA_CHOICE%"=="4" goto :OpenBrowser
if "%STRATA_CHOICE%"=="1" goto :FindPowerShell
if "%STRATA_CHOICE%"=="2" goto :FindPowerShell
if "%STRATA_CHOICE%"=="3" goto :FindPowerShell

echo Invalid choice "%STRATA_CHOICE%". Exiting.
pause
exit /b 1

:OpenBrowser
echo.
echo Opening https://github.com/Niko1221/Strata/releases/latest ...
start "" "https://github.com/Niko1221/Strata/releases/latest"
echo.
echo Manual x64 download guide:
echo   - Desktop App : download "Strata-<version>-windows-x64-setup.exe" and run it.
echo   - Terminal CLI: download "install.ps1" and run it in PowerShell.
echo   Note: strata-control-*.zip and strata-engine-*.tar.gz are internal components.
echo.
pause
exit /b 0

:FindPowerShell
set "STRATA_INST_SELF=%~f0"
set "PS_EXE="

:: 1. Prefer pwsh.exe (PATH, Program Files, WindowsApps alias, or Store MSIX package folder)
for /f "delims=" %%I in ('where.exe pwsh.exe 2^>nul') do call :ProbePs "%%~I"
if not defined PS_EXE for /d %%D in ("%ProgramFiles%\PowerShell\*") do call :ProbePs "%%~fD\pwsh.exe"
if not defined PS_EXE call :ProbePs "%LOCALAPPDATA%\Microsoft\WindowsApps\Microsoft.PowerShell_8wekyb3d8bbwe\pwsh.exe"
if not defined PS_EXE call :ProbePs "%LOCALAPPDATA%\Microsoft\WindowsApps\pwsh.exe"
if not defined PS_EXE for /f "tokens=2,*" %%A in ('reg query "HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages" /s /v PackageRootFolder 2^>nul ^| findstr /i "Microsoft.PowerShell_.*_x64__"') do call :ProbePs "%%~B\pwsh.exe"
if not defined PS_EXE for /f "tokens=2,*" %%A in ('reg query "HKLM\SOFTWARE\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\PackageRepository\Packages" /s /v Path 2^>nul ^| findstr /i "Microsoft.PowerShell_.*_x64__"') do call :ProbePs "%%~B\pwsh.exe"
if not defined PS_EXE for /d %%D in ("%ProgramFiles%\WindowsApps\Microsoft.PowerShell_*_x64__8wekyb3d8bbwe") do call :ProbePs "%%~fD\pwsh.exe"
if not defined PS_EXE call :ProbePs "%USERPROFILE%\.dotnet\tools\pwsh.exe"
if not defined PS_EXE call :ProbePs "%USERPROFILE%\scoop\apps\pwsh\current\pwsh.exe"

:: 2. Fallback to Windows PowerShell 5.1 only if pwsh is not available
if not defined PS_EXE for /f "delims=" %%I in ('where.exe powershell.exe 2^>nul') do call :ProbePs "%%~I"
if not defined PS_EXE call :ProbePs "%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe"
if not defined PS_EXE call :ProbePs "%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"

if not defined PS_EXE (
    echo [ERROR] Could not locate a working pwsh.exe or powershell.exe on this system.
    pause
    exit /b 1
)

echo [*] Using PowerShell: "%PS_EXE%"
"%PS_EXE%" -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "try { $raw = [IO.File]::ReadAllText($env:STRATA_INST_SELF, [Text.Encoding]::UTF8); $ms = '::PS_CODE_' + 'START::'; $me = '::PS_CODE_' + 'END::'; $i = $raw.IndexOf($ms, [StringComparison]::Ordinal); $e = $raw.IndexOf($me, [StringComparison]::Ordinal); if ($i -lt 0 -or $e -le $i) { throw 'Embedded script markers missing!' }; & ([scriptblock]::Create($raw.Substring($i + $ms.Length, $e - $i - $ms.Length))) } catch { Write-Host ('[ERROR] ' + $_) -ForegroundColor Red; exit 1 }"

echo.
if errorlevel 1 (
    echo [FAILED] Strata installation encountered an error.
    pause
    exit /b 1
)
echo [DONE] Open a NEW terminal window and run: strata launch
pause
exit /b 0

:ProbePs
if defined PS_EXE exit /b 0
if "%~1"=="" exit /b 1
"%~1" -NoLogo -NoProfile -Command "exit 0" >nul 2>&1
if errorlevel 1 exit /b 1
set "PS_EXE=%~1"
exit /b 0

::PS_CODE_START::
$ErrorActionPreference = "Stop"
$isLegacyPs = ($PSVersionTable.PSVersion.Major -lt 6)

if ($isLegacyPs) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    if ($env:PSModulePath) {
        $env:PSModulePath = (($env:PSModulePath -split ';') | Where-Object { $_ -and $_ -notmatch '\\PowerShell\\7' }) -join ';'
    }
} else {
    $psHomeMod = Join-Path $PSHOME "Modules"
    if ($env:PSModulePath -and (($env:PSModulePath -split ';') -notcontains $psHomeMod)) {
        $env:PSModulePath = "$psHomeMod;$env:PSModulePath"
    }
}

$env:PATH = "$PSHOME;$env:PATH"

$arch = if ($env:PROCESSOR_ARCHITEW6432) { $env:PROCESSOR_ARCHITEW6432 } else { $env:PROCESSOR_ARCHITECTURE }
if (-not [Environment]::Is64BitOperatingSystem -or ($arch -ne 'AMD64' -and $arch -ne 'x86_64')) {
    throw "Unsupported architecture '$arch'. This installer targets Windows 11 x64 (AMD64) only."
}

function Invoke-DownloadFile([string]$Uri, [string]$OutFile) {
    if ($script:isLegacyPs) {
        Invoke-WebRequest -Uri $Uri -OutFile $OutFile -UseBasicParsing
    } else {
        Invoke-WebRequest -Uri $Uri -OutFile $OutFile
    }
}

function Refresh-ProcessPath {
    $parts = @(
        $PSHOME,
        "$env:LOCALAPPDATA\Strata\terminal\current\bin",
        "$env:LOCALAPPDATA\Strata\bin",
        "$env:LOCALAPPDATA\Strata"
    ) +
    ([Environment]::GetEnvironmentVariable("Path", "User") -split ';') +
    ([Environment]::GetEnvironmentVariable("Path", "Machine") -split ';') +
    ($env:PATH -split ';')
    $env:PATH = ($parts | Where-Object { $_ } | Select-Object -Unique) -join ';'
}

function Install-VcRedistX64 {
    $sys32 = Join-Path $env:SystemRoot "System32"
    if ([IO.File]::Exists((Join-Path $sys32 "vcruntime140.dll")) -and [IO.File]::Exists((Join-Path $sys32 "vcruntime140_1.dll"))) {
        Write-Host "[OK] VC++ 2015-2022 Redistributable (x64) already present." -ForegroundColor Green
        return
    }
    Write-Host "[*] Installing VC++ 2015-2022 Redistributable (x64)..." -ForegroundColor Cyan
    if (Get-Command winget -EA 0) {
        $ErrorActionPreference = "Continue"
        & winget install --id Microsoft.VCRedist.2015+.x64 -e --architecture x64 --silent --accept-package-agreements --accept-source-agreements
        $rc = $LASTEXITCODE
        $ErrorActionPreference = "Stop"
        if ($rc -eq 0 -or [IO.File]::Exists((Join-Path $sys32 "vcruntime140.dll"))) {
            Write-Host "[OK] VC++ x64 runtime installed." -ForegroundColor Green
            return
        }
    }
    $tmp = Join-Path $env:TEMP "vc_redist.x64.exe"
    Invoke-DownloadFile -Uri "https://aka.ms/vs/17/release/vc_redist.x64.exe" -OutFile $tmp
    $p = Start-Process -FilePath $tmp -ArgumentList "/install /quiet /norestart" -Wait -PassThru
    Remove-Item $tmp -Force -EA 0
    if ($p.ExitCode -ne 0 -and $p.ExitCode -ne 3010) {
        Write-Warning "VC++ x64 installer exited with code $($p.ExitCode)."
    } else {
        Write-Host "[OK] VC++ x64 runtime installed." -ForegroundColor Green
    }
}

function Resolve-LatestStrataRelease {
    try {
        $rel = Invoke-RestMethod -Uri "https://api.github.com/repos/Niko1221/Strata/releases/latest" -Headers @{ "User-Agent" = "StrataWin11Installer/1.0" } -TimeoutSec 30
        $asset = $rel.assets | Where-Object { $_.name -match '^Strata-.*-windows-x64-setup\.exe$' } | Select-Object -First 1
        $sha = ""
        if ($asset -and $asset.digest -match '^sha256:([0-9a-fA-F]{64})$') { $sha = $Matches[1].ToLower() }
        return @{
            Tag       = [string]$rel.tag_name
            SetupName = if ($asset) { [string]$asset.name } else { "" }
            SetupUrl  = if ($asset) { [string]$asset.browser_download_url } else { "" }
            SetupSha  = $sha
        }
    } catch {
        try {
            $handler = New-Object System.Net.Http.HttpClientHandler
            $handler.AllowAutoRedirect = $false
            $client = New-Object System.Net.Http.HttpClient($handler)
            try {
                $resp = $client.GetAsync("https://github.com/Niko1221/Strata/releases/latest").GetAwaiter().GetResult()
                $loc = if ($resp.Headers.Location) { $resp.Headers.Location.ToString() } else { "" }
                if ($loc -match '/tag/(v?([0-9][0-9A-Za-z.\-]+))$') {
                    $tag = $Matches[1]; $ver = $Matches[2]
                    $name = "Strata-$ver-windows-x64-setup.exe"
                    return @{
                        Tag       = $tag
                        SetupName = $name
                        SetupUrl  = "https://github.com/Niko1221/Strata/releases/download/$tag/$name"
                        SetupSha  = ""
                    }
                }
            } finally {
                $client.Dispose()
                $handler.Dispose()
            }
        } catch { }
        return @{ Tag = ""; SetupName = ""; SetupUrl = ""; SetupSha = "" }
    }
}

Install-VcRedistX64
$info = Resolve-LatestStrataRelease
if ($info.Tag) { Write-Host "[*] Latest Strata release: $($info.Tag)" -ForegroundColor Gray }

if ($env:STRATA_CHOICE -eq "1" -or $env:STRATA_CHOICE -eq "3") {
    Write-Host "[*] Installing Strata Terminal/CLI latest x64 (verified signed feed)..." -ForegroundColor Cyan
    $tmpInstallPs1 = Join-Path $env:TEMP "strata-install.ps1"
    Invoke-DownloadFile -Uri "https://github.com/Niko1221/Strata/releases/latest/download/install.ps1" -OutFile $tmpInstallPs1
    $ErrorActionPreference = "Continue"
    & $tmpInstallPs1
    $rc = $LASTEXITCODE
    $ErrorActionPreference = "Stop"
    Remove-Item $tmpInstallPs1 -Force -EA 0
    if ($null -ne $rc -and $rc -ne 0) { throw "Strata Terminal installer failed with exit code $rc." }

    Refresh-ProcessPath
    $strataCmd = Get-Command strata -EA 0
    if (-not $strataCmd) {
        $fallbackExe = Join-Path $env:LOCALAPPDATA "Strata\terminal\current\bin\strata.exe"
        if (-not (Test-Path $fallbackExe)) { throw "strata CLI executable not found after install." }
        Write-Host "[OK] Strata CLI installed at: $fallbackExe" -ForegroundColor Green
    } else {
        $verOut = (& strata --version 2>&1 | Out-String).Trim()
        Write-Host "[OK] Strata CLI ready ($verOut) at: $($strataCmd.Source)" -ForegroundColor Green
    }
}

if ($env:STRATA_CHOICE -eq "2" -or $env:STRATA_CHOICE -eq "3") {
    if (-not $info.SetupUrl) { throw "Could not resolve Strata-*-windows-x64-setup.exe download URL from GitHub." }
    $tmpSetup = Join-Path $env:TEMP $info.SetupName
    Write-Host "[*] Downloading $($info.SetupName) ..." -ForegroundColor Cyan
    Invoke-DownloadFile -Uri $info.SetupUrl -OutFile $tmpSetup
    if ($info.SetupSha) {
        $actualSha = (Get-FileHash -LiteralPath $tmpSetup -Algorithm SHA256).Hash.ToLower()
        if ($actualSha -ne $info.SetupSha) {
            Remove-Item $tmpSetup -Force -EA 0
            throw "SHA-256 mismatch for $($info.SetupName) (expected $($info.SetupSha), got $actualSha)."
        }
        Write-Host "[OK] SHA-256 verified ($actualSha)." -ForegroundColor Green
    }
    Write-Host "[*] Launching $($info.SetupName)..." -ForegroundColor Cyan
    $p = Start-Process -FilePath $tmpSetup -Wait -PassThru
    Remove-Item $tmpSetup -Force -EA 0
    if ($p.ExitCode -ne 0) { throw "Desktop installer exited with code $($p.ExitCode)." }
    Write-Host "[OK] Strata Desktop App setup completed." -ForegroundColor Green
}
# ::PS_CODE_END::