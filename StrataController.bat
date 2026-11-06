:: StrataController.bat
@echo off
setlocal EnableExtensions
set "STRATA_SELF=%~f0"
set "STRATA_BOOTLOG=%TEMP%\StrataController.debug.log"

if exist "%STRATA_BOOTLOG%" del /f /q "%STRATA_BOOTLOG%" >nul 2>&1
echo [%DATE% %TIME%] Bootstrap starting from "%STRATA_SELF%" > "%STRATA_BOOTLOG%"

powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -Command "try { $raw = [System.IO.File]::ReadAllText($env:STRATA_SELF, [System.Text.Encoding]::UTF8); $ms = '::' + 'PS_CODE_START' + '::'; $me = '::' + 'PS_CODE_END' + '::'; $idx = $raw.IndexOf($ms); if ($idx -lt 0) { throw 'Start marker not found.' }; if ($raw.IndexOf($me) -lt 0) { throw 'End marker not found. The script file was cut off/truncated when saving!' }; $code = $raw.Substring($idx + $ms.Length); $sb = [scriptblock]::Create($code); & $sb } catch { $err = $_ | Out-String; Add-Content -LiteralPath $env:STRATA_BOOTLOG -Value ('[FATAL] ' + $err); Write-Host $err -ForegroundColor Red; exit 1 }"

if errorlevel 1 (
    echo.
    echo ====================================================================
    echo [ERROR] StrataController failed. Full log saved to:
    echo   "%STRATA_BOOTLOG%"
    echo ====================================================================
    if exist "%STRATA_BOOTLOG%" type "%STRATA_BOOTLOG%"
    echo.
    pause
    exit /b 1
)
exit /b 0
::PS_CODE_START::

$ErrorActionPreference = "Continue"
$DebugLogPath = if ($env:STRATA_BOOTLOG) { $env:STRATA_BOOTLOG } else { Join-Path $env:TEMP "StrataController.debug.log" }

# 1. Kill any orphaned cmd/strata processes from old runs that may be locking temp log files
try {
    Get-CimInstance Win32_Process -Filter "Name = 'cmd.exe' OR Name = 'strata.exe'" -ErrorAction SilentlyContinue | Where-Object {
        $_.CommandLine -and ($_.CommandLine -match 'strata_gui_' -or $_.CommandLine -match 'strata connect')
    } | ForEach-Object {
        & taskkill.exe /F /T /PID $_.ProcessId 2>$null | Out-Null
    }
} catch {}

# 2. Force-clear the debug log in user's temp
try {
    [System.IO.File]::WriteAllText($DebugLogPath, "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff')] Bootstrap starting from $env:STRATA_SELF`r`n", [System.Text.Encoding]::UTF8)
} catch {}

function Write-DebugFile([string]$Message) {
    $ts = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss.fff")
    try { Add-Content -LiteralPath $DebugLogPath -Value "[$ts] $Message" -Encoding UTF8 -ErrorAction SilentlyContinue } catch {}
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

if (-not ([System.Management.Automation.PSTypeName]'StrataGUI.ConsoleWin32').Type) {
    Add-Type -Name ConsoleWin32 -Namespace StrataGUI -MemberDefinition @'
        [DllImport("kernel32.dll")] public static extern IntPtr GetConsoleWindow();
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);
'@
}

function Get-WindowClassName([IntPtr]$hWnd) {
    if ($hWnd -eq [IntPtr]::Zero) { return "" }
    try {
        $sb = New-Object System.Text.StringBuilder 256
        [void][StrataGUI.ConsoleWin32]::GetClassName($hWnd, $sb, $sb.Capacity)
        return $sb.ToString()
    } catch { return "" }
}

