---
name: winforms-ui-design
description: Design, build, and repair modern Windows Forms (WinForms) desktop interfaces in C#. Use when creating new WinForms apps, rescuing cluttered or clipped legacy layouts, implementing dark, light, or dual-theme card-based UIs with runtime light/dark switching, fixing DPI scaling and alignment bugs, or writing lean single-file WinForms tools across standalone csc and Roslyn/SDK toolchains.
---

# Modern WinForms UI Design & Repair

Build clean, responsive, flicker-free WinForms interfaces with intentional hierarchy, DPI resilience, modern custom-painted chrome, dual dark/light theme fidelity, and minimal code footprint.

---

## 1. Design Repair Audit

When rescuing an existing interface, diagnose defects in this order before rewriting code:

1. **Layout Topology & Whitespace:** Flag tall single-column stacks of `GroupBox` panels, dead horizontal bands, ragged right margins, or ungrouped orphan controls.
2. **Clipping & DPI Scaling:** Flag hardcoded `Absolute` heights on container rows holding text or captions, mixed `AutoScaleMode.Dpi` with manual `DeviceDpi` multipliers, and fixed narrow columns that truncate labels or buttons at `MinimumSize`.
3. **Baseline & Vertical Alignment:** Flag rows mixing native `TextBox`, `ComboBox`, `CheckBox`, and `Button` without unified heights or vertical centering, and side-by-side cards with mismatched internal row rhythms.
4. **Workflow & Visual Priority:** Flag primary CTAs placed *above* inputs they depend on, single concatenated status bars that truncate critical diagnostics, and ambiguous enabled/disabled/read-only states.
5. **Visual Polish & Native Artifacts:** Flag harsh system borders in dark themes, flat `#FFFFFF`-on-`#FFFFFF` surfaces or illegible pastel yellow/mint text in light themes, mismatched window title bars, etched illegible disabled buttons, flickering resizes, bright gray disabled `TextBox` fields, and stale colors left behind after runtime theme switches.
6. **Responsiveness & Hygiene:** Flag blocking I/O on the UI thread, unnecessary full-form repaints on timers, missing `[STAThread]`, leaked GDI+ objects/fonts/timers, and unsafe shell command quoting.

---

## 2. Targeted Fixes for Recurring WinForms Anti-Patterns

