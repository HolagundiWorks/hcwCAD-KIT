using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>Axis grid spacings and bubble labels.</summary>
    public static class GridModel
    {
        /// <summary>
        /// Reads spacings such as "4000 4500, 3*3600" (three bays of 3600). Separators are spaces, commas or semicolons;
        /// a repeat is N*V or NxV. Returns null with a reason on the first bad token or when none is given.
        /// </summary>
        public static List<double> ParseSpacings(string text, out string error)
        {
            error = null;
            var res = new List<double>();
            var tokens = (text ?? "").Split(new[] { ' ', ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var raw in tokens)
            {
                string tok = raw.Trim();
                int count = 1;
                string value = tok;
                int star = tok.IndexOfAny(new[] { '*', 'x', 'X' });
                if (star > 0)
                {
                    int c;
                    if (!int.TryParse(tok.Substring(0, star), NumberStyles.Integer, CultureInfo.InvariantCulture, out c) || c < 1 || c > 200)
                    { error = "\"" + tok + "\": the repeat count must be a whole number from 1 to 200"; return null; }
                    count = c;
                    value = tok.Substring(star + 1);
                }
                double v;
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out v) || v <= 0 || double.IsInfinity(v))
                { error = "\"" + tok + "\" is not a positive spacing"; return null; }
                for (int i = 0; i < count; i++) res.Add(v);
            }
            if (res.Count == 0) { error = "give at least one spacing"; return null; }
            return res;
        }

        /// <summary>Grid line positions from the origin: 0 and the running total after each spacing.</summary>
        public static List<double> Positions(IList<double> spacings)
        {
            var res = new List<double> { 0 };
            double sum = 0;
            foreach (var s in spacings) { sum += s; res.Add(sum); }
            return res;
        }

        private static readonly char[] Letters = "ABCDEFGHJKLMNPQRSTUVWXYZ".ToCharArray();   // no I or O

        /// <summary>A, B, C … Z, then AA, AB … (the letters I and O are skipped, as on drawings).</summary>
        public static string Letter(int index)
        {
            if (index < 0) throw new ArgumentOutOfRangeException("index");
            var sb = new StringBuilder();
            int n = index;
            do
            {
                sb.Insert(0, Letters[n % Letters.Length]);
                n = n / Letters.Length - 1;
            } while (n >= 0);
            return sb.ToString();
        }

        /// <summary>The label that follows this one: 3 gives 4, B gives C, H gives J, Z gives AA. Null when the label is neither a number nor letters.</summary>
        public static string NextLabel(string label)
        {
            label = (label ?? "").Trim().ToUpperInvariant();
            int n;
            if (int.TryParse(label, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return (n + 1).ToString(CultureInfo.InvariantCulture);
            if (label.Length == 0) return null;
            int index = 0;
            foreach (char c in label)
            {
                int k = Array.IndexOf(Letters, c);
                if (k < 0) return null;
                index = index * Letters.Length + k + 1;
            }
            return Letter(index);       // index is (position + 1), which is the next label's zero-based index
        }

        /// <summary>1, 2, 3 … from start.</summary>
        public static string Number(int index, int start = 1) => (start + index).ToString(CultureInfo.InvariantCulture);
    }
}
