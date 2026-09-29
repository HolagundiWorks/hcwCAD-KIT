using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace HCW.AutoCAD.Plugin.UI
{
    /// <summary>Edits the attribute fields of the building-permit title block.</summary>
    public class TitleFieldsForm : Form
    {
        private readonly Dictionary<string, TextBox> _boxes = new Dictionary<string, TextBox>();

        public TitleFieldsForm(IList<KeyValuePair<string, string>> fields)
        {
            Text = "hcwCAD-KIT — Title block";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            ClientSize = new Size(560, 640);
            MinimumSize = new Size(480, 400);

            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            int y = 8;
            foreach (var field in fields)
            {
                scroll.Controls.Add(new Label
                {
                    Left = 8, Top = y + 3, Width = 170, Height = 18,
                    Text = Friendly(field.Key)
                });
                var box = new TextBox { Left = 184, Top = y, Width = 340, Text = field.Value ?? "" };
                scroll.Controls.Add(box);
                _boxes[field.Key] = box;
                y += 28;
            }
            scroll.AutoScrollMinSize = new Size(0, y + 8);
            Controls.Add(scroll);

            var bar = new Panel { Dock = DockStyle.Bottom, Height = 44 };
            var ok = new Button { Text = "Apply", Left = 360, Top = 8, Width = 90, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Left = 456, Top = 8, Width = 90, DialogResult = DialogResult.Cancel };
            bar.Controls.Add(ok);
            bar.Controls.Add(cancel);
            Controls.Add(bar);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        public Dictionary<string, string> Values()
        {
            var map = new Dictionary<string, string>();
            foreach (var kv in _boxes) map[kv.Key] = kv.Value.Text ?? "";
            return map;
        }

        private static string Friendly(string tag)
        {
            switch (tag)
            {
                case "PROJECT_TITLE": return "Project title";
                case "DRAWING_TITLE": return "Drawing title";
                case "DRAWING_NO": return "Drawing #";
                case "SIZE": return "Size";
                case "PLOT_USE": return "Plot use";
                case "OWNER": return "Owner's signature";
                case "ARCHITECT": return "Consulting architect";
                case "PID": return "PID";
                case "SITE_AREA": return "Site area";
                case "STABILITY": return "Stability certificate";
                case "FAR_ACH": return "F.A.R. achieved %";
                case "FAR_PERM": return "F.A.R. permissible %";
                case "GC_ACH": return "Ground cover achieved sq m";
                case "GC_PCT": return "Ground cover %";
                case "GC_PERM": return "Ground cover permissible sq m";
                case "FL1": return "Floor 1";
                case "FL2": return "Floor 2";
                case "FL3": return "Floor 3";
                case "FL4": return "Floor 4";
                case "DED1": return "Floor 1 deduction";
                case "DED2": return "Floor 2 deduction";
                case "DED3": return "Floor 3 deduction";
                case "DED4": return "Floor 4 deduction";
                case "NET1": return "Floor 1 built-up net";
                case "NET2": return "Floor 2 built-up net";
                case "NET3": return "Floor 3 built-up net";
                case "NET4": return "Floor 4 built-up net";
                case "GROSS1": return "Floor 1 built-up gross";
                case "GROSS2": return "Floor 2 built-up gross";
                case "GROSS3": return "Floor 3 built-up gross";
                case "GROSS4": return "Floor 4 built-up gross";
                case "TOT_DED": return "Total deduction";
                case "TOT_NET": return "Total built-up net";
                case "TOT_GROSS": return "Total built-up gross";
                default: return tag.Replace('_', ' ');
            }
        }
    }
}
