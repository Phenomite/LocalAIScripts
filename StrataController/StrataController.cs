using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Strata CLI and OMP Settings Controller")]
[assembly: System.Reflection.AssemblyDescription("Controller and configuration utility for Strata inference engine and Oh-My-Pi")]
[assembly: System.Reflection.AssemblyConfiguration("Release")]
[assembly: System.Reflection.AssemblyCompany("Phenomite")]
[assembly: System.Reflection.AssemblyProduct("Strata Controller")]
[assembly: System.Reflection.AssemblyCopyright("Copyright (c) 2026 Phenomite")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.2")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.2")]
[assembly: System.Reflection.AssemblyInformationalVersion("1.0.2")]
[assembly: System.Runtime.InteropServices.ComVisible(false)]

namespace StrataControllerApp {
    internal sealed class ModelInfo { public string Family, FamilyTitle, Model, Display; }
    internal sealed class ThinkingProfile { public string Key, DefaultLevel; public string[] UiLevels, Efforts; }
    internal sealed class PendingOmpSyncInfo {
        public ModelInfo Info;
        public string Level, ConfigPath, OriginalConfigText;
        public bool HadOriginalConfig;
    }

    internal sealed class LogSink {
        public volatile bool Cancelled;
        public readonly object Sync = new object();
        public Thread OutThread, ErrThread;
        public void Wait(int ms) {
            DateTime end = DateTime.UtcNow.AddMilliseconds(ms);
            foreach (Thread t in new[] { OutThread, ErrThread }) {
                if (t == null) continue;
                int rem = (int)(end - DateTime.UtcNow).TotalMilliseconds;
                if (rem > 0) try { t.Join(rem); } catch { }
            }
        }
    }

    internal static class Program {
        public static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
        public static readonly string LogsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Strata\logs");
        private static readonly string PidFilePath = Path.Combine(LogsDir, "StrataController.pids");
        private static readonly object DebugLock = new object(), PidLock = new object();
        public static string DebugLogPath;

        [STAThread]
        private static void Main() {
            try { Directory.CreateDirectory(LogsDir); } catch { }
            string bootLog = Environment.GetEnvironmentVariable("STRATA_BOOTLOG");
            DebugLogPath = !string.IsNullOrEmpty(bootLog) ? bootLog : Path.Combine(LogsDir, "StrataController.debug.log");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { ReportFatal("Unhandled UI Exception", e.Exception); };
            try {
                File.WriteAllText(DebugLogPath, string.Format("[{0:yyyy-MM-dd HH:mm:ss.fff}] Bootstrap starting\r\n", DateTime.Now), Utf8NoBom);
                using (StrataControllerForm form = new StrataControllerForm()) Application.Run(form);
            } catch (Exception ex) {
                ReportFatal("Unhandled Controller Exception", ex);
                Environment.ExitCode = 1;
            }
        }

        public static void WriteDebugFile(string msg) {
            lock (DebugLock) {
                try { File.AppendAllText(DebugLogPath, string.Format("[{0:yyyy-MM-dd HH:mm:ss.fff}] {1}\r\n", DateTime.Now, msg), Utf8NoBom); } catch { }
            }
        }

        private static void ReportFatal(string title, Exception ex) {
            string err = ex != null ? ex.ToString() : "Unknown error";
            WriteDebugFile(title + ": " + err);
            try { MessageBox.Show(err, "Strata Controller Error", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
        }

        private static bool IsSameLiveProcess(int pid, long expectedStartFt) {
            if (pid <= 0 || pid == Process.GetCurrentProcess().Id) return false;
            try {
                using (Process p = Process.GetProcessById(pid)) {
                    if (p.HasExited) return false;
                    return expectedStartFt <= 0 || p.StartTime.ToFileTimeUtc() == expectedStartFt;
                }
            } catch { return false; }
        }

        public static void UpdatePidFile(int addPid, long addStartFt, int removePid) {
            lock (PidLock) {
                try {
                    Directory.CreateDirectory(LogsDir);
                    List<string> lines = new List<string>();
                    if (File.Exists(PidFilePath)) {
                        foreach (string ln in File.ReadAllLines(PidFilePath, Utf8NoBom)) {
                            string[] parts = ln.Split('|');
                            int p; long ft;
                            if (parts.Length == 2 && int.TryParse(parts[0], out p) && long.TryParse(parts[1], out ft) &&
                                p != removePid && p != addPid && IsSameLiveProcess(p, ft)) {
                                lines.Add(p + "|" + ft);
                            }
                        }
                    }
                    if (addPid > 0 && addStartFt > 0) lines.Add(addPid + "|" + addStartFt);
                    File.WriteAllLines(PidFilePath, lines.ToArray(), Utf8NoBom);
                } catch { }
            }
        }

        public static void StopTrackedProcess(int pid, long expectedStartFt) {
            if (IsSameLiveProcess(pid, expectedStartFt)) {
                try {
                    using (Process p = Process.GetProcessById(pid)) {
                        if (!p.HasExited && (expectedStartFt <= 0 || p.StartTime.ToFileTimeUtc() == expectedStartFt)) p.Kill();
                    }
                } catch { }
            }
            UpdatePidFile(0, 0, pid);
        }

        public static bool StopTrackedProcesses() {
            bool stoppedAny = false;
            lock (PidLock) {
                if (!File.Exists(PidFilePath)) return false;
                try {
                    foreach (string ln in File.ReadAllLines(PidFilePath, Utf8NoBom)) {
                        string[] parts = ln.Split('|');
                        int pid; long ft;
                        if (parts.Length == 2 && int.TryParse(parts[0], out pid) && long.TryParse(parts[1], out ft) && IsSameLiveProcess(pid, ft)) {
                            try {
                                using (Process p = Process.GetProcessById(pid)) {
                                    if (!p.HasExited && p.StartTime.ToFileTimeUtc() == ft) { p.Kill(); stoppedAny = true; }
                                }
                            } catch { }
                        }
                    }
                    File.WriteAllText(PidFilePath, "", Utf8NoBom);
                } catch { }
            }
            return stoppedAny;
        }
    }

    internal sealed class StrataControllerForm : Form {
        private const string LocalModelsUrl = "http://127.0.0.1:8080/v1/models";
        private static readonly string[] EngineProcNames = { "strata-dsv41", "strata-server", "strata" };
        private static readonly string[] StopProcNames   = { "strata-dsv41", "strata-server", "strata-vision", "strata" };
        private static readonly Dictionary<string, string> FamilyTitles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "deepseek41", "DeepSeek V4.1-Flash" }, { "coder", "Qwen3.8 Coder" }, { "gyro", "Qwen3.8 Gyro" },
            { "swift", "Qwen3.8 Swift 1.5" }, { "unsloth", "Qwen3.8 Unsloth" }, { "nvfp4", "Qwen3.8 NVFP4" }, { "qwen36", "Qwen3.6-35B-A3B" }
        };

        private static readonly HttpClient LocalHttp = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromMilliseconds(500) };
        private static readonly Color BgDark = Color.FromArgb(24, 24, 28), BgPanel = Color.FromArgb(34, 34, 40), BgInput = Color.FromArgb(45, 45, 54);
        private static readonly Color FgText = Color.FromArgb(235, 235, 240), FgMuted = Color.FromArgb(165, 165, 180);
        private static readonly Color ColorServing = Color.FromArgb(65, 215, 95), ColorLoading = Color.FromArgb(245, 185, 45), ColorStopped = Color.FromArgb(230, 75, 75);

        private readonly Font fontUI = new Font("Segoe UI", 9.5f), fontBold = new Font("Segoe UI Semibold", 9.5f);
        private readonly Font fontStatus = new Font("Segoe UI Semibold", 11f), fontMono = new Font("Consolas", 9f);
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private readonly IPGlobalProperties ipProps = IPGlobalProperties.GetIPGlobalProperties();

        private readonly string strataLocalDir, launchDir, installDir, ompAgentDir, launchLogPath, cmdLogPath;
        private readonly Dictionary<string, ModelInfo> modelMap = new Dictionary<string, ModelInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> modelKeys = new List<string>();
        private string initialThinking = "medium", lastThinkingKey = "", currentEngineLog = "", lastStatusText = "", lastProcInfo = "";

        private Icon appIcon;
        private ToolTip toolTip;
        private Label lblStatus, lblProcInfo;
        private ComboBox cmbModel, cmbCtx, cmbSpeed, cmbPleIo, cmbVramReserve, cmbThinking;
        private CheckBox chkOmpContinue;
        private TextBox txtPreset, txtLog;
        private Button btnStart, btnStop, btnForceStop, btnTune, btnWireOmp, btnRunOmp, btnOpenLog;
        private System.Windows.Forms.Timer pollTimer;

        private bool suppressThinkingSync, isPrompting, isPolling;
        private long engineLogPos, launchLogPos, cmdLogPos, launchStartFt, actionStartFt;
        private Process launchProc, actionProc;
        private LogSink launchSink, actionSink;
        private PendingOmpSyncInfo pendingOmpSync;
        private Action pendingLaunchNext;
        private Action<bool> pendingActionNext;
        private int lastSyncedEnginePid = -1;

