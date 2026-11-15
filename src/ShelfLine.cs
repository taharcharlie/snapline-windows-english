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
            AddHelpRow(content, "Ctrl + Alt + S", "Pin open / close");
            AddHelpRow(content, "Scroll or ← →", "Roll the line; Enter copies");
            AddHelpRow(content, "1 to 9", "After Ctrl + Alt + S: copy that card");
            AddHelpRow(content, "Ctrl + Alt + D", "Hang what is on the clipboard");
            AddHelpRow(content, "Ctrl + Alt + N", "Start a new note");
            AddHelpRow(content, "＋ Note", "Quick notes, saved on this PC");
            AddHelpRow(content, "Click / double-click a note", "Copy its text / edit it");
            AddHelpRow(content, "Hover an image", "Enlarged preview while hovering");
            AddHelpRow(content, "Click / double-click a card", "Copy it / open it");
            AddHelpRow(content, "Drop a file, link or text here", "Hangs a reference card, or a note");
            AddHelpRow(content, "Drag along the line", "Reorder the cards");
            AddHelpRow(content, "Pull a card down", "Drop it in an app, or on this card to delete");
            AddHelpRow(content, "Click a peg", "Delete; Undo shows for a few seconds");
            AddHelpRow(content, "Right-click → Keep", "Yellow peg: never cleared or deleted");
            AddHelpRow(content, "Long-press an image", "Edit in Paint");
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
            return Math.Max(1, (int)((shelfWidth - 112) / 218) - 1); // Reserve one hanging slot for help.
        }
        int VisibleCardCount()
        {
            return Math.Min(CardsPerPage(), Deck().Count);
        }
        // The line shows up to CardsPerPage() cards starting at `first`; `focus` is the highlighted card.
        sealed class LineCard { internal FrameworkElement Element; internal int Index; internal ScaleTransform Scale; internal UIElement Shade; }
        readonly List<LineCard> lineCards = new List<LineCard>();
        int first, focus, rollDelta;
        double wheelRest;
        DateTime rollingUntil;
        const double SlotWidth = 218;
        int LastFirst(int count) { return Math.Max(0, count - CardsPerPage()); }
        // Wheel and swipe: move the line itself by whole cards. With everything in view they move the focus.
        internal void Roll(int step)
        {
            int count = Deck().Count; if (count == 0) return;
            if (count <= CardsPerPage()) { StepFocus(step); return; }
            int next = Math.Max(0, Math.Min(LastFirst(count), first + step));
            if (next == first) return;
            rollDelta = next - first; first = next;
            focus = Math.Max(first, Math.Min(first + CardsPerPage() - 1, focus));
            lastInside = DateTime.Now; Render();
        }
        // Keys: move the focus by one card and keep it centred while the line holds more cards than fit.
        internal void StepFocus(int step)
        {
            int count = Deck().Count; if (count == 0) return;
            focus = Math.Max(0, Math.Min(count - 1, focus + step));
            int next = count <= CardsPerPage() ? 0 : Math.Max(0, Math.Min(LastFirst(count), focus - CardsPerPage() / 2));
            lastInside = DateTime.Now;
            if (next != first) { rollDelta = next - first; first = next; Render(); }
            else ApplyFocus(true);
        }
        void WheelBy(int delta)
        {
            wheelRest += delta;
            while (Math.Abs(wheelRest) >= 120) { int direction = wheelRest > 0 ? 1 : -1; wheelRest -= 120 * direction; Roll(-direction); }
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
        // Gives a freshly hung card its focus scaling and, after a roll, slides it in from where it hung before.
        void Dress(FrameworkElement card, object item, int deckIndex, int rolled)
        {
            var scale = new ScaleTransform(1, 1, card.Width / 2, 0);
            var group = new TransformGroup(); group.Children.Add(scale); group.Children.Add(card.RenderTransform);
            card.RenderTransform = group; card.CacheMode = new BitmapCache(Math.Max(1, Dpi()) * 1.05);
            var shade = new Border { Background = Brush(Theme.Alpha("66", Theme.Bg)), IsHitTestVisible = false, Opacity = 0, Tag = "FocusShade" };
            ((Panel)card).Children.Add(shade);
            lineCards.Add(new LineCard { Element = card, Index = deckIndex, Scale = scale, Shade = shade });
            card.MouseEnter += delegate { if (DateTime.Now < rollingUntil || focus == deckIndex) return; focus = deckIndex; ApplyFocus(true); };
            DressPeg((Panel)card, item, deckIndex - first + 1);
            AttachGestures(card, item);
            if (rolled == 0) return;
            double left = Canvas.GetLeft(card), top = Canvas.GetTop(card), shift = rolled * SlotWidth;
            double oldCenter = Math.Max(24, Math.Min(shelfWidth - 24, left + card.Width / 2 + shift));
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut }; var time = TimeSpan.FromMilliseconds(220);
            card.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(left + shift, left, time) { EasingFunction = ease });
            card.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(RopeY(oldCenter) + 15, top, time) { EasingFunction = ease });
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
            foreach (var card in lineCards)
            {
                var panel = card.Element as Panel; if (panel == null) continue;
                foreach (var number in panel.Children.OfType<Border>().Where(x => Object.Equals(x.Tag, "PegNumber")))
                    number.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            }
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
        double HangingCenter(int index, int imageCount)
        {
            // The images and help card form a single centered group.
            return (shelfWidth - (imageCount + 1) * 218) / 2 + (index + 0.5) * 218;
        }
        List<System.Windows.Point> RopeNodes()
        {
            var nodes = new List<System.Windows.Point>();
            nodes.Add(new System.Windows.Point(24, RopeTop));
            int imageCount = VisibleCardCount(), hangingCount = imageCount + 1;
            double sag = (42 + 2 * Math.Min(hangingCount, 4)) * Slack;
            for (int i = 0; i < hangingCount; i++)
            {
                double x = HangingCenter(i, imageCount);
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
            if (focus < first) first = focus;
            if (focus >= first + perPage) first = focus - perPage + 1;
            var shown = deck.Skip(first).Take(perPage).ToArray();
            int rolled = rollDelta; rollDelta = 0;
            for (int i = 0; i < shown.Length; i++)
            {
                double center = HangingCenter(i, shown.Length);
                var memo = shown[i] as Memo; var link = shown[i] as Link;
                if (memo != null) MakeNoteCard(memo, center, RopeY(center) + 15, i);
                else if (link != null) MakeLinkCard(link, center, RopeY(center) + 15, i);
                else MakeCard((Shot)shown[i], center, RopeY(center) + 15, i);
                Dress((FrameworkElement)canvas.Children[canvas.Children.Count - 1], shown[i], first + i, rolled);
            }
            if (rolled != 0) rollingUntil = DateTime.Now.AddMilliseconds(260);
            ApplyFocus(false);
            if (first > 0) Place(Tool("‹", first + " more to the left (scroll)", delegate { Roll(-1); }), 34, 29);
            int hiddenRight = deck.Count - first - shown.Length;
            if (hiddenRight > 0) Place(Tool("›", hiddenRight + " more to the right (scroll)", delegate { Roll(1); }), shelfWidth - 62, 29);
            double helpCenter = HangingCenter(shown.Length, shown.Length);
            MakeHelpCard(helpCenter, RopeY(helpCenter) + 15, shown.Length);
            UpdateToast();
        }
        void UpdateToast()
        {
            if (toastElement != null) canvas.Children.Remove(toastElement);
            toastElement = null;
            if (!expanded) return;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
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
            Place(row, (shelfWidth - row.DesiredSize.Width) / 2, 238 + SlackDrop);
        }

        void MakeHelpCard(double center, double y, int index)
        {
            const double width = 172, height = 148;
            double lean = Lean(1.8);
            var swing = new RotateTransform(lean, width / 2, -15);
            var grid = new Grid { Width = width, Height = height, RenderTransform = swing, Cursor = Cursors.Hand };
            AutomationProperties.SetName(grid, "Tips (hover to view)");
            var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12) };
            var title = Text("Tips", 18, Theme.White);
            title.FontWeight = FontWeights.SemiBold; title.HorizontalAlignment = HorizontalAlignment.Center;
            content.Children.Add(title);
            var subtitle = Text("Hover for help", 11, Theme.Mid);
            subtitle.HorizontalAlignment = HorizontalAlignment.Center; subtitle.Margin = new Thickness(0, 4, 0, 0);
            content.Children.Add(subtitle);
            var capture = Tool("✂ Capture (copy only)", "Copies only; use the capture shortcut to hang automatically", StartCapture);
            capture.FontSize = Theme.Size(11); capture.Height = 26; capture.Margin = new Thickness(0, 10, 0, 0);
            capture.Background = Brush(Theme.Inset); capture.Foreground = Brush(Theme.Text);
            capture.ToolTip = null; content.Children.Add(capture);
            var addNote = Tool("＋ Note", "New note", NewNote);
            addNote.FontSize = Theme.Size(12); addNote.Height = 27; addNote.Margin = new Thickness(0, 6, 0, 0);
            addNote.Background = Brush(Theme.Accent); addNote.Foreground = Brush(Theme.Bg);
            System.Windows.Automation.AutomationProperties.SetAutomationId(addNote, "NewNote");
            content.Children.Add(addNote);
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
            helpTip.PlacementTarget = grid;
            grid.ToolTip = helpTip;
            RegisterHoverPreview(grid);
            ToolTipService.SetInitialShowDelay(grid, 220);
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
            double maxHeight = Math.Max(80, Math.Min(460, screen.WorkingArea.Height / dpi - 300 - remarkBudget));
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
            double imageWidth = Math.Max(1, shot.Thumb.PixelWidth), imageHeight = Math.Max(1, shot.Thumb.PixelHeight);
            double scale = Math.Min(192 / imageWidth, 124 / imageHeight);
            // Whole numbers: with layout rounding, a fractional size makes WPF clip the peg above the card.
            double width = Math.Round(Math.Max(70, imageWidth * scale)) + 7;
            double height = Math.Round(Math.Max(54, imageHeight * scale)) + 7;
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
            ToolTipService.SetInitialShowDelay(grid, 300);
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