function Resolve-ConsoleWindowHandles {
    $handles = New-Object System.Collections.Generic.List[IntPtr]
    $validClasses = @("ConsoleWindowClass", "CASCADIA_HOSTING_WINDOW_CLASS")

    $fgWnd = [StrataGUI.ConsoleWin32]::GetForegroundWindow()
    if ($fgWnd -ne [IntPtr]::Zero -and ($validClasses -contains (Get-WindowClassName $fgWnd))) {
        [void]$handles.Add($fgWnd)
    }

    $cWnd = [StrataGUI.ConsoleWin32]::GetConsoleWindow()
    if ($cWnd -ne [IntPtr]::Zero -and -not $handles.Contains($cWnd)) {
        if ((Get-WindowClassName $cWnd) -eq "ConsoleWindowClass" -or [StrataGUI.ConsoleWin32]::IsWindowVisible($cWnd)) {
            [void]$handles.Add($cWnd)
        }
    }

    if ($handles.Count -eq 0) {
        try {
            $uniqueTitle = "StrataBootstrapConsole_" + $PID
            $oldTitle = $Host.UI.RawUI.WindowTitle
            $Host.UI.RawUI.WindowTitle = $uniqueTitle
            Start-Sleep -Milliseconds 40
            Get-Process -Name "WindowsTerminal", "OpenConsole", "cmd" -ErrorAction SilentlyContinue | ForEach-Object {
                $h = $_.MainWindowHandle
                if ($h -ne [IntPtr]::Zero -and -not $handles.Contains($h)) {
                    if (($validClasses -contains (Get-WindowClassName $h)) -or ($_.MainWindowTitle -and $_.MainWindowTitle.Contains($uniqueTitle))) {
                        [void]$handles.Add($h)
                    }
                }
            }
            $Host.UI.RawUI.WindowTitle = $oldTitle
        } catch {}
    }

    if ($handles.Count -eq 0 -and $cWnd -ne [IntPtr]::Zero) { [void]$handles.Add($cWnd) }
    return $handles
}

$script:ConsoleHandles = Resolve-ConsoleWindowHandles
$script:Form = $null

function Set-ConsoleVisibility([bool]$Visible) {
    try {
        foreach ($h in $script:ConsoleHandles) {
            if ($h -eq [IntPtr]::Zero -or ($script:Form -and $h -eq $script:Form.Handle)) { continue }
            if ($Visible) {
                [void][StrataGUI.ConsoleWin32]::ShowWindow($h, 9)
                [void][StrataGUI.ConsoleWin32]::ShowWindow($h, 5)
            } else {
                [void][StrataGUI.ConsoleWin32]::ShowWindow($h, 0)
                [void][StrataGUI.ConsoleWin32]::ShowWindowAsync($h, 0)
                if ([StrataGUI.ConsoleWin32]::IsWindowVisible($h)) {
                    [void][StrataGUI.ConsoleWin32]::ShowWindow($h, 6)
                    [void][StrataGUI.ConsoleWin32]::ShowWindowAsync($h, 2)
                }
            }
        }
    } catch {}
}

Set-ConsoleVisibility -Visible $false

