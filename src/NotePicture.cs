// Snapline: a local screenshot shelf for Windows. MIT License.
// One picture can be attached to a note. It is saved as its own file beside the notes and lives exactly as long as the note.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Snapline
{
    sealed partial class Store
    {
        internal string NotePics { get { return Path.Combine(Root, "NotePics"); } }
        // Only names this app made itself: "note-" + 32 hex digits + ".png".
        internal static bool SafeNotePicture(string file)
        {
            return !String.IsNullOrWhiteSpace(file) && file.Length == 41 && file == Path.GetFileName(file) &&
                file.StartsWith("note-", StringComparison.Ordinal) && file.EndsWith(".png", StringComparison.Ordinal) &&
                file.Substring(5, 32).All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
        }
        internal string NotePicturePath(Memo memo)
        {
            return memo != null && SafeNotePicture(memo.Picture) ? Path.Combine(NotePics, memo.Picture) : null;
        }
        // A small copy for the card and the editor; null when the note has no picture or the file cannot be read.
        internal BitmapSource NoteThumb(Memo memo)
        {
            if (memo == null) return null;
            if (memo.Thumb != null) return memo.Thumb;
            string path = NotePicturePath(memo);
            if (path == null) return null;
            try
            {
                if (!File.Exists(path)) return null;
                memo.Thumb = Thumbnail(Load(path));
                return memo.Thumb;
            }
            catch (Exception ex) { Log("Load note picture", ex); return null; }
        }
        // Attaches `image` to the note, replacing any picture it had; null removes the picture.
        internal void SetNotePicture(Memo memo, BitmapSource image)
        {
            if (!Notes.Contains(memo)) throw new InvalidOperationException("This note has been deleted.");
            string old = memo.Picture, name = null;
            var oldThumb = memo.Thumb; DateTime oldTime = memo.Updated;
            BitmapSource thumb = null;
            if (image != null)
            {
                thumb = Thumbnail(image);
                Directory.CreateDirectory(NotePics);
                name = "note-" + Guid.NewGuid().ToString("N") + ".png";
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                try { using (var stream = File.Create(Path.Combine(NotePics, name))) encoder.Save(stream); }
                catch { DeleteNotePicture(name); throw; }
            }
            memo.Picture = name; memo.Thumb = thumb; memo.Updated = DateTime.Now;
            // The note list is saved first; the old file goes only once the list no longer points at it.
            try { SaveNotes(); }
            catch { memo.Picture = old; memo.Thumb = oldThumb; memo.Updated = oldTime; DeleteNotePicture(name); throw; }
            DeleteNotePicture(old);
            if (Changed != null) Changed();
        }
        void DeleteNotePicture(string name)
        {
            if (!SafeNotePicture(name)) return;
            try { File.Delete(Path.Combine(NotePics, name)); }
            catch (Exception ex) { Log("Delete note picture", ex); }
        }
        // Deletes picture files that no note points at. `spare` is a removed note that can still be brought back with Undo.
        internal void SweepNotePictures(Memo spare)
        {
            // If the notes could not be read, nothing is known about which pictures are in use: touch nothing.
            if (notesUnreadable) return;
            try
            {
                if (!Directory.Exists(NotePics)) return;
                var used = new HashSet<string>(Notes.Select(x => x.Picture).Where(x => x != null), StringComparer.OrdinalIgnoreCase);
                if (spare != null && spare.Picture != null) used.Add(spare.Picture);
                foreach (string path in Directory.GetFiles(NotePics, "note-*.png"))
                {
                    string name = Path.GetFileName(path);
                    if (SafeNotePicture(name) && !used.Contains(name)) DeleteNotePicture(name);
                }
            }
            catch (Exception ex) { Log("Tidy note pictures", ex); }
        }
    }

    static class NotePictures
    {
        const int MaxSide = 1600;
        static readonly string[] Kinds = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };
        // The picture on the clipboard: a copied image, or one copied image file. Null when the clipboard holds text or nothing usable,
        // so an ordinary paste of text is never taken over.
        internal static BitmapSource FromClipboard()
        {
            if (Clipboard.ContainsText()) return null;
            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList();
                if (files.Count != 1 || Array.IndexOf(Kinds, Path.GetExtension(files[0]).ToLowerInvariant()) < 0) return null;
                return Fit(Store.Load(files[0]), false);
            }
            if (!Clipboard.ContainsImage()) return null;
            var image = Clipboard.GetImage();
            return image == null ? null : Fit(image, true);
        }
        // Brings a picture down to a sensible size. Clipboard bitmaps often carry a meaningless alpha channel, so that is dropped.
        static BitmapSource Fit(BitmapSource image, bool dropAlpha)
        {
            BitmapSource result = image;
            if (dropAlpha) result = new FormatConvertedBitmap(result, PixelFormats.Bgr32, null, 0);
            double scale = Math.Min(1, (double)MaxSide / Math.Max(result.PixelWidth, result.PixelHeight));
            if (scale < 1) result = new TransformedBitmap(result, new ScaleTransform(scale, scale));
            var settled = new CachedBitmap(result, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            settled.Freeze();
            return settled;
        }
    }

    sealed partial class Shelf
    {
        void OpenNotePicture(Memo note)
        {
            string path = store.NotePicturePath(note);
            if (path == null || !File.Exists(path)) { Notify("That note's picture file is missing."); return; }
            Launch(path);
        }
        void CopyNotePicture(Memo note)
        {
            try
            {
                string path = store.NotePicturePath(note);
                if (path == null || !File.Exists(path)) { Notify("That note's picture file is missing."); return; }
                var data = new DataObject();
                data.SetImage(Store.Load(path));
                SetClipboard(data); Flash(note, "Picture copied");
            }
            catch (Exception ex) { store.Log("Copy note picture", ex); Notify("Copy failed. Try again shortly."); }
        }
    }
}
