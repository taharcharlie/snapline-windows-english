using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Snapline
{
    [DataContract] sealed class Memo
    {
        [DataMember] public string Id;
        [DataMember] public string Text;
        [DataMember] public string Color;
        [DataMember] public DateTime Created;
        [DataMember] public DateTime Updated;
        [DataMember] public double Rank;
        [DataMember] public bool Kept;
        // File name of the attached picture inside the NotePics folder; null when there is none.
        [DataMember] public string Picture;
        internal System.Windows.Media.Imaging.BitmapSource Thumb;
    }
    sealed partial class Store
    {
        internal readonly List<Memo> Notes = new List<Memo>();
        internal const int MaxNoteText = 20000;
        bool notesUnreadable;
        void RestoreNotes()
        {
            string file = System.IO.Path.Combine(Root, "notes.json");
            if (!File.Exists(file)) return;
            try
            {
                if (new FileInfo(file).Length > 32L * 1024 * 1024) throw new InvalidDataException("The notes file is too large.");
                List<Memo> saved;
                using (var stream = File.OpenRead(file)) saved = (List<Memo>)new DataContractJsonSerializer(typeof(List<Memo>)).ReadObject(stream);
                if (saved == null) throw new InvalidDataException("The notes file is not in a valid format.");
                foreach (var memo in saved)
                {
                    if (memo == null) continue;
                    if (memo.Text == null) memo.Text = "";
                    Guid id;
                    if (!Guid.TryParseExact(memo.Id, "N", out id)) memo.Id = Guid.NewGuid().ToString("N");
                    if (Notes.Any(x => x.Id == memo.Id)) continue;
                    memo.Color = MemoTheme.Normalize(memo.Color);
                    if (!SafeNotePicture(memo.Picture)) memo.Picture = null;
                    if (memo.Created == DateTime.MinValue) memo.Created = DateTime.Now;
                    if (memo.Updated == DateTime.MinValue) memo.Updated = memo.Created;
                    Notes.Add(memo);
                }
            }
            catch (Exception ex) { Notes.Clear(); notesUnreadable = true; Log("Restore notes", ex); }
        }
        void SaveNotes()
        {
            if (notesUnreadable) throw new InvalidOperationException("The notes file could not be read, so it was left untouched. Restore notes.json and restart.");
            Write(System.IO.Path.Combine(Root, "notes.json"), Notes);
        }
        internal Memo AddNote(string text, string color)
        {
            if (text == null) text = "";
            if (text.Length > MaxNoteText) throw new InvalidOperationException("A note can hold up to 20000 characters.");
            var memo = new Memo { Id = Guid.NewGuid().ToString("N"), Text = text, Color = MemoTheme.Normalize(color), Created = DateTime.Now, Updated = DateTime.Now, Rank = NextRank() };
            Notes.Insert(0, memo);
            try { SaveNotes(); } catch { Notes.Remove(memo); throw; }
            if (Changed != null) Changed();
            return memo;
        }
        internal void UpdateNote(Memo memo, string text, string color)
        {
            if (!Notes.Contains(memo)) throw new InvalidOperationException("This note has been deleted.");
            if (text == null) text = "";
            if (text.Length > MaxNoteText) throw new InvalidOperationException("A note can hold up to 20000 characters.");
            color = MemoTheme.Normalize(color);
            if (memo.Text == text && memo.Color == color) return;
            string oldText = memo.Text, oldColor = memo.Color;
            DateTime oldTime = memo.Updated;
            memo.Text = text; memo.Color = color; memo.Updated = DateTime.Now;
            try { SaveNotes(); }
            catch { memo.Text = oldText; memo.Color = oldColor; memo.Updated = oldTime; throw; }
            if (Changed != null) Changed();
        }
        internal void RestoreNote(Memo memo, int index)
        {
            if (Notes.Contains(memo)) return;
            index = Math.Max(0, Math.Min(Notes.Count, index));
            Notes.Insert(index, memo);
            try { SaveNotes(); } catch { Notes.Remove(memo); throw; }
            if (Changed != null) Changed();
        }
        internal void RemoveNote(Memo memo)
        {
            int index = Notes.IndexOf(memo);
            if (index < 0) return;
            Notes.RemoveAt(index);
            try { SaveNotes(); } catch { Notes.Insert(index, memo); throw; }
            if (Changed != null) Changed();
        }
    }
    static class MemoTheme
    {
        internal static readonly string[] Colors = { "Butter", "Mint", "Lavender" };
        internal static string Normalize(string color) { return Colors.Contains(color) ? color : "Butter"; }
        internal static Color Hex(string value) { return (Color)ColorConverter.ConvertFromString(value); }
        internal static SolidColorBrush Brush(string value) { var b = new SolidColorBrush(Hex(value)); b.Freeze(); return b; }
        internal static string Top(string color) { return color == "Mint" ? Theme.Panel : color == "Lavender" ? Theme.Panel : Theme.Panel; }
        internal static string Bottom(string color) { return color == "Mint" ? Theme.Panel : color == "Lavender" ? Theme.Panel : Theme.Panel; }
        internal static string Accent(string color) { return Theme.NoteAccent(color); }
        internal static string Edge(string color) { return color == "Mint" ? Theme.Rule : color == "Lavender" ? Theme.Rule : Theme.Rule; }
        internal static Brush Paper(string color) { return Brush(Theme.Panel); }
        internal static Button Button(string text, string name)
        {
            var b = new Button { Content = text, Height = 32, Padding = new Thickness(12, 4, 12, 4), FontSize = Theme.Size(12), Cursor = Cursors.Hand,
                Background = Brush(Theme.Alpha("18", Theme.White)), Foreground = Brush(Theme.Light), BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(0) };
            System.Windows.Automation.AutomationProperties.SetName(b, name);
            var frame = new FrameworkElementFactory(typeof(Border));
            frame.SetValue(Border.CornerRadiusProperty, Theme.Radius(7));
            frame.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
            frame.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
            frame.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = RelativeSource.TemplatedParent });
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetBinding(ContentPresenter.ContentProperty, new Binding("Content") { RelativeSource = RelativeSource.TemplatedParent });
            content.SetBinding(FrameworkElement.MarginProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
            frame.AppendChild(content); b.Template = new ControlTemplate(typeof(Button)) { VisualTree = frame };
            b.MouseEnter += delegate { b.Opacity = 0.78; }; b.MouseLeave += delegate { b.Opacity = 1; };
            return b;
        }
        internal static TextBlock Text(string text, double size, string color)
        { return new TextBlock { Text = text, FontSize = Theme.Size(size), Foreground = Brush(color), FontFamily = Theme.Font }; }
    }

    sealed class MemoEditor : Window
    {
        readonly Store store;
        readonly Memo memo;
        readonly TextBox input;
        readonly TextBlock status, placeholder, total;
        readonly Button calcButton;
        // A calculation note's results, each at the end of its line, drawn over the text.
        readonly Canvas results = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
        bool resultsPending;
        readonly Border paper;
        readonly StackPanel pictureRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        readonly DispatcherTimer saveDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(480) };
        readonly Dictionary<string, Ellipse> swatches = new Dictionary<string, Ellipse>();
        string color;
        bool dirty;
        internal MemoEditor(Store data, Memo note)
        {
            store = data; memo = note; color = note.Color;
            // The card size preset makes the editor and its writing larger too, as far as the screen allows.
            double scale = CardSizes.Scale(store.Settings);
            Title = "Edit note · Snapline";
            Width = Math.Max(524, Math.Min(Math.Round(524 * scale), SystemParameters.WorkArea.Width - 40));
            Height = Math.Max(462, Math.Min(Math.Round(462 * scale), SystemParameters.WorkArea.Height - 40));
            double lineHeight = Math.Round(25 * scale);
            ShowInTaskbar = false; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = Theme.Font;
            UseLayoutRounding = true;
            var outer = new Grid { Margin = new Thickness(14) };
            paper = new Border { CornerRadius = Theme.Radius(12), Background = MemoTheme.Paper(color), BorderBrush = MemoTheme.Brush(MemoTheme.Edge(color)), BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { Color = MemoTheme.Hex(Theme.Shadow), BlurRadius = 22, ShadowDepth = 6, Opacity = 0.20 } };
            outer.Children.Add(paper);
            var content = new Grid { Margin = new Thickness(25, 22, 25, 18) };
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            outer.Children.Add(content);
            var heading = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            var title = new StackPanel();
            title.Children.Add(MemoTheme.Text("Quick note", 22, Theme.Light));
            title.Children.Add(new TextBlock { Text = "Hang a thought here.", FontSize = Theme.Size(11), Foreground = MemoTheme.Brush(Theme.Text), Margin = new Thickness(0, 6, 0, 0) });
            title.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { if (e.ClickCount == 1) DragMove(); };
            heading.Children.Add(title);
            var close = MemoTheme.Button("×", "Close the note editor"); close.Width = 30; close.Height = 30; close.FontSize = Theme.Size(20);
            close.Padding = new Thickness(0); close.HorizontalAlignment = HorizontalAlignment.Right; close.VerticalAlignment = VerticalAlignment.Top;
            close.Click += delegate { Close(); }; heading.Children.Add(close);
            content.Children.Add(heading);
            var palette = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            palette.Children.Add(new TextBlock { Text = "Paper", FontSize = Theme.Size(11), Foreground = MemoTheme.Brush(Theme.Text), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) });
            for (int i = 0; i < MemoTheme.Colors.Length; i++)
            {
                string mode = MemoTheme.Colors[i];
                string label = new[] { "Butter", "Mint", "Lavender" }[i];
                var circle = new Ellipse { Width = 21, Height = 21, Fill = MemoTheme.Brush(MemoTheme.Bottom(mode)), StrokeThickness = 2 };
                swatches.Add(mode, circle);
                var choice = MemoTheme.Button("", label + "Paper"); choice.Content = circle; choice.Width = 32; choice.Height = 32; choice.Padding = new Thickness(0); choice.Margin = new Thickness(0, 0, 4, 0); choice.ToolTip = label;
                System.Windows.Automation.AutomationProperties.SetAutomationId(choice, "NoteColor-" + mode);
                choice.Click += delegate { color = mode; UpdatePaper(); MarkDirty(); };
                palette.Children.Add(choice);
            }
            // Formatting buttons. They never take the focus, so the selection in the text stays put.
            var tools = new[]
            {
                new { Label = "B", Name = "Bold", Tip = "Bold  (Ctrl+B)", Run = (Action)delegate { Wrap("**"); } },
                new { Label = "I", Name = "Italic", Tip = "Italic  (Ctrl+I)", Run = (Action)delegate { Wrap("*"); } },
                new { Label = "U", Name = "Underline", Tip = "Underline  (Ctrl+U)", Run = (Action)delegate { Wrap("__"); } },
                new { Label = "☐ To-do", Name = "Todo", Tip = "Make the line a to-do, or back  (Ctrl+T)", Run = (Action)delegate { ToggleTodo(); } },
                new { Label = "☑ Tick", Name = "Tick", Tip = "Tick or untick the to-do  (Ctrl+D)", Run = (Action)delegate { ToggleDone(); } },
            };
            foreach (var tool in tools)
            {
                var run = tool.Run;
                var b = MemoTheme.Button(tool.Label, tool.Name); b.Focusable = false; b.ToolTip = tool.Tip; b.Height = 32; b.MinWidth = 32; b.Padding = new Thickness(9, 0, 9, 0);
                b.Margin = new Thickness(tool.Name == "Bold" ? 18 : 0, 0, 4, 0);
                if (tool.Name == "Bold") b.FontWeight = FontWeights.Bold;
                if (tool.Name == "Italic") b.FontStyle = FontStyles.Italic;
                if (tool.Name == "Underline") b.Content = new TextBlock { Text = "U", TextDecorations = TextDecorations.Underline };
                System.Windows.Automation.AutomationProperties.SetAutomationId(b, "NoteFormat-" + tool.Name);
                b.Click += delegate { run(); };
                palette.Children.Add(b);
            }
            // ∑ Calc: makes the note a calculation (or plain again) without having to know the keywords. It names the mode in use.
            // In the heading, beside the close button: there is room for its longest name ("∑ Average") at every size and font.
            calcButton = MemoTheme.Button("∑ Calc", "Calculate"); calcButton.Focusable = false; calcButton.Height = 30; calcButton.Padding = new Thickness(10, 0, 10, 0);
            calcButton.HorizontalAlignment = HorizontalAlignment.Right; calcButton.VerticalAlignment = VerticalAlignment.Top; calcButton.Margin = new Thickness(0, 0, 38, 0);
            calcButton.ToolTip = "Work this note out: Math answers each line, Sum adds up the numbers, Average averages them; units convert too (5 km in miles)";
            System.Windows.Automation.AutomationProperties.SetAutomationId(calcButton, "NoteFormat-Calc");
            calcButton.Click += delegate { ShowCalcMenu(); };
            heading.Children.Add(calcButton);
            Grid.SetRow(palette, 1); content.Children.Add(palette);
            Grid.SetRow(pictureRow, 2); content.Children.Add(pictureRow);
            var writing = new Grid { Margin = new Thickness(0, 0, 0, 15) };
            writing.Children.Add(new Border { Background = MemoTheme.Brush(Theme.Alpha("16", Theme.White)), CornerRadius = Theme.Radius(6) });
            var rules = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
            // One rule under each line of writing: the text starts 10 below the top of the box.
            for (int i = 0; i < 14; i++) rules.Children.Add(new Line { X1 = 11, X2 = Width - 79, Y1 = 9 + lineHeight * (i + 1), Y2 = 9 + lineHeight * (i + 1), Stroke = MemoTheme.Brush(Theme.Alpha("18", Theme.Text)), StrokeThickness = 0.5 });
            writing.Children.Add(rules);
            input = new TextBox { Text = note.Text, MaxLength = Store.MaxNoteText, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(11, 10, 10, 10),
                FontSize = Theme.Size(15 * scale), Foreground = MemoTheme.Brush(Theme.Light), CaretBrush = MemoTheme.Brush(MemoTheme.Accent(color)) };
            TextBlock.SetLineHeight(input, lineHeight);
            System.Windows.Automation.AutomationProperties.SetAutomationId(input, "NoteText");
            System.Windows.Automation.AutomationProperties.SetName(input, "Note text");
            writing.Children.Add(input);
            placeholder = new TextBlock { Text = "Write down a task, an idea,\nor a small goal for today…\n\n∑ Calc (top right) works a note out: sums, averages, maths, units.", FontSize = Theme.Size(15 * scale), LineHeight = lineHeight,
                Foreground = MemoTheme.Brush(Theme.Mid), IsHitTestVisible = false, Margin = new Thickness(13, 10, 10, 0), Visibility = note.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed };
            writing.Children.Add(placeholder);
            writing.Children.Add(results);
            total = new TextBlock { FontSize = Theme.Size(13 * scale), FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 16, 8), IsHitTestVisible = false };
            writing.Children.Add(total);
            input.TextChanged += delegate { ScheduleResults(); UpdateCalcButton(); };
            Loaded += delegate { ScheduleResults(); UpdateCalcButton(); }; // The note's own results and kind, as soon as it opens.
            input.SizeChanged += delegate { ScheduleResults(); };
            input.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(delegate { ScheduleResults(); }));
            // In a calculation note, "=" at the end of a line fills in its result; on any other line it is just "=".
            input.PreviewTextInput += delegate(object sender, TextCompositionEventArgs e) { if (e.Text == "=" && Complete()) e.Handled = true; };
            input.TextChanged += delegate { placeholder.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; MarkDirty(); };
            Grid.SetRow(writing, 3); content.Children.Add(writing);
            var footer = new Grid();
            status = MemoTheme.Text("Saved automatically on this PC", 11, Theme.Text); status.VerticalAlignment = VerticalAlignment.Center; status.MaxWidth = 290; status.TextWrapping = TextWrapping.Wrap; status.HorizontalAlignment = HorizontalAlignment.Left; footer.Children.Add(status);
            var done = MemoTheme.Button("Done  ↵", "Finish editing the note"); done.Width = 92; done.HorizontalAlignment = HorizontalAlignment.Right;
            done.Background = MemoTheme.Brush(Theme.Panel); done.Foreground = MemoTheme.Brush(Theme.Light); done.BorderBrush = MemoTheme.Brush(Theme.Alpha("70", Theme.Mid)); done.BorderThickness = new Thickness(1);
            System.Windows.Automation.AutomationProperties.SetAutomationId(done, "NoteDone");
            done.ToolTip = "Ctrl+Enter or Esc closes and saves"; done.Click += delegate { Close(); }; footer.Children.Add(done);
            Grid.SetRow(footer, 4); content.Children.Add(footer);
            Content = outer;
            UpdatePaper(); UpdatePicture();
            saveDelay.Tick += delegate { saveDelay.Stop(); FlushChanges(); };
            Loaded += delegate { Activate(); input.Focus(); input.CaretIndex = input.Text.Length; };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                // Ctrl+V or Shift+Insert with a picture on the clipboard attaches it; with text it pastes as usual.
                bool paste = (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control) || (e.Key == Key.Insert && Keyboard.Modifiers == ModifierKeys.Shift);
                if (paste && PastePicture()) { e.Handled = true; return; }
                if (Keyboard.Modifiers == ModifierKeys.Control && input.IsKeyboardFocused)
                {
                    Action format = e.Key == Key.B ? delegate { Wrap("**"); } : e.Key == Key.I ? delegate { Wrap("*"); } : e.Key == Key.U ? delegate { Wrap("__"); }
                        : e.Key == Key.T ? delegate { ToggleTodo(); } : e.Key == Key.D ? (Action)delegate { ToggleDone(); } : null;
                    if (format != null) { e.Handled = true; format(); return; }
                }
                if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None && input.IsKeyboardFocused && ContinueTodo()) { e.Handled = true; return; }
                if (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None && input.IsKeyboardFocused && Complete()) { e.Handled = true; return; }
                if (e.Key == Key.Escape || (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0)) { e.Handled = true; Close(); }
            };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (!FlushChanges()) e.Cancel = true; };
            Closed += delegate { saveDelay.Stop(); };
        }
        void ShowCalcMenu()
        {
            var mode = NoteMath.ModeOf(input.Text);
            var menu = new ContextMenu { PlacementTarget = calcButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            var choices = new[]
            {
                new { Word = "math", Label = "Math — answer each line  (12*3, 15% of 80, 5 km in miles)", Mode = NoteMath.Mode.Math },
                new { Word = "sum", Label = "Sum — add up the last number on each line", Mode = NoteMath.Mode.Sum },
                new { Word = "avg", Label = "Average — average the last number on each line", Mode = NoteMath.Mode.Avg },
            };
            foreach (var choice in choices)
            {
                string word = choice.Word;
                var item = new MenuItem { Header = choice.Label, IsChecked = mode == choice.Mode };
                item.Click += delegate { SetCalculation(word); };
                menu.Items.Add(item);
            }
            menu.Items.Add(new Separator());
            var plain = new MenuItem { Header = "Plain note", IsEnabled = mode != NoteMath.Mode.None };
            plain.Click += delegate { SetCalculation(null); };
            menu.Items.Add(plain);
            menu.IsOpen = true;
        }
        // Puts the keyword ("math", "sum" or "avg") on the note's first line, replacing any there; null makes it a plain note again.
        internal void SetCalculation(string keyword)
        {
            string text = input.Text; int caret = input.CaretIndex;
            string rest = text;
            if (NoteMath.ModeOf(text) != NoteMath.Mode.None)
            {
                int end = text.IndexOf('\n');
                rest = end < 0 ? "" : text.Substring(end + 1);
            }
            string next = keyword == null ? rest : keyword + "\r\n" + rest;
            if (next.Length > Store.MaxNoteText) return;
            input.Text = next;
            input.Select(Math.Max(0, Math.Min(next.Length, caret + next.Length - text.Length)), 0);
            input.Focus();
        }
        void UpdateCalcButton()
        {
            if (calcButton == null) return;
            var mode = NoteMath.ModeOf(input.Text);
            calcButton.Content = mode == NoteMath.Mode.Math ? "∑ Math" : mode == NoteMath.Mode.Sum ? "∑ Sum" : mode == NoteMath.Mode.Avg ? "∑ Average" : "∑ Calc";
        }
        void ScheduleResults()
        {
            if (resultsPending) return;
            resultsPending = true;
            // After layout, so the character positions are those of the text as it now wraps.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate { resultsPending = false; UpdateResults(); }));
        }
        internal void UpdateResults()
        {
            results.Children.Clear(); total.Text = "";
            var outcome = NoteMath.Evaluate(input.Text);
            if (outcome.Mode == NoteMath.Mode.None) return;
            var brush = MemoTheme.Brush(MemoTheme.Accent(color));
            total.Foreground = brush; total.Text = outcome.Total ?? "";
            string[] lines = input.Text.Split('\n');
            int offset = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                int length = lines[i].TrimEnd('\r').Length;
                if (outcome.Lines[i] != null && length > 0)
                {
                    var end = input.GetRectFromCharacterIndex(offset + length - 1, true);
                    if (!end.IsEmpty && end.Bottom > 0 && end.Top < input.ActualHeight)
                    {
                        var shown = new TextBlock { Text = "= " + outcome.Lines[i], FontSize = input.FontSize, FontFamily = input.FontFamily, Foreground = brush, Opacity = 0.85 };
                        shown.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
                        Canvas.SetLeft(shown, Math.Max(end.Right + 12, input.ActualWidth - 20 - shown.DesiredSize.Width)); Canvas.SetTop(shown, end.Top);
                        results.Children.Add(shown);
                    }
                }
                offset += lines[i].Length + 1;
            }
        }
        // At the end of a line of a calculation note: writes " = result" there. False when there is nothing to fill in.
        internal bool Complete()
        {
            if (input.SelectionLength > 0) return false;
            string text = input.Text; int caret = input.CaretIndex;
            if (caret < text.Length && text[caret] != '\r' && text[caret] != '\n') return false;
            int lineIndex = 0; for (int i = 0; i < caret; i++) if (text[i] == '\n') lineIndex++;
            string value = NoteMath.CompleteLine(text, lineIndex);
            if (value == null) return false;
            bool spaced = caret > 0 && text[caret - 1] == ' ';
            string insert = (spaced ? "" : " ") + "= " + value;
            if (text.Length + insert.Length > Store.MaxNoteText) return false;
            input.SelectedText = insert; input.Select(caret + insert.Length, 0);
            return true;
        }
        // Puts the mark around the selection, or takes it off when the selection already has it.
        internal void Wrap(string mark)
        {
            int start = input.SelectionStart, length = input.SelectionLength;
            string chosen = input.SelectedText;
            bool wrapped = chosen.Length >= 2 * mark.Length + 1 && chosen.StartsWith(mark, StringComparison.Ordinal) && chosen.EndsWith(mark, StringComparison.Ordinal)
                && !(mark == "*" && chosen.StartsWith("**", StringComparison.Ordinal) && !chosen.StartsWith("***", StringComparison.Ordinal));
            if (wrapped) { input.SelectedText = chosen.Substring(mark.Length, chosen.Length - 2 * mark.Length); input.Select(start, length - 2 * mark.Length); }
            else if (input.Text.Length + 2 * mark.Length <= Store.MaxNoteText) { input.SelectedText = mark + chosen + mark; input.Select(start + mark.Length, length); }
            input.Focus();
        }
        // Start and length of the whole lines that the selection touches.
        void SelectedLines(out int start, out int length)
        {
            string text = input.Text;
            int from = input.SelectionStart, to = from + input.SelectionLength;
            if (to > from && text[to - 1] == '\n') to--; // A selection that ends at the start of a line does not include that line.
            start = from == 0 ? 0 : text.LastIndexOf('\n', from - 1) + 1;
            int end = text.IndexOf('\n', to);
            if (end < 0) end = text.Length; else if (end > start && text[end - 1] == '\r') end--;
            length = Math.Max(0, end - start);
        }
        void ReplaceLines(Func<string, string> change)
        {
            int start, length; SelectedLines(out start, out length);
            string[] lines = input.Text.Substring(start, length).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                bool cr = lines[i].EndsWith("\r", StringComparison.Ordinal);
                lines[i] = change(cr ? lines[i].Substring(0, lines[i].Length - 1) : lines[i]) + (cr ? "\r" : "");
            }
            string result = String.Join("\n", lines);
            if (input.Text.Length - length + result.Length > Store.MaxNoteText) return;
            input.Select(start, length); input.SelectedText = result; input.Select(start + result.Length, 0); input.Focus();
        }
        internal void ToggleTodo()
        {
            int start, length; SelectedLines(out start, out length);
            string rest; bool all = true;
            foreach (string line in input.Text.Substring(start, length).Split('\n'))
                if (line.Trim().Length > 0 && NoteMarkup.Todo(line, out rest) == 0) all = false;
            ReplaceLines(delegate(string line)
            {
                string body; int state = NoteMarkup.Todo(line, out body);
                if (all) return state == 0 ? line : body;
                return state != 0 || (line.Trim().Length == 0 && length > 0) ? line : "[ ] " + line;
            });
        }
        internal void ToggleDone()
        {
            ReplaceLines(delegate(string line)
            {
                string body; int state = NoteMarkup.Todo(line, out body);
                return state == 0 ? line : NoteMarkup.Toggle(line, 0);
            });
        }
        // Enter at the end of a to-do starts the next one; Enter on an empty to-do ends the list.
        internal bool ContinueTodo()
        {
            if (input.SelectionLength > 0) return false;
            string text = input.Text; int caret = input.SelectionStart;
            int start = caret == 0 ? 0 : text.LastIndexOf('\n', caret - 1) + 1;
            int end = text.IndexOf('\n', caret); if (end < 0) end = text.Length; else if (end > start && text[end - 1] == '\r') end--;
            string body;
            if (caret != end || NoteMarkup.Todo(text.Substring(start, end - start), out body) == 0) return false;
            if (body.Trim().Length == 0) { input.Select(start, end - start); input.SelectedText = ""; input.Select(start, 0); return true; }
            if (text.Length + 6 > Store.MaxNoteText) return false;
            input.SelectedText = "\r\n[ ] "; input.Select(caret + 6, 0);
            return true;
        }
        void UpdatePaper()
        {
            paper.Background = MemoTheme.Paper(color); paper.BorderBrush = MemoTheme.Brush(MemoTheme.Edge(color));
            if (input != null) input.CaretBrush = MemoTheme.Brush(MemoTheme.Accent(color));
            foreach (var entry in swatches) entry.Value.Stroke = MemoTheme.Brush(entry.Key == color ? MemoTheme.Accent(color) : Theme.Alpha("40", Theme.White));
        }
        void UpdatePicture()
        {
            pictureRow.Children.Clear();
            var thumb = store.NoteThumb(memo);
            if (thumb == null)
            {
                pictureRow.Children.Add(new TextBlock { Text = "Copy an image, then Ctrl+V here to add a picture", FontSize = Theme.Size(11), Foreground = MemoTheme.Brush(Theme.Mid) });
                return;
            }
            pictureRow.Children.Add(new Border { BorderBrush = MemoTheme.Brush(Theme.Rule), BorderThickness = new Thickness(1), Background = MemoTheme.Brush(Theme.Inset),
                Child = new Image { Source = thumb, Height = 78, MaxWidth = 300, Stretch = Stretch.Uniform } });
            var remove = MemoTheme.Button("Remove picture", "Remove the picture from this note"); remove.Margin = new Thickness(12, 0, 0, 0); remove.VerticalAlignment = VerticalAlignment.Bottom;
            System.Windows.Automation.AutomationProperties.SetAutomationId(remove, "NoteRemovePicture");
            remove.Click += delegate
            {
                try { store.SetNotePicture(memo, null); status.Text = "Picture removed"; status.Foreground = MemoTheme.Brush(Theme.Text); }
                catch (Exception ex) { store.Log("Remove note picture", ex); status.Text = "Could not remove the picture. Try again shortly."; status.Foreground = MemoTheme.Brush(Theme.Warn); }
                UpdatePicture(); input.Focus();
            };
            pictureRow.Children.Add(remove);
        }
        // True when the clipboard held a picture, whether or not it could be attached.
        bool PastePicture()
        {
            System.Windows.Media.Imaging.BitmapSource image;
            try { image = NotePictures.FromClipboard(); }
            catch (Exception ex) { store.Log("Read picture from clipboard", ex); return false; }
            if (image == null) return false;
            try { store.SetNotePicture(memo, image); status.Text = "Picture added"; status.Foreground = MemoTheme.Brush(Theme.Text); }
            catch (Exception ex) { store.Log("Add note picture", ex); status.Text = "Could not add that picture. Try again shortly."; status.Foreground = MemoTheme.Brush(Theme.Warn); }
            UpdatePicture();
            return true;
        }
        void MarkDirty()
        {
            dirty = true; status.Text = "Saving…"; status.Foreground = MemoTheme.Brush(Theme.Text);
            saveDelay.Stop(); saveDelay.Start();
        }
        internal bool FlushChanges()
        {
            saveDelay.Stop();
            if (!dirty) return true;
            try
            {
                store.UpdateNote(memo, input.Text, color);
                dirty = false; status.Text = "Saved · " + DateTime.Now.ToString("HH:mm"); status.Foreground = MemoTheme.Brush(Theme.Text); return true;
            }
            catch (Exception ex)
            {
                store.Log("Save memo", ex); status.Text = "Could not save. Your text is kept in this window; try again shortly."; status.Foreground = MemoTheme.Brush(Theme.Warn); return false;
            }
        }
        internal TextBox Input { get { return input; } }
        // For --editor-snapshot: a picture of the editor, to check a theme without opening it by hand.
        internal void Snapshot(string file)
        {
            UpdateLayout(); UpdateResults(); UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(this);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using (var stream = File.Create(file)) encoder.Save(stream);
        }
        internal bool IsBlank { get { return String.IsNullOrWhiteSpace(input.Text) && memo.Picture == null; } }
    }

    sealed partial class Shelf
    {
        ToolTip noteTip;
        MemoEditor memoEditor;
        internal List<object> Deck()
        {
            return store.Items.Cast<object>().Concat(store.Notes.Cast<object>()).Concat(store.Links.Cast<object>())
                .OrderByDescending(x => Store.RankOf(x)).ThenByDescending(x => Store.CreatedOf(x)).ToList();
        }
        internal void NewNote()
        {
            if (dialogOpen || capturePending) return;
            try { var note = store.AddNote("", "Butter"); Reveal(); EditNote(note, true); }
            catch (Exception ex) { store.Log("Create memo", ex); Notify(ex.Message); }
        }
        internal void EditNote(Memo note, bool fresh)
        {
            if (dialogOpen || capturePending || !store.Notes.Contains(note)) return;
            CloseImagePreview(); CloseNotePreview();
            if (helpTip != null) helpTip.IsOpen = false;
            dialogOpen = true;
            try
            {
                memoEditor = new MemoEditor(store, note) { Owner = this };
                memoEditor.ShowDialog();
                if (fresh && memoEditor.IsBlank) store.RemoveNote(note);
            }
            catch (Exception ex) { store.Log("Edit memo", ex); Notify(ex.Message); }
            finally
            {
                memoEditor = null; dialogOpen = false;
                keepUntil = DateTime.Now.AddSeconds(3); lastInside = DateTime.Now; Reveal(); Render();
            }
        }
        void CopyNote(Memo note)
        {
            // A calculation note copies with its results filled in; others without their bold, italic and underline marks.
            string copied = NoteMath.ModeOf(note.Text) != NoteMath.Mode.None ? NoteMath.WithResults(note.Text) : NoteMarkup.Plain(note.Text);
            try { SetClipboard(new DataObject(DataFormats.UnicodeText, copied)); Flash(note, note.Picture != null ? "Text copied" : "Copied"); }
            catch (Exception ex) { store.Log("Copy memo", ex); Notify("Copy failed. Try again shortly."); }
        }
        void DeleteNote(Memo note)
        {
            // No confirmation dialog: the removal can be undone from the notice on the line.
            CloseNotePreview(); RemoveWithUndo(note);
        }
        void CloseNotePreview()
        {
            var current = noteTip; noteTip = null;
            if (current != null) current.IsOpen = false;
        }
        ToolTip NotePopup(Memo note, FrameworkElement target)
        {
            double scale = CardScale;
            double maxHeight = Math.Max(90, Math.Min(440 * scale, screen.WorkingArea.Height / Dpi() - 315 - CardDrop));
            double width = Math.Max(200, Math.Min(360 * scale, screen.WorkingArea.Width / Dpi() - 60));
            var content = new StackPanel { Width = width };
            var title = MemoTheme.Text("Note", 13 * scale, MemoTheme.Accent(note.Color)); title.Margin = new Thickness(0, 0, 0, 12); content.Children.Add(title);
            var picture = new Image { MaxHeight = 230 * scale, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12), Visibility = Visibility.Collapsed };
            content.Children.Add(picture);
            var text = MemoTheme.Text(String.IsNullOrWhiteSpace(note.Text) ? "Empty. Double-click to write something." : note.Text, 14 * scale, Theme.Light);
            text.TextWrapping = TextWrapping.Wrap; text.LineHeight = Math.Round(23 * scale);
            if (!String.IsNullOrWhiteSpace(note.Text)) NoteMarkup.Fill(text, note.Text, MemoTheme.Accent(note.Color), null);
            var scroll = new ScrollViewer { Content = text, MaxHeight = maxHeight, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            content.Children.Add(scroll);
            content.Children.Add(new TextBlock { Text = "Click to copy · Double-click to edit", FontSize = Theme.Size(11 * scale), Foreground = MemoTheme.Brush(Theme.Text), Margin = new Thickness(0, 13, 0, 0) });
            var tip = new ToolTip { Content = new Border { Child = content, Padding = new Thickness(18), CornerRadius = Theme.Radius(9), Background = MemoTheme.Paper(note.Color),
                    BorderBrush = MemoTheme.Brush(MemoTheme.Edge(note.Color)), BorderThickness = new Thickness(1) },
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), HasDropShadow = true,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, PlacementTarget = target,
                HorizontalOffset = (target.Width - width - 38) / 2, VerticalOffset = 10, FontFamily = FontFamily };
            System.Windows.Automation.AutomationProperties.SetName(tip, "Full note");
            tip.Opened += delegate
            {
                if (!HoverPreviewAllowed || !store.Notes.Contains(note)) { tip.IsOpen = false; return; }
                CloseNotePreview(); CloseImagePreview(); if (helpTip != null) helpTip.IsOpen = false; noteTip = tip;
                string file = store.NotePicturePath(note);
                if (file != null && picture.Source == null)
                {
                    try
                    {
                        if (File.Exists(file))
                        {
                            // At the size it is shown, not full resolution, and released again when the preview closes.
                            var thumb = store.NoteThumb(note);
                            double aspect = thumb == null ? 1.5 : thumb.PixelWidth / (double)Math.Max(1, thumb.PixelHeight), dpi = Math.Max(1, Dpi());
                            picture.Source = Store.Preview(file, (int)Math.Ceiling(width * dpi), (int)Math.Ceiling(230 * scale * dpi), aspect); picture.Visibility = Visibility.Visible;
                            if (String.IsNullOrWhiteSpace(note.Text)) scroll.Visibility = Visibility.Collapsed;
                        }
                    }
                    catch (Exception ex) { store.Log("Show note picture", ex); }
                }
            };
            tip.Closed += delegate { picture.Source = null; if (Object.ReferenceEquals(noteTip, tip)) noteTip = null; lastInside = DateTime.Now; };
            return tip;
        }
        // The picture of a note on its own, large. Shown while the pointer is on the picture part of a card that also has text.
        ToolTip NotePicturePopup(Memo note, FrameworkElement card, System.Windows.Media.Imaging.BitmapSource thumb)
        {
            double dpi = Dpi();
            double maxWidth = Math.Max(100, Math.Min(760, screen.WorkingArea.Width / dpi - 64));
            double maxHeight = Math.Max(80, Math.Min(460, screen.WorkingArea.Height / dpi - 300 - CardDrop));
            double aspect = thumb.PixelWidth / (double)Math.Max(1, thumb.PixelHeight);
            double width = Math.Min(maxWidth, maxHeight * aspect), height = width / aspect;
            var preview = new Image { Width = width, Height = height, Stretch = Stretch.Uniform };
            var tip = new ToolTip { Content = new Border { Child = preview, Padding = new Thickness(10), CornerRadius = Theme.Radius(9), Background = MemoTheme.Paper(note.Color),
                    BorderBrush = MemoTheme.Brush(MemoTheme.Edge(note.Color)), BorderThickness = new Thickness(1) },
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), HasDropShadow = true, IsHitTestVisible = false,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, PlacementTarget = card,
                HorizontalOffset = (card.Width - width - 22) / 2, VerticalOffset = 10, FontFamily = FontFamily };
            System.Windows.Automation.AutomationProperties.SetName(tip, "Note picture");
            tip.Opened += delegate
            {
                if (!HoverPreviewAllowed || Mouse.LeftButton == MouseButtonState.Pressed || !store.Notes.Contains(note)) { tip.IsOpen = false; return; }
                try
                {
                    string file = store.NotePicturePath(note);
                    if (file == null || !File.Exists(file)) { tip.IsOpen = false; return; }
                    CloseNotePreview(); CloseImagePreview(); if (helpTip != null) helpTip.IsOpen = false;
                    preview.Source = Store.Preview(file, (int)Math.Ceiling(maxWidth * dpi), (int)Math.Ceiling(maxHeight * dpi), aspect);
                    noteTip = tip;
                }
                catch (Exception ex) { tip.IsOpen = false; store.Log("Preview note picture", ex); }
            };
            tip.Closed += delegate
            {
                preview.Source = null; // Release the enlarged bitmap as soon as the pointer leaves.
                if (Object.ReferenceEquals(noteTip, tip)) noteTip = null; lastInside = DateTime.Now;
            };
            return tip;
        }
        void MakeNoteCard(Memo note, double center, double y, int index)
        {
            // The card size preset scales the card, its text and its spacing together.
            double scale = CardScale, width = CardSizes.NoteWidth(scale), height = CardSizes.NoteHeight(scale);
            double lean = Lean(new[] { -2.4, 1.6, -1.1, 2.2 }[index % 4]);
            var swing = new RotateTransform(lean, width / 2, -15);
            var grid = new Grid { Width = width, Height = height, Cursor = Cursors.Hand, RenderTransform = swing, Tag = note };
            System.Windows.Automation.AutomationProperties.SetName(grid, "Note " + (note.Text.Length > 30 ? note.Text.Substring(0, 30) : note.Text));
            System.Windows.Automation.AutomationProperties.SetAutomationId(grid, "NoteCard-" + note.Id);
            grid.Children.Add(new Border { Background = MemoTheme.Paper(note.Color), BorderBrush = MemoTheme.Brush(MemoTheme.Edge(note.Color)), BorderThickness = new Thickness(1), CornerRadius = Theme.Radius(3),
                Effect = new DropShadowEffect { Color = MemoTheme.Hex(Theme.Shadow), Opacity = 0.16, ShadowDepth = 4, BlurRadius = 9 } });
            grid.Children.Add(new Border { Height = 4, VerticalAlignment = VerticalAlignment.Top, Background = MemoTheme.Brush(MemoTheme.Accent(note.Color)), IsHitTestVisible = false });
            var details = new Grid { Margin = new Thickness(14 * scale, 14 * scale, 14 * scale, 11 * scale), IsHitTestVisible = false };
            details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            details.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var header = new Grid(); header.Children.Add(MemoTheme.Text("Note", 12 * scale, Theme.Mid));
            var date = MemoTheme.Text(note.Created.ToString("MMM d"), 12 * scale, Theme.Dim); date.HorizontalAlignment = HorizontalAlignment.Right; header.Children.Add(date);
            details.Children.Add(header);
            var body = MemoTheme.Text(String.IsNullOrWhiteSpace(note.Text) ? "Double-click to write…" : note.Text, 15 * scale, Theme.Light);
            body.TextWrapping = TextWrapping.Wrap; body.TextTrimming = TextTrimming.CharacterEllipsis; body.LineHeight = Math.Round(22 * scale);
            body.Margin = new Thickness(0, 9 * scale, 0, 5 * scale); body.ClipToBounds = true;
            if (!String.IsNullOrWhiteSpace(note.Text))
            {
                NoteMarkup.Fill(body, note.Text, MemoTheme.Accent(note.Color), delegate(int line)
                {
                    try { store.UpdateNote(note, NoteMarkup.Toggle(note.Text, line), note.Color); keepUntil = DateTime.Now.AddSeconds(3); }
                    catch (Exception ex) { store.Log("Tick to-do", ex); Notify("Could not save that. Try again shortly."); }
                });
                // The boxes are the only part of the text that takes clicks; everything else falls through to the card.
                if (NoteMarkup.HasTodo(note.Text)) details.IsHitTestVisible = true;
            }
            var thumb = store.NoteThumb(note);
            if (thumb == null) { Grid.SetRow(body, 1); details.Children.Add(body); }
            else
            {
                bool wordless = String.IsNullOrWhiteSpace(note.Text);
                var middle = new DockPanel { Margin = new Thickness(0, 8 * scale, 0, 4 * scale), LastChildFill = true };
                var frame = new Border { Height = Math.Round((wordless ? 88 : 50) * scale), ClipToBounds = true, Tag = "NotePicture", Background = MemoTheme.Brush(Theme.Inset),
                    Child = new Image { Source = thumb, Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                DockPanel.SetDock(frame, Dock.Top); middle.Children.Add(frame);
                if (wordless) body.Visibility = Visibility.Collapsed;
                else
                {
                    body.Margin = new Thickness(0, 5 * scale, 0, 0);
                    // With the pointer on the picture, the preview shows the picture alone; on the rest of the card it shows the whole note.
                    // The picture has to take the pointer for that. Clicks on it still travel up to the card.
                    details.IsHitTestVisible = true;
                    frame.ToolTip = NotePicturePopup(note, grid, thumb);
                    ToolTipService.SetInitialShowDelay(frame, Tuning.PreviewDelay(store.Settings, 280)); ToolTipService.SetBetweenShowDelay(frame, 100); ToolTipService.SetShowDuration(frame, 60000);
                    RegisterHoverPreview(frame);
                }
                middle.Children.Add(body);
                Grid.SetRow(middle, 1); details.Children.Add(middle);
            }
            var footer = MemoTheme.Text("Double-click to edit", 11 * scale, Theme.Dim); Grid.SetRow(footer, 2); details.Children.Add(footer);
            grid.Children.Add(details);
            AddHangingPeg(grid);
            var remove = MemoTheme.Button("×", "Delete note"); remove.Width = 21; remove.Height = 21; remove.FontSize = Theme.Size(16); remove.Padding = new Thickness(0); remove.Background = MemoTheme.Brush(Theme.Alpha("DD", Theme.Panel));
            remove.Tag = "DeleteButton";
            remove.HorizontalAlignment = HorizontalAlignment.Right; remove.VerticalAlignment = VerticalAlignment.Top; remove.Margin = new Thickness(0, -9, -8, 0); remove.Opacity = 0; remove.IsHitTestVisible = false;
            remove.Click += delegate { DeleteNote(note); }; grid.Children.Add(remove);
            var menu = new ContextMenu(); menu.Items.Add(Item("Edit note", delegate { EditNote(note, false); })); menu.Items.Add(Item("Copy text", delegate { CopyNote(note); }));
            if (note.Picture != null)
            {
                menu.Items.Add(Item("Copy picture", delegate { CopyNotePicture(note); }));
                menu.Items.Add(Item("Open picture", delegate { OpenNotePicture(note); }));
            }
            menu.Items.Add(Item(note.Kept ? "Stop keeping" : "Keep on the line", delegate { ToggleKeep(note); }));
            menu.Items.Add(new Separator()); menu.Items.Add(Item("Delete note", delegate { DeleteNote(note); }));
            menu.Opened += delegate { CloseNotePreview(); dialogOpen = true; }; menu.Closed += delegate { dialogOpen = false; lastInside = DateTime.Now; }; grid.ContextMenu = menu;
            grid.ToolTip = NotePopup(note, grid); ToolTipService.SetInitialShowDelay(grid, Tuning.PreviewDelay(store.Settings, 280)); ToolTipService.SetBetweenShowDelay(grid, 100); ToolTipService.SetShowDuration(grid, 60000);
            RegisterHoverPreview(grid);
            var breeze = new DoubleAnimation(lean - 0.45, lean + 0.45, TimeSpan.FromSeconds(4.4 + index * 0.2)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            Timeline.SetDesiredFrameRate(breeze, 24); RegisterHangingMotion(grid, swing, breeze);
            grid.MouseEnter += delegate { if (hoverEffectsPaused) return; remove.IsHitTestVisible = true; remove.Opacity = 1; SetHangingAnimation(swing, new DoubleAnimation(0, TimeSpan.FromMilliseconds(180))); };
            grid.MouseLeave += delegate { CloseNotePreview(); if (hoverEffectsPaused) return; remove.IsHitTestVisible = false; remove.Opacity = 0; SetHangingAnimation(swing, breeze); };
            Place(grid, center - width / 2, y);
        }
    }
    static class MemoTests
    {
        static void Check(bool passed, string text) { if (!passed) throw new Exception("Memo test failed: " + text); }
        internal static int Run(string root)
        {
            if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()) throw new InvalidOperationException("Memo tests require an empty isolated directory.");
            var report = new List<string>();
            var store = new Store(root);
            Check(store.Notes.Count == 0, "Existing image store starts without notes");
            string body = "明天记得带相机 📷\n\n买一束花，给生活留一点颜色。\n<b>文字按原样保存</b>";
            var memo = store.AddNote(body, "Mint");
            string id = memo.Id; DateTime created = memo.Created;
            var restored = new Store(root);
            Check(restored.Notes.Count == 1 && restored.Notes[0].Text == body && restored.Notes[0].Color == "Mint" && restored.Notes[0].Id == id, "Unicode, multiline and paper color survive restart");
            report.Add("PASS: Unicode, emoji, multiline text and paper color persist across restart");
            store.UpdateNote(memo, body + "\n完成整理", "Lavender");
            Check(memo.Id == id && memo.Created == created && new Store(root).Notes[0].Color == "Lavender", "Editing preserves identity and chronological position");
            report.Add("PASS: editing preserves creation order, ID and chosen color");
            string textBefore = memo.Text, colorBefore = memo.Color;
            string notesFile = System.IO.Path.Combine(root, "notes.json");
            byte[] durableBefore = File.ReadAllBytes(notesFile);
            using (var locked = File.Open(notesFile + ".tmp", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                bool failed = false; try { store.UpdateNote(memo, "not committed", "Butter"); } catch (IOException) { failed = true; }
                Check(failed && memo.Text == textBefore && memo.Color == colorBefore && File.ReadAllBytes(notesFile).SequenceEqual(durableBefore), "Failed save preserves memory and durable note");
                failed = false; try { store.AddNote("not committed", "Butter"); } catch (IOException) { failed = true; }
                Check(failed && store.Notes.Count == 1, "Failed addition rolled back");
                failed = false; try { store.RemoveNote(memo); } catch (IOException) { failed = true; }
                Check(failed && store.Notes.Contains(memo), "Failed deletion rolled back");
            }
            report.Add("PASS: interrupted saves, additions and deletions preserve existing notes");
            var second = store.AddNote("周末出门走走", "unknown");
            Check(second.Color == "Butter", "Unknown color uses safe default");
            for (int i = 0; i < Store.MaxItems + 1; i++) store.Add(Program.Demo("MEMO TEST " + i, "截图与便签", i % 2 == 0 ? Theme.Light : Theme.Light), "fixture");
            Check(store.Items.Count == Store.MaxItems && store.Notes.Count == 2, "Image retention leaves notes intact");
            store.Clear();
            Check(store.Items.Count == 0 && store.Notes.Count == 2 && new Store(root).Notes.Count == 2, "Clearing screenshots preserves notes");
            report.Add("PASS: notes survive the 60-image retention limit and clearing screenshots");
            store.RemoveNote(second);
            Check(new Store(root).Notes.Count == 1 && File.Exists(notesFile), "Delete only selected memo");
            bool tooLong = false; try { store.UpdateNote(memo, new string('x', Store.MaxNoteText + 1), "Mint"); } catch (InvalidOperationException) { tooLong = true; }
            Check(tooLong && memo.Text == textBefore, "Oversize edit preserves saved text");
            report.Add("PASS: selected-note deletion and oversize-input validation preserve other content");
            string corruptRoot = System.IO.Path.Combine(root, "corrupt-notes"); Directory.CreateDirectory(corruptRoot);
            string corruptFile = System.IO.Path.Combine(corruptRoot, "notes.json"); File.WriteAllText(corruptFile, "{broken-notes");
            var damaged = new Store(corruptRoot); bool refused = false;
            try { damaged.AddNote("replacement", "Butter"); } catch (InvalidOperationException) { refused = true; }
            Check(refused && damaged.Notes.Count == 0 && File.ReadAllText(corruptFile) == "{broken-notes", "Unreadable notes never overwritten");
            report.Add("PASS: unreadable notes file is preserved rather than overwritten");
            string rest;
            Check(NoteMarkup.Todo("[ ] milk", out rest) == 1 && rest == "milk" && NoteMarkup.Todo("- [x] done", out rest) == 2 && rest == "done" && NoteMarkup.Todo("[X]", out rest) == 2 && rest == ""
                && NoteMarkup.Todo("[y] no", out rest) == 0 && NoteMarkup.Todo("[ ]tight", out rest) == 0 && NoteMarkup.Todo("text [ ] later", out rest) == 0, "To-do lines recognised");
            Check(NoteMarkup.Toggle("a\r\n[ ] b\r\n- [x] c", 1) == "a\r\n[x] b\r\n- [x] c" && NoteMarkup.Toggle("a\n[ ] b\n- [x] c", 2) == "a\n[ ] b\n- [ ] c" && NoteMarkup.Toggle("a\n[ ] b", 0) == "a\n[ ] b" && NoteMarkup.Toggle("x", 9) == "x", "Ticking changes one box only");
            Check(NoteMarkup.Plain("**bold** and *it* and __un__, 2*3*4, a_b__c, [x] **done**") == "bold and it and un, 2*3*4, a_b__c, [x] done" && NoteMarkup.Plain("** spaced **") == "** spaced **" && NoteMarkup.Plain(body) == body, "Marks stripped for copying, other text untouched");
            var drawn = new TextBlock(); NoteMarkup.Fill(drawn, "**b** *i* __u__\r\n[x] one\n[ ] two", Theme.Accent, null);
            Check(drawn.Inlines.Count == 11 && drawn.Inlines.OfType<System.Windows.Documents.Run>().Count(x => x.Text == "☑ " || x.Text == "☐ ") == 2 && drawn.Inlines.OfType<System.Windows.Documents.LineBreak>().Count() == 2
                && ((System.Windows.Documents.Span)drawn.Inlines.FirstInline).FontWeight == FontWeights.Bold, "Marks drawn as bold, italic, underline and boxes");
            report.Add("PASS: to-do lines, ticking, mark stripping and drawing");
            var draft = store.AddNote("", "Butter");
            var editor = new MemoEditor(store, draft); var box = editor.Input;
            box.Text = "one two"; box.Select(4, 3); editor.Wrap("**");
            Check(box.Text == "one **two**" && box.SelectedText == "two", "Bold wraps the selection");
            box.Select(4, 7); editor.Wrap("**");
            Check(box.Text == "one two" && box.SelectedText == "two", "Bold again unwraps");
            box.Select(0, 3); editor.Wrap("*"); box.Select(box.Text.Length, 0); editor.Wrap("__");
            Check(box.Text == "*one* two____" && box.SelectionStart == box.Text.Length - 2, "Italic wraps; underline with no selection leaves the caret inside");
            box.Text = "milk\r\neggs\r\ntea"; box.Select(1, 8); editor.ToggleTodo();
            Check(box.Text == "[ ] milk\r\n[ ] eggs\r\ntea", "To-do marks the selected lines");
            box.Select(2, 0); editor.ToggleDone();
            Check(box.Text == "[x] milk\r\n[ ] eggs\r\ntea", "Tick marks the caret line");
            box.Select(18, 0); Check(editor.ContinueTodo() && box.Text == "[x] milk\r\n[ ] eggs\r\n[ ] \r\ntea" && box.SelectionStart == 24, "Enter continues the list");
            Check(editor.ContinueTodo() && box.Text == "[x] milk\r\n[ ] eggs\r\n\r\ntea" && box.SelectionStart == 20, "Enter on an empty to-do ends the list");
            box.Select(box.Text.Length, 0); Check(!editor.ContinueTodo(), "Enter on an ordinary line is left alone");
            box.Select(0, 18); editor.ToggleTodo();
            Check(box.Text == "milk\r\neggs\r\n\r\ntea", "To-do again removes the boxes");
            Check(editor.FlushChanges() && new Store(root).Notes.Any(x => x.Id == draft.Id && x.Text == box.Text), "Formatted text is saved as typed");
            editor.Close(); store.RemoveNote(draft);
            report.Add("PASS: editor bold/italic/underline wrap and unwrap; to-do, tick and list continuation");
            Check(store.Settings.CardSize == 0 && CardSizes.NoteWidth(CardSizes.Scale(store.Settings)) == 182 && CardSizes.NoteHeight(CardSizes.Scale(store.Settings)) == 153, "Notes start at the original size");
            store.Settings.CardSize = 2; store.SaveSettings();
            Check(new Store(root).Settings.CardSize == 2 && CardSizes.NoteWidth(CardSizes.Scale(2)) == 273 && CardSizes.NoteHeight(CardSizes.Scale(2)) == 230, "Card size persists across restart");
            Check(File.ReadAllText(System.IO.Path.Combine(root, "settings.json")).Contains("\"NoteSize\":2"), "Card size is saved under its first name, NoteSize");
            var large = new MemoEditor(store, store.Notes[0]);
            Check(large.Input.FontSize == Theme.Size(15 * 1.5) && TextBlock.GetLineHeight(large.Input) == 38, "Large notes write larger in the editor");
            large.Close();
            store.Settings.CardSize = 9; store.SaveSettings();
            Check(new Store(root).Settings.CardSize == 0, "Unknown card size reads as the original");
            store.Settings.CardSize = 0; store.SaveSettings();
            var maths = new[]
            {
                new[] { "12*(3+4)", "84" }, new[] { "2^10", "1024" }, new[] { "2^3^2", "512" }, new[] { "-3^2", "-9" }, new[] { "15% of 80", "12" }, new[] { "50%", "0.5" },
                new[] { "sqrt(2)", "1.414214" }, new[] { "max(1,234)", "234" }, new[] { "1,234 + 1", "1235" }, new[] { "1/0", null }, new[] { "hello there", null }, new[] { "42", null },
                new[] { "12*(3+4) = 84", null }, new[] { "round(2.567, 2)", "2.57" }, new[] { "(1)*(2)*(3)", "6" },
                new[] { "5 km in miles", "3.106856 miles" }, new[] { "72 f to c", "22.222222 c" }, new[] { "100 °C in °F", "212 °F" }, new[] { "0 c in k", "273.15 k" },
                new[] { "3 cups in ml", "709.764709 ml" }, new[] { "1.5 GB in MB", "1500 MB" }, new[] { "90 min in h", "1.5 h" }, new[] { "5 in in cm", "12.7 cm" },
                new[] { "2 fl oz to ml", "59.147059 ml" }, new[] { "60 mph in km/h", "96.56064 km/h" }, new[] { "5 kg in m", null }, new[] { "1 GiB in MB", "1073.741824 MB" },
            };
            foreach (var pair in maths)
            {
                var got = NoteMath.Evaluate("math\n" + pair[0]).Lines[1];
                Check(got == pair[1], "math: " + pair[0] + " gave " + (got ?? "nothing") + ", expected " + (pair[1] ?? "nothing"));
            }
            var withVariables = NoteMath.Evaluate("Math\nx = 5\nx*3\ny = x + 1\ny^2");
            Check(withVariables.Lines[1] == null && withVariables.Lines[2] == "15" && withVariables.Lines[3] == "6" && withVariables.Lines[4] == "36", "Variables carry to later lines");
            var sum = NoteMath.Evaluate("sum\ncoffee 4.50\nlunch 12\njust words\nbus 2,50\npaid Nov 25\ntaxi - 3\nrefund -1\nrent 1,234.50");
            Check(sum.Total == "Sum: 1253", "sum: " + sum.Total); // 4.5 + 12 + 3 - 1 + 1234.5; "2,50" and "Nov 25" do not count
            Check(NoteMath.Evaluate("avg\n4.5\n12").Total == "Average: 8.25" && NoteMath.Evaluate("avg\nnothing").Total == "Average: none", "avg");
            Check(NoteMath.ModeOf("mathematics\n1+1") == NoteMath.Mode.None && NoteMath.Evaluate("notes\n5 km in miles\n2+2").Lines.All(x => x == null), "Ordinary notes are never calculated");
            Check(NoteMath.WithResults("math\n2+2\nx = 3\n1+1 = 2") == "math\r\n2+2 = 4\r\nx = 3\r\n1+1 = 2" && NoteMath.WithResults("sum\na 1\nb 2") == "sum\r\na 1\r\nb 2\r\nSum: 3", "Copied with results");
            Check(NoteMath.CompleteLine("math\nx = 4\nx", 2) == null && NoteMath.CompleteLine("math\nx = 4\nx*2", 2) == "8", "A bare name is not completed; an expression is");
            var calcDraft = store.AddNote("math\r\n12*(3+4)", "Butter");
            var calcEditor = new MemoEditor(store, calcDraft); var calcBox = calcEditor.Input;
            calcBox.Select(calcBox.Text.Length, 0);
            Check(calcEditor.Complete() && calcBox.Text == "math\r\n12*(3+4) = 84" && calcBox.SelectionStart == calcBox.Text.Length, "Typing = at the end of a line fills in its result");
            Check(!calcEditor.Complete(), "A line already answered is left alone");
            calcBox.Select(4, 0); Check(!calcEditor.Complete(), "The keyword line is not completed");
            calcEditor.FlushChanges(); calcEditor.Close(); store.RemoveNote(calcDraft);
            var drawnCalc = new TextBlock(); NoteMarkup.Fill(drawnCalc, "sum\na 1\nb 2", Theme.Accent, null);
            Check(drawnCalc.Inlines.OfType<System.Windows.Documents.Run>().Skip(1).First().Text.Trim() == "Sum: 3", "The card shows the total on the keyword line");
            Check(NoteMath.ModeOf("Average\n1") == NoteMath.Mode.Avg, "average works as a keyword too");
            var calcPlain = store.AddNote("coffee 4", "Butter");
            var calcSwitch = new MemoEditor(store, calcPlain); var switchBox = calcSwitch.Input;
            calcSwitch.SetCalculation("sum"); Check(switchBox.Text == "sum\r\ncoffee 4", "Calc makes a sum note: " + switchBox.Text);
            calcSwitch.SetCalculation("math"); Check(switchBox.Text == "math\r\ncoffee 4", "Choosing another kind replaces the keyword");
            calcSwitch.SetCalculation(null); Check(switchBox.Text == "coffee 4", "Plain note takes the keyword off");
            calcSwitch.FlushChanges(); calcSwitch.Close(); store.RemoveNote(calcPlain);
            report.Add("PASS: math, sum, avg and unit conversions in notes; variables; ambiguous commas and dates skipped; ordinary notes untouched; = autocomplete; copy and card show results");
            report.Add("PASS: card size choice persists (saved as NoteSize), unknown sizes fall back to the original, the note editor follows it");
            File.WriteAllLines(System.IO.Path.Combine(root, "memo-results.txt"), report, Encoding.UTF8);
            return 0;
        }
    }
}
