using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>
    /// Adds an amount to every number in a piece of text. Leading zeros are kept only when the
    /// original number had them ("01" plus 1 is "02", "9" plus 1 is "10"). No CAD types are used here.
    /// </summary>
    public static class NumberIncrement
    {
        public sealed class Token
        {
            public readonly string Text;
            public readonly bool IsNumber;
            public Token(string text, bool isNumber) { Text = text; IsNumber = isNumber; }
        }

        public static List<Token> Tokenize(string text)
        {
            var tokens = new List<Token>();
            if (string.IsNullOrEmpty(text))
            {
                tokens.Add(new Token("", false));
                return tokens;
            }
            int i = 0;
            while (i < text.Length)
            {
                if (char.IsDigit(text[i]))
                {
                    int j = i;
                    while (j < text.Length && char.IsDigit(text[j])) j++;
                    if (j < text.Length && text[j] == '.' && j + 1 < text.Length && char.IsDigit(text[j + 1]))
                    {
                        j++;
                        while (j < text.Length && char.IsDigit(text[j])) j++;
                    }
                    tokens.Add(new Token(text.Substring(i, j - i), true));
                    i = j;
                }
                else
                {
                    int j = i + 1;
                    while (j < text.Length && !char.IsDigit(text[j])) j++;
                    tokens.Add(new Token(text.Substring(i, j - i), false));
                    i = j;
                }
            }
            return tokens;
        }

        public static string Render(string incrementText, List<Token> tokens, decimal delta)
        {
            var sb = new StringBuilder();
            foreach (var token in tokens)
                sb.Append(token.IsNumber ? Increment(incrementText, token.Text, delta) : token.Text);
            return sb.ToString();
        }

        /// <summary>Adds <paramref name="delta"/> to every number in <paramref name="text"/>.</summary>
        public static string Apply(string text, decimal delta, string incrementText)
        {
            return Render(incrementText, Tokenize(text), delta);
        }

        private static string Increment(string incrementText, string original, decimal delta)
        {
            decimal value;
            if (!decimal.TryParse(original, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return original;
            decimal num = value + delta;
            string body = original.TrimStart('-');
            string incBody = (incrementText ?? "1").TrimStart('-', '+');
            int dcs = DecimalPlaces(body);
            int dci = DecimalPlaces(incBody);
            int places = Math.Max(dcs, dci);
            string rendered = Math.Abs(num).ToString("F" + places, CultureInfo.InvariantCulture);
            if (body.Length > 0 && body[0] == '0')
            {
                int len = body.Length;
                if (dcs > 0) len = len - dcs + places;
                else if (dci > 0) len = len + dci + 1;
                while (rendered.Length < len) rendered = "0" + rendered;
            }
            return num < 0 ? "-" + rendered : rendered;
        }

        private static int DecimalPlaces(string text)
        {
            int dot = text.IndexOf('.');
            return dot < 0 ? 0 : text.Length - dot - 1;
        }
    }
}
