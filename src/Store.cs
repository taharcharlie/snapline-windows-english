// Snapline: a local screenshot shelf for Windows. MIT License.
// Stored items, settings and image files.
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
    [DataContract] sealed class Shot
    {
        [DataMember] public string File;
        [DataMember] public string Digest;
        [DataMember] public string Label;
        [DataMember] public string Remark;
        [DataMember] public DateTime Created;
        // Position on the line (higher is further left) and whether the card is protected from clearing.
        [DataMember] public double Rank;
        [DataMember] public bool Kept;
        internal BitmapSource Thumb;
    }

    [DataContract] sealed class Preferences
    {
        [DataMember] public bool ClipboardImages = false;
        [DataMember] public bool ScreenshotFolder = false;
        [DataMember] public List<string> Folders = new List<string>();
        [DataMember] public bool WelcomeShown;
        [DataMember] public string CaptureShortcut = "Custom";
        [DataMember] public int CaptureShortcutRevision = 1;
        [DataMember] public uint CustomCaptureModifiers = 3;
        [DataMember] public uint CustomCaptureKey = 0x41;
        [DataMember] public bool StartupEnabled;
        [DataMember] public int StartupRevision;
        // Rope slack in percent; 0 means "not set" and reads as 100.
        [DataMember] public int LineSlack;
        // Unkept cards older than this many days are removed; 0 means never.
        [DataMember] public int AutoClearDays;
    }

    sealed partial class Store
    {
        internal readonly string Root, Inbox;
        internal List<Shot> Items = new List<Shot>();
        internal Preferences Settings = new Preferences();
        internal const int MaxItems = 60;
        internal Action Changed;
        internal Store(string root)
        {
            Root = Path.GetFullPath(root);
            Inbox = Path.Combine(Root, "Inbox");
            Directory.CreateDirectory(Inbox);
            Settings = Read<Preferences>(Path.Combine(Root, "settings.json")) ?? new Preferences();
            if (Settings.Folders == null) Settings.Folders = new List<string>();
            Settings.ClipboardImages = false; Settings.ScreenshotFolder = false; // Legacy settings cannot enable other sources.
            // Apply the new default once. Afterwards, preserve explicit alternate choices.
            if (Settings.CaptureShortcutRevision < 1)
            {
                if (Settings.CaptureShortcut == "CtrlPrintScreen") Settings.CaptureShortcut = "CtrlB";
                Settings.CaptureShortcutRevision = 1;
            }
            if (Settings.CaptureShortcut != "CtrlB" && Settings.CaptureShortcut != "CtrlPrintScreen" && Settings.CaptureShortcut != "ShiftPrintScreen" && Settings.CaptureShortcut != "None" && Settings.CaptureShortcut != "Custom")
                Settings.CaptureShortcut = "CtrlB";
            if (Settings.CaptureShortcut == "Custom" && ShortcutSpec.Validate(Settings.CustomCaptureModifiers, Settings.CustomCaptureKey) != null)
                Settings.CaptureShortcut = "CtrlB";
            RestoreNotes();
            SweepNotePictures(null); // Pictures left behind by notes deleted in an earlier run.
            RestoreLinks();
            var saved = Read<List<Shot>>(Path.Combine(Root, "items.json")) ?? new List<Shot>();
            int loose = 0;
            foreach (var shot in saved)
            {
                // The limit counts only cards that are not kept, the same rule Add() applies.
                if (shot == null || (!shot.Kept && ++loose > MaxItems)) continue;
                try
                {
                    if (IsSafe(shot.File) && System.IO.File.Exists(FullPath(shot)))
                    {
                        // Recompute after an external editor changes the cached file.
                        var image = Load(FullPath(shot));
                        shot.Digest = Hash(image);
                        shot.Thumb = Thumbnail(image);
                        shot.Remark = shot.Remark ?? "";
                        Items.Add(shot);
                    }
                }
                catch (Exception ex) { Log("Restore image", ex); }
            }
        }
        static T Read<T>(string file) where T : class
        {
            if (!System.IO.File.Exists(file)) return null;
            try { using (var s = System.IO.File.OpenRead(file)) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(s); }
            catch { return null; }
        }
        static void Write<T>(string file, T value)
        {
            string temp = file + ".tmp";
            using (var s = System.IO.File.Create(temp)) new DataContractJsonSerializer(typeof(T)).WriteObject(s, value);
            if (System.IO.File.Exists(file)) System.IO.File.Replace(temp, file, null);
            else System.IO.File.Move(temp, file);
        }
        internal void SaveSettings() { Write(Path.Combine(Root, "settings.json"), Settings); }
        internal void Save() { Write(Path.Combine(Root, "items.json"), Items); }
        internal void Log(string context, Exception error)
        {
            try { System.IO.File.AppendAllText(Path.Combine(Root, "error.log"), DateTime.Now.ToString("s") + " " + context + ": " + error.Message + Environment.NewLine, Encoding.UTF8); }
            catch { }
        }
        internal bool IsSafe(string file)
        {
            return !String.IsNullOrWhiteSpace(file) && file == Path.GetFileName(file) &&
                file.StartsWith("snap-", StringComparison.Ordinal) && file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
                file.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }
        internal string FullPath(Shot shot)
        {
            if (!IsSafe(shot.File)) throw new InvalidOperationException("Invalid cache filename");
            return Path.Combine(Inbox, shot.File);
        }
        internal static BitmapSource Load(string file)
        {
            if (new FileInfo(file).Length > 64L * 1024 * 1024) throw new InvalidOperationException("The image is larger than 64 MB.");
            using (var stream = System.IO.File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];
                CheckSize(frame);
                frame.Freeze();
                return frame;
            }
        }
        internal static BitmapSource Preview(string file, int maxWidth, int maxHeight, double aspect)
        {
            if (new FileInfo(file).Length > 64L * 1024 * 1024) throw new InvalidOperationException("The image is larger than 64 MB.");
            using (var stream = System.IO.File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                if (aspect >= maxWidth / (double)maxHeight) image.DecodePixelWidth = Math.Max(1, maxWidth);
                else image.DecodePixelHeight = Math.Max(1, maxHeight);
                image.EndInit();
                image.Freeze();
                return image;
            }
        }
        static void CheckSize(BitmapSource image)
        {
            if (image.PixelWidth <= 0 || image.PixelHeight <= 0 || (long)image.PixelWidth * image.PixelHeight > 50000000)
                throw new InvalidOperationException("The image is too large (50 megapixels at most).");
        }
        internal static string Hash(BitmapSource image)
        {
            CheckSize(image);
            var rgba = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            int stride = checked(rgba.PixelWidth * 4);
            byte[] bytes = new byte[checked(stride * rgba.PixelHeight)];
            rgba.CopyPixels(bytes, stride, 0);
            using (var sha = SHA256.Create())
            {
                byte[] dimensions = Encoding.ASCII.GetBytes(rgba.PixelWidth + "x" + rgba.PixelHeight + ":");
                sha.TransformBlock(dimensions, 0, dimensions.Length, dimensions, 0);
                sha.TransformFinalBlock(bytes, 0, bytes.Length);
                return BitConverter.ToString(sha.Hash).Replace("-", "");
            }
        }
        internal static BitmapSource Thumbnail(BitmapSource image)
        {
            double scale = Math.Min(1, 360.0 / Math.Max(image.PixelWidth, image.PixelHeight));
            var small = new TransformedBitmap(image, new ScaleTransform(scale, scale));
            var converted = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
            int stride = converted.PixelWidth * 4;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            // A standalone thumbnail does not retain every full-resolution source in memory.
            var thumbnail = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            thumbnail.Freeze();
            return thumbnail;
        }
        internal Shot Add(BitmapSource image, string label)
        {
            string hash = Hash(image);
            if (Items.Any(x => x.Digest == hash)) return null;
            string filename = "snap-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".png";
            string full = Path.Combine(Inbox, filename);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = System.IO.File.Create(full)) encoder.Save(stream);
            var shot = new Shot { File = filename, Digest = hash, Label = label, Created = DateTime.Now, Thumb = Thumbnail(image), Rank = NextRank() };
            Items.Insert(0, shot);
            // Commit first; do not delete an older cache image until the new list is durable.
            // Kept cards do not count towards the limit and are never cleared by it.
            var overflow = Items.Where(x => !x.Kept).Skip(MaxItems).ToList();
            Items = Items.Where(x => !overflow.Contains(x)).ToList();
            Save();
            foreach (var old in overflow) DeleteCache(old);
            if (Changed != null) Changed();
            return shot;
        }
        internal Shot Import(string file) { return Add(Load(file), Path.GetFileName(file)); }
        void DeleteCache(Shot item)
        {
            try { if (IsSafe(item.File)) System.IO.File.Delete(FullPath(item)); }
            catch (Exception ex) { Log("Delete cached image", ex); }
        }
        internal void Remove(Shot item)
        {
            int index = Items.IndexOf(item); if (index < 0) return;
            Items.RemoveAt(index);
            try { Save(); } catch { Items.Insert(index, item); throw; }
            DeleteCache(item);
            if (Changed != null) Changed();
        }
        // A new card goes to the left end of the line.
        internal double NextRank()
        {
            double top = 0;
            foreach (var shot in Items) top = Math.Max(top, shot.Rank);
            foreach (var memo in Notes) top = Math.Max(top, memo.Rank);
            foreach (var link in Links) top = Math.Max(top, link.Rank);
            return top + 1;
        }
        // Takes a screenshot off the list but leaves its saved copy on disk, so the removal can be undone.
        internal void Detach(Shot item)
        {
            int index = Items.IndexOf(item); if (index < 0) return;
            Items.RemoveAt(index);
            try { Save(); } catch { Items.Insert(index, item); throw; }
            if (Changed != null) Changed();
        }
        internal void Reattach(Shot item, int index)
        {
            if (Items.Contains(item)) return;
            if (!System.IO.File.Exists(FullPath(item))) throw new InvalidOperationException("its saved copy is gone.");
            index = Math.Max(0, Math.Min(Items.Count, index));
            Items.Insert(index, item);
            try { Save(); } catch { Items.Remove(item); throw; }
            if (Changed != null) Changed();
        }
        // Deletes the saved copy of a screenshot that is no longer on the list.
        internal void Discard(Shot item) { if (!Items.Contains(item)) DeleteCache(item); }
        // Stores the order of the whole line, first card first.
        internal void Reorder(IList<object> deck)
        {
            var shotRanks = Items.ToDictionary(x => x, x => x.Rank);
            var noteRanks = Notes.ToDictionary(x => x, x => x.Rank);
            var linkRanks = Links.ToDictionary(x => x, x => x.Rank);
            for (int i = 0; i < deck.Count; i++)
            {
                var shot = deck[i] as Shot; var memo = deck[i] as Memo; var link = deck[i] as Link;
                if (shot != null) shot.Rank = deck.Count - i; else if (memo != null) memo.Rank = deck.Count - i; else if (link != null) link.Rank = deck.Count - i;
            }
            try { if (Notes.Count > 0) SaveNotes(); if (Links.Count > 0) SaveLinks(); Save(); }
            catch
            {
                // Put every list back as it was, in memory and, as far as possible, on disk.
                foreach (var pair in shotRanks) pair.Key.Rank = pair.Value;
                foreach (var pair in noteRanks) pair.Key.Rank = pair.Value;
                foreach (var pair in linkRanks) pair.Key.Rank = pair.Value;
                try { if (Notes.Count > 0) SaveNotes(); } catch { }
                try { if (Links.Count > 0) SaveLinks(); } catch { }
                try { Save(); } catch { }
                throw;
            }
            if (Changed != null) Changed();
        }
        internal void SetKept(object item, bool kept)
        {
            var shot = item as Shot; var memo = item as Memo; var link = item as Link;
            if (shot != null) { shot.Kept = kept; try { Save(); } catch { shot.Kept = !kept; throw; } }
            else if (memo != null) { memo.Kept = kept; try { SaveNotes(); } catch { memo.Kept = !kept; throw; } }
            else if (link != null) { link.Kept = kept; try { SaveLinks(); } catch { link.Kept = !kept; throw; } }
            else return;
            if (Changed != null) Changed();
        }
        internal void Refresh(Shot item)
        {
            if (!Items.Contains(item)) return;
            string file = FullPath(item);
            if (!System.IO.File.Exists(file)) { Remove(item); return; }
            var image = Load(file);
            item.Digest = Hash(image);
            item.Thumb = Thumbnail(image);
            Save();
            if (Changed != null) Changed();
        }
        internal void Clear()
        {
            var removed = Items.Where(x => !x.Kept).ToList();
            var before = Items;
            Items = Items.Where(x => x.Kept).ToList();
            try { Save(); } catch { Items = before; throw; }
            foreach (var item in removed) DeleteCache(item);
            if (Changed != null) Changed();
        }
    }
}
