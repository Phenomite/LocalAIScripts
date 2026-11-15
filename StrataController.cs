using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace StrataControllerApp
{
    internal sealed class ModelInfo
    {
        public string Family, FamilyTitle, Model, Display;
    }

    internal sealed class ThinkingProfile
    {
        public string Key, DefaultLevel;
        public string[] UiLevels, Efforts;
    }

    internal sealed class PendingOmpSyncInfo
    {
        public ModelInfo Info;
        public string Level;
        public string ConfigPath;
        public string OriginalConfigText;
        public bool HadOriginalConfig;
    }

    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        public static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
        public static string DebugLogPath;

        [STAThread]
        private static void Main()
        {
            string bootLog = Environment.GetEnvironmentVariable("STRATA_BOOTLOG");
            DebugLogPath = !string.IsNullOrEmpty(bootLog)
                ? bootLog
                : Path.Combine(Path.GetTempPath(), "StrataController.debug.log");

            try { SetProcessDPIAware(); } catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
            {
                ReportFatal("Unhandled UI Exception", e.Exception);
            };

            try
            {
                CleanupOrphanProcesses();
                File.WriteAllText(
                    DebugLogPath,
                    string.Format("[{0:yyyy-MM-dd HH:mm:ss.fff}] Bootstrap starting\r\n", DateTime.Now),
                    Utf8NoBom);

                using (StrataControllerForm form = new StrataControllerForm())
                {
                    Application.Run(form);
                }
            }
            catch (Exception ex)
            {
                ReportFatal("Unhandled Controller Exception", ex);
                Environment.ExitCode = 1;
            }
        }

        public static void WriteDebugFile(string msg)
        {
            try
            {
                File.AppendAllText(
                    DebugLogPath,
                    string.Format("[{0:yyyy-MM-dd HH:mm:ss.fff}] {1}\r\n", DateTime.Now, msg),
                    Utf8NoBom);
            }
            catch { }
        }

        private static void ReportFatal(string title, Exception ex)
        {
            string err = ex != null ? ex.ToString() : "Unknown error";
            WriteDebugFile(title + ": " + err);
            try { MessageBox.Show(err, "Strata Controller Error", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
        }

        private static void CleanupOrphanProcesses()
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT ProcessId, CommandLine FROM Win32_Process WHERE (Name='cmd.exe' OR Name='strata.exe') AND CommandLine LIKE '%strata%'"))
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementObject mo in results)
                    {
                        try
                        {
                            object pidObj = mo["ProcessId"], cmdObj = mo["CommandLine"];
                            string cmdLine = cmdObj != null ? cmdObj.ToString() : "";
                            if (pidObj != null && Regex.IsMatch(cmdLine, @"strata_gui_|strata connect", RegexOptions.IgnoreCase))
                                KillPidTree(Convert.ToInt32(pidObj));
                        }
                        catch { }
                        finally { mo.Dispose(); }
                    }
                }
            }
            catch { }
        }

        public static void KillPidTree(int pid)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("taskkill.exe", "/F /T /PID " + pid)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (Process tk = Process.Start(psi))
                {
                    if (tk != null) tk.WaitForExit(3000);
                }
            }
            catch { }
        }
    }

    internal sealed class StrataControllerForm : Form
    {
        private static readonly string[] EngineProcNames = { "strata-dsv41", "strata-server", "strata" };
        private static readonly string[] KillProcNames   = { "strata-dsv41", "strata-server", "strata-vision", "strata" };
        private static readonly Dictionary<string, string> FamilyTitles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "deepseek41", "DeepSeek V4.1-Flash" },
            { "coder",      "Qwen3.8 Coder" },
            { "gyro",       "Qwen3.8 Gyro" },
            { "swift",      "Qwen3.8 Swift 1.5" },
            { "unsloth",    "Qwen3.8 Unsloth" },
            { "nvfp4",      "Qwen3.8 NVFP4" },
            { "qwen36",     "Qwen3.6-35B-A3B" }
        };

        private static readonly Color BgDark       = Color.FromArgb(24, 24, 28);
        private static readonly Color BgPanel      = Color.FromArgb(34, 34, 40);
        private static readonly Color BgInput      = Color.FromArgb(45, 45, 54);
        private static readonly Color FgText       = Color.FromArgb(235, 235, 240);
        private static readonly Color FgMuted      = Color.FromArgb(165, 165, 180);
        private static readonly Color ColorServing = Color.FromArgb(65, 215, 95);
        private static readonly Color ColorLoading = Color.FromArgb(245, 185, 45);
        private static readonly Color ColorStopped = Color.FromArgb(230, 75, 75);

        private readonly Font fontUI     = new Font("Segoe UI", 9.5f);
        private readonly Font fontBold   = new Font("Segoe UI Semibold", 9.5f);
        private readonly Font fontStatus = new Font("Segoe UI Semibold", 11f);
        private readonly Font fontMono   = new Font("Consolas", 9f);

        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private readonly IPGlobalProperties ipProps = IPGlobalProperties.GetIPGlobalProperties();

        private readonly string strataLocalDir, launchDir, installDir, ompAgentDir, tempLaunchLog, tempCmdLog;
        private readonly Dictionary<string, ModelInfo> modelMap = new Dictionary<string, ModelInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> modelKeys = new List<string>();
        private string initialThinking = "medium";

        private ToolTip toolTip;
        private Label lblStatus, lblProcInfo;
        private ComboBox cmbModel, cmbCtx, cmbSpeed, cmbPleIo, cmbVramReserve, cmbThinking;
        private CheckBox chkOmpContinue;
        private TextBox txtPreset, txtLog;
        private Button btnStart, btnStop, btnKill, btnTune, btnWireOmp, btnRunOmp, btnOpenLog;
        private Timer pollTimer;

        private bool suppressThinkingSync, isPrompting;
        private string lastThinkingKey = "", currentEngineLog = "", lastStatusText = "", lastProcInfo = "";
        private long engineLogPos, launchLogPos, cmdLogPos;
        private Process launchProc, actionProc;
        private PendingOmpSyncInfo pendingOmpSync;
        private int lastSyncedEnginePid = -1;

        public StrataControllerForm()
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string tempDir = Path.GetTempPath();

            SetupEnvironmentPath(userProfile, localAppData, appData);

            strataLocalDir = Path.Combine(localAppData, "Strata");
            launchDir      = Path.Combine(strataLocalDir, "launch");
            installDir     = Path.Combine(strataLocalDir, "install");
            ompAgentDir    = Path.Combine(Path.Combine(userProfile, ".omp"), "agent");
            tempLaunchLog  = Path.Combine(tempDir, "strata_gui_launch.log");
            tempCmdLog     = Path.Combine(tempDir, "strata_gui_cmd.log");

            try
            {
                foreach (string f in Directory.EnumerateFiles(tempDir, "strata_gui_*.log"))
                    try { File.Delete(f); } catch { }
            }
            catch { }

            File.WriteAllText(tempLaunchLog, "", Program.Utf8NoBom);
            File.WriteAllText(tempCmdLog, "", Program.Utf8NoBom);

            using (Process initEng = GetEngineProc())
            {
                if (initEng == null && Directory.Exists(launchDir))
                {
                    try
                    {
                        foreach (string f in Directory.EnumerateFiles(launchDir, "*.engine.log"))
                            try { File.WriteAllText(f, "", Program.Utf8NoBom); } catch { }
                    }
                    catch { }
                }
            }

            DiscoverReadyModels(userProfile);
            ReadInitialThinking();
            BuildUi();

            WriteGuiLog("Debug log: " + Program.DebugLogPath);
            if (modelKeys.Count > 0)
            {
                foreach (string k in modelKeys)
                {
                    ModelInfo v = modelMap[k];
                    WriteGuiLog(string.Format("Ready Model: {0} [--family {1} --model {2}]", v.FamilyTitle, v.Family, v.Model));
                }
            }
            else
            {
                WriteGuiLog("WARNING: No downloaded/prepared Strata models detected. Run 'strata launch' in terminal to download one.");
            }

            WireEvents();

            pollTimer = new Timer { Interval = 400 };
            pollTimer.Tick += OnPollTimerTick;
            pollTimer.Start();
        }

        private static void SetupEnvironmentPath(string userProfile, string localAppData, string appData)
        {
            HashSet<string> pathSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> pathList = new List<string>();
            string[] sources =
            {
                Path.Combine(userProfile, @".local\bin"),
                Path.Combine(localAppData, @"Strata\bin"),
                Path.Combine(localAppData, "Strata"),
                Path.Combine(localAppData, @"Programs\Strata"),
                Path.Combine(userProfile, @".omp\bin"),
                Path.Combine(appData, "npm"),
                Environment.GetEnvironmentVariable("PATH"),
                Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User),
                Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine)
            };

            foreach (string src in sources)
            {
                if (string.IsNullOrEmpty(src)) continue;
                foreach (string part in src.Split(';'))
                {
                    string expanded = Environment.ExpandEnvironmentVariables(part.Trim());
                    if (!string.IsNullOrEmpty(expanded) && pathSet.Add(expanded))
                        pathList.Add(expanded);
                }
            }
            Environment.SetEnvironmentVariable("PATH", string.Join(";", pathList), EnvironmentVariableTarget.Process);
        }

        private static string FindCommandOnPath(string command)
        {
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            string pathExt = Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD";
            string[] exts = pathExt.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string dir in pathEnv.Split(';'))
            {
                string trimmed = (dir ?? "").Trim().Trim('"');
                if (string.IsNullOrEmpty(trimmed)) continue;

                string direct = Path.Combine(trimmed, command);
                if (File.Exists(direct)) return direct;

                foreach (string ext in exts)
                {
                    string candidate = direct + ext.Trim();
                    if (File.Exists(candidate)) return candidate;
                }
            }
            return null;
        }

        private string ResolveOmpFile(string baseName)
        {
            string yaml = Path.Combine(ompAgentDir, baseName + ".yaml");
            string yml = Path.Combine(ompAgentDir, baseName + ".yml");
            return (!File.Exists(yml) && File.Exists(yaml)) ? yaml : yml;
        }

        private static bool PathExists(string path)
        {
            return !string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path));
        }

        private static ModelInfo ResolveModelInfo(string rawModel, string rawFamily)
        {
            string m = Regex.Replace(Regex.Replace((rawModel ?? "").Trim(), @"^strata-", "", RegexOptions.IgnoreCase), @"\.json$", "", RegexOptions.IgnoreCase).ToLowerInvariant();
            string f = (rawFamily ?? "").Trim().ToLowerInvariant();

            Match match;
            if ((match = Regex.Match(m, @"^deepseek-v4\.1-flash-(.+)$", RegexOptions.IgnoreCase)).Success)
            {
                m = match.Groups[1].Value;
                f = "deepseek41";
            }
            else if ((match = Regex.Match(m, @"^qwen3\.6-(?:35b-a3b-)?(.+)$", RegexOptions.IgnoreCase)).Success)
            {
                m = match.Groups[1].Value;
                if (string.IsNullOrEmpty(f)) f = "qwen36";
            }
            else
            {
                m = Regex.Replace(m, @"^qwen3\.8-flash-next-", "", RegexOptions.IgnoreCase);
                if ((match = Regex.Match(m, @"^gyro-(.+)$", RegexOptions.IgnoreCase)).Success)
                {
                    m = match.Groups[1].Value;
                    f = "gyro";
                }
                else if ((match = Regex.Match(m, @"^(coder|swift|unsloth|nvfp4|qwen36)-(.+)$", RegexOptions.IgnoreCase)).Success)
                {
                    if (string.IsNullOrEmpty(f)) f = match.Groups[1].Value;
                    m = match.Groups[2].Value;
                }
            }

            if (string.IsNullOrEmpty(f))
            {
                if (Regex.IsMatch(m, @"^(ist-?a?-?das|deepseek|dsv41)|bpw$", RegexOptions.IgnoreCase)) f = "deepseek41";
                else if (Regex.IsMatch(m, @"^iq1_m$", RegexOptions.IgnoreCase)) f = "coder";
                else if (Regex.IsMatch(m, @"^gyro-", RegexOptions.IgnoreCase)) f = "gyro";
                else if (Regex.IsMatch(m, @"^nvfp4", RegexOptions.IgnoreCase)) f = "nvfp4";
                else if (Regex.IsMatch(m, @"^(ud-|q8_0)", RegexOptions.IgnoreCase)) f = "unsloth";
                else f = "qwen";
            }

            string fTitle;
            if (!FamilyTitles.TryGetValue(f, out fTitle)) fTitle = "Qwen3.8-Flash-Next";
            return new ModelInfo
            {
                Family = f,
                FamilyTitle = fTitle,
                Model = m,
                Display = string.Format("[{0}]  {1}", fTitle, m)
            };
        }

        private static ThinkingProfile GetThinkingProfile(string family)
        {
            switch (family)
            {
                case "deepseek41":
                    return new ThinkingProfile { Key = "deepseek41", UiLevels = new[] { "auto", "off", "low", "medium", "high", "max" }, Efforts = new[] { "low", "medium", "high", "max" }, DefaultLevel = "high" };
                case "qwen36":
                    return new ThinkingProfile { Key = "qwen36", UiLevels = new[] { "auto", "off", "low", "medium", "high" }, Efforts = new[] { "low", "medium", "high" }, DefaultLevel = "medium" };
                default:
                    return new ThinkingProfile { Key = "qwen38", UiLevels = new[] { "auto", "off", "low", "medium", "xhigh" }, Efforts = new[] { "low", "medium", "xhigh" }, DefaultLevel = "medium" };
            }
        }

        private static string ResolveThinkingChoice(string choice, ThinkingProfile prof)
        {
            string c = (choice ?? "").Trim().ToLowerInvariant(), mapped = prof.DefaultLevel;
            if (Array.IndexOf(prof.UiLevels, c) >= 0) return c;
            if (c == "minimal") mapped = "off";
            else if (c == "high") mapped = Array.IndexOf(prof.UiLevels, "xhigh") >= 0 ? "xhigh" : "high";
            else if (c == "xhigh") mapped = Array.IndexOf(prof.UiLevels, "high") >= 0 ? "high" : "xhigh";
            else if (c == "max") mapped = Array.IndexOf(prof.UiLevels, "xhigh") >= 0 ? "xhigh" : (Array.IndexOf(prof.UiLevels, "high") >= 0 ? "high" : prof.DefaultLevel);
            return Array.IndexOf(prof.UiLevels, mapped) >= 0 ? mapped : prof.DefaultLevel;
        }

        private void AddReadyInfo(ModelInfo info)
        {
            if (info != null && !modelMap.ContainsKey(info.Display))
            {
                modelMap[info.Display] = info;
                modelKeys.Add(info.Display);
            }
        }

        private void AddReadyModel(string mod, string fam)
        {
            if (!string.IsNullOrEmpty(mod)) AddReadyInfo(ResolveModelInfo(mod, fam));
        }

        private static Dictionary<string, object> AsDict(object obj)
        {
            return obj as Dictionary<string, object>;
        }

        private static string GetJsonStr(Dictionary<string, object> dict, string key)
        {
            object v;
            return (dict != null && dict.TryGetValue(key, out v) && v != null) ? v.ToString() : "";
        }

        private static bool GetJsonBool(Dictionary<string, object> dict, string key)
        {
            object v;
            return dict != null && dict.TryGetValue(key, out v) && v is bool && (bool)v;
        }

        private void DiscoverReadyModels(string userProfile)
        {
            if (!string.IsNullOrEmpty(FindCommandOnPath("strata")))
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c strata models list --installed --output json 2>nul")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        StandardOutputEncoding = Encoding.UTF8
                    };
                    using (Process p = Process.Start(psi))
                    {
                        if (p != null)
                        {
                            string cliJson = p.StandardOutput.ReadToEnd();
                            p.WaitForExit(8000);
                            int brace = cliJson.IndexOf('{'), bracket = cliJson.IndexOf('[');
                            int start = (brace >= 0 && (bracket < 0 || brace < bracket)) ? brace : bracket;
                            if (start >= 0)
                            {
                                object parsed = json.DeserializeObject(cliJson.Substring(start));
                                object[] items = parsed as object[];
                                Dictionary<string, object> root = AsDict(parsed);
                                if (root != null)
                                {
                                    object listObj;
                                    if (root.TryGetValue("models", out listObj) || root.TryGetValue("items", out listObj))
                                        items = listObj as object[];
                                    else
                                        items = new object[] { root };
                                }
                                if (items != null)
                                {
                                    foreach (object itemObj in items)
                                    {
                                        Dictionary<string, object> it = AsDict(itemObj);
                                        if (it != null && (GetJsonStr(it, "component_status") == "present" || GetJsonBool(it, "ready")))
                                        {
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
                }
                catch { }
            }

            HashSet<string> rootSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> dataRoots = new List<string>();
            string[] defaultRoots =
            {
                strataLocalDir,
                Path.Combine(strataLocalDir, "Strata-data"),
                Path.Combine(userProfile, @".strata\Strata-data"),
                Path.Combine(userProfile, "Strata"),
                Path.Combine(userProfile, "Strata-data"),
                Path.Combine(userProfile, @"Documents\Strata")
            };
            foreach (string r in defaultRoots)
                if (!string.IsNullOrEmpty(r) && rootSet.Add(r)) dataRoots.Add(r);

            string[] cfgFiles =
            {
                Path.Combine(strataLocalDir, "settings.json"),
                Path.Combine(strataLocalDir, "app-settings.json"),
                Path.Combine(userProfile, @".config\strata\settings.json")
            };
            foreach (string cfgFile in cfgFiles)
            {
                if (!File.Exists(cfgFile)) continue;
                try
                {
                    Dictionary<string, object> c = AsDict(json.DeserializeObject(File.ReadAllText(cfgFile, Encoding.UTF8)));
                    foreach (string key in new[] { "data_dir", "models_dir" })
                    {
                        string p = GetJsonStr(c, key);
                        if (!string.IsNullOrEmpty(p) && rootSet.Add(p)) dataRoots.Add(p);
                    }
                }
                catch { }
            }

            HashSet<string> readyPacks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string root in dataRoots)
            {
                string pkDir = Path.Combine(root, "packs");
                if (!Directory.Exists(pkDir)) continue;
                try
                {
                    foreach (string sub in Directory.EnumerateDirectories(pkDir))
                    {
                        bool hasFiles = false;
                        try
                        {
                            using (IEnumerator<string> en = Directory.EnumerateFiles(sub).GetEnumerator())
                                hasFiles = en.MoveNext();
                        }
                        catch { }

                        if (hasFiles)
                        {
                            string name = Path.GetFileName(sub);
                            readyPacks.Add(name);
                            AddReadyModel(name, "");
                        }
                    }
                }
                catch { }
            }

            foreach (string dir in new[] { installDir, launchDir })
            {
                if (!Directory.Exists(dir)) continue;
                try
                {
                    foreach (string fPath in Directory.EnumerateFiles(dir, "*.json"))
                    {
                        try
                        {
                            Dictionary<string, object> j = AsDict(json.DeserializeObject(File.ReadAllText(fPath, Encoding.UTF8)));
                            if (j == null) continue;
                            string baseName = Path.GetFileNameWithoutExtension(fPath);
                            bool packOk = false;
                            object argsObj;
                            if (j.TryGetValue("args", out argsObj) && argsObj is object[])
                            {
                                object[] jArgs = (object[])argsObj;
                                for (int i = 0; i < jArgs.Length - 1; i++)
                                {
                                    if (jArgs[i] != null && jArgs[i].ToString() == "--pack" &&
                                        jArgs[i + 1] != null && PathExists(jArgs[i + 1].ToString()))
                                    {
                                        packOk = true;
                                        break;
                                    }
                                }
                            }

                            string mName = GetJsonStr(j, "model");
                            if (string.IsNullOrEmpty(mName) && baseName.StartsWith("strata-", StringComparison.OrdinalIgnoreCase))
                                mName = baseName;

                            if (!string.IsNullOrEmpty(mName))
                            {
                                ModelInfo info = ResolveModelInfo(mName, GetJsonStr(j, "family"));
                                if (packOk || readyPacks.Contains(info.Model) ||
                                    File.Exists(Path.Combine(launchDir, baseName + ".engine.log")) ||
                                    PathExists(GetJsonStr(j, "model_path")))
                                {
                                    AddReadyInfo(info);
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        private void ReadInitialThinking()
        {
            string found = "";
            string cfgPath = ResolveOmpFile("config");
            if (File.Exists(cfgPath))
            {
                try
                {
                    List<string> lines = new List<string>(Regex.Split(File.ReadAllText(cfgPath, Encoding.UTF8), @"\r?\n"));
                    int mrIdx = -1;
                    for (int i = 0; i < lines.Count; i++)
                    {
                        if (Regex.IsMatch(lines[i], @"^modelRoles:\s*(?:#.*)?$")) { mrIdx = i; break; }
                    }
                    if (mrIdx >= 0)
                    {
                        int mrEnd = FindBlockEnd(lines, mrIdx, 0);
                        for (int i = mrIdx + 1; i < mrEnd; i++)
                        {
                            Match dm = Regex.Match(lines[i], @"^[ \t]+default:\s*([""']?)[^\s#:""']+/[^\s#:""']+:(off|minimal|low|medium|high|xhigh|max|auto)\1\s*(?:#.*)?$", RegexOptions.IgnoreCase);
                            if (dm.Success)
                            {
                                found = dm.Groups[2].Value.ToLowerInvariant();
                                break;
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(found))
                    {
                        foreach (string ln in lines)
                        {
                            Match mDtl = Regex.Match(ln, @"^defaultThinkingLevel:\s*([a-zA-Z0-9_-]+)");
                            if (mDtl.Success)
                            {
                                found = mDtl.Groups[1].Value.ToLowerInvariant();
                                break;
                            }
                        }
                    }
                }
                catch { }
            }

            if (string.IsNullOrEmpty(found))
            {
                string modPath = ResolveOmpFile("models");
                if (File.Exists(modPath))
                {
                    try
                    {
                        Match mDef = Regex.Match(
                            File.ReadAllText(modPath, Encoding.UTF8),
                            @"^[ \t]+strata:\s*(?:#.*)?\r?\n(?:[ \t]*(?:#[^\r\n]*)?\r?\n|[ \t]{2,}[^\r\n]*\r?\n)*?[ \t]+defaultLevel:\s*([a-zA-Z0-9_-]+)",
                            RegexOptions.Multiline | RegexOptions.IgnoreCase);
                        if (mDef.Success) found = mDef.Groups[1].Value.ToLowerInvariant();
                    }
                    catch { }
                }
            }

            if (!string.IsNullOrEmpty(found)) initialThinking = found;
        }

        private void BuildUi()
        {
            Text = "Strata Engine Controller";
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

            txtPreset = new TextBox
            {
                Dock = DockStyle.Fill,
                BackColor = BgInput,
                ForeColor = FgText,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(3, 2, 3, 2)
            };
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

            chkOmpContinue = new CheckBox
            {
                Text = "Continue last session (-c)",
                Checked = false,
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                ForeColor = FgText,
                BackColor = BgDark,
                Margin = new Padding(6, 2, 3, 2)
            };
            inputRow2.Controls.Add(chkOmpContinue, 3, 1);
            inputRow2.ResumeLayout(false);

            TableLayoutPanel btnGrid = NewGrid(50, new[] { 14f, 16f, 13f, 14f, 13f, 14f, 16f }, new[] { 100f }, BgDark, new Padding(12, 2, 12, 8));
            btnStart   = AddBtn(btnGrid, "Start Engine",    0, Color.FromArgb(35, 134, 54),  fontBold);
            btnStop    = AddBtn(btnGrid, "Stop (Graceful)", 1, Color.FromArgb(180, 105, 14), fontBold);
            btnKill    = AddBtn(btnGrid, "Force Kill",      2, Color.FromArgb(190, 40, 40),  fontBold);
            btnTune    = AddBtn(btnGrid, "Tune Model",      3, Color.FromArgb(115, 75, 180), fontBold);
            btnWireOmp = AddBtn(btnGrid, "Sync omp",        4, Color.FromArgb(45, 105, 190), fontUI);
            btnRunOmp  = AddBtn(btnGrid, "Launch omp",      5, BgPanel,                      fontUI);
            btnOpenLog = AddBtn(btnGrid, "Open Debug Log",  6, BgPanel,                      fontUI);
            btnGrid.ResumeLayout(false);

            Panel logPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 4, 12, 12) };
            Controls.Add(logPanel);
            logPanel.BringToFront();

            txtLog = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                MaxLength = int.MaxValue,
                Font = fontMono,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(16, 16, 20),
                ForeColor = Color.FromArgb(200, 215, 225)
            };
            logPanel.Controls.Add(txtLog);

            toolTip.SetToolTip(cmbPleIo, "direct: keeps 26.8 GiB PLE table on NVMe SSD (prevents startup/tuning RAM spikes via STRATA_PLE_AUTO_RAM=0).\nauto: lets Strata lock PLE in RAM if free RAM allows.\nram: forces PLE lock in RAM.");
            toolTip.SetToolTip(cmbVramReserve, "MiB kept free on GPU (--vram-reserve-mib). Recommended 1280-1536 on 12 GB Windows cards to avoid WDDM paging (#2065).");
            toolTip.SetToolTip(cmbThinking, "Updates modelRoles.default :level suffix and defaultThinkingLevel in ~/.omp/agent/config.yml without overwriting other keys.");
            toolTip.SetToolTip(chkOmpContinue, "When unchecked (recommended), Launch omp starts a fresh session using --thinking. When checked, adds -c to resume the previous session.");

            ResumeLayout(true);
            SyncModelSelection();
        }

        private TableLayoutPanel NewGrid(int height, float[] cols, float[] rows, Color bg, Padding pad)
        {
            TableLayoutPanel g = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = height,
                ColumnCount = cols.Length,
                RowCount = rows.Length,
                BackColor = bg,
                Padding = pad
            };
            g.SuspendLayout();
            foreach (float w in cols)
                g.ColumnStyles.Add(new ColumnStyle(w <= 0f ? SizeType.AutoSize : SizeType.Percent, Math.Max(w, 0f)));
            foreach (float h in rows)
                g.RowStyles.Add(new RowStyle(SizeType.Percent, h));
            Controls.Add(g);
            g.BringToFront();
            return g;
        }

        private static Label AddLbl(TableLayoutPanel g, string text, int col, int row, Font font, Color color, bool autoSize)
        {
            Label l = new Label
            {
                Text = text,
                Font = font,
                ForeColor = color,
                AutoSize = autoSize,
                AutoEllipsis = !autoSize,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(3, 0, 10, 0)
            };
            g.Controls.Add(l, col, row);
            return l;
        }

        private static ComboBox AddCmb(TableLayoutPanel g, int col, string[] items, string defaultItem, ComboBoxStyle style)
        {
            ComboBox c = new ComboBox
            {
                Dock = DockStyle.Fill,
                BackColor = BgInput,
                ForeColor = FgText,
                FlatStyle = FlatStyle.Flat,
                DropDownStyle = style,
                DropDownWidth = 320,
                Margin = new Padding(3, 2, 3, 2)
            };
            if (items != null && items.Length > 0) c.Items.AddRange(items);
            if (!string.IsNullOrEmpty(defaultItem) && c.Items.Contains(defaultItem)) c.SelectedItem = defaultItem;
            else if (c.Items.Count > 0) c.SelectedIndex = 0;
            g.Controls.Add(c, col, 1);
            return c;
        }

        private static Button AddBtn(TableLayoutPanel g, string text, int col, Color bg, Font font)
        {
            Button b = new Button
            {
                Text = text,
                Font = font,
                Dock = DockStyle.Fill,
                BackColor = bg,
                ForeColor = FgText,
                FlatStyle = FlatStyle.Flat,
                AutoEllipsis = true,
                Margin = new Padding(3, 4, 3, 4)
            };
            b.FlatAppearance.BorderSize = 0;
            g.Controls.Add(b, col, 0);
            return b;
        }

        private ModelInfo GetSelectedModelInfo()
        {
            object item = cmbModel.SelectedItem;
            ModelInfo info;
            return (item != null && modelMap.TryGetValue(item.ToString(), out info)) ? info : null;
        }

        private void UpdateThinkingOptions(string family, string preferred)
        {
            ThinkingProfile prof = GetThinkingProfile(family);
            string cur = !string.IsNullOrEmpty(preferred)
                ? preferred
                : (cmbThinking.SelectedItem != null ? cmbThinking.SelectedItem.ToString() : initialThinking);
            string pick = ResolveThinkingChoice(cur, prof);

            suppressThinkingSync = true;
            try
            {
                if (lastThinkingKey != prof.Key || cmbThinking.Items.Count != prof.UiLevels.Length)
                {
                    cmbThinking.BeginUpdate();
                    try
                    {
                        cmbThinking.Items.Clear();
                        cmbThinking.Items.AddRange(prof.UiLevels);
                        lastThinkingKey = prof.Key;
                    }
                    finally { cmbThinking.EndUpdate(); }
                }

                string selected = cmbThinking.SelectedItem != null ? cmbThinking.SelectedItem.ToString() : "";
                if (selected != pick) cmbThinking.SelectedItem = pick;
            }
            finally { suppressThinkingSync = false; }
        }

        private void SyncModelSelection()
        {
            ModelInfo sel = GetSelectedModelInfo();
            string c = cmbCtx.Text.Trim().ToLowerInvariant().Replace("k", "");
            if (sel != null)
            {
                cmbSpeed.Enabled = (sel.Family == "deepseek41");
                txtPreset.Text = string.Equals(sel.Model, "ist-das-coder", StringComparison.OrdinalIgnoreCase)
                    ? "ds41-coder-" + c
                    : string.Format("{0}-{1}-{2}", sel.Family, Regex.Replace(sel.Model, @"[^a-zA-Z0-9\-]", "-"), c);
                UpdateThinkingOptions(sel.Family, null);
            }
            else
            {
                cmbSpeed.Enabled = false;
                UpdateThinkingOptions("qwen", null);
            }
        }

        private bool TryBuildEngineOptionArgs(out string optArgs, out string errorMsg)
        {
            StringBuilder sb = new StringBuilder();
            errorMsg = null;

            string pleMode = cmbPleIo.SelectedItem != null ? cmbPleIo.SelectedItem.ToString() : "direct";
            if (string.Equals(pleMode, "direct", StringComparison.OrdinalIgnoreCase))
                sb.Append(" --set STRATA_PLE_AUTO_RAM=0 --set ple-io=direct");
            else if (string.Equals(pleMode, "ram", StringComparison.OrdinalIgnoreCase))
                sb.Append(" --set ple-io=ram");

            string vramRaw = (cmbVramReserve.Text ?? "").Trim();
            if (!string.IsNullOrEmpty(vramRaw) && !string.Equals(vramRaw, "auto", StringComparison.OrdinalIgnoreCase))
            {
                int vramMib;
                if (!int.TryParse(vramRaw, out vramMib) || vramMib < 0)
                {
                    optArgs = "";
                    errorMsg = "VRAM Reserve must be 'auto' or a non-negative integer (MiB).";
                    return false;
                }
                sb.Append(" --set vram-reserve-mib=").Append(vramMib);
            }

            optArgs = sb.ToString();
            return true;
        }

        private void WireEvents()
        {
            cmbModel.SelectedIndexChanged += delegate { SyncModelSelection(); };
            cmbCtx.SelectedIndexChanged   += delegate { SyncModelSelection(); };
            cmbCtx.TextChanged            += delegate { SyncModelSelection(); };

            cmbThinking.SelectedIndexChanged += delegate
            {
                if (!suppressThinkingSync)
                    SyncOmpConfigFiles(GetSelectedModelInfo(), cmbThinking.SelectedItem != null ? cmbThinking.SelectedItem.ToString() : "", null);
            };

            btnStart.Click += delegate { InvokeStrataLaunch("", "Starting", false); };

            btnTune.Click += delegate
            {
                ModelInfo sel = GetSelectedModelInfo();
                string preset = txtPreset.Text.Trim();
                if (sel == null || string.IsNullOrEmpty(preset))
                {
                    MessageBox.Show(this, "Select a downloaded ready model and preset name first.", "Missing Input");
                    return;
                }
                string pleMode = cmbPleIo.SelectedItem != null ? cmbPleIo.SelectedItem.ToString() : "auto";
                string vramRaw = (cmbVramReserve.Text ?? "").Trim();
                DialogResult ans = MessageBox.Show(
                    this,
                    string.Format("Tune [{0}] ({1}) for this PC and save to preset '{2}'?\n(PLE I/O: {3}, VRAM Reserve: {4} MiB)\nAny running engine will be stopped first.\n\nYES = Quick tune (~20 min)\nNO = Thorough tune (up to 2 hours)\nCANCEL = Abort",
                        sel.FamilyTitle, sel.Model, preset, pleMode, vramRaw),
                    "Tune Model",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);
                if (ans == DialogResult.Cancel) return;
                string mode = (ans == DialogResult.Yes) ? "quick" : "thorough";
                InvokeStrataLaunch(" --tune " + mode + " --retune --restart --set STRATA_ARENA_RAM_WAIT_S=60", "Tuning (" + mode + ")", true);
            };

            btnStop.Click += delegate
            {
                pendingOmpSync = null;
                StopProcTree(ref actionProc);
                string p = txtPreset.Text.Trim();
                WriteGuiLog(string.Format("Stopping Strata engine gracefully (--preset \"{0}\")...", p));
                string cmd = !string.IsNullOrEmpty(p)
                    ? string.Format("call strata launch --stop --preset \"{0}\" >> \"{1}\" 2>&1 || call strata launch --stop >> \"{1}\" 2>&1", p, tempCmdLog)
                    : string.Format("call strata launch --stop >> \"{0}\" 2>&1", tempCmdLog);
                actionProc = StartInteractiveCmd(cmd, tempCmdLog, ref cmdLogPos, false);
            };

            btnKill.Click += delegate
            {
                WriteGuiLog("Force-killing strata processes...");
                bool killedAny = false;
                foreach (string name in KillProcNames)
                {
                    foreach (Process p in Process.GetProcessesByName(name))
                    {
                        killedAny = true;
                        WriteGuiLog(string.Format("Terminating {0} (PID {1})...", p.ProcessName, p.Id));
                        try { if (!p.HasExited) p.Kill(); } catch { }
                        finally { p.Dispose(); }
                    }
                }
                pendingOmpSync = null;
                lastSyncedEnginePid = -1;
                StopProcTree(ref launchProc);
                StopProcTree(ref actionProc);
                WriteGuiLog(killedAny ? "Engine terminated. VRAM and System RAM freed." : "No running strata process found.");
            };

            btnWireOmp.Click += delegate
            {
                pendingOmpSync = null;
                StopProcTree(ref actionProc);
                if (!TestPort8080())
                {
                    WriteGuiLog("WARNING: Server is not responding on http://127.0.0.1:8080; start the engine before clicking 'Sync omp'.");
                    return;
                }
                ModelInfo sel = GetSelectedModelInfo();
                string lvl = cmbThinking.SelectedItem != null ? cmbThinking.SelectedItem.ToString() : "";
                string cfgPath = ResolveOmpFile("config");
                bool hadCfg = File.Exists(cfgPath);
                string origCfg = null;
                if (hadCfg)
                {
                    try { origCfg = File.ReadAllText(cfgPath, Encoding.UTF8); } catch { origCfg = null; }
                }

                WriteGuiLog(string.Format("Syncing omp (thinking: {0}; preserving existing config.yml keys)...", lvl));
                pendingOmpSync = new PendingOmpSyncInfo
                {
                    Info = sel,
                    Level = lvl,
                    ConfigPath = cfgPath,
                    OriginalConfigText = origCfg,
                    HadOriginalConfig = hadCfg
                };
                actionProc = StartInteractiveCmd(
                    string.Format("call strata connect omp --write --yes >> \"{0}\" 2>&1", tempCmdLog),
                    tempCmdLog,
                    ref cmdLogPos,
                    false);
            };

            btnRunOmp.Click += delegate
            {
                ModelInfo sel = GetSelectedModelInfo();
                string lvl = cmbThinking.SelectedItem != null ? cmbThinking.SelectedItem.ToString() : "";
                ThinkingProfile prof = GetThinkingProfile(sel != null ? sel.Family : "qwen");
                string resolvedLvl = !string.IsNullOrEmpty(lvl) ? ResolveThinkingChoice(lvl, prof) : "";

                SyncOmpConfigFiles(sel, resolvedLvl, null);
                string dir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                using (FolderBrowserDialog dlg = new FolderBrowserDialog())
                {
                    dlg.Description = "Select workspace folder for omp (Cancel for user home):";
                    if (dlg.ShowDialog(this) == DialogResult.OK) dir = dlg.SelectedPath;
                }

                string contArg = chkOmpContinue.Checked ? " -c" : "";
                string thinkArg = !string.IsNullOrEmpty(resolvedLvl) ? " --thinking " + resolvedLvl : "";
                string ompCmd = "omp" + contArg + thinkArg;
                WriteGuiLog(string.Format("Launching '{0}' in: {1}", ompCmd, dir));
                Process.Start(new ProcessStartInfo("cmd.exe", "/k " + ompCmd) { WorkingDirectory = dir });
            };

            btnOpenLog.Click += delegate
            {
                Process.Start("notepad.exe", "\"" + Program.DebugLogPath + "\"");
            };
        }

        private void InvokeStrataLaunch(string extraArgs, string actionLabel, bool stopFirst)
        {
            ModelInfo sel = GetSelectedModelInfo();
            string ctx = cmbCtx.Text.Trim(), preset = txtPreset.Text.Trim();
            if (sel == null || string.IsNullOrEmpty(preset))
            {
                MessageBox.Show(this, "Select a downloaded ready model and preset name first.", "Missing Input");
                return;
            }

            string optArgs, optErr;
            if (!TryBuildEngineOptionArgs(out optArgs, out optErr))
            {
                MessageBox.Show(this, optErr, "Invalid Option");
                return;
            }

            StopProcTree(ref launchProc);
            lastSyncedEnginePid = -1;
            currentEngineLog = Path.Combine(launchDir, preset + ".engine.log");
            if (File.Exists(currentEngineLog))
                try { File.WriteAllText(currentEngineLog, "", Program.Utf8NoBom); } catch { }
            engineLogPos = 0;

            string spdArg = (sel.Family == "deepseek41") ? " --speed \"" + cmbSpeed.Text + "\"" : "";
            string launchArgs = string.Format(
                "--family \"{0}\" --model \"{1}\" --context \"{2}\"{3} --save-preset \"{4}\"{5}{6} --no-tui",
                sel.Family, sel.Model, ctx, spdArg, preset, optArgs, extraArgs);

            WriteGuiLog(string.Format("{0} [{1}]: strata launch {2}", actionLabel, sel.FamilyTitle, launchArgs));
            string cmdPrefix = stopFirst ? "call strata launch --stop > nul 2>&1 & " : "";
            launchProc = StartInteractiveCmd(
                string.Format("{0}call strata launch {1} > \"{2}\" 2>&1", cmdPrefix, launchArgs, tempLaunchLog),
                tempLaunchLog,
                ref launchLogPos,
                false);
        }

        private void WriteGuiLog(string msg)
        {
            txtLog.AppendText(string.Format("[{0:HH:mm:ss}] {1}\r\n", DateTime.Now, msg));
            txtLog.ScrollToCaret();
            Program.WriteDebugFile(msg);
        }

        private static int GetIndentLen(string line)
        {
            int count = 0;
            while (count < line.Length && (line[count] == ' ' || line[count] == '\t')) count++;
            return count;
        }

        private static int FindBlockEnd(List<string> lines, int startIdx, int parentIndent)
        {
            for (int k = startIdx + 1; k < lines.Count; k++)
            {
                if (string.IsNullOrWhiteSpace(lines[k]) || lines[k].TrimStart().StartsWith("#")) continue;
                if (GetIndentLen(lines[k]) <= parentIndent) return k;
            }
            return lines.Count;
        }

        private static string GetModelIdFromModelsYml(string path, string providerName)
        {
            if (!File.Exists(path)) return "";
            try
            {
                List<string> lines = new List<string>(Regex.Split(File.ReadAllText(path, Encoding.UTF8), @"\r?\n"));
                int provIdx = -1;
                for (int i = 0; i < lines.Count; i++)
                    if (Regex.IsMatch(lines[i], @"^providers:\s*(?:#.*)?$")) { provIdx = i; break; }
                if (provIdx < 0) return "";

                int targetIdx = -1;
                string targetIndent = "";
                string provRegex = @"^([ \t]+)" + Regex.Escape(providerName) + @":\s*(?:#.*)?$";
                for (int i = provIdx + 1; i < lines.Count; i++)
                {
                    string ln = lines[i];
                    if (string.IsNullOrWhiteSpace(ln) || ln.TrimStart().StartsWith("#")) continue;
                    if (GetIndentLen(ln) == 0) break;
                    Match sm = Regex.Match(ln, provRegex);
                    if (sm.Success) { targetIdx = i; targetIndent = sm.Groups[1].Value; break; }
                }
                if (targetIdx < 0) return "";

                int tEnd = FindBlockEnd(lines, targetIdx, targetIndent.Length);
                for (int i = targetIdx + 1; i < tEnd; i++)
                {
                    Match idM = Regex.Match(lines[i], @"^[ \t]+-\s+id:\s*([""']?)([^\s#""']+)\1");
                    if (idM.Success) return idM.Groups[2].Value;
                }
            }
            catch { }
            return "";
        }

        private bool PatchOmpModelsYml(string path, string providerName, ThinkingProfile prof, string level)
        {
            if (!File.Exists(path) || string.IsNullOrEmpty(providerName)) return false;
            List<string> lines = new List<string>(Regex.Split(File.ReadAllText(path, Encoding.UTF8), @"\r?\n"));
            while (lines.Count > 0 && lines[lines.Count - 1] == "") lines.RemoveAt(lines.Count - 1);

            int provIdx = -1;
            for (int i = 0; i < lines.Count; i++)
                if (Regex.IsMatch(lines[i], @"^providers:\s*(?:#.*)?$")) { provIdx = i; break; }
            if (provIdx < 0) return false;

            int strataIdx = -1;
            string strataIndent = "";
            string provRegex = @"^([ \t]+)" + Regex.Escape(providerName) + @":\s*(?:#.*)?$";
            for (int i = provIdx + 1; i < lines.Count; i++)
            {
                string ln = lines[i];
                if (string.IsNullOrWhiteSpace(ln) || ln.TrimStart().StartsWith("#")) continue;
                if (GetIndentLen(ln) == 0) break;
                Match sm = Regex.Match(ln, provRegex);
                if (sm.Success) { strataIdx = i; strataIndent = sm.Groups[1].Value; break; }
            }
            if (strataIdx < 0) return false;

            int sLen = strataIndent.Length;
            int strataEnd = FindBlockEnd(lines, strataIdx, sLen);
            string childIndent = strataIndent + "  ";
            for (int i = strataIdx + 1; i < strataEnd; i++)
            {
                Match cm = Regex.Match(lines[i], @"^([ \t]+)\S");
                if (cm.Success) { childIndent = cm.Groups[1].Value; break; }
            }
            int cLen = childIndent.Length;

            KeyValuePair<string, string>[] compatList =
            {
                new KeyValuePair<string, string>("supportsDeveloperRole", "false"),
                new KeyValuePair<string, string>("supportsReasoningEffort", "true"),
                new KeyValuePair<string, string>("supportsReasoningParams", "true"),
                new KeyValuePair<string, string>("thinkingFormat", "openai"),
                new KeyValuePair<string, string>("reasoningDisableMode", "none-effort"),
                new KeyValuePair<string, string>("disableReasoningOnToolChoice", "false")
            };
            Dictionary<string, string> compatMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> kv in compatList) compatMap[kv.Key] = kv.Value;

            int compatIdx = -1, modelsIdx = -1;
            for (int i = strataIdx + 1; i < strataEnd; i++)
            {
                string ln = lines[i];
                if (string.IsNullOrWhiteSpace(ln) || ln.TrimStart().StartsWith("#") || GetIndentLen(ln) != cLen) continue;
                if (compatIdx < 0 && Regex.IsMatch(ln, @"^[ \t]+compat:(\s*\{\s*\})?\s*(?:#.*)?$")) compatIdx = i;
                if (modelsIdx < 0 && Regex.IsMatch(ln, @"^[ \t]+models:\s*(?:#.*)?$")) modelsIdx = i;
            }

            if (compatIdx >= 0)
            {
                lines[compatIdx] = childIndent + "compat:";
                int cEnd = Math.Min(strataEnd, FindBlockEnd(lines, compatIdx, cLen));
                string kvIndent = childIndent + "  ";
                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                for (int i = compatIdx + 1; i < cEnd; i++)
                {
                    Match km = Regex.Match(lines[i], @"^([ \t]+)([a-zA-Z0-9_]+):");
                    string val;
                    if (km.Success && compatMap.TryGetValue(km.Groups[2].Value, out val))
                    {
                        kvIndent = km.Groups[1].Value;
                        lines[i] = string.Format("{0}{1}: {2}", kvIndent, km.Groups[2].Value, val);
                        seen.Add(km.Groups[2].Value);
                    }
                }

                int insAt = cEnd;
                while (insAt > compatIdx + 1 && string.IsNullOrWhiteSpace(lines[insAt - 1])) insAt--;
                foreach (KeyValuePair<string, string> kv in compatList)
                    if (!seen.Contains(kv.Key))
                        lines.Insert(insAt++, string.Format("{0}{1}: {2}", kvIndent, kv.Key, kv.Value));
            }
            else
            {
                int insAt = (modelsIdx >= 0) ? modelsIdx : (strataIdx + 1);
                string kvIndent = childIndent + "  ";
                lines.Insert(insAt++, childIndent + "compat:");
                foreach (KeyValuePair<string, string> kv in compatList)
                    lines.Insert(insAt++, string.Format("{0}{1}: {2}", kvIndent, kv.Key, kv.Value));
            }

            strataEnd = FindBlockEnd(lines, strataIdx, sLen);
            modelsIdx = -1;
            for (int i = strataIdx + 1; i < strataEnd; i++)
                if (GetIndentLen(lines[i]) == cLen && Regex.IsMatch(lines[i], @"^[ \t]+models:\s*(?:#.*)?$"))
                {
                    modelsIdx = i;
                    break;
                }

            if (modelsIdx >= 0)
            {
                int mEnd = Math.Min(strataEnd, FindBlockEnd(lines, modelsIdx, cLen));
                List<int> itemStarts = new List<int>();
                for (int i = modelsIdx + 1; i < mEnd; i++)
                    if (Regex.IsMatch(lines[i], @"^[ \t]+-\s+id:")) itemStarts.Add(i);

                for (int idx = itemStarts.Count - 1; idx >= 0; idx--)
                {
                    int itStart = itemStarts[idx];
                    int itEnd = (idx + 1 < itemStarts.Count) ? itemStarts[idx + 1] : mEnd;
                    string propIndent = Regex.Match(lines[itStart], @"^([ \t]+)-").Groups[1].Value + "  ";

                    int thinkIdx = -1, reasonIdx = -1;
                    for (int j = itStart + 1; j < itEnd; j++)
                    {
                        string ln = lines[j];
                        if (string.IsNullOrWhiteSpace(ln) || ln.TrimStart().StartsWith("#") || GetIndentLen(ln) != propIndent.Length) continue;
                        if (Regex.IsMatch(ln, @"^[ \t]+thinking:(\s*\{\s*\})?\s*(?:#.*)?$")) thinkIdx = j;
                        else if (Regex.IsMatch(ln, @"^[ \t]+reasoning:\s*")) reasonIdx = j;
                    }

                    if (reasonIdx >= 0) lines[reasonIdx] = propIndent + "reasoning: true";
                    else
                    {
                        lines.Insert(itStart + 1, propIndent + "reasoning: true");
                        if (thinkIdx >= 0) thinkIdx++;
                        itEnd++;
                    }

                    List<string> thinkLines = new List<string>
                    {
                        propIndent + "thinking:",
                        propIndent + "  mode: effort",
                        propIndent + "  efforts: [" + string.Join(", ", prof.Efforts) + "]",
                        propIndent + "  requiresEffort: false"
                    };
                    if (level != "auto" && level != "off" && Array.IndexOf(prof.Efforts, level) >= 0)
                        thinkLines.Add(propIndent + "  defaultLevel: " + level);

                    if (thinkIdx >= 0)
                    {
                        int tEnd = Math.Min(itEnd, FindBlockEnd(lines, thinkIdx, propIndent.Length));
                        lines.RemoveRange(thinkIdx, tEnd - thinkIdx);
                        lines.InsertRange(thinkIdx, thinkLines);
                    }
                    else
                    {
                        int insAt = itEnd;
                        while (insAt > itStart + 1 && string.IsNullOrWhiteSpace(lines[insAt - 1])) insAt--;
                        lines.InsertRange(insAt, thinkLines);
                    }
                }
            }

            File.WriteAllText(path, string.Join("\r\n", lines) + "\r\n", Program.Utf8NoBom);
            return true;
        }

        private void SyncOmpConfigFiles(ModelInfo modelInfo, string level, string forceDefaultBaseModel)
        {
            if (string.IsNullOrEmpty(level)) return;
            ThinkingProfile prof = GetThinkingProfile(modelInfo != null ? modelInfo.Family : "qwen");
            string lvl = ResolveThinkingChoice(level, prof);
            string cfgPath = ResolveOmpFile("config"), modPath = ResolveOmpFile("models");

            try
            {
                if (!Directory.Exists(ompAgentDir)) Directory.CreateDirectory(ompAgentDir);
                string txt = File.Exists(cfgPath) ? File.ReadAllText(cfgPath, Encoding.UTF8) : "";
                List<string> lines = new List<string>(Regex.Split(txt, @"\r?\n"));
                while (lines.Count > 0 && lines[lines.Count - 1] == "") lines.RemoveAt(lines.Count - 1);

                string activeProvider = "strata";
                int mrIdx = -1;
                for (int i = 0; i < lines.Count; i++)
                    if (Regex.IsMatch(lines[i], @"^modelRoles:(\s*\{\s*\})?\s*(?:#.*)?$")) { mrIdx = i; break; }

                string fallbackTarget = (!string.IsNullOrEmpty(forceDefaultBaseModel) ? forceDefaultBaseModel : "strata/strata") + ":" + lvl;
                if (mrIdx >= 0)
                {
                    if (Regex.IsMatch(lines[mrIdx], @"^modelRoles:\s*\{\s*\}"))
                        lines[mrIdx] = "modelRoles:";

                    int mrEnd = FindBlockEnd(lines, mrIdx, 0);
                    string roleIndent = "  ";
                    int defaultLineIdx = -1;
                    for (int i = mrIdx + 1; i < mrEnd; i++)
                    {
                        string ln = lines[i];
                        if (string.IsNullOrWhiteSpace(ln) || ln.TrimStart().StartsWith("#")) continue;
                        Match indentM = Regex.Match(ln, @"^([ \t]+)\S");
                        if (indentM.Success && roleIndent == "  ") roleIndent = indentM.Groups[1].Value;
                        if (Regex.IsMatch(ln, @"^[ \t]+default:\s*")) { defaultLineIdx = i; break; }
                    }

                    if (defaultLineIdx >= 0)
                    {
                        Match rm = Regex.Match(lines[defaultLineIdx], @"^([ \t]+default:\s*)([""']?)([^\s#""']+)([""']?)(.*)$");
                        if (rm.Success)
                        {
                            string existingSelector = rm.Groups[3].Value;
                            string existingBase = Regex.Replace(existingSelector, @":(off|minimal|low|medium|high|xhigh|max|auto|inherit)$", "", RegexOptions.IgnoreCase);
                            string newBase = !string.IsNullOrEmpty(forceDefaultBaseModel) ? forceDefaultBaseModel : existingBase;
                            int slashIdx = newBase.IndexOf('/');
                            if (slashIdx > 0) activeProvider = newBase.Substring(0, slashIdx);
                            lines[defaultLineIdx] = rm.Groups[1].Value + rm.Groups[2].Value + newBase + ":" + lvl + rm.Groups[4].Value + rm.Groups[5].Value;
                        }
                    }
                    else
                    {
                        lines.Insert(mrIdx + 1, roleIndent + "default: " + fallbackTarget);
                    }
                }
                else
                {
                    lines.Add("modelRoles:");
                    lines.Add("  default: " + fallbackTarget);
                }

                int dtlIdx = -1;
                for (int i = 0; i < lines.Count; i++)
                    if (Regex.IsMatch(lines[i], @"^defaultThinkingLevel:\s*")) { dtlIdx = i; break; }

                string[] validDtl = { "minimal", "low", "medium", "high", "xhigh", "max", "auto" };
                if (lvl == "off")
                {
                    if (dtlIdx >= 0)
                    {
                        Match dtlM = Regex.Match(lines[dtlIdx], @"^(defaultThinkingLevel:\s*)([^\s#]*)(.*)$");
                        string curDtl = dtlM.Success ? dtlM.Groups[2].Value.ToLowerInvariant() : "";
                        if (Array.IndexOf(validDtl, curDtl) < 0)
                            lines[dtlIdx] = (dtlM.Success ? dtlM.Groups[1].Value : "defaultThinkingLevel: ") + prof.Efforts[0] + (dtlM.Success ? dtlM.Groups[3].Value : "");
                    }
                    else lines.Add("defaultThinkingLevel: " + prof.Efforts[0]);
                }
                else if (dtlIdx >= 0)
                {
                    Match dtlM = Regex.Match(lines[dtlIdx], @"^(defaultThinkingLevel:\s*)([^\s#]*)(.*)$");
                    lines[dtlIdx] = (dtlM.Success ? dtlM.Groups[1].Value : "defaultThinkingLevel: ") + lvl + (dtlM.Success ? dtlM.Groups[3].Value : "");
                }
                else lines.Add("defaultThinkingLevel: " + lvl);

                File.WriteAllText(cfgPath, string.Join("\r\n", lines) + "\r\n", Program.Utf8NoBom);

                bool patchedStrata = PatchOmpModelsYml(modPath, "strata", prof, lvl);
                bool patchedActive = !string.Equals(activeProvider, "strata", StringComparison.OrdinalIgnoreCase) &&
                                     PatchOmpModelsYml(modPath, activeProvider, prof, lvl);
                bool patchedModels = patchedStrata || patchedActive;

                string msg = string.Format("Synced OMP thinking -> {0} (role provider: {1}, efforts: [{2}])", lvl, activeProvider, string.Join(", ", prof.Efforts));
                if (!patchedModels)
                    msg += string.Format(" [NOTE: '{0}' is not in models.yml; click 'Sync omp' while serving to configure provider 'strata']", activeProvider);
                WriteGuiLog(msg);
            }
            catch (Exception ex)
            {
                WriteGuiLog("WARNING: Could not update OMP config: " + ex.Message);
            }
        }

        private static bool IsProcRunning(Process p)
        {
            if (p == null) return false;
            try { return !p.HasExited; } catch { return false; }
        }

        private static void StopProcTree(ref Process p)
        {
            if (p == null) return;
            try { if (!p.HasExited) Program.KillPidTree(p.Id); } catch { }
            try { p.Dispose(); } catch { }
            p = null;
        }

        private static Process StartInteractiveCmd(string cmdChain, string logFile, ref long posRef, bool append)
        {
            if (!append)
            {
                try { File.WriteAllText(logFile, "", Program.Utf8NoBom); } catch { }
                posRef = 0;
            }
            return Process.Start(new ProcessStartInfo("cmd.exe", "/c " + cmdChain)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true
            });
        }

        private void SyncLogTail(string path, ref long lastPos, string tag, Process proc)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (fs.Length < lastPos) lastPos = 0;
                    if (fs.Length <= lastPos) return;
                    fs.Seek(lastPos, SeekOrigin.Begin);
                    using (StreamReader sr = new StreamReader(fs, Encoding.UTF8))
                    {
                        string delta = sr.ReadToEnd();
                        lastPos = fs.Position;
                        if (string.IsNullOrEmpty(delta)) return;

                        txtLog.AppendText(delta.IndexOf('\n') >= 0 ? Regex.Replace(delta, @"(?<!\r)\n", "\r\n") : delta);
                        txtLog.ScrollToCaret();
                        Program.WriteDebugFile(string.Format("[{0}] {1}", tag, delta.Trim()));

                        if (!isPrompting && IsProcRunning(proc) && Regex.IsMatch(delta, @"(\[y/N\]|\[Y/n\]|\(y/n\))\s*$"))
                        {
                            isPrompting = true;
                            try
                            {
                                string[] lines = delta.Trim().Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                                int take = Math.Min(3, lines.Length);
                                string q = string.Join("\n", lines, lines.Length - take, take);
                                DialogResult ans = MessageBox.Show(
                                    this,
                                    q + "\n\nClick YES to send 'y' or NO to send 'n'.",
                                    "Confirmation Required",
                                    MessageBoxButtons.YesNo,
                                    MessageBoxIcon.Question);
                                string rep = (ans == DialogResult.Yes) ? "y" : "n";
                                txtLog.AppendText(rep + "\r\n");
                                proc.StandardInput.WriteLine(rep);
                                proc.StandardInput.Flush();
                            }
                            finally { isPrompting = false; }
                        }
                    }
                }
            }
            catch { }
        }

        private static Process GetEngineProc()
        {
            Process best = null;
            long bestWs = -1L;
            foreach (string name in EngineProcNames)
            {
                foreach (Process p in Process.GetProcessesByName(name))
                {
                    try
                    {
                        long ws = p.WorkingSet64;
                        bool isMatch = !string.Equals(p.ProcessName, "strata", StringComparison.OrdinalIgnoreCase) || (ws > 250L * 1024L * 1024L);
                        if (!isMatch)
                            try { isMatch = Regex.IsMatch(p.MainModule.FileName, "engine", RegexOptions.IgnoreCase); } catch { }

                        if (isMatch && ws > bestWs)
                        {
                            if (best != null) best.Dispose();
                            best = p;
                            bestWs = ws;
                            continue;
                        }
                    }
                    catch { }
                    p.Dispose();
                }
            }
            return best;
        }

        private bool TestPort8080()
        {
            try
            {
                bool listening = false;
                foreach (IPEndPoint ep in ipProps.GetActiveTcpListeners())
                    if (ep.Port == 8080) { listening = true; break; }
                if (!listening) return false;
            }
            catch { }

            using (TcpClient tcp = new TcpClient())
            {
                System.Threading.WaitHandle wh = null;
                try
                {
                    IAsyncResult iar = tcp.BeginConnect("127.0.0.1", 8080, null, null);
                    wh = iar.AsyncWaitHandle;
                    if (wh.WaitOne(100, false) && tcp.Connected)
                    {
                        try { tcp.EndConnect(iar); } catch { }
                        return true;
                    }
                    return false;
                }
                catch { return false; }
                finally
                {
                    tcp.Close();
                    if (wh != null) try { wh.Close(); } catch { }
                }
            }
        }

        private string GetLoadedStrataModelId()
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:8080/v1/models");
                req.Method = "GET";
                req.Timeout = 500;
                req.ReadWriteTimeout = 500;
                req.Proxy = null;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    Dictionary<string, object> root = AsDict(json.DeserializeObject(sr.ReadToEnd()));
                    object dataObj;
                    if (root != null && root.TryGetValue("data", out dataObj) && dataObj is object[])
                    {
                        object[] data = (object[])dataObj;
                        if (data.Length > 0) return GetJsonStr(AsDict(data[0]), "id");
                    }
                }
            }
            catch { }
            return "";
        }

        private string FindModelMapKey(string loadedId)
        {
            if (string.IsNullOrEmpty(loadedId) || loadedId == "strata") return "";
            ModelInfo info = ResolveModelInfo(loadedId, "");
            ModelInfo existing;
            if (modelMap.TryGetValue(info.Display, out existing)) return existing.Display;
            foreach (string k in modelKeys)
            {
                ModelInfo v = modelMap[k];
                if (v.Family == info.Family &&
                    (string.Equals(v.Model, info.Model, StringComparison.OrdinalIgnoreCase) ||
                     loadedId.IndexOf(v.Model, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return k;
                }
            }
            return "";
        }

        private void OnPollTimerTick(object sender, EventArgs e)
        {
            SyncLogTail(tempLaunchLog, ref launchLogPos, "LAUNCH_OUT", launchProc);
            SyncLogTail(tempCmdLog,    ref cmdLogPos,    "CMD_OUT",    actionProc);
            if (!string.IsNullOrEmpty(currentEngineLog))
                SyncLogTail(currentEngineLog, ref engineLogPos, "ENGINE_LOG", null);

            if (pendingOmpSync != null && actionProc != null && !IsProcRunning(actionProc))
            {
                PendingOmpSyncInfo pending = pendingOmpSync;
                pendingOmpSync = null;
                bool exitOk = false;
                try { exitOk = (actionProc.ExitCode == 0); } catch { }
                StopProcTree(ref actionProc);

                if (pending.HadOriginalConfig && pending.OriginalConfigText != null && !string.IsNullOrEmpty(pending.ConfigPath))
                {
                    try { File.WriteAllText(pending.ConfigPath, pending.OriginalConfigText, Program.Utf8NoBom); } catch { }
                }

                if (exitOk)
                {
                    ModelInfo syncInfo = pending.Info;
                    string loadedId = TestPort8080() ? GetLoadedStrataModelId() : "";
                    if (!string.IsNullOrEmpty(loadedId) && loadedId != "strata")
                        syncInfo = ResolveModelInfo(loadedId, "");

                    string modYmlId = GetModelIdFromModelsYml(ResolveOmpFile("models"), "strata");
                    string targetId = !string.IsNullOrEmpty(modYmlId) ? modYmlId : (!string.IsNullOrEmpty(loadedId) ? loadedId : "strata");

                    ThinkingProfile prof = GetThinkingProfile(syncInfo != null ? syncInfo.Family : "qwen");
                    SyncOmpConfigFiles(syncInfo, ResolveThinkingChoice(pending.Level, prof), "strata/" + targetId);
                    actionProc = StartInteractiveCmd(
                        string.Format("call strata connect omp --check >> \"{0}\" 2>&1", tempCmdLog),
                        tempCmdLog,
                        ref cmdLogPos,
                        true);
                }
                else
                {
                    WriteGuiLog("WARNING: 'strata connect omp --write --yes' did not complete; original config.yml preserved.");
                }
            }

            string statusTxt, procTxt;
            Color statusClr;

            using (Process eng = GetEngineProc())
            {
                if (eng != null && TestPort8080())
                {
                    if (lastSyncedEnginePid != eng.Id)
                    {
                        lastSyncedEnginePid = eng.Id;
                        string loadedId = GetLoadedStrataModelId();
                        if (!string.IsNullOrEmpty(loadedId))
                        {
                            string matchedKey = FindModelMapKey(loadedId);
                            string curSelected = cmbModel.SelectedItem != null ? cmbModel.SelectedItem.ToString() : "";
                            if (!string.IsNullOrEmpty(matchedKey) && curSelected != matchedKey)
                                cmbModel.SelectedItem = matchedKey;
                            else if (loadedId != "strata")
                                UpdateThinkingOptions(ResolveModelInfo(loadedId, "").Family, null);
                        }
                    }

                    statusTxt = "[SERVING - READY]";
                    statusClr = ColorServing;
                    procTxt = string.Format(
                        "{0}.exe (PID {1}) | RAM: {2:0.0} GB | http://127.0.0.1:8080/v1",
                        eng.ProcessName, eng.Id, eng.WorkingSet64 / (1024.0 * 1024.0 * 1024.0));
                }
                else if (eng != null || IsProcRunning(launchProc))
                {
                    string info = (eng != null)
                        ? string.Format("PID {0} | RAM: {1:0.0} GB", eng.Id, eng.WorkingSet64 / (1024.0 * 1024.0 * 1024.0))
                        : "Initializing...";
                    statusTxt = "[LOADING MODEL...]";
                    statusClr = ColorLoading;
                    procTxt = "Starting engine (" + info + ")";
                }
                else
                {
                    lastSyncedEnginePid = -1;
                    statusTxt = "[STOPPED]";
                    statusClr = ColorStopped;
                    procTxt = "No engine process running";
                }
            }

            if (lastStatusText != statusTxt)
            {
                lastStatusText = statusTxt;
                lblStatus.Text = statusTxt;
                lblStatus.ForeColor = statusClr;
            }
            if (lastProcInfo != procTxt)
            {
                lastProcInfo = procTxt;
                lblProcInfo.Text = procTxt;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            using (Process eng = GetEngineProc())
            {
                if (eng != null)
                {
                    DialogResult ans = MessageBox.Show(
                        this,
                        "Strata Engine is running.\n\nYES = Kill engine before exiting\nNO = Leave running in background\nCANCEL = Stay in controller",
                        "Strata Engine Running",
                        MessageBoxButtons.YesNoCancel,
                        MessageBoxIcon.Question);
                    if (ans == DialogResult.Cancel) { e.Cancel = true; return; }
                    if (ans == DialogResult.Yes)
                    {
                        try { if (!eng.HasExited) eng.Kill(); } catch { }
                        StopProcTree(ref launchProc);
                    }
                }
            }
            pendingOmpSync = null;
            StopProcTree(ref actionProc);
            if (pollTimer != null) pollTimer.Stop();
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (pollTimer != null) { pollTimer.Stop(); pollTimer.Dispose(); pollTimer = null; }
                if (launchProc != null) { try { launchProc.Dispose(); } catch { } launchProc = null; }
                if (actionProc != null) { try { actionProc.Dispose(); } catch { } actionProc = null; }
                if (toolTip != null) { toolTip.Dispose(); toolTip = null; }
                fontUI.Dispose();
                fontBold.Dispose();
                fontStatus.Dispose();
                fontMono.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}