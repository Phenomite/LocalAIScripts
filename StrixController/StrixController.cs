using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Strix Controller")]
[assembly: System.Reflection.AssemblyDescription("Launch Strix scans with the Strata inference server and an omp TUI companion")]
[assembly: System.Reflection.AssemblyProduct("Strix Controller")]
[assembly: System.Reflection.AssemblyConfiguration("Release")]
[assembly: System.Reflection.AssemblyCompany("Phenomite")]
[assembly: System.Reflection.AssemblyCopyright("Copyright (c) 2026 Phenomite")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("1.0.0")]
[assembly: System.Runtime.InteropServices.ComVisible(false)]

namespace StrixControllerApp {
    internal static class Program {
        public static string DebugLogPath = Path.Combine(Path.GetTempPath(), "StrixController.debug.log");

        public static void Log(string msg) {
            try { File.AppendAllText(DebugLogPath, string.Format("[{0:yyyy-MM-dd HH:mm:ss.fff}] {1}\r\n", DateTime.Now, msg)); } catch { }
        }

        private static void HandleError(Exception ex) {
            Log(ex.ToString());
            MessageBox.Show(ex.Message, "Strix Controller Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        [STAThread]
        private static void Main() {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { HandleError(e.Exception); };
            try { Application.Run(new StrixControllerForm()); } catch (Exception ex) { HandleError(ex); }
        }
    }

    internal sealed class StrixControllerForm : Form {
        private const string ModelsUrl = "http://127.0.0.1:8080/v1/models", ApiBase = "http://127.0.0.1:8080/v1", DefaultModel = "qwen3.8-flash-next-iq3_s";
        private const string GuideNewSub = "Optional - scope rules, off-limits areas, or focus endpoints";
        private const string GuideResumeSub = "Optional - mid-scan steer message sent to the resumed run";

        private enum StatusState { Checking, Ready, Warn, Error }
        private enum PillState { Checking, Ok, Warn, Error }

        private sealed class UiTheme {
            public bool IsDark;
            public Color BgCanvas, BgCard, BgInput, BgInputHover, BgDrop, BgBtn, BgHover, BgPressed, BgDisabled;
            public Color BorderCard, BorderInput, BorderHover, BorderReady;
            public Color Accent, Green, GreenHover, GreenPressed, GreenBorder;
            public Color FgText, FgMuted, FgDim;
            public Color ColorOk, ColorWarn, ColorErr;
            public Color ToggleTrack, ToggleKnobOff, ThemeTrack, ThemeThumb, ThemeIconActive;
            public Color PillOkBg, PillOkBd, PillOkFg, PillErrBg, PillErrBd, PillErrFg, PillWarnBg, PillWarnBd, PillWarnFg;

            public static readonly UiTheme Dark = new UiTheme {
                IsDark = true,
                BgCanvas = Color.FromArgb(17, 18, 23), BgCard = Color.FromArgb(26, 28, 35), BgInput = Color.FromArgb(19, 20, 26),
                BgInputHover = Color.FromArgb(24, 26, 34), BgDrop = Color.FromArgb(19, 20, 26), BgBtn = Color.FromArgb(37, 40, 52),
                BgHover = Color.FromArgb(48, 52, 66), BgPressed = Color.FromArgb(30, 33, 44), BgDisabled = Color.FromArgb(33, 36, 44),
                BorderCard = Color.FromArgb(44, 48, 60), BorderInput = Color.FromArgb(56, 61, 78),
                BorderHover = Color.FromArgb(74, 80, 102), BorderReady = Color.FromArgb(44, 112, 66),
                Accent = Color.FromArgb(88, 166, 255), Green = Color.FromArgb(35, 152, 68),
                GreenHover = Color.FromArgb(44, 174, 82), GreenPressed = Color.FromArgb(28, 128, 56), GreenBorder = Color.FromArgb(56, 178, 90),
                FgText = Color.FromArgb(238, 240, 246), FgMuted = Color.FromArgb(154, 162, 182), FgDim = Color.FromArgb(120, 128, 148),
                ColorOk = Color.FromArgb(63, 205, 110), ColorWarn = Color.FromArgb(242, 186, 66), ColorErr = Color.FromArgb(240, 82, 82),
                ToggleTrack = Color.FromArgb(19, 20, 26), ToggleKnobOff = Color.FromArgb(154, 162, 182),
                ThemeTrack = Color.FromArgb(19, 20, 26), ThemeThumb = Color.FromArgb(44, 48, 62), ThemeIconActive = Color.FromArgb(120, 185, 255),
                PillOkBg = Color.FromArgb(24, 52, 36), PillOkBd = Color.FromArgb(44, 110, 66), PillOkFg = Color.FromArgb(172, 238, 190),
                PillErrBg = Color.FromArgb(56, 26, 30), PillErrBd = Color.FromArgb(128, 48, 54), PillErrFg = Color.FromArgb(248, 178, 178),
                PillWarnBg = Color.FromArgb(54, 42, 22), PillWarnBd = Color.FromArgb(120, 90, 38), PillWarnFg = Color.FromArgb(244, 212, 146)
            };

            public static readonly UiTheme Light = new UiTheme {
                IsDark = false,
                BgCanvas = Color.FromArgb(241, 243, 248), BgCard = Color.FromArgb(255, 255, 255), BgInput = Color.FromArgb(246, 247, 251),
                BgInputHover = Color.FromArgb(238, 241, 248), BgDrop = Color.FromArgb(255, 255, 255), BgBtn = Color.FromArgb(243, 245, 250),
                BgHover = Color.FromArgb(232, 236, 246), BgPressed = Color.FromArgb(218, 224, 238), BgDisabled = Color.FromArgb(236, 238, 244),
                BorderCard = Color.FromArgb(216, 220, 232), BorderInput = Color.FromArgb(196, 202, 218),
                BorderHover = Color.FromArgb(156, 166, 188), BorderReady = Color.FromArgb(72, 172, 108),
                Accent = Color.FromArgb(37, 116, 235), Green = Color.FromArgb(27, 138, 61),
                GreenHover = Color.FromArgb(23, 122, 53), GreenPressed = Color.FromArgb(19, 104, 45), GreenBorder = Color.FromArgb(21, 112, 49),
                FgText = Color.FromArgb(24, 28, 38), FgMuted = Color.FromArgb(84, 92, 114), FgDim = Color.FromArgb(108, 116, 136),
                ColorOk = Color.FromArgb(26, 148, 72), ColorWarn = Color.FromArgb(168, 102, 8), ColorErr = Color.FromArgb(206, 46, 46),
                ToggleTrack = Color.FromArgb(210, 215, 228), ToggleKnobOff = Color.FromArgb(255, 255, 255),
                ThemeTrack = Color.FromArgb(232, 236, 244), ThemeThumb = Color.FromArgb(255, 255, 255), ThemeIconActive = Color.FromArgb(202, 122, 8),
                PillOkBg = Color.FromArgb(232, 247, 237), PillOkBd = Color.FromArgb(156, 218, 178), PillOkFg = Color.FromArgb(20, 102, 52),
                PillErrBg = Color.FromArgb(253, 236, 237), PillErrBd = Color.FromArgb(240, 178, 180), PillErrFg = Color.FromArgb(158, 34, 38),
                PillWarnBg = Color.FromArgb(254, 245, 227), PillWarnBd = Color.FromArgb(234, 200, 132), PillWarnFg = Color.FromArgb(136, 84, 10)
            };
        }

        private static UiTheme T = UiTheme.Dark;

        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int sz);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

        private readonly HttpClient http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromMilliseconds(1200) };
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        private readonly Font fontUI = new Font("Segoe UI", 9.5f), fontBold = new Font("Segoe UI Semibold", 9.5f);
        private readonly Font fontLbl = new Font("Segoe UI", 8.75f), fontHead = new Font("Segoe UI Semibold", 8f);
        private readonly string runsDir;
        private ToolTip tip;
        private TableLayoutPanel outer;
        private FlowLayoutPanel pills;
        private TextBox txtTarget, txtInstruction, txtMaxTurns, txtMaxBudget, txtOmpDir;
        private InputWrap wrapTarget;
        private ThemedCombo cmbMode, cmbScope, cmbResume;
        private ToggleCheck chkHeadless;
        private RoundButton btnStart, btnOmp, btnRuns;
        private ThemeToggle btnTheme;
        private CardPanel cLaunch;
        private StatusDot statusDot;
        private StatusPill pillDocker, pillStrix, pillOmp;
        private Label lblStatus, lblGuideSub;
        private System.Windows.Forms.Timer statusTimer;
        private StatusState statusState = StatusState.Checking;
        private string servedModelId = "", strixPath, dockerPath, ompPath;
        private int inputH, btnH;
        private bool checking, closing;

