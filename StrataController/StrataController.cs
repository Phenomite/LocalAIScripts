using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Strata CLI and OMP Settings Controller")]
[assembly: System.Reflection.AssemblyDescription("Controller and configuration utility for Strata inference engine and Oh-My-Pi")]
[assembly: System.Reflection.AssemblyConfiguration("Release")]
[assembly: System.Reflection.AssemblyCompany("Phenomite")]
[assembly: System.Reflection.AssemblyProduct("Strata Controller")]
[assembly: System.Reflection.AssemblyCopyright("Copyright (c) 2026 Phenomite")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.3")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.3")]
[assembly: System.Reflection.AssemblyInformationalVersion("1.0.3")]
[assembly: ComVisible(false)]

namespace StrataControllerApp {
    internal enum StatusState { Checking, Serving, Loading, Stopped }
    internal enum PillKind { Neutral, Ok, Warn, Err }
    internal enum BtnTone { Neutral, Primary, Accent, Warning, Danger }

    internal sealed class ModelInfo { public string Family, FamilyTitle, Model, Display; }
    internal sealed class ThinkingProfile { public string Key, DefaultLevel; public string[] UiLevels, Efforts; }
    internal sealed class PendingOmpSyncInfo {
        public ModelInfo Info;
        public string Level, ConfigPath, OriginalConfigText;
        public bool HadOriginalConfig;
    }
    internal sealed class PollSnapshot {
        public bool HasEngine, Serving;
        public int EnginePid;
        public string EngineName, LoadedModelId, RamText;
        public int RamPct;
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

    internal sealed class UiTheme {
        public bool IsDark;
        public Color BgCanvas, BgCard, BgInput, BgDrop, BgDisabled, LogBg, LogFg;
        public Color BorderCard, BorderInput;
        public Color FgText, FgMuted, FgDim;
        public Color Accent, AccentHover, AccentPressed;
        public Color ColorOk, ColorWarn, ColorErr;
        public Color BtnBg, BtnHover, BtnPressed, BtnBorder;
        public Color Green, GreenHover, GreenPressed;
        public Color Amber, AmberHover, AmberPressed;
        public Color Red, RedHover, RedPressed;
        public Color ToggleOff, ToggleKnob;
        public Color PillNeuBg, PillNeuBd, PillNeuFg;
        public Color PillOkBg, PillOkBd, PillOkFg;
        public Color PillWarnBg, PillWarnBd, PillWarnFg;
        public Color PillErrBg, PillErrBd, PillErrFg;

        public static readonly UiTheme Dark = new UiTheme {
            IsDark = true,
            BgCanvas = Color.FromArgb(17, 18, 23), BgCard = Color.FromArgb(26, 28, 35),
            BgInput = Color.FromArgb(19, 20, 26), BgDrop = Color.FromArgb(30, 33, 43),
            BgDisabled = Color.FromArgb(23, 25, 32), LogBg = Color.FromArgb(16, 18, 24), LogFg = Color.FromArgb(201, 212, 227),
            BorderCard = Color.FromArgb(44, 48, 60), BorderInput = Color.FromArgb(56, 61, 78),
            FgText = Color.FromArgb(232, 236, 244), FgMuted = Color.FromArgb(155, 163, 184), FgDim = Color.FromArgb(104, 112, 134),
            Accent = Color.FromArgb(88, 166, 255), AccentHover = Color.FromArgb(69, 142, 224), AccentPressed = Color.FromArgb(52, 118, 194),
            ColorOk = Color.FromArgb(63, 185, 80), ColorWarn = Color.FromArgb(232, 179, 57), ColorErr = Color.FromArgb(242, 99, 99),
            BtnBg = Color.FromArgb(37, 40, 52), BtnHover = Color.FromArgb(48, 53, 69), BtnPressed = Color.FromArgb(29, 32, 42), BtnBorder = Color.FromArgb(58, 64, 82),
            Green = Color.FromArgb(35, 134, 54), GreenHover = Color.FromArgb(46, 160, 67), GreenPressed = Color.FromArgb(27, 110, 44),
            Amber = Color.FromArgb(166, 99, 16), AmberHover = Color.FromArgb(191, 116, 21), AmberPressed = Color.FromArgb(135, 79, 11),
            Red = Color.FromArgb(184, 50, 50), RedHover = Color.FromArgb(207, 61, 61), RedPressed = Color.FromArgb(150, 38, 38),
            ToggleOff = Color.FromArgb(53, 58, 75), ToggleKnob = Color.FromArgb(240, 244, 250),
            PillNeuBg = Color.FromArgb(34, 37, 49), PillNeuBd = Color.FromArgb(53, 59, 77), PillNeuFg = Color.FromArgb(180, 188, 208),
            PillOkBg = Color.FromArgb(20, 43, 29), PillOkBd = Color.FromArgb(38, 92, 57), PillOkFg = Color.FromArgb(104, 216, 132),
            PillWarnBg = Color.FromArgb(45, 36, 18), PillWarnBd = Color.FromArgb(99, 75, 29), PillWarnFg = Color.FromArgb(242, 196, 90),
            PillErrBg = Color.FromArgb(48, 23, 27), PillErrBd = Color.FromArgb(107, 42, 48), PillErrFg = Color.FromArgb(245, 133, 133)
        };

        public static readonly UiTheme Light = new UiTheme {
            IsDark = false,
            BgCanvas = Color.FromArgb(241, 243, 248), BgCard = Color.FromArgb(255, 255, 255),
            BgInput = Color.FromArgb(246, 247, 251), BgDrop = Color.FromArgb(255, 255, 255),
            BgDisabled = Color.FromArgb(236, 238, 244), LogBg = Color.FromArgb(247, 248, 252), LogFg = Color.FromArgb(31, 37, 51),
            BorderCard = Color.FromArgb(216, 220, 232), BorderInput = Color.FromArgb(196, 202, 218),
            FgText = Color.FromArgb(24, 28, 38), FgMuted = Color.FromArgb(88, 96, 118), FgDim = Color.FromArgb(128, 136, 156),
            Accent = Color.FromArgb(37, 116, 235), AccentHover = Color.FromArgb(28, 98, 204), AccentPressed = Color.FromArgb(22, 79, 166),
            ColorOk = Color.FromArgb(26, 148, 72), ColorWarn = Color.FromArgb(186, 118, 12), ColorErr = Color.FromArgb(206, 46, 46),
            BtnBg = Color.FromArgb(238, 240, 246), BtnHover = Color.FromArgb(226, 230, 240), BtnPressed = Color.FromArgb(210, 216, 230), BtnBorder = Color.FromArgb(200, 206, 221),
            Green = Color.FromArgb(27, 138, 61), GreenHover = Color.FromArgb(22, 117, 51), GreenPressed = Color.FromArgb(17, 94, 40),
            Amber = Color.FromArgb(184, 109, 10), AmberHover = Color.FromArgb(156, 92, 8), AmberPressed = Color.FromArgb(128, 74, 6),
            Red = Color.FromArgb(199, 44, 44), RedHover = Color.FromArgb(173, 35, 35), RedPressed = Color.FromArgb(143, 27, 27),
            ToggleOff = Color.FromArgb(207, 213, 227), ToggleKnob = Color.FromArgb(255, 255, 255),
            PillNeuBg = Color.FromArgb(236, 239, 246), PillNeuBd = Color.FromArgb(200, 207, 223), PillNeuFg = Color.FromArgb(70, 78, 100),
            PillOkBg = Color.FromArgb(232, 247, 237), PillOkBd = Color.FromArgb(156, 218, 178), PillOkFg = Color.FromArgb(20, 102, 52),
            PillWarnBg = Color.FromArgb(254, 245, 227), PillWarnBd = Color.FromArgb(234, 200, 132), PillWarnFg = Color.FromArgb(136, 84, 10),
            PillErrBg = Color.FromArgb(253, 236, 237), PillErrBd = Color.FromArgb(240, 178, 180), PillErrFg = Color.FromArgb(158, 34, 38)
        };
    }

    internal static class Ui {
        public static UiTheme T = UiTheme.Dark;
        public static float Scale = 1f;
        public static int S(float px) { return (int)Math.Round(px * Scale); }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        public static void DwmTitleBar(IntPtr hwnd, bool dark) {
            if (hwnd == IntPtr.Zero) return;
            try {
                int v = dark ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, 20, ref v, 4) != 0) DwmSetWindowAttribute(hwnd, 19, ref v, 4);
            } catch { }
        }

        public static void SetCue(IntPtr hwnd, string cue) {
            if (hwnd != IntPtr.Zero && !string.IsNullOrEmpty(cue)) try { SendMessage(hwnd, 0x1501, (IntPtr)1, cue); } catch { }
        }

        public static GraphicsPath RoundPath(RectangleF r, float rad) {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(rad * 2f, Math.Min(r.Width, r.Height));
            if (d <= 1f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    internal sealed class CardPanel : TableLayoutPanel {
        public bool ReadyHighlight;
        public CardPanel() {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.T.BgCard;
        }
        public void SyncTheme() { BackColor = Ui.T.BgCard; Invalidate(); }
        protected override void OnPaintBackground(PaintEventArgs e) {
            if (Width <= 2 || Height <= 2) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush cb = new SolidBrush(Ui.T.BgCanvas)) e.Graphics.FillRectangle(cb, ClientRectangle);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath gp = Ui.RoundPath(r, Ui.S(8)))
            using (SolidBrush br = new SolidBrush(Ui.T.BgCard))
            using (Pen pen = new Pen(ReadyHighlight ? Ui.T.ColorOk : Ui.T.BorderCard, 1f)) {
                e.Graphics.FillPath(br, gp);
                e.Graphics.DrawPath(pen, gp);
            }
        }
    }

