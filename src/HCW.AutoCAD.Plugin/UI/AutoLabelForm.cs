using System;
using System.Drawing;
using System.Windows.Forms;

namespace HCW.AutoCAD.Plugin.UI
{
    /// <summary>Block pattern, attribute tag, and numbering for AUTOLABEL.</summary>
    public class AutoLabelForm : Form
    {
        private readonly TextBox _block;
        private readonly TextBox _tag;
        private readonly TextBox _prefix;
        private readonly TextBox _suffix;
        private readonly NumericUpDown _start;
        private readonly NumericUpDown _digits;
        private readonly CheckBox _blocks;
        private readonly CheckBox _leaders;

        public AutoLabelForm(string block, string tag, string prefix, string suffix, int start, int digits, bool blocks, bool leaders)
        {
            Text = "hcwCAD-KIT — Label attributes";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(420, 280);

            Controls.Add(new Label { Left = 12, Top = 14, Width = 110, Text = "Block name" });
            _block = new TextBox { Left = 130, Top = 12, Width = 270, Text = block ?? "" };
            Controls.Add(_block);
            Controls.Add(new Label { Left = 12, Top = 42, Width = 110, Text = "Attribute tag" });
            _tag = new TextBox { Left = 130, Top = 40, Width = 270, Text = tag ?? "" };
            Controls.Add(_tag);
            Controls.Add(new Label { Left = 130, Top = 66, Width = 270, Height = 28, Text = "Use * and ? as wildcards. Block name is required." });

            Controls.Add(new Label { Left = 12, Top = 100, Width = 110, Text = "Prefix" });
            _prefix = new TextBox { Left = 130, Top = 98, Width = 80, Text = prefix ?? "" };
            Controls.Add(_prefix);
            Controls.Add(new Label { Left = 220, Top = 100, Width = 50, Text = "Suffix" });
            _suffix = new TextBox { Left = 270, Top = 98, Width = 130, Text = suffix ?? "" };
            Controls.Add(_suffix);

            Controls.Add(new Label { Left = 12, Top = 132, Width = 110, Text = "Start at" });
            _start = new NumericUpDown { Left = 130, Top = 130, Width = 80, Minimum = 0, Maximum = 99999, Value = Math.Max(0, start) };
            Controls.Add(_start);
            Controls.Add(new Label { Left = 220, Top = 132, Width = 50, Text = "Digits" });
            _digits = new NumericUpDown { Left = 270, Top = 130, Width = 60, Minimum = 0, Maximum = 8, Value = Math.Max(0, Math.Min(8, digits)) };
            Controls.Add(_digits);

            _blocks = new CheckBox { Left = 130, Top = 164, Width = 260, Text = "Attributed blocks", Checked = blocks };
            _leaders = new CheckBox { Left = 130, Top = 188, Width = 260, Text = "Multileader blocks", Checked = leaders };
            Controls.Add(_blocks);
            Controls.Add(_leaders);

            var ok = new Button { Text = "Number", Left = 220, Top = 230, Width = 90 };
            ok.Click += (s, e) => Confirm();
            var cancel = new Button { Text = "Cancel", Left = 318, Top = 230, Width = 90, DialogResult = DialogResult.Cancel };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        public string BlockPattern => _block.Text.Trim();
        public string TagPattern => _tag.Text.Trim();
        public string Prefix => _prefix.Text ?? "";
        public string Suffix => _suffix.Text ?? "";
        public int Start => (int)_start.Value;
        public int Digits => (int)_digits.Value;
        public bool IncludeBlocks => _blocks.Checked;
        public bool IncludeLeaders => _leaders.Checked;

        private void Confirm()
        {
            if (BlockPattern.Length == 0 || TagPattern.Length == 0)
            {
                MessageBox.Show(this, "Enter a block name and an attribute tag. Use * to match everything.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!IncludeBlocks && !IncludeLeaders)
            {
                MessageBox.Show(this, "Choose blocks, multileaders, or both.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
