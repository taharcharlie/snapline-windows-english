// Snapline: a local screenshot shelf for Windows. MIT License.
// --preferences-test: the settings behind the Preferences window, and the window itself.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Snapline
{
    static class PreferencesTests
    {
        static void Check(bool passed, string text) { if (!passed) throw new Exception("Preferences test failed: " + text); }

        static IEnumerable<DependencyObject> Descendants(DependencyObject value)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(value); i++)
            {
                var child = VisualTreeHelper.GetChild(value, i);
                yield return child;
                foreach (var deeper in Descendants(child)) yield return deeper;
            }
        }
        static FrameworkElement Find(Window window, string id)
        {
            return Descendants(window).OfType<FrameworkElement>().First(x => AutomationProperties.GetAutomationId(x) == id);
        }
        static void Click(FrameworkElement target)
        {
            target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
        }

        internal static int Run(string root)
        {
            if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()) throw new InvalidOperationException("Preferences tests require an empty isolated directory.");
            var report = new List<string>();
            int exit = 1;
            try
            {
                var store = new Store(root); var s = store.Settings;
                Check(Tuning.CloseDelay(s) == 700 && Tuning.UndoTime(s) == 7 && Tuning.OpenDelay(s) == 0 && Tuning.Animation(s) == 0 && !s.FunOnBattery && store.ShotLimit == Store.MaxItems && store.LinkLimit == Store.MaxLinks
                    && Tuning.PreviewDelay(s, 280) == 280 && Tuning.PreviewDelay(s, 300) == 300 && s.CardSize == 0, "Fresh settings keep the original behaviour");
                report.Add("PASS: a fresh settings file keeps every original timing, limit and size");

                s.PreviewDelay = 9999; s.CloseDelay = 10; s.UndoTime = 99; s.OpenDelay = -5; s.Animation = 7; s.ShotLimit = 3; s.LinkLimit = 500; store.SaveSettings();
                var read = new Store(root).Settings;
                Check(Tuning.PreviewDelay(read, 280) == 1500 && Tuning.CloseDelay(read) == 300 && Tuning.UndoTime(read) == 30 && Tuning.OpenDelay(read) == 0 && Tuning.Animation(read) == 0
                    && Tuning.ShotLimit(read) == 10 && Tuning.LinkLimit(read) == 200, "Out-of-range values are clamped after a restart");
                s.PreviewDelay = 450; s.CloseDelay = 1200; s.UndoTime = 12; s.OpenDelay = 250; s.Animation = 1; store.SaveSettings();
                read = new Store(root).Settings;
                Check(Tuning.PreviewDelay(read, 280) == 450 && Tuning.PreviewDelay(read, 220) == 450 && Tuning.CloseDelay(read) == 1200 && Tuning.UndoTime(read) == 12 && Tuning.OpenDelay(read) == 250 && Tuning.Animation(read) == 1,
                    "Chosen timings persist");
                s.PreviewDelay = 0; s.CloseDelay = 0; s.UndoTime = 0; s.OpenDelay = 0; s.Animation = 0; s.ShotLimit = 0; s.LinkLimit = 0; store.SaveSettings();
                report.Add("PASS: timings persist across a restart; out-of-range values are clamped");

                for (int i = 0; i < 14; i++) store.Add(Program.Demo("LIMIT " + i, "Preferences", i % 2 == 0 ? "#315B75" : "#426C60"), "limit " + i);
                var oldest = store.Items.Last(); store.SetKept(oldest, true);
                Check(store.OverLimit(10, 60) == 3 && store.OverLimit(60, 60) == 0, "Cards past a lower limit are counted");
                s.ShotLimit = 10; store.SaveSettings(); store.ApplyLimits();
                Check(store.Items.Count == 11 && store.Items.Contains(oldest) && Directory.GetFiles(store.Inbox, "*.png").Length == 11 && new Store(root).Items.Count == 11, "A lower limit removes only the oldest unkept screenshots");
                store.Add(Program.Demo("ONE MORE", "Preferences", "#8C6B4D"), "one more");
                Check(store.Items.Count == 11 && store.Items.Contains(oldest), "Hanging one more keeps to the lower limit");
                report.Add("PASS: lowering the screenshot limit removes only the oldest unkept copies, keeps kept cards, and holds for new ones");

                bool lively = Theme.ForceLively; Theme.ForceLively = false;
                try
                {
                    Theme.AnimationMode = 0; Check(!Theme.Fun, "Basic keeps the cards still");
                    Theme.AnimationMode = 1; Theme.FunOnBattery = true; Check(Theme.Fun, "Fun on battery when asked");
                    Theme.FunOnBattery = false; Check(Theme.Fun == !Theme.OnBattery, "Fun reduces to Basic on battery");
                }
                finally { Theme.ForceLively = lively; Theme.AnimationMode = 0; Theme.FunOnBattery = false; }
                Directory.CreateDirectory(Path.Combine(root, "migrate"));
                File.WriteAllText(Path.Combine(root, "migrate", "settings.json"), "{\"Motion\":1}");
                var migrated = new Store(Path.Combine(root, "migrate")).Settings;
                Check(migrated.Animation == 1 && migrated.FunOnBattery && migrated.AnimationRevision == 1, "Always swaying becomes Fun, even on battery");
                File.WriteAllText(Path.Combine(root, "migrate", "settings.json"), "{\"Motion\":2}");
                migrated = new Store(Path.Combine(root, "migrate")).Settings;
                Check(migrated.Animation == 0 && !migrated.FunOnBattery, "Never and Auto become Basic");
                File.WriteAllText(Path.Combine(root, "migrate", "settings.json"), "{\"Motion\":1,\"Animation\":0,\"AnimationRevision\":1}");
                Check(new Store(Path.Combine(root, "migrate")).Settings.Animation == 0, "The migration runs only once");
                report.Add("PASS: Basic is still, Fun moves, Fun reduces to Basic on battery unless asked; the old sway setting migrates once");
                var swing = new Pendulum { Angle = 1.5, Target = 1.5, Speed = 20 }; double widest = 0;
                for (int i = 0; i < 90; i++) { Pendulum.Step(swing, 0, 1.0 / 60); widest = Math.Max(widest, Math.Abs(swing.Angle - 1.5)); }
                Check(widest > 1 && widest <= Pendulum.MaxSwing && Math.Abs(swing.Angle - 1.5) < 0.2, "A kicked card swings, stays within bounds and settles within a second and a half: widest " + widest + ", left " + (swing.Angle - 1.5));
                var glide = new Pendulum(); double peak = 0;
                for (int i = 0; i < 60; i++) { Pendulum.Step(glide, i < 6 ? 10000 : (i < 12 ? -10000 : 0), 1.0 / 60); peak = Math.Max(peak, Math.Abs(glide.Angle)); }
                Check(peak > 1 && peak <= Pendulum.MaxSwing, "A glide swings the cards a few degrees: peak " + peak);
                var dropped = new Pendulum { Y = -26 };
                for (int i = 0; i < 60; i++) Pendulum.Step(dropped, 0, 1.0 / 60);
                Check(Math.Abs(dropped.Y) < 0.5, "A newly hung card settles onto the line within a second: " + dropped.Y);
                report.Add("PASS: Fun pendulum swings within 8 degrees, settles within 1.5 s, responds to a glide, and a dropped card lands within a second");

                Check(LineShortcut.Open.Of(s).SameKeys(new ShortcutSpec(3, 0x53)) && LineShortcut.Hang.Of(s).SameKeys(new ShortcutSpec(3, 0x44)) && LineShortcut.Note.Of(s).SameKeys(new ShortcutSpec(3, 0x4E))
                    && LineShortcut.All.All(x => x.IsDefault(s) && !x.IsOff(s)), "Fresh settings keep Ctrl + Alt + S, D and N");
                Check(LineShortcut.Note.Encode(LineShortcut.Note.Default) == "" && LineShortcut.Note.Encode(null) == "off" && LineShortcut.Note.Encode(new ShortcutSpec(6, 0x77)) == "6,119", "Shortcuts are written as the original, off, or keys");
                LineShortcut.Note.Write(s, "6,119"); store.SaveSettings();
                Check(LineShortcut.Note.Of(new Store(root).Settings).Label == "Ctrl + Shift + F8", "Own keys persist across a restart");
                LineShortcut.Note.Write(s, "nonsense"); Check(LineShortcut.Note.IsDefault(s), "Unreadable keys read as the original");
                LineShortcut.Note.Write(s, "0,65"); Check(LineShortcut.Note.IsDefault(s), "Keys without Ctrl, Shift or Alt read as the original");
                LineShortcut.Note.Write(s, "off"); Check(LineShortcut.Note.Of(s) == null && LineShortcut.Note.Label(s) == "Off", "Off reads as off");
                LineShortcut.Note.Write(s, "");
                Check(ShortcutSpec.Conflict(s, new ShortcutSpec(3, 0x44), LineShortcut.Note).Contains("hangs the clipboard")
                    && ShortcutSpec.Conflict(s, new ShortcutSpec(3, 0x53), null).Contains("opens the line")
                    && ShortcutSpec.Conflict(s, ShortcutSpec.From(s), LineShortcut.Open).Contains("captures a region")
                    && ShortcutSpec.Conflict(s, new ShortcutSpec(6, 0x77), LineShortcut.Note) == null, "Snapline's own shortcuts cannot share keys");
                report.Add("PASS: open, hang and note shortcuts default to Ctrl + Alt + S, D, N; own keys and off persist; unreadable keys fall back; no two of Snapline's shortcuts share keys");

                s.CaptureShortcut = "None"; s.WelcomeShown = true; s.ShotLimit = 0; store.SaveSettings();
                var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                var shelf = new Shelf(store, true, false); app.MainWindow = shelf;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                int phase = 0; DateTime phaseStart = DateTime.Now; bool snappedMid = false, snappedHold = false; Shot heldShot = null, painted = null;
                timer.Tick += delegate
                {
                    if (phase != 0) return;
                    timer.Stop();
                    try
                    {
                        shelf.ShowPreferences(); var window = shelf.Preferences;
                        Check(window != null && window.IsVisible, "Preferences opens");
                        shelf.ShowPreferences(); Check(Object.ReferenceEquals(window, shelf.Preferences), "Asking again brings the same window forward");
                        window.UpdateLayout();
                        Click(Find(window, "Pref-Animation-1"));
                        Check(s.Animation == 1 && Theme.AnimationMode == 1 && new Store(root).Settings.Animation == 1, "Choosing Fun saves it and applies it");
                        Click(Find(window, "Pref-ReduceOnBattery"));
                        Check(s.FunOnBattery && new Store(root).Settings.FunOnBattery, "Turning battery reduction off saves it");
                        Click(Find(window, "Pref-ReduceOnBattery"));
                        Click(Find(window, "Pref-Animation-0"));
                        Click(Find(window, "Pref-UndoTime-More"));
                        Check(Tuning.UndoTime(s) == 8 && new Store(root).Settings.UndoTime == 8, "The Undo stepper saves its value");
                        Click(Find(window, "Pref-TouchLine-1"));
                        Check(s.TriggerLength == 1 && new Store(root).Settings.TriggerLength == 1, "Touch line length saves from Preferences");
                        Check(Find(window, "Pref-CardSize") != null && Find(window, "Pref-Theme") != null && Find(window, "Pref-Font") != null && Find(window, "Pref-Shortcut") != null
                            && Find(window, "Pref-AutoClear") != null && Find(window, "Pref-ShotLimit") != null && Find(window, "Pref-LinkLimit") != null && Find(window, "Pref-PreviewDelay") != null
                            && Find(window, "Pref-CloseDelay") != null && Find(window, "Pref-OpenDelay") != null && Find(window, "Pref-Tension") != null, "Every setting has its control");
                        string error;
                        Check(shelf.TrySetLineShortcut(LineShortcut.Note, new ShortcutSpec(6, 0x77), out error) && shelf.NoteHotkeyRegistered && new Store(root).Settings.NoteShortcut == "6,119",
                            "A note shortcut of your own registers and saves: " + error);
                        Check(!shelf.TrySetLineShortcut(LineShortcut.Hang, new ShortcutSpec(6, 0x77), out error) && error.Contains("starts a new note") && LineShortcut.Hang.IsDefault(s),
                            "A shortcut already used by Snapline is refused and the old one kept");
                        Check(!shelf.TrySelectCaptureShortcut("Custom", 6, 0x77, out error) && error.Contains("starts a new note"), "The capture shortcut cannot take another shortcut's keys");
                        window.UpdateLayout();
                        Click(Find(window, "Pref-Shortcut-Note-2"));
                        Check(LineShortcut.Note.IsOff(s) && !shelf.NoteHotkeyRegistered && new Store(root).Settings.NoteShortcut == "off", "Off in Preferences turns the note shortcut off");
                        Check(Find(window, "Pref-Shortcut-Open") != null && Find(window, "Pref-Shortcut-Hang") != null, "Every shortcut has its row");
                        report.Add("PASS: line shortcuts register with keys of your own, turn off from Preferences, and refuse keys another Snapline shortcut has");
                        window.Snapshot(Path.Combine(root, "preferences"));
                        Check(PreferencesWindow.TabNames.All(x => File.Exists(Path.Combine(root, "preferences-" + x + ".png"))), "A picture of each tab");
                        window.Close();
                        Check(shelf.Preferences == null, "Closing forgets the window");
                        report.Add("PASS: Preferences opens once, its controls save at once, every setting has a control, each tab renders");
                        // Scrolling: enough cards that some are out of view, then the wheel.
                        while (store.Items.Count + store.Notes.Count + store.Links.Count <= shelf.PerPage + 4)
                            store.Add(Program.Demo("SCROLL " + store.Items.Count, "Preferences", "#746DA1"), "scroll " + store.Items.Count);
                        shelf.ForegroundWindow = delegate { return Native.GetShellWindow(); };
                        shelf.TogglePin(); shelf.Render(true); // Pinned open, so it does not close while the test waits.
                        Check(shelf.FirstShown == 0 && !shelf.Gliding, "The line starts at its first card");
                        shelf.WheelForTest(-120);
                        Check(shelf.Gliding && Math.Abs(shelf.ScrollTarget - 1) < 1e-9 && shelf.ScrollView < 1, "One notch starts a glide of one card");
                        phase = 1; phaseStart = DateTime.Now; timer.Interval = TimeSpan.FromMilliseconds(60); timer.Start();
                        return;
                    }
                    catch (Exception ex) { report.Add("FAIL: " + ex); }
                    shelf.Close();
                };
                timer.Tick += delegate
                {
                    if (phase == 0) return;
                    try
                    {
                        double waited = (DateTime.Now - phaseStart).TotalMilliseconds;
                        if (phase == 1 && !snappedMid && waited > 90) { shelf.Snapshot(Path.Combine(root, "glide-mid.png")); snappedMid = true; }
                        if (phase == 1 && waited > 1200)
                        {
                            Check(!shelf.Gliding && shelf.FirstShown == 1 && Math.Abs(shelf.ScrollView - 1) < 1e-9, "The glide settles on the next card: gliding=" + shelf.Gliding + " first=" + shelf.FirstShown + " view=" + shelf.ScrollView + " target=" + shelf.ScrollTarget + " expanded=" + shelf.Expanded);
                            shelf.WheelForTest(-120); shelf.WheelForTest(-120); shelf.WheelForTest(-120);
                            Check(Math.Abs(shelf.ScrollTarget - 4) < 1e-9, "A fast spin adds up to one glide");
                            phase = 2; phaseStart = DateTime.Now;
                        }
                        else if (phase == 2 && waited > 1400)
                        {
                            Check(!shelf.Gliding && shelf.FirstShown == 4, "The fast spin lands four cards on");
                            shelf.WheelForTest(40);
                            Check(Math.Abs(shelf.ScrollTarget - (4 - 1.0 / 3)) < 1e-6, "A touchpad moves the line part of a card");
                            phase = 3; phaseStart = DateTime.Now;
                        }
                        else if (phase == 3 && waited > 1400)
                        {
                            Check(!shelf.Gliding && shelf.FirstShown == 4 && Math.Abs(shelf.ScrollView - 4) < 1e-9, "After the fingers stop it settles on a whole card");
                            bool livelyBefore = Theme.ForceLively; Theme.ForceLively = false; Theme.AnimationMode = 0;
                            try { shelf.WheelForTest(120); Check(shelf.Gliding && Math.Abs(shelf.ScrollTarget - 3) < 1e-9, "Basic animation still glides the line"); }
                            finally { Theme.ForceLively = livelyBefore; }
                            report.Add("PASS: the wheel glides the line one card per notch, a fast spin extends one glide, a touchpad settles on a whole card, and Basic glides too");
                            phase = 31; phaseStart = DateTime.Now;
                        }
                        else if (phase == 31 && waited > 1200)
                        {
                            Check(!shelf.Gliding, "The Basic glide settles");
                            shelf.ShowKeyStripForTest(true); shelf.Snapshot(Path.Combine(root, "key-strip.png")); shelf.ShowKeyStripForTest(false);
                            // Hold a screenshot: the bar fills and the hold fires, with Paint replaced so nothing opens.
                            heldShot = store.Items.FirstOrDefault(x => shelf.CardOnLine(x) != null);
                            shelf.PaintLauncher = delegate(Shot shot) { painted = shot; };
                            var card = shelf.CardOnLine(heldShot);
                            card.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
                            phase = 4; phaseStart = DateTime.Now;
                        }
                        else if (phase == 4 && waited > 560 && !snappedHold) { shelf.Snapshot(Path.Combine(root, "hold.png")); snappedHold = true; }
                        else if (phase == 4 && waited > 1200)
                        {
                            Check(painted != null && Object.ReferenceEquals(painted, heldShot), "Holding a screenshot reaches Edit in Paint");
                            report.Add("PASS: holding a screenshot shows its bar, then opens it for editing; the key strip and a mid-glide frame render");
                            // The compact Tips card: more cards fit, the buttons are there, and the bin is as large as before.
                            int fullPerPage = shelf.PerPage;
                            shelf.ChangeSetting(delegate { s.TipsCard = 1; }, true);
                            Check(shelf.PerPage >= fullPerPage && new Store(root).Settings.TipsCard == 1, "Compact Tips saves and leaves at least as much room");
                            var newNote = Descendants(shelf).OfType<FrameworkElement>().FirstOrDefault(x => AutomationProperties.GetAutomationId(x) == "NewNote");
                            Check(newNote != null, "The compact Tips card keeps the new note button");
                            shelf.ShowBinForTest(true); shelf.UpdateLayout();
                            var bin = shelf.BinForTest;
                            Check(bin != null && bin.ActualWidth >= Math.Round(172 * shelf.CardScale) - 1, "The compact card's bin is full size: " + (bin == null ? 0 : bin.ActualWidth));
                            var leftOfTips = bin.PointToScreen(new Point(12, bin.ActualHeight / 2));
                            var hit = shelf.InputHitTest(shelf.PointFromScreen(leftOfTips)) as DependencyObject; bool onBin = false;
                            for (var at = hit; at != null; at = VisualTreeHelper.GetParent(at)) if (Object.ReferenceEquals(at, bin)) { onBin = true; break; }
                            Check(onBin, "A drop to the left of the slim Tips card lands on the bin");
                            shelf.Snapshot(Path.Combine(root, "tips-compact-bin.png"));
                            shelf.ShowBinForTest(false); shelf.Snapshot(Path.Combine(root, "tips-compact.png"));
                            var help = shelf.HelpBody(); help.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity)); help.Arrange(new Rect(help.DesiredSize)); help.UpdateLayout();
                            var helpPicture = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(help.DesiredSize.Width), (int)Math.Ceiling(help.DesiredSize.Height), 96, 96, PixelFormats.Pbgra32);
                            helpPicture.Render(help);
                            var helpEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); helpEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(helpPicture));
                            using (var stream = File.Create(Path.Combine(root, "tips-help.png"))) helpEncoder.Save(stream);
                            Check(help.DesiredSize.Height < 420, "The Tips fit in a short popup: " + help.DesiredSize.Height);
                            report.Add("PASS: compact Tips card fits at least as many cards, keeps its buttons, and its bin reaches full size and takes drops");
                            exit = 0; timer.Stop(); shelf.Close();
                        }
                    }
                    catch (Exception ex) { report.Add("FAIL: " + ex); timer.Stop(); shelf.Close(); }
                };
                shelf.Loaded += delegate { timer.Start(); };
                shelf.Show();
                app.Run();
            }
            catch (Exception ex) { report.Add("FAIL: " + ex); exit = 1; }
            File.WriteAllLines(Path.Combine(root, "preferences-results.txt"), report, Encoding.UTF8);
            return exit;
        }
    }
}
