// Snapline: a local screenshot shelf for Windows. MIT License.
// The shelf window: start-up, show/hide, pointer tracking and shutdown.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
using Path = System.IO.Path;

namespace Snapline
{
    sealed partial class Shelf : Window
    {
        readonly Store store;
        readonly bool noCapture, demo;
        readonly Canvas canvas = new Canvas();
        readonly TranslateTransform shelfSlide = new TranslateTransform();
        bool opening, collapsing, renderDeferred;
        internal const double ShelfHeight = 270, HiddenShelfOffset = -350;
        // The stage is the clipped area below the bar; cards slide out from under its top edge.
        Grid stage;
        double lineTop;
        int transitionSerial;
        internal bool Collapsing { get { return collapsing; } }
        internal bool Opening { get { return opening; } }
        internal double ShelfOffset { get { return shelfSlide.Y; } }
        FrameworkElement indicator;
        FrameworkElement toastElement;
        readonly DispatcherTimer tick = new DispatcherTimer();

        readonly Dictionary<string, DateTime> pending = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);


        Forms.NotifyIcon tray;
        Forms.ToolStripMenuItem captureMenu, pinMenu;
        internal Forms.ToolStripMenuItem startupMenu;
        internal StartupRegistration Startup;
        internal bool StartCollapsed;
        internal bool Expanded { get { return expanded; } }
        readonly Dictionary<string, Forms.ToolStripMenuItem> shortcutChoices = new Dictionary<string, Forms.ToolStripMenuItem>();
        readonly DispatcherTimer captureDelay = new DispatcherTimer();
        ToolTip helpTip, imageTip;
        bool helpReading, capturePending;
        DateTime captureRequested;
        int captureSerial;
        bool collectCapture;
        internal Action<Action<BitmapSource>> CaptureLauncher = delegate(Action<BitmapSource> complete) { complete(RegionCapture.Select()); };
        FileSystemWatcher inboxWatcher;
        IntPtr handle;
        HwndSource source;
        Forms.Screen screen;
        bool expanded, pinned, dragging, dialogOpen, shuttingDown;
        DateTime keepUntil, hideUntil, lastInside;
        internal Func<IntPtr> ForegroundWindow = Native.GetForegroundWindow;
        internal Func<Native.Point?> PointerPosition = delegate { Native.Point point; return Native.GetCursorPos(out point) ? (Native.Point?)point : null; };