    internal sealed class InputWrap : Panel {
        public readonly TextBox Box;
        private readonly string cue;
        public InputWrap(string cueText, Font font) {
            cue = cueText;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Box = new TextBox { BorderStyle = BorderStyle.None, Font = font, Margin = Padding.Empty };
            Box.GotFocus += delegate { Invalidate(); };
            Box.LostFocus += delegate { Invalidate(); };
            Box.HandleCreated += delegate { Ui.SetCue(Box.Handle, cue); };
            Controls.Add(Box);
            MouseDown += delegate { Box.Focus(); };
            SyncTheme();
        }
        public void SyncTheme() {
            BackColor = Ui.T.BgCard;
            Box.BackColor = Box.ReadOnly ? Ui.T.BgDisabled : Ui.T.BgInput;
            Box.ForeColor = Box.ReadOnly ? Ui.T.FgDim : Ui.T.FgText;
            Invalidate();
        }
        protected override void OnResize(EventArgs e) {
            base.OnResize(e);
            int px = Ui.S(9), bh = Box.PreferredHeight;
            Box.SetBounds(px, Math.Max(2, (Height - bh) / 2), Math.Max(10, Width - px * 2), bh);
        }
        protected override void OnPaint(PaintEventArgs e) {
            if (Width <= 2 || Height <= 2) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush cb = new SolidBrush(Ui.T.BgCard)) e.Graphics.FillRectangle(cb, ClientRectangle);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Color fill = Box.ReadOnly ? Ui.T.BgDisabled : Ui.T.BgInput;
            Color bd = (Box.Focused && !Box.ReadOnly) ? Ui.T.Accent : Ui.T.BorderInput;
            using (GraphicsPath gp = Ui.RoundPath(r, Ui.S(6)))
            using (SolidBrush br = new SolidBrush(fill))
            using (Pen pen = new Pen(bd, 1f)) {
                e.Graphics.FillPath(br, gp);
                e.Graphics.DrawPath(pen, gp);
            }
        }
    }

    internal sealed class LogWrap : Panel {
        public readonly TextBox Box;
        public LogWrap(Font font) {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Padding = new Padding(Ui.S(8), Ui.S(6), Ui.S(8), Ui.S(6));
            Box = new TextBox {
                Multiline = true, ReadOnly = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill, MaxLength = int.MaxValue, Font = font, BorderStyle = BorderStyle.None
            };
            Controls.Add(Box);
            SyncTheme();
        }
        public void SyncTheme() {
            BackColor = Ui.T.BgCard;
            Box.BackColor = Ui.T.LogBg;
            Box.ForeColor = Ui.T.LogFg;
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e) {
            if (Width <= 2 || Height <= 2) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush cb = new SolidBrush(Ui.T.BgCard)) e.Graphics.FillRectangle(cb, ClientRectangle);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath gp = Ui.RoundPath(r, Ui.S(6)))
            using (SolidBrush br = new SolidBrush(Ui.T.LogBg))
            using (Pen pen = new Pen(Ui.T.BorderCard, 1f)) {
                e.Graphics.FillPath(br, gp);
                e.Graphics.DrawPath(pen, gp);
            }
        }
    }

    internal sealed class FlatDropRenderer : ToolStripRenderer {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) {
            using (SolidBrush b = new SolidBrush(Ui.T.BorderInput)) e.Graphics.FillRectangle(b, e.AffectedBounds);
        }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
    }

    internal sealed class ThemedCombo : Control {
        public readonly List<string> Items = new List<string>();
        public readonly bool Editable;
        public readonly TextBox EditBox;
        public event EventHandler SelectedIndexChanged;

        private readonly ToolStripDropDown drop;
        private readonly ListBox list;
        private int selIdx = -1, hoverIdx = -1, lastCloseTick;
        private bool hovered, dropOpen, updating;

        public ThemedCombo(bool editable, Font font) {
            Editable = editable;
            Font = font;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = !editable;

            if (editable) {
                EditBox = new TextBox { BorderStyle = BorderStyle.None, Font = font, Margin = Padding.Empty };
                EditBox.GotFocus += delegate { Invalidate(); };
                EditBox.LostFocus += delegate { Invalidate(); };
                EditBox.TextChanged += delegate { OnTextChanged(EventArgs.Empty); };
                EditBox.KeyDown += delegate(object s, KeyEventArgs e) {
                    if (e.KeyCode == Keys.F4 || (e.Alt && e.KeyCode == Keys.Down)) { e.Handled = true; ToggleDrop(); }
                };
                Controls.Add(EditBox);
            }

            list = new ListBox { BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, IntegralHeight = false, Font = font };
            list.DrawItem += OnListDrawItem;
            list.MouseMove += delegate(object s, MouseEventArgs e) {
                int i = list.IndexFromPoint(e.Location);
                if (i != hoverIdx) { hoverIdx = i; list.Invalidate(); }
            };
            list.MouseClick += delegate(object s, MouseEventArgs e) {
                int i = list.IndexFromPoint(e.Location);
                if (i >= 0) CommitIndex(i);
            };
            list.KeyDown += delegate(object s, KeyEventArgs e) {
                if (e.KeyCode == Keys.Enter && list.SelectedIndex >= 0) { e.Handled = true; CommitIndex(list.SelectedIndex); }
                else if (e.KeyCode == Keys.Escape) { e.Handled = true; drop.Close(); FocusSelf(); }
            };

            ToolStripControlHost host = new ToolStripControlHost(list) { Padding = Padding.Empty, Margin = Padding.Empty, AutoSize = false };
            drop = new ToolStripDropDown { Padding = new Padding(1), Margin = Padding.Empty, AutoSize = true, DropShadowEnabled = true, Renderer = new FlatDropRenderer() };
            drop.Items.Add(host);
            drop.Closed += delegate { dropOpen = false; lastCloseTick = Environment.TickCount; Invalidate(); };
            SyncTheme();
        }

        public int SelectedIndex {
            get { return selIdx; }
            set {
                int next = (value >= 0 && value < Items.Count) ? value : -1;
                if (selIdx == next && (!Editable || (next >= 0 && EditBox.Text == Items[next]))) return;
                selIdx = next;
                if (Editable && selIdx >= 0) EditBox.Text = Items[selIdx];
                if (!updating) { Invalidate(); if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty); }
            }
        }

        public string SelectedItem {
            get { return (selIdx >= 0 && selIdx < Items.Count) ? Items[selIdx] : (Editable ? EditBox.Text : null); }
            set {
                int idx = (value != null) ? Items.IndexOf(value) : -1;
                if (idx >= 0) SelectedIndex = idx;
                else if (Editable) { selIdx = -1; EditBox.Text = value ?? ""; }
            }
        }

        public override string Text {
            get { return Editable ? EditBox.Text : (selIdx >= 0 && selIdx < Items.Count ? Items[selIdx] : ""); }
            set {
                if (Editable) EditBox.Text = value ?? "";
                else SelectedItem = value;
            }
        }

        public void SetItems(IEnumerable<string> items, string selectItem) {
            updating = true;
            try {
                Items.Clear();
                if (items != null) Items.AddRange(items);
                selIdx = -1;
                if (!string.IsNullOrEmpty(selectItem)) selIdx = Items.IndexOf(selectItem);
                if (selIdx < 0 && Items.Count > 0) selIdx = 0;
                if (Editable && selIdx >= 0) EditBox.Text = Items[selIdx];
            } finally { updating = false; }
            Invalidate();
        }

        public void SyncTheme() {
            BackColor = Ui.T.BgCard;
            list.BackColor = Ui.T.BgDrop;
            list.ForeColor = Ui.T.FgText;
            if (EditBox != null) {
                EditBox.ReadOnly = !Enabled;
                EditBox.BackColor = Enabled ? Ui.T.BgInput : Ui.T.BgDisabled;
                EditBox.ForeColor = Enabled ? Ui.T.FgText : Ui.T.FgDim;
            }
            Invalidate();
        }

        private void FocusSelf() { if (Editable) EditBox.Focus(); else Focus(); }

        private void CommitIndex(int idx) {
            drop.Close();
            if (idx >= 0 && idx < Items.Count) {
                bool same = (selIdx == idx);
                selIdx = idx;
                if (Editable) EditBox.Text = Items[idx];
                Invalidate();
                if (!same && SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
                else if (Editable && SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
            FocusSelf();
        }

        private void ToggleDrop() {
            if (!Enabled || Items.Count == 0) return;
            if (dropOpen) { drop.Close(); return; }
            if (unchecked(Environment.TickCount - lastCloseTick) < 160) return;
            list.BeginUpdate();
            list.Items.Clear();
            int maxW = Width;
            foreach (string it in Items) {
                list.Items.Add(it);
                int w = TextRenderer.MeasureText(it, Font).Width + Ui.S(30);
                if (w > maxW) maxW = w;
            }
            list.ItemHeight = Math.Max(Ui.S(26), Font.Height + Ui.S(10));
            int vis = Math.Min(Items.Count, 8);
            int dropW = Math.Min(Math.Max(Width, maxW), Ui.S(520));
            list.Size = new Size(dropW - 2, vis * list.ItemHeight + 2);
            list.SelectedIndex = (selIdx >= 0 && selIdx < list.Items.Count) ? selIdx : -1;
            hoverIdx = list.SelectedIndex;
            list.EndUpdate();
            drop.Items[0].Size = list.Size;
            dropOpen = true;
            Invalidate();
            drop.Show(this, new Point(0, Height + Ui.S(2)));
            list.Focus();
        }

        private void OnListDrawItem(object sender, DrawItemEventArgs e) {
            if (e.Index < 0 || e.Index >= list.Items.Count) return;
            bool sel = (e.Index == hoverIdx) || ((e.State & DrawItemState.Selected) != 0 && hoverIdx < 0);
            Color bg = sel ? Ui.T.Accent : Ui.T.BgDrop;
            Color fg = sel ? Color.White : Ui.T.FgText;
            using (SolidBrush br = new SolidBrush(bg)) e.Graphics.FillRectangle(br, e.Bounds);
            Rectangle tr = new Rectangle(e.Bounds.X + Ui.S(9), e.Bounds.Y, Math.Max(1, e.Bounds.Width - Ui.S(14)), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, list.Items[e.Index].ToString(), Font, tr, fg, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); SyncTheme(); }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e);
            if (!Enabled || e.Button != MouseButtons.Left) return;
            if (!Editable) Focus();
            ToggleDrop();
        }
        protected override bool IsInputKey(Keys keyData) {
            Keys k = keyData & Keys.KeyCode;
            return k == Keys.Up || k == Keys.Down || k == Keys.Home || k == Keys.End || base.IsInputKey(keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);
            if (!Enabled || Editable || Items.Count == 0) return;
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter || e.KeyCode == Keys.F4 || (e.Alt && e.KeyCode == Keys.Down)) { e.Handled = true; ToggleDrop(); }
            else if (e.KeyCode == Keys.Down && selIdx + 1 < Items.Count) { e.Handled = true; SelectedIndex = selIdx + 1; }
            else if (e.KeyCode == Keys.Up && selIdx > 0) { e.Handled = true; SelectedIndex = selIdx - 1; }
            else if (e.KeyCode == Keys.Home) { e.Handled = true; SelectedIndex = 0; }
            else if (e.KeyCode == Keys.End) { e.Handled = true; SelectedIndex = Items.Count - 1; }
        }
        protected override void OnResize(EventArgs e) {
            base.OnResize(e);
            if (EditBox != null) {
                int px = Ui.S(9), aw = Ui.S(26), bh = EditBox.PreferredHeight;
                EditBox.SetBounds(px, Math.Max(2, (Height - bh) / 2), Math.Max(10, Width - px - aw), bh);
            }
        }
        protected override void OnPaint(PaintEventArgs e) {
            if (Width <= 2 || Height <= 2) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush cb = new SolidBrush(Ui.T.BgCard)) e.Graphics.FillRectangle(cb, ClientRectangle);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            bool foc = Focused || dropOpen || (EditBox != null && EditBox.Focused);
            Color fill = Enabled ? Ui.T.BgInput : Ui.T.BgDisabled;
            Color bd = (foc && Enabled) ? Ui.T.Accent : (hovered && Enabled ? Ui.T.FgDim : Ui.T.BorderInput);
            using (GraphicsPath gp = Ui.RoundPath(r, Ui.S(6)))
            using (SolidBrush br = new SolidBrush(fill))
            using (Pen pen = new Pen(bd, 1f)) {
                e.Graphics.FillPath(br, gp);
                e.Graphics.DrawPath(pen, gp);
            }
            int aw = Ui.S(24);
            if (!Editable) {
                string txt = (selIdx >= 0 && selIdx < Items.Count) ? Items[selIdx] : "";
                Rectangle tr = new Rectangle(Ui.S(9), 0, Math.Max(1, Width - Ui.S(11) - aw), Height);
                TextRenderer.DrawText(e.Graphics, txt, Font, tr, Enabled ? Ui.T.FgText : Ui.T.FgDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            float cx = Width - aw * 0.52f, cy = Height * 0.5f, d = Ui.S(3.5f);
            using (Pen cp = new Pen(Enabled ? Ui.T.FgMuted : Ui.T.FgDim, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round }) {
                e.Graphics.DrawLines(cp, new[] { new PointF(cx - d, cy - d * 0.45f), new PointF(cx, cy + d * 0.55f), new PointF(cx + d, cy - d * 0.45f) });
            }
        }
        protected override void Dispose(bool disposing) {
            if (disposing) { drop.Dispose(); list.Dispose(); }
            base.Dispose(disposing);
        }
    }

    internal sealed class ToggleCheck : Control {
        public event EventHandler CheckedChanged;
        private bool isChecked, hovered;
        public ToggleCheck(string text, bool initial, Font font) {
            Text = text; isChecked = initial; Font = font;
            DoubleBuffered = true; Cursor = Cursors.Hand; TabStop = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        }
        public bool Checked {
            get { return isChecked; }
            set { if (isChecked != value) { isChecked = value; Invalidate(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); } }
        }
        public void SyncTheme() { BackColor = Ui.T.BgCard; Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnClick(EventArgs e) { base.OnClick(e); Focus(); Checked = !Checked; }
        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { e.Handled = true; Checked = !Checked; }
        }
        protected override void OnPaint(PaintEventArgs e) {
            if (Width <= 2 || Height <= 2) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush cb = new SolidBrush(Ui.T.BgCard)) e.Graphics.FillRectangle(cb, ClientRectangle);
            int tw = Ui.S(36), th = Ui.S(20), ty = (Height - th) / 2;
            RectangleF tr = new RectangleF(1f, ty + 0.5f, tw - 1f, th - 1f);
            Color track = isChecked ? (hovered ? Ui.T.AccentHover : Ui.T.Accent) : Ui.T.ToggleOff;
            using (GraphicsPath gp = Ui.RoundPath(tr, th / 2f))
            using (SolidBrush br = new SolidBrush(track)) {
                e.Graphics.FillPath(br, gp);
                if (Focused) using (Pen fp = new Pen(Ui.T.Accent, 1.5f)) e.Graphics.DrawPath(fp, gp);
            }
            float pad = Ui.S(3), kd = th - pad * 2f;
            float kx = isChecked ? (tr.Right - pad - kd) : (tr.X + pad);
            using (SolidBrush kb = new SolidBrush(Ui.T.ToggleKnob)) e.Graphics.FillEllipse(kb, kx, ty + pad, kd, kd);
            Rectangle lr = new Rectangle(tw + Ui.S(8), 0, Math.Max(1, Width - tw - Ui.S(8)), Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, lr, Ui.T.FgText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    internal sealed class RoundButton : Button {
        public BtnTone Tone;
        private bool hovered, pressed;
        public RoundButton(string text, BtnTone tone, Font font) {
            Text = text; Tone = tone; Font = font;
            FlatStyle = FlatStyle.Flat; Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        public void SyncTheme() { BackColor = Ui.T.BgCard; Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; pressed = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed = false; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e) {
            if (Width <= 2 || Height <= 2) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush cb = new SolidBrush(Ui.T.BgCard)) e.Graphics.FillRectangle(cb, ClientRectangle);
            Color bg, bd, fg;
            if (!Enabled) { bg = Ui.T.BgDisabled; bd = Ui.T.BorderCard; fg = Ui.T.FgDim; }
            else if (Tone == BtnTone.Primary) { bg = pressed ? Ui.T.GreenPressed : (hovered ? Ui.T.GreenHover : Ui.T.Green); bd = bg; fg = Color.White; }
            else if (Tone == BtnTone.Accent)  { bg = pressed ? Ui.T.AccentPressed : (hovered ? Ui.T.AccentHover : Ui.T.Accent); bd = bg; fg = Color.White; }
            else if (Tone == BtnTone.Warning) { bg = pressed ? Ui.T.AmberPressed : (hovered ? Ui.T.AmberHover : Ui.T.Amber); bd = bg; fg = Color.White; }
            else if (Tone == BtnTone.Danger)  { bg = pressed ? Ui.T.RedPressed : (hovered ? Ui.T.RedHover : Ui.T.Red); bd = bg; fg = Color.White; }
            else { bg = pressed ? Ui.T.BtnPressed : (hovered ? Ui.T.BtnHover : Ui.T.BtnBg); bd = Ui.T.BtnBorder; fg = Ui.T.FgText; }

            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath gp = Ui.RoundPath(r, Ui.S(6)))
            using (SolidBrush br = new SolidBrush(bg))
            using (Pen pen = new Pen(Focused && ShowFocusCues ? Ui.T.Accent : bd, Focused && ShowFocusCues ? 1.5f : 1f)) {
                e.Graphics.FillPath(br, gp);
                e.Graphics.DrawPath(pen, gp);
            }
            Rectangle tr = new Rectangle(Ui.S(6), 0, Math.Max(1, Width - Ui.S(12)), Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, tr, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    internal sealed class StatusPill : Control {
        public bool ShowDot;
        private PillKind kind = PillKind.Neutral;
        public StatusPill(string text, PillKind k, bool dot, Font font, int height) {
            Text = text; kind = k; ShowDot = dot; Font = font; Height = height;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            RecalcWidth();
        }
        public void UpdatePill(string text, PillKind k) {
            if (Text == text && kind == k) return;
            Text = text; kind = k;
            RecalcWidth();
            Invalidate();
        }
        public void SyncTheme() { BackColor = Ui.T.BgCard; Invalidate(); }
        private void RecalcWidth() {
            int tw = TextRenderer.MeasureText(Text ?? "", Font).Width;
            Width = tw + Ui.S(ShowDot ? 28 : 18);
        }
        protected override void OnPaint(PaintEventArgs e) {
            if (Width <= 2 || Height <= 2) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush cb = new SolidBrush(Ui.T.BgCard)) e.Graphics.FillRectangle(cb, ClientRectangle);
            Color bg = Ui.T.PillNeuBg, bd = Ui.T.PillNeuBd, fg = Ui.T.PillNeuFg, dot = Ui.T.FgMuted;
            if (kind == PillKind.Ok)        { bg = Ui.T.PillOkBg; bd = Ui.T.PillOkBd; fg = Ui.T.PillOkFg; dot = Ui.T.ColorOk; }
            else if (kind == PillKind.Warn) { bg = Ui.T.PillWarnBg; bd = Ui.T.PillWarnBd; fg = Ui.T.PillWarnFg; dot = Ui.T.ColorWarn; }
            else if (kind == PillKind.Err)  { bg = Ui.T.PillErrBg; bd = Ui.T.PillErrBd; fg = Ui.T.PillErrFg; dot = Ui.T.ColorErr; }
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath gp = Ui.RoundPath(r, Height / 2f))
            using (SolidBrush br = new SolidBrush(bg))
            using (Pen pen = new Pen(bd, 1f)) {
                e.Graphics.FillPath(br, gp);
                e.Graphics.DrawPath(pen, gp);
            }
            int xOff = Ui.S(9);
            if (ShowDot) {
                float dd = Ui.S(6.5f);
                using (SolidBrush db = new SolidBrush(dot)) e.Graphics.FillEllipse(db, xOff, (Height - dd) / 2f, dd, dd);
                xOff += Ui.S(11);
            }
            Rectangle tr = new Rectangle(xOff, 0, Math.Max(1, Width - xOff - Ui.S(6)), Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, tr, fg, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    internal sealed class StatusHeader : Control {
        private readonly Font fontTitle, fontDetail;
        private StatusState state = StatusState.Checking;
        private string title = "CHECKING...", detail = "Initializing Strata controller...";
        public StatusHeader(Font titleFont, Font detailFont) {
            fontTitle = titleFont; fontDetail = detailFont;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        public void SetState(StatusState st, string newTitle, string newDetail) {
            if (state == st && title == newTitle && detail == newDetail) return;
            state = st; title = newTitle; detail = newDetail;
            Invalidate();
        }
        public void SyncTheme() { BackColor = Ui.T.BgCard; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e) {
            if (Width <= 2 || Height <= 2) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush cb = new SolidBrush(Ui.T.BgCard)) e.Graphics.FillRectangle(cb, ClientRectangle);
            Color sc = Ui.T.FgMuted;
            if (state == StatusState.Serving) sc = Ui.T.ColorOk;
            else if (state == StatusState.Loading) sc = Ui.T.ColorWarn;
            else if (state == StatusState.Stopped) sc = Ui.T.ColorErr;

            float cy = Height / 2f, hr = Ui.S(7.5f), dr = Ui.S(4f), cx = hr + 1f;
            using (SolidBrush hb = new SolidBrush(Color.FromArgb(Ui.T.IsDark ? 60 : 45, sc))) e.Graphics.FillEllipse(hb, cx - hr, cy - hr, hr * 2f, hr * 2f);
            using (SolidBrush db = new SolidBrush(sc)) e.Graphics.FillEllipse(db, cx - dr, cy - dr, dr * 2f, dr * 2f);

            int tx = (int)(cx + hr + Ui.S(7));
            int tw = TextRenderer.MeasureText(title, fontTitle, new Size(int.MaxValue, Height), TextFormatFlags.NoPadding).Width;
            Rectangle tr = new Rectangle(tx, 0, tw + Ui.S(4), Height);
            TextRenderer.DrawText(e.Graphics, title, fontTitle, tr, sc, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);

            int dx = tr.Right + Ui.S(10);
            if (dx < Width - Ui.S(12)) {
                Rectangle drRect = new Rectangle(dx, 0, Width - dx, Height);
                TextRenderer.DrawText(e.Graphics, detail, fontDetail, drRect, Ui.T.FgMuted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }
    }

    internal sealed class ThemeToggle : Button {
        private bool hovered;
        public ThemeToggle(int height) {
            Size = new Size(Ui.S(58), height);
            AccessibleName = "Toggle color theme";
            FlatStyle = FlatStyle.Flat; Cursor = Cursors.Hand; TabStop = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        public void SyncTheme() { BackColor = Ui.T.BgCard; Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e) {
            if (Width <= 2 || Height <= 2) return;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush cb = new SolidBrush(Ui.T.BgCard)) g.FillRectangle(cb, ClientRectangle);
            RectangleF outer = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath gp = Ui.RoundPath(outer, Height / 2f))
            using (SolidBrush bg = new SolidBrush(Ui.T.BgInput))
            using (Pen bd = new Pen(Focused && ShowFocusCues ? Ui.T.Accent : (hovered ? Ui.T.FgDim : Ui.T.BorderInput), Focused && ShowFocusCues ? 1.5f : 1f)) {
                g.FillPath(bg, gp);
                g.DrawPath(bd, gp);
            }
            float pad = Ui.S(2.5f), halfW = (Width - pad * 2f) / 2f, segH = Height - pad * 2f;
            float actX = Ui.T.IsDark ? pad : (pad + halfW);
            Color actBg = Ui.T.IsDark ? Ui.T.BtnHover : Ui.T.BgCard;
            RectangleF seg = new RectangleF(actX, pad, halfW, segH);
            using (GraphicsPath sp = Ui.RoundPath(seg, segH / 2f))
            using (SolidBrush sb = new SolidBrush(actBg))
            using (Pen sbd = new Pen(Ui.T.BorderCard, 1f)) {
                g.FillPath(sb, sp);
                g.DrawPath(sbd, sp);
            }
            // Left: Vector Crescent Moon
            float mcx = pad + halfW * 0.5f, mcy = Height * 0.5f, mr = Ui.S(4.6f);
            Color moonClr = Ui.T.IsDark ? Ui.T.Accent : Ui.T.FgDim;
            Color moonCut = Ui.T.IsDark ? actBg : Ui.T.BgInput;
            using (SolidBrush mb = new SolidBrush(moonClr)) g.FillEllipse(mb, mcx - mr, mcy - mr, mr * 2f, mr * 2f);
            using (SolidBrush cut = new SolidBrush(moonCut)) g.FillEllipse(cut, mcx - mr * 0.15f, mcy - mr * 1.15f, mr * 1.8f, mr * 1.8f);

            // Right: Vector Sun
            float scx = pad + halfW * 1.5f, scy = Height * 0.5f, sr = Ui.S(2.7f), r1 = Ui.S(4.3f), r2 = Ui.S(5.9f);
            Color sunClr = Ui.T.IsDark ? Ui.T.FgDim : Ui.T.ColorWarn;
            using (SolidBrush sb = new SolidBrush(sunClr)) g.FillEllipse(sb, scx - sr, scy - sr, sr * 2f, sr * 2f);
            using (Pen rp = new Pen(sunClr, Math.Max(1.1f, Ui.S(1.2f))) { StartCap = LineCap.Round, EndCap = LineCap.Round }) {
                for (int i = 0; i < 8; i++) {
                    double ang = i * Math.PI / 4.0;
                    float cos = (float)Math.Cos(ang), sin = (float)Math.Sin(ang);
                    g.DrawLine(rp, scx + cos * r1, scy + sin * r1, scx + cos * r2, scy + sin * r2);
                }
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
        private static readonly string[] EngineProcNames = { "strata-hy4", "strata-dsv41", "strata-inkling", "strata-server", "strata" };
        private static readonly string[] StopProcNames   = { "strata-hy4", "strata-dsv41", "strata-inkling", "strata-server", "strata-vision", "strata" };
        private static readonly Dictionary<string, string> FamilyTitles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "deepseek41", "DeepSeek V4.1-Flash" }, { "hy4-flash", "Tencent Hy4-Flash" },
            { "coder", "Qwen3.8 Coder" }, { "gyro", "Qwen3.8 Gyro" },
            { "swift", "Qwen3.8 Swift 1.5" }, { "unsloth", "Qwen3.8 Unsloth" },
            { "nvfp4", "Qwen3.8 NVFP4" }, { "qwen36", "Qwen3.6-35B-A3B" }
        };

        private readonly Font fontHead = new Font("Segoe UI Semibold", 8f);
        private readonly Font fontLbl  = new Font("Segoe UI", 8.5f);
        private readonly Font fontUI   = new Font("Segoe UI", 9.5f);
        private readonly Font fontBold = new Font("Segoe UI Semibold", 9.5f);
        private readonly Font fontMono = new Font("Consolas", 9f);

        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private readonly IPGlobalProperties ipProps = IPGlobalProperties.GetIPGlobalProperties();
        private readonly Microsoft.VisualBasic.Devices.ComputerInfo compInfo = new Microsoft.VisualBasic.Devices.ComputerInfo();
        private HttpClient localHttp = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromMilliseconds(500) };
        private CancellationTokenSource closeCts = new CancellationTokenSource();

        private readonly string userProfile, strataLocalDir, launchDir, installDir, ompAgentDir, launchLogPath, cmdLogPath;
        private readonly Dictionary<string, ModelInfo> modelMap = new Dictionary<string, ModelInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> modelKeys = new List<string>();
        private string initialThinking = "medium", lastThinkingKey = "", currentEngineLog = "";

        private Icon appIcon;
        private ToolTip toolTip;
        private TableLayoutPanel rootGrid;
        private CardPanel cardStatus, cardControl;
        private StatusHeader statusHeader;
        private StatusPill pillRam, pillEndpoint;
        private ThemeToggle themeToggle;
        private ThemedCombo cmbModel, cmbCtx, cmbSpeed, cmbPleIo, cmbVramReserve, cmbThinking;
        private InputWrap wrapPreset;
        private LogWrap logWrap;
        private ToggleCheck chkOmpContinue;
        private TextBox txtPreset, txtLog;
        private RoundButton btnStart, btnStop, btnForceStop, btnTune, btnWireOmp, btnRunOmp, btnOpenLog;
        private System.Windows.Forms.Timer pollTimer;

        private bool suppressThinkingSync, isPrompting, isPolling, resourcesDisposed;
        private long engineLogPos, launchLogPos, cmdLogPos, launchStartFt, actionStartFt;
        private Process launchProc, actionProc;
        private LogSink launchSink, actionSink;
        private PendingOmpSyncInfo pendingOmpSync;
        private Action pendingLaunchNext;
        private Action<bool> pendingActionNext;
        private int lastSyncedEnginePid = -1;

        public StrataControllerForm() {
            using (Graphics g = CreateGraphics()) {
                float sc = g.DpiX / 96f;
                Ui.Scale = (sc >= 0.75f && sc <= 4f) ? sc : 1f;
            }
            userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
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

            ReadInitialThinking();
            BuildUi();
            WriteGuiLog("Debug log: " + Program.DebugLogPath);
            WireEvents();
            pollTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            pollTimer.Tick += OnPollTimerTick;
        }

        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);
            Ui.DwmTitleBar(Handle, Ui.T.IsDark);
        }

        private static string SanitizeArg(string val) {
            return Regex.Replace((val ?? "").Trim(), @"[\r\n""]+", "");
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
            string m = Regex.Replace(Regex.Replace((rawModel ?? "").Trim(), @"^strata-", "", RegexOptions.IgnoreCase), @"\.(?:json|strata)$", "", RegexOptions.IgnoreCase).ToLowerInvariant();
            string f = (rawFamily ?? "").Trim().ToLowerInvariant();
            if (f == "hy4" || f == "hy4_flash" || f == "hy4flash" || f == "hunyuan4" || f == "tencent-hy4-flash")
                f = "hy4-flash";

            Match match;
            if ((match = Regex.Match(m, @"^deepseek-v4\.1-flash-(.+)$", RegexOptions.IgnoreCase)).Success) {
                m = match.Groups[1].Value; f = "deepseek41";
            } else if (Regex.IsMatch(m, @"^(?:tencent[-/])?hy4(?:[-_]?flash)?(?:-strata)?$", RegexOptions.IgnoreCase)) {
                f = "hy4-flash";
            } else if ((match = Regex.Match(m, @"^(?:tencent[-/])?hy4(?:[-_]?flash)?[-_](.+?)(?:-strata)?$", RegexOptions.IgnoreCase)).Success) {
                m = match.Groups[1].Value; f = "hy4-flash";
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
                else if (Regex.IsMatch(m, @"^(?:hy4|sq-(?:64|96|128)(?:-strata)?$)", RegexOptions.IgnoreCase)) f = "hy4-flash";
                else if (Regex.IsMatch(m, @"^iq1_m$", RegexOptions.IgnoreCase)) f = "coder";
                else if (Regex.IsMatch(m, @"^gyro-", RegexOptions.IgnoreCase)) f = "gyro";
                else if (Regex.IsMatch(m, @"^nvfp4", RegexOptions.IgnoreCase)) f = "nvfp4";
                else if (Regex.IsMatch(m, @"^(ud-|q8_0)", RegexOptions.IgnoreCase)) f = "unsloth";
                else f = "qwen";
            }
            if (f == "hy4-flash")
                m = Regex.Replace(m, @"-strata$", "", RegexOptions.IgnoreCase);

            string fTitle;
            if (!FamilyTitles.TryGetValue(f, out fTitle)) fTitle = "Qwen3.8-Flash-Next";
            return new ModelInfo { Family = f, FamilyTitle = fTitle, Model = m, Display = string.Format("[{0}]  {1}", fTitle, m) };
        }

        private static ThinkingProfile GetThinkingProfile(string family) {
            if (family == "deepseek41")
                return new ThinkingProfile { Key = "deepseek41", UiLevels = new[] { "auto", "off", "low", "medium", "high", "max" }, Efforts = new[] { "low", "medium", "high", "max" }, DefaultLevel = "high" };
            if (family == "hy4-flash")
                return new ThinkingProfile { Key = "hy4-flash", UiLevels = new[] { "auto", "off", "low", "medium", "high" }, Efforts = new[] { "low", "medium", "high" }, DefaultLevel = "high" };
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

        private List<ModelInfo> DiscoverReadyModelsBackground() {
            Dictionary<string, ModelInfo> foundMap = new Dictionary<string, ModelInfo>(StringComparer.OrdinalIgnoreCase);
            List<ModelInfo> ordered = new List<ModelInfo>();
            Action<ModelInfo> addInfo = delegate(ModelInfo info) {
                if (info != null && !foundMap.ContainsKey(info.Display)) { foundMap[info.Display] = info; ordered.Add(info); }
            };
            Action<string, string> addMod = delegate(string mod, string fam) {
                if (!string.IsNullOrEmpty(mod)) addInfo(ResolveModelInfo(mod, fam));
            };

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
                                JavaScriptSerializer bgJson = new JavaScriptSerializer();
                                object parsed = bgJson.DeserializeObject(cliJson.Substring(start));
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
                                            addMod(id, GetJsonStr(it, "family"));
                                        }
                                    }
                                }
                            }
                        }
                    }
                } catch { }
            }

            JavaScriptSerializer fsJson = new JavaScriptSerializer();
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
                    Dictionary<string, object> c = AsDict(fsJson.DeserializeObject(File.ReadAllText(cfgFile, Encoding.UTF8)));
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
                        if (hasFiles) {
                            string name = Path.GetFileName(sub);
                            ModelInfo packInfo = ResolveModelInfo(name, "");
                            readyPacks.Add(name);
                            if (packInfo != null && !string.IsNullOrEmpty(packInfo.Model))
                                readyPacks.Add(packInfo.Family + ":" + packInfo.Model);
                            addInfo(packInfo);
                        }
                    }
                } catch { }
            }

            foreach (string dir in new[] { installDir, launchDir }) {
                if (!Directory.Exists(dir)) continue;
                try {
                    foreach (string fPath in Directory.EnumerateFiles(dir, "*.json")) {
                        try {
                            Dictionary<string, object> j = AsDict(fsJson.DeserializeObject(File.ReadAllText(fPath, Encoding.UTF8)));
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
                                if (packOk || readyPacks.Contains(info.Model) || readyPacks.Contains(info.Family + ":" + info.Model) || File.Exists(Path.Combine(launchDir, baseName + ".engine.log")) || PathExists(GetJsonStr(j, "model_path")))
                                    addInfo(info);
                            }
                        } catch { }
                    }
                } catch { }
            }
            return ordered;
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
        
        private CardPanel MakeCard(float[] cols, bool autoSize, Padding pad) {
            CardPanel c = new CardPanel {
                ColumnCount = cols.Length, Dock = autoSize ? DockStyle.Top : DockStyle.Fill, AutoSize = autoSize,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = pad
            };
            c.SuspendLayout();
            foreach (float w in cols) c.ColumnStyles.Add(new ColumnStyle(w <= 0f ? SizeType.AutoSize : SizeType.Percent, Math.Max(w, 0f)));
            return c;
        }

        private Label AddHead(CardPanel c, string title, int span) {
            Label l = new Label {
                Text = title, Font = fontHead, ForeColor = Ui.T.FgMuted, BackColor = Ui.T.BgCard,
                Tag = "card-head", AutoSize = true, UseMnemonic = false, Margin = new Padding(0, 0, 0, Ui.S(7))
            };
            c.Controls.Add(l, 0, 0);
            if (span > 1) c.SetColumnSpan(l, span);
            return l;
        }

        private Label AddFieldLbl(CardPanel c, string text, int col, int row, int span, int cols, int topGap) {
            int ml = col > 0 ? Ui.S(5) : 0, mr = (col + span < cols) ? Ui.S(5) : 0;
            Label l = new Label {
                Text = text, Font = fontLbl, ForeColor = Ui.T.FgMuted, BackColor = Ui.T.BgCard,
                Tag = "card-muted", AutoSize = true, AutoEllipsis = true, UseMnemonic = false, Margin = new Padding(ml, topGap, mr, Ui.S(3))
            };
            c.Controls.Add(l, col, row);
            if (span > 1) c.SetColumnSpan(l, span);
            return l;
        }

        private void PlaceField(CardPanel c, Control ctrl, int col, int row, int span, int cols, int height) {
            int ml = col > 0 ? Ui.S(5) : 0, mr = (col + span < cols) ? Ui.S(5) : 0;
            ctrl.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            if (height > 0) {
                ctrl.Height = height;
                ctrl.MinimumSize = new Size(0, height);
                ctrl.MaximumSize = new Size(0, height);
            }
            ctrl.Margin = new Padding(ml, 0, mr, 0);
            c.Controls.Add(ctrl, col, row);
            if (span > 1) c.SetColumnSpan(ctrl, span);
        }

        private void SetTip(Control c, string tip) {
            toolTip.SetToolTip(c, tip);
            InputWrap iw = c as InputWrap;
            if (iw != null) toolTip.SetToolTip(iw.Box, tip);
            ThemedCombo tc = c as ThemedCombo;
            if (tc != null && tc.EditBox != null) toolTip.SetToolTip(tc.EditBox, tip);
        }

        private void BuildUi() {
            Text = "Strata Engine Controller";
            try { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); if (appIcon != null) Icon = appIcon; } catch { }
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            Size = new Size(Ui.S(960), Ui.S(680));
            MinimumSize = new Size(Ui.S(860), Ui.S(560));
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Ui.T.BgCanvas;
            ForeColor = Ui.T.FgText;
            Font = fontUI;
            toolTip = new ToolTip { AutoPopDelay = 12000, InitialDelay = 400, ReshowDelay = 200 };

            int inputH = Math.Max(Ui.S(32), fontUI.Height + Ui.S(14));
            int btnH   = Math.Max(Ui.S(34), fontBold.Height + Ui.S(16));
            int pillH  = Math.Max(Ui.S(26), fontLbl.Height + Ui.S(10));
            Padding cardPad = new Padding(Ui.S(15), Ui.S(12), Ui.S(15), Ui.S(12));

            SuspendLayout();
            rootGrid = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, BackColor = Ui.T.BgCanvas,
                Padding = new Padding(Ui.S(16), Ui.S(12), Ui.S(16), Ui.S(12))
            };
            rootGrid.SuspendLayout();
            rootGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58f));
            rootGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));
            rootGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rootGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rootGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            rootGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(rootGrid);

            // Row 0: Telemetry / Status Strip
            cardStatus = MakeCard(new[] { 100f, 0f, 0f, 0f }, true, new Padding(Ui.S(14), Ui.S(9), Ui.S(14), Ui.S(9)));
            cardStatus.RowCount = 1;
            cardStatus.RowStyles.Add(new RowStyle(SizeType.Absolute, pillH));
            cardStatus.Margin = new Padding(0, 0, 0, Ui.S(10));
            statusHeader = new StatusHeader(fontBold, fontLbl) {
                Anchor = AnchorStyles.Left | AnchorStyles.Right, Height = pillH,
                MinimumSize = new Size(0, pillH), MaximumSize = new Size(0, pillH),
                Margin = new Padding(0, 0, Ui.S(10), 0)
            };
            pillRam      = new StatusPill("RAM --", PillKind.Neutral, false, fontLbl, pillH) { Anchor = AnchorStyles.Right, Margin = new Padding(0, 0, Ui.S(6), 0) };
            pillEndpoint = new StatusPill("127.0.0.1:8080", PillKind.Neutral, true, fontLbl, pillH) { Anchor = AnchorStyles.Right, Margin = new Padding(0, 0, Ui.S(8), 0) };
            themeToggle  = new ThemeToggle(pillH) { Anchor = AnchorStyles.Right, Margin = Padding.Empty };
            cardStatus.Controls.Add(statusHeader, 0, 0);
            cardStatus.Controls.Add(pillRam, 1, 0);
            cardStatus.Controls.Add(pillEndpoint, 2, 0);
            cardStatus.Controls.Add(themeToggle, 3, 0);
            cardStatus.ResumeLayout(false);
            rootGrid.Controls.Add(cardStatus, 0, 0);
            rootGrid.SetColumnSpan(cardStatus, 2);

            // Row 1 Left: MODEL & PRESET (58%, 3 cols: 32/38/30, mirrored 5-row rhythm)
            CardPanel cardModel = MakeCard(new[] { 32f, 38f, 30f }, true, cardPad);
            cardModel.RowCount = 5;
            cardModel.Margin = new Padding(0, 0, Ui.S(6), Ui.S(10));
            cardModel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cardModel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cardModel.RowStyles.Add(new RowStyle(SizeType.Absolute, inputH));
            cardModel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cardModel.RowStyles.Add(new RowStyle(SizeType.Absolute, inputH));
            AddHead(cardModel, "MODEL & PRESET", 3);
            AddFieldLbl(cardModel, "Model", 0, 1, 2, 3, 0);
            AddFieldLbl(cardModel, "Speed (DS41)", 2, 1, 1, 3, 0);
            cmbModel = new ThemedCombo(false, fontUI);
            cmbSpeed = new ThemedCombo(false, fontUI);
            cmbSpeed.SetItems(new[] { "full", "faster" }, "full");
            PlaceField(cardModel, cmbModel, 0, 2, 2, 3, inputH);
            PlaceField(cardModel, cmbSpeed, 2, 2, 1, 3, inputH);
            AddFieldLbl(cardModel, "Context", 0, 3, 1, 3, Ui.S(9));
            AddFieldLbl(cardModel, "Preset (--save-preset)", 1, 3, 2, 3, Ui.S(9));
            cmbCtx = new ThemedCombo(true, fontUI);
            cmbCtx.SetItems(new[] { "256k", "128k", "64k", "32k", "16k", "8k" }, "256k");
            wrapPreset = new InputWrap("e.g. ds41-coder-256", fontUI);
            txtPreset = wrapPreset.Box;
            PlaceField(cardModel, cmbCtx, 0, 4, 1, 3, inputH);
            PlaceField(cardModel, wrapPreset, 1, 4, 2, 3, inputH);
            cardModel.ResumeLayout(false);
            rootGrid.Controls.Add(cardModel, 0, 1);

            // Row 1 Right: ENGINE & OMP (42%, 2 cols: 46/54, mirrored 5-row rhythm)
            CardPanel cardOpts = MakeCard(new[] { 46f, 54f }, true, cardPad);
            cardOpts.RowCount = 5;
            cardOpts.Margin = new Padding(Ui.S(6), 0, 0, Ui.S(10));
            cardOpts.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cardOpts.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cardOpts.RowStyles.Add(new RowStyle(SizeType.Absolute, inputH));
            cardOpts.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cardOpts.RowStyles.Add(new RowStyle(SizeType.Absolute, inputH));
            AddHead(cardOpts, "ENGINE & OMP OPTIONS", 2);
            AddFieldLbl(cardOpts, "PLE I/O", 0, 1, 1, 2, 0);
            AddFieldLbl(cardOpts, "VRAM Reserve (MiB)", 1, 1, 1, 2, 0);
            cmbPleIo = new ThemedCombo(false, fontUI);
            cmbPleIo.SetItems(new[] { "auto", "direct", "ram" }, "auto");
            cmbVramReserve = new ThemedCombo(true, fontUI);
            cmbVramReserve.SetItems(new[] { "auto", "1088", "1280", "1536", "2048" }, "auto");
            PlaceField(cardOpts, cmbPleIo, 0, 2, 1, 2, inputH);
            PlaceField(cardOpts, cmbVramReserve, 1, 2, 1, 2, inputH);
            AddFieldLbl(cardOpts, "OMP Thinking", 0, 3, 1, 2, Ui.S(9));
            AddFieldLbl(cardOpts, "OMP Session", 1, 3, 1, 2, Ui.S(9));
            cmbThinking = new ThemedCombo(false, fontUI);
            chkOmpContinue = new ToggleCheck("Continue (-c)", true, fontUI);
            PlaceField(cardOpts, cmbThinking, 0, 4, 1, 2, inputH);
            PlaceField(cardOpts, chkOmpContinue, 1, 4, 1, 2, inputH);
            cardOpts.ResumeLayout(false);
            rootGrid.Controls.Add(cardOpts, 1, 1);

            // Row 2: Flexible Log Card
            CardPanel cardLog = MakeCard(new[] { 100f }, false, cardPad);
            cardLog.RowCount = 2;
            cardLog.Margin = new Padding(0, 0, 0, Ui.S(10));
            cardLog.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cardLog.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            AddHead(cardLog, "ENGINE & ACTIVITY LOG", 1);
            logWrap = new LogWrap(fontMono) { Dock = DockStyle.Fill, Margin = Padding.Empty };
            txtLog = logWrap.Box;
            cardLog.Controls.Add(logWrap, 0, 1);
            cardLog.ResumeLayout(false);
            rootGrid.Controls.Add(cardLog, 0, 2);
            rootGrid.SetColumnSpan(cardLog, 2);

            // Row 3 Left: TOOLS & OMP
            CardPanel cardTools = MakeCard(new[] { 25f, 25f, 25f, 25f }, true, new Padding(Ui.S(15), Ui.S(11), Ui.S(15), Ui.S(11)));
            cardTools.RowCount = 2;
            cardTools.Margin = new Padding(0, 0, Ui.S(6), 0);
            cardTools.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cardTools.RowStyles.Add(new RowStyle(SizeType.Absolute, btnH));
            AddHead(cardTools, "TOOLS & OMP", 4);
            btnTune    = new RoundButton("Tune Model", BtnTone.Accent, fontBold);
            btnWireOmp = new RoundButton("Sync omp",   BtnTone.Neutral, fontUI);
            btnRunOmp  = new RoundButton("Resume omp", BtnTone.Neutral, fontUI);
            btnOpenLog = new RoundButton("Debug Log",  BtnTone.Neutral, fontUI);
            PlaceField(cardTools, btnTune,    0, 1, 1, 4, btnH);
            PlaceField(cardTools, btnWireOmp, 1, 1, 1, 4, btnH);
            PlaceField(cardTools, btnRunOmp,  2, 1, 1, 4, btnH);
            PlaceField(cardTools, btnOpenLog, 3, 1, 1, 4, btnH);
            cardTools.ResumeLayout(false);
            rootGrid.Controls.Add(cardTools, 0, 3);

            // Row 3 Right: ENGINE CONTROL (Primary CTA bottom-right)
            cardControl = MakeCard(new[] { 28f, 34f, 38f }, true, new Padding(Ui.S(15), Ui.S(11), Ui.S(15), Ui.S(11)));
            cardControl.RowCount = 2;
            cardControl.Margin = new Padding(Ui.S(6), 0, 0, 0);
            cardControl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cardControl.RowStyles.Add(new RowStyle(SizeType.Absolute, btnH));
            AddHead(cardControl, "ENGINE CONTROL", 3);
            btnStop      = new RoundButton("Stop",         BtnTone.Warning, fontBold);
            btnForceStop = new RoundButton("Force Stop",   BtnTone.Danger,  fontBold);
            btnStart     = new RoundButton("Start Engine", BtnTone.Primary, fontBold);
            PlaceField(cardControl, btnStop,      0, 1, 1, 3, btnH);
            PlaceField(cardControl, btnForceStop, 1, 1, 1, 3, btnH);
            PlaceField(cardControl, btnStart,     2, 1, 1, 3, btnH);
            cardControl.ResumeLayout(false);
            rootGrid.Controls.Add(cardControl, 1, 3);

            SetTip(cmbPleIo, "direct: keeps 26.8 GiB PLE table on NVMe SSD (prevents startup/tuning RAM spikes via STRATA_PLE_AUTO_RAM=0).\nauto: lets Strata lock PLE in RAM if free RAM allows.\nram: forces PLE lock in RAM.");
            SetTip(cmbVramReserve, "MiB kept free on GPU (--vram-reserve-mib). Recommended 1280-1536 on 12 GB Windows cards to avoid WDDM paging (#2065).");
            SetTip(cmbThinking, "Updates modelRoles.default :level suffix and defaultThinkingLevel in ~/.omp/agent/config.yml without overwriting other keys.");
            SetTip(chkOmpContinue, "When unchecked (recommended), Launch omp starts a fresh session using --thinking. When checked, adds -c to resume the previous session.");
            SetTip(btnStop, "Stop Strata engine gracefully using --stop and cleanup.");
            SetTip(btnOpenLog, "Open full debug log file (" + Program.DebugLogPath + ").");
            SetTip(pillEndpoint, "Local OpenAI-compatible inference endpoint: http://127.0.0.1:8080/v1");
            SetTip(pillRam, "Host physical system memory usage.");
            SetTip(themeToggle, "Switch to light mode");

            rootGrid.ResumeLayout(false);
            ResumeLayout(true);
            SyncModelSelection();
        }

        private void ApplyTheme(bool dark) {
            Ui.T = dark ? UiTheme.Dark : UiTheme.Light;
            if (IsHandleCreated) Ui.DwmTitleBar(Handle, Ui.T.IsDark);
            SuspendLayout();
            BackColor = Ui.T.BgCanvas;
            ForeColor = Ui.T.FgText;
            rootGrid.BackColor = Ui.T.BgCanvas;
            ApplyThemeRecursive(rootGrid);
            SetTip(themeToggle, Ui.T.IsDark ? "Switch to light mode" : "Switch to dark mode");
            ResumeLayout(true);
            Invalidate(true);
            Update();
        }

        private static void ApplyThemeRecursive(Control parent) {
            foreach (Control c in parent.Controls) {
                CardPanel cp = c as CardPanel;
                if (cp != null) cp.SyncTheme();
                else if (c is InputWrap) ((InputWrap)c).SyncTheme();
                else if (c is LogWrap) ((LogWrap)c).SyncTheme();
                else if (c is ThemedCombo) ((ThemedCombo)c).SyncTheme();
                else if (c is ToggleCheck) ((ToggleCheck)c).SyncTheme();
                else if (c is RoundButton) ((RoundButton)c).SyncTheme();
                else if (c is StatusPill) ((StatusPill)c).SyncTheme();
                else if (c is StatusHeader) ((StatusHeader)c).SyncTheme();
                else if (c is ThemeToggle) ((ThemeToggle)c).SyncTheme();
                else if (c is Label) {
                    string tag = c.Tag as string;
                    if (tag == "card-head" || tag == "card-muted") { c.ForeColor = Ui.T.FgMuted; c.BackColor = Ui.T.BgCard; }
                    else if (tag == "card-dim") { c.ForeColor = Ui.T.FgDim; c.BackColor = Ui.T.BgCard; }
                }
                if (c.HasChildren && !(c is InputWrap) && !(c is LogWrap) && !(c is ThemedCombo))
                    ApplyThemeRecursive(c);
            }
        }

        private ModelInfo GetSelectedModelInfo() {
            string item = cmbModel.SelectedItem;
            ModelInfo info;
            return (!string.IsNullOrEmpty(item) && modelMap.TryGetValue(item, out info)) ? info : null;
        }

        private void UpdateThinkingOptions(string family, string preferred) {
            ThinkingProfile prof = GetThinkingProfile(family);
            string cur = !string.IsNullOrEmpty(preferred) ? preferred : (!string.IsNullOrEmpty(cmbThinking.SelectedItem) ? cmbThinking.SelectedItem : initialThinking);
            string pick = ResolveThinkingChoice(cur, prof);
            suppressThinkingSync = true;
            try {
                if (lastThinkingKey != prof.Key || cmbThinking.Items.Count != prof.UiLevels.Length) {
                    cmbThinking.SetItems(prof.UiLevels, pick);
                    lastThinkingKey = prof.Key;
                } else if (cmbThinking.SelectedItem != pick) {
                    cmbThinking.SelectedItem = pick;
                }
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
            string pleMode = cmbPleIo.SelectedItem ?? "direct";
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
                Text = "Debug Log - " + Program.DebugLogPath, Size = new Size(Ui.S(840), Ui.S(540)),
                StartPosition = FormStartPosition.CenterParent, AutoScaleMode = AutoScaleMode.None,
                BackColor = Ui.T.BgCanvas, ForeColor = Ui.T.FgText, Font = fontUI, KeyPreview = true, Icon = appIcon,
                Padding = new Padding(Ui.S(12))
            }) {
                dlg.HandleCreated += delegate { Ui.DwmTitleBar(dlg.Handle, Ui.T.IsDark); };
                LogWrap wrap = new LogWrap(fontMono) { Dock = DockStyle.Fill };
                wrap.Box.WordWrap = false;
                wrap.Box.ScrollBars = ScrollBars.Both;
                wrap.Box.Text = content;
                dlg.Controls.Add(wrap);
                dlg.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) dlg.Close(); };
                dlg.Shown += delegate { wrap.Box.SelectionStart = wrap.Box.TextLength; wrap.Box.ScrollToCaret(); };
                dlg.ShowDialog(this);
            }
        }

        private void WireEvents() {
            themeToggle.Click += delegate { ApplyTheme(!Ui.T.IsDark); };
            cmbModel.SelectedIndexChanged += delegate { SyncModelSelection(); };
            cmbCtx.SelectedIndexChanged   += delegate { SyncModelSelection(); };
            cmbCtx.TextChanged            += delegate { SyncModelSelection(); };
            chkOmpContinue.CheckedChanged += delegate { btnRunOmp.Text = chkOmpContinue.Checked ? "Resume omp" : "Launch omp"; };
            cmbThinking.SelectedIndexChanged += delegate {
                if (!suppressThinkingSync) SyncOmpConfigFiles(GetSelectedModelInfo(), cmbThinking.SelectedItem ?? "", null);
            };
            btnStart.Click += delegate { InvokeStrataLaunch("", "Starting", false); };
            btnTune.Click += delegate {
                ModelInfo sel = GetSelectedModelInfo();
                string preset = SanitizeArg(txtPreset.Text);
                if (sel == null || string.IsNullOrEmpty(preset)) { MessageBox.Show(this, "Select a downloaded ready model and preset name first.", "Missing Input"); return; }
                string pleMode = cmbPleIo.SelectedItem ?? "auto";
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
                string p = SanitizeArg(txtPreset.Text);
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
            btnWireOmp.Click += async delegate {
                pendingOmpSync = null; pendingActionNext = null;
                StopTrackedProc(ref actionProc, ref actionSink, actionStartFt);
                btnWireOmp.Enabled = false;
                string probeId;
                try {
                    probeId = await ProbeLocalServerAsync(closeCts.Token);
                } finally {
                    if (!IsDisposed && IsHandleCreated) btnWireOmp.Enabled = true;
                }
                if (IsDisposed || !IsHandleCreated) return;
                if (probeId == null) {
                    WriteGuiLog("WARNING: Server is not responding on http://127.0.0.1:8080; start the engine before clicking 'Sync omp'.");
                    return;
                }
                ModelInfo sel = GetSelectedModelInfo();
                string lvl = cmbThinking.SelectedItem ?? "";
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
                string lvl = cmbThinking.SelectedItem ?? "";
                string resolvedLvl = !string.IsNullOrEmpty(lvl) ? ResolveThinkingChoice(lvl, GetThinkingProfile(sel != null ? sel.Family : "qwen")) : "";
                SyncOmpConfigFiles(sel, resolvedLvl, null);
                string dir = userProfile;
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
            string ctx = SanitizeArg(cmbCtx.Text), preset = SanitizeArg(txtPreset.Text);
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

            string spdArg = (sel.Family == "deepseek41") ? " --speed \"" + SanitizeArg(cmbSpeed.Text) + "\"" : "";
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

        private async Task<string> ProbeLocalServerAsync(CancellationToken ct) {
            bool listening = await Task.Run(delegate {
                try {
                    foreach (IPEndPoint ep in ipProps.GetActiveTcpListeners()) if (ep.Port == 8080) return true;
                    return false;
                } catch { return true; }
            });
            if (!listening || ct.IsCancellationRequested || localHttp == null) return null;
            try {
                using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct)) {
                    linked.CancelAfter(500);
                    using (HttpResponseMessage resp = await localHttp.GetAsync(LocalModelsUrl, linked.Token).ConfigureAwait(false)) {
                        if ((int)resp.StatusCode >= 500) return null;
                        if (resp.IsSuccessStatusCode) {
                            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                            Dictionary<string, object> root = AsDict(new JavaScriptSerializer().DeserializeObject(body));
                            object dataObj;
                            if (root != null && root.TryGetValue("data", out dataObj) && dataObj is object[]) {
                                object[] data = (object[])dataObj;
                                if (data.Length > 0) return GetJsonStr(AsDict(data[0]), "id");
                            }
                        }
                        return "";
                    }
                }
            } catch { return null; }
        }

        private async Task<PollSnapshot> CaptureSnapshotAsync(CancellationToken ct) {
            PollSnapshot snap = await Task.Run(delegate {
                PollSnapshot s = new PollSnapshot { LoadedModelId = "", RamText = "RAM N/A" };
                try {
                    ulong total = compInfo.TotalPhysicalMemory, avail = compInfo.AvailablePhysicalMemory;
                    if (total > 0) {
                        ulong used = total > avail ? total - avail : 0UL;
                        s.RamPct = (int)Math.Round(used * 100.0 / total);
                        s.RamText = string.Format("RAM {0:0.0}/{1:0.0} GB ({2}%)", used / 1073741824.0, total / 1073741824.0, s.RamPct);
                    }
                } catch { }
                using (Process eng = GetEngineProc()) {
                    if (eng != null) {
                        s.HasEngine = true;
                        s.EnginePid = eng.Id;
                        try { s.EngineName = eng.ProcessName; } catch { s.EngineName = "strata"; }
                    }
                }
                return s;
            });
            if (snap.HasEngine && !ct.IsCancellationRequested) {
                string loaded = await ProbeLocalServerAsync(ct);
                if (loaded != null) { snap.Serving = true; snap.LoadedModelId = loaded; }
            }
            return snap;
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

        private async void OnPollTimerTick(object sender, EventArgs e) {
            await PollOnceAsync();
        }

        private async Task PollOnceAsync() {
            if (isPolling || isPrompting || IsDisposed || !IsHandleCreated) return;
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
                            string loadedId = await ProbeLocalServerAsync(closeCts.Token);
                            if (IsDisposed || !IsHandleCreated) return;
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

                PollSnapshot snap = await CaptureSnapshotAsync(closeCts.Token);
                if (IsDisposed || !IsHandleCreated) return;

                bool serving = snap.HasEngine && snap.Serving;
                if (serving) {
                    if (lastSyncedEnginePid != snap.EnginePid && !string.IsNullOrEmpty(snap.LoadedModelId)) {
                        lastSyncedEnginePid = snap.EnginePid;
                        string matchedKey = FindModelMapKey(snap.LoadedModelId);
                        string curSelected = cmbModel.SelectedItem ?? "";
                        if (!string.IsNullOrEmpty(matchedKey) && curSelected != matchedKey) cmbModel.SelectedItem = matchedKey;
                        else if (snap.LoadedModelId != "strata") UpdateThinkingOptions(ResolveModelInfo(snap.LoadedModelId, "").Family, null);
                    }
                    statusHeader.SetState(StatusState.Serving, "SERVING · READY", string.Format("{0}.exe (PID {1})", snap.EngineName, snap.EnginePid));
                    pillEndpoint.UpdatePill("127.0.0.1:8080", PillKind.Ok);
                } else if (snap.HasEngine || IsProcRunning(launchProc) || pendingLaunchNext != null) {
                    string info = snap.HasEngine ? string.Format("PID {0}", snap.EnginePid) : "Initializing...";
                    statusHeader.SetState(StatusState.Loading, "LOADING MODEL...", "Starting engine (" + info + ")");
                    pillEndpoint.UpdatePill("127.0.0.1:8080", PillKind.Warn);
                } else {
                    lastSyncedEnginePid = -1;
                    statusHeader.SetState(StatusState.Stopped, "STOPPED", "No engine process running");
                    pillEndpoint.UpdatePill("127.0.0.1:8080", PillKind.Neutral);
                }

                pillRam.UpdatePill(snap.RamText, snap.RamPct >= 88 ? PillKind.Warn : PillKind.Neutral);
                if (cardControl.ReadyHighlight != serving) {
                    cardControl.ReadyHighlight = serving;
                    cardControl.Invalidate();
                }
            } finally { isPolling = false; }
        }

        protected override async void OnShown(EventArgs e) {
            base.OnShown(e);
            Activate();
            statusHeader.SetState(StatusState.Checking, "CHECKING...", "Scanning installed models & engine...");

            List<ModelInfo> discovered = await Task.Run(delegate {
                using (Process initEng = GetEngineProc()) {
                    if (initEng == null && Directory.Exists(launchDir)) {
                        try {
                            foreach (string f in Directory.EnumerateFiles(launchDir, "*.engine.log"))
                                try { File.WriteAllText(f, "", Program.Utf8NoBom); } catch { }
                        } catch { }
                    }
                }
                return DiscoverReadyModelsBackground();
            });

            if (IsDisposed || !IsHandleCreated) return;
            modelMap.Clear();
            modelKeys.Clear();
            foreach (ModelInfo v in discovered) {
                if (!modelMap.ContainsKey(v.Display)) {
                    modelMap[v.Display] = v;
                    modelKeys.Add(v.Display);
                    WriteGuiLog(string.Format("Ready Model: {0} [--family {1} --model {2}]", v.FamilyTitle, v.Family, v.Model));
                }
            }
            if (modelKeys.Count == 0)
                WriteGuiLog("WARNING: No downloaded/prepared Strata models detected. Run 'strata launch' in terminal to download one.");

            cmbModel.SetItems(modelKeys, modelKeys.Count > 0 ? modelKeys[0] : null);
            SyncModelSelection();
            await PollOnceAsync();
            if (!IsDisposed && IsHandleCreated && pollTimer != null) pollTimer.Start();
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

        protected override void OnFormClosed(FormClosedEventArgs e) {
            CleanupResources();
            base.OnFormClosed(e);
        }

        private void CleanupResources() {
            if (resourcesDisposed) return;
            resourcesDisposed = true;
            if (closeCts != null) { try { closeCts.Cancel(); closeCts.Dispose(); } catch { } closeCts = null; }
            if (localHttp != null) { try { localHttp.Dispose(); } catch { } localHttp = null; }
            if (launchSink != null) { launchSink.Cancelled = true; launchSink = null; }
            if (actionSink != null) { actionSink.Cancelled = true; actionSink = null; }
            if (pollTimer != null) { pollTimer.Stop(); pollTimer.Dispose(); pollTimer = null; }
            if (launchProc != null) { try { launchProc.Dispose(); } catch { } launchProc = null; }
            if (actionProc != null) { try { actionProc.Dispose(); } catch { } actionProc = null; }
            if (toolTip != null) { toolTip.Dispose(); toolTip = null; }
            if (appIcon != null) { appIcon.Dispose(); appIcon = null; }
            fontHead.Dispose(); fontLbl.Dispose(); fontUI.Dispose(); fontBold.Dispose(); fontMono.Dispose();
        }

        protected override void Dispose(bool disposing) {
            if (disposing) CleanupResources();
            base.Dispose(disposing);
        }
    }
}