| Anti-Pattern | Root Failure | Fix |
|---|---|---|
| Stacked 1-column `GroupBox` (`BackColor == Form.BackColor`) | Dated Win32 chrome, wasted width, floating caption steals ~20px and clips rows | Replace with a responsive multi-column `TableLayoutPanel` hosting double-buffered rounded `CardPanel : TableLayoutPanel` cards on a contrasting canvas. |
| Hardcoded `RowStyle(Absolute, ...)` on sections + `AutoScaleMode.Dpi` | Double-scaling or clipped bottom controls across DPI factors and font sizes | Set `AutoScaleMode = AutoScaleMode.None` and scale once via a single `S(float px)` DPI helper (or rely strictly on framework auto-scaling without manual multipliers). Size fixed cards with `AutoSize = true` inside `RowStyle(AutoSize)` outer rows; compute `inputH = Math.Max(S(32), fontUI.Height + S(14))`. |
| Unwrapped `TextBox`, stock `ComboBox`, `CheckBox`, and `Button` on one row | Native controls have different fixed heights and pin unevenly under `Dock.Fill` | Wrap borderless `TextBox` in a rounded `InputWrap` (`OnResize` vertically centers `PreferredHeight`, adds focus ring, click-padding-to-focus, and `EM_SETCUEBANNER` cue text). Use a custom-painted `ThemedCombo` (or owner-drawn `ComboBox` self-calibrated to `inputH`) matching input chrome. Center custom `ToggleCheck` and `RoundButton` to exact row heights. |
| Inner borderless `TextBox` of an `InputWrap` given `Dock = DockStyle.Fill` | WinForms docking overrides manual `SetBounds` centering: a single-line `TextBox` pins to the top edge, overwrites the painted top border (open-top box), leaves dead space below, and crowds the label above | Never dock the inner `TextBox`; only the outer `InputWrap` participates in grid layout (anchored `Left \| Right` with locked height `inputH`). Let `PositionBox()` / `OnResize` exclusively set the inner `TextBox` bounds (`y = (Height - PreferredHeight) / 2`, `x` inset by `padX`) so the text floats centered inside the painted border. |
| `Dock = DockStyle.Fill` on fixed-height controls or `AutoSize` cards inside `TableLayoutPanel` | `Panel.DefaultSize` (`100px`) and `TableLayoutPanel` dumping surplus vertical space into the last `AutoSize` row stretch bottom-row controls; `Control.GetPreferredSize()` then ratchets that inflated height across resizes | Give `AutoSize` cards `Dock = DockStyle.Top`. Inside each card, use `RowStyle(AutoSize)` for label rows and computed `RowStyle(SizeType.Absolute, inputH)` / `btnH` (derived from DPI + font height, never raw pixels) for single-line control rows. Place `InputWrap`, `ThemedCombo`, `ToggleCheck`, and `RoundButton` with `Anchor = AnchorStyles.Left \| AnchorStyles.Right` and `MinimumSize`/`MaximumSize = new Size(0, h)`. Reserve `Dock = DockStyle.Fill` + `RowStyle(Percent, 100)` strictly for the expanding content row. |
| Default `Label.UseMnemonic = true` on headers and field labels | Ampersands (`&`) are silently stripped and turned into accelerator underlines (e.g., `"ENGINE & OMP"` renders as `"ENGINE OMP"`) | Set `UseMnemonic = false` in `AddHead` and `AddLbl` builders for all non-accelerator labels, and pass `TextFormatFlags.NoPrefix` when custom-painting text. |
| Uniform right margin (`Padding(2, 1, 14, 1)`) on all fields | Indents rightmost column; creates unequal split-column widths | Apply symmetric gutters: `left = col > 0 ? S(5) : 0`, `right = (col + span < cols) ? S(5) : 0`. Outer edges stay flush across full-span and split rows. |
| Primary CTA placed above optional configuration | Violates Z-pattern reading order; users trigger actions prematurely | Order top-to-bottom: **Status/Header -> Core Inputs -> Expanding Content -> Secondary Utilities (bottom-left) + Primary CTA (bottom-right)**. |
| Stock `Button` disabled on colored background | Renders bright fill with unreadable etched gray text | Override `OnPaint` on a `RoundButton : Button` (`UserPaint`, `OptimizedDoubleBuffer`) with explicit `Enabled`, `Hover`, `Pressed`, and muted `Disabled` palettes. |
| `TextBox.Enabled = false` in custom themes | OS forces system gray background regardless of palette | Keep `Enabled = true`; set `ReadOnly = true`, then assign themed `BgDisabled` `BackColor` and `FgDim` `ForeColor` *after* setting `ReadOnly`. Re-apply via `InputWrap.SyncTheme()` on theme switches. |
| Hardcoded static colors or naive light-mode inversion | Reusing dark-mode pastels (e.g., `#F2BA42` amber or dark pill fills) on white cards ruins legibility; cached per-control `Color` fields go stale on toggle | Centralize all tokens in immutable `UiTheme.Dark` and `UiTheme.Light` instances behind an active `T` reference. Paint custom controls from `T`, darken light-mode semantic text/accents, and represent transient highlights semantically (booleans/enums, not raw `Color` fields). |
| Long concatenated status bar string | Truncates secondary warnings on narrow viewports | Pair a concise primary status message + glowing indicator dot on the left with compact hoverable `StatusPill` badges (and an optional compact theme toggle) on the right. |
| Synchronous I/O in constructor or timer (`.Result` / `.GetAwaiter().GetResult()`) | Freezes startup and stutters UI on every poll | Run re-entrancy-guarded `async` checks starting in `OnShown`. Check `IsDisposed \|\| !IsHandleCreated` after every `await`. Diff state before calling `Invalidate()` so steady-state polls trigger zero repaints. |
| Bright light title bar on dark form (or stale dark title bar in light mode) | Visual jarring at top edge on startup or after toggling themes | Call `DwmSetWindowAttribute` (attempting attribute `20`, falling back to `19`) with `v = T.IsDark ? 1 : 0` inside `try/catch` in `OnHandleCreated` *and* dynamically inside `ApplyTheme()` (guarded by `IsHandleCreated`). |
| Missing `[STAThread]`; raw newlines in CLI args; leaked handles | Broken clipboard/OLE dialogs; truncated shell commands; resource leaks | Mark `Main()` with `[STAThread]`. Sanitize `\r?\n` before passing multiline input to shell commands. Set `UseShellExecute = true` for Explorer/URL launches. Dispose fonts, timers, tooltips, and HTTP clients in `OnFormClosed`. |