try {
    $extraPaths = @(
        "$env:USERPROFILE\.local\bin", "$env:LOCALAPPDATA\Strata\bin", "$env:LOCALAPPDATA\Strata",
        "$env:LOCALAPPDATA\Programs\Strata", "$env:USERPROFILE\.omp\bin", "$env:APPDATA\npm"
    )
    $currentPaths = ($env:PATH -split ';') | Where-Object { $_ -and $_.Trim() }
    $regRaw = [Environment]::GetEnvironmentVariable("Path", "User") + ";" + [Environment]::GetEnvironmentVariable("Path", "Machine")
    $regPaths = ($regRaw -split ';') | Where-Object { $_ -and $_.Trim() } | ForEach-Object { [Environment]::ExpandEnvironmentVariables($_.Trim()) }

    $allPaths = New-Object System.Collections.Generic.List[string]
    foreach ($p in ($extraPaths + $currentPaths + $regPaths)) {
        if ($p -and -not $allPaths.Contains($p)) { [void]$allPaths.Add($p) }
    }
    $env:PATH = $allPaths -join ";"

    $StrataLocalDir = Join-Path $env:LOCALAPPDATA "Strata"
    $LaunchDir      = Join-Path $StrataLocalDir "launch"
    $OmpConfigPath  = Join-Path $env:USERPROFILE ".omp\agent\config.yml"
    $TempLaunchLog  = Join-Path $env:TEMP "strata_gui_launch.log"
    $TempCmdLog     = Join-Path $env:TEMP "strata_gui_cmd.log"

    Get-ChildItem -Path $env:TEMP -Filter "strata_gui_*.log" -ErrorAction SilentlyContinue | ForEach-Object {
        try { [System.IO.File]::WriteAllText($_.FullName, "", [System.Text.Encoding]::UTF8); Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue } catch {}
    }
    [System.IO.File]::WriteAllText($TempLaunchLog, "", [System.Text.Encoding]::UTF8)
    [System.IO.File]::WriteAllText($TempCmdLog, "", [System.Text.Encoding]::UTF8)

    if (-not (Get-Process -Name "strata-dsv41", "strata-server" -ErrorAction SilentlyContinue) -and (Test-Path -LiteralPath $LaunchDir)) {
        Get-ChildItem -Path $LaunchDir -Filter "*.engine.log" -ErrorAction SilentlyContinue | ForEach-Object {
            try { [System.IO.File]::WriteAllText($_.FullName, "", [System.Text.Encoding]::UTF8) } catch {}
        }
    }

    $script:EngineLogPos = 0; $script:LaunchLogPos = 0; $script:CmdLogPos = 0
    $script:CurrentEngineLog = ""; $script:LaunchProc = $null; $script:ActionProc = $null; $script:IsPrompting = $false

    $ModelList = New-Object System.Collections.Generic.List[string]
    foreach ($m in @("ist-das-coder", "ist-das-2.00bpw", "ist-das-2.25bpw", "ist-das-2.50bpw", "q2_0", "iq3_xxs", "iq3_s")) { [void]$ModelList.Add($m) }
    if (Test-Path -LiteralPath $LaunchDir) {
        Get-ChildItem -Path $LaunchDir -Filter "*.json" -ErrorAction SilentlyContinue | ForEach-Object {
            try {
                $json = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
                if ($json -and $json.model -and -not $ModelList.Contains([string]$json.model)) { [void]$ModelList.Add([string]$json.model) }
            } catch {}
        }
    }

    # Read current defaultThinkingLevel from ~/.omp/agent/config.yml if present
    $InitialThinking = "medium"
    if (Test-Path -LiteralPath $OmpConfigPath) {
        try {
            $cfgRaw = Get-Content -LiteralPath $OmpConfigPath -Raw -ErrorAction SilentlyContinue
            if ($cfgRaw -match '(?m)^\s*defaultThinkingLevel:\s*([a-zA-Z0-9_-]+)') {
                $InitialThinking = $Matches[1].Trim().ToLower()
            }
        } catch {}
    }

    $BgDark   = [System.Drawing.Color]::FromArgb(24, 24, 28)
    $BgPanel  = [System.Drawing.Color]::FromArgb(34, 34, 40)
    $BgInput  = [System.Drawing.Color]::FromArgb(45, 45, 54)
    $FgText   = [System.Drawing.Color]::FromArgb(235, 235, 240)
    $FgMuted  = [System.Drawing.Color]::FromArgb(165, 165, 180)
    $FontUI   = New-Object System.Drawing.Font("Segoe UI", 9.5)
    $FontBold = New-Object System.Drawing.Font("Segoe UI Semibold", 9.5)
    $FontMono = New-Object System.Drawing.Font("Consolas", 9)

    $Form = New-Object System.Windows.Forms.Form
    $script:Form = $Form
    $Form.Text = "Strata Engine Controller"
    $Form.Size = New-Object System.Drawing.Size(820, 620)
    $Form.MinimumSize = New-Object System.Drawing.Size(760, 500)
    $Form.StartPosition = "CenterScreen"
    $Form.BackColor = $BgDark; $Form.ForeColor = $FgText; $Form.Font = $FontUI

    function Add-Lbl($Parent, [string]$Text, [int]$X, [int]$Y, $Font = $null, $Color = $null) {
        $l = New-Object System.Windows.Forms.Label
        $l.Text = $Text; $l.Location = New-Object System.Drawing.Point($X, $Y); $l.AutoSize = $true
        if ($Font) { $l.Font = $Font }
        if ($Color) { $l.ForeColor = $Color }
        $Parent.Controls.Add($l)
        return $l
    }

    function Add-Btn($Parent, [string]$Text, [int]$X, [int]$Y, [int]$W, $Bg, $Font) {
        $b = New-Object System.Windows.Forms.Button
        $b.Text = $Text; $b.Font = $Font; $b.Location = New-Object System.Drawing.Point($X, $Y)
        $b.Size = New-Object System.Drawing.Size($W, 42); $b.BackColor = $Bg; $b.ForeColor = $FgText
        $b.FlatStyle = "Flat"; $b.FlatAppearance.BorderSize = 0
        $Parent.Controls.Add($b)
        return $b
    }

    $StatusPanel = New-Object System.Windows.Forms.Panel
    $StatusPanel.Dock = "Top"; $StatusPanel.Height = 48; $StatusPanel.BackColor = $BgPanel
    $Form.Controls.Add($StatusPanel)

    $LblStatus   = Add-Lbl $StatusPanel "[CHECKING ENGINE...]" 16 12 (New-Object System.Drawing.Font("Segoe UI Semibold", 11)) $null
    $LblProcInfo = Add-Lbl $StatusPanel "Endpoint: http://127.0.0.1:8080/v1" 310 15 $null $FgMuted

    $CfgPanel = New-Object System.Windows.Forms.Panel
    $CfgPanel.Dock = "Top"; $CfgPanel.Height = 150
    $Form.Controls.Add($CfgPanel); $CfgPanel.BringToFront()

    [void](Add-Lbl $CfgPanel "Model (--model):" 16 12)
    $CmbModel = New-Object System.Windows.Forms.ComboBox
    $CmbModel.Location = New-Object System.Drawing.Point(16, 34); $CmbModel.Size = New-Object System.Drawing.Size(185, 28)
    $CmbModel.BackColor = $BgInput; $CmbModel.ForeColor = $FgText; $CmbModel.FlatStyle = "Flat"
    $CmbModel.Items.AddRange($ModelList.ToArray()); $CmbModel.Text = "ist-das-coder"
    $CfgPanel.Controls.Add($CmbModel)

    [void](Add-Lbl $CfgPanel "Context:" 213 12)
    $CmbCtx = New-Object System.Windows.Forms.ComboBox
    $CmbCtx.Location = New-Object System.Drawing.Point(213, 34); $CmbCtx.Size = New-Object System.Drawing.Size(85, 28)
    $CmbCtx.BackColor = $BgInput; $CmbCtx.ForeColor = $FgText; $CmbCtx.FlatStyle = "Flat"
    [void]$CmbCtx.Items.AddRange(@("256k", "128k", "64k", "32k", "16k", "8k")); $CmbCtx.Text = "256k"
    $CfgPanel.Controls.Add($CmbCtx)

    [void](Add-Lbl $CfgPanel "Speed:" 310 12)
    $CmbSpeed = New-Object System.Windows.Forms.ComboBox
    $CmbSpeed.Location = New-Object System.Drawing.Point(310, 34); $CmbSpeed.Size = New-Object System.Drawing.Size(85, 28)
    $CmbSpeed.BackColor = $BgInput; $CmbSpeed.ForeColor = $FgText; $CmbSpeed.FlatStyle = "Flat"; $CmbSpeed.DropDownStyle = "DropDownList"
    [void]$CmbSpeed.Items.AddRange(@("full", "faster")); $CmbSpeed.SelectedItem = "full"
    $CfgPanel.Controls.Add($CmbSpeed)

    [void](Add-Lbl $CfgPanel "OMP Thinking:" 407 12)
    $CmbThinking = New-Object System.Windows.Forms.ComboBox
    $CmbThinking.Location = New-Object System.Drawing.Point(407, 34); $CmbThinking.Size = New-Object System.Drawing.Size(105, 28)
    $CmbThinking.BackColor = $BgInput; $CmbThinking.ForeColor = $FgText; $CmbThinking.FlatStyle = "Flat"; $CmbThinking.DropDownStyle = "DropDownList"
    [void]$CmbThinking.Items.AddRange(@("auto", "off", "minimal", "low", "medium", "high", "xhigh", "max"))
    if ($CmbThinking.Items.Contains($InitialThinking)) { $CmbThinking.SelectedItem = $InitialThinking } else { $CmbThinking.SelectedItem = "medium" }
    $CfgPanel.Controls.Add($CmbThinking)

    [void](Add-Lbl $CfgPanel "Preset Name (--save-preset):" 524 12)
    $TxtPreset = New-Object System.Windows.Forms.TextBox
    $TxtPreset.Location = New-Object System.Drawing.Point(524, 34); $TxtPreset.Size = New-Object System.Drawing.Size(260, 28)
    $TxtPreset.BackColor = $BgInput; $TxtPreset.ForeColor = $FgText; $TxtPreset.BorderStyle = "FixedSingle"; $TxtPreset.Text = "ds41-coder-256"
    $CfgPanel.Controls.Add($TxtPreset)

    $UpdatePresetName = {
        $m = $CmbModel.Text.Trim(); $c = $CmbCtx.Text.Trim().ToLower().Replace("k", "")
        if ($m -eq "ist-das-coder") { $TxtPreset.Text = "ds41-coder-$c" }
        elseif ($m) { $TxtPreset.Text = ($m -replace '[^a-zA-Z0-9\-]', '-') + "-$c" }
    }
    $CmbModel.Add_SelectedIndexChanged($UpdatePresetName); $CmbModel.Add_TextChanged($UpdatePresetName)
    $CmbCtx.Add_SelectedIndexChanged($UpdatePresetName);   $CmbCtx.Add_TextChanged($UpdatePresetName)

    $BtnStart   = Add-Btn $CfgPanel "Start Engine"    16  80 135 ([System.Drawing.Color]::FromArgb(35, 134, 54))  $FontBold
    $BtnStop    = Add-Btn $CfgPanel "Stop (Graceful)" 160 80 135 ([System.Drawing.Color]::FromArgb(180, 105, 14)) $FontBold
    $BtnKill    = Add-Btn $CfgPanel "Force Kill"      304 80 115 ([System.Drawing.Color]::FromArgb(190, 40, 40))  $FontBold
    $BtnWireOmp = Add-Btn $CfgPanel "Sync omp"        428 80 105 ([System.Drawing.Color]::FromArgb(45, 105, 190)) $FontUI
    $BtnRunOmp  = Add-Btn $CfgPanel "Launch omp"      542 80 110 $BgPanel $FontUI
    $BtnOpenLog = Add-Btn $CfgPanel "Open Debug Log"  661 80 123 $BgPanel $FontUI

    $LogPanel = New-Object System.Windows.Forms.Panel
    $LogPanel.Dock = "Fill"; $LogPanel.Padding = New-Object System.Windows.Forms.Padding(16, 0, 16, 16)
    $Form.Controls.Add($LogPanel); $LogPanel.BringToFront()

    $TxtLog = New-Object System.Windows.Forms.TextBox
    $TxtLog.Multiline = $true; $TxtLog.ReadOnly = $true; $TxtLog.ScrollBars = "Vertical"; $TxtLog.Dock = "Fill"
    $TxtLog.MaxLength = [int]::MaxValue; $TxtLog.Font = $FontMono; $TxtLog.BorderStyle = "FixedSingle"
    $TxtLog.BackColor = [System.Drawing.Color]::FromArgb(16, 16, 20)
    $TxtLog.ForeColor = [System.Drawing.Color]::FromArgb(200, 215, 225)
    $LogPanel.Controls.Add($TxtLog)

    function Write-GuiLog([string]$msg) {
        $ts = (Get-Date).ToString("HH:mm:ss")
        $TxtLog.AppendText("[$ts] $msg`r`n")
        $TxtLog.ScrollToCaret()
        Write-DebugFile $msg
    }

    # Updates defaultThinkingLevel in ~/.omp/agent/config.yml
    function Set-OmpThinkingLevel([string]$Level) {
        if (-not $Level) { return }
        try {
            $ompAgentDir = Split-Path -Parent $OmpConfigPath
            if (-not (Test-Path -LiteralPath $ompAgentDir)) {
                New-Item -Path $ompAgentDir -ItemType Directory -Force -ErrorAction SilentlyContinue | Out-Null
            }
            if (Test-Path -LiteralPath $OmpConfigPath) {
                $content = [System.IO.File]::ReadAllText($OmpConfigPath, [System.Text.Encoding]::UTF8)
                if ($content -match '(?m)^(\s*defaultThinkingLevel:\s*).*$') {
                    $content = [regex]::Replace($content, '(?m)^(\s*defaultThinkingLevel:\s*).*$', "`${1}$Level")
                } else {
                    $content = $content.TrimEnd() + "`r`ndefaultThinkingLevel: $Level`r`n"
                }
                [System.IO.File]::WriteAllText($OmpConfigPath, $content, [System.Text.Encoding]::UTF8)
                Write-GuiLog "Updated ~/.omp/agent/config.yml -> defaultThinkingLevel: $Level"
            } else {
                [System.IO.File]::WriteAllText($OmpConfigPath, "defaultThinkingLevel: $Level`r`n", [System.Text.Encoding]::UTF8)
                Write-GuiLog "Created ~/.omp/agent/config.yml with defaultThinkingLevel: $Level"
            }
        } catch {
            Write-GuiLog "WARNING: Could not update config.yml thinking level: $($_.Exception.Message)"
        }
    }

    $CmbThinking.Add_SelectedIndexChanged({
        $lvl = [string]$CmbThinking.SelectedItem
        if ($lvl) { Set-OmpThinkingLevel -Level $lvl }
    })

    function Stop-ProcessTree($Proc) {
        if ($Proc) {
            try { if (-not $Proc.HasExited) { & taskkill.exe /F /T /PID $Proc.Id 2>$null | Out-Null } } catch {}
            try { $Proc.Dispose() } catch {}
        }
    }

    function Start-InteractiveCmd([string]$CmdChain, [string]$LogFile, [ref]$PosRef) {
        try { [System.IO.File]::WriteAllText($LogFile, "", [System.Text.Encoding]::UTF8) }
        catch { Remove-Item -LiteralPath $LogFile -Force -ErrorAction SilentlyContinue; New-Item -Path $LogFile -ItemType File -Force -ErrorAction SilentlyContinue | Out-Null }
        $PosRef.Value = 0

        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = "cmd.exe"; $psi.Arguments = "/c $CmdChain"
        $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true; $psi.RedirectStandardInput = $true

        $proc = New-Object System.Diagnostics.Process
        $proc.StartInfo = $psi
        [void]$proc.Start()
        return $proc
    }

    function Read-UnlockedFileTail([string]$Path, [ref]$LastPos) {
        if (-not (Test-Path -LiteralPath $Path)) { return "" }
        $fs = $null; $sr = $null
        try {
            $fs = New-Object System.IO.FileStream($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
            if ($fs.Length -lt $LastPos.Value) { $LastPos.Value = 0 }
            if ($fs.Length -gt $LastPos.Value) {
                [void]$fs.Seek($LastPos.Value, [System.IO.SeekOrigin]::Begin)
                $sr = New-Object System.IO.StreamReader($fs, [System.Text.Encoding]::UTF8)
                $newText = $sr.ReadToEnd()
                $LastPos.Value = $fs.Position
                return $newText
            }
        } catch {
            Write-DebugFile "Read-UnlockedFileTail error: $($_.Exception.Message)"
        } finally {
            if ($sr) { $sr.Dispose() } elseif ($fs) { $fs.Dispose() }
        }
        return ""
    }

    function Invoke-InteractivePromptCheck([string]$DeltaText, $Proc) {
        if ($script:IsPrompting -or -not $DeltaText -or -not $Proc) { return }
        try { if ($Proc.HasExited) { return } } catch { return }

        if ($DeltaText -match '(\[y/N\]|\[Y/n\]|\(y/n\))\s*$') {
            $script:IsPrompting = $true
            try {
                $promptQuestion = (($DeltaText.Trim() -split "`r?`n") | Select-Object -Last 3) -join "`n"
                $ans = [System.Windows.Forms.MessageBox]::Show(
                    $Form, "$promptQuestion`n`nClick YES to send 'y' or NO to send 'n'.",
                    "Confirmation Required", [System.Windows.Forms.MessageBoxButtons]::YesNo, [System.Windows.Forms.MessageBoxIcon]::Question
                )
                $reply = if ($ans -eq [System.Windows.Forms.DialogResult]::Yes) { "y" } else { "n" }
                $TxtLog.AppendText("$reply`r`n"); $TxtLog.ScrollToCaret()
                $Proc.StandardInput.WriteLine($reply); $Proc.StandardInput.Flush()
                Write-DebugFile "Sent interactive reply '$reply' to PID $($Proc.Id)"
            } catch {
                Write-DebugFile "Interactive input error: $($_.Exception.Message)"
            } finally {
                $script:IsPrompting = $false
            }
        }
    }

    function Test-Port8080 {
        $tcp = $null
        try {
            $tcp = New-Object System.Net.Sockets.TcpClient
            $iar = $tcp.BeginConnect("127.0.0.1", 8080, $null, $null)
            if ($iar.AsyncWaitHandle.WaitOne(120, $false) -and $tcp.Connected) { $tcp.EndConnect($iar); return $true }
        } catch {} finally { if ($tcp) { try { $tcp.Close(); $tcp.Dispose() } catch {} } }
        return $false
    }

    $strataCmd = Get-Command strata -ErrorAction SilentlyContinue | Select-Object -First 1
    $ompCmd    = Get-Command omp -ErrorAction SilentlyContinue | Select-Object -First 1
    Write-GuiLog "Debug log file: $DebugLogPath"
    if ($strataCmd) { Write-GuiLog "Resolved strata CLI: $(if ($strataCmd.Source) { $strataCmd.Source } else { $strataCmd.Definition })" }
    else { Write-GuiLog "WARNING: 'strata' not found on PATH." }
    if ($ompCmd) { Write-GuiLog "Resolved omp CLI:    $(if ($ompCmd.Source) { $ompCmd.Source } else { $ompCmd.Definition })" }
    else { Write-GuiLog "WARNING: 'omp' not found on PATH." }

    $BtnStart.Add_Click({
        try {
            $model = $CmbModel.Text.Trim(); $ctx = $CmbCtx.Text.Trim(); $speed = $CmbSpeed.Text.Trim(); $preset = $TxtPreset.Text.Trim()
            if (-not $model -or -not $preset) {
                [System.Windows.Forms.MessageBox]::Show("Please enter a model and preset name.", "Missing Input")
                return
            }
            Stop-ProcessTree $script:LaunchProc
            $engLog = Join-Path $LaunchDir "$preset.engine.log"
            $script:CurrentEngineLog = $engLog
            if (Test-Path -LiteralPath $engLog) { try { [System.IO.File]::WriteAllText($engLog, "", [System.Text.Encoding]::UTF8) } catch {} }
            $script:EngineLogPos = 0

            $cmdLine = "call strata launch --model `"$model`" --context `"$ctx`" --speed `"$speed`" --save-preset `"$preset`" --no-tui > `"$TempLaunchLog`" 2>&1"
            Write-GuiLog "Starting Engine: strata launch --model `"$model`" --context `"$ctx`" --speed `"$speed`" --save-preset `"$preset`" --no-tui"
            $script:LaunchProc = Start-InteractiveCmd -CmdChain $cmdLine -LogFile $TempLaunchLog -PosRef ([ref]$script:LaunchLogPos)
        } catch { Write-GuiLog "ERROR starting engine: $($_.Exception.Message)" }
    })

    $BtnStop.Add_Click({
        try {
            Stop-ProcessTree $script:ActionProc
            $preset = $TxtPreset.Text.Trim()
            Write-GuiLog "Stopping Strata engine gracefully (--preset `"$preset`")..."
            $stopCmd = if ($preset) { "call strata launch --stop --preset `"$preset`" >> `"$TempCmdLog`" 2>&1 & call strata launch --stop >> `"$TempCmdLog`" 2>&1" }
                       else { "call strata launch --stop >> `"$TempCmdLog`" 2>&1" }
            $script:ActionProc = Start-InteractiveCmd -CmdChain $stopCmd -LogFile $TempCmdLog -PosRef ([ref]$script:CmdLogPos)
        } catch { Write-GuiLog "ERROR stopping engine: $($_.Exception.Message)" }
    })

    $BtnKill.Add_Click({
        try {
            Write-GuiLog "Force-killing strata processes..."
            $killed = $false
            foreach ($pName in @("strata-dsv41", "strata-server", "strata")) {
                Get-Process -Name $pName -ErrorAction SilentlyContinue | ForEach-Object {
                    Write-GuiLog "Terminating $($_.ProcessName) (PID $($_.Id))..."
                    Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue; $killed = $true
                }
            }
            Stop-ProcessTree $script:LaunchProc; Stop-ProcessTree $script:ActionProc
            Write-GuiLog $(if ($killed) { "Engine terminated. VRAM and System RAM freed." } else { "No running strata engine process found." })
        } catch { Write-GuiLog "ERROR during Force Kill: $($_.Exception.Message)" }
    })

    $BtnWireOmp.Add_Click({
        try {
            Stop-ProcessTree $script:ActionProc
            $lvl = [string]$CmbThinking.SelectedItem
            Write-GuiLog "Running: strata connect omp --write && omp config set defaultThinkingLevel $lvl && strata connect omp --check"
            $ompCmdChain = "call strata connect omp --write >> `"$TempCmdLog`" 2>&1 & call omp config set defaultThinkingLevel $lvl >> `"$TempCmdLog`" 2>&1 & call strata connect omp --check >> `"$TempCmdLog`" 2>&1"
            $script:ActionProc = Start-InteractiveCmd -CmdChain $ompCmdChain -LogFile $TempCmdLog -PosRef ([ref]$script:CmdLogPos)
        } catch { Write-GuiLog "ERROR syncing omp: $($_.Exception.Message)" }
    })

    $BtnRunOmp.Add_Click({
        try {
            $lvl = [string]$CmbThinking.SelectedItem
            if ($lvl) { Set-OmpThinkingLevel -Level $lvl }
            $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
            $dlg.Description = "Select workspace folder to launch omp in (or Cancel for user home):"
            $workDir = if ($dlg.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { $dlg.SelectedPath } else { $env:USERPROFILE }
            Write-GuiLog "Launching omp (defaultThinkingLevel: $lvl) in folder: $workDir"
            Start-Process -FilePath "cmd.exe" -WorkingDirectory $workDir -ArgumentList "/k omp"
        } catch { Write-GuiLog "ERROR launching omp: $($_.Exception.Message)" }
    })

    $BtnOpenLog.Add_Click({
        try { Start-Process -FilePath "notepad.exe" -ArgumentList "`"$DebugLogPath`"" }
        catch { Write-GuiLog "Could not open notepad: $($_.Exception.Message)" }
    })

    $Timer = New-Object System.Windows.Forms.Timer
    $Timer.Interval = 400
    $Timer.Add_Tick({
        $launchDelta = Read-UnlockedFileTail -Path $TempLaunchLog -LastPos ([ref]$script:LaunchLogPos)
        if ($launchDelta) {
            $TxtLog.AppendText($launchDelta.Replace("`r`n", "`n").Replace("`r", "`n").Replace("`n", "`r`n"))
            $TxtLog.ScrollToCaret()
            Write-DebugFile "[LAUNCH_OUT] $($launchDelta.Trim())"
            Invoke-InteractivePromptCheck -DeltaText $launchDelta -Proc $script:LaunchProc
        }

        $cmdDelta = Read-UnlockedFileTail -Path $TempCmdLog -LastPos ([ref]$script:CmdLogPos)
        if ($cmdDelta) {
            $TxtLog.AppendText($cmdDelta.Replace("`r`n", "`n").Replace("`r", "`n").Replace("`n", "`r`n"))
            $TxtLog.ScrollToCaret()
            Write-DebugFile "[CMD_OUT] $($cmdDelta.Trim())"
            Invoke-InteractivePromptCheck -DeltaText $cmdDelta -Proc $script:ActionProc
        }

        $preset = $TxtPreset.Text.Trim()
        if ($preset) {
            $engLog = Join-Path $LaunchDir "$preset.engine.log"
            if ($script:CurrentEngineLog -ne $engLog) {
                $script:CurrentEngineLog = $engLog
                $script:EngineLogPos = if (Test-Path -LiteralPath $engLog) { (Get-Item -LiteralPath $engLog).Length } else { 0 }
            }
            $engDelta = Read-UnlockedFileTail -Path $engLog -LastPos ([ref]$script:EngineLogPos)
            if ($engDelta) {
                $TxtLog.AppendText($engDelta.Replace("`r`n", "`n").Replace("`r", "`n").Replace("`n", "`r`n"))
                $TxtLog.ScrollToCaret()
                Write-DebugFile "[ENGINE_LOG] $($engDelta.Trim())"
            }
        }

        $engProc  = Get-Process -Name "strata-dsv41", "strata-server" -ErrorAction SilentlyContinue | Select-Object -First 1
        $portOpen = Test-Port8080

        if ($engProc -and $portOpen) {
            $ramGB = [math]::Round($engProc.WorkingSet64 / 1GB, 1)
            $LblStatus.Text = "[SERVING - READY]"
            $LblStatus.ForeColor = [System.Drawing.Color]::FromArgb(65, 215, 95)
            $LblProcInfo.Text = "$($engProc.ProcessName).exe (PID $($engProc.Id)) | RAM: $ramGB GB | http://127.0.0.1:8080/v1"
        } elseif ($engProc -or ($script:LaunchProc -and -not $script:LaunchProc.HasExited)) {
            $ramInfo = if ($engProc) { "PID $($engProc.Id) | RAM: $([math]::Round($engProc.WorkingSet64 / 1GB, 1)) GB" } else { "Initializing..." }
            $LblStatus.Text = "[LOADING MODEL...]"
            $LblStatus.ForeColor = [System.Drawing.Color]::FromArgb(245, 185, 45)
            $LblProcInfo.Text = "Starting engine ($ramInfo)"
        } else {
            $LblStatus.Text = "[STOPPED]"
            $LblStatus.ForeColor = [System.Drawing.Color]::FromArgb(230, 75, 75)
            $LblProcInfo.Text = "No engine process running"
        }
    })
    $Timer.Start()

    $Form.Add_Shown({ $Form.Activate() })

    $Form.Add_FormClosing({
        param($sender, $e)
        $engProc = Get-Process -Name "strata-dsv41", "strata-server" -ErrorAction SilentlyContinue
        if ($engProc) {
            $ans = [System.Windows.Forms.MessageBox]::Show(
                "The Strata Engine is currently running.`n`nYES = Kill engine before exiting`nNO = Leave engine running in background`nCANCEL = Stay in controller",
                "Strata Engine Running", [System.Windows.Forms.MessageBoxButtons]::YesNoCancel, [System.Windows.Forms.MessageBoxIcon]::Question
            )
            if ($ans -eq [System.Windows.Forms.DialogResult]::Cancel) {
                if ($e) { $e.Cancel = $true }; $_.Cancel = $true; return
            }
            if ($ans -eq [System.Windows.Forms.DialogResult]::Yes) {
                $engProc | Stop-Process -Force -ErrorAction SilentlyContinue
                Stop-ProcessTree $script:LaunchProc
            }
        }
        Stop-ProcessTree $script:ActionProc
        $Timer.Stop()
    })

    [void]$Form.ShowDialog()
    $Form.Dispose()

} catch {
    Set-ConsoleVisibility -Visible $true
    $errText = $_ | Out-String
    Write-DebugFile "Unhandled GUI Exception: $errText"
    try { [System.Windows.Forms.MessageBox]::Show($errText, "Strata Controller Error", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error) } catch {}
    throw
}
# ::PS_CODE_END::