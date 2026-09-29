using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace HCW.AutoCAD.Plugin.UI
{
    /// <summary>Prefix, suffix, start number, digit count, and which layouts to renumber.</summary>
    public class LayoutRenumberForm : Form
    {
        private readonly TextBox _prefix;
        private readonly TextBox _suffix;
        private readonly NumericUpDown _start;
        private readonly NumericUpDown _digits;
        private readonly CheckedListBox _list;

        public LayoutRenumberForm(IList<string> layoutNames, string prefix, string suffix, int start, int digits)
        {
            Text = "hcwCAD-KIT — Renumber layouts";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(420, 460);

            Controls.Add(new Label { Left = 12, Top = 14, Width = 70, Text = "Prefix" });
            _prefix = new TextBox { Left = 88, Top = 12, Width = 120, Text = prefix ?? "" };
            Controls.Add(_prefix);
            Controls.Add(new Label { Left = 220, Top = 14, Width = 60, Text = "Suffix" });
            _suffix = new TextBox { Left = 280, Top = 12, Width = 120, Text = suffix ?? "" };
            Controls.Add(_suffix);

            Controls.Add(new Label { Left = 12, Top = 44, Width = 70, Text = "Start at" });
            _start = new NumericUpDown { Left = 88, Top = 42, Width = 80, Minimum = 0, Maximum = 99999, Value = Math.Max(0, start) };
            Controls.Add(_start);
            Controls.Add(new Label { Left = 220, Top = 44, Width = 60, Text = "Digits" });
            _digits = new NumericUpDown { Left = 280, Top = 42, Width = 60, Minimum = 0, Maximum = 8, Value = Math.Max(0, Math.Min(8, digits)) };
            Controls.Add(_digits);

            Controls.Add(new Label { Left = 12, Top = 74, Width = 390, Text = "Layouts in tab order. Untick any sheet to leave it as it is." });
            _list = new CheckedListBox { Left = 12, Top = 96, Width = 396, Height = 280, CheckOnClick = true };
            foreach (var name in layoutNames)
            {
                _list.Items.Add(name);
                _list.SetItemChecked(_list.Items.Count - 1, true);
            }
            Controls.Add(_list);

            var all = new Button { Text = "All", Left = 12, Top = 388, Width = 70 };
            all.Click += (s, e) => SetAll(true);
            var none = new Button { Text = "None", Left = 88, Top = 388, Width = 70 };
            none.Click += (s, e) => SetAll(false);
            var ok = new Button { Text = "Renumber", Left = 220, Top = 418, Width = 90 };
            ok.Click += (s, e) => Confirm();
            var cancel = new Button { Text = "Cancel", Left = 318, Top = 418, Width = 90, DialogResult = DialogResult.Cancel };
            Controls.Add(all);
            Controls.Add(none);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        public string Prefix => _prefix.Text.Trim();
        public string Suffix => _suffix.Text.Trim();
        public int Start => (int)_start.Value;
        public int Digits => (int)_digits.Value;

        public List<string> Chosen()
        {
            var names = new List<string>();
            foreach (var item in _list.CheckedItems) names.Add(item.ToString());
            return names;
        }

        private void SetAll(bool check)
        {
            for (int i = 0; i < _list.Items.Count; i++) _list.SetItemChecked(i, check);
        }

        private void Confirm()
        {
            if (Chosen().Count == 0)
            {
                MessageBox.Show(this, "Tick at least one layout.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string bad = InvalidChars(Prefix) ?? InvalidChars(Suffix);
            if (bad != null)
            {
                MessageBox.Show(this, "Prefix and suffix cannot contain " + bad + ".", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        private static string InvalidChars(string value)
        {
            const string banned = "\\<>/?\"::;*|,=`";
            foreach (char c in value)
                if (banned.IndexOf(c) >= 0) return "\\ < > / ? \" : ; * | , = `";
            return null;
        }
    }
}