---

## 3. Architecture & Layout Selection

Pick the simplest topology that fits the task without dead whitespace or vertical crowding:

- **Quick Launcher / Focused Dialog (<= 5 fields, 1 primary action):** Single centered column of 1–2 cards (`520–640px` wide) with a docked bottom action footer.
- **Controller / Runner / Parameter Dashboard (5–12 fields + 1 multiline/output zone + secondary tools):** **2-Column Asymmetric Bento Grid** (`58% / 42%` or `60% / 40%`):
  - **Row 0 (`AutoSize`, full span):** Top telemetry/status strip or header bar — host status indicator + message on the left, compact `StatusPill` badges and an optional height-matched theme toggle (`ThemeToggle`) at the trailing right edge so chrome stays on one baseline without adding a row.
  - **Row 1 (`AutoSize`, 2 cards):** Primary target/input card (left) + execution limits/options card (right). Mirror internal row counts so titles, labels, and inputs lock horizontally across both cards.
  - **Row 2 (`Percent 100%`, full span):** **One** flexible card (`AutoSize = false`) hosting multiline input, logs, or a data list that absorbs all vertical resize.
  - **Row 3 (`AutoSize`, 2 cards):** Companion utilities (bottom-left) + Session/Primary CTA card (bottom-right) sharing identical row structures so all action buttons sit on one baseline.
  - **Row 4 (`AutoSize`, full span):** Compact single-line muted hint/footer if needed.
- **Master-Detail / Inspector Workspace (browsable collection + detail editor):** `SplitContainer` (or 2-column grid with fixed/min-width left list + flexible right card stack) with `FixedPanel = Panel1`.
- **Multi-Mode Suite (> 12 fields or distinct workflows):** Slim left navigation rail (icon/label buttons) switching a single active card canvas; avoid stacking more than 4 card rows on one screen or relying on stock `TabControl` chrome unless owner-drawn.

### Spacing & Grid Discipline

- **Outer form padding:** `S(16)` horizontal, `S(12)–S(14)` vertical.
- **Card internal padding:** `S(16)` horizontal, `S(13)–S(15)` vertical.
- **Uniform gutters:** `S(12)` between cards (`S(6)` per adjacent edge); `S(10)` between split columns inside a card (`S(5)` per adjacent edge).
- **Label-to-input rhythm:** `S(4)` between a field label and its control; `S(8)–S(12)` before the next field group.
- **Batch mutations:** Wrap dynamic rebuilds, theme switches, or bulk list updates in `SuspendLayout()`/`ResumeLayout()` and `BeginUpdate()`/`EndUpdate()`.

---

## 4. Visual System & Custom Control Craft

### Three-Tier Surface Hierarchy

Avoid flat single-color gray or `#FFFFFF`-on-`#FFFFFF` whiteouts. Define clear luminance separation in both themes:

