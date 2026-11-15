@echo off
setlocal EnableExtensions
title Install oh-my-pi (omp) - Latest Windows x64

echo ============================================================
echo   oh-my-pi (omp) Installer - Windows 11 x64
echo ============================================================
echo   [1] Auto-install latest x64 omp + lightest dependencies
echo       (Standalone omp.exe; installs VC++ x64 / Git x64 only if missing)
echo   [2] Open GitHub Releases page in browser (manual download)
echo   [3] Exit
echo ============================================================
set "OMP_CHOICE=1"
set /p "OMP_CHOICE=Select option [1-3] (Default 1): "

if "%OMP_CHOICE%"=="3" exit /b 0
if "%OMP_CHOICE%"=="2" goto :OpenBrowser
if "%OMP_CHOICE%"=="1" goto :FindPowerShell

echo Invalid choice "%OMP_CHOICE%". Exiting.
pause
exit /b 1

:OpenBrowser
echo.
echo Opening https://github.com/can1357/oh-my-pi/releases/latest ...
start "" "https://github.com/can1357/oh-my-pi/releases/latest"
echo.
echo Manual x64 install steps:
echo   1. Download "omp-windows-x64.exe" from the release assets.
echo   2. Rename it to "omp.exe" in "%LOCALAPPDATA%\omp\omp.exe"
echo      and add "%LOCALAPPDATA%\omp" to your User PATH.
echo   3. Optional: install Git for Windows x64 if not already installed.
echo.
set "OMP_DEPS=N"
set /p "OMP_DEPS=Still auto-check/install lightest dependencies (VC++ x64 & Git x64)? [y/N]: "
if /i not "%OMP_DEPS%"=="Y" exit /b 0

:FindPowerShell
set "OMP_SELF=%~f0"
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
"%PS_EXE%" -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "try { $raw = [IO.File]::ReadAllText($env:OMP_SELF, [Text.Encoding]::UTF8); $ms = '::PS_CODE_' + 'START::'; $me = '::PS_CODE_' + 'END::'; $i = $raw.IndexOf($ms, [StringComparison]::Ordinal); $e = $raw.IndexOf($me, [StringComparison]::Ordinal); if ($i -lt 0 -or $e -le $i) { throw 'Embedded script markers missing!' }; & ([scriptblock]::Create($raw.Substring($i + $ms.Length, $e - $i - $ms.Length))) } catch { Write-Host ('[ERROR] ' + $_) -ForegroundColor Red; exit 1 }"

echo.
if errorlevel 1 (
    echo [FAILED] oh-my-pi installation encountered an error.
    pause
    exit /b 1
)
echo [DONE] Open a NEW terminal window and run: omp --version
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
        "$env:LOCALAPPDATA\omp",
        "$env:USERPROFILE\.local\bin",
        "$env:ProgramFiles\Git\cmd",
        "$env:LOCALAPPDATA\Programs\Git\cmd"
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

function Install-GitX64 {
    Refresh-ProcessPath
    if (Get-Command git.exe -EA 0) {
        Write-Host "[OK] Git already installed: $(& git --version)" -ForegroundColor Green
        return
    }
    Write-Host "[*] Installing latest Git for Windows (x64)..." -ForegroundColor Cyan
    if (Get-Command winget -EA 0) {
        foreach ($scopeArg in @(@("--scope", "user"), @())) {
            $ErrorActionPreference = "Continue"
            & winget install --id Git.Git -e --architecture x64 @scopeArg --silent --accept-package-agreements --accept-source-agreements
            $rc = $LASTEXITCODE
            $ErrorActionPreference = "Stop"
            Refresh-ProcessPath
            if ($rc -eq 0 -or (Get-Command git.exe -EA 0)) {
                Write-Host "[OK] Git x64 installed: $(& git --version)" -ForegroundColor Green
                return
            }
        }
    }
    $headers = @{ "User-Agent" = "OmpWin11Installer/1.0" }
    $rel = Invoke-RestMethod -Uri "https://api.github.com/repos/git-for-windows/git/releases/latest" -Headers $headers -TimeoutSec 30
    $asset = $rel.assets | Where-Object { $_.name -match '^Git-.*-64-bit\.exe$' } | Select-Object -First 1
    if (-not $asset) { throw "Could not find 64-bit Git installer in latest release." }
    $tmp = Join-Path $env:TEMP $asset.name
    Invoke-DownloadFile -Uri $asset.browser_download_url -OutFile $tmp
    $p = Start-Process -FilePath $tmp -ArgumentList "/VERYSILENT /NORESTART /NOCANCEL /SP-" -Wait -PassThru
    Remove-Item $tmp -Force -EA 0
    Refresh-ProcessPath
    if ($p.ExitCode -ne 0) { throw "Git installer exited with code $($p.ExitCode)." }
    Write-Host "[OK] Git x64 installed." -ForegroundColor Green
}

