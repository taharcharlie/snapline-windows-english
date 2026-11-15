// Snapline: a local screenshot shelf for Windows. MIT License.
// Tray icon, tray menu and the help dialog.
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
        void MakeTray()
        {
            tray = new Forms.NotifyIcon();
            try { tray.Icon = Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location); }
            catch { tray.Icon = Drawing.SystemIcons.Application; }
            tray.Text = "Snapline";
            var menu = new Forms.ContextMenuStrip();
            captureMenu = new Forms.ToolStripMenuItem("Capture region (copy only)", null, delegate { StartCapture(); });
            menu.Items.Add(captureMenu);
            var shortcutMenu = new Forms.ToolStripMenuItem("Capture shortcut");
            string[] modes = { "CtrlB", "CtrlPrintScreen", "ShiftPrintScreen", "Custom", "None" };
            string[] labels = { "Ctrl + B", "Ctrl + PrtSc (copy only)", "Shift + PrtSc (copy only)", "Custom…", "Shortcut off" };
            for (int i = 0; i < modes.Length; i++)
            {
                string selected = modes[i];
                var choice = new Forms.ToolStripMenuItem(labels[i], null, delegate { if (selected == "Custom") EditCaptureShortcut(); else SelectCaptureShortcut(selected); });
                shortcutChoices.Add(selected, choice); shortcutMenu.DropDownItems.Add(choice);
            }
            menu.Items.Add(shortcutMenu);
            UpdateShortcutMenu();
            menu.Items.Add("Open / close (auto-hide)", null, delegate { ToggleFromTray(); });
            menu.Items.Add("New note", null, delegate { NewNote(); });
            menu.Items.Add("Import images…", null, delegate { ChooseFiles(); });
            var tensionMenu = new Forms.ToolStripMenuItem("Line tension");
            var tensions = new[] { new { Name = "Tight", Value = 15 }, new { Name = "Firm", Value = 55 }, new { Name = "Normal", Value = 100 }, new { Name = "Loose", Value = 150 }, new { Name = "Very loose", Value = 200 } };
            foreach (var choice in tensions)
            {
                int value = choice.Value;
                tensionMenu.DropDownItems.Add(new Forms.ToolStripMenuItem(choice.Name, null, delegate { store.Settings.LineSlack = value; store.SaveSettings(); Reveal(); Render(true); keepUntil = DateTime.Now.AddSeconds(3); }) { Tag = value });
            }
            tensionMenu.DropDownOpening += delegate { int now = (int)Math.Round(Slack * 100); foreach (Forms.ToolStripMenuItem item in tensionMenu.DropDownItems) item.Checked = (int)item.Tag == now; };
            menu.Items.Add(tensionMenu);
            pinMenu = new Forms.ToolStripMenuItem("Keep visible", null, delegate { TogglePin(); });
            menu.Items.Add(pinMenu);
            menu.Opening += delegate { pinMenu.Checked = pinned; };
            menu.Items.Add("Previous page", null, delegate { ChangePage(-1); Reveal(); });
            menu.Items.Add("Next page", null, delegate { ChangePage(1); Reveal(); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(new Forms.ToolStripMenuItem("Only shortcut captures are collected") { Enabled = false });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Open storage folder", null, delegate { Launch(store.Inbox); });
            menu.Items.Add("Clear screenshots…", null, delegate
            {
                dialogOpen = true;
                try
                {
                    if (Forms.MessageBox.Show("Clear the " + store.Items.Count + " saved copies? Original screenshots are not deleted.", "Snapline", Forms.MessageBoxButtons.YesNo, Forms.MessageBoxIcon.Question) == Forms.DialogResult.Yes) store.Clear();
                }
                finally { dialogOpen = false; }
            });
            startupMenu = new Forms.ToolStripMenuItem("Start at login", null, delegate
            {
                string error;
                if (Startup != null && !Startup.Apply(!store.Settings.StartupEnabled, out error)) Notify(error);
                startupMenu.Checked = store.Settings.StartupEnabled;
            }) { Enabled = Startup != null };
            menu.Items.Add(startupMenu);
            menu.Opening += delegate { startupMenu.Checked = Startup != null && store.Settings.StartupEnabled; };
            menu.Items.Add("Help", null, delegate { About(); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Quit", null, delegate { Close(); });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { ToggleFromTray(); };
            tray.Visible = true;
        }
        void About()
        {
            dialogOpen = true;
            try
            {
                Forms.MessageBox.Show("Snapline 0.5.8\n\n" + CaptureShortcutLabel + ": capture a region (when the shortcut is available).\n'Tips' card right of the images: hover for help; its button captures and copies only.\nWin + Shift + S and other screenshot tools are not collected.\nCtrl + Alt + S: open / close; stays open once opened.\nDouble-click the tray icon: open temporarily, auto-hides when you leave.\nTray 'Keep visible': tick to stay open, untick to auto-hide again.\nTray 'Start at login': off until you turn it on.\nTouch the line at the top centre: opens the line.\n'Add caption' or the slip under an image: note its purpose; Enter saves, Esc cancels.\nClick an image: copy. Double-click: open.\nDrag out: copy as a file. Shift-drag to a folder: move.\nLong-press an image: edit in Paint. Right-click: more actions.\n\nCaptures made with the capture shortcut are added automatically. Other screenshots and copied images are not.\nUp to 60 images are kept; the oldest saved copies are cleared beyond that.\nRuns entirely on this PC: no network, no uploads, no changes to system screenshot settings.", "Snapline", Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Information);
            }
            finally { dialogOpen = false; }
        }
    }
}
