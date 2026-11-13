:: StrataController.bat
@echo off
setlocal EnableExtensions
set "STRATA_SELF=%~f0"
set "STRATA_BOOTLOG=%TEMP%\StrataController.debug.log"
echo [%DATE% %TIME%] Bootstrap starting from "%STRATA_SELF%" > "%STRATA_BOOTLOG%"

powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -Command "try { $raw = [IO.File]::ReadAllText($env:STRATA_SELF, [Text.Encoding]::UTF8); $ms = '::PS_CODE_' + 'START::'; $me = '::PS_CODE_' + 'END::'; $i = $raw.IndexOf($ms); $e = $raw.IndexOf($me); if ($i -lt 0 -or $e -lt 0) { throw 'Script markers missing or file truncated!' }; & ([scriptblock]::Create($raw.Substring($i + $ms.Length, $e - $i - $ms.Length))) } catch { $err = $_ | Out-String; Add-Content -LiteralPath $env:STRATA_BOOTLOG -Value ('[FATAL] ' + $err); Write-Host $err -ForegroundColor Red; exit 1 }"

if errorlevel 1 (
    echo. & echo [ERROR] StrataController failed. Log: "%STRATA_BOOTLOG%" & echo.
    if exist "%STRATA_BOOTLOG%" type "%STRATA_BOOTLOG%"
    pause & exit /b 1
)
exit /b 0
::PS_CODE_START::
$ErrorActionPreference = "Continue"
$DebugLogPath = if ($env:STRATA_BOOTLOG) { $env:STRATA_BOOTLOG } else { Join-Path $env:TEMP "StrataController.debug.log" }

try {
    Get-CimInstance Win32_Process -Filter "Name='cmd.exe' OR Name='strata.exe'" -EA 0 |
        Where-Object { $_.CommandLine -match 'strata_gui_|strata connect' } |
        ForEach-Object { & taskkill.exe /F /T /PID $_.ProcessId 2>$null | Out-Null }
    [IO.File]::WriteAllText($DebugLogPath, "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff')] Bootstrap starting`r`n", [Text.Encoding]::UTF8)
} catch {}

function Write-DebugFile([string]$Msg) {
    try { Add-Content -LiteralPath $DebugLogPath -Value "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff')] $Msg" -Encoding UTF8 -EA 0 } catch {}
}

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
[Windows.Forms.Application]::EnableVisualStyles()

if (-not ([Management.Automation.PSTypeName]'StrataGUI.ConsoleWin32').Type) {
    Add-Type -Name ConsoleWin32 -Namespace StrataGUI -MemberDefinition @'
        [DllImport("kernel32.dll")] public static extern IntPtr GetConsoleWindow();
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder s, int n);
'@
}

function Get-WndClass([IntPtr]$h) {
    if ($h -eq [IntPtr]::Zero) { return "" }
    $sb = New-Object Text.StringBuilder 256; [void][StrataGUI.ConsoleWin32]::GetClassName($h, $sb, 256); return $sb.ToString()
}

$script:ConsoleHandles = New-Object Collections.Generic.List[IntPtr]
$validClasses = @("ConsoleWindowClass", "CASCADIA_HOSTING_WINDOW_CLASS")
foreach ($h in @([StrataGUI.ConsoleWin32]::GetForegroundWindow(), [StrataGUI.ConsoleWin32]::GetConsoleWindow())) {
    if ($h -ne [IntPtr]::Zero -and -not $script:ConsoleHandles.Contains($h) -and ($validClasses -contains (Get-WndClass $h) -or [StrataGUI.ConsoleWin32]::IsWindowVisible($h))) {
        [void]$script:ConsoleHandles.Add($h)
    }
}

function Set-ConsoleVisibility([bool]$Visible) {
    foreach ($h in $script:ConsoleHandles) {
        if ($h -eq [IntPtr]::Zero -or ($script:Form -and $h -eq $script:Form.Handle)) { continue }
        if ($Visible) { [void][StrataGUI.ConsoleWin32]::ShowWindow($h, 9); [void][StrataGUI.ConsoleWin32]::ShowWindow($h, 5) }
        else {
            [void][StrataGUI.ConsoleWin32]::ShowWindow($h, 0); [void][StrataGUI.ConsoleWin32]::ShowWindowAsync($h, 0)
            if ([StrataGUI.ConsoleWin32]::IsWindowVisible($h)) { [void][StrataGUI.ConsoleWin32]::ShowWindow($h, 6); [void][StrataGUI.ConsoleWin32]::ShowWindowAsync($h, 2) }
        }
    }
}
Set-ConsoleVisibility $false

