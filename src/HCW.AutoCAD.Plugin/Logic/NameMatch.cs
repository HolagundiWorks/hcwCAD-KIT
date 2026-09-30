using System.Text;

namespace HCW.AutoCAD.Plugin.Logic
{
    public static class NameMatch
    {
        /// <summary>
        /// Compares deduction names loosely: case, spaces, hyphens and leading zeros are ignored,
        /// so "FB01-D1", "fb01-d1" and "FB 01 D-1" are the same deduction.
        /// </summary>
        public static string NormKey(string label)
        {
            var sb = new StringBuilder();
            bool inDigits = false;
            foreach (char ch in label ?? "")
            {
                if (!char.IsLetterOrDigit(ch)) { inDigits = false; continue; }
                if (char.IsDigit(ch))
                {
                    if (ch == '0' && !inDigits) { inDigits = true; continue; }
                    inDigits = true;
                }
                else inDigits = false;
                sb.Append(char.ToLowerInvariant(ch));
            }
            return sb.ToString();
        }
    }
}