        public StrixControllerForm() {
            SetupEnvironmentPath();
            runsDir = Path.Combine(Application.StartupPath, "runs");
            DoubleBuffered = true;
            BuildUi();
            RefreshResumeList();
            statusTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            statusTimer.Tick += delegate { CheckBackend(); };
        }

        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);
            UpdateTitleBarTheme();
        }

        protected override void OnShown(EventArgs e) {
            base.OnShown(e);
            ActiveControl = txtTarget;
            txtTarget.SelectionStart = txtTarget.TextLength;
            statusTimer.Start();
            CheckBackend();
        }

        protected override void OnFormClosing(FormClosingEventArgs e) {
            closing = true;
            statusTimer.Stop();
            try { cts.Cancel(); } catch { }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e) {
            base.OnFormClosed(e);
            statusTimer.Dispose(); cts.Dispose(); http.Dispose(); tip.Dispose();
            fontUI.Dispose(); fontBold.Dispose(); fontLbl.Dispose(); fontHead.Dispose();
        }

        private void UpdateTitleBarTheme() {
            if (!IsHandleCreated) return;
            try {
                int v = T.IsDark ? 1 : 0;
                if (DwmSetWindowAttribute(Handle, 20, ref v, 4) != 0) DwmSetWindowAttribute(Handle, 19, ref v, 4);
            } catch { }
        }

        private int S(float px) { return (int)Math.Round(px * DeviceDpi / 96f); }

        private static GraphicsPath RoundPath(RectangleF r, float rad) {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(rad * 2f, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private sealed class CardPanel : TableLayoutPanel {
            private bool readyHighlight;
            public float Rad;
            public CardPanel(float rad) { Rad = rad; DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); }
            public void SetReady(bool ready) { if (readyHighlight == ready) return; readyHighlight = ready; Invalidate(); }
            protected override void OnPaintBackground(PaintEventArgs e) {
                if (Width <= 2 || Height <= 2) return;
                e.Graphics.Clear(T.BgCanvas);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath p = RoundPath(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Rad)) {
                    using (SolidBrush b = new SolidBrush(T.BgCard)) e.Graphics.FillPath(b, p);
                    using (Pen pen = new Pen(readyHighlight ? T.BorderReady : T.BorderCard, 1f)) e.Graphics.DrawPath(pen, p);
                }
            }
        }

        private sealed class StatusDot : Control {
            private readonly int outerD, innerD;
            private Color color;
            public StatusDot(int outerSz, int innerSz, Color initial) {
                outerD = outerSz; innerD = innerSz; color = initial; TabStop = false;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }
            public void SetColor(Color c) { if (color == c) return; color = c; Invalidate(); }
            protected override void OnPaint(PaintEventArgs e) {
                if (Width <= 2 || Height <= 2) return;
                Graphics g = e.Graphics;
                g.Clear(T.BgCard);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using (SolidBrush b1 = new SolidBrush(Color.FromArgb(55, color))) g.FillEllipse(b1, (Width - outerD) / 2f, (Height - outerD) / 2f, outerD, outerD);
                using (SolidBrush b2 = new SolidBrush(color)) g.FillEllipse(b2, (Width - innerD) / 2f, (Height - innerD) / 2f, innerD, innerD);
            }
        }

        private sealed class InputWrap : Panel {
            public readonly TextBox Box;
            private readonly float rad;
            private readonly int px, py;
            private bool focused, hover;
            public InputWrap(TextBox tb, float r, int padX, int padY) {
                Box = tb; rad = r; px = padX; py = padY;
                DoubleBuffered = true; Cursor = Cursors.IBeam;
                SetStyle(ControlStyles.ResizeRedraw, true);
                Controls.Add(tb);
                tb.Enter += delegate { focused = true; Invalidate(); };
                tb.Leave += delegate { focused = false; Invalidate(); };
                tb.MouseEnter += delegate { SetHover(true); };
                tb.MouseLeave += delegate { CheckHoverLeave(); };
                MouseEnter += delegate { SetHover(true); };
                MouseLeave += delegate { CheckHoverLeave(); };
                MouseDown += delegate { tb.Focus(); };
                if (tb.Multiline) {
                    tb.KeyDown += delegate(object s, KeyEventArgs e) {
                        if (e.Control && e.KeyCode == Keys.A) { tb.SelectAll(); e.SuppressKeyPress = true; }
                    };
                }
                PositionBox();
                SyncTheme();
            }
            private void SetHover(bool on) { if (hover != on) { hover = on; Invalidate(); } }
            private void CheckHoverLeave() { if (!ClientRectangle.Contains(PointToClient(Control.MousePosition))) SetHover(false); }
            public void SyncTheme() {
                bool dim = Box.ReadOnly || !Box.Enabled;
                Color fill = dim ? T.BgDisabled : T.BgInput;
                Box.BackColor = fill;
                Box.ForeColor = dim ? T.FgDim : T.FgText;
                Box.Invalidate();
                Invalidate();
            }
            protected override void OnResize(EventArgs e) { base.OnResize(e); PositionBox(); }
            protected override void OnPaintBackground(PaintEventArgs e) { }
            private void PositionBox() {
                if (Box.Multiline) Box.SetBounds(px, py, Math.Max(10, Width - px * 2), Math.Max(16, Height - py * 2));
                else Box.SetBounds(px, Math.Max(1, (Height - Box.PreferredHeight) / 2), Math.Max(10, Width - px * 2), Box.PreferredHeight);
            }
            protected override void OnPaint(PaintEventArgs e) {
                if (Width <= 2 || Height <= 2) return;
                e.Graphics.Clear(T.BgCard);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                bool dim = Box.ReadOnly || !Box.Enabled;
                Color fill = dim ? T.BgDisabled : T.BgInput;
                Color bd = focused ? T.Accent : (dim ? T.BorderCard : (hover ? T.BorderHover : T.BorderInput));
                using (GraphicsPath p = RoundPath(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), rad)) {
                    using (SolidBrush b = new SolidBrush(fill)) e.Graphics.FillPath(b, p);
                    using (Pen pen = new Pen(bd, 1f)) e.Graphics.DrawPath(pen, p);
                }
            }
        }

        private sealed class ThemedCombo : Control {
            private sealed class DropRenderer : ToolStripRenderer {
                protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) {
                    using (SolidBrush b = new SolidBrush(T.BorderInput)) e.Graphics.FillRectangle(b, e.AffectedBounds);
                }
                protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
            }

            private readonly float rad;
            private readonly int chev, padX;
            private readonly ListBox list;
            private readonly ToolStripControlHost host;
            private readonly ToolStripDropDown drop;
            private int selectedIndex = -1, closedTick;
            private bool hover;

            public event EventHandler DropDown, SelectedIndexChanged;
            public int DropDownWidth;
            public ListBox.ObjectCollection Items { get { return list.Items; } }
            public object SelectedItem { get { return (selectedIndex >= 0 && selectedIndex < list.Items.Count) ? list.Items[selectedIndex] : null; } }
            public int SelectedIndex {
                get { return selectedIndex; }
                set {
                    if (value < -1 || value >= list.Items.Count) value = -1;
                    if (selectedIndex == value) return;
                    selectedIndex = value;
                    Invalidate();
                    if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
                }
            }

            public ThemedCombo(float r, int chevSz, int textPadX) {
                rad = r; chev = chevSz; padX = textPadX; TabStop = true; Cursor = Cursors.Hand; BackColor = T.BgCard; ForeColor = T.FgText;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                list = new ListBox { BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, IntegralHeight = false, BackColor = T.BgDrop, ForeColor = T.FgText, Cursor = Cursors.Hand };
                list.DrawItem += OnListDrawItem;
                list.MouseMove += delegate(object s, MouseEventArgs e) {
                    int i = list.IndexFromPoint(e.Location);
                    if (i >= 0 && i != list.SelectedIndex) list.SelectedIndex = i;
                };
                list.MouseUp += delegate(object s, MouseEventArgs e) {
                    if (e.Button != MouseButtons.Left || !list.ClientRectangle.Contains(e.Location)) return;
                    int i = list.IndexFromPoint(e.Location);
                    if (i >= 0) { SelectedIndex = i; drop.Close(); Focus(); }
                };
                list.KeyDown += delegate(object s, KeyEventArgs e) {
                    if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) {
                        if (list.SelectedIndex >= 0) SelectedIndex = list.SelectedIndex;
                        drop.Close(); Focus(); e.Handled = true;
                    } else if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.F4 || (e.Alt && e.KeyCode == Keys.Up)) {
                        drop.Close(); Focus(); e.Handled = true;
                    }
                };
                host = new ToolStripControlHost(list) { AutoSize = false, Margin = Padding.Empty, Padding = Padding.Empty };
                drop = new ToolStripDropDown { AutoSize = false, Padding = new Padding(1), Margin = Padding.Empty, Renderer = new DropRenderer() };
                drop.Items.Add(host);
                drop.Closed += delegate { closedTick = Environment.TickCount; Invalidate(); };
            }

            public void SetItems(IList<string> items, string preferred) {
                string oldVal = SelectedItem != null ? SelectedItem.ToString() : null;
                list.BeginUpdate();
                list.Items.Clear();
                for (int i = 0; i < items.Count; i++) list.Items.Add(items[i]);
                int nextIdx = list.Items.Count > 0 ? 0 : -1;
                if (preferred != null) {
                    int found = list.Items.IndexOf(preferred);
                    if (found >= 0) nextIdx = found;
                }
                selectedIndex = nextIdx;
                list.EndUpdate();
                Invalidate();
                string newVal = SelectedItem != null ? SelectedItem.ToString() : null;
                if (!string.Equals(oldVal, newVal, StringComparison.Ordinal) && SelectedIndexChanged != null)
                    SelectedIndexChanged(this, EventArgs.Empty);
            }

            public void SyncTheme() {
                BackColor = T.BgCard; ForeColor = T.FgText;
                list.BackColor = T.BgDrop; list.ForeColor = T.FgText;
                list.Invalidate(); drop.Invalidate(); Invalidate();
            }

            private void ToggleDrop() {
                if (drop.Visible) { drop.Close(); return; }
                if (DropDown != null) DropDown(this, EventArgs.Empty);
                if (list.Items.Count == 0) return;
                list.Font = Font;
                list.BackColor = T.BgDrop;
                list.ForeColor = T.FgText;
                int maxVis = Math.Min(list.Items.Count, 10);
                int rowH = Math.Max(Height - 2, Font.Height + 10);
                int scrollW = list.Items.Count > maxVis ? SystemInformation.VerticalScrollBarWidth : 0;
                int popW = Math.Max(Width, DropDownWidth + scrollW), popH = maxVis * rowH;
                list.ItemHeight = rowH;
                list.Size = host.Size = new Size(popW - 2, popH);
                drop.Size = new Size(popW, popH + 2);
                list.SelectedIndex = (selectedIndex >= 0 && selectedIndex < list.Items.Count) ? selectedIndex : 0;
                drop.Show(this, 0, Height + 2);
                list.Focus();
                Invalidate();
            }

            private void OnListDrawItem(object s, DrawItemEventArgs e) {
                if (e.Index < 0 || e.Index >= list.Items.Count) return;
                bool hot = e.Index == list.SelectedIndex;
                using (SolidBrush b = new SolidBrush(hot ? T.BgHover : T.BgDrop)) e.Graphics.FillRectangle(b, e.Bounds);
                Rectangle tr = new Rectangle(e.Bounds.X + padX, e.Bounds.Y, Math.Max(1, e.Bounds.Width - padX - 4), e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, (list.Items[e.Index] ?? "").ToString(), Font, tr, T.FgText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }

            protected override bool IsInputKey(Keys keyData) {
                return keyData == Keys.Up || keyData == Keys.Down || keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Home || keyData == Keys.End || base.IsInputKey(keyData);
            }
            protected override void OnKeyDown(KeyEventArgs e) {
                base.OnKeyDown(e);
                if ((e.Alt && e.KeyCode == Keys.Down) || e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter || e.KeyCode == Keys.F4) { ToggleDrop(); e.Handled = true; }
                else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Right) { if (selectedIndex < list.Items.Count - 1) SelectedIndex = selectedIndex + 1; e.Handled = true; }
                else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Left) { if (selectedIndex > 0) SelectedIndex = selectedIndex - 1; e.Handled = true; }
                else if (e.KeyCode == Keys.Home && list.Items.Count > 0) { SelectedIndex = 0; e.Handled = true; }
                else if (e.KeyCode == Keys.End && list.Items.Count > 0) { SelectedIndex = list.Items.Count - 1; e.Handled = true; }
            }
            protected override void OnMouseWheel(MouseEventArgs e) {
                base.OnMouseWheel(e);
                if (list.Items.Count == 0) return;
                int next = selectedIndex + (e.Delta < 0 ? 1 : -1);
                if (next >= 0 && next < list.Items.Count) SelectedIndex = next;
            }
            protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
            protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
            protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
            protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); }
            protected override void OnMouseDown(MouseEventArgs e) {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left) return;
                Focus();
                if (unchecked(Environment.TickCount - closedTick) < 180) return;
                ToggleDrop();
            }
            protected override void OnPaint(PaintEventArgs e) {
                if (Width <= 2 || Height <= 2) return;
                Graphics g = e.Graphics; g.Clear(T.BgCard); g.SmoothingMode = SmoothingMode.AntiAlias;
                bool active = Focused || drop.Visible;
                Color bg = !Enabled ? T.BgDisabled : (hover && !active ? T.BgInputHover : T.BgInput);
                Color bd = !Enabled ? T.BorderCard : (active ? T.Accent : (hover ? T.BorderHover : T.BorderInput));
                using (GraphicsPath p = RoundPath(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), rad)) {
                    using (SolidBrush b = new SolidBrush(bg)) g.FillPath(b, p);
                    using (Pen pen = new Pen(bd, 1f)) g.DrawPath(pen, p);
                }
                int arrowW = Math.Max(22, chev * 6), cx = Width - arrowW / 2, cy = Height / 2, dy = drop.Visible ? -chev / 2 : chev / 2;
                string txt = SelectedItem != null ? SelectedItem.ToString() : "";
                TextRenderer.DrawText(g, txt, Font, new Rectangle(padX, 0, Math.Max(1, Width - padX - arrowW), Height), Enabled ? T.FgText : T.FgDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                using (Pen cp = new Pen(Enabled ? (active ? T.FgText : T.FgMuted) : T.FgDim, 1.5f))
                    g.DrawLines(cp, new[] { new Point(cx - chev, cy - dy), new Point(cx, cy + dy), new Point(cx + chev, cy - dy) });
            }
            protected override void Dispose(bool disposing) {
                if (disposing) { drop.Dispose(); list.Dispose(); }
                base.Dispose(disposing);
            }
        }

        private sealed class RoundButton : Button {
            public bool Primary;
            private readonly float rad;
            private bool hover, down;
            public RoundButton(float r) {
                rad = r; Cursor = Cursors.Hand;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }
            protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = down = false; Invalidate(); }
            protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } }
            protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); down = false; Invalidate(); }
            protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); }
            protected override void OnPaint(PaintEventArgs e) {
                if (Width <= 2 || Height <= 2) return;
                Graphics g = e.Graphics;
                g.Clear(T.BgCard);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Color fill = Primary ? T.Green : T.BgBtn;
                Color hoverFill = Primary ? T.GreenHover : T.BgHover;
                Color pressFill = Primary ? T.GreenPressed : T.BgPressed;
                Color border = Primary ? T.GreenBorder : T.BorderInput;
                Color bg = !Enabled ? T.BgDisabled : (down ? pressFill : (hover ? hoverFill : fill));
                Color bd = !Enabled ? T.BorderCard : (Focused ? T.Accent : (hover && !Primary ? T.BorderHover : border));
                Color fg = !Enabled ? T.FgDim : (Primary ? Color.White : T.FgText);
                using (GraphicsPath p = RoundPath(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), rad)) {
                    using (SolidBrush b = new SolidBrush(bg)) g.FillPath(b, p);
                    using (Pen pen = new Pen(bd, 1f)) g.DrawPath(pen, p);
                }
                if (Primary && Focused && Enabled && Width > 6 && Height > 6) {
                    using (GraphicsPath inner = RoundPath(new RectangleF(2f, 2f, Width - 4.5f, Height - 4.5f), Math.Max(1f, rad - 1.5f)))
                    using (Pen ip = new Pen(Color.FromArgb(165, Color.White), 1f)) g.DrawPath(ip, inner);
                }
                TextRenderer.DrawText(g, Text, Font, ClientRectangle, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }

        private sealed class ToggleCheck : CheckBox {
            private readonly int tw, th, gap;
            private bool hover;
            public ToggleCheck(int w, int h, int g) {
                tw = w; th = h; gap = g; AutoSize = false; Cursor = Cursors.Hand; BackColor = T.BgCard;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }
            protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
            protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); }
            protected override void OnPaint(PaintEventArgs e) {
                if (Width <= 2 || Height <= 2) return;
                Graphics g = e.Graphics;
                g.Clear(T.BgCard);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int tx = 2, ty = (Height - th) / 2, pad = Math.Max(3, th / 6), d = th - pad * 2;
                if (Focused && Enabled) {
                    using (GraphicsPath fp = RoundPath(new RectangleF(0.5f, ty - 1.5f, tw + 3f, th + 3f), (th + 3f) / 2f))
                    using (Pen fpen = new Pen(T.Accent, 1f)) g.DrawPath(fpen, fp);
                }
                Color trackFill = !Enabled ? T.BgDisabled : (Checked ? T.Accent : T.ToggleTrack);
                Color trackBd = !Enabled ? T.BorderCard : (Checked ? T.Accent : (hover ? T.BorderHover : T.BorderInput));
                using (GraphicsPath p = RoundPath(new RectangleF(tx + 0.5f, ty + 0.5f, tw - 1f, th - 1f), th / 2f)) {
                    using (SolidBrush b = new SolidBrush(trackFill)) g.FillPath(b, p);
                    using (Pen pen = new Pen(trackBd, 1f)) g.DrawPath(pen, p);
                }
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                Color knob = !Enabled ? T.FgDim : (Checked ? Color.White : T.ToggleKnobOff);
                using (SolidBrush tb = new SolidBrush(knob))
                    g.FillEllipse(tb, Checked ? (tx + tw - d - pad) : (tx + pad), ty + pad, d, d);
                int lx = tx + tw + gap;
                TextRenderer.DrawText(g, Text, Font, new Rectangle(lx, 0, Math.Max(1, Width - lx), Height), Enabled ? T.FgText : T.FgDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }

        private sealed class StatusPill : Control {
            private PillState state = PillState.Checking;
            private readonly int px, ds, textGap;
            public StatusPill(string text, Font f, int h, int padX, int dotSz) {
                Text = text; Font = f; px = padX; ds = dotSz; textGap = Math.Max(4, padX / 2); Height = h;
                int textW = TextRenderer.MeasureText(text, f, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width + 2;
                Width = textW + px * 2 + ds + textGap;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }
            public void SetState(PillState next) { if (state == next) return; state = next; Invalidate(); }
            protected override void OnPaint(PaintEventArgs e) {
                if (Width <= 2 || Height <= 2) return;
                Graphics g = e.Graphics; g.Clear(T.BgCard); g.SmoothingMode = SmoothingMode.AntiAlias;
                Color bg = state == PillState.Ok ? T.PillOkBg : (state == PillState.Error ? T.PillErrBg : (state == PillState.Warn ? T.PillWarnBg : T.BgInput));
                Color bd = state == PillState.Ok ? T.PillOkBd : (state == PillState.Error ? T.PillErrBd : (state == PillState.Warn ? T.PillWarnBd : T.BorderInput));
                Color dt = state == PillState.Ok ? T.ColorOk : (state == PillState.Error ? T.ColorErr : (state == PillState.Warn ? T.ColorWarn : T.FgDim));
                Color fg = state == PillState.Ok ? T.PillOkFg : (state == PillState.Error ? T.PillErrFg : (state == PillState.Warn ? T.PillWarnFg : T.FgMuted));
                using (GraphicsPath p = RoundPath(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Height / 2f)) {
                    using (SolidBrush b = new SolidBrush(bg)) g.FillPath(b, p);
                    using (Pen pen = new Pen(bd, 1f)) g.DrawPath(pen, p);
                }
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using (SolidBrush db = new SolidBrush(dt)) g.FillEllipse(db, px, (Height - ds) / 2f, ds, ds);
                int tx = px + ds + textGap;
                TextRenderer.DrawText(g, Text, Font, new Rectangle(tx, 0, Math.Max(1, Width - tx - px + 2), Height), fg, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }
        }

        private sealed class ThemeToggle : Button {
            private bool isLight, hover, down;
            public bool IsLight { get { return isLight; } set { if (isLight == value) return; isLight = value; Invalidate(); } }
            public ThemeToggle(int w, int h) {
                Size = new Size(w, h); Cursor = Cursors.Hand; Text = ""; AccessibleName = "Toggle light or dark mode";
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }
            protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = down = false; Invalidate(); }
            protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } }
            protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); down = false; Invalidate(); }
            protected override void OnPaint(PaintEventArgs e) {
                if (Width <= 2 || Height <= 2) return;
                Graphics g = e.Graphics;
                g.Clear(T.BgCard);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Color bd = Focused ? T.Accent : (hover ? T.BorderHover : T.BorderInput);
                using (GraphicsPath track = RoundPath(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Height / 2f)) {
                    using (SolidBrush tb = new SolidBrush(T.ThemeTrack)) g.FillPath(tb, track);
                    using (Pen tp = new Pen(bd, 1f)) g.DrawPath(tp, track);
                }
                float pad = Math.Max(2f, Height * 0.11f), th = Height - pad * 2f, tw = (Width - pad * 2f) / 2f;
                float tx = isLight ? (Width - pad - tw) : pad;
                Color thumbBg = down ? T.BgPressed : T.ThemeThumb;
                using (GraphicsPath thumb = RoundPath(new RectangleF(tx, pad, tw, th), th / 2f)) {
                    using (SolidBrush hb = new SolidBrush(thumbBg)) g.FillPath(hb, thumb);
                    using (Pen hp = new Pen(T.BorderInput, 1f)) g.DrawPath(hp, thumb);
                }
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float cy = Height / 2f, lx = pad + tw / 2f, rx = Width - pad - tw / 2f, r = Math.Max(3.2f, th * 0.27f);
                Color moonFg = !isLight ? T.ThemeIconActive : T.FgDim;
                Color moonBg = !isLight ? thumbBg : T.ThemeTrack;
                using (SolidBrush mb = new SolidBrush(moonFg)) g.FillEllipse(mb, lx - r, cy - r, r * 2f, r * 2f);
                float cr = r * 0.84f, cx = lx + r * 0.42f, ccy = cy - r * 0.26f;
                using (SolidBrush cb = new SolidBrush(moonBg)) g.FillEllipse(cb, cx - cr, ccy - cr, cr * 2f, cr * 2f);
                Color sunFg = isLight ? T.ThemeIconActive : T.FgDim;
                float sr = r * 0.46f, r1 = r * 0.76f, r2 = r * 1.08f;
                using (SolidBrush sb = new SolidBrush(sunFg)) g.FillEllipse(sb, rx - sr, cy - sr, sr * 2f, sr * 2f);
                using (Pen sp = new Pen(sunFg, Math.Max(1.1f, r * 0.24f)) { StartCap = LineCap.Round, EndCap = LineCap.Round }) {
                    for (int i = 0; i < 8; i++) {
                        double a = i * Math.PI / 4.0;
                        float cos = (float)Math.Cos(a), sin = (float)Math.Sin(a);
                        g.DrawLine(sp, rx + cos * r1, cy + sin * r1, rx + cos * r2, cy + sin * r2);
                    }
                }
            }
        }

        private CardPanel MakeCard(TableLayoutPanel parent, int col, int row, int colSpan, Padding margin, bool stretch, int cols, RowStyle[] rows) {
            CardPanel c = new CardPanel(S(9)) {
                Dock = DockStyle.Fill, AutoSize = !stretch, ColumnCount = cols, RowCount = rows.Length,
                Padding = new Padding(S(16), S(13), S(16), S(15)), Margin = margin
            };
            for (int i = 0; i < cols; i++) c.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / cols));
            foreach (RowStyle r in rows) c.RowStyles.Add(r);
            parent.Controls.Add(c, col, row);
            if (colSpan > 1) parent.SetColumnSpan(c, colSpan);
            return c;
        }

        private Label AddHead(CardPanel c, string title, string sub) {
            Label h = new Label { Text = title, Font = fontHead, ForeColor = T.FgMuted, BackColor = T.BgCard, Tag = "card-muted", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 0, S(8)) };
            c.Controls.Add(h, 0, 0);
            if (sub == null) { if (c.ColumnCount > 1) c.SetColumnSpan(h, c.ColumnCount); return null; }
            Label s = new Label { Text = sub, Font = fontLbl, ForeColor = T.FgDim, BackColor = T.BgCard, Tag = "card-dim", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, AutoEllipsis = true, Margin = new Padding(S(8), 0, 0, S(8)) };
            c.Controls.Add(s, 1, 0);
            return s;
        }

        private Padding ColPad(int col, int span, int cols, int top, int bot) {
            int g = S(5);
            return new Padding(col > 0 ? g : 0, top, (col + span < cols) ? g : 0, bot);
        }

        private Label AddLbl(CardPanel c, string text, int col, int row, int span, int topGap) {
            Label l = new Label { Text = text, Font = fontLbl, ForeColor = T.FgMuted, BackColor = T.BgCard, Tag = "card-muted", AutoSize = true, Margin = ColPad(col, span, c.ColumnCount, topGap, S(4)) };
            c.Controls.Add(l, col, row);
            if (span > 1) c.SetColumnSpan(l, span);
            return l;
        }

        private TextBox AddInput(CardPanel c, int col, int row, int span, string val, string cue, string help, bool multi) {
            InputWrap wrap;
            return AddInput(c, col, row, span, val, cue, help, multi, out wrap);
        }

        private TextBox AddInput(CardPanel c, int col, int row, int span, string val, string cue, string help, bool multi, out InputWrap wrap) {
            TextBox tb = new TextBox { Text = val, BorderStyle = BorderStyle.None, BackColor = T.BgInput, ForeColor = T.FgText, Font = fontUI, Multiline = multi, AcceptsReturn = multi, ScrollBars = multi ? ScrollBars.Vertical : ScrollBars.None };
            if (!multi && !string.IsNullOrEmpty(cue)) tb.HandleCreated += delegate { SendMessage(tb.Handle, 0x1501, (IntPtr)1, cue); };
            wrap = new InputWrap(tb, S(6), S(10), S(8)) { Dock = DockStyle.Fill, Margin = ColPad(col, span, c.ColumnCount, 0, 0) };
            c.Controls.Add(wrap, col, row);
            if (span > 1) c.SetColumnSpan(wrap, span);
            if (help != null) { tip.SetToolTip(tb, help); tip.SetToolTip(wrap, help); }
            return tb;
        }

        private ThemedCombo AddCombo(CardPanel c, int col, int row, int span, string[] items, int sel, string help) {
            ThemedCombo cb = new ThemedCombo(S(6), S(4), S(10)) { Font = fontUI, Dock = DockStyle.Fill, Margin = ColPad(col, span, c.ColumnCount, 0, 0) };
            if (items != null) { cb.Items.AddRange(items); cb.SelectedIndex = sel; }
            c.Controls.Add(cb, col, row);
            if (span > 1) c.SetColumnSpan(cb, span);
            if (help != null) tip.SetToolTip(cb, help);
            return cb;
        }

        private RoundButton AddBtn(CardPanel c, int col, int row, int span, string text, bool primary, string help) {
            RoundButton b = new RoundButton(S(6)) {
                Text = text, Font = fontBold, Dock = DockStyle.Fill, Primary = primary,
                Margin = ColPad(col, span, c.ColumnCount, 0, 0)
            };
            c.Controls.Add(b, col, row);
            if (span > 1) c.SetColumnSpan(b, span);
            if (help != null) tip.SetToolTip(b, help);
            return b;
        }

        private void SetTooltipIfChanged(Control c, string text) {
            if (!string.Equals(tip.GetToolTip(c), text, StringComparison.Ordinal)) tip.SetToolTip(c, text);
        }

        private void BuildUi() {
            Text = "Strix Controller  -  Strata";
            AutoScaleMode = AutoScaleMode.None;
            BackColor = T.BgCanvas; Font = fontUI; StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(S(920), S(640));
            MinimumSize = new Size(S(820), S(610));
            inputH = Math.Max(S(32), fontUI.Height + S(14));
            btnH = inputH + S(4);
            tip = new ToolTip { AutoPopDelay = 12000, InitialDelay = 250, ReshowDelay = 100 };

            outer = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = T.BgCanvas, ColumnCount = 2, RowCount = 5, Padding = new Padding(S(16), S(14), S(16), S(10)) };
            outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58f));
            outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(outer);

            // Row 0: Live Status Bar Card
            int stH = Math.Max(S(26), fontBold.Height + S(8));
            int pH = Math.Max(S(22), fontHead.Height + S(8));
            CardPanel cStatus = new CardPanel(S(9)) { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, RowCount = 1, Padding = new Padding(S(14), S(8), S(12), S(8)), Margin = new Padding(0, 0, 0, S(12)) };
            cStatus.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(22)));
            cStatus.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            cStatus.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            cStatus.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            cStatus.RowStyles.Add(new RowStyle(SizeType.Absolute, stH));
            outer.Controls.Add(cStatus, 0, 0); outer.SetColumnSpan(cStatus, 2);

            statusDot = new StatusDot(S(14), S(8), T.ColorWarn) { Dock = DockStyle.Fill, Margin = Padding.Empty };
            cStatus.Controls.Add(statusDot, 0, 0);

            lblStatus = new Label { Text = "Checking Strata backend...", Font = fontBold, ForeColor = T.FgMuted, BackColor = T.BgCard, Tag = "status", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Margin = new Padding(S(2), 0, S(8), 0) };
            cStatus.Controls.Add(lblStatus, 1, 0);
            tip.SetToolTip(lblStatus, "Live health check every 5s: Strata inference server, Docker sandbox, strix command, and omp command. Strata, Docker, and strix must be green to start.");

            pills = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = T.BgCard, Anchor = AnchorStyles.Right, Margin = Padding.Empty, Padding = Padding.Empty };
            pillDocker = new StatusPill("Docker", fontHead, pH, S(8), S(6)) { Margin = Padding.Empty };
            pillStrix = new StatusPill("strix", fontHead, pH, S(8), S(6)) { Margin = new Padding(S(6), 0, 0, 0) };
            pillOmp = new StatusPill("omp", fontHead, pH, S(8), S(6)) { Margin = new Padding(S(6), 0, 0, 0) };
            tip.SetToolTip(pillDocker, "Checking Docker CLI...");
            tip.SetToolTip(pillStrix, "Checking strix command...");
            tip.SetToolTip(pillOmp, "Checking optional omp command...");
            pills.Controls.AddRange(new Control[] { pillDocker, pillStrix, pillOmp });
            cStatus.Controls.Add(pills, 2, 0);

            btnTheme = new ThemeToggle(pH * 2 + S(6), pH) { IsLight = !T.IsDark, Anchor = AnchorStyles.Right, Margin = new Padding(S(10), 0, 0, 0) };
            cStatus.Controls.Add(btnTheme, 3, 0);
            tip.SetToolTip(btnTheme, T.IsDark ? "Switch to light mode" : "Switch to dark mode");

            // Row 1: Target & Scope (Left) + Limits & Mode (Right)
            RowStyle[] twoFieldRows = { new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Absolute, inputH), new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Absolute, inputH) };
            CardPanel cTarget = MakeCard(outer, 0, 1, 1, new Padding(0, 0, S(6), S(12)), false, 2, twoFieldRows);
            AddHead(cTarget, "TARGET & SCOPE", null);
            AddLbl(cTarget, "URL, IP, or local folder (one per scan)", 0, 1, 2, 0);
            txtTarget = AddInput(cTarget, 0, 2, 2, "http://localhost:8080", "URL, IP, domain, or local folder", "Where Strix points its agents: URL, IP, domain, or a local folder for white-box testing. localhost auto-rewrites to host.docker.internal so the Docker sandbox reaches Windows services.", false, out wrapTarget);
            AddLbl(cTarget, "Scan depth", 0, 3, 1, S(8));
            AddLbl(cTarget, "Scope mode", 1, 3, 1, S(8));
            cmbMode = AddCombo(cTarget, 0, 4, 1, new[] { "quick", "standard", "deep" }, 0, "quick: fast shallow pass, fewest tokens - practical on a local flash model. standard: balanced. deep: most thorough, longest run.");
            cmbScope = AddCombo(cTarget, 1, 4, 1, new[] { "auto", "diff", "full" }, 0, "auto: Strix decides. full: test everything. diff: only what changed since a base (git targets only).");

            CardPanel cLimits = MakeCard(outer, 1, 1, 1, new Padding(S(6), 0, 0, S(12)), false, 2, new[] {
                new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Absolute, inputH), new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Absolute, inputH)
            });
            AddHead(cLimits, "LIMITS & MODE", null);
            AddLbl(cLimits, "Max turns", 0, 1, 1, 0);
            AddLbl(cLimits, "Max budget (USD)", 1, 1, 1, 0);
            txtMaxTurns = AddInput(cLimits, 0, 2, 1, "500", "500", "Agent step budget; each step is one model call plus tool runs. Lower it (e.g. 100) to keep flash-model runs short. Strix default: 500.", false);
            txtMaxBudget = AddInput(cLimits, 1, 2, 1, "", "No cap", "Cost cap in USD. Irrelevant on local Strata inference (cost computes as ~0); set only for paid API models. Blank = no cap.", false);
            AddLbl(cLimits, "Execution mode", 0, 3, 2, S(8));
            chkHeadless = new ToggleCheck(S(36), S(20), S(10)) { Text = "Headless (no TUI chat)", Font = fontUI, Dock = DockStyle.Fill, Margin = Padding.Empty };
            cLimits.Controls.Add(chkHeadless, 0, 4); cLimits.SetColumnSpan(chkHeadless, 2);
            tip.SetToolTip(chkHeadless, "Off (recommended): Strix opens its interactive TUI in a new console so you can chat with and steer agents live. On: no chat window, scan runs to completion.");

            // Row 2: Agent Guidance (Stretches vertically on resize)
            CardPanel cGuide = MakeCard(outer, 0, 2, 2, new Padding(0, 0, 0, S(12)), true, 2, new[] { new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Percent, 100f) });
            cGuide.ColumnStyles[0] = new ColumnStyle(SizeType.AutoSize);
            cGuide.ColumnStyles[1] = new ColumnStyle(SizeType.Percent, 100f);
            lblGuideSub = AddHead(cGuide, "AGENT GUIDANCE", GuideNewSub);
            txtInstruction = AddInput(cGuide, 0, 1, 2, "", null, "Strix prepends this to every agent's task. Example: 'No DoS. Auth test only as guest. Focus on the /api endpoints.'", true);

            // Row 3: Companion Tools (Left) + Launch Scan (Right)
            CardPanel cTools = MakeCard(outer, 0, 3, 1, new Padding(0, 0, S(6), S(8)), false, 2, new[] {
                new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Absolute, inputH), new RowStyle(SizeType.Absolute, S(12)), new RowStyle(SizeType.Absolute, btnH)
            });
            AddHead(cTools, "COMPANION TOOLS", null);
            AddLbl(cTools, "omp workspace folder", 0, 1, 2, 0);
            txtOmpDir = AddInput(cTools, 0, 2, 2, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Project folder for omp TUI", "Project folder where the omp TUI opens. Type or paste a path; leave blank for your user folder.", false);
            btnOmp = AddBtn(cTools, 0, 4, 1, "Launch omp TUI", false, "Opens the oh-my-pi terminal agent in its own console in the workspace folder above so you can discuss blockers while Strix runs.");
            btnRuns = AddBtn(cTools, 1, 4, 1, "Open runs folder", false, "Opens the folder where finished scans are saved: runs\\<run-name> contains reports, findings JSON, and agent logs.");
            btnOmp.Enabled = false;

            cLaunch = MakeCard(outer, 1, 3, 1, new Padding(S(6), 0, 0, S(8)), false, 1, new[] {
                new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Absolute, inputH), new RowStyle(SizeType.Absolute, S(12)), new RowStyle(SizeType.Absolute, btnH)
            });
            AddHead(cLaunch, "LAUNCH SCAN", null);
            AddLbl(cLaunch, "Run session (or resume previous)", 0, 1, 1, 0);
            cmbResume = AddCombo(cLaunch, 0, 2, 1, null, 0, "(new scan) starts fresh. Picking a run name resumes it with prior state, and Guidance becomes a mid-scan steer message.");
            btnStart = AddBtn(cLaunch, 0, 4, 1, "Start Scan", true, "Launches strix in a new console window with STRIX_LLM / LLM_API_BASE pointed at Strata automatically. Console stays open after completion.");
            btnStart.Enabled = false;

            // Row 4: Footer tip
            Label footer = new Label { Text = "Hover any control for guidance - localhost auto-rewrites to host.docker.internal - Status refreshes every 5s", Font = fontLbl, ForeColor = T.FgDim, BackColor = T.BgCanvas, Tag = "canvas-dim", Dock = DockStyle.Fill, Height = fontLbl.Height + S(2), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Margin = new Padding(S(2), 0, 0, 0) };
            outer.Controls.Add(footer, 0, 4); outer.SetColumnSpan(footer, 2);

            btnTheme.Click += delegate { SetTheme(T.IsDark); };
            btnStart.Click += delegate { StartScan(); };
            btnOmp.Click += delegate { LaunchOmp(); };
            btnRuns.Click += delegate { OpenRunsFolder(); };
            cmbResume.DropDown += delegate { RefreshResumeList(); };
            cmbResume.SelectedIndexChanged += delegate { SyncResumeUi(); };
        }

        private void SetTheme(bool light) {
            UiTheme next = light ? UiTheme.Light : UiTheme.Dark;
            if (T == next) return;
            T = next;
            ApplyTheme();
        }

        private void ApplyTheme() {
            SuspendLayout();
            UpdateTitleBarTheme();
            BackColor = T.BgCanvas;
            outer.BackColor = T.BgCanvas;
            pills.BackColor = T.BgCard;
            btnTheme.IsLight = !T.IsDark;
            SetTooltipIfChanged(btnTheme, T.IsDark ? "Switch to light mode" : "Switch to dark mode");
            ApplyThemeRecursive(outer);
            SyncStatusColors();
            statusDot.Invalidate();
            ResumeLayout(true);
            Invalidate(true);
            Update();
        }

        private void ApplyThemeRecursive(Control parent) {
            foreach (Control c in parent.Controls) {
                InputWrap iw = c as InputWrap;
                if (iw != null) { iw.SyncTheme(); continue; }
                ThemedCombo tc = c as ThemedCombo;
                if (tc != null) { tc.SyncTheme(); continue; }
                ToggleCheck tg = c as ToggleCheck;
                if (tg != null) { tg.BackColor = T.BgCard; tg.Invalidate(); continue; }
                Label lbl = c as Label;
                if (lbl != null) {
                    string role = lbl.Tag as string;
                    if (role == "card-muted") { lbl.BackColor = T.BgCard; lbl.ForeColor = T.FgMuted; }
                    else if (role == "card-dim") { lbl.BackColor = T.BgCard; lbl.ForeColor = T.FgDim; }
                    else if (role == "canvas-dim") { lbl.BackColor = T.BgCanvas; lbl.ForeColor = T.FgDim; }
                    else if (role == "status") { lbl.BackColor = T.BgCard; }
                    continue;
                }
                if (c is CardPanel) { c.Invalidate(); ApplyThemeRecursive(c); continue; }
                if (c is RoundButton || c is StatusPill || c is ThemeToggle || c is StatusDot) { c.Invalidate(); continue; }
                if (c.HasChildren) ApplyThemeRecursive(c);
            }
        }

        private void SyncStatusColors() {
            Color nextDot = statusState == StatusState.Ready ? T.ColorOk : (statusState == StatusState.Error ? T.ColorErr : T.ColorWarn);
            Color nextFg = statusState == StatusState.Checking ? T.FgMuted : (statusState == StatusState.Ready ? T.FgText : (statusState == StatusState.Warn ? T.ColorWarn : T.ColorErr));
            if (lblStatus.ForeColor != nextFg) lblStatus.ForeColor = nextFg;
            statusDot.SetColor(nextDot);
        }

        private void SyncResumeUi() {
            bool resuming = cmbResume.SelectedIndex > 0;
            if (txtTarget.ReadOnly != resuming) {
                txtTarget.ReadOnly = resuming;
                wrapTarget.SyncTheme();
            }
            string cta = resuming ? "Resume Scan" : "Start Scan";
            if (btnStart.Text != cta) btnStart.Text = cta;
            string sub = resuming ? GuideResumeSub : GuideNewSub;
            if (lblGuideSub != null && lblGuideSub.Text != sub) lblGuideSub.Text = sub;
        }

        private static void SetupEnvironmentPath() {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> list = new List<string>();
            string[] sources = {
                Path.Combine(home, @".local\bin"), Path.Combine(home, @".bun\bin"), Path.Combine(pf, @"Docker\Docker\resources\bin"),
                Environment.GetEnvironmentVariable("PATH"), Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User), Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine)
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
            foreach (string dir in pathEnv.Split(';')) {
                string trimmed = (dir ?? "").Trim().Trim('"');
                if (string.IsNullOrEmpty(trimmed)) continue;
                try {
                    foreach (string ext in exts) {
                        string candidate = Path.Combine(trimmed, command + ext.Trim());
                        if (File.Exists(candidate)) return candidate;
                    }
                } catch { }
            }
            return null;
        }

        private async Task<string> QueryServedModelAsync(CancellationToken token) {
            try {
                using (HttpResponseMessage resp = await http.GetAsync(ModelsUrl, token)) {
                    if (!resp.IsSuccessStatusCode) return "";
                    string body = await resp.Content.ReadAsStringAsync();
                    Match m = Regex.Match(body, "\"id\"\\s*:\\s*\"([^\"]+)\"");
                    return m.Success ? m.Groups[1].Value : DefaultModel;
                }
            } catch {
                return "";
            }
        }

        private async void CheckBackend() {
            if (checking || closing || IsDisposed) return;
            checking = true;
            statusTimer.Enabled = false;
            try {
                CancellationToken token = cts.Token;
                string curDocker = dockerPath, curStrix = strixPath, curOmp = ompPath;
                Task<string[]> toolsTask = Task.Run(delegate {
                    string d = (curDocker != null && File.Exists(curDocker)) ? curDocker : FindCommandOnPath("docker");
                    string s = (curStrix != null && File.Exists(curStrix)) ? curStrix : FindCommandOnPath("strix");
                    string o = (curOmp != null && File.Exists(curOmp)) ? curOmp : FindCommandOnPath("omp");
                    return new[] { d, s, o };
                }, token);
                Task<string> modelTask = QueryServedModelAsync(token);
                await Task.WhenAll(toolsTask, modelTask);
                if (closing || IsDisposed || !IsHandleCreated) return;

                string[] tools = toolsTask.Result;
                dockerPath = tools[0]; strixPath = tools[1]; ompPath = tools[2];
                servedModelId = modelTask.Result;

                bool strataOk = servedModelId != "", dockerOk = dockerPath != null, strixOk = strixPath != null, ompOk = ompPath != null;
                bool ready = strataOk && dockerOk && strixOk;
                string msg = ready
                    ? ("Strata serving  " + servedModelId)
                    : (!strataOk ? "Strata offline (127.0.0.1:8080) - start Strata Controller" : ("Strata serving " + servedModelId + " - missing required tool(s)"));
                if (lblStatus.Text != msg) lblStatus.Text = msg;
                SetTooltipIfChanged(lblStatus, msg + "\r\nLive health check every 5s: Strata, Docker, and strix must be green to start.");

                StatusState nextState = ready ? StatusState.Ready : (strataOk ? StatusState.Warn : StatusState.Error);
                if (statusState != nextState) { statusState = nextState; SyncStatusColors(); }

                pillDocker.SetState(dockerOk ? PillState.Ok : PillState.Error);
                pillStrix.SetState(strixOk ? PillState.Ok : PillState.Error);
                pillOmp.SetState(ompOk ? PillState.Ok : PillState.Warn);
                SetTooltipIfChanged(pillDocker, dockerOk ? "Docker CLI: " + dockerPath : "Docker CLI missing from PATH (required for Strix sandbox)");
                SetTooltipIfChanged(pillStrix, strixOk ? "strix: " + strixPath : "strix not found in PATH or .local\\bin (required)");
                SetTooltipIfChanged(pillOmp, ompOk ? "omp: " + ompPath : "omp not found on PATH (optional companion TUI)");

                if (btnStart.Enabled != ready) btnStart.Enabled = ready;
                if (btnOmp.Enabled != ompOk) btnOmp.Enabled = ompOk;
                cLaunch.SetReady(ready);
            } catch (OperationCanceledException) {
            } catch (Exception ex) {
                Program.Log("CheckBackend: " + ex);
            } finally {
                checking = false;
                if (!closing && !IsDisposed) statusTimer.Enabled = true;
            }
        }

        private void RefreshResumeList() {
            string prev = cmbResume.SelectedItem != null ? cmbResume.SelectedItem.ToString() : null;
            List<string> items = new List<string> { "(new scan)" };
            int maxW = cmbResume.Width;
            try {
                if (Directory.Exists(runsDir)) {
                    List<DirectoryInfo> dirs = new List<DirectoryInfo>(new DirectoryInfo(runsDir).EnumerateDirectories());
                    dirs.Sort(delegate(DirectoryInfo a, DirectoryInfo b) {
                        int cmp = b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc);
                        return cmp != 0 ? cmp : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
                    });
                    foreach (DirectoryInfo di in dirs) {
                        items.Add(di.Name);
                        int w = TextRenderer.MeasureText(di.Name, fontUI, Size.Empty, TextFormatFlags.NoPrefix).Width + S(28);
                        if (w > maxW) maxW = w;
                    }
                }
            } catch { }
            cmbResume.DropDownWidth = maxW;
            cmbResume.SetItems(items, prev);
            SyncResumeUi();
        }

        private bool EnsureRunsDir() {
            try { Directory.CreateDirectory(runsDir); return true; }
            catch (Exception ex) {
                Program.Log("EnsureRunsDir: " + ex);
                MessageBox.Show(this, "Could not create runs folder: " + ex.Message, "Folder Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private static string QuoteCliArg(string value) {
            string clean = Regex.Replace((value ?? "").Replace("\"", "'"), "\\s*\\r?\\n\\s*", " ");
            clean = Regex.Replace(clean, @"(\\+)$", "$1$1");
            return "\"" + clean + "\"";
        }

        private void StartScan() {
            string target = txtTarget.Text.Trim().Trim('"');
            string resume = cmbResume.SelectedIndex > 0 ? cmbResume.SelectedItem.ToString() : "";
            if (strixPath == null) return;
            if (resume == "" && target.Length == 0) {
                MessageBox.Show(this, "Enter a target, or pick a run to resume.", "Missing Input", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtTarget.Focus();
                return;
            }
            if (resume == "" && target.IndexOf('"') >= 0) {
                MessageBox.Show(this, "Target cannot contain embedded double quotes.", "Invalid Input", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtTarget.Focus();
                return;
            }

            int turns = 500;
            string turnsRaw = txtMaxTurns.Text.Trim();
            if (turnsRaw.Length > 0 && (!int.TryParse(turnsRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out turns) || turns <= 0)) {
                MessageBox.Show(this, "Max turns must be a positive whole number (e.g. 100 or 500).", "Invalid Input", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtMaxTurns.Focus();
                return;
            }

            string budgetRaw = txtMaxBudget.Text.Trim(), budgetArg = "";
            decimal budgetVal;
            if (budgetRaw.Length > 0) {
                if (!decimal.TryParse(budgetRaw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out budgetVal) || budgetVal <= 0m) {
                    MessageBox.Show(this, "Max budget must be a positive number like 5 or 5.50.", "Invalid Input", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    txtMaxBudget.Focus();
                    return;
                }
                budgetArg = " --max-budget " + budgetVal.ToString(CultureInfo.InvariantCulture);
            }

            if (!EnsureRunsDir()) return;

            string model = servedModelId != "" ? servedModelId : DefaultModel;
            string mode = cmbMode.SelectedItem != null ? cmbMode.SelectedItem.ToString() : "quick";
            string scope = cmbScope.SelectedIndex > 0 ? " --scope-mode " + cmbScope.SelectedItem : "";
            string instruction = txtInstruction.Text.Trim();
            string args = (resume != "" ? ("--resume " + QuoteCliArg(resume)) : ("--target " + QuoteCliArg(target)))
                + " -m " + mode + scope + " --max-turns " + turns + budgetArg;
            if (instruction.Length > 0) args += " --instruction " + QuoteCliArg(instruction);
            if (chkHeadless.Checked) args += " -n";

            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", string.Format("/k \"{0} {1}\"", QuoteCliArg(strixPath), args)) {
                UseShellExecute = false, CreateNoWindow = false, WorkingDirectory = runsDir
            };
            psi.EnvironmentVariables["STRIX_LLM"] = "openai/" + model;
            psi.EnvironmentVariables["LLM_API_BASE"] = ApiBase;
            psi.EnvironmentVariables["LLM_API_KEY"] = "strata";
            try {
                using (Process.Start(psi)) { }
            } catch (Exception ex) {
                Program.Log("StartScan: " + ex);
                MessageBox.Show(this, ex.Message, "Launch Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LaunchOmp() {
            Program.Log("LaunchOmp: enter, ompPath=" + (ompPath ?? "<null>"));
            if (ompPath == null) { MessageBox.Show(this, "'omp' was not found on PATH.", "Missing Tool", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            string dir = txtOmpDir.Text.Trim().Trim('"');
            if (dir.Length == 0) dir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!Directory.Exists(dir)) {
                MessageBox.Show(this, "omp workspace folder does not exist:\r\n" + dir, "Invalid Folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtOmpDir.Focus();
                return;
            }
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", string.Format("/k \"{0}\"", QuoteCliArg(ompPath))) {
                UseShellExecute = false, CreateNoWindow = false, WorkingDirectory = dir
            };
            try {
                using (Process.Start(psi)) { }
                Program.Log("LaunchOmp: started omp in " + dir);
            } catch (Exception ex) {
                Program.Log("LaunchOmp: launch failed: " + ex);
                MessageBox.Show(this, ex.Message, "Launch Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenRunsFolder() {
            if (!EnsureRunsDir()) return;
            try {
                using (Process.Start(new ProcessStartInfo(runsDir) { UseShellExecute = true })) { }
            } catch (Exception ex) {
                Program.Log("OpenRunsFolder: " + ex);
                MessageBox.Show(this, ex.Message, "Open Folder Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}