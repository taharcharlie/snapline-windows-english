// Snapline: a local screenshot shelf for Windows. MIT License.
// One place for colours, fonts and corner shape. Palette: Base16 Ashes.
using System;
using System.Windows;
using System.Windows.Media;

namespace Snapline
{
    static class Theme
    {
        // Surfaces, darkest to lightest.
        internal const string Bg = "#1C2023", Panel = "#24292D", Inset = "#2B3136", Rule = "#393F45";
        // Text, quietest to strongest.
        internal const string Dim = "#565E65", Mid = "#747C84", Text = "#ADB3BA", Light = "#C7CCD1", White = "#F3F4F5";
        // Accents.
        internal const string Accent = "#C7C795", Green = "#95C7AE", Purple = "#AE95C7", Warn = "#C7AE95";
        internal const string Shadow = "#000000", Clear = "#00000000";

        internal const string FontName = "Barlow Condensed, Segoe UI";
        internal static readonly FontFamily Font = new FontFamily(FontName);

        // Barlow Condensed is narrow, so every size in the app is scaled up from the original design.
        internal const double FontScale = 1.2;
        internal static double Size(double points) { return Math.Round(points * FontScale, 1); }

        // "#RRGGBB" plus a two-digit hex alpha gives "#AARRGGBB".
        internal static string Alpha(string alpha, string colour) { return "#" + alpha + colour.Substring(1); }
        // Every corner in the app goes through here; the theme is square.
        internal static CornerRadius Radius(params double[] original) { return new CornerRadius(0); }
        internal static string NoteAccent(string paper) { return paper == "Mint" ? Green : paper == "Lavender" ? Purple : Accent; }

        // On battery, or when Windows animations are switched off, the cards hang still and removed cards do not fall.
        internal static bool ForceCalm, ForceLively;
        static bool calm; static DateTime calmChecked;
        internal static bool Calm
        {
            get
            {
                if (ForceLively) return false; // The built-in tests time the falling animation.
                if (ForceCalm) return true;
                if ((DateTime.Now - calmChecked).TotalSeconds < 5) return calm;
                calmChecked = DateTime.Now;
                try { calm = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline || !SystemParameters.ClientAreaAnimation; }
                catch { calm = false; }
                return calm;
            }
        }

        // Right-click menus in the same dark panels as the rest of the app.
        internal static ResourceDictionary MenuResources()
        {
            string xaml = @"<ResourceDictionary xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
<Style TargetType=""ContextMenu"">
  <Setter Property=""OverridesDefaultStyle"" Value=""True""/>
  <Setter Property=""SnapsToDevicePixels"" Value=""True""/>
  <Setter Property=""HasDropShadow"" Value=""True""/>
  <Setter Property=""FontFamily"" Value=""@FONT""/>
  <Setter Property=""FontSize"" Value=""@SIZE""/>
  <Setter Property=""Foreground"" Value=""@LIGHT""/>
  <Setter Property=""Template""><Setter.Value>
    <ControlTemplate TargetType=""ContextMenu"">
      <Border Background=""@PANEL"" BorderBrush=""@RULE"" BorderThickness=""1"" Padding=""0,4"">
        <StackPanel IsItemsHost=""True"" KeyboardNavigation.DirectionalNavigation=""Cycle""/>
      </Border>
    </ControlTemplate></Setter.Value></Setter>
</Style>
<Style TargetType=""MenuItem"">
  <Setter Property=""OverridesDefaultStyle"" Value=""True""/>
  <Setter Property=""Template""><Setter.Value>
    <ControlTemplate TargetType=""MenuItem"">
      <Border x:Name=""Back"" Background=""Transparent"" Padding=""16,6,30,6"">
        <ContentPresenter ContentSource=""Header"" RecognizesAccessKey=""False""/>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property=""IsHighlighted"" Value=""True""><Setter TargetName=""Back"" Property=""Background"" Value=""@INSET""/><Setter Property=""Foreground"" Value=""@WHITE""/></Trigger>
        <Trigger Property=""IsEnabled"" Value=""False""><Setter Property=""Foreground"" Value=""@DIM""/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
</Style>
<Style x:Key=""{x:Static MenuItem.SeparatorStyleKey}"" TargetType=""Separator"">
  <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""Separator""><Border Height=""1"" Margin=""0,4"" Background=""@RULE""/></ControlTemplate></Setter.Value></Setter>
</Style>
</ResourceDictionary>";
            xaml = xaml.Replace("@FONT", FontName).Replace("@SIZE", Size(13).ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Replace("@LIGHT", Light).Replace("@PANEL", Panel).Replace("@RULE", Rule).Replace("@INSET", Inset).Replace("@WHITE", White).Replace("@DIM", Dim);
            return (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(xaml);
        }
    }
}
