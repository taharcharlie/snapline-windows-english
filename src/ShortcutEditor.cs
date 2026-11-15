using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Snapline
{
    sealed class ShortcutEditor : Window
    {
        readonly TextBox input;
        readonly TextBlock status;
        readonly Button save;
        readonly Func<ShortcutSpec, string> apply;
        ShortcutSpec candidate;
        internal ShortcutSpec Candidate { get { return candidate; } }
        internal ShortcutEditor(ShortcutSpec current, Func<ShortcutSpec, string> saveChoice)
        {
            apply = saveChoice; candidate = current;
            Title = "Custom shortcut · Snapline";
            Width = 484; Height = 382; ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false; WindowStyle = WindowStyle.None;
            AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = Theme.Font; UseLayoutRounding = true;
            var outer = new Grid { Margin = new Thickness(14) };
            var panel = new Border { CornerRadius = Theme.Radius(14), Background = MemoTheme.Brush(Theme.Panel),
                BorderBrush = MemoTheme.Brush(Theme.Rule), BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { Color = MemoTheme.Hex(Theme.Shadow), BlurRadius = 24, ShadowDepth = 6, Opacity = 0.19 } };
            outer.Children.Add(panel);
            var content = new Grid { Margin = new Thickness(25, 22, 25, 20) };
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            outer.Children.Add(content);
            var header = new Grid { Margin = new Thickness(0, 0, 0, 17) };
            var heading = new StackPanel();
            heading.Children.Add(MemoTheme.Text("SNAPLINE  /  SHORTCUT", 10, Theme.Mid));
            var title = MemoTheme.Text("Custom shortcut", 23, Theme.Light); title.Margin = new Thickness(0, 6, 0, 0); heading.Children.Add(title);
            heading.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { if (e.ClickCount == 1) DragMove(); };
            header.Children.Add(heading);
            var close = MemoTheme.Button("×", "Cancel shortcut setup");
            close.Width = 30; close.Height = 30; close.FontSize = 20; close.Padding = new Thickness(0);
            close.Foreground = MemoTheme.Brush(Theme.Mid); close.HorizontalAlignment = HorizontalAlignment.Right; close.VerticalAlignment = VerticalAlignment.Top;
            close.Click += delegate { Close(); }; header.Children.Add(close); content.Children.Add(header);
            var description = new TextBlock { Text = "Click the box below, then press the shortcut you want.",
                FontSize = 12, Foreground = MemoTheme.Brush(Theme.Text), Margin = new Thickness(0, 0, 0, 13) };
            Grid.SetRow(description, 1); content.Children.Add(description);
            input = new TextBox { IsReadOnly = true, Text = current == null ? "Click here, then press keys" : current.Label,
                FontSize = current == null ? 16 : 24, FontFamily = Theme.Font,
                TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                Height = 68, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(9),
                Foreground = MemoTheme.Brush(Theme.Text), Cursor = Cursors.Hand, IsReadOnlyCaretVisible = false };
            System.Windows.Automation.AutomationProperties.SetAutomationId(input, "ShortcutInput");
            System.Windows.Automation.AutomationProperties.SetName(input, "Press the capture shortcut");
            input.PreviewKeyDown += RecordKey;
            input.GotKeyboardFocus += delegate { input.Select(0, 0); };
            var keyPanel = new Border { Child = input, CornerRadius = Theme.Radius(10),
                Background = MemoTheme.Brush(Theme.Panel), BorderBrush = MemoTheme.Brush(Theme.Rule), BorderThickness = new Thickness(1.5) };
            Grid.SetRow(keyPanel, 2); content.Children.Add(keyPanel);
            var guidance = new StackPanel { Margin = new Thickness(0, 12, 0, 12) };
            guidance.Children.Add(new TextBlock { Text = "Ctrl, Shift, Alt + letter / digit / symbol / function key",
                FontSize = 10.5, Foreground = MemoTheme.Brush(Theme.Mid) });
            status = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 9, 0, 0) };
            System.Windows.Automation.AutomationProperties.SetAutomationId(status, "ShortcutStatus");
            guidance.Children.Add(status); Grid.SetRow(guidance, 3); content.Children.Add(guidance);
            var footer = new Grid();
            var cancel = MemoTheme.Button("Cancel", "Cancel shortcut setup"); cancel.Width = 75; cancel.Foreground = MemoTheme.Brush(Theme.Text);
            cancel.HorizontalAlignment = HorizontalAlignment.Left; cancel.Click += delegate { Close(); }; footer.Children.Add(cancel);
            save = MemoTheme.Button("Save and enable", "Save and enable the shortcut"); save.Width = 124; save.Height = 35;
            save.Background = MemoTheme.Brush(Theme.Accent); save.Foreground = MemoTheme.Brush(Theme.Bg); save.HorizontalAlignment = HorizontalAlignment.Right;
            System.Windows.Automation.AutomationProperties.SetAutomationId(save, "ShortcutSave");
            save.Click += delegate { SaveChoice(); }; footer.Children.Add(save);
            Grid.SetRow(footer, 4); content.Children.Add(footer); Content = outer;
            UpdateCandidate();
            Loaded += delegate { Activate(); input.Focus(); input.Select(0, 0); };
            PreviewKeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None) { e.Handled = true; Close(); }
            };
        }
        void RecordKey(object sender, KeyEventArgs e)
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Tab && (Keyboard.Modifiers == ModifierKeys.None || Keyboard.Modifiers == ModifierKeys.Shift)) return;
            if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None) { e.Handled = true; Close(); return; }
            e.Handled = true;
            uint modifiers = ShortcutSpec.ModifiersFrom(Keyboard.Modifiers);
            if (ShortcutSpec.IsModifier(key))
            {
                candidate = null;
                input.FontSize = 23; input.Text = ShortcutSpec.ModifierLabel(modifiers) + " + …";
                input.Select(0, 0);
                status.Text = "Press another key; if nothing shows, try a different combination.";
                status.Foreground = MemoTheme.Brush(Theme.Mid); save.IsEnabled = false; return;
            }
            Record(modifiers, (uint)KeyInterop.VirtualKeyFromKey(key));
        }
        internal void Record(uint modifiers, uint key)
        {
            string error = ShortcutSpec.Validate(modifiers, key);
            candidate = error == null ? new ShortcutSpec(modifiers, key) : null;
            input.FontSize = 24; input.Text = new ShortcutSpec(modifiers, key).Label;
            input.Select(0, 0);
            if (error == null) UpdateCandidate();
            else { status.Text = error; status.Foreground = MemoTheme.Brush(Theme.Warn); save.IsEnabled = false; }
        }
        void UpdateCandidate()
        {
            save.IsEnabled = candidate != null;
            status.Text = candidate == null ? "Checked for conflicts when saved; kept across restarts." : candidate.Warning;
            status.Foreground = MemoTheme.Brush(candidate != null && candidate.Warning.StartsWith("This shortcut") ? Theme.Warn : Theme.Mid);
        }
        internal void SaveChoice()
        {
            if (candidate == null) return;
            string error = apply(candidate);
            if (error == null) { DialogResult = true; }
            else { status.Text = error; status.Foreground = MemoTheme.Brush(Theme.Warn); input.Focus(); }
        }
        internal void Snapshot(string file)
        {
            UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(this);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = System.IO.File.Create(file)) encoder.Save(stream);
        }
    }
}
