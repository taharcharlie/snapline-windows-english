// Snapline: a local screenshot shelf for Windows. MIT License.
// One place for colours, fonts and corner shape. Palette: Base16 Ashes.
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

        // "#RRGGBB" plus a two-digit hex alpha gives "#AARRGGBB".
        internal static string Alpha(string alpha, string colour) { return "#" + alpha + colour.Substring(1); }
        // Every corner in the app goes through here; the theme is square.
        internal static CornerRadius Radius(params double[] original) { return new CornerRadius(0); }
        internal static string NoteAccent(string paper) { return paper == "Mint" ? Green : paper == "Lavender" ? Purple : Accent; }
    }
}
