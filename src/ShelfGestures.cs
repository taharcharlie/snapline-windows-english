// Snapline: a local screenshot shelf for Windows. MIT License.
// What the pointer does to a card: click, reorder along the rope, pull off the rope, delete with undo, keep.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Snapline
{
    // A picture of the card that follows the pointer while it is off the rope. Clicks and drops pass through it.
    sealed class DragGhost : Window
    {
        readonly double scale;
        internal DragGhost(ImageSource picture, double width, double height, double scale)
        {
            this.scale = scale;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; ShowActivated = false; Topmost = true; IsHitTestVisible = false; Focusable = false;
            Width = width; Height = height; Left = -10000; Top = -10000;
            Content = new Image { Source = picture, Opacity = 0.9, Stretch = Stretch.Fill };
            SourceInitialized += delegate
            {
                IntPtr h = new WindowInteropHelper(this).Handle;
                Native.SetWindowLong(h, -20, Native.GetWindowLong(h, -20) | 0x00000020 | 0x08000000 | 0x00000080);
            };
        }
        internal void Follow()
        {
            Native.Point p;
            if (!Native.GetCursorPos(out p)) return;
            Left = p.X / scale - Width / 2; Top = p.Y / scale + 10;
        }
    }

    sealed partial class Shelf
    {
        // ---- delete with undo ----
        object undoItem; int undoIndex; DateTime undoUntil;
        static bool IsKept(object item)
        {
            var shot = item as Shot; var memo = item as Memo;
            return shot != null ? shot.Kept : memo != null && memo.Kept;
        }
        // Takes a card off the line. It can be brought back until the Undo notice goes away.
        // `fall` plays the falling-paper animation; a card dropped on the bin is already off the rope, so it does not fall.
        internal void RemoveWithUndo(object item, bool fall = true)
        {
            var shot = item as Shot; var memo = item as Memo;
            if (shot == null && memo == null) return;
            if (IsKept(item)) { Notify("This card is kept. Right-click it and choose 'Stop keeping' first."); return; }
            try
            {
                int index = shot != null ? store.Items.IndexOf(shot) : store.Notes.IndexOf(memo);
                if (index < 0) return;
                FinishUndo(); // Only the latest removal can be undone.
                Action commit = delegate { if (shot != null) store.Detach(shot); else store.RemoveNote(memo); };
                if (fall) RemoveWithFlight(HangingElement(item), commit); else commit();
                undoItem = item; undoIndex = index;
                undoUntil = DateTime.Now.AddSeconds(7); keepUntil = undoUntil; lastInside = DateTime.Now;
                toast = ""; UpdateToast();
            }
            catch (Exception ex) { store.Log("Remove card", ex); Notify("Could not remove. The card is kept; try again shortly."); }
        }
        internal void Undo()
        {
            object item = undoItem; if (item == null) return;
            undoItem = null;
            try
            {
                var shot = item as Shot;
                if (shot != null) store.Reattach(shot, undoIndex); else store.RestoreNote((Memo)item, undoIndex);
                keepUntil = DateTime.Now.AddSeconds(3);
            }
            catch (Exception ex) { store.Log("Undo removal", ex); Notify("Could not bring the card back: " + ex.Message); }
            UpdateToast();
        }
        // The removal becomes permanent: a removed screenshot's saved copy is deleted now.
        void FinishUndo()
        {
            var shot = undoItem as Shot; undoItem = null;
            if (shot != null) store.Discard(shot);
        }
        void ToggleKeep(object item)
        {
            if (item == null) return;
            try { store.SetKept(item, !IsKept(item)); }
            catch (Exception ex) { store.Log("Keep card", ex); Notify("Could not save that: " + ex.Message); }
        }

        // ---- the peg ----
        void DressPeg(Panel card, object item)
        {
            bool kept = IsKept(item);
            if (kept)
                foreach (var part in FlightDecorations(card).OfType<Border>().Where(x => Object.Equals(x.Tag, "HangingPeg") && x.Width > 2))
                    part.Background = Brush(Theme.Accent);
            var hit = new Border
            {
                // Almost, not fully, transparent: a see-through window lets clicks fall through fully transparent pixels.
                Width = 22, Height = 28, Background = Brush(Theme.Alpha("0C", Theme.Bg)), Tag = "HangingPeg", Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -24, 0, 0),
                ToolTip = kept ? "Kept: stays on the line" : "Unclip to delete (Undo appears)"
            };
            hit.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { e.Handled = true; };
            hit.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e) { e.Handled = true; RemoveWithUndo(item); };
            card.Children.Add(hit);
        }

        // ---- click, reorder, pull off ----
        List<LineCard> reorderSlots;
        bool binDrop;
        Border binFace;
        void AttachGestures(FrameworkElement card, object item)
        {
            var shot = item as Shot; var memo = item as Memo;
            bool down = false, held = false, reorder = false;
            Point start = new Point(); double grabX = 0;
            var hold = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(550) };
            hold.Tick += delegate { hold.Stop(); if (down && !reorder && shot != null) { held = true; down = false; card.ReleaseMouseCapture(); Edit(shot); } };
            card.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                e.Handled = true; CloseImagePreview(); CloseNotePreview();
                if (e.ClickCount == 2)
                {
                    hold.Stop(); down = false; card.ReleaseMouseCapture();
                    if (shot != null) Launch(store.FullPath(shot)); else EditNote(memo, false);
                    return;
                }
                start = e.GetPosition(canvas); grabX = start.X - Canvas.GetLeft(card);
                down = true; held = reorder = false; card.CaptureMouse(); hold.Start();
            };
            card.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (!down || held || e.LeftButton != MouseButtonState.Pressed) return;
                var p = e.GetPosition(canvas); double dx = p.X - start.X, dy = p.Y - start.Y;
                if (!reorder)
                {
                    if (Math.Abs(dx) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(dy) < SystemParameters.MinimumVerticalDragDistance) return;
                    hold.Stop();
                    // Downwards pulls the card off the rope; sideways slides it along.
                    if (dy > Math.Abs(dx)) { down = false; card.ReleaseMouseCapture(); Unclip(card, item); return; }
                    reorder = true; BeginReorder(card);
                }
                if (dy > 70) { down = false; reorder = false; card.ReleaseMouseCapture(); EndReorder(card, false); Unclip(card, item); return; }
                DragReorder(card, p.X - grabX);
            };
            card.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                hold.Stop(); bool wasDown = down, wasReorder = reorder; down = reorder = false; card.ReleaseMouseCapture(); e.Handled = true;
                if (wasReorder) { EndReorder(card, true); return; }
                if (wasDown && !held) { if (shot != null) Copy(shot); else CopyNote(memo); }
            };
            card.LostMouseCapture += delegate { hold.Stop(); if (reorder) { reorder = false; down = false; EndReorder(card, true); } };
        }
        void BeginReorder(FrameworkElement card)
        {
            reorderSlots = lineCards.OrderBy(x => x.Index).ToList();
            rollingUntil = DateTime.MaxValue; // No focus changes from cards sliding under the pointer.
            Panel.SetZIndex(card, 50);
            double left = Canvas.GetLeft(card), top = Canvas.GetTop(card);
            card.BeginAnimation(Canvas.LeftProperty, null); card.BeginAnimation(Canvas.TopProperty, null);
            Canvas.SetLeft(card, left); Canvas.SetTop(card, top);
        }
        void DragReorder(FrameworkElement card, double left)
        {
            if (reorderSlots == null) return;
            left = Math.Max(0, Math.Min(shelfWidth - card.Width, left));
            double center = left + card.Width / 2;
            Canvas.SetLeft(card, left); Canvas.SetTop(card, RopeY(center) + 21); // Hangs a little lower while held.
            int count = reorderSlots.Count, from = reorderSlots.FindIndex(x => Object.ReferenceEquals(x.Element, card));
            if (from < 0) return;
            int to = (int)Math.Floor((center - HangingCenter(0, count) + SlotWidth / 2) / SlotWidth);
            to = Math.Max(0, Math.Min(count - 1, to));
            if (to == from) return;
            var moving = reorderSlots[from]; reorderSlots.RemoveAt(from); reorderSlots.Insert(to, moving);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut }; var time = TimeSpan.FromMilliseconds(160);
            for (int i = 0; i < count; i++)
            {
                var other = reorderSlots[i].Element; if (Object.ReferenceEquals(other, card)) continue;
                double slot = HangingCenter(i, count);
                other.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(slot - other.Width / 2, time) { EasingFunction = ease });
                other.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(RopeY(slot) + 15, time) { EasingFunction = ease });
            }
        }
        void EndReorder(FrameworkElement card, bool commit)
        {
            var slots = reorderSlots; if (slots == null) return;
            reorderSlots = null; rollingUntil = DateTime.Now;
            if (commit && slots.Count > 0)
            {
                var deck = Deck();
                var indexes = slots.Select(x => x.Index).ToList();
                bool valid = indexes.All(x => x >= 0 && x < deck.Count), moved = !indexes.SequenceEqual(indexes.OrderBy(x => x));
                if (valid && moved)
                {
                    int start = indexes.Min();
                    var arranged = indexes.Select(x => deck[x]).ToList();
                    for (int i = 0; i < arranged.Count; i++) deck[start + i] = arranged[i];
                    focus = start + slots.FindIndex(x => Object.ReferenceEquals(x.Element, card));
                    try { store.Reorder(deck); return; } // The store's change notice redraws the line.
                    catch (Exception ex) { store.Log("Reorder cards", ex); Notify("Could not save the new order: " + ex.Message); }
                }
            }
            Render(true);
        }
        // The card leaves the rope and follows the pointer; it can be dropped into another app or on the bin.
        void Unclip(FrameworkElement card, object item)
        {
            var shot = item as Shot; var memo = item as Memo;
            DragGhost ghost = null;
            GiveFeedbackEventHandler follow = null;
            try
            {
                dragging = true; binDrop = false;
                var data = new DataObject();
                if (shot != null) data.SetData(DataFormats.FileDrop, new[] { store.FullPath(shot) });
                else data.SetData(DataFormats.UnicodeText, memo.Text ?? "");
                try
                {
                    var picture = FlightImage(card);
                    if (picture != null) { ghost = new DragGhost(picture.Source, picture.Width, picture.Height, Dpi()); ghost.Show(); ghost.Follow(); }
                }
                catch (Exception ex) { store.Log("Drag picture", ex); }
                card.Opacity = 0.25; ShowBin(true);
                follow = delegate { if (ghost != null) ghost.Follow(); };
                card.GiveFeedback += follow;
                var result = DragDrop.DoDragDrop(card, data, shot != null ? DragDropEffects.Copy | DragDropEffects.Move : DragDropEffects.Copy);
                if (binDrop) RemoveWithUndo(item, false);
                else if (shot != null && (result == DragDropEffects.Move || !System.IO.File.Exists(store.FullPath(shot)))) store.Remove(shot);
            }
            catch (Exception ex) { store.Log("Drag card", ex); Notify("Drag did not complete. Right-click the card for other ways to copy it."); }
            finally
            {
                if (follow != null) card.GiveFeedback -= follow;
                if (ghost != null) ghost.Close();
                card.Opacity = 1; ShowBin(false);
                dragging = false; binDrop = false; lastInside = DateTime.Now;
            }
        }
        void ShowBin(bool show)
        {
            if (helpTip != null && show) helpTip.IsOpen = false;
            if (binFace != null) binFace.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }
        // While a card is off the rope, the Tips card turns into the bin.
        void AddBin(Grid helpCard)
        {
            var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 30, Foreground = Brush(Theme.Warn), HorizontalAlignment = HorizontalAlignment.Center });
            label.Children.Add(new TextBlock { Text = "Drop here to delete", FontSize = Theme.Size(13), Foreground = Brush(Theme.Light), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) });
            binFace = new Border { Background = Brush(Theme.Panel), BorderBrush = Brush(Theme.Rule), BorderThickness = new Thickness(1), Child = label, Visibility = Visibility.Collapsed, Tag = "Bin" };
            helpCard.Children.Add(binFace);
            helpCard.AllowDrop = true;
            DragEventHandler over = delegate(object sender, DragEventArgs e)
            {
                if (!dragging) return;
                e.Effects = DragDropEffects.Copy; e.Handled = true;
                binFace.BorderBrush = Brush(Theme.Warn); binFace.BorderThickness = new Thickness(2);
            };
            helpCard.DragEnter += over; helpCard.DragOver += over;
            helpCard.DragLeave += delegate { binFace.BorderBrush = Brush(Theme.Rule); binFace.BorderThickness = new Thickness(1); };
            helpCard.Drop += delegate(object sender, DragEventArgs e)
            {
                if (!dragging) return;
                binDrop = true; e.Effects = DragDropEffects.Copy; e.Handled = true;
            };
        }
    }
}