- **Canvas (`BgCanvas`):** Framing background around card gutters (`~#111217` dark / `~#F1F3F8` cool slate-white light).
- **Elevated Card (`BgCard`):** Distinct surface step (`~#1A1C23` dark / `#FFFFFF` pure white light) bounded by a crisp 1px `BorderCard` (`~#2C303C` dark / `~#D8DCE8` light).
- **Recessed Input Well (`BgInput`):** Slightly deeper than the card in both modes (`~#13141A` dark / `~#F6F7FB` softly tinted light) with 1px `BorderInput` (`~#383D4E` dark / `~#C4CADA` light), transitioning to `Accent` (`~#58A6FF` dark / `~#2574EB` light) on keyboard focus.
- **Semantic Status Colors:** Reserve green (healthy / primary go), amber (warning / optional missing), and coral-red (error / blocked / destructive) strictly for state and primary actions.

### Light Mode & Runtime Theme Switching

Treat light mode as a first-class palette with explicit contrast engineering rather than an algorithmic color flip:

1. **Dual-Palette Token Object (`UiTheme`):** Store all surfaces, borders, foregrounds, interactive states (`BgBtn`, `BgHover`, `BgPressed`, `BgDrop`, `BgDisabled`), toggle track/knob tokens, and pill triples (`Bg`/`Bd`/`Fg`) in immutable `UiTheme.Dark` and `UiTheme.Light` objects referenced through an active `T`. Custom controls paint straight from `T` with zero stale per-instance `Color` fields.
2. **Light-Mode Contrast & Tactile Rules:**
   - **Deepen semantic foregrounds & accents:** Dark-mode yellows (`#F2BA42`) and pastel mints fail contrast on white cards. Shift to deep accessible ink (`FgText ~#181C26`, `FgMuted ~#586076`, `FgDim ~#80889C`) and saturated darker status/action tones (`Accent ~#2574EB`, `ColorOk ~#1A9448`, `ColorWarn ~#BA760C`, `ColorErr ~#CE2E2E`, primary `Green ~#1B8A3D` with white text).
   - **Invert status pill construction:** Swap dark-mode's deep fill + pastel text for light-mode's soft tinted fill (`#E8F7ED` / `#FEF5E3` / `#FDECED`), medium border (`#9CDAB2` / `#EAC884` / `#F0B2B4`), saturated dot, and deep legible text (`#146634` / `#88540A` / `#9E2226`).
   - **Explicit pressed fills & visible unchecked tracks:** Do not rely on `ControlPaint.Dark(BgBtn, 0.08f)` on bright light buttons (its HLS math can land lighter than `BgHover`); define explicit `BgPressed` and `GreenPressed` tokens visibly deeper than hover. Give unchecked `ToggleCheck` tracks a distinct neutral fill (`~#D2D7E4`) with a crisp white knob (`#FFFFFF`), and paint custom dropdown popups (`BgDrop`) on an elevated `#FFFFFF` surface.
3. **Theme Toggle Control Craft (`ThemeToggle`):**
   - Prefer a compact segmented **Moon / Sun capsule** (`[ ☾ | ☀ ]`) sized to match status pill height (`pH`) at the far right of Row 0 so current mode and target mode are never ambiguous.
   - Inherit from `Button` (`UserPaint`, `OptimizedDoubleBuffer`, `AccessibleName`) so Tab focus, `Accent` focus ring, and `Space`/`Enter` toggling work natively.
   - Draw the Moon and Sun with anti-aliased **GDI+ vector primitives** (never Unicode emoji glyphs, which misalign or render inconsistently across Windows font fallbacks).