        double shelfWidth;
        string toast = "";
        DateTime toastUntil;
        internal bool ClipboardRegistered, HotkeyRegistered, CaptureHotkeyRegistered, HangHotkeyRegistered, NoteHotkeyRegistered;
        DateTime nextExpiry = DateTime.Now.AddSeconds(20);
        DateTime nextTriggerCheck;
        // Sets how long the touch line is. Returns true when the length changed.
        bool ApplyTriggerLength()
        {
            int mode = store.Settings.TriggerLength;
            double width = mode == 1 ? TopHoverGate.IndicatorWidth * 1.5 : mode == 2 ? BarClockWidth() : TopHoverGate.IndicatorWidth;
            nextTriggerCheck = DateTime.Now.AddSeconds(30);
            double shift = mode == 2 ? BarShift : 0;
            if (Math.Abs(width - TopHoverGate.Width) < 0.5 && shift == TopHoverGate.Shift) return false;
            TopHoverGate.Width = width; TopHoverGate.Shift = shift;
            return true;
        }
        // The width of the date and clock in the middle of the bar ("Sunday, Nov 15   5:57 PM"), worked out by measuring
        // the same words in the bar's font and size. It changes a little with the day, the month and the hour.
        double BarClockWidth()
        {
            try
            {
                var face = new Typeface(Theme.Font, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
                var plain = System.Globalization.CultureInfo.InvariantCulture; DateTime now = DateTime.Now;
                double total = BarGap;
                foreach (string words in new[] { now.ToString("dddd, MMM d", plain), now.ToString("h:mm tt", plain) })
                    total += new FormattedText(words, plain, FlowDirection.LeftToRight, face, BarFontSize, Brushes.Black, Math.Max(1, Dpi())).WidthIncludingTrailingWhitespace;
                return Math.Max(TopHoverGate.IndicatorWidth * 1.5, Math.Min(300, total));
            }
            catch { return 154; }
        }
        // The bar's font size, the space it leaves between the date and the clock, and how far right of centre the pair sits.
        const double BarFontSize = 16, BarGap = 31, BarShift = 5.5;
        internal string CaptureShortcutLabel { get { var shortcut = ShortcutSpec.From(store.Settings); return shortcut == null ? "Shortcut off" : shortcut.Label; } }
        internal Shelf(Store data, bool noCapture, bool demo)
        {
            store = data;
            this.noCapture = noCapture;
            this.demo = demo;
            Title = "Snapline";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Height = 4; Width = 1000;
            canvas.RenderTransform = shelfSlide;
            stage = new Grid { ClipToBounds = true, VerticalAlignment = VerticalAlignment.Top, Children = { canvas, motionLayer } };
            Content = stage;
            FontFamily = Theme.Font;
            UseLayoutRounding = true;
            int knownCount = store.Items.Count + store.Notes.Count + store.Links.Count;
            store.Changed = delegate { int count = store.Items.Count + store.Notes.Count + store.Links.Count; bool deckChanged = count != knownCount; if (count > knownCount) { first = 0; focus = 0; } knownCount = count; Render(deckChanged); };
            store.RemarkChanged = RefreshRemark;
            canvas.MouseWheel += delegate(object sender, MouseWheelEventArgs e) { if (expanded) { WheelBy(e.Delta); e.Handled = true; } };
            SourceInitialized += Initialize;
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (memoEditor != null && !memoEditor.FlushChanges()) e.Cancel = true; if (remarkEditor != null && !remarkEditor.TrySave()) e.Cancel = true; };
            Closed += Cleanup;
            AllowDrop = true;
            DragOver += delegate(object sender, DragEventArgs e) { e.Effects = !dragging && CanHang(e.Data) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
            Drop += delegate(object sender, DragEventArgs e)
            {
                if (dragging || !CanHang(e.Data)) return; // A card of our own dropped back on the line is not hung again.
                e.Effects = DragDropEffects.Copy; e.Handled = true; // Copy: the source keeps what was dragged.
                try { HangDropped(e.Data); }
                catch (Exception ex) { store.Log("Hang dropped item", ex); Notify("Could not hang that."); }
            };
        }
        static SolidColorBrush Brush(string hex) { var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex); b.Freeze(); return b; }
        internal void Initialize(object sender, EventArgs e)
        {
            handle = new WindowInteropHelper(this).Handle;
            source = HwndSource.FromHwnd(handle);
            source.AddHook(Message);
            Native.SetWindowLong(handle, -20, Native.GetWindowLong(handle, -20) | 0x08000000 | 0x00000080);

            ClipboardRegistered = false; // Only direct capture results are collected.
            HotkeyRegistered = Native.RegisterHotKey(handle, 1, 0x4003, 0x53); // Ctrl+Alt+S, no repeat.
            HangHotkeyRegistered = Native.RegisterHotKey(handle, 4, 0x4003, 0x44); // Ctrl+Alt+D, no repeat.
            NoteHotkeyRegistered = Native.RegisterHotKey(handle, 5, 0x4003, 0x4E); // Ctrl+Alt+N, no repeat.
            RegisterCaptureShortcut();
            screen = ScreenUnderPointer();
            MakeTray();
            if (Startup != null && !StartCollapsed)
            {
                string error;
                if (!Startup.Initialize(out error)) tray.ShowBalloonTip(5000, "Start at login is off", error, Forms.ToolTipIcon.Warning);
            }
            store.SaveSettings(); // Persist disabled legacy collection preferences on upgrade.
            captureDelay.Interval = TimeSpan.FromMilliseconds(80);
            captureDelay.Tick += CaptureWhenReleased;

            ApplyTriggerLength();
            Position(false, screen);
            SetupWatchers();

            tick.Interval = TimeSpan.FromMilliseconds(80);
            tick.Tick += Tick;
            tick.Start();
            if (demo) { pinned = true; Reveal(); }
            else if (StartCollapsed) { Render(); }
            else
            {
                Reveal();
                keepUntil = DateTime.Now.AddSeconds(7);
                if (!store.Settings.WelcomeShown)
                {
                    tray.ShowBalloonTip(4500, "Snapline is running", CaptureShortcutLabel + " captures a region. See the 'Tips' card right of the images. Start at login is in the tray menu.", Forms.ToolTipIcon.Info);
                    store.Settings.WelcomeShown = true;
                    store.SaveSettings();
                }
            }

            if (store.Settings.CaptureShortcut != "None" && !CaptureHotkeyRegistered) StartupNotice(CaptureShortcutLabel + " is in use by another program. Use the scissors button or change the shortcut in the tray menu.");
            if (!HotkeyRegistered) StartupNotice("Ctrl + Alt + S is in use by another program. Open the line from the top edge or the tray.");
            else if (!HangHotkeyRegistered) StartupNotice("Ctrl + Alt + D is in use by another program. Hang the clipboard from the tray menu instead.");
            else if (!NoteHotkeyRegistered) StartupNotice("Ctrl + Alt + N is in use by another program. Start a note from the tray menu instead.");
        }
        void StartupNotice(string message)
        {
            if (StartCollapsed) tray.ShowBalloonTip(5000, "Snapline", message, Forms.ToolTipIcon.Warning);
            else Notify(message);
        }
        IntPtr Message(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == Native.MouseActivate) { handled = true; return new IntPtr(3); }

