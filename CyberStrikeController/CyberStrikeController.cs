using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("CyberStrike Controller")]
[assembly: System.Reflection.AssemblyDescription("Launch CyberStrike pentest agent from the local repo with Strata inference")]
[assembly: System.Reflection.AssemblyProduct("CyberStrike Controller")]
[assembly: System.Reflection.AssemblyConfiguration("Release")]
[assembly: System.Reflection.AssemblyCompany("Phenomite")]
[assembly: System.Reflection.AssemblyCopyright("Copyright (c) 2026 Phenomite")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("1.0.0")]
[assembly: System.Runtime.InteropServices.ComVisible(false)]


namespace CyberStrikeControllerApp {
    internal static class Program {
        public static string DebugLogPath = Path.Combine(Path.GetTempPath(), "CyberStrikeController.debug.log");

        public static void Log(string msg) {
            try { File.AppendAllText(DebugLogPath, string.Format("[{0:yyyy-MM-dd HH:mm:ss.fff}] {1}\r\n", DateTime.Now, msg)); } catch { }
        }

        private static void HandleError(Exception ex) {
            Log(ex.ToString());
            MessageBox.Show(ex.Message, "CyberStrike Controller Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        [STAThread]
        private static void Main() {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { HandleError(e.Exception); };
            try { Application.Run(new CyberStrikeControllerForm()); } catch (Exception ex) { HandleError(ex); }
        }
    }

    internal sealed class CyberStrikeControllerForm : Form {
        private const string ModelsUrl = "http://127.0.0.1:8080/v1/models", RepoDir = @"C:\Users\Bob\Documents\GitHub\CyberStrike";

        private enum StatusState { Checking, Ready, Warn, Error }

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
                FgText = Color.FromArgb(238, 240, 246), FgMuted = Color.FromArgb(148, 156, 176), FgDim = Color.FromArgb(104, 112, 132),
                ColorOk = Color.FromArgb(63, 205, 110), ColorWarn = Color.FromArgb(242, 186, 66), ColorErr = Color.FromArgb(240, 82, 82),
                ToggleTrack = Color.FromArgb(19, 20, 26), ToggleKnobOff = Color.FromArgb(148, 156, 176),
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
                FgText = Color.FromArgb(24, 28, 38), FgMuted = Color.FromArgb(88, 96, 118), FgDim = Color.FromArgb(128, 136, 156),
                ColorOk = Color.FromArgb(26, 148, 72), ColorWarn = Color.FromArgb(186, 118, 12), ColorErr = Color.FromArgb(206, 46, 46),
                ToggleTrack = Color.FromArgb(210, 215, 228), ToggleKnobOff = Color.FromArgb(255, 255, 255),
                ThemeTrack = Color.FromArgb(232, 236, 244), ThemeThumb = Color.FromArgb(255, 255, 255), ThemeIconActive = Color.FromArgb(214, 134, 12),
                PillOkBg = Color.FromArgb(232, 247, 237), PillOkBd = Color.FromArgb(156, 218, 178), PillOkFg = Color.FromArgb(20, 102, 52),
                PillErrBg = Color.FromArgb(253, 236, 237), PillErrBd = Color.FromArgb(240, 178, 180), PillErrFg = Color.FromArgb(158, 34, 38),
                PillWarnBg = Color.FromArgb(254, 245, 227), PillWarnBd = Color.FromArgb(234, 200, 132), PillWarnFg = Color.FromArgb(136, 84, 10)
            };
        }

        private static UiTheme T = UiTheme.Dark;

        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int sz);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

