// Snapline: a local screenshot shelf for Windows. MIT License.
// The line itself: layout, rope, cards, previews and the Tips card.
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
        ToolTip HelpPopup()
        {
            var content = new StackPanel { Width = 278 };
            var title = Text("Snapline · Tips", 14, Theme.Accent);
            title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0, 0, 0, 10);
            content.Children.Add(title);
            AddHelpRow(content, CaptureShortcutLabel, ShortcutCollects ? "Capture a region and hang it" : "Set a capture shortcut in the tray menu");
            if (!CaptureHotkeyRegistered && store.Settings.CaptureShortcut != "None")
                content.Children.Add(new TextBlock { Text = CaptureShortcutLabel + " is in use; change it in the tray menu", FontSize = Theme.Size(10), Foreground = Brush(Theme.Warn), Margin = new Thickness(0, 0, 0, 7) });
            AddHelpRow(content, "Scissors button", "Copies only, does not hang");
            AddHelpRow(content, "Touch the top line", "Opens the line");
            AddHelpRow(content, "Slip under an image", "Add or edit its caption");
            if (!LineShortcut.Open.IsOff(store.Settings)) AddHelpRow(content, LineShortcutLabel(LineShortcut.Open), "Pin open / close");
            AddHelpRow(content, "Scroll or ← →", "Roll the line; Enter copies");
            AddHelpRow(content, "1 to 9", LineShortcut.Open.IsOff(store.Settings) ? "After pinning open: copy that card" : "After " + LineShortcutLabel(LineShortcut.Open) + ": copy that card");
            if (!LineShortcut.Hang.IsOff(store.Settings)) AddHelpRow(content, LineShortcutLabel(LineShortcut.Hang), "Hang what is on the clipboard");
            if (!LineShortcut.Note.IsOff(store.Settings)) AddHelpRow(content, LineShortcutLabel(LineShortcut.Note), "Start a new note");
            AddHelpRow(content, "＋ Note", "Quick notes, saved on this PC");
            AddHelpRow(content, "Click / double-click a note", "Copy its text / edit it");
            AddHelpRow(content, "Hover an image", "Enlarged preview while hovering");
            AddHelpRow(content, "Click / double-click a card", "Copy it / open it");
            AddHelpRow(content, "Drop a file, link or text here", "Hangs a reference card, or a note");
            AddHelpRow(content, "Drag along the line", "Reorder the cards");
            AddHelpRow(content, "Pull a card down", "Drop it in an app, or on this card to delete");
            AddHelpRow(content, "Click a peg", "Delete; Undo shows for a few seconds");
            AddHelpRow(content, "Right-click → Keep", "Yellow peg: never cleared or deleted");
            AddHelpRow(content, "Hold an image", "Edit in Paint (a bar fills first)");
            AddHelpRow(content, "Right-click an image", "Save as, remove and more");
            var footer = Text("Only shortcut captures are collected; customise in the tray", 10, Theme.Mid);
            footer.Margin = new Thickness(0, 7, 0, 0); content.Children.Add(footer);
            if (store.Settings.CaptureShortcut == "CtrlB")
            {
                var shortcutNote = Text("Ctrl + B overrides Bold; turn off or change in the tray", 10, Theme.Warn);
                shortcutNote.Margin = new Thickness(0, 5, 0, 0); content.Children.Add(shortcutNote);
            }
            var body = new Border { Child = content, Padding = new Thickness(14), CornerRadius = Theme.Radius(10),
                Background = Brush(Theme.Panel), BorderBrush = Brush(Theme.Rule), BorderThickness = new Thickness(1) };
            var tip = new ToolTip { Content = body, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Padding = new Thickness(0), HasDropShadow = true, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                HorizontalOffset = -67, VerticalOffset = 7, FontFamily = FontFamily };
            tip.Opened += delegate { helpReading = true; };
            tip.Closed += delegate { helpReading = false; lastInside = DateTime.Now; };
            return tip;
        }
        void AddHelpRow(StackPanel content, string key, string action)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var label = Text(key, 12, Theme.Light); label.TextWrapping = TextWrapping.Wrap; label.Margin = new Thickness(0, 0, 8, 0);
            var detail = new TextBlock { Text = action, FontSize = Theme.Size(12), Foreground = Brush(Theme.Mid), TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(detail, 1); row.Children.Add(label); row.Children.Add(detail); content.Children.Add(row);
        }
        MenuItem Item(string text, Action action)
        {
            var item = new MenuItem { Header = text };
            item.Click += delegate { action(); };
            return item;
        }
        ContextMenu ShotMenu(Shot shot)
        {
            var menu = new ContextMenu();
            menu.Items.Add(Item("Copy image", delegate { Copy(shot); }));
            menu.Items.Add(Item("Copy PNG file", delegate { CopyFile(shot); }));
            menu.Items.Add(Item("Open image", delegate { Launch(store.FullPath(shot)); }));
            menu.Items.Add(Item(String.IsNullOrWhiteSpace(shot.Remark) ? "Add caption…" : "Edit caption…", delegate { menu.IsOpen = false; dialogOpen = false; Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(delegate { EditRemark(shot, null); })); }));
            menu.Items.Add(Item("Edit in Paint", delegate { Edit(shot); }));
            menu.Items.Add(Item("Save as…", delegate { SaveAs(shot); }));
            menu.Items.Add(Item("Show in folder", delegate { Process.Start("explorer.exe", "/select,\"" + store.FullPath(shot) + "\""); }));
            menu.Items.Add(Item(shot.Kept ? "Stop keeping" : "Keep on the line", delegate { ToggleKeep(shot); }));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Remove from the line", delegate { RemoveShot(shot); }));
            menu.Opened += delegate { CloseImagePreview(); dialogOpen = true; };
            menu.Closed += delegate { dialogOpen = false; lastInside = DateTime.Now; };
            return menu;
        }
        TextBlock Text(string text, double size, string color)
        {
            return new TextBlock { Text = text, FontSize = Theme.Size(size), Foreground = Brush(color), VerticalAlignment = VerticalAlignment.Center };
        }
        void Place(UIElement element, double x, double y) { Canvas.SetLeft(element, x); Canvas.SetTop(element, y); canvas.Children.Add(element); }
        Button Tool(string text, string tip, Action click)
        {
            var button = new Button { Content = text, ToolTip = tip, Height = 28, MinWidth = 28, Padding = new Thickness(8, 2, 8, 2), Background = Brush(Theme.Alpha("CC", Theme.Panel)), Foreground = Brush(Theme.Text), BorderThickness = new Thickness(0), Cursor = Cursors.Hand, FontSize = Theme.Size(12), Focusable = false };
            AutomationProperties.SetName(button, tip);
            button.Click += delegate { click(); };
            return button;
        }
        // Independent cards hang from a thin, gently sagging line. Empty space stays transparent.
        int CardsPerPage()
        {
            return Math.Max(1, (int)((shelfWidth - 112 - TipsSlot) / SlotWidth)); // The Tips card takes its own slot.
        }
        int VisibleCardCount()
        {
            return Math.Min(CardsPerPage(), Deck().Count);
        }
        // The line shows up to CardsPerPage() cards starting at `first`; `focus` is the highlighted card.
        // Shift and Drop place a card relative to the rope point it hangs from, so a glide can move it along the rope.
        sealed class LineCard { internal FrameworkElement Element; internal int Index; internal ScaleTransform Scale; internal TranslateTransform Press; internal UIElement Shade; internal double Shift, Drop; }
        readonly List<LineCard> lineCards = new List<LineCard>();
        int first, focus;
        // Scrolling glides the line: `view` is where it is now and `target` where it is heading, both in cards from the start.
        // While it glides, `first` is the whole card at or left of `view`, and one more card hangs on each side to slide in.
        double view, target;
        bool gliding;
        DateTime lastFrame;
        DispatcherTimer settle;
        DateTime rollingUntil;
        // Every card hangs in a slot this wide: 218 at the original size, wider by as much as the widest card (a screenshot, 200) has grown.
        double SlotWidth { get { return Math.Round(218 + (CardScale - 1) * 200); } }
        int LastFirst(int count) { return Math.Max(0, count - CardsPerPage()); }
        // The ‹ › buttons and the tray: glide the line by whole cards. With every card in view there is nothing to move.
        internal void Roll(int step)
        {
            if (Deck().Count <= CardsPerPage()) return;
            lastInside = DateTime.Now; GlideTo(Math.Round(target) + step, true);
        }
        // Keys: move the focus by one card and keep it centred while the line holds more cards than fit.
        internal void StepFocus(int step)
        {
            int count = Deck().Count; if (count == 0) return;
            focus = Math.Max(0, Math.Min(count - 1, focus + step));
            lastInside = DateTime.Now;
            int next = count <= CardsPerPage() ? 0 : Math.Max(0, Math.Min(LastFirst(count), focus - CardsPerPage() / 2));
            if (next != (int)Math.Round(target)) GlideTo(next, true);
            ApplyFocus(true);
        }
        // Wheel and swipe move the line itself, never just the highlight. A wheel notch is one card; a touchpad moves it
        // as far as the fingers go, and the line settles on a whole card a moment after they stop.
        void WheelBy(int delta)
        {
            if (reorderSlots != null || dragging || Deck().Count <= CardsPerPage()) return;
            lastInside = DateTime.Now;
            GlideTo(target - delta / 120.0, false);
            if (settle == null)
            {
                settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
                settle.Tick += delegate { settle.Stop(); if (Math.Abs(target - Math.Round(target)) > 1e-6) GlideTo(Math.Round(target), true); };
            }
            settle.Stop(); settle.Start();
        }
        void GlideTo(double to, bool whole)
        {
            to = Math.Max(0, Math.Min(LastFirst(Deck().Count), to));
            if (whole) to = Math.Round(to);
            target = to;
            int perPage = CardsPerPage(), start = (int)Math.Round(target);
            focus = Math.Max(start, Math.Min(start + perPage - 1, focus)); // The highlight stays on a card that will be in view.
            CloseImagePreview(); CloseNotePreview();
            if (gliding) return;
            gliding = true; rollingUntil = DateTime.MaxValue; lastFrame = DateTime.Now;
            WakeSwing(); // In Fun the cards lag behind the glide and swing.
            Render(); // Hangs the extra card on each side now, so the one sliding in is there from the start.
            CompositionTarget.Rendering += GlideFrame;
        }
        // For the built-in tests: the scroll state, and the wheel without a mouse.
        internal double ScrollView { get { return view; } }
        internal double ScrollTarget { get { return target; } }
        internal bool Gliding { get { return gliding; } }
        internal int FirstShown { get { return first; } }
        internal int PerPage { get { return CardsPerPage(); } }
        internal void WheelForTest(int delta) { WheelBy(delta); }
        // Shows the key strip as the keyboard mode would, without taking any keys from other apps.
        internal void ShowKeyStripForTest(bool on) { lineKeys = on; UpdateToast(); }
        internal FrameworkElement CardOnLine(object item) { var found = lineCards.FirstOrDefault(x => Object.ReferenceEquals(Deck()[x.Index], item)); return found == null ? null : found.Element; }
        // Each frame closes a fixed share of the remaining distance: quick at first, easing in, never past the target.
        void GlideFrame(object sender, EventArgs e)
        {
            if (!gliding) return;
            // A slow frame still moves the line its share; only a long stall is capped.
            DateTime now = DateTime.Now; double seconds = Math.Min(0.25, (now - lastFrame).TotalSeconds); lastFrame = now;
            view += (target - view) * (1 - Math.Exp(-seconds / 0.075));
            bool arrived = Math.Abs(target - view) < 0.003;
            if (arrived) view = target;
            PlaceBand();
            if (arrived && target == Math.Round(target)) EndGlide();
        }
        // Moves the cards to `view`; when it passes a whole card, hangs the next set (one more on each side).
        void PlaceBand()
        {
            int start = (int)Math.Floor(view + 1e-6);
            if (start != first) { first = start; Render(); return; }
            foreach (var card in lineCards) PlaceOnLine(card);
        }
        void PlaceOnLine(LineCard card)
        {
            int perPage = CardsPerPage(), count = Math.Min(perPage, Deck().Count);
            double slot = card.Index - view, center = HangingCenter(slot, count);
            Canvas.SetLeft(card.Element, center + card.Shift); Canvas.SetTop(card.Element, RopeY(center) + card.Drop);
            // Past either end of the visible slots a card fades out, and stops taking the pointer when mostly gone.
            // A slim Tips card hides less, so on that side a card is gone within half a slot.
            double outside = Math.Max(0, Math.Max(-slot, (slot - (count - 1)) * (CompactTips ? 2 : 1)));
            card.Element.Opacity = Math.Max(0, 1 - outside);
            card.Element.IsHitTestVisible = outside < 0.5;
        }
        void EndGlide()
        {
            if (gliding) CompositionTarget.Rendering -= GlideFrame;
            gliding = false; view = target; rollingUntil = DateTime.Now;
            ApplyFocus(true);
            // Cards slid under a still pointer; let the one under it take the highlight now.
            Mouse.Synchronize();
        }
        // Stops a glide where it is going, without animating, for a redraw that cannot wait (closing, a new screen).
        void StopGlide()
        {
            if (gliding) CompositionTarget.Rendering -= GlideFrame;
            gliding = false; view = target = first; rollingUntil = DateTime.Now;
        }
        void ApplyFocus(bool animate)
        {
            foreach (var card in lineCards)
            {
                bool on = card.Index == focus;
                // Cards stay opaque; the ones out of focus sit under a dark wash instead of turning see-through.
                double scale = on ? 1.05 : 1, shade = on ? 0 : 1;
                if (!animate) { card.Scale.ScaleX = card.Scale.ScaleY = scale; card.Shade.Opacity = shade; continue; }
                var time = TimeSpan.FromMilliseconds(140);
                card.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, time));
                card.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, time));
                card.Shade.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(shade, time));
            }
        }
        // Gives a freshly hung card its focus scaling, its peg and its gestures, and remembers where it hangs from the rope.
        void Dress(FrameworkElement card, object item, int deckIndex, double center)
        {
            var scale = new ScaleTransform(1, 1, card.Width / 2, 0);
            var press = new TranslateTransform(); // The small sink while the card is pressed.
            var fall = new TranslateTransform(); // In Fun, a newly hung card drops onto the line.
            var group = new TransformGroup(); group.Children.Add(scale); group.Children.Add(press); group.Children.Add(fall); group.Children.Add(card.RenderTransform);
            var hanging = hangingMotions.Find(x => Object.ReferenceEquals(x.Card, card));
            if (hanging != null) hanging.Fall = fall;
            card.RenderTransform = group; card.CacheMode = new BitmapCache(Math.Max(1, Dpi()) * 1.05);
            var shade = new Border { Background = Brush(Theme.Alpha("66", Theme.Bg)), IsHitTestVisible = false, Opacity = 0, Tag = "FocusShade" };
            ((Panel)card).Children.Add(shade);
            var entry = new LineCard { Element = card, Index = deckIndex, Scale = scale, Press = press, Shade = shade, Shift = Canvas.GetLeft(card) - center, Drop = Canvas.GetTop(card) - RopeY(center) };
            lineCards.Add(entry);
            card.MouseEnter += delegate { if (DateTime.Now < rollingUntil || focus == deckIndex) return; focus = deckIndex; ApplyFocus(true); };
            DressPeg((Panel)card, item, deckIndex - first + 1);
            AttachGestures(card, item);
            PlaceOnLine(entry);
        }
        bool lineKeys;
        IntPtr lineKeysOwner;
        static readonly uint[] LineKeyCodes = { 0x25, 0x27, 0x0D, 0x1B, 0x2E, 0x4B };
        // After the line is opened from the keyboard, arrows, Enter, Esc and Delete drive it until it closes.
        void SetLineKeys(bool on)
        {
            if (on == lineKeys || handle == IntPtr.Zero) return;
            lineKeys = on;
            if (on) lineKeysOwner = Native.GetForegroundWindow();
            for (int i = 0; i < LineKeyCodes.Length; i++)
                if (on) Native.RegisterHotKey(handle, 20 + i, i < 2 ? 0u : 0x4000u, LineKeyCodes[i]); else Native.UnregisterHotKey(handle, 20 + i);
            // 1 to 9 grab the card with that number on its peg.
            for (int i = 0; i < 9; i++)
                if (on) Native.RegisterHotKey(handle, 30 + i, 0x4000u, (uint)(0x31 + i)); else Native.UnregisterHotKey(handle, 30 + i);
            // Ctrl + Z brings back the card just removed.
            if (on) Native.RegisterHotKey(handle, 26, 0x4002u, 0x5A); else Native.UnregisterHotKey(handle, 26);
            foreach (var card in lineCards)
            {
                var panel = card.Element as Panel; if (panel == null) continue;
                foreach (var number in panel.Children.OfType<Border>().Where(x => Object.Equals(x.Tag, "PegNumber")))
                    number.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            }
            UpdateToast(); // Shows or hides the strip naming these keys.
        }
        void LineDigit(int number)
        {
            var deck = Deck(); int at = first + number - 1;
            if (at < 0 || at >= deck.Count || !lineCards.Any(x => x.Index == at)) return;
            focus = at; ApplyFocus(true);
            Activate(deck[at]); Collapse(true);
        }
        void LineKey(int id)
        {
            var deck = Deck();
            object item = focus >= 0 && focus < deck.Count ? deck[focus] : null;
            switch (id)
            {
                case 20: StepFocus(-1); break;
                case 21: StepFocus(1); break;
                case 22: if (item != null) Activate(item); Collapse(true); break;
                case 23: Collapse(true); break;
                case 24: RemoveWithUndo(item); break;
                case 25: ToggleKeep(item); break;
            }
        }
        // The cards and the Tips card form one centred group. Tips takes a full slot, or a slim one when compact.
        bool CompactTips { get { return store.Settings.TipsCard == 1; } }
        double TipsWidth { get { return CompactTips ? Math.Round(64 * CardScale) : Math.Round(172 * CardScale); } }
        double TipsSlot { get { return CompactTips ? TipsWidth + 36 : SlotWidth; } }
        double HangingCenter(double index, int imageCount)
        {
            return (shelfWidth - imageCount * SlotWidth - TipsSlot) / 2 + (index + 0.5) * SlotWidth;
        }
        double TipsCenter(int imageCount)
        {
            return (shelfWidth - imageCount * SlotWidth - TipsSlot) / 2 + imageCount * SlotWidth + TipsSlot / 2;
        }
        List<System.Windows.Point> RopeNodes()
        {
            var nodes = new List<System.Windows.Point>();
            nodes.Add(new System.Windows.Point(24, RopeTop));
            int imageCount = VisibleCardCount(), hangingCount = imageCount + 1;
            double sag = (42 + 2 * Math.Min(hangingCount, 4)) * Slack;
            for (int i = 0; i < hangingCount; i++)
            {
                double x = i == imageCount ? TipsCenter(imageCount) : HangingCenter(i, imageCount);
                double loadPosition = (i + 1.0) / (hangingCount + 1.0);
                nodes.Add(new System.Windows.Point(x, RopeTop + sag * Math.Sin(Math.PI * loadPosition)));
            }
            nodes.Add(new System.Windows.Point(shelfWidth - 24, RopeTop));
            return nodes;
        }
        // Where the rope is tied: just under the top edge of the stage, which sits at the bar's bottom edge.
        const double RopeTop = 7;
        double StrandSag(System.Windows.Point a, System.Windows.Point b)
        {
            // A little slack between pegs, with the larger dip caused by the hanging photos.
            return Math.Min(8, Math.Max(1.5, (b.X - a.X) / 80)) * Math.Min(1.6, Slack);
        }
        double RopeY(double x)
        {
            var nodes = RopeNodes();
            for (int i = 1; i < nodes.Count; i++)
            {
                var a = nodes[i - 1]; var b = nodes[i];
                if (x > b.X && i < nodes.Count - 1) continue;
                double t = Math.Max(0, Math.Min(1, (x - a.X) / Math.Max(1, b.X - a.X)));
                return a.Y + (b.Y - a.Y) * t + 4 * StrandSag(a, b) * t * (1 - t);
            }
            return RopeTop;
        }

        internal void Render(bool force = false)
        {
            if (collapsing || (remarkEditor != null && !force)) { renderDeferred = true; return; }
            renderDeferred = false;
            CloseImagePreview(); CloseNotePreview();
            if (helpTip != null) helpTip.IsOpen = false;
            ClearHoverEffects();
            canvas.Children.Clear(); indicator = null; toastElement = null;
            if (!expanded) motionLayer.Children.Clear();
            if (!expanded)
            {
                StopGlide(); RestSwings();
                var mark = new Border { Width = TopHoverGate.Width, Height = TopHoverGate.IndicatorHeight, CornerRadius = Theme.Radius(1), Background = Brush(Theme.Accent), ToolTip = "Touch this line to open · See the 'Tips' card for help" };
                indicator = mark;
                mark.MouseEnter += delegate { TryRevealFromIndicator(); };
                Place(mark, Math.Max(0, (shelfWidth - TopHoverGate.Width) / 2 + TopHoverGate.Shift), 0);
                return;
            }
            double left = 24, right = shelfWidth - 24;
            var nodes = RopeNodes();
            var geometry = new StreamGeometry();
            using (var drawing = geometry.Open())
            {
                drawing.BeginFigure(nodes[0], false, false);
                for (int i = 1; i < nodes.Count; i++)
                {
                    var a = nodes[i - 1]; var b = nodes[i];
                    double dx = b.X - a.X, dy = b.Y - a.Y;
                    double slack = StrandSag(a, b) * 4 / 3;
                    drawing.BezierTo(new System.Windows.Point(a.X + dx / 3, a.Y + dy / 3 + slack),
                        new System.Windows.Point(a.X + dx * 2 / 3, a.Y + dy * 2 / 3 + slack),
                        b, true, false);
                }
            }

            geometry.Freeze();
            canvas.Children.Add(new System.Windows.Shapes.Path
            {
                Data = geometry, Stroke = Brush(Theme.Clear), StrokeThickness = 3.5,
                IsHitTestVisible = false, Effect = new DropShadowEffect { BlurRadius = 2, ShadowDepth = 1, Opacity = 0.12 }
            });
            canvas.Children.Add(new System.Windows.Shapes.Path
            {
                Data = geometry, Stroke = Brush(Theme.Text), StrokeThickness = 1.6,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
            });
            Place(new Ellipse { Width = 5, Height = 5, Fill = Brush(Theme.Accent), IsHitTestVisible = false }, left - 2.5, RopeTop - 2.5);
            Place(new Ellipse { Width = 5, Height = 5, Fill = Brush(Theme.Accent), IsHitTestVisible = false }, right - 2.5, RopeTop - 2.5);
            lineCards.Clear();
            int perPage = CardsPerPage();
            var deck = Deck();
            focus = Math.Max(0, Math.Min(deck.Count - 1, focus));
            first = Math.Max(0, Math.Min(LastFirst(deck.Count), first));
            if (!gliding)
            {
                // Settled: the line shows the focused card, and a later glide starts from here.
                if (focus < first) first = focus;
                if (focus >= first + perPage) first = focus - perPage + 1;
                view = target = first;
            }
            int visible = Math.Min(perPage, deck.Count);
            // The cards in view, plus while the line glides one more on each side to slide in from or out to.
            int from = gliding ? Math.Max(0, first - 1) : first, to = Math.Min(deck.Count, first + perPage + (gliding ? 1 : 0));
            for (int k = from; k < to; k++)
            {
                double center = HangingCenter(k - view, visible);
                var memo = deck[k] as Memo; var link = deck[k] as Link;
                if (memo != null) MakeNoteCard(memo, center, RopeY(center) + 15, k);
                else if (link != null) MakeLinkCard(link, center, RopeY(center) + 15, k);
                else MakeCard((Shot)deck[k], center, RopeY(center) + 15, k);
                Dress((FrameworkElement)canvas.Children[canvas.Children.Count - 1], deck[k], k, center);
            }
            var shown = deck.Skip(first).Take(perPage).ToArray();
            ApplyFocus(false);
            if (first > 0) { var back = Tool("‹", first + " more to the left (scroll)", delegate { Roll(-1); }); Panel.SetZIndex(back, 20); Place(back, 34, 29); }
            int hiddenRight = deck.Count - first - shown.Length;
            if (hiddenRight > 0) { var more = Tool("›", hiddenRight + " more to the right (scroll)", delegate { Roll(1); }); Panel.SetZIndex(more, 20); Place(more, shelfWidth - 62, 29); }
            double helpCenter = TipsCenter(shown.Length);
            MakeHelpCard(helpCenter, RopeY(helpCenter) + 15, shown.Length);
            // Cards sliding out on the right pass behind the Tips card.
            Panel.SetZIndex((UIElement)canvas.Children[canvas.Children.Count - 1], 10);
            SettleSwings(deck);
            UpdateToast();
        }
        void UpdateToast()
        {
            if (toastElement != null) canvas.Children.Remove(toastElement);
            toastElement = null;
            if (!expanded) return;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            if (lineKeys)
            {
                // While the keys belong to the line, say which ones; a mode you cannot see is a mode you cannot use.
                var keys = new TextBlock { FontSize = Theme.Size(11), Foreground = Brush(Theme.Mid), VerticalAlignment = VerticalAlignment.Center };
                foreach (var pair in new[] { new[] { "← →", "roll" }, new[] { "Enter", "copy" }, new[] { "1–9", "copy that card" }, new[] { "K", "keep" }, new[] { "Del", "remove" }, new[] { "Ctrl+Z", "undo" }, new[] { "Esc", "close" } })
                {
                    if (keys.Inlines.Count > 0) keys.Inlines.Add(new System.Windows.Documents.Run("   ·   "));
                    keys.Inlines.Add(new System.Windows.Documents.Run(pair[0]) { Foreground = Brush(Theme.Light), FontWeight = FontWeights.SemiBold });
                    keys.Inlines.Add(new System.Windows.Documents.Run(" " + pair[1]));
                }
                var strip = new Border { Child = keys, Padding = new Thickness(12, 6, 12, 6), Background = Brush(Theme.Alpha("EA", Theme.Panel)), BorderBrush = Brush(Theme.Rule), BorderThickness = new Thickness(1),
                    IsHitTestVisible = false, Tag = "LineKeys", Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
                row.Children.Add(strip);
            }
            if (undoItem != null)
            {
                var words = new TextBlock { FontSize = Theme.Size(12), Foreground = Brush(Theme.Text), VerticalAlignment = VerticalAlignment.Center };
                words.Inlines.Add(undoItem is Memo ? "Note removed" : undoItem is Link ? "Unhung; the original is untouched" : "Screenshot removed");
                words.Inlines.Add(new System.Windows.Documents.Run("     Undo") { Foreground = Brush(Theme.Accent), FontWeight = FontWeights.SemiBold });
                var notice = new Border
                {
                    Child = words, Padding = new Thickness(14, 7, 14, 7), Background = Brush(Theme.Panel), BorderBrush = Brush(Theme.Accent),
                    BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Tag = "ShelfUndo", ToolTip = "Bring the card back"
                };
                notice.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { e.Handled = true; };
                notice.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e) { e.Handled = true; Undo(); };
                row.Children.Add(notice);
            }
            if (toast != "")
            {
                var note = Text(toast, 11, Theme.Text);
                var feedback = new Border
                {
                    Child = note, Padding = new Thickness(12, 6, 12, 6),
                    CornerRadius = Theme.Radius(12), Background = Brush(Theme.Alpha("EA", Theme.Panel)),
                    BorderBrush = Brush(Theme.Alpha("88", Theme.Rule)), BorderThickness = new Thickness(0.5),
                    IsHitTestVisible = false, Tag = "ShelfFeedback"
                };
                if (row.Children.Count > 0) feedback.Margin = new Thickness(10, 0, 0, 0);
                feedback.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(feedback);
            }
            if (row.Children.Count == 0) return;
            row.Measure(new System.Windows.Size(Double.PositiveInfinity, Double.PositiveInfinity));
            toastElement = row;
            Place(row, (shelfWidth - row.DesiredSize.Width) / 2, 238 + SlackDrop + CardDrop);
        }

        void MakeHelpCard(double center, double y, int index)
        {
            double size = CardScale, width = TipsWidth, height = Math.Round(148 * size);
            double lean = Lean(1.8);
            var swing = new RotateTransform(lean, width / 2, -15);
            var grid = new Grid { Width = width, Height = height, RenderTransform = swing, Cursor = Cursors.Hand };
            AutomationProperties.SetName(grid, "Tips (hover to view)");
            var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12 * size) };
            if (CompactTips) FillCompactTips(content, size);
            else
            {
            var title = Text("Tips", 18 * size, Theme.White);
            title.FontWeight = FontWeights.SemiBold; title.HorizontalAlignment = HorizontalAlignment.Center;
            content.Children.Add(title);
            var subtitle = Text("Hover for help", 11 * size, Theme.Mid);
            subtitle.HorizontalAlignment = HorizontalAlignment.Center; subtitle.Margin = new Thickness(0, 4 * size, 0, 0);
            content.Children.Add(subtitle);
            var capture = Tool("✂ Capture (copy only)", "Copies only; use the capture shortcut to hang automatically", StartCapture);
            capture.FontSize = Theme.Size(11 * size); capture.Height = Math.Round(26 * size); capture.Margin = new Thickness(0, 10 * size, 0, 0);
            capture.Background = Brush(Theme.Inset); capture.Foreground = Brush(Theme.Text);
            capture.ToolTip = null; content.Children.Add(capture);
            var addNote = Tool("＋ Note", "New note", NewNote);
            addNote.FontSize = Theme.Size(12 * size); addNote.Height = Math.Round(27 * size); addNote.Margin = new Thickness(0, 6 * size, 0, 0);
            addNote.Background = Brush(Theme.Accent); addNote.Foreground = Brush(Theme.Bg);
            System.Windows.Automation.AutomationProperties.SetAutomationId(addNote, "NewNote");
            content.Children.Add(addNote);
            }
            grid.Children.Add(new Border
            {
                CornerRadius = Theme.Radius(5), Background = Brush(Theme.Panel),
                BorderBrush = Brush(Theme.Rule), BorderThickness = new Thickness(1),
                Child = content, Effect = new DropShadowEffect { BlurRadius = 10, Opacity = 0.16, ShadowDepth = 3 }
            });
            // Corner brackets mark the Tips card as the control card on the line.
            var brackets = Geometry.Parse(String.Format(System.Globalization.CultureInfo.InvariantCulture,
                "M0,12 V0 H12 M{0},0 H{1} V12 M{1},{2} V{3} H{0} M12,{3} H0 V{2}", width - 12, width, height - 12, height));
            grid.Children.Add(new System.Windows.Shapes.Path { Data = brackets, Stroke = Brush(Theme.Accent), StrokeThickness = 1.5, IsHitTestVisible = false });
            AddHangingPeg(grid);
            AddBin(grid);
            helpTip = HelpPopup();
            helpTip.PlacementTarget = grid; helpTip.HorizontalOffset = (width - 306) / 2; // Centred under the card; the help is 306 wide.
            grid.ToolTip = helpTip;
            RegisterHoverPreview(grid);
            ToolTipService.SetInitialShowDelay(grid, Tuning.PreviewDelay(store.Settings, 220));
            ToolTipService.SetShowDuration(grid, 60000);
            var breeze = new DoubleAnimation(lean - 0.6, lean + 0.6, TimeSpan.FromSeconds(3.8))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            Timeline.SetDesiredFrameRate(breeze, 24);
            RegisterHangingMotion(grid, swing, breeze);
            grid.MouseEnter += delegate { if (!hoverEffectsPaused) SetHangingAnimation(swing, new DoubleAnimation(0, TimeSpan.FromMilliseconds(180))); };
            grid.MouseLeave += delegate { if (!hoverEffectsPaused) SetHangingAnimation(swing, breeze); };
            grid.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e) { e.Handled = true; if (HoverPreviewAllowed) helpTip.IsOpen = !helpTip.IsOpen; };
            Place(grid, center - width / 2, y);
        }
        // The compact Tips card: a "?" over two square buttons, capture and new note. Resting on it opens the same help.
        void FillCompactTips(StackPanel content, double size)
        {
            content.Margin = new Thickness(8 * size);
            var mark = Text("?", 26 * size, Theme.White);
            mark.FontWeight = FontWeights.SemiBold; mark.HorizontalAlignment = HorizontalAlignment.Center;
            content.Children.Add(mark);
            var capture = Tool("✂", "Capture a region (copies only)", StartCapture);
            capture.FontSize = Theme.Size(14 * size); capture.Width = capture.Height = Math.Round(34 * size); capture.Padding = new Thickness(0); capture.Margin = new Thickness(0, 8 * size, 0, 0);
            capture.Background = Brush(Theme.Inset); capture.Foreground = Brush(Theme.Text);
            content.Children.Add(capture);
            var addNote = Tool("＋", "New note", NewNote);
            addNote.FontSize = Theme.Size(16 * size); addNote.Width = addNote.Height = Math.Round(34 * size); addNote.Padding = new Thickness(0); addNote.Margin = new Thickness(0, 6 * size, 0, 0);
            addNote.Background = Brush(Theme.Accent); addNote.Foreground = Brush(Theme.Bg);
            System.Windows.Automation.AutomationProperties.SetAutomationId(addNote, "NewNote");
            content.Children.Add(addNote);
        }
        void AddHangingPeg(Grid grid)
        {
            var pin = new Border
            {
                Width = 6, Height = 22, CornerRadius = Theme.Radius(2), Tag = "HangingPeg",
                Background = Brush(Theme.Text),
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, -20, 0, 0), IsHitTestVisible = false,
                Effect = new DropShadowEffect { BlurRadius = 2, ShadowDepth = 1, Opacity = 0.16 }
            };
            grid.Children.Add(pin);
            grid.Children.Add(new Border
            {
                Width = 1, Height = 14, Background = Brush(Theme.Alpha("66", Theme.Mid)), Tag = "HangingPeg",
                VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, -16, 0, 0), IsHitTestVisible = false
            });
        }

        void CloseImagePreview()
        {
            var current = imageTip;
            imageTip = null;
            if (current != null) current.IsOpen = false;
        }
        ToolTip ImagePopup(Shot shot, FrameworkElement target)
        {
            double dpi = Dpi();
            double maxWidth = Math.Max(100, Math.Min(760, screen.WorkingArea.Width / dpi - 64));
            double remarkBudget = String.IsNullOrWhiteSpace(shot.Remark) ? 0 : Math.Max(48, Math.Min(140, screen.WorkingArea.Height / dpi / 5));
            double maxHeight = Math.Max(80, Math.Min(460, screen.WorkingArea.Height / dpi - 300 - CardDrop - remarkBudget));
            double aspect = shot.Thumb.PixelWidth / (double)Math.Max(1, shot.Thumb.PixelHeight);
            double width = Math.Min(maxWidth, maxHeight * aspect), height = width / aspect;
            double bodyWidth = Math.Max(180, width);
            var preview = new Image { Width = width, Height = height, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
            var content = new StackPanel { Width = bodyWidth };
            ScrollViewer remarkScroll = null;
            var title = Text("Preview", 12, Theme.Text);
            title.Margin = new Thickness(0, 0, 0, 8); content.Children.Add(title);
            content.Children.Add(new Border { Child = preview, Background = Brush(Theme.Panel), CornerRadius = Theme.Radius(4), ClipToBounds = true });
            if (remarkBudget > 0)
            {
                var remarkText = new TextBlock { Text = shot.Remark, FontSize = Theme.Size(13), LineHeight = 20, TextWrapping = TextWrapping.Wrap,
                    Foreground = Brush(Theme.Light) };
                AutomationProperties.SetAutomationId(remarkText, "PreviewRemark");
                var scroll = new ScrollViewer { Content = remarkText, MaxHeight = remarkBudget, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
                remarkScroll = scroll;
                content.Children.Add(new Border { Child = scroll, Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 9, 0, 0),
                    CornerRadius = Theme.Radius(5), Background = Brush(Theme.Panel), BorderBrush = Brush(Theme.Rule), BorderThickness = new Thickness(1) });
            }
            content.Children.Add(new TextBlock { Text = "Click to copy · Double-click to open · Drag out" + (remarkBudget > 0 ? " · Scroll for long captions" : ""), FontSize = Theme.Size(11),
                Foreground = Brush(Theme.Mid), Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap });
            var tip = new ToolTip
            {
                Content = new Border { Child = content, Padding = new Thickness(10), CornerRadius = Theme.Radius(9),
                    Background = Brush(Theme.Panel), BorderBrush = Brush(Theme.Rule), BorderThickness = new Thickness(1) },
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0),
                HasDropShadow = true, IsHitTestVisible = false, FontFamily = FontFamily,
                Tag = remarkScroll,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, PlacementTarget = target,
                HorizontalOffset = (target.Width - bodyWidth - 22) / 2, VerticalOffset = 10
            };
            tip.Opened += delegate
            {
                if (!HoverPreviewAllowed ||
                    Mouse.LeftButton == MouseButtonState.Pressed || !store.Items.Contains(shot)) { tip.IsOpen = false; return; }
                try
                {
                    CloseImagePreview(); CloseNotePreview();
                    if (helpTip != null) helpTip.IsOpen = false;
                    preview.Source = Store.Preview(store.FullPath(shot), (int)Math.Ceiling(maxWidth * dpi), (int)Math.Ceiling(maxHeight * dpi), aspect);
                    imageTip = tip;
                }
                catch (Exception ex) { tip.IsOpen = false; store.Log("Preview image", ex); }
            };
            tip.Closed += delegate
            {
                preview.Source = null; // Release the enlarged bitmap as soon as the pointer leaves.
                if (Object.ReferenceEquals(imageTip, tip)) imageTip = null;
                lastInside = DateTime.Now;
            };
            return tip;
        }
        void MakeCard(Shot shot, double center, double y, int index)
        {
            double imageWidth = Math.Max(1, shot.Thumb.PixelWidth), imageHeight = Math.Max(1, shot.Thumb.PixelHeight), size = CardScale;
            double scale = Math.Min(192 * size / imageWidth, 124 * size / imageHeight);
            // Whole numbers: with layout rounding, a fractional size makes WPF clip the peg above the card.
            double width = Math.Round(Math.Max(70 * size, imageWidth * scale)) + 7;
            double height = Math.Round(Math.Max(54 * size, imageHeight * scale)) + 7;
            double lean = Lean(new[] { -2.2, 1.4, -1.1, 2.0, -0.6 }[index % 5]);
            double cardWidth = width;
            var swing = new RotateTransform(lean, cardWidth / 2, -15);
            // Slightly larger than its contents. At 125% scaling the contents round up to a fraction more than an
            // exactly-fitting card, and WPF then clips the card to its bounds, which cut off the peg above it.
            var card = new Grid { Width = cardWidth + 1, Height = height + RemarkStripHeight + 2, RenderTransform = swing, Tag = shot };
            card.Children.Add(new Border
            {
                CornerRadius = Theme.Radius(5), Background = Brush(Theme.Panel), IsHitTestVisible = false,
                Effect = new DropShadowEffect { Color = MemoTheme.Hex(Theme.Shadow), BlurRadius = 10, Opacity = 0.18, ShadowDepth = 3 }
            });
            var grid = new Grid
            {
                Width = width, Height = height, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Cursor = Cursors.Hand
            };
            AutomationProperties.SetName(grid, "Hung image " + shot.Label);
            AutomationProperties.SetAutomationId(grid, "ShotCard-" + shot.File);
            grid.ToolTip = ImagePopup(shot, grid);
            RegisterHoverPreview(grid);
            ToolTipService.SetInitialShowDelay(grid, Tuning.PreviewDelay(store.Settings, 300));
            ToolTipService.SetBetweenShowDelay(grid, 100);
            ToolTipService.SetShowDuration(grid, 60000);
            grid.PreviewMouseDown += delegate { CloseImagePreview(); };
            grid.PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e) {
                if (imageTip == null || !imageTip.IsOpen || imageTip.PlacementTarget != grid) return;
                var scroll = imageTip.Tag as ScrollViewer;
                if (scroll != null) { scroll.ScrollToVerticalOffset(scroll.VerticalOffset - e.Delta / 3.0); e.Handled = true; }
            };
            var frame = new Border
            {
                CornerRadius = Theme.Radius(5, 5, 0, 0), Background = Brush(Theme.Panel),
                BorderBrush = Brush(Theme.Rule), BorderThickness = new Thickness(1, 1, 1, 0),
                Padding = new Thickness(2.5)
            };
            frame.Child = new Border
            {
                CornerRadius = Theme.Radius(3), ClipToBounds = true,
                Child = new Image { Source = shot.Thumb, Stretch = Stretch.Uniform }
            };
            grid.Children.Add(frame);
            var remove = new Button
            {
                Content = "×", Width = 19, Height = 19, FontSize = Theme.Size(13),
                Background = Brush(Theme.Alpha("E6", Theme.Bg)), Foreground = Brush(Theme.Light), BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 3, 3, 0), Focusable = false, ToolTip = "Remove saved copy",
                Opacity = 0, IsHitTestVisible = false, Tag = "DeleteButton"
            };
            AutomationProperties.SetName(remove, "Remove " + shot.Label);
            remove.PreviewMouseDown += delegate(object sender, MouseButtonEventArgs e) { e.Handled = true; RemoveShot(shot); };
            grid.Children.Add(remove);
            var breeze = new DoubleAnimation(lean - 0.65, lean + 0.65, TimeSpan.FromSeconds(3.4 + (index % 3) * 0.6))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            Timeline.SetDesiredFrameRate(breeze, 24);
            RegisterHangingMotion(card, swing, breeze);
            card.MouseEnter += delegate
            {
                if (hoverEffectsPaused) return;
                remove.IsHitTestVisible = true;
                remove.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
                SetHangingAnimation(swing, new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)));
            };
            grid.MouseLeave += delegate { CloseImagePreview(); };
            card.MouseLeave += delegate
            {
                CloseImagePreview(); CloseNotePreview();
                if (hoverEffectsPaused) return;
                remove.IsHitTestVisible = false;
                remove.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)));
                Timeline.SetDesiredFrameRate(breeze, 24);
                SetHangingAnimation(swing, breeze);
            };
            grid.ContextMenu = ShotMenu(shot);
            card.Children.Add(grid);
            card.Children.Add(RemarkStrip(shot, cardWidth, height));
            // On the outer card, like notes: inside the nested image grid WPF clips it away.
            AddHangingPeg(card);
            Place(card, center - cardWidth / 2, y);
        }
    }
}
