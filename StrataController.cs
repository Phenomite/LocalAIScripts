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
        public string Family;
        public string FamilyTitle;
        public string Model;
        public string Display;
    }

    internal sealed class ThinkingProfile
    {
        public string Key;
        public string[] UiLevels;
        public string[] Efforts;
        public string DefaultLevel;
    }

    internal sealed class PendingOmpSyncInfo
    {
        public ModelInfo Info;
        public string Level;
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
            string tempDir = Path.GetTempPath();
            DebugLogPath = !string.IsNullOrEmpty(bootLog)
                ? bootLog
                : Path.Combine(tempDir, "StrataController.debug.log");

            try { SetProcessDPIAware(); } catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
            {
                ReportFatal("Unhandled UI Exception", e.Exception);
            };

            try
            {
                CleanupOrphanProcesses();
                File.WriteAllText(
                    DebugLogPath,
                    string.Format("[{0}] Bootstrap starting\r\n", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")),
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
                    string.Format("[{0}] {1}\r\n", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), msg),
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
                            object pidObj = mo["ProcessId"];
                            object cmdObj = mo["CommandLine"];
                            string cmdLine = cmdObj != null ? cmdObj.ToString() : "";
                            if (pidObj != null && Regex.IsMatch(cmdLine, @"strata_gui_|strata connect", RegexOptions.IgnoreCase))
                            {
                                KillPidTree(Convert.ToInt32(pidObj));
                            }
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
                ProcessStartInfo psi = new ProcessStartInfo("taskkill.exe", "/F /T /PID " + pid);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
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
        private static readonly string[] EngineProcNames = new string[] { "strata-dsv41", "strata-server", "strata" };
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

        private readonly string strataLocalDir;
        private readonly string launchDir;
        private readonly string installDir;
        private readonly string ompAgentDir;
        private readonly string tempLaunchLog;
        private readonly string tempCmdLog;

        private readonly Dictionary<string, ModelInfo> modelMap = new Dictionary<string, ModelInfo>(StringComparer.Ordinal);
        private readonly List<string> modelKeys = new List<string>();
        private string initialThinking = "medium";

        private Label lblStatus;
        private Label lblProcInfo;
        private Label lblModel;
        private ComboBox cmbModel;
        private ComboBox cmbCtx;
        private ComboBox cmbSpeed;
        private ComboBox cmbThinking;
        private TextBox txtPreset;
        private Button btnStart;
        private Button btnStop;
        private Button btnKill;
        private Button btnTune;
        private Button btnWireOmp;
        private Button btnRunOmp;
        private Button btnOpenLog;
        private TextBox txtLog;
        private Timer pollTimer;

        private bool suppressThinkingSync = false;
        private string lastThinkingKey = "";
        private long engineLogPos = 0;
        private long launchLogPos = 0;
        private long cmdLogPos = 0;
        private string currentEngineLog = "";
        private Process launchProc = null;
        private Process actionProc = null;
        private bool isPrompting = false;
        private PendingOmpSyncInfo pendingOmpSync = null;
        private int lastSyncedEnginePid = -1;
        private string lastStatusText = "";
        private string lastProcInfo = "";

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
                {
                    try { File.Delete(f); } catch { }
                }
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
                        {
                            try { File.WriteAllText(f, "", Program.Utf8NoBom); } catch { }
                        }
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

            pollTimer = new Timer();
            pollTimer.Interval = 400;
            pollTimer.Tick += OnPollTimerTick;
            pollTimer.Start();
        }

        private static void SetupEnvironmentPath(string userProfile, string localAppData, string appData)
        {
            HashSet<string> pathSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> pathList = new List<string>();
            List<string> raw = new List<string>
            {
                Path.Combine(userProfile, @".local\bin"),
                Path.Combine(localAppData, @"Strata\bin"),
                Path.Combine(localAppData, "Strata"),
                Path.Combine(localAppData, @"Programs\Strata"),
                Path.Combine(userProfile, @".omp\bin"),
                Path.Combine(appData, "npm")
            };

            AddSplitPaths(raw, Environment.GetEnvironmentVariable("PATH"));
            AddSplitPaths(raw, Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User));
            AddSplitPaths(raw, Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine));

            foreach (string p in raw)
            {
                if (string.IsNullOrEmpty(p)) continue;
                string expanded = Environment.ExpandEnvironmentVariables(p.Trim());
                if (!string.IsNullOrEmpty(expanded) && pathSet.Add(expanded))
                {
                    pathList.Add(expanded);
                }
            }

            Environment.SetEnvironmentVariable("PATH", string.Join(";", pathList), EnvironmentVariableTarget.Process);
        }

        private static void AddSplitPaths(List<string> dst, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            foreach (string part in value.Split(';'))
            {
                dst.Add(part);
            }
        }

        private static string FindCommandOnPath(string command)
        {
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            string pathExt = Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD";
            string[] exts = pathExt.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string dir in pathEnv.Split(';'))
            {
                if (string.IsNullOrEmpty(dir)) continue;
                string trimmed = dir.Trim().Trim('"');
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
            foreach (string ext in new string[] { "yml", "yaml" })
            {
                string candidate = Path.Combine(ompAgentDir, baseName + "." + ext);
                if (File.Exists(candidate)) return candidate;
            }
            return Path.Combine(ompAgentDir, baseName + ".yml");
        }

        private static bool PathExists(string path)
        {
            return !string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path));
        }

        private static ModelInfo ResolveModelInfo(string rawModel, string rawFamily)
        {
            string m = (rawModel ?? "").Trim();
            m = Regex.Replace(m, @"^strata-", "", RegexOptions.IgnoreCase);
            m = Regex.Replace(m, @"\.json$", "", RegexOptions.IgnoreCase);
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
                    if (string.IsNullOrEmpty(f)) f = match.Groups[1].Value.ToLowerInvariant();
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

            ModelInfo info = new ModelInfo();
            info.Family = f;
            info.FamilyTitle = fTitle;
            info.Model = m;
            info.Display = string.Format("[{0}]  {1}", fTitle, m);
            return info;
        }

        private static ThinkingProfile GetThinkingProfile(string family)
        {
            ThinkingProfile p = new ThinkingProfile();
            switch (family)
            {
                case "deepseek41":
                    p.Key = "deepseek41";
                    p.UiLevels = new string[] { "auto", "off", "low", "medium", "high", "max" };
                    p.Efforts = new string[] { "low", "medium", "high", "max" };
                    p.DefaultLevel = "high";
                    break;
                case "qwen36":
                    p.Key = "qwen36";
                    p.UiLevels = new string[] { "auto", "off", "low", "medium", "high" };
                    p.Efforts = new string[] { "low", "medium", "high" };
                    p.DefaultLevel = "medium";
                    break;
                default:
                    p.Key = "qwen38";
                    p.UiLevels = new string[] { "auto", "off", "low", "medium", "xhigh" };
                    p.Efforts = new string[] { "low", "medium", "xhigh" };
                    p.DefaultLevel = "medium";
                    break;
            }
            return p;
        }

        private static string ResolveThinkingChoice(string choice, ThinkingProfile prof)
        {
            string c = (choice ?? "").Trim().ToLowerInvariant();
            if (Array.IndexOf(prof.UiLevels, c) >= 0) return c;

            string mapped;
            switch (c)
            {
                case "minimal":
                    mapped = "off";
                    break;
                case "high":
                    mapped = (Array.IndexOf(prof.UiLevels, "xhigh") >= 0) ? "xhigh" : "high";
                    break;
                case "xhigh":
                    mapped = (Array.IndexOf(prof.UiLevels, "high") >= 0) ? "high" : "xhigh";
                    break;
                case "max":
                    if (Array.IndexOf(prof.UiLevels, "xhigh") >= 0) mapped = "xhigh";
                    else if (Array.IndexOf(prof.UiLevels, "high") >= 0) mapped = "high";
                    else mapped = prof.DefaultLevel;
                    break;
                default:
                    mapped = prof.DefaultLevel;
                    break;
            }

            return (Array.IndexOf(prof.UiLevels, mapped) >= 0) ? mapped : prof.DefaultLevel;
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
            if (!string.IsNullOrEmpty(mod))
            {
                AddReadyInfo(ResolveModelInfo(mod, fam));
            }
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
            return (dict != null && dict.TryGetValue(key, out v) && v is bool) ? (bool)v : false;
        }

        private void DiscoverReadyModels(string userProfile)
        {
            // 1. Query strata CLI installed models inventory (component_status == 'present')
            if (!string.IsNullOrEmpty(FindCommandOnPath("strata")))
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c strata models list --installed --output json 2>nul");
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.RedirectStandardOutput = true;
                    psi.StandardOutputEncoding = Encoding.UTF8;
                    using (Process p = Process.Start(psi))
                    {
                        if (p != null)
                        {
                            string cliJson = p.StandardOutput.ReadToEnd();
                            p.WaitForExit(8000);
                            int brace = cliJson.IndexOf('{');
                            int bracket = cliJson.IndexOf('[');
                            int start = (brace >= 0 && (bracket < 0 || brace < bracket)) ? brace : bracket;
                            if (start >= 0)
                            {
                                object parsed = json.DeserializeObject(cliJson.Substring(start));
                                object[] items = null;
                                Dictionary<string, object> root = AsDict(parsed);
                                if (root != null)
                                {
                                    object listObj;
                                    if (root.TryGetValue("models", out listObj)) items = listObj as object[];
                                    else if (root.TryGetValue("items", out listObj)) items = listObj as object[];
                                    else items = new object[] { root };
                                }
                                else
                                {
                                    items = parsed as object[];
                                }

                                if (items != null)
                                {
                                    foreach (object itemObj in items)
                                    {
                                        Dictionary<string, object> it = AsDict(itemObj);
                                        if (it == null) continue;
                                        if (GetJsonStr(it, "component_status") == "present" || GetJsonBool(it, "ready"))
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

            // 2. Check prepared pack folders on disk across known Strata data roots
            HashSet<string> rootSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> dataRoots = new List<string>();
            string[] defaultRoots = new string[]
            {
                strataLocalDir,
                Path.Combine(strataLocalDir, "Strata-data"),
                Path.Combine(userProfile, @".strata\Strata-data"),
                Path.Combine(userProfile, "Strata"),
                Path.Combine(userProfile, "Strata-data"),
                Path.Combine(userProfile, @"Documents\Strata")
            };
            foreach (string r in defaultRoots)
            {
                if (!string.IsNullOrEmpty(r) && rootSet.Add(r)) dataRoots.Add(r);
            }

            string[] cfgFiles = new string[]
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
                    if (c != null)
                    {
                        foreach (string key in new string[] { "data_dir", "models_dir" })
                        {
                            string p = GetJsonStr(c, key);
                            if (!string.IsNullOrEmpty(p) && rootSet.Add(p)) dataRoots.Add(p);
                        }
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
                            {
                                hasFiles = en.MoveNext();
                            }
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

            // 3. Check install\strata-*.json and launch\*.json configs with verified on-disk packs/logs
            foreach (string dir in new string[] { installDir, launchDir })
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
                            if (j.TryGetValue("args", out argsObj))
                            {
                                object[] jArgs = argsObj as object[];
                                if (jArgs != null)
                                {
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
                            }

                            string mName = GetJsonStr(j, "model");
                            if (string.IsNullOrEmpty(mName) && baseName.StartsWith("strata-", StringComparison.OrdinalIgnoreCase))
                            {
                                mName = baseName;
                            }

                            if (!string.IsNullOrEmpty(mName))
                            {
                                ModelInfo info = ResolveModelInfo(mName, GetJsonStr(j, "family"));
                                string engLog = Path.Combine(launchDir, baseName + ".engine.log");
                                string modelPath = GetJsonStr(j, "model_path");
                                if (packOk || readyPacks.Contains(info.Model) || File.Exists(engLog) || PathExists(modelPath))
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
                    string cfgRaw = File.ReadAllText(cfgPath, Encoding.UTF8);
                    Match mRole = Regex.Match(
                        cfgRaw,
                        @"^modelRoles:\s*(?:#.*)?\r?\n(?:[ \t]*(?:#[^\r\n]*)?\r?\n|[ \t]+[^\r\n]*\r?\n)*?[ \t]+default:\s*strata/[^:\s#]+:(off|minimal|low|medium|high|xhigh|max|auto)\b",
                        RegexOptions.Multiline | RegexOptions.IgnoreCase);
                    if (mRole.Success)
                    {
                        found = mRole.Groups[1].Value.ToLowerInvariant();
                    }
                    else
                    {
                        Match mDtl = Regex.Match(cfgRaw, @"^\s*defaultThinkingLevel:\s*([a-zA-Z0-9_-]+)", RegexOptions.Multiline);
                        if (mDtl.Success) found = mDtl.Groups[1].Value.ToLowerInvariant();
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
                        string modRaw = File.ReadAllText(modPath, Encoding.UTF8);
                        Match mDef = Regex.Match(
                            modRaw,
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
            Size = new Size(860, 620);
            MinimumSize = new Size(780, 500);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgDark;
            ForeColor = FgText;
            Font = fontUI;
            AutoScaleMode = AutoScaleMode.Dpi;

            SuspendLayout();

            TableLayoutPanel statusGrid = NewGrid(46, new float[] { 0f, 100f }, new float[] { 100f }, BgPanel, new Padding(12, 6, 12, 6));
            lblStatus   = AddLbl(statusGrid, "[CHECKING ENGINE...]", 0, 0, fontStatus, FgText, true);
            lblProcInfo = AddLbl(statusGrid, "Endpoint: http://127.0.0.1:8080/v1", 1, 0, fontUI, FgMuted, false);
            statusGrid.ResumeLayout(false);

            TableLayoutPanel inputGrid = NewGrid(74, new float[] { 36f, 11f, 13f, 15f, 25f }, new float[] { 42f, 58f }, BgDark, new Padding(12, 6, 12, 2));
            lblModel    = AddLbl(inputGrid, "Ready Model [Family]:", 0, 0, fontBold, FgText, false);
            AddLbl(inputGrid, "Context:", 1, 0, fontUI, FgText, false);
            AddLbl(inputGrid, "Speed (DS41):", 2, 0, fontUI, FgText, false);
            AddLbl(inputGrid, "OMP Thinking:", 3, 0, fontUI, FgText, false);
            AddLbl(inputGrid, "Preset (--save-preset):", 4, 0, fontUI, FgText, false);

            cmbModel    = AddCmb(inputGrid, 0, modelKeys.ToArray(), null, ComboBoxStyle.DropDownList);
            cmbCtx      = AddCmb(inputGrid, 1, new string[] { "256k", "128k", "64k", "32k", "16k", "8k" }, "256k", ComboBoxStyle.DropDown);
            cmbSpeed    = AddCmb(inputGrid, 2, new string[] { "full", "faster" }, "full", ComboBoxStyle.DropDownList);
            cmbThinking = AddCmb(inputGrid, 3, new string[0], null, ComboBoxStyle.DropDownList);

            txtPreset = new TextBox();
            txtPreset.Dock = DockStyle.Fill;
            txtPreset.BackColor = BgInput;
            txtPreset.ForeColor = FgText;
            txtPreset.BorderStyle = BorderStyle.FixedSingle;
            txtPreset.Margin = new Padding(3, 2, 3, 2);
            inputGrid.Controls.Add(txtPreset, 4, 1);
            inputGrid.ResumeLayout(false);

            TableLayoutPanel btnGrid = NewGrid(56, new float[] { 15f, 16f, 13f, 14f, 13f, 14f, 15f }, new float[] { 100f }, BgDark, new Padding(12, 2, 12, 8));
            btnStart   = AddBtn(btnGrid, "Start Engine",    0, Color.FromArgb(35, 134, 54),  fontBold);
            btnStop    = AddBtn(btnGrid, "Stop (Graceful)", 1, Color.FromArgb(180, 105, 14), fontBold);
            btnKill    = AddBtn(btnGrid, "Force Kill",      2, Color.FromArgb(190, 40, 40),  fontBold);
            btnTune    = AddBtn(btnGrid, "Tune Model",      3, Color.FromArgb(115, 75, 180), fontBold);
            btnWireOmp = AddBtn(btnGrid, "Sync omp",        4, Color.FromArgb(45, 105, 190), fontUI);
            btnRunOmp  = AddBtn(btnGrid, "Launch omp",      5, BgPanel,                      fontUI);
            btnOpenLog = AddBtn(btnGrid, "Open Debug Log",  6, BgPanel,                      fontUI);
            btnGrid.ResumeLayout(false);

            Panel logPanel = new Panel();
            logPanel.Dock = DockStyle.Fill;
            logPanel.Padding = new Padding(16, 0, 16, 16);
            Controls.Add(logPanel);
            logPanel.BringToFront();

            txtLog = new TextBox();
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.Dock = DockStyle.Fill;
            txtLog.MaxLength = int.MaxValue;
            txtLog.Font = fontMono;
            txtLog.BorderStyle = BorderStyle.FixedSingle;
            txtLog.BackColor = Color.FromArgb(16, 16, 20);
            txtLog.ForeColor = Color.FromArgb(200, 215, 225);
            logPanel.Controls.Add(txtLog);

            ResumeLayout(true);

            SyncModelSelection();
        }

        private TableLayoutPanel NewGrid(int height, float[] cols, float[] rows, Color bg, Padding pad)
        {
            TableLayoutPanel g = new TableLayoutPanel();
            g.Dock = DockStyle.Top;
            g.Height = height;
            g.ColumnCount = cols.Length;
            g.RowCount = rows.Length;
            g.BackColor = bg;
            g.Padding = pad;
            g.SuspendLayout();
            foreach (float w in cols)
            {
                g.ColumnStyles.Add(new ColumnStyle(w <= 0f ? SizeType.AutoSize : SizeType.Percent, Math.Max(w, 0f)));
            }
            foreach (float h in rows)
            {
                g.RowStyles.Add(new RowStyle(SizeType.Percent, h));
            }
            Controls.Add(g);
            g.BringToFront();
            return g;
        }

        private static Label AddLbl(TableLayoutPanel g, string text, int col, int row, Font font, Color color, bool autoSize)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = font;
            l.ForeColor = color;
            l.AutoSize = autoSize;
            l.AutoEllipsis = !autoSize;
            l.Dock = DockStyle.Fill;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.Margin = new Padding(3, 0, 10, 0);
            g.Controls.Add(l, col, row);
            return l;
        }

        private static ComboBox AddCmb(TableLayoutPanel g, int col, string[] items, string defaultItem, ComboBoxStyle style)
        {
            ComboBox c = new ComboBox();
            c.Dock = DockStyle.Fill;
            c.BackColor = BgInput;
            c.ForeColor = FgText;
            c.FlatStyle = FlatStyle.Flat;
            c.DropDownStyle = style;
            c.DropDownWidth = 320;
            c.Margin = new Padding(3, 2, 3, 2);
            if (items != null && items.Length > 0) c.Items.AddRange(items);
            if (!string.IsNullOrEmpty(defaultItem) && c.Items.Contains(defaultItem)) c.SelectedItem = defaultItem;
            else if (c.Items.Count > 0) c.SelectedIndex = 0;
            g.Controls.Add(c, col, 1);
            return c;
        }

        private static Button AddBtn(TableLayoutPanel g, string text, int col, Color bg, Font font)
        {
            Button b = new Button();
            b.Text = text;
            b.Font = font;
            b.Dock = DockStyle.Fill;
            b.BackColor = bg;
            b.ForeColor = FgText;
            b.FlatStyle = FlatStyle.Flat;
            b.AutoEllipsis = true;
            b.Margin = new Padding(3, 4, 3, 4);
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
            bool needsRebuild = (lastThinkingKey != prof.Key) || (cmbThinking.Items.Count != prof.UiLevels.Length);

            suppressThinkingSync = true;
            try
            {
                if (needsRebuild)
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
                lblModel.Text = string.Format("Model [{0} (--family {1})]:", sel.FamilyTitle, sel.Family);
                cmbSpeed.Enabled = (sel.Family == "deepseek41");
                txtPreset.Text = (sel.Model == "ist-das-coder")
                    ? "ds41-coder-" + c
                    : string.Format("{0}-{1}-{2}", sel.Family, Regex.Replace(sel.Model, @"[^a-zA-Z0-9\-]", "-"), c);
                UpdateThinkingOptions(sel.Family, null);
            }
            else
            {
                lblModel.Text = "Ready Model [None Found]:";
                cmbSpeed.Enabled = false;
                UpdateThinkingOptions("qwen", null);
            }
        }

        private void WireEvents()
        {
            cmbModel.SelectedIndexChanged += delegate { SyncModelSelection(); };
            cmbCtx.SelectedIndexChanged   += delegate { SyncModelSelection(); };
            cmbCtx.TextChanged            += delegate { SyncModelSelection(); };

            cmbThinking.SelectedIndexChanged += delegate
            {
                if (suppressThinkingSync) return;
                string lvl = cmbThinking.SelectedItem != null ? cmbThinking.SelectedItem.ToString() : "";
                SyncOmpConfigFiles(GetSelectedModelInfo(), lvl);
            };

            btnStart.Click += delegate { InvokeStrataLaunch("", "Starting"); };

            btnTune.Click += delegate
            {
                ModelInfo sel = GetSelectedModelInfo();
                string preset = txtPreset.Text.Trim();
                if (sel == null || string.IsNullOrEmpty(preset))
                {
                    MessageBox.Show(this, "Select a downloaded ready model and preset name first.", "Missing Input");
                    return;
                }
                DialogResult ans = MessageBox.Show(
                    this,
                    string.Format("Tune [{0}] ({1}) for this PC and save to preset '{2}'?\n\nYES = Quick tune (~20 min)\nNO = Thorough tune (up to 2 hours)\nCANCEL = Abort",
                        sel.FamilyTitle, sel.Model, preset),
                    "Tune Model",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);
                if (ans == DialogResult.Cancel) return;
                string mode = (ans == DialogResult.Yes) ? "quick" : "thorough";
                InvokeStrataLaunch(" --tune " + mode + " --retune --restart", "Tuning (" + mode + ")");
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
                foreach (string name in EngineProcNames)
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
                ModelInfo sel = GetSelectedModelInfo();
                string lvl = cmbThinking.SelectedItem != null ? cmbThinking.SelectedItem.ToString() : "";
                WriteGuiLog(string.Format("Syncing omp (thinking: {0})...", lvl));
                pendingOmpSync = new PendingOmpSyncInfo { Info = sel, Level = lvl };
                actionProc = StartInteractiveCmd(
                    string.Format("call strata connect omp --write >> \"{0}\" 2>&1", tempCmdLog),
                    tempCmdLog,
                    ref cmdLogPos,
                    false);
            };

            btnRunOmp.Click += delegate
            {
                ModelInfo sel = GetSelectedModelInfo();
                string lvl = cmbThinking.SelectedItem != null ? cmbThinking.SelectedItem.ToString() : "";
                SyncOmpConfigFiles(sel, lvl);
                string dir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                using (FolderBrowserDialog dlg = new FolderBrowserDialog())
                {
                    dlg.Description = "Select workspace folder for omp (Cancel for user home):";
                    if (dlg.ShowDialog(this) == DialogResult.OK) dir = dlg.SelectedPath;
                }
                WriteGuiLog(string.Format("Launching omp (thinking: {0}) in: {1}", lvl, dir));
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/k omp");
                psi.WorkingDirectory = dir;
                Process.Start(psi);
            };

            btnOpenLog.Click += delegate
            {
                Process.Start("notepad.exe", "\"" + Program.DebugLogPath + "\"");
            };
        }

        private void InvokeStrataLaunch(string extraArgs, string actionLabel)
        {
            ModelInfo sel = GetSelectedModelInfo();
            string ctx = cmbCtx.Text.Trim();
            string preset = txtPreset.Text.Trim();
            if (sel == null || string.IsNullOrEmpty(preset))
            {
                MessageBox.Show(this, "Select a downloaded ready model and preset name first.", "Missing Input");
                return;
            }

            StopProcTree(ref launchProc);
            lastSyncedEnginePid = -1;
            currentEngineLog = Path.Combine(launchDir, preset + ".engine.log");
            if (File.Exists(currentEngineLog))
            {
                try { File.WriteAllText(currentEngineLog, "", Program.Utf8NoBom); } catch { }
            }
            engineLogPos = 0;

            string spdArg = (sel.Family == "deepseek41") ? " --speed \"" + cmbSpeed.Text + "\"" : "";
            string launchArgs = string.Format(
                "--family \"{0}\" --model \"{1}\" --context \"{2}\"{3} --save-preset \"{4}\"{5} --no-tui",
                sel.Family, sel.Model, ctx, spdArg, preset, extraArgs);

            WriteGuiLog(string.Format("{0} [{1}]: strata launch {2}", actionLabel, sel.FamilyTitle, launchArgs));
            launchProc = StartInteractiveCmd(
                string.Format("call strata launch {0} > \"{1}\" 2>&1", launchArgs, tempLaunchLog),
                tempLaunchLog,
                ref launchLogPos,
                false);
        }

        private void WriteGuiLog(string msg)
        {
            txtLog.AppendText(string.Format("[{0}] {1}\r\n", DateTime.Now.ToString("HH:mm:ss"), msg));
            txtLog.ScrollToCaret();
            Program.WriteDebugFile(msg);
        }

        private static int GetIndentLen(string line)
        {
            int count = 0;
            while (count < line.Length && (line[count] == ' ' || line[count] == '\t')) count++;
            return count;
        }

        private static int FindStrataEnd(List<string> lines, int strataIdx, int sLen)
        {
            int end = lines.Count;
            for (int k = strataIdx + 1; k < lines.Count; k++)
            {
                if (string.IsNullOrWhiteSpace(lines[k]) || lines[k].TrimStart().StartsWith("#")) continue;
                if (GetIndentLen(lines[k]) <= sLen) { end = k; break; }
            }
            return end;
        }

        private bool PatchOmpModelsYml(string path, ThinkingProfile prof, string level)
        {
            if (!File.Exists(path)) return false;
            string raw = File.ReadAllText(path, Encoding.UTF8);
            List<string> lines = new List<string>(Regex.Split(raw, @"\r?\n"));
            while (lines.Count > 0 && lines[lines.Count - 1] == "") lines.RemoveAt(lines.Count - 1);

            int provIdx = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                if (Regex.IsMatch(lines[i], @"^providers:\s*(?:#.*)?$")) { provIdx = i; break; }
            }
            if (provIdx < 0) return false;

            int strataIdx = -1;
            string strataIndent = "";
            for (int i = provIdx + 1; i < lines.Count; i++)
            {
                string ln = lines[i];
                if (string.IsNullOrWhiteSpace(ln) || ln.TrimStart().StartsWith("#")) continue;
                if (GetIndentLen(ln) == 0) break;
                Match sm = Regex.Match(ln, @"^([ \t]+)strata:\s*(?:#.*)?$");
                if (sm.Success) { strataIdx = i; strataIndent = sm.Groups[1].Value; break; }
            }
            if (strataIdx < 0) return false;

            int sLen = strataIndent.Length;
            int strataEnd = FindStrataEnd(lines, strataIdx, sLen);
            string childIndent = strataIndent + "  ";
            for (int i = strataIdx + 1; i < strataEnd; i++)
            {
                Match cm = Regex.Match(lines[i], @"^([ \t]+)\S");
                if (cm.Success) { childIndent = cm.Groups[1].Value; break; }
            }
            int cLen = childIndent.Length;

            KeyValuePair<string, string>[] compatList = new KeyValuePair<string, string>[]
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

            int compatIdx = -1;
            int modelsIdx = -1;
            for (int i = strataIdx + 1; i < strataEnd; i++)
            {
                string ln = lines[i];
                if (string.IsNullOrWhiteSpace(ln) || ln.TrimStart().StartsWith("#")) continue;
                if (GetIndentLen(ln) == cLen)
                {
                    if (compatIdx < 0 && Regex.IsMatch(ln, @"^[ \t]+compat:(\s*\{\s*\})?\s*(?:#.*)?$")) compatIdx = i;
                    if (modelsIdx < 0 && Regex.IsMatch(ln, @"^[ \t]+models:\s*(?:#.*)?$")) modelsIdx = i;
                }
            }

            if (compatIdx >= 0)
            {
                lines[compatIdx] = childIndent + "compat:";
                int cEnd = strataEnd;
                for (int i = compatIdx + 1; i < strataEnd; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i]) || lines[i].TrimStart().StartsWith("#")) continue;
                    if (GetIndentLen(lines[i]) <= cLen) { cEnd = i; break; }
                }

                string kvIndent = childIndent + "  ";
                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                for (int i = compatIdx + 1; i < cEnd; i++)
                {
                    Match km = Regex.Match(lines[i], @"^([ \t]+)([a-zA-Z0-9_]+):");
                    if (km.Success)
                    {
                        kvIndent = km.Groups[1].Value;
                        string kName = km.Groups[2].Value;
                        string val;
                        if (compatMap.TryGetValue(kName, out val))
                        {
                            lines[i] = string.Format("{0}{1}: {2}", kvIndent, kName, val);
                            seen.Add(kName);
                        }
                    }
                }

                int insAt = cEnd;
                while (insAt > compatIdx + 1 && string.IsNullOrWhiteSpace(lines[insAt - 1])) insAt--;
                foreach (KeyValuePair<string, string> kv in compatList)
                {
                    if (!seen.Contains(kv.Key))
                    {
                        lines.Insert(insAt, string.Format("{0}{1}: {2}", kvIndent, kv.Key, kv.Value));
                        insAt++;
                    }
                }
            }
            else
            {
                int insAt = (modelsIdx >= 0) ? modelsIdx : (strataIdx + 1);
                string kvIndent = childIndent + "  ";
                lines.Insert(insAt++, childIndent + "compat:");
                foreach (KeyValuePair<string, string> kv in compatList)
                {
                    lines.Insert(insAt++, string.Format("{0}{1}: {2}", kvIndent, kv.Key, kv.Value));
                }
            }

            strataEnd = FindStrataEnd(lines, strataIdx, sLen);
            modelsIdx = -1;
            for (int i = strataIdx + 1; i < strataEnd; i++)
            {
                if (GetIndentLen(lines[i]) == cLen && Regex.IsMatch(lines[i], @"^[ \t]+models:\s*(?:#.*)?$"))
                {
                    modelsIdx = i;
                    break;
                }
            }

            if (modelsIdx >= 0)
            {
                int mEnd = strataEnd;
                for (int i = modelsIdx + 1; i < strataEnd; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i]) || lines[i].TrimStart().StartsWith("#")) continue;
                    if (GetIndentLen(lines[i]) <= cLen) { mEnd = i; break; }
                }

                List<int> itemStarts = new List<int>();
                for (int i = modelsIdx + 1; i < mEnd; i++)
                {
                    if (Regex.IsMatch(lines[i], @"^[ \t]+-\s+id:")) itemStarts.Add(i);
                }

                for (int idx = itemStarts.Count - 1; idx >= 0; idx--)
                {
                    int itStart = itemStarts[idx];
                    int itEnd = (idx + 1 < itemStarts.Count) ? itemStarts[idx + 1] : mEnd;
                    Match im = Regex.Match(lines[itStart], @"^([ \t]+)-");
                    string propIndent = im.Groups[1].Value + "  ";

                    int thinkIdx = -1;
                    int reasonIdx = -1;
                    for (int j = itStart + 1; j < itEnd; j++)
                    {
                        string ln = lines[j];
                        if (string.IsNullOrWhiteSpace(ln) || ln.TrimStart().StartsWith("#")) continue;
                        if (GetIndentLen(ln) == propIndent.Length)
                        {
                            if (Regex.IsMatch(ln, @"^[ \t]+thinking:(\s*\{\s*\})?\s*(?:#.*)?$")) thinkIdx = j;
                            else if (Regex.IsMatch(ln, @"^[ \t]+reasoning:\s*")) reasonIdx = j;
                        }
                    }

                    if (reasonIdx >= 0)
                    {
                        lines[reasonIdx] = propIndent + "reasoning: true";
                    }
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
                    {
                        thinkLines.Add(propIndent + "  defaultLevel: " + level);
                    }

                    if (thinkIdx >= 0)
                    {
                        int tEnd = itEnd;
                        for (int j = thinkIdx + 1; j < itEnd; j++)
                        {
                            if (string.IsNullOrWhiteSpace(lines[j]) || lines[j].TrimStart().StartsWith("#")) continue;
                            if (GetIndentLen(lines[j]) <= propIndent.Length) { tEnd = j; break; }
                        }
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

        private void SyncOmpConfigFiles(ModelInfo modelInfo, string level)
        {
            if (string.IsNullOrEmpty(level)) return;
            string fam = (modelInfo != null) ? modelInfo.Family : "qwen";
            ThinkingProfile prof = GetThinkingProfile(fam);
            string lvl = ResolveThinkingChoice(level, prof);

            string cfgPath = ResolveOmpFile("config");
            string modPath = ResolveOmpFile("models");

            try
            {
                if (!Directory.Exists(ompAgentDir)) Directory.CreateDirectory(ompAgentDir);
                string txt = File.Exists(cfgPath) ? File.ReadAllText(cfgPath, Encoding.UTF8) : "";

                List<string> lines = new List<string>(Regex.Split(txt, @"\r?\n"));
                while (lines.Count > 0 && lines[lines.Count - 1] == "") lines.RemoveAt(lines.Count - 1);

                int mrIdx = -1;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (Regex.IsMatch(lines[i], @"^modelRoles:\s*(?:#.*)?$")) { mrIdx = i; break; }
                }

                if (mrIdx >= 0)
                {
                    for (int i = mrIdx + 1; i < lines.Count; i++)
                    {
                        string ln = lines[i];
                        if (string.IsNullOrWhiteSpace(ln) || ln.TrimStart().StartsWith("#")) continue;
                        if (GetIndentLen(ln) == 0) break;
                        Match rm = Regex.Match(ln, @"^([ \t]+default:\s*)(strata/[^\s#]+)(.*)$");
                        if (rm.Success)
                        {
                            string baseId = Regex.Replace(rm.Groups[2].Value, @":(off|minimal|low|medium|high|xhigh|max|auto|inherit)$", "");
                            string newVal = (lvl == "auto") ? baseId : (baseId + ":" + lvl);
                            lines[i] = rm.Groups[1].Value + newVal + rm.Groups[3].Value;
                            break;
                        }
                    }
                }

                int dtlIdx = -1;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (Regex.IsMatch(lines[i], @"^defaultThinkingLevel:\s*([a-zA-Z0-9_-]*)(.*)$")) { dtlIdx = i; break; }
                }

                string[] validDtl = new string[] { "minimal", "low", "medium", "high", "xhigh", "max", "auto" };
                if (lvl == "off")
                {
                    if (dtlIdx >= 0)
                    {
                        Match dm = Regex.Match(lines[dtlIdx], @"^defaultThinkingLevel:\s*([a-zA-Z0-9_-]*)(.*)$");
                        string curDtl = dm.Groups[1].Value.ToLowerInvariant();
                        if (Array.IndexOf(validDtl, curDtl) < 0) lines[dtlIdx] = "defaultThinkingLevel: " + prof.Efforts[0];
                    }
                    else
                    {
                        lines.Add("defaultThinkingLevel: " + prof.Efforts[0]);
                    }
                }
                else
                {
                    if (dtlIdx >= 0) lines[dtlIdx] = "defaultThinkingLevel: " + lvl;
                    else lines.Add("defaultThinkingLevel: " + lvl);
                }

                File.WriteAllText(cfgPath, string.Join("\r\n", lines) + "\r\n", Program.Utf8NoBom);
                bool patchedModels = PatchOmpModelsYml(modPath, prof, lvl);
                string msg = string.Format("Synced OMP thinking -> {0} (efforts: [{1}])", lvl, string.Join(", ", prof.Efforts));
                if (!patchedModels) msg += " [run 'Sync omp' while engine serves to generate models.yml]";
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
            if (p != null)
            {
                try { if (!p.HasExited) Program.KillPidTree(p.Id); } catch { }
                try { p.Dispose(); } catch { }
                p = null;
            }
        }

        private static Process StartInteractiveCmd(string cmdChain, string logFile, ref long posRef, bool append)
        {
            if (!append)
            {
                try { File.WriteAllText(logFile, "", Program.Utf8NoBom); } catch { }
                posRef = 0;
            }

            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c " + cmdChain);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardInput = true;
            Process p = new Process();
            p.StartInfo = psi;
            p.Start();
            return p;
        }

        private void SyncLogTail(string path, ref long lastPos, string tag, Process proc)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (fs.Length < lastPos) lastPos = 0;
                    if (fs.Length > lastPos)
                    {
                        fs.Seek(lastPos, SeekOrigin.Begin);
                        using (StreamReader sr = new StreamReader(fs, Encoding.UTF8))
                        {
                            string delta = sr.ReadToEnd();
                            lastPos = fs.Position;
                            if (!string.IsNullOrEmpty(delta))
                            {
                                string norm = (delta.IndexOf('\n') >= 0) ? Regex.Replace(delta, @"(?<!\r)\n", "\r\n") : delta;
                                txtLog.AppendText(norm);
                                txtLog.ScrollToCaret();
                                Program.WriteDebugFile(string.Format("[{0}] {1}", tag, delta.Trim()));

                                if (!isPrompting && IsProcRunning(proc) && Regex.IsMatch(delta, @"(\[y/N\]|\[Y/n\]|\(y/n\))\s*$"))
                                {
                                    isPrompting = true;
                                    try
                                    {
                                        string[] lines = delta.Trim().Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);
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
                        {
                            try { isMatch = Regex.IsMatch(p.MainModule.FileName, "engine", RegexOptions.IgnoreCase); } catch { }
                        }
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
                {
                    if (ep.Port == 8080) { listening = true; break; }
                }
                if (!listening) return false;
            }
            catch { }

            using (TcpClient tcp = new TcpClient())
            {
                IAsyncResult iar = null;
                System.Threading.WaitHandle wh = null;
                try
                {
                    iar = tcp.BeginConnect("127.0.0.1", 8080, null, null);
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
                    if (wh != null) { try { wh.Close(); } catch { } }
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
                    if (root != null && root.TryGetValue("data", out dataObj))
                    {
                        object[] data = dataObj as object[];
                        if (data != null && data.Length > 0) return GetJsonStr(AsDict(data[0]), "id");
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
            if (modelMap.ContainsKey(info.Display)) return info.Display;
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
            {
                SyncLogTail(currentEngineLog, ref engineLogPos, "ENGINE_LOG", null);
            }

            if (pendingOmpSync != null && actionProc != null && !IsProcRunning(actionProc))
            {
                PendingOmpSyncInfo pending = pendingOmpSync;
                pendingOmpSync = null;
                bool exitOk = false;
                try { exitOk = (actionProc.ExitCode == 0); } catch { }
                StopProcTree(ref actionProc);

                if (exitOk)
                {
                    ModelInfo syncInfo = pending.Info;
                    if (TestPort8080())
                    {
                        string loadedId = GetLoadedStrataModelId();
                        if (!string.IsNullOrEmpty(loadedId) && loadedId != "strata")
                        {
                            syncInfo = ResolveModelInfo(loadedId, "");
                        }
                    }
                    ThinkingProfile prof = GetThinkingProfile(syncInfo != null ? syncInfo.Family : "qwen");
                    string resolvedLvl = ResolveThinkingChoice(pending.Level, prof);
                    SyncOmpConfigFiles(syncInfo, resolvedLvl);
                    actionProc = StartInteractiveCmd(
                        string.Format("call strata connect omp --check >> \"{0}\" 2>&1", tempCmdLog),
                        tempCmdLog,
                        ref cmdLogPos,
                        true);
                }
                else
                {
                    WriteGuiLog("WARNING: 'strata connect omp --write' did not complete; skipping OMP config patch.");
                }
            }

            string statusTxt;
            Color statusClr;
            string procTxt;

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
                            {
                                cmbModel.SelectedItem = matchedKey;
                            }
                            else if (loadedId != "strata")
                            {
                                UpdateThinkingOptions(ResolveModelInfo(loadedId, "").Family, null);
                            }
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
                fontUI.Dispose();
                fontBold.Dispose();
                fontStatus.Dispose();
                fontMono.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}