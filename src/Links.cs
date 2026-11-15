// Snapline: a local screenshot shelf for Windows. MIT License.
// Reference cards: a file, a folder or a web address hung on the line. The line stores where the thing is,
// never a copy, and nothing here ever moves, renames or deletes the original.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace Snapline
{
    [DataContract] sealed class Link
    {
        [DataMember] public string Id;
        [DataMember] public string Target;   // Full path, or an http(s) address.
        [DataMember] public string Kind;     // "File", "Folder" or "Url".
        [DataMember] public string Title;
        [DataMember] public DateTime Created;
        [DataMember] public double Rank;
        [DataMember] public bool Kept;
        // Not saved: what the last look at the disk found.
        internal bool Missing;
        internal DateTime Checked;
        internal bool IsUrl { get { return Kind == "Url"; } }
    }

    static class LinkNative
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct FileInfo
        {
            internal IntPtr Icon; internal int IconIndex; internal uint Attributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string DisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] internal string TypeName;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr SHGetFileInfo(string path, uint attributes, ref FileInfo info, uint size, uint flags);
        [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);
    }

    sealed partial class Store
    {
        internal readonly List<Link> Links = new List<Link>();
        internal const int MaxLinks = 60;
        bool linksUnreadable;
        string LinksFile { get { return System.IO.Path.Combine(Root, "links.json"); } }
        internal static bool WebAddress(string text, out Uri address)
        {
            address = null;
            if (String.IsNullOrWhiteSpace(text) || text.Length > 2000 || text.IndexOfAny(new[] { '\r', '\n', ' ' }) >= 0) return false;
            return Uri.TryCreate(text, UriKind.Absolute, out address) && (address.Scheme == Uri.UriSchemeHttp || address.Scheme == Uri.UriSchemeHttps);
        }
        // A file or folder card must hold a complete path: a drive or share, no quotes, nothing relative.
        internal static bool FullLocalPath(string target)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(target) || target.Length > 1000 || target.IndexOf('"') >= 0) return false;
                if (target.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0 || !System.IO.Path.IsPathRooted(target)) return false;
                bool drive = target.Length >= 3 && Char.IsLetter(target[0]) && target[1] == ':' && (target[2] == '\\' || target[2] == '/');
                return drive || target.StartsWith(@"\\", StringComparison.Ordinal);
            }
            catch { return false; }
        }
        // Unkept cards hung more than the given number of days ago. A card with no date is left alone.
        internal List<object> Expired(int days)
        {
            var old = new List<object>();
            if (days <= 0) return old;
            DateTime cutoff = DateTime.Now.AddDays(-days);
            old.AddRange(Items.Where(x => !x.Kept && x.Created.Year > 2000 && x.Created < cutoff).Cast<object>());
            old.AddRange(Notes.Where(x => !x.Kept && x.Created.Year > 2000 && x.Created < cutoff).Cast<object>());
            old.AddRange(Links.Where(x => !x.Kept && x.Created.Year > 2000 && x.Created < cutoff).Cast<object>());
            return old;
        }
        internal int Expire(int days)
        {
            int count = 0;
            foreach (object item in Expired(days))
            {
                try
                {
                    var shot = item as Shot; var memo = item as Memo; var link = item as Link;
                    // For a reference card only the card goes; the file it points at is not touched.
                    if (shot != null) Remove(shot); else if (memo != null) RemoveNote(memo); else if (link != null) RemoveLink(link);
                    count++;
                }
                catch (Exception ex) { Log("Auto-clear card", ex); }
            }
            return count;
        }
        void RestoreLinks()
        {
            if (!File.Exists(LinksFile)) return;
            try
            {
                if (new System.IO.FileInfo(LinksFile).Length > 8L * 1024 * 1024) throw new InvalidDataException("The references file is too large.");
                List<Link> saved;
                using (var stream = File.OpenRead(LinksFile)) saved = (List<Link>)new DataContractJsonSerializer(typeof(List<Link>)).ReadObject(stream);
                if (saved == null) throw new InvalidDataException("The references file is not in a valid format.");
                foreach (var link in saved)
                {
                    if (link == null || String.IsNullOrWhiteSpace(link.Target)) continue;
                    if (link.Kind != "File" && link.Kind != "Folder" && link.Kind != "Url") continue;
                    Uri address;
                    if (link.Kind == "Url" && !WebAddress(link.Target, out address)) continue;
                    if (link.Kind != "Url" && !FullLocalPath(link.Target)) continue;
                    Guid id;
                    if (!Guid.TryParseExact(link.Id, "N", out id)) link.Id = Guid.NewGuid().ToString("N");
                    if (Links.Any(x => x.Id == link.Id)) continue;
                    if (link.Title == null) link.Title = "";
                    if (link.Created == DateTime.MinValue) link.Created = DateTime.Now;
                    Links.Add(link);
                }
            }
            catch (Exception ex) { Links.Clear(); linksUnreadable = true; Log("Restore references", ex); }
        }
        void SaveLinks()
        {
            if (linksUnreadable) throw new InvalidOperationException("The references file could not be read, so it was left untouched. Restore links.json and restart.");
            Write(LinksFile, Links);
        }
        // Hangs a reference. Hanging the same target again returns the card that is already there.
        internal Link AddLink(string target, string kind, string title, out bool existed)
        {
            var same = Links.FirstOrDefault(x => x.Kind == kind && String.Equals(x.Target, target, kind == "Url" ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));
            existed = same != null;
            if (existed) return same;
            var link = new Link { Id = Guid.NewGuid().ToString("N"), Target = target, Kind = kind, Title = title ?? "", Created = DateTime.Now, Rank = NextRank() };
            var before = Links.ToList();
            Links.Insert(0, link);
            // Only cards leave the list here. The files they point at are never touched.
            var overflow = Links.Where(x => !x.Kept).Skip(MaxLinks).ToList();
            foreach (var old in overflow) Links.Remove(old);
            try { SaveLinks(); } catch { Links.Clear(); Links.AddRange(before); throw; }
            if (Changed != null) Changed();
            return link;
        }
        internal void RemoveLink(Link link)
        {
            int index = Links.IndexOf(link); if (index < 0) return;
            Links.RemoveAt(index);
            try { SaveLinks(); } catch { Links.Insert(index, link); throw; }
            if (Changed != null) Changed();
        }
        internal void RestoreLink(Link link, int index)
        {
            if (Links.Contains(link)) return;
            // The same thing was hung again in the meantime: that card stands in for the restored one.
            if (Links.Any(x => x.Kind == link.Kind && String.Equals(x.Target, link.Target, StringComparison.OrdinalIgnoreCase))) return;
            index = Math.Max(0, Math.Min(Links.Count, index));
            Links.Insert(index, link);
            try { SaveLinks(); } catch { Links.Remove(link); throw; }
            if (Changed != null) Changed();
        }
        internal void Retarget(Link link, string target)
        {
            if (!FullLocalPath(target)) throw new InvalidOperationException("that is not a full path.");
            if (Links.Any(x => !Object.ReferenceEquals(x, link) && x.Kind == link.Kind && String.Equals(x.Target, target, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("another card already points there.");
            string old = link.Target;
            link.Target = target;
            try { SaveLinks(); } catch { link.Target = old; throw; }
            link.Missing = false; link.Checked = DateTime.MinValue;
            if (Changed != null) Changed();
        }
        internal static double RankOf(object item)
        {
            var shot = item as Shot; if (shot != null) return shot.Rank;
            var memo = item as Memo; if (memo != null) return memo.Rank;
            var link = item as Link; return link != null ? link.Rank : 0;
        }
        internal static DateTime CreatedOf(object item)
        {
            var shot = item as Shot; if (shot != null) return shot.Created;
            var memo = item as Memo; if (memo != null) return memo.Created;
            var link = item as Link; return link != null ? link.Created : DateTime.MinValue;
        }
    }

    sealed partial class Shelf
    {
        // ---- what a click and a double-click do, for every kind of card ----
        void Activate(object item)
        {
            var shot = item as Shot; var memo = item as Memo; var link = item as Link;
            if (shot != null) Copy(shot); else if (memo != null) CopyNote(memo); else if (link != null) CopyLink(link);
        }
        void OpenItem(object item)
        {
            var shot = item as Shot; var memo = item as Memo; var link = item as Link;
            if (shot != null) Launch(store.FullPath(shot)); else if (memo != null) EditNote(memo, false); else if (link != null) Launch(link.Target);
        }
        // A short word over the card itself, so the answer to a click appears where the click was.
        internal void Flash(object item, string text)
        {
            var card = HangingElement(item) as Panel;
            if (card == null || !expanded) { Notify(text); return; }
            var veil = new Border
            {
                Background = Brush(Theme.Alpha("D9", Theme.Bg)), IsHitTestVisible = false, Tag = "CardFlash",
                Child = new TextBlock { Text = text, FontSize = Theme.Size(15), FontWeight = FontWeights.SemiBold, Foreground = Brush(Theme.Accent),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            card.Children.Add(veil);
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(450)) { BeginTime = TimeSpan.FromMilliseconds(600) };
            fade.Completed += delegate { card.Children.Remove(veil); };
            veil.BeginAnimation(UIElement.OpacityProperty, fade);
            DateTime stay = DateTime.Now.AddMilliseconds(1300);
            if (keepUntil < stay) keepUntil = stay;
            lastInside = DateTime.Now;
        }
        void CopyLink(Link link)
        {
            try
            {
                if (link.IsUrl) { SetClipboard(new DataObject(DataFormats.UnicodeText, link.Target)); Flash(link, "Address copied"); return; }
                // No look at the disk here: a slow network or cloud path must not freeze a click.
                // The background check below corrects the card either way.
                link.Checked = DateTime.MinValue; CheckLink(link);
                if (link.Missing) { Flash(link, "Missing"); return; }
                SetClipboard(new DataObject(DataFormats.FileDrop, new[] { link.Target }));
                Flash(link, "Copied");
            }
            catch (Exception ex) { store.Log("Copy reference", ex); Notify("Copy failed. Try again shortly."); }
        }
        bool linksStale, hangBatch;
        void MarkMissing(Link link, bool missing)
        {
            link.Checked = DateTime.Now;
            if (link.Missing == missing) return;
            link.Missing = missing;
            linksStale = true; // Redrawn from Tick at a quiet moment.
        }
        // Looks at the disk away from the interface, so a slow network or cloud drive cannot freeze the line.
        void CheckLink(Link link)
        {
            if (link.IsUrl || (DateTime.Now - link.Checked).TotalSeconds < 20) return;
            link.Checked = DateTime.Now;
            string target = link.Target; bool folder = link.Kind == "Folder";
            Task.Run(delegate { try { return folder ? Directory.Exists(target) : File.Exists(target); } catch { return false; } })
                .ContinueWith(delegate(Task<bool> look)
                {
                    bool missing = !(look.Status == TaskStatus.RanToCompletion && look.Result);
                    Dispatcher.BeginInvoke(new Action(delegate { if (!shuttingDown && link.Target == target && store.Links.Contains(link)) MarkMissing(link, missing); }));
                });
        }
        static ImageSource IconFor(Link link)
        {
            if (link.IsUrl) return null;
            try
            {
                var info = new LinkNative.FileInfo();
                // 0x100 icon, 0x10 "use the attributes I pass": Windows answers from the file type alone and does not open the file.
                LinkNative.SHGetFileInfo(link.Target, link.Kind == "Folder" ? 0x10u : 0x80u, ref info, (uint)Marshal.SizeOf(typeof(LinkNative.FileInfo)), 0x100 | 0x10);
                if (info.Icon == IntPtr.Zero) return null;
                try { var picture = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); picture.Freeze(); return picture; }
                finally { LinkNative.DestroyIcon(info.Icon); }
            }
            catch { return null; }
        }
        static string LinkName(Link link)
        {
            if (!String.IsNullOrWhiteSpace(link.Title)) return link.Title;
            if (link.IsUrl) return link.Target;
            string name = System.IO.Path.GetFileName(link.Target.TrimEnd('\\', '/'));
            return String.IsNullOrEmpty(name) ? link.Target : name;
        }
        static string LinkPlace(Link link)
        {
            try
            {
                if (link.IsUrl) return new Uri(link.Target).Host;
                string parent = System.IO.Path.GetDirectoryName(link.Target.TrimEnd('\\', '/'));
                return String.IsNullOrEmpty(parent) ? link.Target : parent;
            }
            catch { return link.Target; }
        }
        ContextMenu LinkMenu(Link link)
        {
            var menu = new ContextMenu();
            menu.Items.Add(Item("Open", delegate { Launch(link.Target); }));
            if (!link.IsUrl) menu.Items.Add(Item("Show in folder", delegate { try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + link.Target + "\""); } catch (Exception ex) { store.Log("Show reference", ex); } }));
            menu.Items.Add(Item(link.IsUrl ? "Copy address" : "Copy path", delegate
            {
                try { SetClipboard(new DataObject(DataFormats.UnicodeText, link.Target)); Flash(link, link.IsUrl ? "Address copied" : "Path copied"); }
                catch (Exception ex) { store.Log("Copy path", ex); Notify("Copy failed. Try again shortly."); }
            }));
            if (!link.IsUrl && link.Missing) menu.Items.Add(Item("Locate…", delegate { Locate(link); }));
            menu.Items.Add(Item(link.Kept ? "Stop keeping" : "Keep on the line", delegate { ToggleKeep(link); }));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item(link.IsUrl ? "Unhang" : "Unhang (the original stays where it is)", delegate { RemoveWithUndo(link); }));
            menu.Opened += delegate { dialogOpen = true; };
            menu.Closed += delegate { dialogOpen = false; lastInside = DateTime.Now; };
            return menu;
        }
        void Locate(Link link)
        {
            dialogOpen = true;
            try
            {
                string found = null;
                if (link.Kind == "Folder")
                {
                    using (var picker = new Forms.FolderBrowserDialog { Description = "Where is \"" + LinkName(link) + "\" now?" })
                        if (picker.ShowDialog() == Forms.DialogResult.OK) found = picker.SelectedPath;
                }
                else
                {
                    using (var picker = new Forms.OpenFileDialog { Title = "Where is \"" + LinkName(link) + "\" now?", FileName = System.IO.Path.GetFileName(link.Target) })
                        if (picker.ShowDialog() == Forms.DialogResult.OK) found = picker.FileName;
                }
                if (found != null) { link.Title = ""; store.Retarget(link, found); }
            }
            catch (Exception ex) { store.Log("Locate reference", ex); Notify("Could not save that: " + ex.Message); }
            finally { dialogOpen = false; keepUntil = DateTime.Now.AddSeconds(3); lastInside = DateTime.Now; }
        }
        void MakeLinkCard(Link link, double center, double y, int index)
        {
            const double width = 182, height = 124;
            double lean = Lean(new[] { 1.8, -1.5, 1.0, -2.1 }[index % 4]);
            var swing = new RotateTransform(lean, width / 2, -15);
            var grid = new Grid { Width = width, Height = height, Cursor = Cursors.Hand, RenderTransform = swing, Tag = link };
            string name = LinkName(link), kind = link.IsUrl ? "Link" : link.Kind;
            System.Windows.Automation.AutomationProperties.SetName(grid, kind + " " + (name.Length > 40 ? name.Substring(0, 40) : name));
            System.Windows.Automation.AutomationProperties.SetAutomationId(grid, "LinkCard-" + link.Id);
            grid.Children.Add(new Border { Background = Brush(Theme.Panel), BorderBrush = Brush(Theme.Rule), BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { Color = MemoTheme.Hex(Theme.Shadow), Opacity = 0.16, ShadowDepth = 4, BlurRadius = 9 } });
            grid.Children.Add(new Border { Height = 4, VerticalAlignment = VerticalAlignment.Top, Background = Brush(Theme.Text), IsHitTestVisible = false });
            var body = new Grid { Margin = new Thickness(14, 14, 14, 10), IsHitTestVisible = false, Opacity = link.Missing ? 0.5 : 1 };
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var header = new Grid();
            header.Children.Add(new TextBlock { Text = kind, FontSize = Theme.Size(12), Foreground = Brush(Theme.Mid) });
            // The chain glyph marks every reference card: the thing itself lives somewhere else.
            header.Children.Add(new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 13, Foreground = Brush(Theme.Dim), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
            body.Children.Add(header);
            var middle = new DockPanel { Margin = new Thickness(0, 8, 0, 4), LastChildFill = true };
            var icon = IconFor(link);
            FrameworkElement mark = icon != null
                ? (FrameworkElement)new Image { Source = icon, Width = 30, Height = 30, VerticalAlignment = VerticalAlignment.Top }
                : new TextBlock { Text = link.IsUrl ? "" : "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 26, Foreground = Brush(Theme.Light), VerticalAlignment = VerticalAlignment.Top };
            mark.Margin = new Thickness(0, 2, 10, 0); DockPanel.SetDock(mark, Dock.Left); middle.Children.Add(mark);
            middle.Children.Add(new TextBlock { Text = name, FontSize = Theme.Size(13), Foreground = Brush(Theme.Light), TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis, LineHeight = 19, MaxHeight = 40, VerticalAlignment = VerticalAlignment.Top });
            Grid.SetRow(middle, 1); body.Children.Add(middle);
            var footer = new TextBlock { Text = link.Missing ? "Missing" : LinkPlace(link), FontSize = Theme.Size(11), Foreground = Brush(link.Missing ? Theme.Warn : Theme.Dim), TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetRow(footer, 2); body.Children.Add(footer);
            grid.Children.Add(body);
            AddHangingPeg(grid);
            grid.ContextMenu = LinkMenu(link);
            grid.ToolTip = (link.Missing ? "Missing: " : "") + link.Target;
            ToolTipService.SetInitialShowDelay(grid, 500);
            var breeze = new DoubleAnimation(lean - 0.45, lean + 0.45, TimeSpan.FromSeconds(4.1 + (index % 3) * 0.3))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            Timeline.SetDesiredFrameRate(breeze, 24); RegisterHangingMotion(grid, swing, breeze);
            grid.MouseEnter += delegate { if (!hoverEffectsPaused) SetHangingAnimation(swing, new DoubleAnimation(0, TimeSpan.FromMilliseconds(180))); };
            grid.MouseLeave += delegate { if (!hoverEffectsPaused) SetHangingAnimation(swing, breeze); };
            Place(grid, center - width / 2, y);
            CheckLink(link);
        }

        // ---- things dropped on the line ----
        static bool CanHang(IDataObject data)
        {
            return data.GetDataPresent(DataFormats.FileDrop) || data.GetDataPresent("UniformResourceLocatorW") ||
                data.GetDataPresent("UniformResourceLocator") || data.GetDataPresent(DataFormats.UnicodeText);
        }
        internal void HangClipboard()
        {
            if (dialogOpen || dragging || reorderSlots != null || memoEditor != null || remarkEditor != null || editingShortcut) return;
            try
            {
                IDataObject data = null;
                // Another program may hold the clipboard for a moment.
                for (int i = 0; i < 4 && data == null; i++)
                {
                    try { data = Clipboard.GetDataObject(); }
                    catch (System.Runtime.InteropServices.COMException) { System.Threading.Thread.Sleep(40); }
                }
                Reveal(); keepUntil = DateTime.Now.AddSeconds(4);
                if (data == null) { Notify("The clipboard is busy. Try again."); return; }
                if (data.GetDataPresent(DataFormats.FileDrop)) { HangDropped(data); return; }
                if (Clipboard.ContainsImage())
                {
                    var image = Clipboard.GetImage();
                    if (image != null)
                    {
                        var opaque = new FormatConvertedBitmap(image, PixelFormats.Bgr32, null, 0); opaque.Freeze();
                        var shot = store.Add(opaque, "Clipboard");
                        if (shot == null) Notify("That image is already on the line."); else Flash(shot, "Hung");
                        return;
                    }
                }
                if (DroppedAddress(data) != null) { HangDropped(data); return; }
                string text = data.GetData(DataFormats.UnicodeText) as string;
                if (String.IsNullOrWhiteSpace(text)) { Notify("Nothing on the clipboard to hang."); return; }
                text = text.Trim();
                if (text.Length > Store.MaxNoteText) text = text.Substring(0, Store.MaxNoteText);
                // A note that was just copied from the line is not hung a second time.
                var same = store.Notes.FirstOrDefault(x => String.Equals((x.Text ?? "").Trim(), text, StringComparison.Ordinal));
                if (same != null)
                {
                    int at = Deck().FindIndex(x => Object.ReferenceEquals(x, same));
                    if (at >= 0) { focus = at; Render(true); }
                    Flash(same, "Already here"); return;
                }
                HangDropped(data);
            }
            catch (Exception ex) { store.Log("Hang clipboard", ex); Notify("Could not hang the clipboard."); }
        }
        void HangDropped(IDataObject data)
        {
            if (data.GetDataPresent(DataFormats.FileDrop))
            {
                var paths = data.GetData(DataFormats.FileDrop) as string[];
                if (paths == null || paths.Length == 0) return;
                if (paths.Length == 1) { HangPath(paths[0]); return; }
                hangBatch = true;
                try { foreach (string path in paths.Take(40)) HangPath(path); }
                finally { hangBatch = false; }
                Reveal(); Render(true);
                Notify(paths.Length > 40 ? "Hung the first 40 of " + paths.Length + " items." : "Hung " + paths.Length + " items.");
                return;
            }
            string address = DroppedAddress(data);
            if (address != null) { HangAddress(address, DroppedTitle(data)); return; }
            string text = data.GetData(DataFormats.UnicodeText) as string;
            if (String.IsNullOrWhiteSpace(text)) return;
            text = text.Trim();
            if (text.Length > Store.MaxNoteText) text = text.Substring(0, Store.MaxNoteText);
            try { var note = store.AddNote(text, "Butter"); Reveal(); Flash(note, "Hung"); }
            catch (Exception ex) { store.Log("Hang text", ex); Notify("Could not hang that: " + ex.Message); }
        }
        // An image is copied onto the line as before; every other file, and any folder, is hung as a reference.
        void HangPath(string path)
        {
            try
            {
                if (!Store.FullLocalPath(path)) return;
                if (Directory.Exists(path)) { HangLink(path, "Folder", ""); return; }
                if (ImageFile(path)) { Import(path); return; }
                HangLink(path, "File", "");
            }
            catch (Exception ex) { store.Log("Hang file", ex); Notify("Could not hang that: " + ex.Message); }
        }
        void HangAddress(string address, string title)
        {
            try { HangLink(address, "Url", title ?? ""); }
            catch (Exception ex) { store.Log("Hang address", ex); Notify("Could not hang that: " + ex.Message); }
        }
        void HangLink(string target, string kind, string title)
        {
            bool existed; int before = store.Links.Count;
            var link = store.AddLink(target, kind, title, out existed);
            if (!existed && store.Links.Count <= before) Notify("The oldest reference card was unhung to stay within " + Store.MaxLinks + ". Its original is untouched.");
            if (hangBatch) return;
            int at = Deck().FindIndex(x => Object.ReferenceEquals(x, link));
            if (at >= 0) focus = at;
            Reveal(); Render(true);
            Flash(link, existed ? "Already here" : "Hung");
        }
        static string ReadText(IDataObject data, string format, Encoding encoding)
        {
            try
            {
                object value = data.GetData(format);
                var text = value as string;
                if (text == null)
                {
                    var stream = value as Stream; if (stream == null) return null;
                    using (var copy = new MemoryStream()) { stream.Position = 0; stream.CopyTo(copy); text = encoding.GetString(copy.ToArray()); }
                }
                int end = text.IndexOf('\0'); if (end >= 0) text = text.Substring(0, end);
                return text;
            }
            catch { return null; }
        }
        static string DroppedAddress(IDataObject data)
        {
            Uri address;
            foreach (string candidate in new[] { ReadText(data, "UniformResourceLocatorW", Encoding.Unicode), ReadText(data, "UniformResourceLocator", Encoding.Default), data.GetData(DataFormats.UnicodeText) as string })
                if (candidate != null && Store.WebAddress(candidate.Trim(), out address)) return address.AbsoluteUri;
            return null;
        }
        // Browsers pass the page title in different ways; when none is found the card shows the address.
        static string DroppedTitle(IDataObject data)
        {
            try
            {
                if (data.GetDataPresent("text/x-moz-url"))
                {
                    var stream = data.GetData("text/x-moz-url") as Stream;
                    if (stream != null)
                        using (var copy = new MemoryStream())
                        {
                            stream.Position = 0; stream.CopyTo(copy);
                            var lines = Encoding.Unicode.GetString(copy.ToArray()).Split('\n');
                            if (lines.Length > 1) { string title = lines[1].Trim('\0', '\r', ' '); if (title.Length > 0) return ClipTitle(title); }
                        }
                }
                if (data.GetDataPresent("FileGroupDescriptorW"))
                {
                    var stream = data.GetData("FileGroupDescriptorW") as Stream;
                    if (stream != null)
                        using (var copy = new MemoryStream())
                        {
                            stream.Position = 0; stream.CopyTo(copy);
                            byte[] bytes = copy.ToArray();
                            const int nameAt = 76; // Past the item count and the fixed fields of the first entry.
                            if (bytes.Length > nameAt + 2)
                            {
                                string file = Encoding.Unicode.GetString(bytes, nameAt, Math.Min(520, bytes.Length - nameAt));
                                int end = file.IndexOf('\0'); if (end >= 0) file = file.Substring(0, end);
                                if (file.EndsWith(".url", StringComparison.OrdinalIgnoreCase)) file = file.Substring(0, file.Length - 4);
                                if (file.Trim().Length > 0) return ClipTitle(file.Trim());
                            }
                        }
                }
            }
            catch { }
            return null;
        }
        static string ClipTitle(string title) { return title.Length > 200 ? title.Substring(0, 200) : title; }
    }
}