        public StrataControllerForm() {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            SetupEnvironmentPath(userProfile, localAppData, appData);

            strataLocalDir = Path.Combine(localAppData, "Strata");
            launchDir      = Path.Combine(strataLocalDir, "launch");
            installDir     = Path.Combine(strataLocalDir, "install");
            ompAgentDir    = Path.Combine(Path.Combine(userProfile, ".omp"), "agent");
            launchLogPath  = Path.Combine(Program.LogsDir, "strata_gui_launch.log");
            cmdLogPath     = Path.Combine(Program.LogsDir, "strata_gui_cmd.log");
            try { File.WriteAllText(launchLogPath, "", Program.Utf8NoBom); } catch { }
            try { File.WriteAllText(cmdLogPath, "", Program.Utf8NoBom); } catch { }

            using (Process initEng = GetEngineProc()) {
                if (initEng == null && Directory.Exists(launchDir)) {
                    try {
                        foreach (string f in Directory.EnumerateFiles(launchDir, "*.engine.log"))
                            try { File.WriteAllText(f, "", Program.Utf8NoBom); } catch { }
                    } catch { }
                }
            }

            DiscoverReadyModels(userProfile);
            ReadInitialThinking();
            BuildUi();
            WriteGuiLog("Debug log: " + Program.DebugLogPath);
            if (modelKeys.Count > 0) {
                foreach (string k in modelKeys) {
                    ModelInfo v = modelMap[k];
                    WriteGuiLog(string.Format("Ready Model: {0} [--family {1} --model {2}]", v.FamilyTitle, v.Family, v.Model));
                }
            } else {
                WriteGuiLog("WARNING: No downloaded/prepared Strata models detected. Run 'strata launch' in terminal to download one.");
            }
            WireEvents();
            pollTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            pollTimer.Tick += OnPollTimerTick;
            pollTimer.Start();
        }