4. **Instant, Flicker-Free Runtime Switching (`ApplyTheme`):**
   - Guard `UpdateTitleBarTheme()` with `if (!IsHandleCreated) return;` and call `DwmSetWindowAttribute` (`20`, fallback `19`) with `v = T.IsDark ? 1 : 0` both in `OnHandleCreated` and during `ApplyTheme()`.
   - Wrap the switch in `SuspendLayout()` ... `ResumeLayout(true)` followed by a single unified `Invalidate(true); Update();`.
   - Walk the control tree once (`ApplyThemeRecursive`), using lightweight `Tag` roles on native `Label`s (`"card-muted"`, `"card-dim"`, `"canvas-dim"`, `"status"`), invoking `SyncTheme()` on composite wrappers (`InputWrap` and its inner `TextBox`, `ThemedCombo` and its popup `ListBox`), and invalidating custom controls.
   - **Preserve live state across toggles:** Track transient visual states semantically (e.g., `StatusState` enum, `CardPanel.ReadyHighlight` bool, `TextBox.ReadOnly`) so switching themes mid-session immediately re-renders ready card borders, live status colors, and read-only dimmed inputs without waiting for the next polling tick.
   - **Optional OS theme default:** Default to the tool's primary theme or probe `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme` (`DWORD == 1`) inside `try/catch` during startup when matching Windows system appearance is desired.

### Typography Scale (4 Roles)

Use native system UI typography (`Segoe UI` / `Segoe UI Semibold`):

1. **Card Micro-Header:** `8pt Semibold`, uppercase, muted — clarifies structure without competing with values.
2. **Field Label / Hint:** `8.5–8.75pt Regular`, muted or dim — sits cleanly above its paired control.
3. **Input / Body Value:** `9.5pt Regular`, high-contrast foreground.
4. **Status & Action Labels:** `9.5pt Semibold` — crisp contrast on buttons and status strips.

### Custom-Paint & Rendering Invariants

Apply these rules to every custom control (`CardPanel`, `InputWrap`, `ThemedCombo`, `RoundButton`, `ToggleCheck`, `StatusPill`, `ThemeToggle`, custom lists/tabs):

1. **Minimize Guard:** Start every paint/WM_PAINT handler with `if (Width <= 2 || Height <= 2) return;` to prevent GDI+ exceptions on window minimize.
2. **Half-Pixel Crisp Strokes:** Draw anti-aliased rounded borders inside `new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f)` so right/bottom 1px edges never clip.
3. **Double Buffering:** Enable `DoubleBuffered = true` and `ControlStyles.ResizeRedraw` on forms, cards, and custom controls. For `DataGridView` or `ListView`, enable double buffering (via subclass or non-public property) to eliminate scroll flicker.
4. **ClearType Parity:** Render text with `TextRenderer.DrawText` (not `Graphics.DrawString`) so custom controls match native Win32 font rendering and support `TextFormatFlags.EndEllipsis`.
5. **ComboBox Hygiene:** In owner-drawn item rendering, guard `if (e.Index < 0) return;`, sync popup `ListBox` `BackColor`/`ForeColor` with `T.BgDrop`/`T.FgText` on theme switch, and dynamically widen `DropDownWidth` to the widest measured item so long values never truncate.
6. **Zero Resource Leaks:** Wrap every `GraphicsPath`, `Region`, `Pen`, `SolidBrush`, and `Graphics.FromHwnd` in `using`.

---

## 5. UX, Ergonomics & State Patterns

- **Keyboard & Focus:** Add controls in top-to-bottom, left-to-right order so `Tab` flow is natural. Render an explicit `Accent` focus ring on every interactive control, including header toggles.
- **Safe Enter/Escape Behavior:** Do not set `Form.AcceptButton` if the UI triggers long-running/destructive processes or contains path fields alongside multiline editors. Always set `AcceptsReturn = true` on multiline `TextBox` controls.
- **State Coupling:** When one control overrides or disables another (e.g., resuming a prior session ignores a target path), visually dim the overridden field via `SyncTheme()` and dynamically update the primary CTA text (`Start` -> `Resume`).
- **Inline Guidance & Tooltips:** Prefer short cue-banner placeholders (`EM_SETCUEBANNER`) for format hints, attach identical tooltips to both composite wrappers (`InputWrap`) and their inner controls, and update dynamic tooltips (such as `"Switch to light mode"` / `"Switch to dark mode"`) on toggle.
- **Non-Blocking Feedback:** Never stall startup waiting for local servers or external tools; display an immediate "Checking..." state, populate controls, and transition smoothly when async probes finish.