            if (msg == Native.Hotkey)
            {
                if (w.ToInt32() == 1) { Toggle(); handled = true; }
                else if (w.ToInt32() == 4) { HangClipboard(); handled = true; }
                else if (w.ToInt32() == 5)
                {
                    // The editor is a dialog that stays open; start it after this message has been answered.
                    if (!(dragging || reorderSlots != null || memoEditor != null || remarkEditor != null || editingShortcut || shuttingDown))
                        Dispatcher.BeginInvoke(new Action(NewNote));
                    handled = true;
                }
                else if (w.ToInt32() >= 20 && w.ToInt32() <= 25) { if (lineKeys && expanded) LineKey(w.ToInt32()); handled = true; }
                else if (w.ToInt32() >= 30 && w.ToInt32() <= 38) { if (lineKeys && expanded) LineDigit(w.ToInt32() - 29); handled = true; }
                else if (w.ToInt32() == captureHotkeyId && CaptureHotkeyRegistered)
                {
                    if (!editingShortcut) StartCapture(ShortcutCollects, ShortcutSpec.From(store.Settings).Key);
                    handled = true;
                }
            }
            // Horizontal wheel or a two-finger sideways swipe.
            if (msg == 0x020E && expanded) { WheelBy(-(short)((w.ToInt64() >> 16) & 0xFFFF)); handled = true; }
            if ((uint)msg == Native.ShowMessage) { ToggleFromTray(); handled = true; }
            return IntPtr.Zero;
        }

        Forms.Screen ScreenUnderPointer()
        {
            var point = PointerPosition();
            return point.HasValue ? Forms.Screen.FromPoint(new Drawing.Point(point.Value.X, point.Value.Y)) : (screen ?? Forms.Screen.PrimaryScreen);
        }
        double Dpi()
        {
            try { uint dpi = Native.GetDpiForWindow(handle); return dpi == 0 ? 1 : dpi / 96.0; }
            catch (EntryPointNotFoundException) { return source.CompositionTarget.TransformToDevice.M11; }
        }
        void Position(bool open, Forms.Screen target)
        {
            bool changed = screen == null || screen.DeviceName != target.DeviceName;
            screen = target;
            var area = screen.WorkingArea;
            double scale = Dpi();
            lineTop = open ? BarBottom(area, scale) : 0;
            int height = (int)Math.Ceiling((open ? ShelfHeight + SlackDrop + lineTop : 4) * scale);
            Native.SetWindowPos(handle, new IntPtr(-1), area.Left, area.Top, area.Width, height, 0x0010);
            // A monitor move can change the WPF presentation source's DPI.
            scale = Dpi();
            int updated = (int)Math.Ceiling((open ? ShelfHeight + SlackDrop + lineTop : 4) * scale);
            if (height != updated) Native.SetWindowPos(handle, new IntPtr(-1), area.Left, area.Top, area.Width, updated, 0x0010);
            shelfWidth = area.Width / scale;
            canvas.Width = shelfWidth;
            canvas.Height = open ? ShelfHeight + SlackDrop : 4;
            stage.Margin = new Thickness(0, lineTop, 0, 0);
            stage.Height = canvas.Height;
            if (changed) { first = 0; focus = 0; }
        }
        // Bottom edge of a komorebi bar docked at the top of this screen, in WPF units; 0 when there is none.
        double BarBottom(Drawing.Rectangle area, double scale)
        {
            try
            {
                foreach (var process in Process.GetProcessesByName("komorebi-bar"))
                {
                    IntPtr bar = process.MainWindowHandle; Native.Rect r;
                    if (bar == IntPtr.Zero || !Native.GetWindowRect(bar, out r)) continue;
                    int middle = area.Left + area.Width / 2, bottom = r.Bottom - area.Top;
                    bool docked = r.Top <= area.Top + 4 && r.Left < middle && r.Right > middle && r.Right - r.Left > area.Width / 2;
                    if (docked && bottom > 0 && bottom < area.Height / 6) return Math.Ceiling(bottom / scale);
                }
            }
            catch (Exception ex) { store.Log("Find bar", ex); }
            return 0;
        }
        internal double Slack { get { int v = store.Settings.LineSlack; return (v <= 0 ? 100 : v) / 100.0; } }
        // Extra room below the cards when the rope hangs looser than normal.
        double SlackDrop { get { return Math.Max(0, Slack - 1) * 52; } }
        double Lean(double degrees) { return degrees * (0.4 + 0.6 * Slack); }
        internal Drawing.Rectangle IndicatorHitBounds()
        {
            if (indicator == null || expanded || !IsLoaded) return Drawing.Rectangle.Empty;
            var first = indicator.PointToScreen(new System.Windows.Point(0, 0));
            var last = indicator.PointToScreen(new System.Windows.Point(indicator.ActualWidth, indicator.ActualHeight));
            return Drawing.Rectangle.FromLTRB((int)Math.Round(first.X), (int)Math.Round(first.Y), (int)Math.Round(last.X), (int)Math.Round(last.Y));
        }
        bool Fullscreen(Forms.Screen target = null)
        {
            IntPtr foreground = ForegroundWindow();
            if (foreground == IntPtr.Zero || foreground == handle || dialogOpen || dragging) return false;
            return HoverPolicy.Suppresses(foreground, (target ?? screen).Bounds);
        }
        void TryRevealFromIndicator()
        {
            if (expanded || collapsing || shuttingDown || dialogOpen || dragging || helpReading || capturePending ||
                FlyingCount > 0 || DateTime.Now < hideUntil) return;
            var point = PointerPosition();
            if (!point.HasValue || !IndicatorHitBounds().Contains(point.Value.X, point.Value.Y)) return;
            if (!demo && Fullscreen(ScreenUnderPointer())) return;
            Reveal();
        }
        void Tick(object sender, EventArgs e)
        {
            if (shuttingDown) return;
            // The longest setting follows the bar's date and clock, whose width shifts through the day.
            if (store.Settings.TriggerLength == 2 && DateTime.Now > nextTriggerCheck && ApplyTriggerLength() && !expanded && !collapsing) Render();
            // Typing into an editor or a dialog must never be taken over by the line keys.
            if (lineKeys && (dialogOpen || memoEditor != null || remarkEditor != null || editingShortcut)) SetLineKeys(false);
            // The line is not open, or the user switched to another window: plain keys belong to that app again.
            if (lineKeys && (!expanded || Native.GetForegroundWindow() != lineKeysOwner)) SetLineKeys(false);
            if (!dragging && reorderSlots == null) ProcessPending();
            // Checked before the early exits below, so an open tooltip cannot keep a removed card recoverable forever.
            if (undoItem != null && !dragging && DateTime.Now > undoUntil) { FinishUndo(); UpdateToast(); }
            if (store.Settings.AutoClearDays > 0 && DateTime.Now > nextExpiry)
            {
                // Never under the user's eyes: wait until the line is closed and nothing is in hand.
                if (expanded || dragging || reorderSlots != null || dialogOpen || undoItem != null || FlyingCount > 0) nextExpiry = DateTime.Now.AddMinutes(1);
                else
                {
                    nextExpiry = DateTime.Now.AddHours(1);
                    try { store.Expire(store.Settings.AutoClearDays); }
                    catch (Exception ex) { store.Log("Auto-clear", ex); }
                }
            }
            if (linksStale && expanded && !collapsing && !dragging && reorderSlots == null && !helpReading && Mouse.LeftButton != MouseButtonState.Pressed &&
                (imageTip == null || !imageTip.IsOpen) && (noteTip == null || !noteTip.IsOpen)) { linksStale = false; Render(); }
            if (dialogOpen || dragging || helpReading || capturePending) return;
            var point = PointerPosition(); if (!point.HasValue) return;
            var p = point.Value;
            Forms.Screen under = Forms.Screen.FromPoint(new Drawing.Point(p.X, p.Y));
            DateTime now = DateTime.Now;
            if (FlyingCount > 0) { lastInside = now; return; }
            if (!demo && Fullscreen(under))
            {
                if (expanded && screen.DeviceName == under.DeviceName) Collapse(false, false);
                return;
            }
            if ((imageTip != null && imageTip.IsOpen) || (noteTip != null && noteTip.IsOpen)) { lastInside = now; return; }
            if (collapsing) return;
            if (!expanded) TryRevealFromIndicator();
            bool inside = expanded && p.X >= screen.WorkingArea.Left && p.X < screen.WorkingArea.Right &&
                p.Y >= screen.WorkingArea.Top && p.Y < screen.WorkingArea.Top + (266 + SlackDrop + lineTop) * Dpi();
            if (inside) lastInside = now;
            else if (lineKeys && (Native.GetAsyncKeyState(0x01) & 0x8000) != 0) SetLineKeys(false); // Clicked into another app.
            if (expanded && !pinned && now > keepUntil && (now - lastInside).TotalMilliseconds > 700) Collapse(false);
            if (toast != "" && now > toastUntil) { toast = ""; UpdateToast(); }
        }
        internal void Reveal()
        {
            bool was = expanded;
            bool reversing = collapsing;
            double offset = shelfSlide.Y;
            int serial = ++transitionSerial; collapsing = false;
            shelfSlide.BeginAnimation(TranslateTransform.YProperty, null);
            var target = ScreenUnderPointer();
            bool moved = screen == null || target.DeviceName != screen.DeviceName;
            opening = !was || reversing || moved || Math.Abs(offset) > .1;
            shelfSlide.Y = !was || moved ? HiddenShelfOffset : offset;
            canvas.BeginAnimation(UIElement.OpacityProperty, null); canvas.Opacity = 1;
            expanded = true;
            if (!was || moved) Position(true, target);
            if (Fullscreen() && !demo) { Collapse(false, false); return; }
            lastInside = DateTime.Now;
            if (!was || moved || renderDeferred) Render();
            if (opening)
            {
                var slide = new DoubleAnimation(shelfSlide.Y, 0, TimeSpan.FromMilliseconds(300))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                slide.Completed += delegate { if (serial == transitionSerial && !shuttingDown) opening = false; };
                shelfSlide.BeginAnimation(TranslateTransform.YProperty, slide);
            }
        }

        void Collapse(bool manual, bool animate = true)
        {
            SetLineKeys(false);
            if (manual) { pinned = false; hideUntil = DateTime.Now.AddSeconds(1); }
            CloseImagePreview(); CloseNotePreview(); if (helpTip != null) helpTip.IsOpen = false;
            if (collapsing && animate) return;
            int serial = ++transitionSerial;
            opening = false;
            Action finish = delegate {
                if (serial != transitionSerial || shuttingDown) return;
                collapsing = false; expanded = false;
                Position(false, screen);
                shelfSlide.BeginAnimation(TranslateTransform.YProperty, null); shelfSlide.Y = 0;
                Render();
            };
            canvas.BeginAnimation(UIElement.OpacityProperty, null);
            canvas.Opacity = 1;
            if (!expanded || !animate) { finish(); return; }
            collapsing = true;
            var slide = new DoubleAnimation(shelfSlide.Y, HiddenShelfOffset, TimeSpan.FromMilliseconds(250))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            slide.Completed += delegate { finish(); };
            shelfSlide.BeginAnimation(TranslateTransform.YProperty, slide);
        }
        void Toggle()
        {
            if (expanded && !collapsing) Collapse(true);
            else { pinned = true; Reveal(); if (expanded && !collapsing) SetLineKeys(true); }
        }
        internal void ToggleFromTray()
        {
            if (expanded && !collapsing) Collapse(true);
            else
            {
                pinned = false;
                Reveal();
                // Allow time to move from the taskbar to the shelf before auto-hiding.
                keepUntil = DateTime.Now.AddSeconds(3);
            }
        }
        internal void TogglePin()
        {
            pinned = !pinned;
            pinMenu.Checked = pinned;
            Reveal();
            if (!pinned) keepUntil = DateTime.Now.AddSeconds(3);
        }
        void Notify(string message)
        {
            toast = message.Length > 80 ? message.Substring(0, 80) : message;
            toastUntil = DateTime.Now.AddSeconds(5);
            keepUntil = toastUntil;
            Reveal(); UpdateToast();
        }

        // For --card-rects: where each note card is on the screen, in real pixels, so a test can point at one.
        internal void WriteCardRects(string file)
        {
            var lines = new List<string> { "expanded=" + expanded };
            foreach (var card in canvas.Children.OfType<FrameworkElement>())
            {
                var note = card.Tag as Memo;
                if (note == null || PresentationSource.FromVisual(card) == null) continue;
                var a = card.PointToScreen(new Point(0, 0)); var b = card.PointToScreen(new Point(card.Width, card.Height));
                lines.Add(String.Join("|", (int)a.X, (int)a.Y, (int)(b.X - a.X), (int)(b.Y - a.Y), note.Picture != null, note.Text.Split('\n')[0]));
            }
            System.IO.File.WriteAllLines(file, lines);
        }
        internal void Snapshot(string file)
        {
            canvas.Measure(new System.Windows.Size(shelfWidth, 270));
            canvas.Arrange(new System.Windows.Rect(0, 0, shelfWidth, 270));
            canvas.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(shelfWidth), 270, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(canvas);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = System.IO.File.Create(file)) encoder.Save(stream);
        }
        void Cleanup(object sender, EventArgs e)
        {
            shuttingDown = true;
            store.RemarkChanged = null;
            transitionSerial++; shelfSlide.BeginAnimation(TranslateTransform.YProperty, null);
            ClearHoverEffects(); CloseFallingPapers();
            SetLineKeys(false); FinishUndo();
            tick.Stop(); captureDelay.Stop();
            CloseImagePreview(); CloseNotePreview();
            if (helpTip != null) helpTip.IsOpen = false;

            if (inboxWatcher != null) inboxWatcher.Dispose();
            Native.UnregisterHotKey(handle, 1); Native.UnregisterHotKey(handle, 2); Native.UnregisterHotKey(handle, 3); Native.UnregisterHotKey(handle, 4); Native.UnregisterHotKey(handle, 5);
            if (source != null) source.RemoveHook(Message);
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            store.Save(); store.SaveSettings();
        }
    }
}