try {
    $regPath = [Environment]::GetEnvironmentVariable("Path", "User") + ";" + [Environment]::GetEnvironmentVariable("Path", "Machine")
    $env:PATH = (@("$env:USERPROFILE\.local\bin", "$env:LOCALAPPDATA\Strata\bin", "$env:LOCALAPPDATA\Strata", "$env:LOCALAPPDATA\Programs\Strata", "$env:USERPROFILE\.omp\bin", "$env:APPDATA\npm") +
        ($env:PATH -split ';') + ($regPath -split ';' | ForEach-Object { [Environment]::ExpandEnvironmentVariables($_.Trim()) }) | Where-Object { $_ } | Select-Object -Unique) -join ";"

    $StrataLocalDir = Join-Path $env:LOCALAPPDATA "Strata"
    $LaunchDir      = Join-Path $StrataLocalDir "launch"
    $InstallDir     = Join-Path $StrataLocalDir "install"
    $OmpConfigPath  = Join-Path $env:USERPROFILE ".omp\agent\config.yml"
    $TempLaunchLog  = Join-Path $env:TEMP "strata_gui_launch.log"
    $TempCmdLog     = Join-Path $env:TEMP "strata_gui_cmd.log"

    Get-ChildItem $env:TEMP -Filter "strata_gui_*.log" -EA 0 | ForEach-Object { try { Remove-Item $_.FullName -Force -EA 0 } catch {} }
    [IO.File]::WriteAllText($TempLaunchLog, "", [Text.Encoding]::UTF8); [IO.File]::WriteAllText($TempCmdLog, "", [Text.Encoding]::UTF8)

    function Get-EngineProc {
        Get-Process "strata-dsv41", "strata-server", "strata" -EA 0 |
            Where-Object { $_.ProcessName -ne 'strata' -or $_.Path -match 'engine' -or $_.WorkingSet64 -gt 250MB } |
            Sort-Object WorkingSet64 -Descending | Select-Object -First 1
    }

    if (-not (Get-EngineProc) -and (Test-Path $LaunchDir)) {
        Get-ChildItem $LaunchDir -Filter "*.engine.log" -EA 0 | ForEach-Object { try { [IO.File]::WriteAllText($_.FullName, "", [Text.Encoding]::UTF8) } catch {} }
    }

    # Resolve model tag/id into explicit Strata Family + clean Model name
    function Resolve-ModelInfo([string]$RawModel, [string]$RawFamily = "") {
        $m = $RawModel.Trim() -replace '^strata-', '' -replace '\.json$', ''
        $f = $RawFamily.Trim().ToLower()
        if ($m -match '^deepseek-v4\.1-flash-(.+)$') { $m = $Matches[1]; $f = "deepseek41" }
        elseif ($m -match '^(?:qwen3\.8-flash-next-)?gyro-(.+)$') { $m = $Matches[1]; $f = "gyro" }
        elseif ($m -match '^(coder|swift|unsloth|nvfp4|qwen36)-(.+)$') { if (-not $f) { $f = $Matches[1].ToLower() }; $m = $Matches[2] }
        if (-not $f) {
            $f = switch -Regex ($m) {
                '^(ist-?a?-?das|deepseek|dsv41)|bpw$' { "deepseek41"; break }
                '^iq1_m$'                             { "coder"; break }
                '^gyro-'                              { "gyro"; break }
                '^nvfp4'                              { "nvfp4"; break }
                '^(ud-|q8_0)'                         { "unsloth"; break }
                default                               { "qwen" }
            }
        }
        $fTitle = switch ($f) {
            "deepseek41" { "DeepSeek V4.1-Flash" }
            "coder"      { "Qwen3.8 Coder" }
            "gyro"       { "Qwen3.8 Gyro" }
            "swift"      { "Qwen3.8 Swift 1.5" }
            "unsloth"    { "Qwen3.8 Unsloth" }
            "nvfp4"      { "Qwen3.8 NVFP4" }
            "qwen36"     { "Qwen3.6-35B-A3B" }
            default      { "Qwen3.8-Flash-Next" }
        }
        return @{ Family = $f; FamilyTitle = $fTitle; Model = $m; Display = "[$fTitle]  $m" }
    }

    # Discover ONLY downloaded, prepared models ready to launch
    $script:ModelMap = [ordered]@{}
    function Add-ReadyModel([string]$Mod, [string]$Fam = "") {
        if (-not $Mod) { return }
        $info = Resolve-ModelInfo $Mod $Fam
        if (-not $script:ModelMap.Contains($info.Display)) { $script:ModelMap[$info.Display] = $info }
    }

    # 1. Query strata CLI installed models inventory (component_status == 'present')
    if (Get-Command strata -EA 0) {
        try {
            $cliJson = (& strata models list --installed --output json 2>$null | Out-String)
            if ($cliJson -match '\{') {
                $parsed = $cliJson | ConvertFrom-Json -EA 0
                $items = if ($parsed.models) { $parsed.models } elseif ($parsed.items) { $parsed.items } else { @($parsed) }
                foreach ($it in $items) {
                    if ($it.component_status -eq 'present' -or $it.ready -eq $true) {
                        $id = if ($it.model) { $it.model } elseif ($it.model_id) { $it.model_id } else { $it.id }
                        Add-ReadyModel ([string]$id) ([string]$it.family)
                    }
                }
            }
        } catch {}
    }

    # 2. Check prepared pack folders on disk across known Strata data roots
    $dataRoots = @($StrataLocalDir, "$StrataLocalDir\Strata-data", "$env:USERPROFILE\.strata\Strata-data", "$env:USERPROFILE\Strata", "$env:USERPROFILE\Strata-data", "$env:USERPROFILE\Documents\Strata")
    foreach ($cfgFile in @("$StrataLocalDir\settings.json", "$StrataLocalDir\app-settings.json", "$env:USERPROFILE\.config\strata\settings.json")) {
        if (Test-Path $cfgFile) {
            try { $c = Get-Content $cfgFile -Raw | ConvertFrom-Json; foreach ($p in @($c.data_dir, $c.models_dir)) { if ($p) { $dataRoots += $p } } } catch {}
        }
    }
    $readyPacks = @{}
    foreach ($root in ($dataRoots | Select-Object -Unique)) {
        $pkDir = Join-Path $root "packs"
        if (Test-Path $pkDir) {
            Get-ChildItem $pkDir -Directory -EA 0 | Where-Object { (Get-ChildItem $_.FullName -File -EA 0 | Measure-Object).Count -gt 0 } | ForEach-Object {
                $readyPacks[$_.Name.ToLower()] = $true; Add-ReadyModel $_.Name
            }
        }
    }

    # 3. Check install\strata-*.json and launch\*.json configs with verified on-disk packs/logs
    foreach ($dir in @($InstallDir, $LaunchDir)) {
        if (-not (Test-Path $dir)) { continue }
        Get-ChildItem $dir -Filter "*.json" -EA 0 | ForEach-Object {
            try {
                $j = Get-Content $_.FullName -Raw | ConvertFrom-Json -EA 0
                if (-not $j) { return }
                $packOk = $false
                if ($j.args) {
                    $idx = [array]::IndexOf($j.args, "--pack")
                    if ($idx -ge 0 -and ($idx + 1) -lt $j.args.Count -and (Test-Path $j.args[$idx + 1])) { $packOk = $true }
                }
                $mName = if ($j.model) { [string]$j.model } elseif ($_.BaseName -like "strata-*") { $_.BaseName } else { "" }
                if ($mName) {
                    $info = Resolve-ModelInfo $mName ([string]$j.family)
                    $engLogExists = Test-Path (Join-Path $LaunchDir "$($_.BaseName).engine.log")
                    if ($packOk -or $readyPacks[$info.Model.ToLower()] -or $engLogExists -or ($j.model_path -and (Test-Path $j.model_path))) {
                        Add-ReadyModel $info.Model $info.Family
                    }
                }
            } catch {}
        }
    }

    $InitialThinking = "medium"
    if (Test-Path $OmpConfigPath) {
        if ((Get-Content $OmpConfigPath -Raw -EA 0) -match '(?m)^\s*defaultThinkingLevel:\s*([a-zA-Z0-9_-]+)') { $InitialThinking = $Matches[1].ToLower() }
    }

    $BgDark = [Drawing.Color]::FromArgb(24,24,28); $BgPanel = [Drawing.Color]::FromArgb(34,34,40); $BgInput = [Drawing.Color]::FromArgb(45,45,54)
    $FgText = [Drawing.Color]::FromArgb(235,235,240); $FgMuted = [Drawing.Color]::FromArgb(165,165,180)
    $FontUI = New-Object Drawing.Font("Segoe UI", 9.5); $FontBold = New-Object Drawing.Font("Segoe UI Semibold", 9.5); $FontMono = New-Object Drawing.Font("Consolas", 9)

    $Form = New-Object Windows.Forms.Form -Property @{
        Text = "Strata Engine Controller"; Size = New-Object Drawing.Size(860, 620); MinimumSize = New-Object Drawing.Size(780, 500)
        StartPosition = "CenterScreen"; BackColor = $BgDark; ForeColor = $FgText; Font = $FontUI; AutoScaleMode = "Dpi"
    }
    $script:Form = $Form

    function New-Grid($Parent, [int]$H, [double[]]$Cols, [double[]]$Rows, $Bg = $BgDark, $Pad = @(12,4,12,4)) {
        $g = New-Object Windows.Forms.TableLayoutPanel -Property @{
            Dock = "Top"; Height = $H; ColumnCount = $Cols.Count; RowCount = $Rows.Count; BackColor = $Bg
            Padding = New-Object Windows.Forms.Padding($Pad[0], $Pad[1], $Pad[2], $Pad[3])
        }
        foreach ($w in $Cols) { [void]$g.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle($(if ($w -le 0) { "AutoSize" } else { "Percent" }), [single][math]::Max($w, 0)))) }
        foreach ($h in $Rows) { [void]$g.RowStyles.Add((New-Object Windows.Forms.RowStyle("Percent", [single]$h))) }
        $Parent.Controls.Add($g); $g.BringToFront(); return $g
    }
    function Add-Lbl($G, $T, $Col, $Row = 0, $F = $FontUI, $C = $FgText, $Auto = $false) {
        $l = New-Object Windows.Forms.Label -Property @{
            Text = $T; Font = $F; ForeColor = $C; AutoSize = $Auto; AutoEllipsis = (-not $Auto)
            Dock = "Fill"; TextAlign = "MiddleLeft"; Margin = New-Object Windows.Forms.Padding(3, 0, 10, 0)
        }
        $G.Controls.Add($l, $Col, $Row); return $l
    }
    function Add-Cmb($G, $Col, $Items, $Default, $Style = "DropDownList") {
        $c = New-Object Windows.Forms.ComboBox -Property @{
            Dock = "Fill"; BackColor = $BgInput; ForeColor = $FgText; FlatStyle = "Flat"
            DropDownStyle = $Style; DropDownWidth = 320; Margin = New-Object Windows.Forms.Padding(3, 2, 3, 2)
        }
        if ($Items) { [void]$c.Items.AddRange(@($Items)) }
        if ($Default -and $c.Items.Contains($Default)) { $c.SelectedItem = $Default } elseif ($c.Items.Count -gt 0) { $c.SelectedIndex = 0 }
        $G.Controls.Add($c, $Col, 1); return $c
    }
    function Add-Btn($G, $T, $Col, $Bg, $F = $FontUI) {
        $b = New-Object Windows.Forms.Button -Property @{
            Text = $T; Font = $F; Dock = "Fill"; BackColor = $Bg; ForeColor = $FgText
            FlatStyle = "Flat"; AutoEllipsis = $true; Margin = New-Object Windows.Forms.Padding(3, 4, 3, 4)
        }
        $b.FlatAppearance.BorderSize = 0; $G.Controls.Add($b, $Col, 0); return $b
    }

    $StatusGrid  = New-Grid $Form 46 @(0, 100) @(100) $BgPanel @(12, 6, 12, 6)
    $LblStatus   = Add-Lbl $StatusGrid "[CHECKING ENGINE...]" 0 0 (New-Object Drawing.Font("Segoe UI Semibold", 11)) $FgText $true
    $LblProcInfo = Add-Lbl $StatusGrid "Endpoint: http://127.0.0.1:8080/v1" 1 0 $FontUI $FgMuted

    $InputGrid   = New-Grid $Form 74 @(36, 11, 13, 15, 25) @(42, 58) $BgDark @(12, 6, 12, 2)
    $LblModel    = Add-Lbl $InputGrid "Ready Model [Family]:" 0 0 $FontBold
    [void](Add-Lbl $InputGrid "Context:" 1); [void](Add-Lbl $InputGrid "Speed (DS41):" 2); [void](Add-Lbl $InputGrid "OMP Thinking:" 3); [void](Add-Lbl $InputGrid "Preset (--save-preset):" 4)
    $CmbModel    = Add-Cmb $InputGrid 0 @($script:ModelMap.Keys) $null "DropDownList"
    $CmbCtx      = Add-Cmb $InputGrid 1 @("256k","128k","64k","32k","16k","8k") "256k" "DropDown"
    $CmbSpeed    = Add-Cmb $InputGrid 2 @("full","faster") "full"
    $CmbThinking = Add-Cmb $InputGrid 3 @("auto","off","minimal","low","medium","high","xhigh","max") $InitialThinking
    $TxtPreset   = New-Object Windows.Forms.TextBox -Property @{ Dock = "Fill"; BackColor = $BgInput; ForeColor = $FgText; BorderStyle = "FixedSingle"; Margin = New-Object Windows.Forms.Padding(3, 2, 3, 2) }
    $InputGrid.Controls.Add($TxtPreset, 4, 1)

    $SyncModelSelection = {
        $sel = if ($CmbModel.SelectedItem) { $script:ModelMap[[string]$CmbModel.SelectedItem] } else { $null }
        $c = $CmbCtx.Text.Trim().ToLower().Replace("k", "")
        if ($sel) {
            $LblModel.Text = "Model [$($sel.FamilyTitle) (--family $($sel.Family))]:"
            $CmbSpeed.Enabled = ($sel.Family -eq "deepseek41")
            $TxtPreset.Text = if ($sel.Model -eq "ist-das-coder") { "ds41-coder-$c" } else { "$($sel.Family)-$(($sel.Model -replace '[^a-zA-Z0-9\-]','-'))-$c" }
        } else {
            $LblModel.Text = "Ready Model [None Found]:"
            $CmbSpeed.Enabled = $false
        }
    }
    $CmbModel.Add_SelectedIndexChanged($SyncModelSelection)
    $CmbCtx.Add_SelectedIndexChanged($SyncModelSelection); $CmbCtx.Add_TextChanged($SyncModelSelection)
    & $SyncModelSelection

    $BtnGrid    = New-Grid $Form 56 @(17, 18, 15, 15, 16, 19) @(100) $BgDark @(12, 2, 12, 8)
    $BtnStart   = Add-Btn $BtnGrid "Start Engine"    0 ([Drawing.Color]::FromArgb(35,134,54))  $FontBold
    $BtnStop    = Add-Btn $BtnGrid "Stop (Graceful)" 1 ([Drawing.Color]::FromArgb(180,105,14)) $FontBold
    $BtnKill    = Add-Btn $BtnGrid "Force Kill"      2 ([Drawing.Color]::FromArgb(190,40,40))  $FontBold
    $BtnWireOmp = Add-Btn $BtnGrid "Sync omp"        3 ([Drawing.Color]::FromArgb(45,105,190))
    $BtnRunOmp  = Add-Btn $BtnGrid "Launch omp"      4 $BgPanel
    $BtnOpenLog = Add-Btn $BtnGrid "Open Debug Log"  5 $BgPanel
    
    $LogPanel = New-Object Windows.Forms.Panel -Property @{ Dock = "Fill"; Padding = New-Object Windows.Forms.Padding(16, 0, 16, 16) }
    $Form.Controls.Add($LogPanel); $LogPanel.BringToFront()
    $TxtLog = New-Object Windows.Forms.TextBox -Property @{ Multiline = $true; ReadOnly = $true; ScrollBars = "Vertical"; Dock = "Fill"; MaxLength = [int]::MaxValue; Font = $FontMono; BorderStyle = "FixedSingle"; BackColor = [Drawing.Color]::FromArgb(16,16,20); ForeColor = [Drawing.Color]::FromArgb(200,215,225) }
    $LogPanel.Controls.Add($TxtLog)

    function Write-GuiLog([string]$msg) { $TxtLog.AppendText("[$(Get-Date -Format 'HH:mm:ss')] $msg`r`n"); $TxtLog.ScrollToCaret(); Write-DebugFile $msg }

    function Set-OmpThinkingLevel([string]$Level) {
        if (-not $Level) { return }
        try {
            $dir = Split-Path -Parent $OmpConfigPath
            if (-not (Test-Path $dir)) { New-Item $dir -ItemType Directory -Force -EA 0 | Out-Null }
            $txt = if (Test-Path $OmpConfigPath) { [IO.File]::ReadAllText($OmpConfigPath, [Text.Encoding]::UTF8) } else { "" }
            $txt = if ($txt -match '(?m)^\s*defaultThinkingLevel:') { [regex]::Replace($txt, '(?m)^(\s*defaultThinkingLevel:\s*).*$', "`${1}$Level") } else { ($txt.TrimEnd() + "`r`ndefaultThinkingLevel: $Level`r`n").TrimStart() }
            [IO.File]::WriteAllText($OmpConfigPath, $txt, [Text.Encoding]::UTF8)
            Write-GuiLog "Updated ~/.omp/agent/config.yml -> defaultThinkingLevel: $Level"
        } catch { Write-GuiLog "WARNING: Could not update config.yml: $($_.Exception.Message)" }
    }
    $CmbThinking.Add_SelectedIndexChanged({ Set-OmpThinkingLevel ([string]$CmbThinking.SelectedItem) })

    $script:EngineLogPos = 0; $script:LaunchLogPos = 0; $script:CmdLogPos = 0
    $script:CurrentEngineLog = ""; $script:LaunchProc = $null; $script:ActionProc = $null; $script:IsPrompting = $false

    function Stop-ProcTree($P) { if ($P) { try { if (-not $P.HasExited) { & taskkill.exe /F /T /PID $P.Id 2>$null | Out-Null } } catch {}; try { $P.Dispose() } catch {} } }

    function Start-InteractiveCmd([string]$CmdChain, [string]$LogFile, [ref]$PosRef) {
        try { [IO.File]::WriteAllText($LogFile, "", [Text.Encoding]::UTF8) } catch {}
        $PosRef.Value = 0
        $p = New-Object Diagnostics.Process
        $p.StartInfo = New-Object Diagnostics.ProcessStartInfo("cmd.exe", "/c $CmdChain") -Property @{ UseShellExecute = $false; CreateNoWindow = $true; RedirectStandardInput = $true }
        [void]$p.Start(); return $p
    }

    function Sync-LogTail([string]$Path, [ref]$LastPos, [string]$Tag, $Proc = $null) {
        if (-not $Path -or -not (Test-Path -LiteralPath $Path)) { return }
        $fs = $null; $sr = $null
        try {
            $fs = New-Object IO.FileStream($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
            if ($fs.Length -lt $LastPos.Value) { $LastPos.Value = 0 }
            if ($fs.Length -gt $LastPos.Value) {
                [void]$fs.Seek($LastPos.Value, [IO.SeekOrigin]::Begin)
                $sr = New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8)
                $delta = $sr.ReadToEnd(); $LastPos.Value = $fs.Position
                if ($delta) {
                    $TxtLog.AppendText(($delta -replace "\r?\n", "`r`n")); $TxtLog.ScrollToCaret()
                    Write-DebugFile "[$Tag] $($delta.Trim())"
                    if (-not $script:IsPrompting -and $Proc -and -not $Proc.HasExited -and $delta -match '(\[y/N\]|\[Y/n\]|\(y/n\))\s*$') {
                        $script:IsPrompting = $true
                        try {
                            $q = (($delta.Trim() -split "\r?\n") | Select-Object -Last 3) -join "`n"
                            $ans = [Windows.Forms.MessageBox]::Show($Form, "$q`n`nClick YES to send 'y' or NO to send 'n'.", "Confirmation Required", "YesNo", "Question")
                            $rep = if ($ans -eq "Yes") { "y" } else { "n" }
                            $TxtLog.AppendText("$rep`r`n"); $Proc.StandardInput.WriteLine($rep); $Proc.StandardInput.Flush()
                        } finally { $script:IsPrompting = $false }
                    }
                }
            }
        } catch {} finally { if ($sr) { $sr.Dispose() } elseif ($fs) { $fs.Dispose() } }
    }

    function Test-Port8080 {
        $tcp = New-Object Net.Sockets.TcpClient
        try { $iar = $tcp.BeginConnect("127.0.0.1", 8080, $null, $null); return ($iar.AsyncWaitHandle.WaitOne(100, $false) -and $tcp.Connected) }
        catch { return $false } finally { $tcp.Close() }
    }

    Write-GuiLog "Debug log: $DebugLogPath"
    if ($script:ModelMap.Count -gt 0) {
        foreach ($v in $script:ModelMap.Values) { Write-GuiLog "Ready Model: $($v.FamilyTitle) [--family $($v.Family) --model $($v.Model)]" }
    } else { Write-GuiLog "WARNING: No downloaded/prepared Strata models detected. Run 'strata launch' in terminal to download one." }

    $BtnStart.Add_Click({
        $sel = if ($CmbModel.SelectedItem) { $script:ModelMap[[string]$CmbModel.SelectedItem] } else { $null }
        $ctx = $CmbCtx.Text.Trim(); $preset = $TxtPreset.Text.Trim()
        if (-not $sel -or -not $preset) { [Windows.Forms.MessageBox]::Show("Select a downloaded ready model and preset name first.", "Missing Input") | Out-Null; return }
        Stop-ProcTree $script:LaunchProc
        $script:CurrentEngineLog = Join-Path $LaunchDir "$preset.engine.log"
        if (Test-Path $script:CurrentEngineLog) { try { [IO.File]::WriteAllText($script:CurrentEngineLog, "", [Text.Encoding]::UTF8) } catch {} }
        $script:EngineLogPos = 0
        $spdArg = if ($sel.Family -eq "deepseek41") { " --speed `"$($CmbSpeed.Text)`"" } else { "" }
        $args = "--family `"$($sel.Family)`" --model `"$($sel.Model)`" --context `"$ctx`"$spdArg --save-preset `"$preset`" --no-tui"
        Write-GuiLog "Starting [$($sel.FamilyTitle)]: strata launch $args"
        $script:LaunchProc = Start-InteractiveCmd "call strata launch $args > `"$TempLaunchLog`" 2>&1" $TempLaunchLog ([ref]$script:LaunchLogPos)
    })

    $BtnStop.Add_Click({
        Stop-ProcTree $script:ActionProc; $p = $TxtPreset.Text.Trim()
        Write-GuiLog "Stopping Strata engine gracefully (--preset `"$p`")..."
        $cmd = if ($p) { "call strata launch --stop --preset `"$p`" >> `"$TempCmdLog`" 2>&1 || call strata launch --stop >> `"$TempCmdLog`" 2>&1" } else { "call strata launch --stop >> `"$TempCmdLog`" 2>&1" }
        $script:ActionProc = Start-InteractiveCmd $cmd $TempCmdLog ([ref]$script:CmdLogPos)
    })

    $BtnKill.Add_Click({
        Write-GuiLog "Force-killing strata processes..."
        $procs = Get-Process "strata-dsv41", "strata-server", "strata" -EA 0
        $procs | ForEach-Object { Write-GuiLog "Terminating $($_.ProcessName) (PID $($_.Id))..."; Stop-Process -Id $_.Id -Force -EA 0 }
        Stop-ProcTree $script:LaunchProc; Stop-ProcTree $script:ActionProc
        Write-GuiLog $(if ($procs) { "Engine terminated. VRAM and System RAM freed." } else { "No running strata process found." })
    })

    $BtnWireOmp.Add_Click({
        Stop-ProcTree $script:ActionProc; $lvl = [string]$CmbThinking.SelectedItem; Set-OmpThinkingLevel $lvl
        Write-GuiLog "Syncing omp (defaultThinkingLevel: $lvl)..."
        $script:ActionProc = Start-InteractiveCmd "call strata connect omp --write >> `"$TempCmdLog`" 2>&1 & call omp config set defaultThinkingLevel $lvl >> `"$TempCmdLog`" 2>&1 & call strata connect omp --check >> `"$TempCmdLog`" 2>&1" $TempCmdLog ([ref]$script:CmdLogPos)
    })

    $BtnRunOmp.Add_Click({
        $lvl = [string]$CmbThinking.SelectedItem; Set-OmpThinkingLevel $lvl
        $dlg = New-Object Windows.Forms.FolderBrowserDialog -Property @{ Description = "Select workspace folder for omp (Cancel for user home):" }
        $dir = if ($dlg.ShowDialog() -eq "OK") { $dlg.SelectedPath } else { $env:USERPROFILE }
        Write-GuiLog "Launching omp (thinking: $lvl) in: $dir"
        Start-Process "cmd.exe" -WorkingDirectory $dir -ArgumentList "/k omp"
    })

    $BtnOpenLog.Add_Click({ Start-Process "notepad.exe" "`"$DebugLogPath`"" })

    $Timer = New-Object Windows.Forms.Timer -Property @{ Interval = 400 }
    $Timer.Add_Tick({
        Sync-LogTail $TempLaunchLog ([ref]$script:LaunchLogPos) "LAUNCH_OUT" $script:LaunchProc
        Sync-LogTail $TempCmdLog    ([ref]$script:CmdLogPos)    "CMD_OUT"    $script:ActionProc
        if ($script:CurrentEngineLog) { Sync-LogTail $script:CurrentEngineLog ([ref]$script:EngineLogPos) "ENGINE_LOG" }

        $eng = Get-EngineProc
        if ($eng -and (Test-Port8080)) {
            $LblStatus.Text = "[SERVING - READY]"; $LblStatus.ForeColor = [Drawing.Color]::FromArgb(65,215,95)
            $LblProcInfo.Text = "$($eng.ProcessName).exe (PID $($eng.Id)) | RAM: $([math]::Round($eng.WorkingSet64 / 1GB, 1)) GB | http://127.0.0.1:8080/v1"
        } elseif ($eng -or ($script:LaunchProc -and -not $script:LaunchProc.HasExited)) {
            $info = if ($eng) { "PID $($eng.Id) | RAM: $([math]::Round($eng.WorkingSet64 / 1GB, 1)) GB" } else { "Initializing..." }
            $LblStatus.Text = "[LOADING MODEL...]"; $LblStatus.ForeColor = [Drawing.Color]::FromArgb(245,185,45)
            $LblProcInfo.Text = "Starting engine ($info)"
        } else {
            $LblStatus.Text = "[STOPPED]"; $LblStatus.ForeColor = [Drawing.Color]::FromArgb(230,75,75)
            $LblProcInfo.Text = "No engine process running"
        }
    })
    $Timer.Start()

    $Form.Add_Shown({ $Form.Activate() })
    $Form.Add_FormClosing({
        $eng = Get-EngineProc
        if ($eng) {
            $ans = [Windows.Forms.MessageBox]::Show("Strata Engine is running.`n`nYES = Kill engine before exiting`nNO = Leave running in background`nCANCEL = Stay in controller", "Strata Engine Running", "YesNoCancel", "Question")
            if ($ans -eq "Cancel") { $_.Cancel = $true; return }
            if ($ans -eq "Yes") { $eng | Stop-Process -Force -EA 0; Stop-ProcTree $script:LaunchProc }
        }
        Stop-ProcTree $script:ActionProc; $Timer.Stop()
    })

    [void]$Form.ShowDialog(); $Form.Dispose()
} catch {
    Set-ConsoleVisibility $true; $err = $_ | Out-String; Write-DebugFile "Unhandled GUI Exception: $err"
    try { [Windows.Forms.MessageBox]::Show($err, "Strata Controller Error", "OK", "Error") | Out-Null } catch {}
    throw
}
# ::PS_CODE_END::