// Snapline: a local screenshot shelf for Windows. MIT License.
// The Preferences window: every setting in one place, laid out like Pebble's (own header, tabs, rows), in Snapline's theme.
// Changes apply at once; the theme and the font restart Snapline, as they do from the tray.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Snapline
{
    sealed class PreferencesWindow : Window
    {
        readonly Shelf shelf;
        readonly Store store;
        // Four tabs, so no page needs scrolling. The tab shown last time is shown again in this run.
        internal static readonly string[] TabNames = { "Look", "Line", "Behaviour", "Misc" };
        static int lastTab;
        readonly Grid[] pages = new Grid[4];
        readonly List<Border> tabMarks = new List<Border>();
        readonly List<TextBlock> tabLabels = new List<TextBlock>();
        StackPanel rows;
        // Every control redraws itself from the settings through one of these, after any change.
        readonly List<Action> refreshers = new List<Action>();
        const double RowWidth = 440;

        internal PreferencesWindow(Shelf owner, Store data)
        {
            shelf = owner; store = data;
            Title = "Snapline · Preferences";
            // Borderless with its own header: komorebi tiles any window with a title bar, and leaves this one floating.
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            SizeToContent = SizeToContent.Height; Width = RowWidth + 50; // the rows, their margins and the 1-wide border
            Background = MemoTheme.Brush(Theme.Panel); BorderBrush = MemoTheme.Brush(Theme.Rule); BorderThickness = new Thickness(1);
            FontFamily = Theme.Font; FontSize = Theme.Size(13); Foreground = MemoTheme.Brush(Theme.Light);
            UseLayoutRounding = true;
            AutomationProperties.SetAutomationId(this, "Preferences");
            var root = new DockPanel { LastChildFill = true, Background = MemoTheme.Brush(Theme.Panel) };
            var header = Header(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
            var tabs = Tabs(); DockPanel.SetDock(tabs, Dock.Top); root.Children.Add(tabs);
            // The pages share one cell; the hidden ones still take their room, so the window is as tall as the tallest page.
            var stack = new Grid { Margin = new Thickness(24, 4, 24, 16) };
            for (int i = 0; i < pages.Length; i++) { pages[i] = new Grid(); stack.Children.Add(pages[i]); }
            root.Children.Add(stack);
            Content = root;
            Build();
            ShowTab(lastTab);
            PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        }

        // ---- frame ----

        FrameworkElement Header()
        {
            var bar = new Grid { Height = 38, Background = MemoTheme.Brush(Theme.Bg) };
            var title = new TextBlock { Text = "Snapline  ·  Preferences", Foreground = MemoTheme.Brush(Theme.White), FontSize = Theme.Size(13), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
            bar.Children.Add(title);
            var close = new TextBlock { Text = "✕", Width = 38, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right,
                Foreground = MemoTheme.Brush(Theme.Mid), Cursor = Cursors.Hand, FontSize = 13, Background = Brushes.Transparent, Padding = new Thickness(0, 10, 0, 10) };
            AutomationProperties.SetName(close, "Close Preferences");
            close.MouseEnter += delegate { close.Foreground = MemoTheme.Brush(Theme.White); };
            close.MouseLeave += delegate { close.Foreground = MemoTheme.Brush(Theme.Mid); };
            close.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e) { e.Handled = true; Close(); };
            bar.Children.Add(close);
            bar.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { if (e.OriginalSource != close) DragMove(); };
            return bar;
        }

        // The tab strip under the header: the names, the current one bright with an accent line under it.
        FrameworkElement Tabs()
        {
            var strip = new Grid { Height = 40, Margin = new Thickness(24, 0, 24, 0) };
            strip.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Background = MemoTheme.Brush(Theme.Rule) });
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < TabNames.Length; i++)
            {
                int index = i;
                var cell = new Grid { Width = 96, Background = Brushes.Transparent, Cursor = Cursors.Hand };
                var label = new TextBlock { Text = TabNames[i], HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var mark = new Border { Height = 2, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(18, 0, 18, 0), Background = MemoTheme.Brush(Theme.Accent) };
                cell.Children.Add(label); cell.Children.Add(mark);
                AutomationProperties.SetAutomationId(cell, "PrefTab-" + TabNames[i]);
                cell.MouseLeftButtonUp += delegate { ShowTab(index); };
                tabLabels.Add(label); tabMarks.Add(mark); row.Children.Add(cell);
            }
            strip.Children.Add(row);
            return strip;
        }

        internal void ShowTab(int index)
        {
            lastTab = Math.Max(0, Math.Min(pages.Length - 1, index));
            for (int i = 0; i < pages.Length; i++)
            {
                bool on = i == lastTab;
                pages[i].Visibility = on ? Visibility.Visible : Visibility.Hidden;
                tabLabels[i].Foreground = MemoTheme.Brush(on ? Theme.White : Theme.Mid);
                tabMarks[i].Visibility = on ? Visibility.Visible : Visibility.Hidden;
            }
        }

        void Refresh() { foreach (var each in refreshers) each(); }

        // Saves the settings after a change made through `change`, then lets the line catch up.
        void Commit(Action change, bool showLine)
        {
            shelf.ChangeSetting(change, showLine);
            Refresh();
        }

        // ---- layout ----

        void Build()
        {
            var s = store.Settings;
            Page(0);
            Section("LOOK");
            Row("Theme", Choice("Theme", Theme.Names, delegate { return Math.Max(0, Array.IndexOf(Theme.Names, Theme.Name)); },
                delegate(int i) { shelf.ChooseTheme(Theme.Names[i]); }), "Snapline restarts to apply a theme (about a second).");
            var fonts = Theme.Fonts.Where(x => Theme.FontInstalled(x) || x == Theme.FontBase).ToArray();
            Row("Font", Cycle("Font", fonts, delegate { return Math.Max(0, Array.IndexOf(fonts, Theme.FontBase)); },
                delegate(int i) { shelf.ChooseFont(fonts[i]); }), "Snapline restarts to apply a font.");
            Row("Tips card", Choice("TipsCard", new[] { "Full", "Compact" }, delegate { return s.TipsCard == 1 ? 1 : 0; },
                delegate(int i) { Commit(delegate { s.TipsCard = i; }, true); }), "Compact: a slim tab with the same help, buttons and bin.");
            Row("Card size", Choice("CardSize", CardSizes.Names, delegate { return s.CardSize; }, delegate(int i) { shelf.ChooseCardSize(i); Refresh(); }),
                "Notes, screenshots, references and Tips, with their text.");

            Page(1);
            Section("LINE");
            var tensions = new[] { 15, 55, 100, 150, 200 };
            Row("Line tension", Choice("Tension", new[] { "Tight", "Firm", "Normal", "Loose", "Very loose" },
                delegate { int now = (int)Math.Round(shelf.Slack * 100); int at = Array.IndexOf(tensions, now); return at < 0 ? 2 : at; },
                delegate(int i) { Commit(delegate { s.LineSlack = tensions[i]; }, true); }));
            Row("Touch line length", Choice("TouchLine", new[] { "Short", "Medium", "Long" }, delegate { return Math.Max(0, Math.Min(2, s.TriggerLength)); },
                delegate(int i) { Commit(delegate { s.TriggerLength = i; }, false); shelf.TriggerLengthChanged(); }), "Long matches the bar's date and clock.");
            Section("TIMING");
            Row("Previews appear after", Slider("PreviewDelay", delegate { return Tuning.PreviewDelay(s, 300) / 1000.0; }, delegate(double v) { s.PreviewDelay = (int)Math.Round(v * 1000); },
                0.1, 1.5, 0.05, "0.00", " s"), "Resting the pointer on a card, before its preview opens.");
            Row("Line stays open after you leave", Slider("CloseDelay", delegate { return Tuning.CloseDelay(s) / 1000.0; }, delegate(double v) { s.CloseDelay = (int)Math.Round(v * 1000); },
                0.3, 5, 0.1, "0.0", " s"));
            Row("Undo shows for", Stepper("UndoTime", delegate { return Tuning.UndoTime(s); }, delegate(int v) { Commit(delegate { s.UndoTime = v; }, false); }, 3, 30, 1, " s"));

            Page(2);
            Section("SHORTCUTS");
            var modes = new[] { "CtrlB", "CtrlPrintScreen", "ShiftPrintScreen", "Custom", "None" };
            Row("Capture shortcut", Choice("Shortcut", new[] { "Ctrl+B", "Ctrl+PrtSc", "Shift+PrtSc", "Custom…", "Off" },
                delegate { return Math.Max(0, Array.IndexOf(modes, s.CaptureShortcut)); },
                delegate(int i) { if (modes[i] == "Custom") shelf.EditCaptureShortcut(); else shelf.SelectCaptureShortcut(modes[i]); Refresh(); }),
                null, delegate { return "Now: " + shelf.CaptureShortcutLabel + (s.CaptureShortcut == "CtrlPrintScreen" || s.CaptureShortcut == "ShiftPrintScreen" ? " (copies only)" : ""); });
            foreach (var each in LineShortcut.All) LineShortcutRow(each);
            Section("CARDS");
            var ages = new[] { 0, 1, 7, 30 };
            Row("Auto-clear old cards", Choice("AutoClear", new[] { "Never", "1 day", "7 days", "30 days" },
                delegate { int at = Array.IndexOf(ages, s.AutoClearDays); return at < 0 ? 0 : at; },
                delegate(int i) { shelf.SetAutoClear(ages[i]); Refresh(); }), "Kept cards and the files behind references stay.");
            Section("STARTUP");
            if (shelf.Startup != null)
                Row("Start at login", Switch("StartAtLogin", delegate { return s.StartupEnabled; }, delegate(bool on) { shelf.SetStartAtLogin(on); Refresh(); }));
            else
                Row("Start at login", Note("Not available in this copy"), "Only the copy started normally manages this.");

            Page(3);
            Section("MOTION");
            Row("Animation", Choice("Animation", Tuning.AnimationNames, delegate { return Tuning.Animation(s); },
                delegate(int i) { Commit(delegate { s.Animation = i; Theme.AnimationMode = i; }, true); }), "Basic: light, cards hang still. Fun: they swing like clothes on a line.");
            Row("Reduce animation on battery", Switch("ReduceOnBattery", delegate { return !s.FunOnBattery; },
                delegate(bool on) { Commit(delegate { s.FunOnBattery = !on; Theme.FunOnBattery = !on; }, true); }), "On battery, Fun behaves as Basic.");
            Row("Open after resting on the line", Slider("OpenDelay", delegate { return Tuning.OpenDelay(s) / 1000.0; }, delegate(double v) { s.OpenDelay = (int)Math.Round(v * 1000); },
                0, 1, 0.05, "0.00", " s"), "0 opens it as soon as you touch it.");
            Section("LIMITS");
            Row("Screenshots kept", Stepper("ShotLimit", delegate { return store.ShotLimit; }, delegate(int v) { shelf.SetLimits(v, store.LinkLimit); Refresh(); }, 10, 200, 10, ""),
                "The oldest unkept one makes room for a new one.");
            Row("Reference cards kept", Stepper("LinkLimit", delegate { return store.LinkLimit; }, delegate(int v) { shelf.SetLimits(store.ShotLimit, v); Refresh(); }, 10, 200, 10, ""),
                "Only the card goes; the file or folder is never touched.");
            Refresh();
        }

        // Original keys, keys of your own (recorded in the shortcut dialog), or off. A refusal shows under the label.
        void LineShortcutRow(LineShortcut which)
        {
            var s = store.Settings; string problem = null;
            Action<ShortcutSpec> to = delegate(ShortcutSpec spec)
            {
                string error;
                problem = shelf.TrySetLineShortcut(which, spec, out error) ? null : error;
                Refresh();
            };
            Row(which.Title, Choice("Shortcut-" + which.Name, new[] { which.Default.Label.Replace(" + ", "+"), "Custom…", "Off" },
                delegate { return which.IsOff(s) ? 2 : which.IsDefault(s) ? 0 : 1; },
                delegate(int i)
                {
                    if (i == 0) to(which.Default);
                    else if (i == 2) to(null);
                    else { problem = null; shelf.EditLineShortcut(which); Refresh(); }
                }),
                null, delegate
                {
                    if (problem != null) return problem;
                    if (which.IsOff(s)) return "Off";
                    return "Now: " + which.Label(s) + (shelf.LineShortcutRegistered(which) ? "" : " (in use by another program)");
                });
        }

        void Page(int index) { rows = new StackPanel(); pages[index].Children.Add(rows); }

        void Section(string title)
        {
            rows.Children.Add(new TextBlock { Text = title, FontSize = Theme.Size(10.5), FontWeight = FontWeights.SemiBold, Foreground = MemoTheme.Brush(Theme.Mid), Margin = new Thickness(0, 14, 0, 4) });
        }

        void Row(string label, FrameworkElement control, string note = null, Func<string> liveNote = null)
        {
            var row = new Grid { Width = RowWidth, MinHeight = 36, Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            words.Children.Add(new TextBlock { Text = label, Foreground = MemoTheme.Brush(Theme.Light), TextWrapping = TextWrapping.Wrap });
            if (note != null || liveNote != null)
            {
                var small = new TextBlock { Text = note ?? "", FontSize = Theme.Size(10.5), Foreground = MemoTheme.Brush(Theme.Mid), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
                if (liveNote != null) refreshers.Add(delegate { small.Text = liveNote(); });
                words.Children.Add(small);
            }
            row.Children.Add(words);
            // A control too wide to leave the label room sits on its own line under the label.
            control.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
            if (control.DesiredSize.Width > RowWidth - 190)
            {
                row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetColumnSpan(words, 2);
                control.HorizontalAlignment = HorizontalAlignment.Left; control.Margin = new Thickness(0, 6, 0, 4);
                Grid.SetRow(control, 1); Grid.SetColumnSpan(control, 2);
            }
            else { control.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(control, 1); }
            row.Children.Add(control);
            rows.Children.Add(row);
        }

        // ---- controls ----

        static FrameworkElement Note(string text) { return new TextBlock { Text = text, FontSize = Theme.Size(11), Foreground = MemoTheme.Brush(Theme.Mid) }; }

        // An on/off switch: a square track in the accent when on, with the knob at that end.
        FrameworkElement Switch(string id, Func<bool> get, Action<bool> set)
        {
            var knob = new Border { Width = 14, Height = 14, Background = MemoTheme.Brush(Theme.White), Margin = new Thickness(3, 0, 3, 0) };
            var track = new Border { Width = 38, Height = 20, Child = knob, Cursor = Cursors.Hand };
            AutomationProperties.SetAutomationId(track, "Pref-" + id);
            refreshers.Add(delegate
            {
                bool on = get();
                track.Background = MemoTheme.Brush(on ? Theme.Accent : Theme.Rule);
                knob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            });
            track.MouseLeftButtonUp += delegate { set(!get()); };
            return track;
        }

        // Side-by-side choices; the chosen one sits on a darker inset.
        FrameworkElement Choice(string id, string[] labels, Func<int> get, Action<int> set)
        {
            var frame = new Border { BorderBrush = MemoTheme.Brush(Theme.Rule), BorderThickness = new Thickness(1), Height = 26 };
            var grid = new UniformGrid { Rows = 1, Columns = labels.Length };
            AutomationProperties.SetAutomationId(frame, "Pref-" + id);
            var cells = new List<Border>();
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                var text = new TextBlock { Text = labels[i], FontSize = Theme.Size(11), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 7, 0) };
                var cell = new Border { Child = text, Margin = new Thickness(2), Cursor = Cursors.Hand, Background = Brushes.Transparent };
                AutomationProperties.SetAutomationId(cell, "Pref-" + id + "-" + i);
                AutomationProperties.SetName(cell, labels[i]);
                cell.MouseLeftButtonUp += delegate { if (get() != index || labels[index].EndsWith("…", StringComparison.Ordinal)) set(index); };
                cells.Add(cell); grid.Children.Add(cell);
            }
            frame.Child = grid;
            refreshers.Add(delegate
            {
                int now = get();
                for (int i = 0; i < cells.Count; i++)
                {
                    cells[i].Background = i == now ? MemoTheme.Brush(Theme.Inset) : Brushes.Transparent;
                    ((TextBlock)cells[i].Child).Foreground = MemoTheme.Brush(i == now ? Theme.White : Theme.Mid);
                }
            });
            return frame;
        }

        // One value from a list, stepped with ‹ and ›: for lists too long to show side by side.
        FrameworkElement Cycle(string id, string[] values, Func<int> get, Action<int> set)
        {
            var frame = new Border { BorderBrush = MemoTheme.Brush(Theme.Rule), BorderThickness = new Thickness(1), Height = 26, Width = 200 };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            var back = Arrow("‹", "Previous"); var next = Arrow("›", "Next");
            var value = new TextBlock { FontSize = Theme.Size(11), Foreground = MemoTheme.Brush(Theme.White), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(value, 1); Grid.SetColumn(next, 2);
            grid.Children.Add(back); grid.Children.Add(value); grid.Children.Add(next);
            frame.Child = grid;
            AutomationProperties.SetAutomationId(frame, "Pref-" + id);
            back.MouseLeftButtonUp += delegate { set((get() + values.Length - 1) % values.Length); };
            next.MouseLeftButtonUp += delegate { set((get() + 1) % values.Length); };
            refreshers.Add(delegate { value.Text = values[Math.Max(0, Math.Min(values.Length - 1, get()))]; });
            return frame;
        }

        static TextBlock Arrow(string text, string name)
        {
            var arrow = new TextBlock { Text = text, FontSize = 15, Foreground = MemoTheme.Brush(Theme.Light), TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, Cursor = Cursors.Hand };
            AutomationProperties.SetName(arrow, name);
            return arrow;
        }

        // A track with a square knob and the value beside it. Dragging changes the value; it is saved when the button is let go.
        // The wheel steps it, and each step is saved.
        FrameworkElement Slider(string id, Func<double> get, Action<double> set, double min, double max, double step, string format, string unit)
        {
            const double track = 140, left = 7;
            var canvas = new Canvas { Width = track + left, Height = 24, Background = Brushes.Transparent, Cursor = Cursors.Hand };
            var rail = new Border { Width = track - left, Height = 3, Background = MemoTheme.Brush(Theme.Rule) };
            var fill = new Border { Height = 3, Background = MemoTheme.Brush(Theme.Accent) };
            var knob = new Border { Width = 12, Height = 12, Background = MemoTheme.Brush(Theme.White) };
            Canvas.SetLeft(rail, left); Canvas.SetTop(rail, 10.5); Canvas.SetLeft(fill, left); Canvas.SetTop(fill, 10.5); Canvas.SetTop(knob, 6);
            canvas.Children.Add(rail); canvas.Children.Add(fill); canvas.Children.Add(knob);
            var value = new TextBlock { FontSize = Theme.Size(11), Foreground = MemoTheme.Brush(Theme.White), Width = 52, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(canvas); panel.Children.Add(value);
            AutomationProperties.SetAutomationId(panel, "Pref-" + id);
            Action draw = delegate
            {
                double v = Math.Max(min, Math.Min(max, get())), f = (v - min) / (max - min);
                fill.Width = Math.Max(0, (track - left) * f); Canvas.SetLeft(knob, left + (track - left) * f - 6);
                value.Text = v.ToString(format, System.Globalization.CultureInfo.InvariantCulture) + unit;
            };
            refreshers.Add(draw);
            Action<double> to = delegate(double x)
            {
                double f = Math.Max(0, Math.Min(1, (x - left) / (track - left)));
                double v = Math.Round((min + f * (max - min)) / step) * step;
                if (Math.Abs(v - get()) > 1e-9) { set(v); draw(); }
            };
            canvas.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { canvas.CaptureMouse(); to(e.GetPosition(canvas).X); e.Handled = true; };
            canvas.MouseMove += delegate(object sender, MouseEventArgs e) { if (canvas.IsMouseCaptured) to(e.GetPosition(canvas).X); };
            canvas.MouseLeftButtonUp += delegate { if (!canvas.IsMouseCaptured) return; canvas.ReleaseMouseCapture(); Commit(delegate { }, false); };
            panel.MouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                double v = Math.Max(min, Math.Min(max, Math.Round((get() + (e.Delta > 0 ? step : -step)) / step) * step));
                e.Handled = true; Commit(delegate { set(v); }, false);
            };
            return panel;
        }

        // − value +, for whole numbers.
        FrameworkElement Stepper(string id, Func<int> get, Action<int> set, int min, int max, int step, string unit)
        {
            var frame = new Border { BorderBrush = MemoTheme.Brush(Theme.Rule), BorderThickness = new Thickness(1), Height = 26, Width = 118 };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            var less = Arrow("−", "Less"); var more = Arrow("+", "More");
            AutomationProperties.SetAutomationId(less, "Pref-" + id + "-Less"); AutomationProperties.SetAutomationId(more, "Pref-" + id + "-More");
            var value = new TextBlock { FontSize = Theme.Size(11), Foreground = MemoTheme.Brush(Theme.White), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(value, 1); Grid.SetColumn(more, 2);
            grid.Children.Add(less); grid.Children.Add(value); grid.Children.Add(more);
            frame.Child = grid;
            AutomationProperties.SetAutomationId(frame, "Pref-" + id);
            Action<int> by = delegate(int direction)
            {
                int v = Math.Max(min, Math.Min(max, get() + direction * step));
                if (v != get()) set(v);
            };
            less.MouseLeftButtonUp += delegate { by(-1); };
            more.MouseLeftButtonUp += delegate { by(1); };
            frame.MouseWheel += delegate(object sender, MouseWheelEventArgs e) { e.Handled = true; by(e.Delta > 0 ? 1 : -1); };
            refreshers.Add(delegate { value.Text = get() + unit; });
            return frame;
        }

        // For --preferences-test: a picture of each tab, to check the layout in a theme without opening it by hand.
        internal void Snapshot(string fileStem)
        {
            for (int i = 0; i < pages.Length; i++)
            {
                ShowTab(i); UpdateLayout();
                var content = (FrameworkElement)Content;
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(content);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using (var stream = System.IO.File.Create(fileStem + "-" + TabNames[i] + ".png")) encoder.Save(stream);
            }
        }
    }
}