function Add-UserPathEntry([string]$Dir) {
    $cur = [Environment]::GetEnvironmentVariable("Path", "User")
    $list = @($cur -split ';' | Where-Object { $_ })
    if ($list -notcontains $Dir) {
        $newPath = (@($Dir) + $list) -join ';'
        [Environment]::SetEnvironmentVariable("Path", $newPath, "User")
    }
}

Install-VcRedistX64
Install-GitX64

if ($env:OMP_CHOICE -eq "2") { exit 0 }

Write-Host "[*] Checking latest oh-my-pi release (x64)..." -ForegroundColor Cyan
$latestTag = ""; $assetUrl = ""; $assetSha = ""
try {
    $rel = Invoke-RestMethod -Uri "https://api.github.com/repos/can1357/oh-my-pi/releases/latest" -Headers @{ "User-Agent" = "OmpWin11Installer/1.0" } -TimeoutSec 30
    $latestTag = [string]$rel.tag_name
    $asset = $rel.assets | Where-Object { $_.name -eq "omp-windows-x64.exe" } | Select-Object -First 1
    if ($asset) {
        $assetUrl = [string]$asset.browser_download_url
        if ($asset.digest -match '^sha256:([0-9a-fA-F]{64})$') { $assetSha = $Matches[1].ToLower() }
    }
    if ($latestTag) { Write-Host "[*] Latest release detected: $latestTag" -ForegroundColor Gray }
} catch {
    Write-Warning "GitHub API lookup skipped ($($_.Exception.Message)); falling back to latest release endpoint."
}

$installedOk = $false
$tmpInstallPs1 = Join-Path $env:TEMP "omp-install.ps1"
try {
    Write-Host "[*] Running official oh-my-pi binary installer (-Binary)..." -ForegroundColor Cyan
    Invoke-DownloadFile -Uri "https://omp.sh/install.ps1" -OutFile $tmpInstallPs1
    $ErrorActionPreference = "Continue"
    & $tmpInstallPs1 -Binary
    $rc = $LASTEXITCODE
    $ErrorActionPreference = "Stop"
    Refresh-ProcessPath
    if ((Get-Command omp.exe -EA 0) -and ($null -eq $rc -or $rc -eq 0)) { $installedOk = $true }
} catch {
    $ErrorActionPreference = "Stop"
    Write-Warning "Primary installer failed ($($_.Exception.Message)); using direct GitHub x64 binary fallback..."
} finally {
    Remove-Item $tmpInstallPs1 -Force -EA 0
}

if (-not $installedOk) {
    $dlUrl = if ($assetUrl) { $assetUrl } else { "https://github.com/can1357/oh-my-pi/releases/latest/download/omp-windows-x64.exe" }
    $installDir = Join-Path $env:LOCALAPPDATA "omp"
    if (-not [IO.Directory]::Exists($installDir)) { [void][IO.Directory]::CreateDirectory($installDir) }
    $tmpExe = Join-Path $env:TEMP "omp-windows-x64.exe"
    $dstExe = Join-Path $installDir "omp.exe"

    Write-Host "[*] Downloading $dlUrl ..." -ForegroundColor Cyan
    Invoke-DownloadFile -Uri $dlUrl -OutFile $tmpExe
    if ($assetSha) {
        $actualSha = (Get-FileHash -LiteralPath $tmpExe -Algorithm SHA256).Hash.ToLower()
        if ($actualSha -ne $assetSha) {
            Remove-Item $tmpExe -Force -EA 0
            throw "SHA-256 mismatch for omp-windows-x64.exe (expected $assetSha, got $actualSha)."
        }
        Write-Host "[OK] SHA-256 verified ($actualSha)." -ForegroundColor Green
    }
    Move-Item -LiteralPath $tmpExe -Destination $dstExe -Force
    Add-UserPathEntry $installDir
    Refresh-ProcessPath
}

# Remove stale omp.bunx marker if left over from an older npm/bun install so 'omp update' stays on binary mode
foreach ($dir in @("$env:LOCALAPPDATA\omp", "$env:USERPROFILE\.local\bin", "$env:USERPROFILE\.bun\bin")) {
    $marker = Join-Path $dir "omp.bunx"
    if ((Test-Path (Join-Path $dir "omp.exe")) -and (Test-Path $marker)) {
        Remove-Item $marker -Force -EA 0
    }
}

$ompCmd = Get-Command omp.exe -EA 0
if (-not $ompCmd) { throw "omp.exe was not found on PATH after installation." }
$ver = (& $ompCmd.Source --version 2>&1 | Out-String).Trim()
Write-Host "[OK] Installed omp ($ver) at: $($ompCmd.Source)" -ForegroundColor Green
# ::PS_CODE_END::