        private static void SetupEnvironmentPath(string userProfile, string localAppData, string appData) {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> list = new List<string>();
            string[] sources = {
                Path.Combine(userProfile, @".local\bin"), Path.Combine(localAppData, @"Strata\bin"),
                Path.Combine(localAppData, "Strata"), Path.Combine(localAppData, @"Programs\Strata"),
                Path.Combine(userProfile, @".omp\bin"), Path.Combine(appData, "npm"),
                Environment.GetEnvironmentVariable("PATH"),
                Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User),
                Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine)
            };
            foreach (string src in sources) {
                if (string.IsNullOrEmpty(src)) continue;
                foreach (string part in src.Split(';')) {
                    string exp = Environment.ExpandEnvironmentVariables(part.Trim());
                    if (!string.IsNullOrEmpty(exp) && seen.Add(exp)) list.Add(exp);
                }
            }
            Environment.SetEnvironmentVariable("PATH", string.Join(";", list), EnvironmentVariableTarget.Process);
        }

        private static string FindCommandOnPath(string command) {
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            string[] exts = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            bool hasExt = Path.HasExtension(command);
            foreach (string dir in pathEnv.Split(';')) {
                string trimmed = (dir ?? "").Trim().Trim('"');
                if (string.IsNullOrEmpty(trimmed)) continue;
                try {
                    string direct = Path.Combine(trimmed, command);
                    if (hasExt) { if (File.Exists(direct)) return direct; }
                    else {
                        foreach (string ext in exts) {
                            string candidate = direct + ext.Trim();
                            if (File.Exists(candidate)) return candidate;
                        }
                        if (File.Exists(direct)) return direct;
                    }
                } catch { }
            }
            return null;
        }

        private static ProcessStartInfo BuildToolStartInfo(string toolNameOrPath, string args, bool interactiveWindow, bool redirectInput, string workDir) {
            string resolved = File.Exists(toolNameOrPath) ? toolNameOrPath : (FindCommandOnPath(toolNameOrPath) ?? toolNameOrPath);
            bool isBatch = resolved.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || resolved.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
            string file = isBatch ? (Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe") : resolved;
            string finalArgs = isBatch ? string.Format("/d /c call \"{0}\"{1}", resolved, string.IsNullOrEmpty(args) ? "" : " " + args) : args;
            ProcessStartInfo psi = new ProcessStartInfo(file, finalArgs) { UseShellExecute = false, CreateNoWindow = !interactiveWindow };
            if (!string.IsNullOrEmpty(workDir)) psi.WorkingDirectory = workDir;
            if (!interactiveWindow) {
                psi.RedirectStandardInput = redirectInput;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;
            }
            return psi;
        }

        private string ResolveOmpFile(string baseName) {
            string yaml = Path.Combine(ompAgentDir, baseName + ".yaml"), yml = Path.Combine(ompAgentDir, baseName + ".yml");
            return (!File.Exists(yml) && File.Exists(yaml)) ? yaml : yml;
        }

        private static bool PathExists(string path) { return !string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path)); }
        private static Dictionary<string, object> AsDict(object obj) { return obj as Dictionary<string, object>; }
        private static string GetJsonStr(Dictionary<string, object> d, string k) { object v; return (d != null && d.TryGetValue(k, out v) && v != null) ? v.ToString() : ""; }
        private static bool GetJsonBool(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v is bool && (bool)v; }

        private static ModelInfo ResolveModelInfo(string rawModel, string rawFamily) {
            string m = Regex.Replace(Regex.Replace((rawModel ?? "").Trim(), @"^strata-", "", RegexOptions.IgnoreCase), @"\.json$", "", RegexOptions.IgnoreCase).ToLowerInvariant();
            string f = (rawFamily ?? "").Trim().ToLowerInvariant();
            Match match;
            if ((match = Regex.Match(m, @"^deepseek-v4\.1-flash-(.+)$", RegexOptions.IgnoreCase)).Success) {
                m = match.Groups[1].Value; f = "deepseek41";
            } else if ((match = Regex.Match(m, @"^qwen3\.6-(?:35b-a3b-)?(.+)$", RegexOptions.IgnoreCase)).Success) {
                m = match.Groups[1].Value; if (string.IsNullOrEmpty(f)) f = "qwen36";
            } else {
                m = Regex.Replace(m, @"^qwen3\.8-flash-next-", "", RegexOptions.IgnoreCase);
                if ((match = Regex.Match(m, @"^gyro-(.+)$", RegexOptions.IgnoreCase)).Success) {
                    m = match.Groups[1].Value; f = "gyro";
                } else if ((match = Regex.Match(m, @"^(coder|swift|unsloth|nvfp4|qwen36)-(.+)$", RegexOptions.IgnoreCase)).Success) {
                    if (string.IsNullOrEmpty(f)) f = match.Groups[1].Value;
                    m = match.Groups[2].Value;
                }
            }
            if (string.IsNullOrEmpty(f)) {
                if (Regex.IsMatch(m, @"^(ist-?a?-?das|deepseek|dsv41)|bpw$", RegexOptions.IgnoreCase)) f = "deepseek41";
                else if (Regex.IsMatch(m, @"^iq1_m$", RegexOptions.IgnoreCase)) f = "coder";
                else if (Regex.IsMatch(m, @"^gyro-", RegexOptions.IgnoreCase)) f = "gyro";
                else if (Regex.IsMatch(m, @"^nvfp4", RegexOptions.IgnoreCase)) f = "nvfp4";
                else if (Regex.IsMatch(m, @"^(ud-|q8_0)", RegexOptions.IgnoreCase)) f = "unsloth";
                else f = "qwen";
            }
            string fTitle;
            if (!FamilyTitles.TryGetValue(f, out fTitle)) fTitle = "Qwen3.8-Flash-Next";
            return new ModelInfo { Family = f, FamilyTitle = fTitle, Model = m, Display = string.Format("[{0}]  {1}", fTitle, m) };
        }

        private static ThinkingProfile GetThinkingProfile(string family) {
            if (family == "deepseek41")
                return new ThinkingProfile { Key = "deepseek41", UiLevels = new[] { "auto", "off", "low", "medium", "high", "max" }, Efforts = new[] { "low", "medium", "high", "max" }, DefaultLevel = "high" };
            if (family == "qwen36")
                return new ThinkingProfile { Key = "qwen36", UiLevels = new[] { "auto", "off", "low", "medium", "high" }, Efforts = new[] { "low", "medium", "high" }, DefaultLevel = "medium" };
            return new ThinkingProfile { Key = "qwen38", UiLevels = new[] { "auto", "off", "low", "medium", "xhigh" }, Efforts = new[] { "low", "medium", "xhigh" }, DefaultLevel = "medium" };
        }

        private static string ResolveThinkingChoice(string choice, ThinkingProfile prof) {
            string c = (choice ?? "").Trim().ToLowerInvariant(), mapped = prof.DefaultLevel;
            if (Array.IndexOf(prof.UiLevels, c) >= 0) return c;
            if (c == "minimal") mapped = "off";
            else if (c == "high") mapped = Array.IndexOf(prof.UiLevels, "xhigh") >= 0 ? "xhigh" : "high";
            else if (c == "xhigh") mapped = Array.IndexOf(prof.UiLevels, "high") >= 0 ? "high" : "xhigh";
            else if (c == "max") mapped = Array.IndexOf(prof.UiLevels, "xhigh") >= 0 ? "xhigh" : (Array.IndexOf(prof.UiLevels, "high") >= 0 ? "high" : prof.DefaultLevel);
            return Array.IndexOf(prof.UiLevels, mapped) >= 0 ? mapped : prof.DefaultLevel;
        }

        private void AddReadyInfo(ModelInfo info) {
            if (info != null && !modelMap.ContainsKey(info.Display)) { modelMap[info.Display] = info; modelKeys.Add(info.Display); }
        }

        private void AddReadyModel(string mod, string fam) { if (!string.IsNullOrEmpty(mod)) AddReadyInfo(ResolveModelInfo(mod, fam)); }

        private void DiscoverReadyModels(string userProfile) {
            string strataExe = FindCommandOnPath("strata");
            if (!string.IsNullOrEmpty(strataExe)) {
                try {
                    using (Process p = Process.Start(BuildToolStartInfo(strataExe, "models list --installed --output json", false, false, null))) {
                        if (p != null) {
                            StringBuilder outBuf = new StringBuilder();
                            Thread tOut = new Thread(delegate() { try { outBuf.Append(p.StandardOutput.ReadToEnd()); } catch { } }) { IsBackground = true };
                            Thread tErr = new Thread(delegate() { try { p.StandardError.ReadToEnd(); } catch { } }) { IsBackground = true };
                            tOut.Start(); tErr.Start();
                            if (!p.WaitForExit(8000)) { try { if (!p.HasExited) p.Kill(); } catch { } }
                            tOut.Join(500);
                            string cliJson = outBuf.ToString();
                            int brace = cliJson.IndexOf('{'), bracket = cliJson.IndexOf('[');
                            int start = (brace >= 0 && (bracket < 0 || brace < bracket)) ? brace : bracket;
                            if (start >= 0) {
                                object parsed = json.DeserializeObject(cliJson.Substring(start));
                                object[] items = parsed as object[];
                                Dictionary<string, object> root = AsDict(parsed);
                                if (root != null) {
                                    object listObj;
                                    items = (root.TryGetValue("models", out listObj) || root.TryGetValue("items", out listObj)) ? listObj as object[] : new object[] { root };
                                }
                                if (items != null) {
                                    foreach (object itemObj in items) {
                                        Dictionary<string, object> it = AsDict(itemObj);
                                        if (it != null && (GetJsonStr(it, "component_status") == "present" || GetJsonBool(it, "ready"))) {
                                            string id = GetJsonStr(it, "model");
                                            if (string.IsNullOrEmpty(id)) id = GetJsonStr(it, "model_id");
                                            if (string.IsNullOrEmpty(id)) id = GetJsonStr(it, "id");
                                            AddReadyModel(id, GetJsonStr(it, "family"));
                                        }
                                    }
                                }
                            }
                        }
                    }
                } catch { }
            }

            HashSet<string> rootSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> dataRoots = new List<string>();
            foreach (string r in new[] {
                strataLocalDir, Path.Combine(strataLocalDir, "Strata-data"), Path.Combine(userProfile, @".strata\Strata-data"),
                Path.Combine(userProfile, "Strata"), Path.Combine(userProfile, "Strata-data"), Path.Combine(userProfile, @"Documents\Strata")
            }) if (!string.IsNullOrEmpty(r) && rootSet.Add(r)) dataRoots.Add(r);

            foreach (string cfgFile in new[] {
                Path.Combine(strataLocalDir, "settings.json"), Path.Combine(strataLocalDir, "app-settings.json"), Path.Combine(userProfile, @".config\strata\settings.json")
            }) {
                if (!File.Exists(cfgFile)) continue;
                try {
                    Dictionary<string, object> c = AsDict(json.DeserializeObject(File.ReadAllText(cfgFile, Encoding.UTF8)));
                    foreach (string key in new[] { "data_dir", "models_dir" }) {
                        string p = GetJsonStr(c, key);
                        if (!string.IsNullOrEmpty(p) && rootSet.Add(p)) dataRoots.Add(p);
                    }
                } catch { }
            }

            HashSet<string> readyPacks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string root in dataRoots) {
                string pkDir = Path.Combine(root, "packs");
                if (!Directory.Exists(pkDir)) continue;
                try {
                    foreach (string sub in Directory.EnumerateDirectories(pkDir)) {
                        bool hasFiles = false;
                        try { using (IEnumerator<string> en = Directory.EnumerateFiles(sub).GetEnumerator()) hasFiles = en.MoveNext(); } catch { }
                        if (hasFiles) { string name = Path.GetFileName(sub); readyPacks.Add(name); AddReadyModel(name, ""); }
                    }
                } catch { }
            }

            foreach (string dir in new[] { installDir, launchDir }) {
                if (!Directory.Exists(dir)) continue;
                try {
                    foreach (string fPath in Directory.EnumerateFiles(dir, "*.json")) {
                        try {
                            Dictionary<string, object> j = AsDict(json.DeserializeObject(File.ReadAllText(fPath, Encoding.UTF8)));
                            if (j == null) continue;
                            string baseName = Path.GetFileNameWithoutExtension(fPath);
                            bool packOk = false;
                            object argsObj;
                            if (j.TryGetValue("args", out argsObj) && argsObj is object[]) {
                                object[] jArgs = (object[])argsObj;
                                for (int i = 0; i < jArgs.Length; i++) {
                                    string arg = jArgs[i] != null ? jArgs[i].ToString() : "";
                                    if (arg == "--pack" && i + 1 < jArgs.Length && jArgs[i + 1] != null && PathExists(jArgs[i + 1].ToString())) { packOk = true; break; }
                                    if (arg.StartsWith("--pack=", StringComparison.Ordinal) && PathExists(arg.Substring(7))) { packOk = true; break; }
                                }
                            }
                            string mName = GetJsonStr(j, "model");
                            if (string.IsNullOrEmpty(mName) && baseName.StartsWith("strata-", StringComparison.OrdinalIgnoreCase)) mName = baseName;
                            if (!string.IsNullOrEmpty(mName)) {
                                ModelInfo info = ResolveModelInfo(mName, GetJsonStr(j, "family"));
                                if (packOk || readyPacks.Contains(info.Model) || File.Exists(Path.Combine(launchDir, baseName + ".engine.log")) || PathExists(GetJsonStr(j, "model_path")))
                                    AddReadyInfo(info);
                            }
                        } catch { }
                    }
                } catch { }
            }
        }

        private void ReadInitialThinking() {
            string found = "", cfgPath = ResolveOmpFile("config");
            if (File.Exists(cfgPath)) {
                try {
                    List<string> lines = new List<string>(Regex.Split(File.ReadAllText(cfgPath, Encoding.UTF8), @"\r?\n"));
                    int mrIdx = FindYamlLine(lines, 0, lines.Count, 0, @"modelRoles:\s*(?:#.*)?$");
                    if (mrIdx >= 0) {
                        int mrEnd = FindBlockEnd(lines, mrIdx, 0, false);
                        for (int i = mrIdx + 1; i < mrEnd; i++) {
                            Match dm = Regex.Match(lines[i], @"^[ \t]+default:\s*([""']?)[^\s#""']+/[^\s#""']+:(off|minimal|low|medium|high|xhigh|max|auto)\1\s*(?:#.*)?$", RegexOptions.IgnoreCase);
                            if (dm.Success) { found = dm.Groups[2].Value.ToLowerInvariant(); break; }
                        }
                    }
                    if (string.IsNullOrEmpty(found)) {
                        foreach (string ln in lines) {
                            Match mDtl = Regex.Match(ln, @"^defaultThinkingLevel:\s*([a-zA-Z0-9_-]+)");
                            if (mDtl.Success) { found = mDtl.Groups[1].Value.ToLowerInvariant(); break; }
                        }
                    }
                } catch { }
            }
            if (string.IsNullOrEmpty(found)) {
                string modPath = ResolveOmpFile("models");
                if (File.Exists(modPath)) {
                    try {
                        Match mDef = Regex.Match(File.ReadAllText(modPath, Encoding.UTF8), @"^[ \t]+strata:\s*(?:#.*)?\r?\n(?:[ \t]*(?:#[^\r\n]*)?\r?\n|[ \t]{2,}[^\r\n]*\r?\n)*?[ \t]+defaultLevel:\s*([a-zA-Z0-9_-]+)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                        if (mDef.Success) found = mDef.Groups[1].Value.ToLowerInvariant();
                    } catch { }
                }
            }
            if (!string.IsNullOrEmpty(found)) initialThinking = found;
        }

        private void BuildUi() {
            Text = "Strata Engine Controller";
            try { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); if (appIcon != null) Icon = appIcon; } catch { }
            Size = new Size(920, 660);
            MinimumSize = new Size(800, 520);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgDark;
            ForeColor = FgText;
            Font = fontUI;
            AutoScaleMode = AutoScaleMode.Dpi;
            toolTip = new ToolTip { AutoPopDelay = 12000, InitialDelay = 400, ReshowDelay = 200 };
            SuspendLayout();

            TableLayoutPanel statusGrid = NewGrid(44, new[] { 0f, 100f }, new[] { 100f }, BgPanel, new Padding(12, 6, 12, 6));
            lblStatus   = AddLbl(statusGrid, "[CHECKING ENGINE...]", 0, 0, fontStatus, FgText, true);
            lblProcInfo = AddLbl(statusGrid, "Endpoint: http://127.0.0.1:8080/v1", 1, 0, fontUI, FgMuted, false);
            statusGrid.ResumeLayout(false);

            TableLayoutPanel inputRow1 = NewGrid(58, new[] { 42f, 13f, 15f, 30f }, new[] { 42f, 58f }, BgDark, new Padding(12, 6, 12, 2));
            AddLbl(inputRow1, "Model:", 0, 0, fontBold, FgText, false);
            AddLbl(inputRow1, "Context:", 1, 0, fontUI, FgText, false);
            AddLbl(inputRow1, "Speed (DS41):", 2, 0, fontUI, FgText, false);
            AddLbl(inputRow1, "Preset (--save-preset):", 3, 0, fontUI, FgText, false);
            cmbModel = AddCmb(inputRow1, 0, modelKeys.ToArray(), null, ComboBoxStyle.DropDownList);
            cmbCtx   = AddCmb(inputRow1, 1, new[] { "256k", "128k", "64k", "32k", "16k", "8k" }, "256k", ComboBoxStyle.DropDown);
            cmbSpeed = AddCmb(inputRow1, 2, new[] { "full", "faster" }, "full", ComboBoxStyle.DropDownList);
            txtPreset = new TextBox { Dock = DockStyle.Fill, BackColor = BgInput, ForeColor = FgText, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(3, 2, 3, 2) };
            inputRow1.Controls.Add(txtPreset, 3, 1);
            inputRow1.ResumeLayout(false);

            TableLayoutPanel inputRow2 = NewGrid(58, new[] { 18f, 22f, 24f, 36f }, new[] { 42f, 58f }, BgDark, new Padding(12, 2, 12, 4));
            AddLbl(inputRow2, "PLE I/O:", 0, 0, fontUI, FgText, false);
            AddLbl(inputRow2, "VRAM Reserve (MiB):", 1, 0, fontUI, FgText, false);
            AddLbl(inputRow2, "OMP Thinking:", 2, 0, fontUI, FgText, false);
            AddLbl(inputRow2, "OMP Launch Options:", 3, 0, fontUI, FgText, false);
            cmbPleIo       = AddCmb(inputRow2, 0, new[] { "auto", "direct", "ram" }, "auto", ComboBoxStyle.DropDownList);
            cmbVramReserve = AddCmb(inputRow2, 1, new[] { "auto", "1088", "1280", "1536", "2048" }, "auto", ComboBoxStyle.DropDown);
            cmbThinking    = AddCmb(inputRow2, 2, new string[0], null, ComboBoxStyle.DropDownList);
            chkOmpContinue = new CheckBox { Text = "Continue last session (-c)", Checked = true, Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = FgText, BackColor = BgDark, Margin = new Padding(6, 2, 3, 2) };
            inputRow2.Controls.Add(chkOmpContinue, 3, 1);
            inputRow2.ResumeLayout(false);

            TableLayoutPanel btnGrid = NewGrid(50, new[] { 14f, 16f, 13f, 14f, 13f, 14f, 16f }, new[] { 100f }, BgDark, new Padding(12, 2, 12, 8));
            btnStart     = AddBtn(btnGrid, "Start Engine",    0, Color.FromArgb(35, 134, 54),  fontBold);
            btnStop      = AddBtn(btnGrid, "Stop (Graceful)", 1, Color.FromArgb(180, 105, 14), fontBold);
            btnForceStop = AddBtn(btnGrid, "Force Stop",      2, Color.FromArgb(190, 40, 40),  fontBold);
            btnTune      = AddBtn(btnGrid, "Tune Model",      3, Color.FromArgb(115, 75, 180), fontBold);
            btnWireOmp   = AddBtn(btnGrid, "Sync omp",        4, Color.FromArgb(45, 105, 190), fontUI);
            btnRunOmp    = AddBtn(btnGrid, "Launch omp",      5, BgPanel,                      fontUI);
            btnOpenLog   = AddBtn(btnGrid, "Open Debug Log",  6, BgPanel,                      fontUI);
            btnGrid.ResumeLayout(false);

            Panel logPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 4, 12, 12) };
            Controls.Add(logPanel);
            logPanel.BringToFront();
            txtLog = new TextBox {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
                MaxLength = int.MaxValue, Font = fontMono, BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(16, 16, 20), ForeColor = Color.FromArgb(200, 215, 225)
            };
            logPanel.Controls.Add(txtLog);

            toolTip.SetToolTip(cmbPleIo, "direct: keeps 26.8 GiB PLE table on NVMe SSD (prevents startup/tuning RAM spikes via STRATA_PLE_AUTO_RAM=0).\nauto: lets Strata lock PLE in RAM if free RAM allows.\nram: forces PLE lock in RAM.");
            toolTip.SetToolTip(cmbVramReserve, "MiB kept free on GPU (--vram-reserve-mib). Recommended 1280-1536 on 12 GB Windows cards to avoid WDDM paging (#2065).");
            toolTip.SetToolTip(cmbThinking, "Updates modelRoles.default :level suffix and defaultThinkingLevel in ~/.omp/agent/config.yml without overwriting other keys.");
            toolTip.SetToolTip(chkOmpContinue, "When unchecked (recommended), Launch omp starts a fresh session using --thinking. When checked, adds -c to resume the previous session.");
            ResumeLayout(true);
            SyncModelSelection();
        }

        private TableLayoutPanel NewGrid(int height, float[] cols, float[] rows, Color bg, Padding pad) {
            TableLayoutPanel g = new TableLayoutPanel { Dock = DockStyle.Top, Height = height, ColumnCount = cols.Length, RowCount = rows.Length, BackColor = bg, Padding = pad };
            g.SuspendLayout();
            foreach (float w in cols) g.ColumnStyles.Add(new ColumnStyle(w <= 0f ? SizeType.AutoSize : SizeType.Percent, Math.Max(w, 0f)));
            foreach (float h in rows) g.RowStyles.Add(new RowStyle(SizeType.Percent, h));
            Controls.Add(g);
            g.BringToFront();
            return g;
        }

        private static Label AddLbl(TableLayoutPanel g, string text, int col, int row, Font font, Color color, bool autoSize) {
            Label l = new Label { Text = text, Font = font, ForeColor = color, AutoSize = autoSize, AutoEllipsis = !autoSize, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(3, 0, 10, 0) };
            g.Controls.Add(l, col, row);
            return l;
        }

        private static ComboBox AddCmb(TableLayoutPanel g, int col, string[] items, string defaultItem, ComboBoxStyle style) {
            ComboBox c = new ComboBox { Dock = DockStyle.Fill, BackColor = BgInput, ForeColor = FgText, FlatStyle = FlatStyle.Flat, DropDownStyle = style, DropDownWidth = 320, Margin = new Padding(3, 2, 3, 2) };
            if (items != null && items.Length > 0) c.Items.AddRange(items);
            if (!string.IsNullOrEmpty(defaultItem) && c.Items.Contains(defaultItem)) c.SelectedItem = defaultItem;
            else if (c.Items.Count > 0) c.SelectedIndex = 0;
            g.Controls.Add(c, col, 1);
            return c;
        }

        private static Button AddBtn(TableLayoutPanel g, string text, int col, Color bg, Font font) {
            Button b = new Button { Text = text, Font = font, Dock = DockStyle.Fill, BackColor = bg, ForeColor = FgText, FlatStyle = FlatStyle.Flat, AutoEllipsis = true, Margin = new Padding(3, 4, 3, 4) };
            b.FlatAppearance.BorderSize = 0;
            g.Controls.Add(b, col, 0);
            return b;
        }

        private ModelInfo GetSelectedModelInfo() {
            object item = cmbModel.SelectedItem;
            ModelInfo info;
            return (item != null && modelMap.TryGetValue(item.ToString(), out info)) ? info : null;
        }

        private void UpdateThinkingOptions(string family, string preferred) {
            ThinkingProfile prof = GetThinkingProfile(family);
            string cur = !string.IsNullOrEmpty(preferred) ? preferred : (cmbThinking.SelectedItem != null ? cmbThinking.SelectedItem.ToString() : initialThinking);
            string pick = ResolveThinkingChoice(cur, prof);
            suppressThinkingSync = true;
            try {
                if (lastThinkingKey != prof.Key || cmbThinking.Items.Count != prof.UiLevels.Length) {
                    cmbThinking.BeginUpdate();
                    try { cmbThinking.Items.Clear(); cmbThinking.Items.AddRange(prof.UiLevels); lastThinkingKey = prof.Key; }
                    finally { cmbThinking.EndUpdate(); }
                }
                if ((cmbThinking.SelectedItem != null ? cmbThinking.SelectedItem.ToString() : "") != pick) cmbThinking.SelectedItem = pick;
            } finally { suppressThinkingSync = false; }
        }

        private void SyncModelSelection() {
            ModelInfo sel = GetSelectedModelInfo();
            string c = cmbCtx.Text.Trim().ToLowerInvariant().Replace("k", "");
            if (sel != null) {
                cmbSpeed.Enabled = (sel.Family == "deepseek41");
                txtPreset.Text = string.Equals(sel.Model, "ist-das-coder", StringComparison.OrdinalIgnoreCase)
                    ? "ds41-coder-" + c
                    : string.Format("{0}-{1}-{2}", sel.Family, Regex.Replace(sel.Model, @"[^a-zA-Z0-9\-]", "-"), c);
                UpdateThinkingOptions(sel.Family, null);
            } else {
                cmbSpeed.Enabled = false;
                UpdateThinkingOptions("qwen", null);
            }
        }

        private bool TryBuildEngineOptionArgs(out string optArgs, out string errorMsg) {
            StringBuilder sb = new StringBuilder();
            errorMsg = null;
            string pleMode = cmbPleIo.SelectedItem != null ? cmbPleIo.SelectedItem.ToString() : "direct";
            if (string.Equals(pleMode, "direct", StringComparison.OrdinalIgnoreCase)) sb.Append(" --set STRATA_PLE_AUTO_RAM=0 --set ple-io=direct");
            else if (string.Equals(pleMode, "ram", StringComparison.OrdinalIgnoreCase)) sb.Append(" --set ple-io=ram");

            string vramRaw = (cmbVramReserve.Text ?? "").Trim();
            if (!string.IsNullOrEmpty(vramRaw) && !string.Equals(vramRaw, "auto", StringComparison.OrdinalIgnoreCase)) {
                int vramMib;
                if (!int.TryParse(vramRaw, out vramMib) || vramMib < 0) {
                    optArgs = "";
                    errorMsg = "VRAM Reserve must be 'auto' or a non-negative integer (MiB).";
                    return false;
                }
                sb.Append(" --set vram-reserve-mib=").Append(vramMib);
            }
            optArgs = sb.ToString();
            return true;
        }

        private bool ForceStopAllStrata(bool logOutput) {
            bool stoppedAny = Program.StopTrackedProcesses();
            foreach (string name in StopProcNames) {
                foreach (Process p in Process.GetProcessesByName(name)) {
                    stoppedAny = true;
                    if (logOutput) WriteGuiLog(string.Format("Stopping {0} (PID {1})...", p.ProcessName, p.Id));
                    try { if (!p.HasExited) p.Kill(); } catch { }
                    finally { p.Dispose(); }
                }
            }
            pendingOmpSync = null; pendingActionNext = null; pendingLaunchNext = null; lastSyncedEnginePid = -1;
            StopTrackedProc(ref launchProc, ref launchSink, launchStartFt); launchStartFt = 0;
            StopTrackedProc(ref actionProc, ref actionSink, actionStartFt); actionStartFt = 0;
            return stoppedAny;
        }

        private void ShowDebugLogDialog() {
            string content;
            try { content = File.Exists(Program.DebugLogPath) ? File.ReadAllText(Program.DebugLogPath, Encoding.UTF8) : "(Debug log file not found)"; }
            catch (Exception ex) { content = "Could not read debug log: " + ex.Message; }
            using (Form dlg = new Form {
                Text = "Debug Log - " + Program.DebugLogPath, Size = new Size(820, 540), StartPosition = FormStartPosition.CenterParent,
                BackColor = BgDark, ForeColor = FgText, Font = fontUI, KeyPreview = true, Icon = appIcon
            }) {
                TextBox box = new TextBox {
                    Multiline = true, ReadOnly = true, WordWrap = false, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill,
                    MaxLength = int.MaxValue, Font = fontMono, BorderStyle = BorderStyle.FixedSingle,
                    BackColor = Color.FromArgb(16, 16, 20), ForeColor = Color.FromArgb(200, 215, 225), Text = content
                };
                dlg.Controls.Add(box);
                dlg.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) dlg.Close(); };
                dlg.Shown += delegate { box.SelectionStart = box.TextLength; box.ScrollToCaret(); };
                dlg.ShowDialog(this);
            }
        }

        private void WireEvents() {
            cmbModel.SelectedIndexChanged += delegate { SyncModelSelection(); };
            cmbCtx.SelectedIndexChanged   += delegate { SyncModelSelection(); };
            cmbCtx.TextChanged            += delegate { SyncModelSelection(); };
            cmbThinking.SelectedIndexChanged += delegate {
                if (!suppressThinkingSync) SyncOmpConfigFiles(GetSelectedModelInfo(), cmbThinking.SelectedItem != null ? cmbThinking.SelectedItem.ToString() : "", null);
            };
            btnStart.Click += delegate { InvokeStrataLaunch("", "Starting", false); };
            btnTune.Click += delegate {
                ModelInfo sel = GetSelectedModelInfo();
                string preset = txtPreset.Text.Trim();
                if (sel == null || string.IsNullOrEmpty(preset)) { MessageBox.Show(this, "Select a downloaded ready model and preset name first.", "Missing Input"); return; }
                string pleMode = cmbPleIo.SelectedItem != null ? cmbPleIo.SelectedItem.ToString() : "auto";
                string vramRaw = (cmbVramReserve.Text ?? "").Trim();
                DialogResult ans = MessageBox.Show(this,
                    string.Format("Tune [{0}] ({1}) for this PC and save to preset '{2}'?\n(PLE I/O: {3}, VRAM Reserve: {4} MiB)\nAny running engine will be stopped first.\n\nYES = Quick tune (~20 min)\nNO = Thorough tune (up to 2 hours)\nCANCEL = Abort",
                        sel.FamilyTitle, sel.Model, preset, pleMode, vramRaw),
                    "Tune Model", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (ans == DialogResult.Cancel) return;
                string mode = (ans == DialogResult.Yes) ? "quick" : "thorough";
                InvokeStrataLaunch(" --tune " + mode + " --retune --restart --set STRATA_ARENA_RAM_WAIT_S=60", "Tuning (" + mode + ")", true);
            };
            btnStop.Click += delegate {
                pendingOmpSync = null; pendingActionNext = null;
                StopTrackedProc(ref actionProc, ref actionSink, actionStartFt);
                string p = txtPreset.Text.Trim();
                WriteGuiLog(string.Format("Stopping Strata engine gracefully (--preset \"{0}\")...", p));
                Action runCleanupIfAlive = delegate {
                    bool alive = IsProcRunning(launchProc);
                    if (!alive) using (Process eng = GetEngineProc()) alive = (eng != null);
                    if (alive) {
                        actionProc = StartStrata("runtime cleanup", cmdLogPath, true, ref cmdLogPos, ref actionSink, out actionStartFt);
                        pendingActionNext = delegate {
                            using (Process eng = GetEngineProc()) {
                                if (eng != null || IsProcRunning(launchProc))
                                    WriteGuiLog("WARNING: Engine is still running after graceful stop; use 'Force Stop' if needed.");
                            }
                        };
                    }
                };
                if (!string.IsNullOrEmpty(p)) {
                    actionProc = StartStrata(string.Format("launch --stop --preset \"{0}\"", p), cmdLogPath, false, ref cmdLogPos, ref actionSink, out actionStartFt);
                    pendingActionNext = delegate(bool ok) {
                        bool alive = !ok || IsProcRunning(launchProc);
                        if (!alive) using (Process eng = GetEngineProc()) alive = (eng != null);
                        if (alive) {
                            actionProc = StartStrata("launch --stop", cmdLogPath, true, ref cmdLogPos, ref actionSink, out actionStartFt);
                            pendingActionNext = delegate { runCleanupIfAlive(); };
                        }
                    };
                } else {
                    actionProc = StartStrata("launch --stop", cmdLogPath, false, ref cmdLogPos, ref actionSink, out actionStartFt);
                    pendingActionNext = delegate { runCleanupIfAlive(); };
                }
            };
            btnForceStop.Click += delegate {
                WriteGuiLog("Force-stopping strata processes...");
                bool stoppedAny = ForceStopAllStrata(true);
                WriteGuiLog(stoppedAny ? "Engine terminated. VRAM and System RAM freed." : "No running strata process found.");
            };
            btnWireOmp.Click += delegate {
                pendingOmpSync = null; pendingActionNext = null;
                StopTrackedProc(ref actionProc, ref actionSink, actionStartFt);
                string ignored;
                if (!ProbeLocalServer(out ignored)) {
                    WriteGuiLog("WARNING: Server is not responding on http://127.0.0.1:8080; start the engine before clicking 'Sync omp'.");
                    return;
                }
                ModelInfo sel = GetSelectedModelInfo();
                string lvl = cmbThinking.SelectedItem != null ? cmbThinking.SelectedItem.ToString() : "";
                string cfgPath = ResolveOmpFile("config");
                bool hadCfg = File.Exists(cfgPath);
                string origCfg = null;
                if (hadCfg) try { origCfg = File.ReadAllText(cfgPath, Encoding.UTF8); } catch { origCfg = null; }
                WriteGuiLog(string.Format("Syncing omp (thinking: {0}; preserving existing config.yml keys)...", lvl));
                pendingOmpSync = new PendingOmpSyncInfo { Info = sel, Level = lvl, ConfigPath = cfgPath, OriginalConfigText = origCfg, HadOriginalConfig = hadCfg };
                actionProc = StartStrata("connect omp --write --yes", cmdLogPath, false, ref cmdLogPos, ref actionSink, out actionStartFt);
            };
            btnRunOmp.Click += delegate {
                string ompPath = FindCommandOnPath("omp");
                if (string.IsNullOrEmpty(ompPath)) {
                    WriteGuiLog("ERROR: 'omp' command was not found on PATH.");
                    MessageBox.Show(this, "'omp' was not found on PATH.", "Missing Tool", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                ModelInfo sel = GetSelectedModelInfo();
                string lvl = cmbThinking.SelectedItem != null ? cmbThinking.SelectedItem.ToString() : "";
                string resolvedLvl = !string.IsNullOrEmpty(lvl) ? ResolveThinkingChoice(lvl, GetThinkingProfile(sel != null ? sel.Family : "qwen")) : "";
                SyncOmpConfigFiles(sel, resolvedLvl, null);
                string dir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                using (FolderBrowserDialog dlg = new FolderBrowserDialog { Description = "Select workspace folder for omp (Cancel for user home):" }) {
                    if (dlg.ShowDialog(this) == DialogResult.OK) dir = dlg.SelectedPath;
                }
                string ompArgs = ((chkOmpContinue.Checked ? "-c " : "") + (!string.IsNullOrEmpty(resolvedLvl) ? "--thinking " + resolvedLvl : "")).Trim();
                WriteGuiLog(string.Format("Launching 'omp{0}' in: {1}", string.IsNullOrEmpty(ompArgs) ? "" : " " + ompArgs, dir));
                try { using (Process p = Process.Start(BuildToolStartInfo(ompPath, ompArgs, true, false, dir))) { } }
                catch (Exception ex) { WriteGuiLog("ERROR launching omp: " + ex.Message); }
            };
            btnOpenLog.Click += delegate { ShowDebugLogDialog(); };
        }

        private void InvokeStrataLaunch(string extraArgs, string actionLabel, bool stopFirst) {
            ModelInfo sel = GetSelectedModelInfo();
            string ctx = cmbCtx.Text.Trim(), preset = txtPreset.Text.Trim();
            if (sel == null || string.IsNullOrEmpty(ctx) || string.IsNullOrEmpty(preset)) {
                MessageBox.Show(this, "Select a downloaded ready model, context, and preset name first.", "Missing Input");
                return;
            }
            string optArgs, optErr;
            if (!TryBuildEngineOptionArgs(out optArgs, out optErr)) { MessageBox.Show(this, optErr, "Invalid Option"); return; }

            pendingLaunchNext = null;
            StopTrackedProc(ref launchProc, ref launchSink, launchStartFt);
            lastSyncedEnginePid = -1;
            currentEngineLog = Path.Combine(launchDir, preset + ".engine.log");
            if (File.Exists(currentEngineLog)) try { File.WriteAllText(currentEngineLog, "", Program.Utf8NoBom); } catch { }
            engineLogPos = 0;

            string spdArg = (sel.Family == "deepseek41") ? " --speed \"" + cmbSpeed.Text + "\"" : "";
            string launchArgs = string.Format("launch --family \"{0}\" --model \"{1}\" --context \"{2}\"{3} --save-preset \"{4}\"{5}{6} --no-tui",
                sel.Family, sel.Model, ctx, spdArg, preset, optArgs, extraArgs);
            WriteGuiLog(string.Format("{0} [{1}]: strata {2}", actionLabel, sel.FamilyTitle, launchArgs));
            if (stopFirst) {
                launchProc = StartStrata("launch --stop", null, false, ref launchLogPos, ref launchSink, out launchStartFt);
                pendingLaunchNext = delegate { launchProc = StartStrata(launchArgs, launchLogPath, false, ref launchLogPos, ref launchSink, out launchStartFt); };
            } else {
                launchProc = StartStrata(launchArgs, launchLogPath, false, ref launchLogPos, ref launchSink, out launchStartFt);
            }
        }

        private void AppendLogText(string text) {
            if (txtLog.TextLength > 600000) {
                string cur = txtLog.Text;
                int cut = cur.IndexOf('\n', cur.Length / 2);
                txtLog.Text = (cut >= 0) ? cur.Substring(cut + 1) : cur.Substring(cur.Length / 2);
            }
            txtLog.AppendText(text);
            txtLog.ScrollToCaret();
        }

        private void WriteGuiLog(string msg) {
            AppendLogText(string.Format("[{0:HH:mm:ss}] {1}\r\n", DateTime.Now, msg));
            Program.WriteDebugFile(msg);
        }

        private static int GetIndentLen(string line) {
            int n = 0;
            while (n < line.Length && (line[n] == ' ' || line[n] == '\t')) n++;
            return n;
        }

        private static int FindBlockEnd(List<string> lines, int startIdx, int parentIndent, bool allowDash) {
            for (int k = startIdx + 1; k < lines.Count; k++) {
                string ln = lines[k];
                if (string.IsNullOrWhiteSpace(ln) || ln.TrimStart().StartsWith("#")) continue;
                int ind = GetIndentLen(ln);
                if (ind < parentIndent || (ind == parentIndent && !(allowDash && ln.TrimStart().StartsWith("-")))) return k;
            }
            return lines.Count;
        }

        private static int FindYamlLine(List<string> lines, int start, int end, int exactIndent, string bodyRegex) {
            for (int i = start; i < end; i++) {
                string ln = lines[i];
                if (string.IsNullOrWhiteSpace(ln) || ln.TrimStart().StartsWith("#")) continue;
                int ind = GetIndentLen(ln);
                if (exactIndent >= 0 && ind != exactIndent) continue;
                if (Regex.IsMatch(ln.Substring(ind), "^" + bodyRegex)) return i;
            }
            return -1;
        }

        private static bool TryFindProviderBlock(List<string> lines, string providerName, out int idx, out int end, out string indent) {
            idx = -1; end = 0; indent = "";
            int provIdx = FindYamlLine(lines, 0, lines.Count, 0, @"providers:\s*(?:#.*)?$");
            if (provIdx < 0) return false;
            int provEnd = FindBlockEnd(lines, provIdx, 0, false);
            string pat = Regex.Escape(providerName) + @":\s*(?:#.*)?$";
            for (int i = provIdx + 1; i < provEnd; i++) {
                string ln = lines[i];
                if (string.IsNullOrWhiteSpace(ln) || ln.TrimStart().StartsWith("#")) continue;
                int ind = GetIndentLen(ln);
                if (ind > 0 && Regex.IsMatch(ln.Substring(ind), "^" + pat)) {
                    idx = i; indent = ln.Substring(0, ind); end = FindBlockEnd(lines, idx, ind, false); return true;
                }
            }
            return false;
        }

        private static string GetModelIdFromModelsYml(string path, string providerName) {
            if (!File.Exists(path)) return "";
            try {
                List<string> lines = new List<string>(Regex.Split(File.ReadAllText(path, Encoding.UTF8), @"\r?\n"));
                int tIdx, tEnd; string tIndent;
                if (!TryFindProviderBlock(lines, providerName, out tIdx, out tEnd, out tIndent)) return "";
                for (int i = tIdx + 1; i < tEnd; i++) {
                    Match idM = Regex.Match(lines[i], @"^[ \t]+-\s+id:\s*([""']?)([^\s#""']+)\1");
                    if (idM.Success) return idM.Groups[2].Value;
                }
            } catch { }
            return "";
        }

        private bool PatchOmpModelsYml(string path, string providerName, ThinkingProfile prof, string level) {
            if (!File.Exists(path) || string.IsNullOrEmpty(providerName)) return false;
            List<string> lines = new List<string>(Regex.Split(File.ReadAllText(path, Encoding.UTF8), @"\r?\n"));
            while (lines.Count > 0 && lines[lines.Count - 1] == "") lines.RemoveAt(lines.Count - 1);

            int strataIdx, strataEnd; string strataIndent;
            if (!TryFindProviderBlock(lines, providerName, out strataIdx, out strataEnd, out strataIndent)) return false;

            string childIndent = strataIndent + "  ";
            for (int i = strataIdx + 1; i < strataEnd; i++) {
                Match cm = Regex.Match(lines[i], @"^([ \t]+)\S");
                if (cm.Success) { childIndent = cm.Groups[1].Value; break; }
            }
            int sLen = strataIndent.Length, cLen = childIndent.Length;

            KeyValuePair<string, string>[] compatList = {
                new KeyValuePair<string, string>("supportsDeveloperRole", "false"),
                new KeyValuePair<string, string>("supportsReasoningEffort", "true"),
                new KeyValuePair<string, string>("supportsReasoningParams", "true"),
                new KeyValuePair<string, string>("thinkingFormat", "openai"),
                new KeyValuePair<string, string>("reasoningDisableMode", "none-effort"),
                new KeyValuePair<string, string>("disableReasoningOnToolChoice", "false")
            };
            Dictionary<string, string> compatMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> kv in compatList) compatMap[kv.Key] = kv.Value;

            int compatIdx = FindYamlLine(lines, strataIdx + 1, strataEnd, cLen, @"compat:(?:\s*\{[^}]*\})?\s*(?:#.*)?$");
            int modelsIdx = FindYamlLine(lines, strataIdx + 1, strataEnd, cLen, @"models:\s*(?:#.*)?$");
            if (compatIdx >= 0) {
                lines[compatIdx] = childIndent + "compat:";
                int cEnd = Math.Min(strataEnd, FindBlockEnd(lines, compatIdx, cLen, false));
                string kvIndent = childIndent + "  ";
                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                for (int i = compatIdx + 1; i < cEnd; i++) {
                    Match km = Regex.Match(lines[i], @"^([ \t]+)([a-zA-Z0-9_]+):");
                    string val;
                    if (km.Success && compatMap.TryGetValue(km.Groups[2].Value, out val)) {
                        kvIndent = km.Groups[1].Value;
                        lines[i] = string.Format("{0}{1}: {2}", kvIndent, km.Groups[2].Value, val);
                        seen.Add(km.Groups[2].Value);
                    }
                }
                int insAt = cEnd;
                while (insAt > compatIdx + 1 && string.IsNullOrWhiteSpace(lines[insAt - 1])) insAt--;
                foreach (KeyValuePair<string, string> kv in compatList)
                    if (!seen.Contains(kv.Key)) lines.Insert(insAt++, string.Format("{0}{1}: {2}", kvIndent, kv.Key, kv.Value));
            } else {
                int insAt = (modelsIdx >= 0) ? modelsIdx : (strataIdx + 1);
                string kvIndent = childIndent + "  ";
                lines.Insert(insAt++, childIndent + "compat:");
                foreach (KeyValuePair<string, string> kv in compatList)
                    lines.Insert(insAt++, string.Format("{0}{1}: {2}", kvIndent, kv.Key, kv.Value));
            }

            strataEnd = FindBlockEnd(lines, strataIdx, sLen, false);
            modelsIdx = FindYamlLine(lines, strataIdx + 1, strataEnd, cLen, @"models:\s*(?:#.*)?$");
            if (modelsIdx >= 0) {
                int mEnd = Math.Min(strataEnd, FindBlockEnd(lines, modelsIdx, cLen, true));
                List<int> itemStarts = new List<int>();
                for (int i = modelsIdx + 1; i < mEnd; i++) if (Regex.IsMatch(lines[i], @"^[ \t]+-\s+id:")) itemStarts.Add(i);

                for (int idx = itemStarts.Count - 1; idx >= 0; idx--) {
                    int itStart = itemStarts[idx], itEnd = (idx + 1 < itemStarts.Count) ? itemStarts[idx + 1] : mEnd;
                    string propIndent = Regex.Match(lines[itStart], @"^([ \t]+-\s*)").Groups[1].Value.Replace('-', ' ');
                    int thinkIdx = FindYamlLine(lines, itStart + 1, itEnd, propIndent.Length, @"thinking:(?:\s*(?:\{.*\}|[^\r\n#]*))?\s*(?:#.*)?$");
                    int reasonIdx = FindYamlLine(lines, itStart + 1, itEnd, propIndent.Length, @"reasoning:\s*");

                    if (reasonIdx >= 0) lines[reasonIdx] = propIndent + "reasoning: true";
                    else { lines.Insert(itStart + 1, propIndent + "reasoning: true"); if (thinkIdx >= 0) thinkIdx++; itEnd++; }

                    List<string> thinkLines = new List<string> {
                        propIndent + "thinking:", propIndent + "  mode: effort",
                        propIndent + "  efforts: [" + string.Join(", ", prof.Efforts) + "]", propIndent + "  requiresEffort: false"
                    };
                    if (level != "auto" && level != "off" && Array.IndexOf(prof.Efforts, level) >= 0)
                        thinkLines.Add(propIndent + "  defaultLevel: " + level);

                    if (thinkIdx >= 0) {
                        int tEnd = Math.Min(itEnd, FindBlockEnd(lines, thinkIdx, propIndent.Length, false));
                        lines.RemoveRange(thinkIdx, tEnd - thinkIdx);
                        lines.InsertRange(thinkIdx, thinkLines);
                    } else {
                        int insAt = itEnd;
                        while (insAt > itStart + 1 && string.IsNullOrWhiteSpace(lines[insAt - 1])) insAt--;
                        lines.InsertRange(insAt, thinkLines);
                    }
                }
            }
            File.WriteAllText(path, string.Join("\r\n", lines) + "\r\n", Program.Utf8NoBom);
            return true;
        }

        private void SyncOmpConfigFiles(ModelInfo modelInfo, string level, string forceDefaultBaseModel) {
            if (string.IsNullOrEmpty(level)) return;
            ThinkingProfile prof = GetThinkingProfile(modelInfo != null ? modelInfo.Family : "qwen");
            string lvl = ResolveThinkingChoice(level, prof), cfgPath = ResolveOmpFile("config"), modPath = ResolveOmpFile("models");
            try {
                if (!Directory.Exists(ompAgentDir)) Directory.CreateDirectory(ompAgentDir);
                string txt = File.Exists(cfgPath) ? File.ReadAllText(cfgPath, Encoding.UTF8) : "";
                List<string> lines = new List<string>(Regex.Split(txt, @"\r?\n"));
                while (lines.Count > 0 && lines[lines.Count - 1] == "") lines.RemoveAt(lines.Count - 1);

                string activeProvider = "strata", fallbackTarget = (!string.IsNullOrEmpty(forceDefaultBaseModel) ? forceDefaultBaseModel : "strata/strata") + ":" + lvl;
                int mrIdx = FindYamlLine(lines, 0, lines.Count, 0, @"modelRoles:(\s*\{\s*\})?\s*(?:#.*)?$");
                if (mrIdx >= 0) {
                    if (Regex.IsMatch(lines[mrIdx], @"^modelRoles:\s*\{\s*\}")) lines[mrIdx] = "modelRoles:";
                    int mrEnd = FindBlockEnd(lines, mrIdx, 0, false), defaultLineIdx = -1;
                    string roleIndent = "  ";
                    for (int i = mrIdx + 1; i < mrEnd; i++) {
                        string ln = lines[i];
                        if (string.IsNullOrWhiteSpace(ln) || ln.TrimStart().StartsWith("#")) continue;
                        Match indentM = Regex.Match(ln, @"^([ \t]+)\S");
                        if (indentM.Success && roleIndent == "  ") roleIndent = indentM.Groups[1].Value;
                        if (Regex.IsMatch(ln, @"^[ \t]+default:\s*")) { defaultLineIdx = i; break; }
                    }
                    if (defaultLineIdx >= 0) {
                        Match rm = Regex.Match(lines[defaultLineIdx], @"^([ \t]+default:\s*)([""']?)([^\s#""']+)([""']?)(.*)$");
                        if (rm.Success) {
                            string existingBase = Regex.Replace(rm.Groups[3].Value, @":(off|minimal|low|medium|high|xhigh|max|auto|inherit)$", "", RegexOptions.IgnoreCase);
                            string newBase = !string.IsNullOrEmpty(forceDefaultBaseModel) ? forceDefaultBaseModel : existingBase;
                            int slashIdx = newBase.IndexOf('/');
                            if (slashIdx > 0) activeProvider = newBase.Substring(0, slashIdx);
                            lines[defaultLineIdx] = rm.Groups[1].Value + rm.Groups[2].Value + newBase + ":" + lvl + rm.Groups[4].Value + rm.Groups[5].Value;
                        } else lines[defaultLineIdx] = roleIndent + "default: " + fallbackTarget;
                    } else lines.Insert(mrIdx + 1, roleIndent + "default: " + fallbackTarget);
                } else {
                    lines.Add("modelRoles:");
                    lines.Add("  default: " + fallbackTarget);
                }

                int dtlIdx = FindYamlLine(lines, 0, lines.Count, 0, @"defaultThinkingLevel:\s*");
                string[] validDtl = { "minimal", "low", "medium", "high", "xhigh", "max", "auto" };
                if (lvl == "off") {
                    if (dtlIdx >= 0) {
                        Match dtlM = Regex.Match(lines[dtlIdx], @"^(defaultThinkingLevel:\s*)([^\s#]*)(.*)$");
                        string curDtl = dtlM.Success ? dtlM.Groups[2].Value.ToLowerInvariant() : "";
                        if (Array.IndexOf(validDtl, curDtl) < 0)
                            lines[dtlIdx] = (dtlM.Success ? dtlM.Groups[1].Value : "defaultThinkingLevel: ") + prof.Efforts[0] + (dtlM.Success ? dtlM.Groups[3].Value : "");
                    } else lines.Add("defaultThinkingLevel: " + prof.Efforts[0]);
                } else if (dtlIdx >= 0) {
                    Match dtlM = Regex.Match(lines[dtlIdx], @"^(defaultThinkingLevel:\s*)([^\s#]*)(.*)$");
                    lines[dtlIdx] = (dtlM.Success ? dtlM.Groups[1].Value : "defaultThinkingLevel: ") + lvl + (dtlM.Success ? dtlM.Groups[3].Value : "");
                } else lines.Add("defaultThinkingLevel: " + lvl);

                File.WriteAllText(cfgPath, string.Join("\r\n", lines) + "\r\n", Program.Utf8NoBom);
                bool patched = PatchOmpModelsYml(modPath, "strata", prof, lvl) ||
                               (!string.Equals(activeProvider, "strata", StringComparison.OrdinalIgnoreCase) && PatchOmpModelsYml(modPath, activeProvider, prof, lvl));
                string msg = string.Format("Synced OMP thinking -> {0} (role provider: {1}, efforts: [{2}])", lvl, activeProvider, string.Join(", ", prof.Efforts));
                if (!patched) msg += string.Format(" [NOTE: '{0}' is not in models.yml; click 'Sync omp' while serving to configure provider 'strata']", activeProvider);
                WriteGuiLog(msg);
            } catch (Exception ex) { WriteGuiLog("WARNING: Could not update OMP config: " + ex.Message); }
        }

        private static bool IsProcRunning(Process p) {
            if (p == null) return false;
            try { return !p.HasExited; } catch { return false; }
        }

        private static void StopTrackedProc(ref Process p, ref LogSink sink, long startFt) {
            if (sink != null) { sink.Cancelled = true; sink = null; }
            if (p == null) return;
            try { Program.StopTrackedProcess(p.Id, startFt); } catch { }
            try { p.Dispose(); } catch { }
            p = null;
        }

        private static void AppendLogBytes(string path, LogSink sink, byte[] buf, int count) {
            if (string.IsNullOrEmpty(path) || sink.Cancelled || count <= 0) return;
            lock (sink.Sync) {
                if (sink.Cancelled) return;
                try {
                    using (FileStream fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                        fs.Write(buf, 0, count);
                } catch { }
            }
        }

        private static Thread StartStreamPump(Stream stream, string logFile, LogSink sink) {
            Thread t = new Thread(delegate() {
                byte[] buf = new byte[4096];
                try {
                    int n;
                    while (!sink.Cancelled && (n = stream.Read(buf, 0, buf.Length)) > 0)
                        AppendLogBytes(logFile, sink, buf, n);
                } catch { }
            }) { IsBackground = true };
            t.Start();
            return t;
        }

        private static Process StartStrata(string args, string logFile, bool append, ref long posRef, ref LogSink activeSink, out long startFt) {
            startFt = 0;
            if (activeSink != null) { activeSink.Cancelled = true; activeSink = null; }
            if (!string.IsNullOrEmpty(logFile) && !append) {
                try { File.WriteAllText(logFile, "", Program.Utf8NoBom); } catch { }
                posRef = 0;
            }
            LogSink sink = new LogSink();
            activeSink = sink;
            try {
                Process p = Process.Start(BuildToolStartInfo("strata", args, false, true, null));
                if (p == null) return null;
                try { if (!p.HasExited) { startFt = p.StartTime.ToFileTimeUtc(); Program.UpdatePidFile(p.Id, startFt, 0); } } catch { }
                sink.OutThread = StartStreamPump(p.StandardOutput.BaseStream, logFile, sink);
                sink.ErrThread = StartStreamPump(p.StandardError.BaseStream, logFile, sink);
                return p;
            } catch (Exception ex) {
                byte[] errBytes = Program.Utf8NoBom.GetBytes("ERROR launching strata: " + ex.Message + "\r\n");
                AppendLogBytes(logFile, sink, errBytes, errBytes.Length);
                return null;
            }
        }

        private void SyncLogTail(string path, ref long lastPos, string tag, Process proc) {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                    if (fs.Length < lastPos) lastPos = 0;
                    if (fs.Length <= lastPos) return;
                    fs.Seek(lastPos, SeekOrigin.Begin);
                    using (StreamReader sr = new StreamReader(fs, Encoding.UTF8)) {
                        string delta = sr.ReadToEnd();
                        lastPos = fs.Position;
                        if (string.IsNullOrEmpty(delta)) return;
                        AppendLogText(delta.IndexOf('\n') >= 0 ? Regex.Replace(delta, @"(?<!\r)\n", "\r\n") : delta);
                        Program.WriteDebugFile(string.Format("[{0}] {1}", tag, delta.Trim()));
                        if (!isPrompting && IsProcRunning(proc) && Regex.IsMatch(delta, @"(\[y/N\]|\[Y/n\]|\(y/n\))\s*$")) {
                            isPrompting = true;
                            try {
                                string[] lines = delta.Trim().Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                                int take = Math.Min(3, lines.Length);
                                string q = string.Join("\n", lines, lines.Length - take, take);
                                DialogResult ans = MessageBox.Show(this, q + "\n\nClick YES to send 'y' or NO to send 'n'.", "Confirmation Required", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                                string rep = (ans == DialogResult.Yes) ? "y" : "n";
                                AppendLogText(rep + "\r\n");
                                if (IsProcRunning(proc)) {
                                    try { proc.StandardInput.WriteLine(rep); proc.StandardInput.Flush(); } catch { }
                                }
                            } finally { isPrompting = false; }
                        }
                    }
                }
            } catch { }
        }

        private static Process GetEngineProc() {
            Process best = null;
            long bestWs = -1L;
            foreach (string name in EngineProcNames) {
                foreach (Process p in Process.GetProcessesByName(name)) {
                    try {
                        long ws = p.WorkingSet64;
                        bool isMatch = !string.Equals(p.ProcessName, "strata", StringComparison.OrdinalIgnoreCase) || (ws > 250L * 1024L * 1024L);
                        if (!isMatch) try { isMatch = Regex.IsMatch(p.MainModule.FileName, "engine", RegexOptions.IgnoreCase); } catch { }
                        if (isMatch && ws > bestWs) {
                            if (best != null) best.Dispose();
                            best = p; bestWs = ws; continue;
                        }
                    } catch { }
                    p.Dispose();
                }
            }
            return best;
        }

        private bool ProbeLocalServer(out string loadedId) {
            loadedId = "";
            try {
                bool listening = false;
                foreach (IPEndPoint ep in ipProps.GetActiveTcpListeners()) if (ep.Port == 8080) { listening = true; break; }
                if (!listening) return false;
            } catch { }
            try {
                using (CancellationTokenSource cts = new CancellationTokenSource(500))
                using (HttpResponseMessage resp = LocalHttp.GetAsync(LocalModelsUrl, cts.Token).ConfigureAwait(false).GetAwaiter().GetResult()) {
                    if ((int)resp.StatusCode >= 500) return false;
                    if (resp.IsSuccessStatusCode) {
                        string body = resp.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                        Dictionary<string, object> root = AsDict(json.DeserializeObject(body));
                        object dataObj;
                        if (root != null && root.TryGetValue("data", out dataObj) && dataObj is object[]) {
                            object[] data = (object[])dataObj;
                            if (data.Length > 0) loadedId = GetJsonStr(AsDict(data[0]), "id");
                        }
                    }
                    return true;
                }
            } catch { return false; }
        }

        private string FindModelMapKey(string loadedId) {
            if (string.IsNullOrEmpty(loadedId) || loadedId == "strata") return "";
            ModelInfo info = ResolveModelInfo(loadedId, ""), existing;
            if (modelMap.TryGetValue(info.Display, out existing)) return existing.Display;
            foreach (string k in modelKeys) {
                ModelInfo v = modelMap[k];
                if (v.Family == info.Family && (string.Equals(v.Model, info.Model, StringComparison.OrdinalIgnoreCase) || loadedId.IndexOf(v.Model, StringComparison.OrdinalIgnoreCase) >= 0))
                    return k;
            }
            return "";
        }

        private void OnPollTimerTick(object sender, EventArgs e) {
            if (isPolling || isPrompting) return;
            isPolling = true;
            try {
                if (launchProc != null && !IsProcRunning(launchProc)) {
                    if (launchSink != null) { launchSink.Wait(200); launchSink.Cancelled = true; launchSink = null; }
                    SyncLogTail(launchLogPath, ref launchLogPos, "LAUNCH_OUT", null);
                    try { Program.UpdatePidFile(0, 0, launchProc.Id); } catch { }
                    try { launchProc.Dispose(); } catch { }
                    launchProc = null; launchStartFt = 0;
                    if (pendingLaunchNext != null) { Action next = pendingLaunchNext; pendingLaunchNext = null; next(); }
                } else {
                    SyncLogTail(launchLogPath, ref launchLogPos, "LAUNCH_OUT", launchProc);
                }

                if (actionProc != null && !IsProcRunning(actionProc)) {
                    if (actionSink != null) { actionSink.Wait(200); actionSink.Cancelled = true; actionSink = null; }
                    SyncLogTail(cmdLogPath, ref cmdLogPos, "CMD_OUT", null);
                    bool exitOk = false;
                    try { exitOk = (actionProc.ExitCode == 0); } catch { }
                    try { Program.UpdatePidFile(0, 0, actionProc.Id); } catch { }
                    try { actionProc.Dispose(); } catch { }
                    actionProc = null; actionStartFt = 0;

                    if (pendingOmpSync != null) {
                        PendingOmpSyncInfo pending = pendingOmpSync;
                        pendingOmpSync = null;
                        if (pending.HadOriginalConfig && pending.OriginalConfigText != null && !string.IsNullOrEmpty(pending.ConfigPath))
                            try { File.WriteAllText(pending.ConfigPath, pending.OriginalConfigText, Program.Utf8NoBom); } catch { }
                        if (exitOk) {
                            ModelInfo syncInfo = pending.Info;
                            string loadedId;
                            ProbeLocalServer(out loadedId);
                            if (!string.IsNullOrEmpty(loadedId) && loadedId != "strata") syncInfo = ResolveModelInfo(loadedId, "");
                            string modYmlId = GetModelIdFromModelsYml(ResolveOmpFile("models"), "strata");
                            string targetId = !string.IsNullOrEmpty(modYmlId) ? modYmlId : (!string.IsNullOrEmpty(loadedId) ? loadedId : "strata");
                            SyncOmpConfigFiles(syncInfo, ResolveThinkingChoice(pending.Level, GetThinkingProfile(syncInfo != null ? syncInfo.Family : "qwen")), "strata/" + targetId);
                            actionProc = StartStrata("connect omp --check", cmdLogPath, true, ref cmdLogPos, ref actionSink, out actionStartFt);
                        } else {
                            WriteGuiLog("WARNING: 'strata connect omp --write --yes' did not complete; original config.yml preserved.");
                        }
                    } else if (pendingActionNext != null) {
                        Action<bool> next = pendingActionNext;
                        pendingActionNext = null;
                        next(exitOk);
                    }
                } else {
                    SyncLogTail(cmdLogPath, ref cmdLogPos, "CMD_OUT", actionProc);
                }

                if (!string.IsNullOrEmpty(currentEngineLog)) SyncLogTail(currentEngineLog, ref engineLogPos, "ENGINE_LOG", null);

                string statusTxt, procTxt, loadedModelId = "";
                Color statusClr;
                using (Process eng = GetEngineProc()) {
                    if (eng != null && ProbeLocalServer(out loadedModelId)) {
                        if (lastSyncedEnginePid != eng.Id && !string.IsNullOrEmpty(loadedModelId)) {
                            lastSyncedEnginePid = eng.Id;
                            string matchedKey = FindModelMapKey(loadedModelId);
                            string curSelected = cmbModel.SelectedItem != null ? cmbModel.SelectedItem.ToString() : "";
                            if (!string.IsNullOrEmpty(matchedKey) && curSelected != matchedKey) cmbModel.SelectedItem = matchedKey;
                            else if (loadedModelId != "strata") UpdateThinkingOptions(ResolveModelInfo(loadedModelId, "").Family, null);
                        }
                        statusTxt = "[SERVING - READY]";
                        statusClr = ColorServing;
                        procTxt = string.Format("{0}.exe (PID {1}) | RAM: {2:0.0} GB | http://127.0.0.1:8080/v1", eng.ProcessName, eng.Id, eng.WorkingSet64 / (1024.0 * 1024.0 * 1024.0));
                    } else if (eng != null || IsProcRunning(launchProc) || pendingLaunchNext != null) {
                        string info = (eng != null) ? string.Format("PID {0} | RAM: {1:0.0} GB", eng.Id, eng.WorkingSet64 / (1024.0 * 1024.0 * 1024.0)) : "Initializing...";
                        statusTxt = "[LOADING MODEL...]";
                        statusClr = ColorLoading;
                        procTxt = "Starting engine (" + info + ")";
                    } else {
                        lastSyncedEnginePid = -1;
                        statusTxt = "[STOPPED]";
                        statusClr = ColorStopped;
                        procTxt = "No engine process running";
                    }
                }
                if (lastStatusText != statusTxt) { lastStatusText = statusTxt; lblStatus.Text = statusTxt; lblStatus.ForeColor = statusClr; }
                if (lastProcInfo != procTxt) { lastProcInfo = procTxt; lblProcInfo.Text = procTxt; }
            } finally { isPolling = false; }
        }

        protected override void OnShown(EventArgs e) {
            base.OnShown(e);
            Activate();
            BeginInvoke((MethodInvoker)delegate { OnPollTimerTick(null, EventArgs.Empty); });
        }

        protected override void OnFormClosing(FormClosingEventArgs e) {
            using (Process eng = GetEngineProc()) {
                if (eng != null || IsProcRunning(launchProc)) {
                    DialogResult ans = MessageBox.Show(this,
                        "Strata Engine is running.\n\nYES = Stop engine before exiting\nNO = Leave running in background\nCANCEL = Stay in controller",
                        "Strata Engine Running", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    if (ans == DialogResult.Cancel) { e.Cancel = true; return; }
                    if (ans == DialogResult.Yes) ForceStopAllStrata(false);
                }
            }
            pendingOmpSync = null; pendingActionNext = null; pendingLaunchNext = null;
            StopTrackedProc(ref actionProc, ref actionSink, actionStartFt);
            if (pollTimer != null) pollTimer.Stop();
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing) {
            if (disposing) {
                if (launchSink != null) { launchSink.Cancelled = true; launchSink = null; }
                if (actionSink != null) { actionSink.Cancelled = true; actionSink = null; }
                if (pollTimer != null) { pollTimer.Stop(); pollTimer.Dispose(); pollTimer = null; }
                if (launchProc != null) { try { launchProc.Dispose(); } catch { } launchProc = null; }
                if (actionProc != null) { try { actionProc.Dispose(); } catch { } actionProc = null; }
                if (toolTip != null) { toolTip.Dispose(); toolTip = null; }
                if (appIcon != null) { appIcon.Dispose(); appIcon = null; }
                fontUI.Dispose(); fontBold.Dispose(); fontStatus.Dispose(); fontMono.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}