// Snapline: a local screenshot shelf for Windows. MIT License.
// Program entry point and self-test.
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

[assembly: System.Reflection.AssemblyTitle("Snapline")]
[assembly: System.Reflection.AssemblyDescription("Private, local screenshot shelf for Windows")]
[assembly: System.Reflection.AssemblyVersion("0.5.8.0")]

namespace Snapline
{
    static class Program
    {
        static string Option(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
        [STAThread] static int Main(string[] args)
        {
            string root = Option(args, "--data-dir") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Snapline");
            try
            {
                Theme.ForceLively = args.Any(x => x.EndsWith("-test", StringComparison.Ordinal));
                if (args.Contains("--self-test")) { SelfTest(root); return 0; }
                if (args.Contains("--integration-test")) return Integration.Run(root);
                if (args.Contains("--memo-test")) return MemoTests.Run(root);
                if (args.Contains("--shortcut-test")) return ShortcutTests.Run(root);
                if (args.Contains("--hover-test")) return HoverTests.Run(root);
                if (args.Contains("--startup-test")) return StartupTests.Run(root);
                if (args.Contains("--remark-test")) return RemarkTests.Run(root);
                if (args.Contains("--interaction-test")) return InteractionTests.Run(root);
                if (args.Contains("--preferences-test")) return PreferencesTests.Run(root);
                bool fresh;
                using (var mutex = new Mutex(true, "Local\\Snapline-" + BitConverter.ToString(SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(root)))).Replace("-", "").Substring(0, 20), out fresh))
                {
                    if (!fresh && args.Contains("--restarted"))
                    {
                        // Started by a running Snapline that is closing to change theme: wait for it to finish.
                        try { fresh = mutex.WaitOne(10000); } catch (AbandonedMutexException) { fresh = true; }
                    }
                    if (!fresh) { if (!args.Contains("--startup")) Native.PostMessage(new IntPtr(0xffff), Native.ShowMessage, IntPtr.Zero, IntPtr.Zero); return 0; }
                    var store = new Store(root);
                    GC.Collect(); // Let go of what loading the line needed before the app settles in for the day.
                    Theme.Apply(Option(args, "--theme") ?? store.Settings.ThemeName);
                    Theme.ApplyFont(Option(args, "--font") ?? store.Settings.FontName);
                    Theme.AnimationMode = Tuning.Animation(store.Settings); Theme.FunOnBattery = store.Settings.FunOnBattery;
                    var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                    try { app.Resources.MergedDictionaries.Add(Theme.MenuResources()); }
                    catch (Exception ex) { store.Log("Menu style", ex); } // Plain menus still work.
                    app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e)
                    {
                        store.Log("UI error", e.Exception);
                        e.Handled = true;
                    };
                    bool demo = args.Contains("--demo");
                    Theme.ForceCalm = args.Contains("--calm"); // Basic whatever the setting, for checking it.
                    if (demo && store.Items.Count == 0)
                    {
                        store.Add(Demo("IDEAS", "Hang up an idea", "#315B75"), "Sample · Ideas");
                        store.Add(Demo("A LITTLE BREAK", "Time for a coffee", "#8C6B4D"), "Sample · Daily");
                        store.Add(Demo("READY TO SHARE", "Screenshots within reach", "#426C60"), "Sample · Share");
                    }
                    if (args.Contains("--demo-notes") && store.Notes.Count == 0)
                    {
                        store.AddNote("Weekend plan\\nWalk in the park, bring the camera.\\n\\nLeave some time to slow down.".Replace("\\n", "\n"), "Butter");
                        store.AddNote("**Today**\\n[x] Sort the files\\n[ ] Answer *one* email\\n[ ] Leave room for an idea".Replace("\\n", "\n"), "Mint");
                        store.AddNote("A small idea\\nWrite things down\\nand hang them up like photos.".Replace("\\n", "\n"), "Lavender");
                        if (args.Contains("--demo-picture-note"))
                            store.SetNotePicture(store.AddNote("[ ] Print the map\\nFor the trip on Saturday.".Replace("\\n", "\n"), "Butter"), Demo("THE MAP", "A picture inside a note", "#746DA1"));
                    }
                    if (demo && args.Contains("--demo-remarks") && store.Items.Count > 0)
                    {
                        string[] examples = { "Use this layout for the Friday proposal.", "A small idea for next time.", "Project colours; confirm with the team." };
                        for (int i = 0; i < store.Items.Count; i++) store.UpdateRemark(store.Items[i], examples[i % examples.Length]);
                    }
                    var shelf = new Shelf(store, args.Contains("--no-capture"), demo);
                    shelf.StartCollapsed = args.Contains("--startup");
                    if (StartupRegistration.ManagesSystemStartup(args))
                        shelf.Startup = new StartupRegistration(store, new RegistryStartupEntry(), System.Reflection.Assembly.GetExecutingAssembly().Location);
                    app.MainWindow = shelf;
                    var quit = new DispatcherTimer();
                    string seconds = Option(args, "--quit-after");
                    if (seconds != null) { quit.Interval = TimeSpan.FromSeconds(Double.Parse(seconds, System.Globalization.CultureInfo.InvariantCulture)); quit.Tick += delegate { quit.Stop(); shelf.Close(); }; quit.Start(); }
                    string snapshot = Option(args, "--snapshot");
                    string diagnostics = Option(args, "--diagnostics");
                    string cardRects = Option(args, "--card-rects");
                    if (cardRects != null)
                    {
                        var report = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                        report.Tick += delegate { try { shelf.WriteCardRects(cardRects); } catch (Exception ex) { store.Log("Card rects", ex); } };
                        report.Start();
                    }
                    shelf.Loaded += delegate
                    {
                        var delay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                        delay.Tick += delegate
                        {
                            delay.Stop();
                            if (snapshot != null) shelf.Snapshot(snapshot);
                            string editorSnapshot = Option(args, "--editor-snapshot");
                            if (editorSnapshot != null && store.Notes.Count > 0)
                            {
                                var editor = new MemoEditor(store, store.Notes.FirstOrDefault(x => NoteMarkup.HasTodo(x.Text)) ?? store.Notes[0]);
                                editor.Show(); editor.Snapshot(editorSnapshot); editor.Close();
                            }
                            if (diagnostics != null) System.IO.File.WriteAllText(diagnostics, "collectionMode=ConfiguredShortcutOnly" + Environment.NewLine + "captureUi=BuiltinRegion" + Environment.NewLine + "clipboardListener=" + shelf.ClipboardRegistered + Environment.NewLine + "hotkey=" + shelf.HotkeyRegistered + Environment.NewLine + "captureHotkey=" + shelf.CaptureHotkeyRegistered + Environment.NewLine + "captureShortcut=" + shelf.CaptureShortcutLabel + Environment.NewLine + "items=" + store.Items.Count + Environment.NewLine + "notes=" + store.Notes.Count + Environment.NewLine + "expanded=" + shelf.Expanded + Environment.NewLine + "startupManaged=" + (shelf.Startup != null) + Environment.NewLine + "startupEnabled=" + store.Settings.StartupEnabled + Environment.NewLine + "windowHandle=" + new WindowInteropHelper(shelf).Handle.ToInt64() + Environment.NewLine);
                        };
                        delay.Start();
                    };
                    shelf.Show();
                    app.Run();
                    mutex.ReleaseMutex();
                    return 0;
                }
            }
            catch (Exception ex)
            {
                Directory.CreateDirectory(root);
                System.IO.File.WriteAllText(Path.Combine(root, "startup-error.log"), ex.ToString(), Encoding.UTF8);
                Forms.MessageBox.Show("Could not start: " + ex.Message + "\nDetails were written to " + Path.Combine(root, "startup-error.log"), "Snapline");
                return 1;
            }
        }
        internal static BitmapSource Demo(string title, string caption, string color)
        {
            var visual = new DrawingVisual();
            using (var c = visual.RenderOpen())
            {
                var background = (SolidColorBrush)new BrushConverter().ConvertFromString(color);
                c.DrawRectangle(background, null, new System.Windows.Rect(0, 0, 640, 400));
                c.DrawEllipse(new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), null, new System.Windows.Point(505, 45), 170, 170);
                c.DrawEllipse(new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)), null, new System.Windows.Point(60, 400), 170, 170);
                c.DrawText(new FormattedText("S N A P L I N E", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 16, Brushes.White, 1.0), new System.Windows.Point(45, 38));
                c.DrawText(new FormattedText(title, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 35, Brushes.White, 1.0), new System.Windows.Point(45, 170));
                c.DrawText(new FormattedText(caption, System.Globalization.CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight, new Typeface("Microsoft YaHei"), 24, Brushes.White, 1.0), new System.Windows.Point(45, 232));
                c.DrawRectangle(new SolidColorBrush(Color.FromArgb(130, 255, 255, 255)), null, new System.Windows.Rect(45, 315, 95, 3));
            }
            var bitmap = new RenderTargetBitmap(640, 400, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
        }
        static void Assert(bool condition, string message) { if (!condition) throw new Exception("TEST FAILED: " + message); }
        static void SelfTest(string root)
        {
            if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()) throw new InvalidOperationException("Self-test requires an empty, isolated data directory.");
            var results = new List<string>();
            var store = new Store(root);
            BitmapSource image = Demo("TEST", "Local image", "#426C60");
            Shot first = store.Add(image, "test");
            Assert(first != null && System.IO.File.Exists(store.FullPath(first)), "PNG saved");
            Assert(store.Add(image, "duplicate") == null && store.Items.Count == 1, "Duplicate pixels rejected");
            Assert(Store.Hash(image) == Store.Hash(Store.Load(store.FullPath(first))), "PNG round-trip hash");
            var preview = Store.Preview(store.FullPath(first), 120, 90, image.PixelWidth / (double)image.PixelHeight);
            Assert(preview.PixelWidth <= 120 && preview.PixelHeight <= 90 && preview.IsFrozen, "Preview decode is bounded and frozen");
            using (var exclusive = System.IO.File.Open(store.FullPath(first), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Assert(exclusive.Length > 0, "Preview releases its source file handle");
            Assert(Store.Hash(image) == Store.Hash(Store.Load(store.FullPath(first))), "Preview preserves cache image");
            results.Add("PASS: enlarged preview decoding is bounded, source handle released and cached image unchanged");
            var restored = new Store(root);
            Assert(restored.Items.Count == 1 && restored.Items[0].Thumb != null, "Persistence and thumbnails");
            string external = Path.Combine(root, "external.png");
            System.IO.File.Copy(store.FullPath(first), external);
            store.Remove(first);
            Assert(System.IO.File.Exists(external), "Remove preserves external original");
            Assert(store.Items.Count == 0 && !System.IO.File.Exists(store.FullPath(first)), "Remove clears only cache");
            Shot imported = store.Import(external);
            Assert(imported != null && store.FullPath(imported) != external, "Import makes own copy");
            Assert(!store.IsSafe("../external.png") && !store.IsSafe("external.png") && !store.IsSafe("C:\\x.png"), "Cache path traversal blocked");
            store.Clear();
            Assert(System.IO.File.Exists(external), "Clear preserves source");
            store.Settings.ClipboardImages = false; store.SaveSettings();
            Assert(!new Store(root).Settings.ClipboardImages, "Preference persistence");
            store.Settings.ClipboardImages = true; store.Settings.ScreenshotFolder = true; store.Settings.Folders.Add("C:\\LegacyScreenshots"); store.SaveSettings();
            var restricted = new Store(root);
            Assert(!restricted.Settings.ClipboardImages && !restricted.Settings.ScreenshotFolder && restricted.Settings.Folders.Count == 1, "Legacy broad collection is disabled without deleting folder preferences");
            results.Add("PASS: legacy clipboard and external-folder collection preferences are disabled");
            store.Settings.CaptureShortcut = "ShiftPrintScreen"; store.SaveSettings();
            Assert(new Store(root).Settings.CaptureShortcut == "ShiftPrintScreen", "Shortcut choice persists");
            store.Settings.CaptureShortcut = "None"; store.SaveSettings();
            Assert(new Store(root).Settings.CaptureShortcut == "None", "Disabled shortcut stays disabled");
            System.IO.File.WriteAllText(Path.Combine(root, "settings.json"), "{\"ClipboardImages\":false,\"ScreenshotFolder\":false,\"Folders\":[],\"WelcomeShown\":true}");
            var migrated = new Store(root);
            Assert(migrated.Settings.CaptureShortcut == "CtrlB" && !migrated.Settings.ClipboardImages && !migrated.Settings.ScreenshotFolder && migrated.Settings.WelcomeShown, "Old settings migrate without resetting preferences");
            System.IO.File.WriteAllText(Path.Combine(root, "settings.json"), "{\"CaptureShortcut\":\"CtrlPrintScreen\",\"ClipboardImages\":false,\"ScreenshotFolder\":false,\"Folders\":[],\"WelcomeShown\":true}");
            var upgraded = new Store(root);
            Assert(upgraded.Settings.CaptureShortcut == "CtrlB" && upgraded.Settings.CaptureShortcutRevision == 1 && !upgraded.Settings.ClipboardImages, "Previous default migrates to Ctrl+B once");
            upgraded.Settings.CaptureShortcut = "CtrlPrintScreen"; upgraded.SaveSettings();
            Assert(new Store(root).Settings.CaptureShortcut == "CtrlPrintScreen", "Explicit alternate choice survives future restarts");
            results.Add("PASS: old Ctrl+PrtSc default migrates to Ctrl+B once; explicit alternatives remain selectable");
            results.Add("PASS: shortcut selection and disabled state persist; old settings migrate preserving preferences");
            Assert(Shelf.ImageFile("x.PNG") && !Shelf.ImageFile("x.exe"), "Image extension filter");
            for (int i = 0; i < Store.MaxItems + 2; i++)
                store.Add(Demo("SHOT " + i, "测试", i % 2 == 0 ? "#426C60" : "#315B75"), "item " + i);
            Assert(store.Items.Count == Store.MaxItems, "Retention capped at 60");
            Assert(Directory.GetFiles(store.Inbox, "*.png").Length == Store.MaxItems, "Old cache files pruned");
            Assert(new Store(root).Items.Count == Store.MaxItems, "Retention survives restart");
            results.Add("PASS: PNG save/load, pixel deduplication, persistence, thumbnails");
            results.Add("PASS: imported source preserved during remove and clear");
            results.Add("PASS: path traversal rejected, settings persisted, extension filtering");
            results.Add("PASS: 60-item retention and cache cleanup");
            System.IO.File.WriteAllLines(Path.Combine(root, "self-test-results.txt"), results, Encoding.UTF8);
        }
    }
}
