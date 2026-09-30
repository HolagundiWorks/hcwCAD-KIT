using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using HCW.AutoCAD.Plugin.Logic;

namespace HCW.AutoCAD.Plugin.UI
{
    /// <summary>Pick the layers that hold the walls, windows and doors, columns and furniture.</summary>
    public class AutoDimLayersForm : Form
    {
        private readonly CheckedListBox _walls, _windows, _columns, _furniture;
        private readonly CheckBox _isolate;

        public AutoDimLayersForm(IList<string> layers, LayerChoice initial, string furnitureNote)
        {
            Text = "hcwCAD-KIT — Dimension layers";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            ClientSize = new Size(820, 520);
            MinimumSize = new Size(640, 380);

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3, Padding = new Padding(8) };
            for (int i = 0; i < 4; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

            _walls = List(layers, initial.Walls);
            _windows = List(layers, initial.Windows);
            _columns = List(layers, initial.Columns);
            _furniture = List(layers, initial.Furniture);

            AddColumn(grid, 0, "Walls", _walls);
            AddColumn(grid, 1, "Windows and doors", _windows);
            AddColumn(grid, 2, "Columns", _columns);
            AddColumn(grid, 3, "Furniture", _furniture);

            grid.Controls.Add(new Label { Text = "Walls: lines and polylines. Windows and doors: blocks (with a line on MEASURE-DEDUCT), lines or polylines. Columns: closed polylines or blocks. " + furnitureNote,
                Dock = DockStyle.Fill, AutoSize = false }, 0, 2);
            grid.SetColumnSpan(grid.GetControlFromPosition(0, 2), 4);

            Controls.Add(grid);

            var bar = new Panel { Dock = DockStyle.Bottom, Height = 46 };
            _isolate = new CheckBox
            {
                Text = "Leave only the wall, window and column layers on (Restore Layers brings the rest back)",
                Left = 10, Top = 12, Width = 560, Checked = initial.LeaveIsolated
            };
            var ok = new Button { Text = "OK", Left = 620, Top = 8, Width = 90, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Left = 718, Top = 8, Width = 90, DialogResult = DialogResult.Cancel };
            bar.Controls.Add(_isolate);
            bar.Controls.Add(ok);
            bar.Controls.Add(cancel);
            Controls.Add(bar);
            AcceptButton = ok;
            CancelButton = cancel;
            Resize += (s, e) => { ok.Left = bar.Width - 200; cancel.Left = bar.Width - 102; };
        }

        public LayerChoice Read()
        {
            return new LayerChoice
            {
                Walls = Checked(_walls),
                Windows = Checked(_windows),
                Columns = Checked(_columns),
                Furniture = Checked(_furniture),
                LeaveIsolated = _isolate.Checked
            };
        }

        private static List<string> Checked(CheckedListBox box) => box.CheckedItems.Cast<string>().ToList();

        private static CheckedListBox List(IList<string> layers, List<string> selected)
        {
            var box = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
            var picked = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
            foreach (var name in layers.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                box.Items.Add(name, picked.Contains(name));
            return box;
        }

        private static void AddColumn(TableLayoutPanel grid, int column, string title, CheckedListBox box)
        {
            grid.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) }, column, 0);
            grid.Controls.Add(box, column, 1);
        }
    }
}
