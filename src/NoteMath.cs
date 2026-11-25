// Snapline: a local screenshot shelf for Windows. MIT License.
// Calculations in notes. A note whose first line is exactly "math", "sum" or "avg" is worked out as it is drawn:
//   math   each line is an expression (12*(3+4), 2^10, sqrt(2), 15% of 80), a variable (x = 5), or a conversion (5 km in miles)
//   sum    the last number on each line is added up ("coffee 4.50"); avg averages them; conversions work here too
// Other notes are never touched. This file has no WPF in it, so the built-in tests can check it directly.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Snapline
{
    static class NoteMath
    {
        internal enum Mode { None, Math, Sum, Avg }
        internal sealed class Outcome
        {
            internal Mode Mode;
            internal string[] Lines;   // the result for each line of the note, or null
            internal string Total;     // "Sum: 16.5" or "Average: 8.25" for sum and avg notes, else null
        }
        const int MaxLines = 200, MaxLineLength = 500, MaxDepth = 64;
        static readonly CultureInfo Plain = CultureInfo.InvariantCulture;

        internal static Mode ModeOf(string text)
        {
            if (String.IsNullOrEmpty(text)) return Mode.None;
            int end = text.IndexOf('\n');
            string first = (end < 0 ? text : text.Substring(0, end)).Trim().ToLowerInvariant();
            return first == "math" ? Mode.Math : first == "sum" ? Mode.Sum : first == "avg" ? Mode.Avg : Mode.None;
        }

        internal static Outcome Evaluate(string text)
        {
            var mode = ModeOf(text);
            if (mode == Mode.None) return new Outcome { Mode = mode, Lines = new string[0] }; // Ordinary notes cost one look at the first line.
            string[] lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
            var outcome = new Outcome { Mode = mode, Lines = new string[lines.Length] };
            var variables = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            double total = 0; int counted = 0;
            for (int i = 1; i < lines.Length && i < MaxLines; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.Length > MaxLineLength) continue;
                string converted = Convert(line, variables);
                if (converted != null) { outcome.Lines[i] = converted; continue; }
                if (mode == Mode.Math) outcome.Lines[i] = MathLine(line, variables);
                else
                {
                    double value;
                    if (LastNumber(line, out value)) { total += value; counted++; }
                }
            }
            if (mode == Mode.Sum) outcome.Total = "Sum: " + Format(total);
            if (mode == Mode.Avg) outcome.Total = counted == 0 ? "Average: none" : "Average: " + Format(total / counted);
            return outcome;
        }

        // The note as copied: each worked-out line gains " = result" unless it already ends with one, and the total follows.
        internal static string WithResults(string text)
        {
            var outcome = Evaluate(text);
            if (outcome.Mode == Mode.None) return text;
            string[] lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
            var copy = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) copy.Append("\r\n");
                copy.Append(lines[i]);
                if (outcome.Lines[i] != null) copy.Append(" = ").Append(outcome.Lines[i]);
            }
            if (outcome.Total != null) copy.Append("\r\n").Append(outcome.Total);
            return copy.ToString();
        }

        internal static string Format(double value)
        {
            if (Double.IsNaN(value) || Double.IsInfinity(value)) return null;
            if (Math.Abs(value) >= 1e15) return value.ToString("G10", Plain);
            string s = Math.Round(value, 6).ToString("0.######", Plain);
            return s == "-0" ? "0" : s;
        }

        // ---- math lines ----

        static readonly Regex Assignment = new Regex(@"^([A-Za-z_][A-Za-z_0-9]*)\s*=\s*(.+)$");
        static readonly Regex Answered = new Regex(@"=\s*-?[\d.,]+\s*[A-Za-z°/²0-9 ]*$");
        static readonly Regex PercentOf = new Regex(@"^(.+?)\s*%\s*of\s+(.+)$", RegexOptions.IgnoreCase);
        static readonly Regex PlainNumber = new Regex(@"^-?\d+(\.\d+)?$");

        static string MathLine(string line, Dictionary<string, double> variables)
        {
            var assign = Assignment.Match(line);
            if (assign.Success && !Functions.Contains(assign.Groups[1].Value.ToLowerInvariant()))
            {
                double value;
                if (!TryExpression(assign.Groups[2].Value, variables, out value)) return null;
                variables[assign.Groups[1].Value] = value;
                // "x = 5" needs no answer; "y = x + 1" shows what y became.
                return PlainNumber.IsMatch(assign.Groups[2].Value.Trim()) ? null : Format(value);
            }
            if (Answered.IsMatch(line)) return null; // Already worked out, by hand or by autocomplete.
            return Answer(line, variables);
        }
        // The value of a plain expression line, for display and for autocomplete; null when it is not one.
        internal static string Answer(string line, Dictionary<string, double> variables)
        {
            line = line.Trim();
            if (line.Length == 0 || line.Length > MaxLineLength) return null;
            var of = PercentOf.Match(line);
            double a, b;
            if (of.Success && TryExpression(of.Groups[1].Value, variables, out a) && TryExpression(of.Groups[2].Value, variables, out b)) return Format(a / 100 * b);
            if (!TryExpression(line, variables, out a)) return null;
            // A line that is just a number is not worth answering.
            return PlainNumber.IsMatch(line.Replace(",", "")) ? null : Format(a);
        }
        // What typing "=" or Tab at the end of a line of a calculation note would fill in; null when nothing.
        internal static string CompleteLine(string text, int lineIndex)
        {
            var mode = ModeOf(text);
            if (mode == Mode.None || lineIndex < 1) return null;
            string[] lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
            if (lineIndex >= lines.Length) return null;
            string line = lines[lineIndex].Trim();
            // A bare name is probably the start of "name = ...", a new value for it, not a request for the old one.
            if (line.Length == 0 || Answered.IsMatch(line) || Assignment.IsMatch(line) || Regex.IsMatch(line, @"^[A-Za-z_]\w*$")) return null;
            return Evaluate(text).Lines[lineIndex];
        }

        static readonly HashSet<string> Functions = new HashSet<string> { "sqrt", "abs", "round", "floor", "ceil", "sin", "cos", "tan", "log", "ln", "exp", "min", "max" };

        static bool TryExpression(string source, Dictionary<string, double> variables, out double value)
        {
            value = 0;
            try
            {
                var parser = new Parser(source, variables);
                value = parser.Expression(0);
                parser.SkipSpace();
                return parser.AtEnd && !Double.IsNaN(value) && !Double.IsInfinity(value);
            }
            catch (FormatException) { return false; }
        }

        sealed class Parser
        {
            readonly string s; readonly Dictionary<string, double> vars; int at; int functionDepth;
            internal Parser(string source, Dictionary<string, double> variables) { s = source; vars = variables; }
            internal bool AtEnd { get { return at >= s.Length; } }
            internal void SkipSpace() { while (at < s.Length && Char.IsWhiteSpace(s[at])) at++; }
            bool Take(char c) { SkipSpace(); if (at < s.Length && s[at] == c) { at++; return true; } return false; }
            static FormatException Fail() { return new FormatException(); }
            internal double Expression(int depth)
            {
                if (depth > MaxDepth) throw Fail();
                double value = Term(depth);
                while (true)
                {
                    if (Take('+')) value += Term(depth);
                    else if (Take('-') || Take('−')) value -= Term(depth);
                    else return value;
                }
            }
            double Term(int depth)
            {
                double value = Power(depth);
                while (true)
                {
                    if (Take('*') || Take('×')) value *= Power(depth);
                    else if (Take('/') || Take('÷')) value /= Power(depth);
                    else return value;
                }
            }
            double Power(int depth)
            {
                double value = Unary(depth);
                if (Take('^')) return Math.Pow(value, Unary2(depth)); // Right to left: 2^3^2 is 2^9.
                return value;
            }
            double Unary2(int depth) { double v = Unary(depth); if (Take('^')) return Math.Pow(v, Unary2(depth)); return v; }
            double Unary(int depth)
            {
                if (depth > MaxDepth) throw Fail();
                if (Take('-') || Take('−')) return -Power(depth + 1);
                if (Take('+')) return Power(depth + 1);
                double value = Primary(depth);
                if (Take('%')) value /= 100;
                return value;
            }
            double Primary(int depth)
            {
                SkipSpace();
                if (at >= s.Length) throw Fail();
                char c = s[at];
                if (c == '(') { at++; double v = Expression(depth + 1); if (!Take(')')) throw Fail(); return v; }
                if (Char.IsDigit(c) || c == '.') return Number();
                if (Char.IsLetter(c) || c == '_')
                {
                    int start = at;
                    while (at < s.Length && (Char.IsLetterOrDigit(s[at]) || s[at] == '_')) at++;
                    string name = s.Substring(start, at - start);
                    string lower = name.ToLowerInvariant();
                    if (Functions.Contains(lower))
                    {
                        if (!Take('(')) throw Fail();
                        functionDepth++;
                        var args = new List<double> { Expression(depth + 1) };
                        while (Take(',')) args.Add(Expression(depth + 1));
                        functionDepth--;
                        if (!Take(')')) throw Fail();
                        return Call(lower, args);
                    }
                    if (lower == "pi") return Math.PI;
                    if (lower == "e") return Math.E;
                    double known;
                    if (vars.TryGetValue(name, out known)) return known;
                    throw Fail();
                }
                throw Fail();
            }
            double Number()
            {
                var digits = new StringBuilder();
                while (at < s.Length && (Char.IsDigit(s[at]) || s[at] == '.' || s[at] == ','))
                {
                    if (s[at] == ',')
                    {
                        // A comma groups thousands only outside a function's parentheses, before exactly three digits.
                        bool grouping = functionDepth == 0 && HasThreeDigitsAfter(at);
                        if (!grouping) break;
                        at++; continue;
                    }
                    digits.Append(s[at]); at++;
                }
                double value;
                if (!Double.TryParse(digits.ToString(), NumberStyles.AllowDecimalPoint, Plain, out value)) throw Fail();
                return value;
            }
            bool HasThreeDigitsAfter(int comma)
            {
                for (int i = 1; i <= 3; i++) if (comma + i >= s.Length || !Char.IsDigit(s[comma + i])) return false;
                return comma + 4 >= s.Length || !Char.IsDigit(s[comma + 4]);
            }
            static double Call(string name, List<double> a)
            {
                switch (name)
                {
                    case "sqrt": return Math.Sqrt(One(a));
                    case "abs": return Math.Abs(One(a));
                    case "round": return a.Count == 2 ? Math.Round(a[0], (int)Math.Max(0, Math.Min(10, a[1]))) : Math.Round(One(a));
                    case "floor": return Math.Floor(One(a));
                    case "ceil": return Math.Ceiling(One(a));
                    case "sin": return Math.Sin(One(a) * Math.PI / 180); // Degrees, as people mostly mean them.
                    case "cos": return Math.Cos(One(a) * Math.PI / 180);
                    case "tan": return Math.Tan(One(a) * Math.PI / 180);
                    case "log": return Math.Log10(One(a));
                    case "ln": return Math.Log(One(a));
                    case "exp": return Math.Exp(One(a));
                    case "min": return a.Min();
                    case "max": return a.Max();
                }
                throw Fail();
            }
            static double One(List<double> a) { if (a.Count != 1) throw Fail(); return a[0]; }
        }

        // ---- sum and avg lines ----

        static readonly Regex Numbers = new Regex(@"(?<![\w.])(-?)(\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?)(?![\w])");
        static readonly Regex AmbiguousComma = new Regex(@"\d,(\d{1,2}|\d{4,})(?!\d)");
        static readonly Regex DateAtEnd = new Regex(@"\b(jan|feb|mar|apr|may|jun|jul|aug|sep|sept|oct|nov|dec)[a-z]*\.?\s+\d{1,2}\s*$", RegexOptions.IgnoreCase);

        static bool LastNumber(string line, out double value)
        {
            value = 0;
            // "2,50" or "1.234,56" is a number written the other way round; rather than guess, the line does not count.
            if (AmbiguousComma.IsMatch(line) || DateAtEnd.IsMatch(line)) return false;
            var all = Numbers.Matches(line);
            if (all.Count == 0) return false;
            var last = all[all.Count - 1];
            if (!Double.TryParse(last.Groups[2].Value.Replace(",", ""), NumberStyles.AllowDecimalPoint, Plain, out value)) return false;
            // A minus right before the digits makes it negative; "coffee - 4.50" uses the dash as a separator.
            if (last.Groups[1].Value == "-")
            {
                int dash = last.Index;
                bool separated = dash > 0 && Char.IsWhiteSpace(line[dash - 1]) && dash + 1 < line.Length && Char.IsWhiteSpace(line[dash + 1]);
                if (!separated) value = -value;
            }
            return true;
        }

        // ---- conversions ----

        sealed class Unit { internal string Kind; internal double Factor; }
        static readonly Dictionary<string, Unit> Units = BuildUnits();
        static Dictionary<string, Unit> BuildUnits()
        {
            var units = new Dictionary<string, Unit>(StringComparer.OrdinalIgnoreCase);
            Action<string, double, string> add = delegate(string kind, double factor, string names)
            { foreach (var name in names.Split(' ')) units[name] = new Unit { Kind = kind, Factor = factor }; };
            add("length", 0.001, "mm"); add("length", 0.01, "cm"); add("length", 1, "m meter meters metre metres"); add("length", 1000, "km kilometer kilometers kilometre kilometres");
            add("length", 0.0254, "in inch inches"); add("length", 0.3048, "ft foot feet"); add("length", 0.9144, "yd yard yards"); add("length", 1609.344, "mi mile miles"); add("length", 1852, "nmi");
            add("mass", 1e-6, "mg"); add("mass", 0.001, "g gram grams"); add("mass", 1, "kg kilo kilos kilogram kilograms"); add("mass", 1000, "t tonne tonnes");
            add("mass", 0.45359237, "lb lbs pound pounds"); add("mass", 0.028349523125, "oz ounce ounces"); add("mass", 6.35029318, "st stone stones");
            add("volume", 0.001, "ml"); add("volume", 0.01, "cl"); add("volume", 0.1, "dl"); add("volume", 1, "l liter liters litre litres"); add("volume", 1000, "m3");
            add("volume", 0.00492892159375, "tsp teaspoon teaspoons"); add("volume", 0.01478676478125, "tbsp tablespoon tablespoons"); add("volume", 0.2365882365, "cup cups");
            add("volume", 0.0295735295625, "floz"); add("volume", 0.473176473, "pt pint pints"); add("volume", 0.946352946, "qt quart quarts"); add("volume", 3.785411784, "gal gallon gallons");
            add("area", 1e-6, "mm2"); add("area", 1e-4, "cm2"); add("area", 1, "m2 sqm"); add("area", 1e6, "km2"); add("area", 1e4, "ha hectare hectares");
            add("area", 4046.8564224, "acre acres"); add("area", 0.09290304, "ft2 sqft"); add("area", 0.00064516, "in2 sqin"); add("area", 2589988.110336, "mi2");
            add("speed", 1, "m/s mps"); add("speed", 1 / 3.6, "km/h kmh kph"); add("speed", 0.44704, "mph"); add("speed", 0.514444, "kn knot knots"); add("speed", 0.3048, "ft/s fps");
            add("time", 0.001, "ms"); add("time", 1, "s sec secs second seconds"); add("time", 60, "min mins minute minutes"); add("time", 3600, "h hr hrs hour hours");
            add("time", 86400, "day days"); add("time", 604800, "wk week weeks"); add("time", 31557600, "yr year years");
            add("data", 1, "b byte bytes"); add("data", 1e3, "kb"); add("data", 1e6, "mb"); add("data", 1e9, "gb"); add("data", 1e12, "tb");
            add("data", 1024, "kib"); add("data", 1048576, "mib"); add("data", 1073741824, "gib"); add("data", 1099511627776, "tib");
            add("temperature", 0, "c °c celsius f °f fahrenheit k kelvin");
            return units;
        }
        // Searched from the right, so the last " in " / " to " / " as " splits: "5 in in cm" is inches into centimetres.
        static readonly Regex Into = new Regex(@"\s+(?:in|to|as)\s+", RegexOptions.IgnoreCase | RegexOptions.RightToLeft);
        static readonly Regex Quantity = new Regex(@"^(.*?)\s*((?:fl\s+oz)|(?:sq\s+(?:ft|in|m))|[A-Za-z°µ][A-Za-z°/²0-9]*)$");

        static string Convert(string line, Dictionary<string, double> variables)
        {
            var into = Into.Match(line);
            if (!into.Success) return null;
            string left = line.Substring(0, into.Index).Trim(), right = line.Substring(into.Index + into.Length).Trim();
            Unit to; string toName = Normal(right);
            if (!Units.TryGetValue(toName, out to)) return null;
            var quantity = Quantity.Match(left);
            if (!quantity.Success) return null;
            Unit from; string fromName = Normal(quantity.Groups[2].Value);
            if (!Units.TryGetValue(fromName, out from) || from.Kind != to.Kind) return null;
            double amount;
            string expression = quantity.Groups[1].Value.Trim();
            if (expression.Length == 0 || !TryExpression(expression, variables, out amount)) return null;
            double result = from.Kind == "temperature" ? Temperature(amount, fromName, toName) : amount * from.Factor / to.Factor;
            string shown = Format(result);
            return shown == null ? null : shown + " " + right;
        }
        static string Normal(string unit)
        {
            string u = Regex.Replace(unit.Trim().ToLowerInvariant(), @"\s+", " ");
            if (u == "fl oz") return "floz";
            if (u.StartsWith("sq ", StringComparison.Ordinal)) return "sq" + u.Substring(3);
            return u.Replace("²", "2");
        }
        static double Temperature(double value, string from, string to)
        {
            char f = from.TrimStart('°')[0], t = to.TrimStart('°')[0];
            double celsius = f == 'f' ? (value - 32) * 5 / 9 : f == 'k' ? value - 273.15 : value;
            return t == 'f' ? celsius * 9 / 5 + 32 : t == 'k' ? celsius + 273.15 : celsius;
        }
    }
}
