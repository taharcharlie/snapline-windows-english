// Snapline: a local screenshot shelf for Windows. MIT License.
// Light formatting inside a note. The note stays plain text; these marks are read when it is drawn:
//   **bold**   *italic*   __underline__   and a line that starts with [ ] or [x] is a to-do.
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Snapline
{
    static class NoteMarkup
    {
        // A mark must hug its text ("** bold **" is left alone), and a single star must not sit inside a word or a sum.
        static readonly Regex Marks = new Regex(@"\*\*(?=\S)(.+?)(?<=\S)\*\*|__(?=\S)(.+?)(?<=\S)__|(?<![\*\w])\*(?=[^\s\*])([^\*]+?)(?<=\S)\*(?![\*\w])", RegexOptions.Compiled);

        static readonly FontFamily Symbols = new FontFamily("Segoe UI Symbol");
        // WPF draws every weight and style of a per-user Barlow Condensed as the regular face, and does not slant it either.
        // So with Barlow, bold names the semibold family outright, and italic is left looking like ordinary text
        // rather than switching to another font. The other fonts on offer have real bold and italic faces.
        static readonly FontFamily BarlowBold = new FontFamily("Barlow Condensed SemiBold, Segoe UI");

        // 0: an ordinary line. 1: an open to-do. 2: a ticked to-do. "rest" is the line without its box.
        internal static int Todo(string line, out string rest)
        {
            rest = line;
            string t = line.TrimEnd('\r').TrimStart(' ', '\t');
            if (t.StartsWith("- ", StringComparison.Ordinal)) t = t.Substring(2);
            if (t.Length < 3 || t[0] != '[' || t[2] != ']' || (t.Length > 3 && t[3] != ' ')) return 0;
            char mark = t[1];
            if (mark != ' ' && mark != 'x' && mark != 'X') return 0;
            rest = t.Length > 4 ? t.Substring(4) : "";
            return mark == ' ' ? 1 : 2;
        }
        internal static bool HasTodo(string text)
        {
            string rest;
            foreach (string line in (text ?? "").Split('\n')) if (Todo(line, out rest) != 0) return true;
            return false;
        }
        // Ticks or unticks the to-do on the given line (counted from 0); other text is untouched.
        internal static string Toggle(string text, int lineIndex)
        {
            string[] lines = text.Split('\n');
            string rest;
            if (lineIndex < 0 || lineIndex >= lines.Length) return text;
            int state = Todo(lines[lineIndex], out rest);
            if (state == 0) return text;
            int box = lines[lineIndex].IndexOf('[');
            lines[lineIndex] = lines[lineIndex].Substring(0, box + 1) + (state == 1 ? "x" : " ") + lines[lineIndex].Substring(box + 2);
            return String.Join("\n", lines);
        }
        // The text without bold, italic and underline marks, for copying into other programs. To-do boxes stay.
        internal static string Plain(string text)
        {
            if (String.IsNullOrEmpty(text)) return "";
            for (int pass = 0; pass < 4; pass++)
            {
                string next = Marks.Replace(text, delegate(Match m) { return m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value; });
                if (next == text) break;
                text = next;
            }
            return text;
        }
        // Draws the note into a text block. With "toggle", each to-do box can be clicked and reports its line.
        internal static void Fill(TextBlock block, string text, string accent, Action<int> toggle)
        {
            block.Inlines.Clear();
            // A calculation note (first line "math", "sum" or "avg") is drawn as written, with its results; no marks or boxes,
            // so that 2*3*4 stays arithmetic rather than becoming italics.
            var calculation = NoteMath.Evaluate(text);
            if (calculation.Mode != NoteMath.Mode.None) { FillCalculation(block, text, accent, calculation); return; }
            string[] lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) block.Inlines.Add(new LineBreak());
                string rest; int state = Todo(lines[i], out rest);
                if (state == 0) { Styled(block.Inlines, lines[i], 0); continue; }
                int line = i;
                block.Inlines.Add(Box(state == 2, accent, toggle == null ? (Action)null : delegate { toggle(line); }));
                var words = new Span();
                Styled(words.Inlines, rest, 0);
                if (state == 2) { words.Foreground = MemoTheme.Brush(Theme.Mid); words.TextDecorations = TextDecorations.Strikethrough; }
                block.Inlines.Add(words);
            }
        }
        static void FillCalculation(TextBlock block, string text, string accent, NoteMath.Outcome calculation)
        {
            string[] lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) block.Inlines.Add(new LineBreak());
                var words = new Run(lines[i]);
                if (i == 0) words.Foreground = MemoTheme.Brush(Theme.Mid);
                block.Inlines.Add(words);
                // The total sits on the keyword line, so even a small card that cuts off its last lines shows it.
                if (i == 0 && calculation.Total != null) block.Inlines.Add(new Run("   " + calculation.Total) { Foreground = MemoTheme.Brush(accent), FontWeight = FontWeights.SemiBold });
                if (calculation.Lines[i] != null) block.Inlines.Add(new Run("  = " + calculation.Lines[i]) { Foreground = MemoTheme.Brush(accent) });
            }
        }
        static void Styled(InlineCollection target, string text, int depth)
        {
            int at = 0;
            if (depth < 4)
            {
                foreach (Match m in Marks.Matches(text))
                {
                    if (m.Index > at) target.Add(new Run(text.Substring(at, m.Index - at)));
                    var span = new Span();
                    if (m.Groups[1].Success) { span.FontWeight = FontWeights.Bold; if (Theme.FontBase == "Barlow Condensed") span.FontFamily = BarlowBold; Styled(span.Inlines, m.Groups[1].Value, depth + 1); }
                    else if (m.Groups[2].Success) { span.TextDecorations = TextDecorations.Underline; Styled(span.Inlines, m.Groups[2].Value, depth + 1); }
                    else { span.FontStyle = FontStyles.Italic; Styled(span.Inlines, m.Groups[3].Value, depth + 1); }
                    target.Add(span);
                    at = m.Index + m.Length;
                }
            }
            if (at < text.Length) target.Add(new Run(text.Substring(at)));
        }
        static Inline Box(bool done, string accent, Action click)
        {
            // A character, not a drawn control: a control inside the text stays visible on lines that are cut off, a character does not.
            var hit = new Run(done ? "☑ " : "☐ ") { FontFamily = Symbols, Foreground = MemoTheme.Brush(done ? accent : Theme.Text) };
            if (click != null)
            {
                // Handled here so that the card underneath does not also copy, drag or open the note.
                bool pressed = false;
                hit.Cursor = Cursors.Hand; hit.ToolTip = done ? "Untick" : "Tick";
                hit.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { e.Handled = true; pressed = e.ClickCount == 1; };
                hit.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e) { e.Handled = true; if (!pressed) return; pressed = false; click(); };
            }
            return hit;
        }
    }
}