        private readonly HttpClient http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromMilliseconds(500) };
        private readonly Font fontUI = new Font("Segoe UI", 9.5f), fontBold = new Font("Segoe UI Semibold", 9.5f);
        private readonly Font fontLbl = new Font("Segoe UI", 8.75f), fontHead = new Font("Segoe UI Semibold", 8f);
        private ToolTip tip;
        private TableLayoutPanel outer;
        private FlowLayoutPanel pills;
        private TextBox txtProject, txtPrompt, txtModel, txtAgent, txtPort;
        private ThemedCombo cmbMode;
        private ToggleCheck chkContinue;
        private RoundButton btnLaunch, btnRepo, btnWeb;
        private ThemeToggle btnTheme;
        private CardPanel cLaunch, cTarget, cModel, cPrompt;
        private Panel statusDot;
        private StatusPill pillBun, pillRepo, pillStrata;
        private Label lblStatus;
        private System.Windows.Forms.Timer statusTimer;
        private StatusState statusState = StatusState.Checking;
        private Color dotColor;
        private string servedModelId = "", bunPath;
        private int inputH, btnH;
        private bool checking;

        public CyberStrikeControllerForm() {
            dotColor = T.ColorWarn;
            SetupEnvironmentPath();
            bunPath = FindCommandOnPath("bun");
            DoubleBuffered = true;
            BuildUi();
            statusTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            statusTimer.Tick += delegate { CheckBackend(); };
        }

        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);
            UpdateTitleBarTheme();
        }

        protected override void OnShown(EventArgs e) {
            base.OnShown(e);
            statusTimer.Start();
            CheckBackend();
        }

        protected override void OnFormClosed(FormClosedEventArgs e) {
            base.OnFormClosed(e);
            statusTimer.Stop(); statusTimer.Dispose(); http.Dispose(); tip.Dispose();
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
            public bool ReadyHighlight;
            public float Rad;
            public CardPanel(float rad) { Rad = rad; DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); }
            protected override void OnPaintBackground(PaintEventArgs e) {
                if (Width <= 2 || Height <= 2) return;
                e.Graphics.Clear(T.BgCanvas);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath p = RoundPath(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Rad)) {
                    using (SolidBrush b = new SolidBrush(T.BgCard)) e.Graphics.FillPath(b, p);
                    using (Pen pen = new Pen(ReadyHighlight ? T.BorderReady : T.BorderCard, 1f)) e.Graphics.DrawPath(pen, p);
                }
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

        // Combobox with dark/light theme and custom dropdown list
        private sealed class ThemedCombo : Control {
            private sealed class DropRenderer : ToolStripRenderer {
                protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) {
                    using (SolidBrush b = new SolidBrush(T.BorderInput)) e.Graphics.FillRectangle(b, e.AffectedBounds);
                }
                protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
            }

            private readonly float rad;
            private readonly int chev;
            private readonly ListBox list;
            private readonly ToolStripControlHost host;
            private readonly ToolStripDropDown drop;
            private int itemH = 26, selectedIndex = -1, hoverIdx = -1, closedTick;
            private bool hover;

            public event EventHandler DropDown, SelectedIndexChanged;
            public int DropDownWidth;
            public ListBox.ObjectCollection Items { get { return list.Items; } }
            public int ItemHeight { get { return itemH; } set { itemH = Math.Max(16, value); Height = itemH + 6; } }
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

            public ThemedCombo(float r, int chevSz) {
                rad = r; chev = chevSz; TabStop = true; Cursor = Cursors.Hand; BackColor = T.BgCard; ForeColor = T.FgText;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                list = new ListBox { BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, IntegralHeight = false, BackColor = T.BgDrop, ForeColor = T.FgText, Cursor = Cursors.Hand };
                list.DrawItem += OnListDrawItem;
                list.MouseMove += delegate(object s, MouseEventArgs e) {
                    int i = list.IndexFromPoint(e.Location);
                    if (i != hoverIdx) { hoverIdx = i; if (i >= 0) list.SelectedIndex = i; list.Invalidate(); }
                };
                list.MouseLeave += delegate { if (hoverIdx != -1) { hoverIdx = -1; list.Invalidate(); } };
                list.MouseUp += delegate(object s, MouseEventArgs e) {
                    if (e.Button != MouseButtons.Left || !list.ClientRectangle.Contains(e.Location)) return;
                    int i = list.IndexFromPoint(e.Location);
                    if (i >= 0) { SelectedIndex = i; drop.Close(); Focus(); }
                };
                list.KeyDown += delegate(object s, KeyEventArgs e) {
                    if (e.KeyCode == Keys.Enter) { if (list.SelectedIndex >= 0) SelectedIndex = list.SelectedIndex; drop.Close(); Focus(); e.Handled = true; }
                    else if (e.KeyCode == Keys.Escape) { drop.Close(); Focus(); e.Handled = true; }
                };
                host = new ToolStripControlHost(list) { AutoSize = false, Margin = Padding.Empty, Padding = Padding.Empty };
                drop = new ToolStripDropDown { AutoSize = false, Padding = new Padding(1), Margin = Padding.Empty, Renderer = new DropRenderer() };
                drop.Items.Add(host);
                drop.Closed += delegate { closedTick = Environment.TickCount; Invalidate(); };
            }

            public void SyncTheme() {
                BackColor = T.BgCard; ForeColor = T.FgText;
                list.BackColor = T.BgDrop; list.ForeColor = T.FgText;
                Invalidate();
            }

            public void BeginUpdate() { list.BeginUpdate(); }
            public void EndUpdate() { list.EndUpdate(); Invalidate(); }

            private void ToggleDrop() {
                if (drop.Visible) { drop.Close(); return; }
                if (DropDown != null) DropDown(this, EventArgs.Empty);
                if (list.Items.Count == 0) return;
                list.Font = Font;
                list.BackColor = T.BgDrop;
                list.ForeColor = T.FgText;
                int rowH = Math.Max(Font.Height + 10, itemH), popW = Math.Max(Width, DropDownWidth), popH = Math.Min(list.Items.Count, 10) * rowH;
                list.ItemHeight = rowH;
                list.Size = host.Size = new Size(popW - 2, popH);
                drop.Size = new Size(popW, popH + 2);
                hoverIdx = selectedIndex;
                list.SelectedIndex = (selectedIndex >= 0 && selectedIndex < list.Items.Count) ? selectedIndex : -1;
                drop.Show(this, 0, Height + 2);
                list.Focus();
                Invalidate();
            }

            private void OnListDrawItem(object s, DrawItemEventArgs e) {
                if (e.Index < 0 || e.Index >= list.Items.Count) return;
                bool hot = e.Index == hoverIdx || (hoverIdx < 0 && e.Index == selectedIndex);
                using (SolidBrush b = new SolidBrush(hot ? T.BgHover : T.BgDrop)) e.Graphics.FillRectangle(b, e.Bounds);
                Rectangle tr = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, Math.Max(1, e.Bounds.Width - 12), e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, (list.Items[e.Index] ?? "").ToString(), Font, tr, T.FgText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }

            protected override bool IsInputKey(Keys keyData) {
                return keyData == Keys.Up || keyData == Keys.Down || keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData);
            }
            protected override void OnKeyDown(KeyEventArgs e) {
                base.OnKeyDown(e);
                if ((e.Alt && e.KeyCode == Keys.Down) || e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter || e.KeyCode == Keys.F4) { ToggleDrop(); e.Handled = true; }
                else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Right) { if (selectedIndex < list.Items.Count - 1) SelectedIndex = selectedIndex + 1; e.Handled = true; }
                else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Left) { if (selectedIndex > 0) SelectedIndex = selectedIndex - 1; e.Handled = true; }
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
                int padX = Math.Max(8, chev * 2), arrowW = Math.Max(22, chev * 6), cx = Width - arrowW / 2, cy = Height / 2, dy = drop.Visible ? -chev / 2 : chev / 2;
                string txt = SelectedItem != null ? SelectedItem.ToString() : "";
                TextRenderer.DrawText(g, txt, Font, new Rectangle(padX, 0, Math.Max(1, Width - padX - arrowW), Height), Enabled ? T.FgText : T.FgDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
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
                e.Graphics.Clear(T.BgCard);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Color fill = Primary ? T.Green : T.BgBtn;
                Color hoverFill = Primary ? T.GreenHover : T.BgHover;
                Color pressFill = Primary ? T.GreenPressed : T.BgPressed;
                Color border = Primary ? T.GreenBorder : T.BorderInput;
                Color bg = !Enabled ? T.BgDisabled : (down ? pressFill : (hover ? hoverFill : fill));
                Color bd = !Enabled ? T.BorderCard : (Focused ? T.Accent : border);
                Color fg = !Enabled ? T.FgDim : (Primary ? Color.White : T.FgText);
                using (GraphicsPath p = RoundPath(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), rad)) {
                    using (SolidBrush b = new SolidBrush(bg)) e.Graphics.FillPath(b, p);
                    using (Pen pen = new Pen(bd, 1f)) e.Graphics.DrawPath(pen, p);
                }
                TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        private sealed class ToggleCheck : CheckBox {
            private readonly int tw, th, gap;
            public ToggleCheck(int w, int h, int g) {
                tw = w; th = h; gap = g; AutoSize = false; Cursor = Cursors.Hand; BackColor = T.BgCard;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }
            protected override void OnPaint(PaintEventArgs e) {
                if (Width <= 2 || Height <= 2) return;
                Graphics g = e.Graphics;
                g.Clear(T.BgCard);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int ty = (Height - th) / 2, pad = Math.Max(3, th / 6), d = th - pad * 2;
                using (GraphicsPath p = RoundPath(new RectangleF(0.5f, ty + 0.5f, tw - 1f, th - 1f), th / 2f)) {
                    using (SolidBrush b = new SolidBrush(Checked ? T.Accent : T.ToggleTrack)) g.FillPath(b, p);
                    using (Pen pen = new Pen(Checked || Focused ? T.Accent : T.BorderInput, 1f)) g.DrawPath(pen, p);
                }
                using (SolidBrush tb = new SolidBrush(Checked ? Color.White : T.ToggleKnobOff))
                    g.FillEllipse(tb, Checked ? tw - d - pad : pad, ty + pad, d, d);
                TextRenderer.DrawText(g, Text, Font, new Rectangle(tw + gap, 0, Math.Max(1, Width - tw - gap), Height), T.FgText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
        }

        private sealed class StatusPill : Control {
            private bool ok, req = true, init;
            private readonly int px, ds;
            public StatusPill(string text, Font f, int h, int padX, int dotSz) {
                Text = text; Font = f; px = padX; ds = dotSz; Height = h;
                Width = TextRenderer.MeasureText(text, f).Width + padX * 2 + dotSz + padX / 2;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                Margin = new Padding(dotSz, 0, 0, 0);
            }
            public void SetState(bool isOk, bool isReq) { if (init && ok == isOk && req == isReq) return; init = true; ok = isOk; req = isReq; Invalidate(); }
            protected override void OnPaint(PaintEventArgs e) {
                if (Width <= 2 || Height <= 2) return;
                Graphics g = e.Graphics; g.Clear(T.BgCard); g.SmoothingMode = SmoothingMode.AntiAlias;
                Color bg = ok ? T.PillOkBg : (req ? T.PillErrBg : T.PillWarnBg);
                Color bd = ok ? T.PillOkBd : (req ? T.PillErrBd : T.PillWarnBd);
                Color dt = ok ? T.ColorOk : (req ? T.ColorErr : T.ColorWarn);
                Color fg = ok ? T.PillOkFg : (req ? T.PillErrFg : T.PillWarnFg);
                using (GraphicsPath p = RoundPath(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Height / 2f)) {
                    using (SolidBrush b = new SolidBrush(bg)) e.Graphics.FillPath(b, p);
                    using (Pen pen = new Pen(bd, 1f)) e.Graphics.DrawPath(pen, p);
                }
                using (SolidBrush db = new SolidBrush(dt)) g.FillEllipse(db, px, (Height - ds) / 2f, ds, ds);
                int tx = px + ds + px / 2;
                TextRenderer.DrawText(g, Text, Font, new Rectangle(tx, 0, Width - tx - px / 2, Height), fg, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        private sealed class ThemeToggle : Button {
            private bool isLight, hover;
            public bool IsLight { get { return isLight; } set { if (isLight == value) return; isLight = value; Invalidate(); } }
            public ThemeToggle(int w, int h) {
                Size = new Size(w, h); Cursor = Cursors.Hand; Text = ""; AccessibleName = "Toggle light or dark mode";
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }
            protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
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
                using (GraphicsPath thumb = RoundPath(new RectangleF(tx, pad, tw, th), th / 2f)) {
                    using (SolidBrush hb = new SolidBrush(T.ThemeThumb)) g.FillPath(hb, thumb);
                    using (Pen hp = new Pen(T.BorderInput, 1f)) g.DrawPath(hp, thumb);
                }
                float cy = Height / 2f, lx = pad + tw / 2f, rx = Width - pad - tw / 2f, r = Math.Max(3.2f, th * 0.27f);
                Color moonFg = !isLight ? T.ThemeIconActive : T.FgDim;
                Color moonBg = !isLight ? T.ThemeThumb : T.ThemeTrack;
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

        private void AddHead(CardPanel c, string title, string sub) {
            Label h = new Label { Text = title, Font = fontHead, ForeColor = T.FgMuted, BackColor = T.BgCard, Tag = "card-muted", AutoSize = true, Margin = new Padding(0, 0, 0, S(8)) };
            c.Controls.Add(h, 0, 0);
            if (sub == null) { if (c.ColumnCount > 1) c.SetColumnSpan(h, c.ColumnCount); return; }
            Label s = new Label { Text = sub, Font = fontLbl, ForeColor = T.FgDim, BackColor = T.BgCard, Tag = "card-dim", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, AutoEllipsis = true, AutoSize = false };
            c.Controls.Add(s, 1, 0);
            if (c.ColumnCount > 2) c.SetColumnSpan(s, c.ColumnCount - 1);
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
            ThemedCombo cb = new ThemedCombo(S(6), S(4)) { Font = fontUI, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = ColPad(col, span, c.ColumnCount, 0, 0) };
            cb.ItemHeight = Math.Max(fontUI.Height + S(4), inputH - 6);
            if (cb.Height > inputH) cb.ItemHeight = Math.Max(fontUI.Height + 2, cb.ItemHeight - (cb.Height - inputH));
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

        private void BuildUi() {
            Text = "CyberStrike Controller  -  Strata";
            AutoScaleMode = AutoScaleMode.None;
            BackColor = T.BgCanvas; Font = fontUI; StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(S(900), S(640));
            MinimumSize = new Size(S(780), S(600));
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
            CardPanel cStatus = new CardPanel(S(9)) { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, RowCount = 1, Padding = new Padding(S(14), S(8), S(12), S(8)), Margin = new Padding(0, 0, 0, S(12)) };
            cStatus.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(22)));
            cStatus.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            cStatus.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            cStatus.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            cStatus.RowStyles.Add(new RowStyle(SizeType.Absolute, stH));
            outer.Controls.Add(cStatus, 0, 0); outer.SetColumnSpan(cStatus, 2);

            statusDot = new Panel { Dock = DockStyle.Fill, BackColor = T.BgCard, Margin = Padding.Empty };
            statusDot.Paint += delegate(object s, PaintEventArgs pe) {
                pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                int d1 = S(14), d2 = S(8);
                using (SolidBrush b1 = new SolidBrush(Color.FromArgb(55, dotColor))) pe.Graphics.FillEllipse(b1, (statusDot.Width - d1) / 2f, (statusDot.Height - d1) / 2f, d1, d1);
                using (SolidBrush b2 = new SolidBrush(dotColor)) pe.Graphics.FillEllipse(b2, (statusDot.Width - d2) / 2f, (statusDot.Height - d2) / 2f, d2, d2);
            };
            cStatus.Controls.Add(statusDot, 0, 0);

            lblStatus = new Label { Text = "Checking CyberStrike stack...", Font = fontBold, ForeColor = T.FgMuted, BackColor = T.BgCard, Tag = "status", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, AutoSize = false, Margin = new Padding(S(4), 0, S(10), 0) };
            cStatus.Controls.Add(lblStatus, 1, 0);
            tip.SetToolTip(lblStatus, "Live health check every 5s: bun command, CyberStrike repo with installed deps, and Strata inference server. Bun and the repo must be green to launch.");

            pills = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = T.BgCard, Anchor = AnchorStyles.Right, Margin = Padding.Empty, Padding = Padding.Empty };
            int pH = Math.Max(S(22), fontHead.Height + S(8));
            pillBun = new StatusPill("bun", fontHead, pH, S(8), S(6));
            pillRepo = new StatusPill("cyberstrike", fontHead, pH, S(8), S(6));
            pillStrata = new StatusPill("Strata", fontHead, pH, S(8), S(6));
            pills.Controls.AddRange(new Control[] { pillBun, pillRepo, pillStrata });
            cStatus.Controls.Add(pills, 2, 0);

            btnTheme = new ThemeToggle(pH * 2 + S(6), pH) { IsLight = !T.IsDark, Anchor = AnchorStyles.Right, Margin = new Padding(S(10), 0, 0, 0) };
            cStatus.Controls.Add(btnTheme, 3, 0);
            tip.SetToolTip(btnTheme, T.IsDark ? "Switch to light mode" : "Switch to dark mode");

            // Row 1: Target & Mode (Left) + Model & Options (Right)
            RowStyle[] twoFieldRows = { new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Absolute, inputH), new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Absolute, inputH) };
            cTarget = MakeCard(outer, 0, 1, 1, new Padding(0, 0, S(6), S(12)), false, 2, twoFieldRows);
            AddHead(cTarget, "TARGET & MODE", "workspace; target URL/IP in the prompt");
            AddLbl(cTarget, "Project folder", 0, 1, 2, 0);
            InputWrap dummy;
            txtProject = AddInput(cTarget, 0, 2, 2, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "C:\\path\\to\\project", "Workspace folder where the agent runs and stores its session. CyberStrike can attack any target (URL, IP, domain, or codebase) - name the actual target in the PROMPT below; this folder is only where it works.", false, out dummy);
            AddLbl(cTarget, "Launch mode", 0, 3, 1, S(8));
            AddLbl(cTarget, "Agent", 1, 3, 1, S(8));
            cmbMode = AddCombo(cTarget, 0, 4, 1, new[] { "TUI (interactive)", "Run (headless)", "Web UI" }, 0, "TUI: full interactive agent in a console. Run: one headless scan that prints the result and exits. Web UI: starts the server and opens the browser dashboard.");
            txtAgent = AddInput(cTarget, 1, 4, 1, "", "default", "CyberStrike agent to use (e.g. pentest). Blank = the default agent. Run 'cyberstrike agent' to list them.", false, out dummy);

            cModel = MakeCard(outer, 1, 1, 1, new Padding(S(6), 0, 0, S(12)), false, 2, new[] {
                new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Absolute, inputH), new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Absolute, inputH)
            });
            AddHead(cModel, "MODEL & OPTIONS", null);
            AddLbl(cModel, "Model", 0, 1, 2, 0);
            txtModel = AddInput(cModel, 0, 2, 2, "", "openai/qwen3.8-flash-next-iq3_s", "Overrides the default model for this launch. Format: provider/model as shown by 'cyberstrike models'. Blank = CyberStrike's configured default.", false, out dummy);
            AddLbl(cModel, "Server port", 0, 3, 1, S(8));
            AddLbl(cModel, "Session", 1, 3, 1, S(8));
            txtPort = AddInput(cModel, 0, 4, 1, "4096", "4096", "Port for the built-in server (Web UI and headless runs). Change only if 4096 is taken.", false, out dummy);
            chkContinue = new ToggleCheck(S(36), S(20), S(10)) { Text = "Continue", Font = fontUI, Dock = DockStyle.Fill, Margin = ColPad(1, 1, 2, 0, 0) };
            cModel.Controls.Add(chkContinue, 1, 4);
            tip.SetToolTip(chkContinue, "Continue the most recent session for the project folder instead of starting fresh (-c).");

            // Row 2: Prompt (stretches vertically on resize)
            cPrompt = MakeCard(outer, 0, 2, 2, new Padding(0, 0, 0, S(12)), true, 2, new[] { new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Percent, 100f) });
            cPrompt.ColumnStyles[0] = new ColumnStyle(SizeType.AutoSize);
            cPrompt.ColumnStyles[1] = new ColumnStyle(SizeType.Percent, 100f);
            AddHead(cPrompt, "PROMPT", "Required for Run mode; sent as initial prompt in TUI mode");
            txtPrompt = AddInput(cPrompt, 0, 1, 2, "", "e.g. Pentest http://target-or-IP: recon, OWASP Top 10, report findings", "The task for the agent, including the target to attack (URL, IP, domain, or local app). Run mode requires it; TUI mode starts the chat with it pre-filled; Web UI ignores it.", true, out dummy);

            // Row 3: Companion Tools (Left) + Launch (Right)
            CardPanel cTools = MakeCard(outer, 0, 3, 1, new Padding(0, 0, S(6), S(8)), false, 2, new[] {
                new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Absolute, btnH)
            });
            AddHead(cTools, "SHORTCUTS", null);
            AddLbl(cTools, "Open the repo or a running dashboard", 0, 1, 2, 0);
            btnRepo = AddBtn(cTools, 0, 2, 1, "Open repo folder", false, "Opens the CyberStrike source repo in Explorer: " + RepoDir);
            btnWeb = AddBtn(cTools, 1, 2, 1, "Open web dashboard", false, "Opens http://127.0.0.1:<port> in your browser. Needs a running server: use Web UI mode, or 'cyberstrike serve'.");

            cLaunch = MakeCard(outer, 1, 3, 1, new Padding(S(6), 0, 0, S(8)), false, 1, new[] {
                new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Absolute, btnH)
            });
            AddHead(cLaunch, "LAUNCH", null);
            AddLbl(cLaunch, "Green status required", 0, 1, 1, 0);
            btnLaunch = AddBtn(cLaunch, 0, 2, 1, "Launch TUI", true, "Opens a console running the CyberStrike dev CLI from the repo via bun, with the options above applied. The console stays open after the run.");

            // Row 4: Footer tip
            Label footer = new Label { Text = "Hover any control for guidance - CLI runs from the repo via bun (dev build) - Status refreshes every 5s", Font = fontLbl, ForeColor = T.FgDim, BackColor = T.BgCanvas, Tag = "canvas-dim", AutoSize = true, Margin = new Padding(S(2), S(2), 0, 0) };
            outer.Controls.Add(footer, 0, 4); outer.SetColumnSpan(footer, 2);

            btnTheme.Click += delegate { SetTheme(T.IsDark); };
            btnLaunch.Click += delegate { Launch(); };
            btnRepo.Click += delegate { try { Process.Start(new ProcessStartInfo("explorer.exe", RepoDir) { UseShellExecute = true }); } catch { } };
            btnWeb.Click += delegate { OpenDashboard(); };
            cmbMode.SelectedIndexChanged += delegate { SyncModeUi(); };
        }

        private void SyncModeUi() {
            string mode = cmbMode.SelectedItem != null ? cmbMode.SelectedItem.ToString() : "";
            bool web = mode.StartsWith("Web");
            btnLaunch.Text = web ? "Open Web UI" : (mode.StartsWith("Run") ? "Run Scan" : "Launch TUI");
            txtProject.ReadOnly = web;
            txtModel.ReadOnly = web;
            txtAgent.ReadOnly = web;
            chkContinue.Enabled = !web;
            txtPrompt.ReadOnly = web;
            foreach (Control c in cTarget.Controls) { InputWrap iw = c as InputWrap; if (iw != null) iw.SyncTheme(); }
            foreach (Control c in cModel.Controls) { InputWrap iw = c as InputWrap; if (iw != null) iw.SyncTheme(); }
            foreach (Control c in cPrompt.Controls) { InputWrap iw = c as InputWrap; if (iw != null) iw.SyncTheme(); }
            chkContinue.Invalidate();
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
            statusDot.BackColor = T.BgCard;
            pills.BackColor = T.BgCard;
            btnTheme.IsLight = !T.IsDark;
            tip.SetToolTip(btnTheme, T.IsDark ? "Switch to light mode" : "Switch to dark mode");
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
                if (c is RoundButton || c is StatusPill || c is ThemeToggle) { c.Invalidate(); continue; }
                if (c.HasChildren) ApplyThemeRecursive(c);
            }
        }

        private void SyncStatusColors() {
            Color nextDot = statusState == StatusState.Ready ? T.ColorOk : (statusState == StatusState.Error ? T.ColorErr : T.ColorWarn);
            Color nextFg = statusState == StatusState.Checking ? T.FgMuted : (statusState == StatusState.Ready ? T.FgText : (statusState == StatusState.Warn ? T.ColorWarn : T.ColorErr));
            lblStatus.ForeColor = nextFg;
            if (dotColor != nextDot) { dotColor = nextDot; statusDot.Invalidate(); }
        }

        private static void SetupEnvironmentPath() {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> list = new List<string>();
            string[] sources = {
                Path.Combine(home, @".local\bin"), Path.Combine(home, @".bun\bin"),
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

        private bool RepoInstalled() {
            return Directory.Exists(Path.Combine(RepoDir, "packages", "cyberstrike", "node_modules"));
        }

        private async void CheckBackend() {
            if (checking || IsDisposed) return;
            checking = true;
            statusTimer.Enabled = false;
            try {
                if (bunPath == null) bunPath = FindCommandOnPath("bun");
                bool repoOk = RepoInstalled();
                try {
                    string body = await http.GetStringAsync(ModelsUrl);
                    Match m = Regex.Match(body, "\"id\"\\s*:\\s*\"([^\"]+)\"");
                    servedModelId = m.Success ? m.Groups[1].Value : "";
                } catch { servedModelId = ""; }
                if (IsDisposed || !IsHandleCreated) return;

                bool bunOk = bunPath != null;
                bool strataOk = servedModelId != "";
                bool ready = bunOk && repoOk;
                lblStatus.Text = !ready
                    ? (!bunOk ? "bun not found on PATH - install bun first" : "CyberStrike deps missing - run 'bun install' in the repo")
                    : (strataOk ? ("CyberStrike ready - Strata serving " + servedModelId) : "CyberStrike ready - Strata offline (127.0.0.1:8080); pick a configured provider");
                statusState = !ready ? StatusState.Error : (strataOk ? StatusState.Ready : StatusState.Warn);
                SyncStatusColors();

                pillBun.SetState(bunOk, true);
                pillRepo.SetState(repoOk, true);
                pillStrata.SetState(strataOk, false);
                tip.SetToolTip(pillBun, bunOk ? "bun: " + bunPath : "bun not found in PATH or .bun\\bin (required to run the repo CLI)");
                tip.SetToolTip(pillRepo, repoOk ? "CyberStrike repo: " + RepoDir : "Repo or node_modules missing at " + RepoDir + " - run 'bun install' there");
                tip.SetToolTip(pillStrata, strataOk ? "Strata inference serving " + servedModelId : "Strata offline (optional: CyberStrike can use any configured provider)");

                btnLaunch.Enabled = ready;
                if (cLaunch.ReadyHighlight != ready) { cLaunch.ReadyHighlight = ready; cLaunch.Invalidate(); }
            } finally {
                checking = false;
                if (!IsDisposed) statusTimer.Enabled = true;
            }
        }

        private static string Flatten(string text) {
            return Regex.Replace((text ?? "").Trim().Replace("\"", "'"), "\\s*\\r?\\n\\s*", " ");
        }

        private int Port() {
            int port;
            if (!int.TryParse(txtPort.Text.Trim(), out port) || port < 1 || port > 65535) port = 4096;
            return port;
        }

        private void Launch() {
            if (bunPath == null) { MessageBox.Show(this, "'bun' was not found on PATH.", "Missing Tool"); return; }
            string mode = cmbMode.SelectedItem != null ? cmbMode.SelectedItem.ToString() : "TUI (interactive)";
            string project = txtProject.Text.Trim().Trim('"');
            if (project.Length == 0) project = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string model = txtModel.Text.Trim().Replace("\"", "'");
            string agent = txtAgent.Text.Trim().Replace("\"", "'");
            string prompt = Flatten(txtPrompt.Text);
            bool web = mode.StartsWith("Web");

            string args = string.Format("\"{0}\" run --cwd \"{1}\" --conditions=browser src/index.ts", bunPath, Path.Combine(RepoDir, "packages", "cyberstrike"));
            if (web) {
                args += " web --port " + Port();
            } else {
                if (!Directory.Exists(project)) { MessageBox.Show(this, "Project folder does not exist: " + project, "Invalid Folder"); return; }
                if (mode.StartsWith("Run")) {
                    if (prompt.Length == 0) { MessageBox.Show(this, "Run mode needs a prompt, e.g. 'Pentest this app and report findings'.", "Missing Input"); return; }
                    args += " run \"" + prompt + "\" --dir \"" + project + "\"";
                } else {
                    args += " \"" + project + "\"";
                    if (prompt.Length > 0) args += " --prompt \"" + prompt + "\"";
                }
                if (model.Length > 0) args += " -m " + model;
                if (agent.Length > 0) args += " --agent \"" + agent + "\"";
                if (chkContinue.Checked) args += " -c";
            }

            Program.Log("Launch: " + args);
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", string.Format("/k \"\"{0}\"\"", args)) { UseShellExecute = false, CreateNoWindow = false, WorkingDirectory = RepoDir };
            try { Process.Start(psi); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Launch failed"); }
        }

        private void OpenDashboard() {
            string url = "http://127.0.0.1:" + Port();
            Program.Log("OpenDashboard: " + url);
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Open failed"); }
        }
    }
}
