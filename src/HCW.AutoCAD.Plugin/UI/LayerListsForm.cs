using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace HCW.AutoCAD.Plugin.UI
{
    /// <summary>One or more lists of the drawing's layers to tick, each with a "Pick from drawing…" button.</summary>
    public class LayerListsForm : Form
    {
        private readonly List<CheckedListBox> _boxes = new List<CheckedListBox>();

        /// <summary>Set when the dialog closes with Retry: the index of the list that asked to pick layers from the drawing.</summary>
        public int PickIndex = -1;

        public LayerListsForm(string title, string note, IList<string> layers, IList<KeyValuePair<string, List<string>>> lists)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            ClientSize = new Size(Math.Max(420, 300 * lists.Count), 480);
            MinimumSize = new Size(400, 360);

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = lists.Count, RowCount = 4, Padding = new Padding(8) };
            for (int i = 0; i < lists.Count; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / lists.Count));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));

            for (int i = 0; i < lists.Count; i++)
            {
                int index = i;
                var box = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
                var picked = new HashSet<string>(lists[i].Value, StringComparer.OrdinalIgnoreCase);
                foreach (var name in layers.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                    box.Items.Add(name, picked.Contains(name));
                _boxes.Add(box);

                grid.Controls.Add(new Label { Text = lists[i].Key, Dock = DockStyle.Fill, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) }, i, 0);
                grid.Controls.Add(box, i, 1);
                var pick = new Button { Text = "Pick from drawing…", Dock = DockStyle.Fill };
                pick.Click += (s, e) => { PickIndex = index; DialogResult = DialogResult.Retry; Close(); };
                grid.Controls.Add(pick, i, 2);
            }
            var label = new Label { Text = note, Dock = DockStyle.Fill, AutoSize = false };
            grid.Controls.Add(label, 0, 3);
            grid.SetColumnSpan(label, lists.Count);
            Controls.Add(grid);

            var bar = new Panel { Dock = DockStyle.Bottom, Height = 46 };
            var ok = new Button { Text = "OK", Left = 0, Top = 8, Width = 90, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Left = 0, Top = 8, Width = 90, DialogResult = DialogResult.Cancel };
            bar.Controls.Add(ok);
            bar.Controls.Add(cancel);
            Controls.Add(bar);
            AcceptButton = ok;
            CancelButton = cancel;
            EventHandler place = (s, e) => { ok.Left = bar.Width - 200; cancel.Left = bar.Width - 102; };
            Resize += place;
            Load += place;
        }

        /// <summary>The ticked layers of each list, in the order the lists were given.</summary>
        public List<List<string>> Read()
        {
            return _boxes.Select(b => b.CheckedItems.Cast<string>().ToList()).ToList();
        }
    }
}