---

## 6. Toolchain-Agnostic & Lean Engineering

Design single-file and project-based WinForms code so it builds cleanly across standalone command-line compilers (`csc`) and full SDK/Roslyn toolchains without hardcoding runtime versions:

- **Match Target Language Baseline:** When the build environment may invoke a standalone legacy compiler alongside modern Roslyn toolchains, stick to conservative, universally supported C# syntax (explicit `out` declarations, `as` casts instead of pattern matching, `string.Format` or `+`, block-bodied members, explicit null checks, constructor/field initialization) rather than newer syntactic sugar. When targeting a known modern SDK exclusively, use project-consistent idioms cleanly.
- **Minimal Dependency Surface:** Keep references strictly to core assemblies already needed by the app (`System`, `System.Drawing`, `System.Windows.Forms`, plus `System.Net.Http` only when making HTTP calls). Avoid pulling in unnecessary namespaces or third-party UI kits for lightweight utilities.
- **Low Line-Count Discipline:**
  - Centralize rounded geometry in a single shared `RoundPath(RectangleF, float)` helper reused across cards, inputs, combos, buttons, toggles, and badges.
  - Keep custom micro-controls compact (~15–35 lines each) and cohesive so palette tokens (`T`) are shared directly without boilerplate plumbing.
  - Use small declarative builders (`MakeCard`, `AddHead`, `AddLbl`, `AddInput`, `AddCombo`, `AddBtn`) that configure grid placement, column span, symmetric margins, theme tags, placeholders, and tooltips in one call per field.

---

## 7. Pre-Ship Verification Checklist

- [ ] **Vertical Budget at `MinimumSize`:** Sum outer vertical padding + all `AutoSize` cards + card gutters + ~40px window chrome. Confirm the flexible row retains comfortable usable space (e.g., >= 3–4 visible lines in multiline editors or lists).
- [ ] **Horizontal Budget at `MinimumSize`:** Confirm the narrowest split column exceeds its widest label and button text with >= 16px margin to spare, and that status bar text retains generous space beside pills and any header toggle.
- [ ] **Row & Height Parity:** Confirm text inputs, dropdowns, toggles, and buttons share identical heights and horizontal baselines within and across paired cards (and status pills align vertically with any header theme toggle).
- [ ] **Resize Invariance:** Resize the form vertically from `MinimumSize` to maximized and back; confirm single-line inputs, combos, toggles, and buttons in the bottom row of every `AutoSize` card stay locked to `inputH`/`btnH` (with centered inner `TextBox` text inside intact rounded borders) while only the designated flexible content row grows or shrinks.
- [ ] **Theme Parity (Dark & Light):** Toggle between Dark and Light modes mid-session across every live state (checking, ready with green launch border, offline error, optional-missing warning, read-only resumed input, open dropdown popup, disabled buttons, and keyboard focus). Verify title bar chrome switches cleanly, warning/pill text maintains crisp legibility on white cards, and vector toggle icons scale sharply across DPI factors.
- [ ] **State Matrix:** Test focused (Tab), hovered, pressed, disabled, read-only, empty-input validation, long-string truncation/dropdown width, and minimize/restore (`0x0` bounds).
- [ ] **Threading & Cleanup:** Confirm zero UI stutter during polling or startup, clean cancellation/disposal on close while async operations are in flight, and `[STAThread]` on `Main()`.
- [ ] **Clean Compilation:** Verify zero errors and zero warnings against the target build toolchain (`csc` standalone and/or SDK build).
