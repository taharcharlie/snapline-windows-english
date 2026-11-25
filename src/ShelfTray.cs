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
            // While a removal can still be undone, the menu offers it and says what comes back.
            var undoMenu = new Forms.ToolStripMenuItem("Undo remove", null, delegate { Reveal(); Undo(); keepUntil = DateTime.Now.AddSeconds(3); }) { Visible = false };
            menu.Items.Add(undoMenu);
            menu.Opening += delegate { undoMenu.Visible = undoItem != null; if (undoItem != null) undoMenu.Text = "Undo remove (" + UndoDescription() + ")"; };
            noteItem = (Forms.ToolStripMenuItem)menu.Items.Add("New note", null, delegate { NewNote(); });
            hangItem = (Forms.ToolStripMenuItem)menu.Items.Add("Hang the clipboard", null, delegate { HangClipboard(); });
            UpdateLineShortcutHints();
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
            var triggerMenu = new Forms.ToolStripMenuItem("Touch line length");
            var lengths = new[] { new { Name = "Short", Value = 0 }, new { Name = "Medium", Value = 1 }, new { Name = "Long (as wide as the bar's date and clock)", Value = 2 } };
            foreach (var choice in lengths)
            {
                int value = choice.Value;
                triggerMenu.DropDownItems.Add(new Forms.ToolStripMenuItem(choice.Name, null, delegate
                {
                    store.Settings.TriggerLength = value; store.SaveSettings();
                    ApplyTriggerLength(); if (!expanded && !collapsing) Render();
                }) { Tag = value });
            }
            triggerMenu.DropDownOpening += delegate { foreach (Forms.ToolStripMenuItem item in triggerMenu.DropDownItems) item.Checked = (int)item.Tag == store.Settings.TriggerLength; };
            menu.Items.Add(triggerMenu);
            var themeMenu = new Forms.ToolStripMenuItem("Theme");
            for (int i = 0; i < Theme.Names.Length; i++)
            {
                string name = Theme.Names[i];
                themeMenu.DropDownItems.Add(new Forms.ToolStripMenuItem(name + "  (" + Theme.Hints[i] + ")", null, delegate { ChooseTheme(name); }) { Tag = name, Checked = name == Theme.Name });
            }
            menu.Items.Add(themeMenu);
            var fontMenu = new Forms.ToolStripMenuItem("Font");
            foreach (string each in Theme.Fonts)
            {
                string name = each; bool installed = Theme.FontInstalled(name);
                fontMenu.DropDownItems.Add(new Forms.ToolStripMenuItem(installed ? name : name + "  (not installed)", null, delegate { ChooseFont(name); }) { Checked = name == Theme.FontBase, Enabled = installed || name == Theme.FontBase });
            }
            menu.Items.Add(fontMenu);
            var cardSizeMenu = new Forms.ToolStripMenuItem("Card size");
            for (int i = 0; i < CardSizes.Names.Length; i++)
            {
                int value = i;
                cardSizeMenu.DropDownItems.Add(new Forms.ToolStripMenuItem(CardSizes.Names[i] + "  (" + CardSizes.Hints[i] + ")", null, delegate { ChooseCardSize(value); }) { Tag = value });
            }
            cardSizeMenu.DropDownOpening += delegate { foreach (Forms.ToolStripMenuItem item in cardSizeMenu.DropDownItems) item.Checked = (int)item.Tag == store.Settings.CardSize; };
            menu.Items.Add(cardSizeMenu);
            var clearMenu = new Forms.ToolStripMenuItem("Auto-clear old cards");
            var ages = new[] { new { Name = "Never", Value = 0 }, new { Name = "After 1 day", Value = 1 }, new { Name = "After 7 days", Value = 7 }, new { Name = "After 30 days", Value = 30 } };
            foreach (var choice in ages)
            {
                int value = choice.Value;
                clearMenu.DropDownItems.Add(new Forms.ToolStripMenuItem(choice.Name, null, delegate { SetAutoClear(value); }) { Tag = value });
            }
            clearMenu.DropDownOpening += delegate { foreach (Forms.ToolStripMenuItem item in clearMenu.DropDownItems) item.Checked = (int)item.Tag == store.Settings.AutoClearDays; };
            menu.Items.Add(clearMenu);
            pinMenu = new Forms.ToolStripMenuItem("Keep visible", null, delegate { TogglePin(); });
            menu.Items.Add(pinMenu);
            menu.Opening += delegate { pinMenu.Checked = pinned; };
            menu.Items.Add("Roll left", null, delegate { Reveal(); Roll(-1); });
            menu.Items.Add("Roll right", null, delegate { Reveal(); Roll(1); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(new Forms.ToolStripMenuItem("Only shortcut captures are collected") { Enabled = false });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Open storage folder", null, delegate { Launch(store.Inbox); });
            menu.Items.Add("Clear screenshots…", null, delegate
            {
                dialogOpen = true;
                try
                {
                    if (Forms.MessageBox.Show("Clear the " + store.Items.Count(x => !x.Kept) + " saved copies? Kept cards stay. Original screenshots are not deleted.", "Snapline", Forms.MessageBoxButtons.YesNo, Forms.MessageBoxIcon.Question) == Forms.DialogResult.Yes) store.Clear();
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
            menu.Items.Add("Preferences…", null, delegate { ShowPreferences(); });
            menu.Items.Add("Help", null, delegate { About(); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Quit", null, delegate { Close(); });
            menu.Items.Add("Close menu", null, delegate { }); // A visible way out, besides clicking elsewhere.
            DressTrayMenu(menu);
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { ToggleFromTray(); };
            tray.Visible = true;
        }
        static Drawing.Color TrayColour(string hex) { return Drawing.ColorTranslator.FromHtml(hex); }
        sealed class TrayColours : Forms.ProfessionalColorTable
        {
            public override Drawing.Color ToolStripDropDownBackground { get { return TrayColour(Theme.Panel); } }
            public override Drawing.Color ImageMarginGradientBegin { get { return TrayColour(Theme.Panel); } }
            public override Drawing.Color ImageMarginGradientMiddle { get { return TrayColour(Theme.Panel); } }
            public override Drawing.Color ImageMarginGradientEnd { get { return TrayColour(Theme.Panel); } }
            public override Drawing.Color MenuBorder { get { return TrayColour(Theme.Rule); } }
            public override Drawing.Color MenuItemBorder { get { return TrayColour(Theme.Inset); } }
            public override Drawing.Color MenuItemSelected { get { return TrayColour(Theme.Inset); } }
            public override Drawing.Color MenuItemSelectedGradientBegin { get { return TrayColour(Theme.Inset); } }
            public override Drawing.Color MenuItemSelectedGradientEnd { get { return TrayColour(Theme.Inset); } }
            public override Drawing.Color MenuItemPressedGradientBegin { get { return TrayColour(Theme.Inset); } }
            public override Drawing.Color MenuItemPressedGradientEnd { get { return TrayColour(Theme.Inset); } }
            public override Drawing.Color SeparatorDark { get { return TrayColour(Theme.Rule); } }
            public override Drawing.Color SeparatorLight { get { return TrayColour(Theme.Rule); } }
        }
        sealed class TrayRenderer : Forms.ToolStripProfessionalRenderer
        {
            internal TrayRenderer() : base(new TrayColours()) { RoundedEdges = false; }
            protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = TrayColour(!e.Item.Enabled ? Theme.Dim : e.Item.Selected ? Theme.White : Theme.Light);
                base.OnRenderItemText(e);
            }
            protected override void OnRenderArrow(Forms.ToolStripArrowRenderEventArgs e) { e.ArrowColor = TrayColour(Theme.Mid); base.OnRenderArrow(e); }
            // A small accent square marks a ticked item.
            protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
            {
                var box = e.ImageRectangle;
                using (var brush = new Drawing.SolidBrush(TrayColour(Theme.Accent)))
                    e.Graphics.FillRectangle(brush, box.X + box.Width / 2 - 4, box.Y + box.Height / 2 - 4, 8, 8);
            }
        }
        void DressTrayMenu(Forms.ContextMenuStrip menu)
        {
            try
            {
                var renderer = new TrayRenderer();
                var font = new Drawing.Font(Theme.FontBase, (float)(9.5 * Theme.FontScale));
                if (font.Name != Theme.FontBase) { font.Dispose(); font = new Drawing.Font("Segoe UI", 9.5f); }
                Action<Forms.ToolStripDropDown> dress = null;
                dress = delegate(Forms.ToolStripDropDown strip)
                {
                    strip.Renderer = renderer; strip.Font = font; strip.BackColor = TrayColour(Theme.Panel); strip.ForeColor = TrayColour(Theme.Light);
                    foreach (var item in strip.Items.OfType<Forms.ToolStripMenuItem>()) if (item.HasDropDownItems) dress(item.DropDown);
                };
                dress(menu);
            }
            catch (Exception ex) { store.Log("Tray menu style", ex); } // The plain menu still works.
        }
        // Colours and fonts are fixed when the windows are built, so a new theme or font is applied by starting Snapline again.
        internal void ChooseTheme(string name)
        {
            if (name == Theme.Name) return;
            string before = store.Settings.ThemeName;
            store.Settings.ThemeName = name;
            try { store.SaveSettings(); } catch (Exception ex) { store.Settings.ThemeName = before; store.Log("Theme setting", ex); Notify("Could not save that setting."); return; }
            RestartToApply("--theme", "the " + name + " theme");
        }
        internal void ChooseFont(string name)
        {
            if (name == Theme.FontBase) return;
            string before = store.Settings.FontName;
            store.Settings.FontName = name;
            try { store.SaveSettings(); } catch (Exception ex) { store.Settings.FontName = before; store.Log("Font setting", ex); Notify("Could not save that setting."); return; }
            RestartToApply("--font", name);
        }
        Forms.ToolStripMenuItem noteItem, hangItem;
        // The tray items name their shortcuts, which can change in Preferences.
        void UpdateLineShortcutHints()
        {
            if (noteItem != null) noteItem.ToolTipText = LineShortcut.Note.IsOff(store.Settings) ? null : LineShortcutLabel(LineShortcut.Note);
            if (hangItem != null) hangItem.ToolTipText = LineShortcut.Hang.IsOff(store.Settings) ? null : LineShortcutLabel(LineShortcut.Hang);
        }
        PreferencesWindow preferences;
        internal PreferencesWindow Preferences { get { return preferences; } }
        // One Preferences window at a time; asking again brings it forward.
        internal void ShowPreferences()
        {
            if (preferences != null) { preferences.Activate(); return; }
            preferences = new PreferencesWindow(this, store) { Owner = this };
            preferences.Closed += delegate { preferences = null; lastInside = DateTime.Now; };
            preferences.Show(); preferences.Activate();
        }
        // For Preferences: makes a change to the settings, saves them, and lets the line catch up.
        // With showLine the line opens to show the change, as the tray's Line tension does.
        internal void ChangeSetting(Action change, bool showLine)
        {
            change();
            try { store.SaveSettings(); } catch (Exception ex) { store.Log("Preferences", ex); Notify("Could not save that setting."); }
            if (showLine)
            {
                // The open shelf's height depends on the tension, so it is fitted again first.
                if (expanded && !collapsing) Position(true, screen);
                Reveal(); Render(true); keepUntil = DateTime.Now.AddSeconds(3);
            }
            else if (expanded && !collapsing) Render(true);
        }
        internal void TriggerLengthChanged() { ApplyTriggerLength(); if (!expanded && !collapsing) Render(); }
        internal void SetStartAtLogin(bool on)
        {
            string error;
            if (Startup != null && !Startup.Apply(on, out error)) Notify(error);
            if (startupMenu != null) startupMenu.Checked = Startup != null && store.Settings.StartupEnabled;
        }
        // Lowering a limit below what is hung asks first, then takes the oldest unkept cards off at once.
        internal void SetLimits(int shots, int links)
        {
            int over = store.OverLimit(shots, links);
            if (over > 0)
            {
                dialogOpen = true;
                try
                {
                    var answer = Forms.MessageBox.Show("This takes the " + (over == 1 ? "oldest card" : over + " oldest cards") + " that are not kept off the line now. Their screenshot copies are deleted; " +
                        "kept cards (yellow peg) stay, and files behind reference cards are never touched.\n\nGo ahead?",
                        "Snapline", Forms.MessageBoxButtons.OKCancel, Forms.MessageBoxIcon.Warning, Forms.MessageBoxDefaultButton.Button2);
                    if (answer != Forms.DialogResult.OK) return;
                }
                finally { dialogOpen = false; }
            }
            int oldShots = store.Settings.ShotLimit, oldLinks = store.Settings.LinkLimit;
            store.Settings.ShotLimit = shots; store.Settings.LinkLimit = links;
            try { store.SaveSettings(); }
            catch (Exception ex) { store.Settings.ShotLimit = oldShots; store.Settings.LinkLimit = oldLinks; store.Log("Limit setting", ex); Notify("Could not save that setting."); return; }
            if (over > 0)
            {
                try { store.ApplyLimits(); }
                catch (Exception ex) { store.Log("Apply limits", ex); Notify("Saved. The extra cards go the next time one is hung."); }
            }
        }
        // Unlike the theme and font, a card size applies at once: the open line grows or shrinks to fit and hangs the cards again.
        internal void ChooseCardSize(int value)
        {
            value = CardSizes.Normalize(value);
            if (value == store.Settings.CardSize) return;
            int before = store.Settings.CardSize;
            store.Settings.CardSize = value;
            try { store.SaveSettings(); } catch (Exception ex) { store.Settings.CardSize = before; store.Log("Card size setting", ex); Notify("Could not save that setting."); return; }
            if (expanded && !collapsing) Position(true, screen);
            Reveal(); Render(true); keepUntil = DateTime.Now.AddSeconds(3);
        }
        void RestartToApply(string forcedOption, string what)
        {
            try
            {
                var arguments = Environment.GetCommandLineArgs().Skip(1).Where(x => x != "--restarted" && x != "--startup").ToList();
                int forced = arguments.IndexOf(forcedOption);
                if (forced >= 0) arguments.RemoveRange(forced, Math.Min(2, arguments.Count - forced));
                arguments.Add("--restarted");
                Process.Start(new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,
                    String.Join(" ", arguments.Select(x => "\"" + x.TrimEnd('\\') + "\""))) { UseShellExecute = false });
            }
            catch (Exception ex) { store.Log("Restart", ex); Notify("Saved. Quit and start Snapline again to see " + what + "."); return; }
            Close();
        }
        internal void SetAutoClear(int days)
        {
            int due = store.Expired(days).Count;
            if (due > 0)
            {
                dialogOpen = true;
                try
                {
                    var answer = Forms.MessageBox.Show(due + (due == 1 ? " card is" : " cards are") + " already older than that and will be removed the next time the line is closed.\n\nKept cards (yellow peg) stay. Files behind reference cards are never touched.\n\nTurn this on?",
                        "Auto-clear old cards", Forms.MessageBoxButtons.OKCancel, Forms.MessageBoxIcon.Warning, Forms.MessageBoxDefaultButton.Button2);
                    if (answer != Forms.DialogResult.OK) return;
                }
                finally { dialogOpen = false; }
            }
            int before = store.Settings.AutoClearDays;
            store.Settings.AutoClearDays = days;
            try { store.SaveSettings(); } catch (Exception ex) { store.Settings.AutoClearDays = before; store.Log("Auto-clear setting", ex); Notify("Could not save that setting."); return; }
            nextExpiry = DateTime.Now.AddSeconds(20);
        }
        void About()
        {
            dialogOpen = true;
            try
            {
                Forms.MessageBox.Show("Snapline, Ashes edition (based on 0.5.8)\n\n" +
                    "OPEN\nTouch the line at the top centre, or press " + LineShortcutLabel(LineShortcut.Open) + " (stays open; arrows roll, Enter or 1 to 9 copies, K keeps, Delete removes, Ctrl + Z undoes, Esc closes; a strip under the line lists these keys).\nScroll or swipe: the line glides; a notch moves it one card.\nDouble-click the tray icon: open until you leave.\n\n" +
                    "HANG\n" + CaptureShortcutLabel + ": capture a region and hang it (when the shortcut is available).\n" + LineShortcutLabel(LineShortcut.Hang) + ": hang what is on the clipboard.\n" + LineShortcutLabel(LineShortcut.Note) + ": start a new note.\nDrop a file, folder, link or text on a card. Files and folders hang as references: the original is never moved or deleted.\n'+ Note' on the Tips card: a quick note. Start a note with a line saying math, sum or avg to have it worked out, units too (5 km in miles); = or Tab at the end of a line writes the result in.\n\n" +
                    "NOTES\nIn the editor: B, I, U (Ctrl + B / I / U) mark the selection **bold**, *italic* or __underlined__. To-do (Ctrl + T) turns lines into [ ] boxes; Tick (Ctrl + D) ticks them.\nOn the line, click a box to tick it. On a note with a picture, clicking the picture copies it and clicking elsewhere copies the text; resting on the picture previews it alone; right-click has Open picture.\n\n" +
                    "USE\nClick a card: copy. Double-click: open or edit.\nDrag along the line: reorder. Pull a card down: drop it into an app, or on the Tips card to delete.\nClick a peg: delete, with Undo for a few seconds.\nRight-click: more, including Keep (yellow peg: never cleared).\n'Add caption' under an image: note what it is for.\n\n" +
                    "TRAY\nTheme and Font (Snapline restarts to apply one), Card size (Small, Medium or Large), Tips card (Full or Compact), Animation (Basic or Fun, in Preferences), Line tension, Touch line length, Auto-clear old cards (off unless you turn it on), Start at login, capture shortcut. Preferences… has all of these in one window, with the other shortcuts, with timings, card sway and how many cards are held.\n\n" +
                    "Win + Shift + S and other screenshot tools are not collected. Up to 60 screenshots and 60 reference cards are held; the oldest unkept ones make room.\nRuns entirely on this PC: no network, no uploads.", "Snapline", Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Information);
            }
            finally { dialogOpen = false; }
        }
    }
}
