using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using HCW.AutoCAD.Plugin.Logic;

namespace HCW.AutoCAD.Plugin.UI
{
    /// <summary>
    /// Project data in one place: the details for the title block (name plate), the floors with their heights and slab thickness, and the standard
    /// beam depths. The floors are the levels of the take-off book, so the section, stair, door and window tools read the same figures.
    /// </summary>
    public class ProjectForm : Form
    {
        public const int PageDetails = 0, PageFloors = 1, PageBeams = 2;

        private readonly Dictionary<string, TextBox> _boxes = new Dictionary<string, TextBox>();
        private readonly NumericUpDown _count = new NumericUpDown { Minimum = 1, Maximum = 30, Width = 60 };
        private readonly DataGridView _grid = new DataGridView();
        private readonly TextBox _beams = new TextBox { Multiline = true, AcceptsReturn = true };
        private readonly CheckBox _fillTitle = new CheckBox { Text = "Fill the title block with these details when I click OK", Checked = true, AutoSize = true };
        private readonly double _newFfl, _newCeiling, _newLintel, _newSlab;
        private bool _loading;

        public ProjectData Data { get; private set; }
        public List<LevelRow> Floors { get; private set; }
        public bool FillTitleBlock => _fillTitle.Checked;

        /// <param name="newFloor">The heights a floor added with the count gets when there is no floor to copy (millimetres).</param>
        public ProjectForm(ProjectData data, IList<LevelRow> floors, LevelRow newFloor, int startPage)
        {
            Data = data;
            _newFfl = newFloor.FflMm; _newCeiling = newFloor.CeilingMm; _newLintel = newFloor.LintelMm; _newSlab = newFloor.SlabMm;

            Text = "hcwCAD-KIT — Project data";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            ClientSize = new Size(640, 520);
            MinimumSize = new Size(560, 420);

            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(DetailsPage(data));
            tabs.TabPages.Add(FloorsPage(floors));
            tabs.TabPages.Add(BeamsPage(data));
            tabs.SelectedIndex = Math.Max(0, Math.Min(startPage, tabs.TabPages.Count - 1));
            Controls.Add(tabs);

            var bar = new Panel { Dock = DockStyle.Bottom, Height = 44 };
            var ok = new Button { Text = "OK", Left = 440, Top = 8, Width = 90, DialogResult = DialogResult.OK, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            var cancel = new Button { Text = "Cancel", Left = 536, Top = 8, Width = 90, DialogResult = DialogResult.Cancel, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            bar.Controls.Add(ok);
            bar.Controls.Add(cancel);
            Controls.Add(bar);
            AcceptButton = ok;
            CancelButton = cancel;
            FormClosing += OnClosing;
        }

        private TabPage DetailsPage(ProjectData data)
        {
            var page = new TabPage("Project details") { Padding = new Padding(6) };
            var note = new Label { Dock = DockStyle.Top, Height = 34, Text = "Kept in the drawing and filled into the title block (name plate). Leave a box empty to leave that title block field as it is." };
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            int y = 8;
            foreach (var f in ProjectData.Fields)
            {
                scroll.Controls.Add(new Label { Left = 8, Top = y + 3, Width = 190, Height = 18, Text = f.Label + (f.TitleField == null ? "" : "  *") });
                var box = new TextBox { Left = 204, Top = y, Width = 380, Text = data.Get(f.Key), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
                scroll.Controls.Add(box);
                _boxes[f.Key] = box;
                y += 28;
            }
            scroll.Controls.Add(new Label { Left = 8, Top = y + 4, Width = 576, Height = 18, Text = "*  goes in the title block" });
            _fillTitle.Left = 8; _fillTitle.Top = y + 30;
            scroll.Controls.Add(_fillTitle);
            scroll.AutoScrollMinSize = new Size(0, y + 64);
            page.Controls.Add(scroll);
            page.Controls.Add(note);
            return page;
        }

        private TabPage FloorsPage(IList<LevelRow> floors)
        {
            var page = new TabPage("Floors") { Padding = new Padding(6) };
            var top = new Panel { Dock = DockStyle.Top, Height = 66 };
            top.Controls.Add(new Label { Left = 4, Top = 8, Width = 120, Height = 20, Text = "Number of floors" });
            _count.Left = 128; _count.Top = 5;
            top.Controls.Add(_count);
            top.Controls.Add(new Label
            {
                Left = 4, Top = 32, Width = 600, Height = 32,
                Text = "One row per floor, in millimetres. Floor to floor is finished floor level to the next; the lintel bottom is measured up from the floor; the slab is the one under the floor. A floor added copies the last one."
            });
            _grid.Dock = DockStyle.Fill;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.RowHeadersVisible = false;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.Columns.Add("Name", "Floor");
            _grid.Columns.Add("Ffl", "Floor to floor");
            _grid.Columns.Add("Ceiling", "Ceiling height");
            _grid.Columns.Add("Lintel", "Lintel bottom");
            _grid.Columns.Add("Slab", "Slab thickness");

            _loading = true;
            foreach (var f in floors) AddRow(f);
            if (_grid.Rows.Count == 0) AddRow(new LevelRow { Name = ProjectData.FloorName(0), FflMm = _newFfl, CeilingMm = _newCeiling, LintelMm = _newLintel, SlabMm = _newSlab });
            _count.Value = _grid.Rows.Count;
            _loading = false;
            _count.ValueChanged += (s, e) => ResizeFloors((int)_count.Value);

            page.Controls.Add(_grid);
            page.Controls.Add(top);
            return page;
        }

        private TabPage BeamsPage(ProjectData data)
        {
            var page = new TabPage("Beams") { Padding = new Padding(6) };
            var note = new Label
            {
                Dock = DockStyle.Top, Height = 52,
                Text = "Standard beam depths in millimetres, separated by commas or spaces (for example 230, 300, 375, 450, 600). Kept in the drawing for the structural tools; leave empty if you do not use them."
            };
            _beams.Dock = DockStyle.Fill;
            _beams.Text = ProjectData.FormatDepths(data.BeamDepthsMm);
            page.Controls.Add(_beams);
            page.Controls.Add(note);
            return page;
        }

        private void AddRow(LevelRow f)
        {
            _grid.Rows.Add(f.Name, Num(f.FflMm), Num(f.CeilingMm), Num(f.LintelMm), Num(f.SlabMm));
        }

        private static string Num(double v) => Math.Round(v, 1).ToString("0.#", CultureInfo.InvariantCulture);

        private void ResizeFloors(int n)
        {
            if (_loading) return;
            while (_grid.Rows.Count > n) _grid.Rows.RemoveAt(_grid.Rows.Count - 1);
            while (_grid.Rows.Count < n)
            {
                int i = _grid.Rows.Count;
                var last = i > 0 ? _grid.Rows[i - 1] : null;
                _grid.Rows.Add(ProjectData.FloorName(i),
                    last != null ? last.Cells[1].Value : Num(_newFfl), last != null ? last.Cells[2].Value : Num(_newCeiling),
                    last != null ? last.Cells[3].Value : Num(_newLintel), last != null ? last.Cells[4].Value : Num(_newSlab));
            }
        }

        private static bool Parse(object cell, out double v) =>
            double.TryParse(Convert.ToString(cell, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v >= 0;

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (DialogResult != DialogResult.OK) return;
            var rows = new List<LevelRow>();
            foreach (DataGridViewRow r in _grid.Rows)
            {
                double ffl, ceil, lintel, slab;
                if (!Parse(r.Cells[1].Value, out ffl) || !Parse(r.Cells[2].Value, out ceil) || !Parse(r.Cells[3].Value, out lintel) || !Parse(r.Cells[4].Value, out slab) || ffl <= 0)
                {
                    MessageBox.Show(this, "Floor " + (r.Index + 1) + ": every height is a number in millimetres, and floor to floor must be more than zero.", "Project data", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    e.Cancel = true;
                    return;
                }
                string name = Convert.ToString(r.Cells[0].Value, CultureInfo.InvariantCulture);
                rows.Add(new LevelRow { Name = string.IsNullOrWhiteSpace(name) ? ProjectData.FloorName(r.Index) : name.Trim(), FflMm = ffl, CeilingMm = ceil, LintelMm = lintel, SlabMm = slab });
            }
            Floors = rows;
            foreach (var kv in _boxes) Data.Set(kv.Key, kv.Value.Text);
            Data.BeamDepthsMm = ProjectData.ParseDepths(_beams.Text);
        }
    }
}
