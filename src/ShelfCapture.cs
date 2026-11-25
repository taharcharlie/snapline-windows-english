// Snapline: a local screenshot shelf for Windows. MIT License.
// Capture shortcut, importing, copying and file actions.
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
    sealed partial class Shelf
    {
        internal void RegisterCaptureShortcut()
        {
            Native.UnregisterHotKey(handle, captureHotkeyId);
            captureHotkeyId = 2;
            var shortcut = ShortcutSpec.From(store.Settings);
            CaptureHotkeyRegistered = shortcut != null && Native.RegisterHotKey(handle, captureHotkeyId, shortcut.Modifiers | 0x4000u, shortcut.Key);
        }
        internal void SelectCaptureShortcut(string shortcut)
        {
            string error;
            if (!TrySelectCaptureShortcut(shortcut, 0, 0, out error)) Notify(error);
        }
        void UpdateShortcutMenu()
        {
            UpdateLineShortcutHints();
            if (captureMenu != null) captureMenu.Text = "Capture region (copy only)";
            foreach (var choice in shortcutChoices)
            {
                choice.Value.Checked = choice.Key == store.Settings.CaptureShortcut;
                if (choice.Key == "Custom")
                    choice.Value.Text = store.Settings.CaptureShortcut == "Custom" ? "Custom… (" + CaptureShortcutLabel + ")" : "Custom…";
            }
        }
        internal void StartCapture() { StartCapture(false); }
        internal void StartCapture(bool collect, uint triggerKey = 0)
        {
            if (capturePending || shuttingDown) return;
            CloseFallingPapers();
            CloseImagePreview(); CloseNotePreview();
            if (helpTip != null) helpTip.IsOpen = false;
            pinned = false; Collapse(true, false);
            canvas.Visibility = Visibility.Hidden;
            capturePending = true; captureSerial++;
            collectCapture = collect; captureTriggerKey = triggerKey;
            captureRequested = DateTime.Now;
            hideUntil = DateTime.Now.AddSeconds(5);
            captureDelay.Start();
        }
        void CaptureWhenReleased(object sender, EventArgs e)
        {
            if (!Native.CaptureKeysReleased(captureTriggerKey))
            {
                if ((DateTime.Now - captureRequested).TotalSeconds < 5) return;
                captureDelay.Stop(); CompleteCapture(captureSerial, null, false);
                Notify("Release the shortcut keys and capture again."); return;
            }
            if ((DateTime.Now - captureRequested).TotalMilliseconds < 240) return;
            captureDelay.Stop();
            int session = captureSerial;
            bool collect = collectCapture;
            try { CaptureLauncher(delegate(BitmapSource result) { CompleteCapture(session, result, collect); }); }
            catch (Exception ex)
            {
                CompleteCapture(session, null, false);
                store.Log("Capture selection", ex); Notify(ex.Message);
            }
        }
        void CompleteCapture(int session, BitmapSource image, bool collect)
        {
            if (shuttingDown || !capturePending || session != captureSerial) return;
            capturePending = false; canvas.Visibility = Visibility.Visible;
            // Cancellation never reads, clears or replaces the clipboard.
            if (image == null) return;
            bool copied = true;
            try { var data = new DataObject(); data.SetImage(image); SetClipboard(data); }
            catch (Exception ex) { copied = false; store.Log("Copy captured image", ex); }
            if (collect && !noCapture)
            {
                store.Add(image, "Snapline capture");
                Notify(copied ? "Captured, hung and copied. Click 'Add caption' under the image to note its purpose." : "Captured and hung; the clipboard was busy, click the image to copy it.");
            }
            else Notify(copied ? (ShortcutCollects ? "Screenshot copied. To hang it automatically, use " + CaptureShortcutLabel + "." :
                "Screenshot copied. To hang automatically, set a capture shortcut in the tray menu.") : "The clipboard was busy and the screenshot was not copied. Try again.");
        }
        internal void SetupWatchers()
        {
            if (noCapture || inboxWatcher != null) return;
            // Refresh existing cached images after editing, without importing other files.
            inboxWatcher = new FileSystemWatcher(store.Inbox, "*.png") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName };
            inboxWatcher.Changed += delegate(object s, FileSystemEventArgs e) { Dispatcher.BeginInvoke(new Action(delegate { if (!shuttingDown) pending[e.FullPath] = DateTime.Now; })); };
            inboxWatcher.EnableRaisingEvents = true;
        }
        internal static bool ImageFile(string file)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            return new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" }.Contains(ext);
        }

        void ProcessPending()
        {
            foreach (var file in pending.Keys.ToArray())
            {
                if ((DateTime.Now - pending[file]).TotalMilliseconds < 550) continue;
                try
                {
                    if (!System.IO.File.Exists(file)) { pending.Remove(file); continue; }
                    if (Path.GetDirectoryName(file).Equals(store.Inbox, StringComparison.OrdinalIgnoreCase))
                    {
                        var item = store.Items.FirstOrDefault(x => store.FullPath(x).Equals(file, StringComparison.OrdinalIgnoreCase));
                        if (item != null) store.Refresh(item);
                    }
                    pending.Remove(file);
                }
                catch (Exception ex)
                {
                    // Retry partially written files, bounded so corrupt images cannot cause a busy loop.
                    if ((DateTime.Now - pending[file]).TotalSeconds > 10) { pending.Remove(file); store.Log("Import watched image", ex); }
                }
            }
        }
        void Import(string file)
        {
            try { if (!ImageFile(file)) { Notify("PNG, JPEG, BMP, GIF and TIFF images are supported."); return; } var added = store.Import(file); Notify(added == null ? "This image is already on the shelf." : "Added to the shelf. The original file is untouched."); }
            catch (Exception ex) { store.Log("Import image", ex); Notify("Could not import: " + ex.Message); }
        }
        void ChooseFiles()
        {
            dialogOpen = true;
            try
            {
                using (var picker = new Forms.OpenFileDialog { Title = "Import images", Multiselect = true, Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff" })
                    if (picker.ShowDialog() == Forms.DialogResult.OK) foreach (string file in picker.FileNames) Import(file);
            }
            finally { dialogOpen = false; }
        }

        internal void Copy(Shot shot)
        {
            try
            {

                var data = new DataObject();
                var image = Store.Load(store.FullPath(shot));
                data.SetImage(image);

                SetClipboard(data);

                Flash(shot, "Copied");
            }
            catch (Exception ex) { store.Log("Copy image", ex); Notify("Copy failed. Try again shortly."); }

        }
        internal void CopyFile(Shot shot)
        {
            try
            {

                SetClipboard(new DataObject(DataFormats.FileDrop, new[] { store.FullPath(shot) }));

                Notify("PNG file copied. Paste it into a folder.");
            }
            catch (Exception ex) { store.Log("Copy file", ex); Notify("Could not copy the file. Try again shortly."); }

        }
        internal static void SetClipboard(DataObject data)
        {
            // Clipboard owners (including chat apps) can hold the lock briefly.
            for (int attempt = 0; ; attempt++)
            {
                try { Clipboard.SetDataObject(data, true); return; }
                catch (COMException) { if (attempt >= 11) throw; Thread.Sleep(70); }
            }
        }
        void Launch(string file)
        {
            try { Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); }
            catch (Exception ex) { store.Log("Open file", ex); Notify("Could not open: " + ex.Message); }
        }
        void Edit(Shot shot)
        {
            try { Process.Start(new ProcessStartInfo("mspaint.exe", "\"" + store.FullPath(shot) + "\"") { UseShellExecute = true }); Notify("Opened in Paint. The shelf updates when you save."); }
            catch (Exception ex) { store.Log("Open editor", ex); Notify("Could not open Paint. Right-click and choose Open image."); }
        }
        void SaveAs(Shot shot)
        {
            dialogOpen = true;
            try
            {
                using (var picker = new Forms.SaveFileDialog { Title = "Save screenshot as", FileName = shot.File, Filter = "PNG image|*.png", InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) })
                    if (picker.ShowDialog() == Forms.DialogResult.OK)
                    {
                        if (Path.GetFullPath(picker.FileName).Equals(store.FullPath(shot), StringComparison.OrdinalIgnoreCase)) return;
                        System.IO.File.Copy(store.FullPath(shot), picker.FileName, true); Notify("Saved. The copy on the shelf is kept.");
                    }
            }
            catch (Exception ex) { store.Log("Save image", ex); Notify("Could not save: " + ex.Message); }
            finally { dialogOpen = false; }
        }
    }
}
