using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace HCW.AutoCAD.Plugin.UI
{
    /// <summary>A door or window block found in the drawing that carries a deduction line.</summary>
    public class BlockFound
    {
        public string Name;
        public int Count;
        /// <summary>Deduction line length of the first insert, in schedule units.</summary>
        public double Length;
    }

    /// <summary>Floors, door and window schedule, columns, and deduction name map.</summary>
    public class MeasureScheduleForm : Form
    {
        private readonly DataGridView _floors;
        private readonly DataGridView _openings;
        private readonly DataGridView _columns;
        private readonly DataGridView _maps;
        private readonly CheckBox _draw;
        private readonly IList<BlockFound> _blocks;

        public bool DrawTable => _draw.Checked;

        public MeasureScheduleForm(MeasureBook book, IList<KeyValuePair<string, int>> deductionLabels, string heightUnit, IList<BlockFound> blocks = null)
        {
            Text = "hcwCAD-KIT — Measure schedule";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            ClientSize = new Size(760, 520);
            MinimumSize = new Size(640, 420);

            _blocks = blocks ?? new List<BlockFound>();
            var tabs = new TabControl { Dock = DockStyle.Fill };
            _floors = Grid("Floor", "FFL to FFL height (" + heightUnit + ")", "Ceiling height (" + heightUnit + ")", "Lintel bottom height (" + heightUnit + ")");
            _openings = Grid("Name", "Door or window", "Type", "Length (" + heightUnit + ")", "Height (" + heightUnit + ")", "Sill (" + heightUnit + ")", "Lintel bottom (" + heightUnit + ")", "Block name", "Count");
            SetupOpeningGrid();
            _columns = Grid("Mark", "Width (" + heightUnit + ")", "Depth (" + heightUnit + ")", "Name", "Count");
            _maps = Grid("Deduction", "Measured length", "Schedule name");
            _maps.Columns[1].ReadOnly = true;
            FillFloors(book);
            FillOpenings(book);
            FillColumns(book);
            FillMaps(book, deductionLabels);

            tabs.TabPages.Add(Page("Floors", _floors, "Each row is one floor. FFL to FFL is finished floor level to the next; lintel bottom is measured up from the FFL. Wall paint uses the ceiling height."));
            tabs.TabPages.Add(Page("Doors and windows", _openings, "Name (W1), door or window, type (pick from the list), length and height. Length is the size along the wall that is deducted. Sill and lintel bottom are optional: a blank lintel bottom uses the floor's value. Block name links door and window blocks (with a line on MEASURE-DEDUCT) to this entry; separate several names with ;. " + BlockHint()));
            tabs.TabPages.Add(Page("Columns", _columns, "Concrete columns of the same size share one mark."));
            tabs.TabPages.Add(Page("Deduction map", _maps, "Each measured deduction (FB01-D1) maps to one schedule name (W1). The closest schedule length within 50 mm (2 in) is pre-filled."));
            Controls.Add(tabs);

            var bar = new Panel { Dock = DockStyle.Bottom, Height = 46 };
            var group = new Button { Text = "Group same size", Left = 8, Top = 8, Width = 130 };
            group.Click += (s, e) => Group();
            var fromBlocks = new Button { Text = "Add from blocks", Left = 146, Top = 8, Width = 120 };
            fromBlocks.Click += (s, e) => AddFromBlocks();
            fromBlocks.Enabled = _blocks.Count > 0;
            _draw = new CheckBox { Text = "Draw schedule on the sheet", Left = 276, Top = 12, Width = 220, Checked = true };
            var ok = new Button { Text = "Apply", Left = 560, Top = 8, Width = 90, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Left = 658, Top = 8, Width = 90, DialogResult = DialogResult.Cancel };
            bar.Controls.Add(group);
            bar.Controls.Add(fromBlocks);
            bar.Controls.Add(_draw);
            bar.Controls.Add(ok);
            bar.Controls.Add(cancel);
            Controls.Add(bar);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        public MeasureBook Read()
        {
            var book = new MeasureBook();
            foreach (DataGridViewRow row in _floors.Rows)
            {
                if (row.IsNewRow) continue;
                string name = Cell(row, 0);
                if (name.Length == 0) continue;
                book.Floors.Add(new MeasureBook.FloorSpec { Name = name, FflHeight = Num(row, 1), Height = Num(row, 2), LintelBottom = Num(row, 3) });
            }
            foreach (DataGridViewRow row in _openings.Rows)
            {
                if (row.IsNewRow) continue;
                string mark = Cell(row, 0);
                if (mark.Length == 0) continue;
                book.Openings.Add(new MeasureBook.OpeningSpec
                {
                    Mark = mark,
                    Type = Cell(row, 2),
                    Kind = Cell(row, 1).Length > 0 ? Cell(row, 1)
                        : mark.StartsWith("W", StringComparison.OrdinalIgnoreCase) ? "Window" : "Door",
                    Width = Num(row, 3),
                    Height = Num(row, 4),
                    Sill = Num(row, 5),
                    LintelBottom = Num(row, 6),
                    BlockName = Cell(row, 7),
                    Count = Math.Max(1, (int)Num(row, 8))
                });
            }
            foreach (DataGridViewRow row in _columns.Rows)
            {
                if (row.IsNewRow) continue;
                string mark = Cell(row, 0);
                if (mark.Length == 0) continue;
                book.Columns.Add(new MeasureBook.ColumnSpec
                {
                    Mark = mark,
                    Width = Num(row, 1),
                    Depth = Num(row, 2),
                    Name = Cell(row, 3),
                    Count = Math.Max(1, (int)Num(row, 4))
                });
            }
            foreach (DataGridViewRow row in _maps.Rows)
            {
                if (row.IsNewRow) continue;
                string label = Cell(row, 0);
                string mark = Cell(row, 2);
                if (label.Length == 0 || mark.Length == 0) continue;
                book.Maps.Add(new MeasureBook.DeductionMap { Label = label, Mark = mark });
            }
            return book;
        }

        private string BlockHint()
        {
            if (_blocks.Count == 0) return "";
            return "Blocks found: " + string.Join(", ", _blocks.Select(b => b.Name + " (" + b.Count + ")")) + ".";
        }

        /// <summary>One new row for every block not already linked to an entry.</summary>
        private void AddFromBlocks()
        {
            var linked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DataGridViewRow existing in _openings.Rows)
            {
                if (existing.IsNewRow) continue;
                used.Add(Cell(existing, 0));
                foreach (var name in Cell(existing, 7).Split(';'))
                    if (name.Trim().Length > 0) linked.Add(name.Trim());
            }
            foreach (var block in _blocks)
            {
                if (linked.Contains(block.Name)) continue;
                string lower = block.Name.ToLowerInvariant();
                bool window = lower.Contains("win") || lower.StartsWith("w");
                string kind = window ? "Window" : "Door";
                string prefix = window ? "W" : "D";
                int n = 1;
                while (used.Contains(prefix + n)) n++;
                string mark = prefix + n;
                used.Add(mark);

                int i = _openings.Rows.Add();
                var row = _openings.Rows[i];
                row.Cells[0].Value = mark;
                row.Cells[1].Value = kind;
                FillTypes(row, MeasureBook.TypesFor(kind)[0]);
                row.Cells[3].Value = block.Length.ToString("0.###");
                row.Cells[4].Value = window ? "1.2" : "2.1";
                row.Cells[7].Value = block.Name;
                row.Cells[8].Value = "1";
            }
        }

        private void Group()
        {
            var book = Read();
            book.GroupSameSizes();
            _openings.Rows.Clear();
            _columns.Rows.Clear();
            FillOpenings(book);
            FillColumns(book);
        }

        private static TabPage Page(string title, DataGridView grid, string hint)
        {
            var page = new TabPage(title) { Padding = new Padding(6) };
            page.Controls.Add(new Label { Dock = DockStyle.Top, Height = 52, Text = hint });
            grid.Dock = DockStyle.Fill;
            page.Controls.Add(grid);
            return page;
        }

        private static DataGridView Grid(params string[] headers)
        {
            var grid = new DataGridView
            {
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            foreach (var header in headers)
                grid.Columns.Add(header, header);
            return grid;
        }

        /// <summary>Kind and type are drop-downs; the type list follows the kind of the row.</summary>
        private void SetupOpeningGrid()
        {
            var kind = new DataGridViewComboBoxColumn { HeaderText = _openings.Columns[1].HeaderText, Name = "Kind" };
            kind.Items.AddRange("Door", "Window");
            var type = new DataGridViewComboBoxColumn { HeaderText = _openings.Columns[2].HeaderText, Name = "Type" };
            // Until a row has a kind, offer every type.
            type.Items.AddRange(MeasureBook.DoorTypes.Concat(MeasureBook.WindowTypes).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
            _openings.Columns.RemoveAt(2);
            _openings.Columns.RemoveAt(1);
            _openings.Columns.Insert(1, kind);
            _openings.Columns.Insert(2, type);
            _openings.DataError += (s, e) => e.ThrowException = false;
            _openings.CellValueChanged += (s, e) =>
            {
                if (e.RowIndex < 0 || e.RowIndex >= _openings.Rows.Count) return;
                var row = _openings.Rows[e.RowIndex];
                if (row.IsNewRow) return;
                if (e.ColumnIndex == 0 && Cell(row, 1).Length == 0)
                    row.Cells[1].Value = Cell(row, 0).StartsWith("W", StringComparison.OrdinalIgnoreCase) ? "Window" : "Door";
                if (e.ColumnIndex == 1)
                    FillTypes(row, Cell(row, 2));
            };
            _openings.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_openings.IsCurrentCellDirty && _openings.CurrentCell is DataGridViewComboBoxCell)
                    _openings.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
        }

        private static void FillTypes(DataGridViewRow row, string current)
        {
            string kind = (row.Cells[1].Value ?? "").ToString();
            var list = new List<string>(MeasureBook.TypesFor(kind));
            // A type typed in an older drawing stays selectable.
            if (!string.IsNullOrWhiteSpace(current) && !list.Contains(current, StringComparer.OrdinalIgnoreCase))
                list.Add(current);
            var cell = new DataGridViewComboBoxCell { DataSource = list };
            row.Cells[2] = cell;
            var match = list.FirstOrDefault(t => string.Equals(t, current, StringComparison.OrdinalIgnoreCase));
            cell.Value = match;
        }

        private void FillFloors(MeasureBook book)
        {
            if (book.Floors.Count == 0)
                _floors.Rows.Add("Ground", "3.15", "3", "2.1");
            foreach (var f in book.Floors)
                _floors.Rows.Add(f.Name, f.FflHeight.ToString("0.###"), f.Height.ToString("0.###"), f.LintelBottom.ToString("0.###"));
        }

        private void FillOpenings(MeasureBook book)
        {
            foreach (var o in book.Openings)
                {
                    int i = _openings.Rows.Add();
                    var row = _openings.Rows[i];
                    row.Cells[0].Value = o.Mark;
                    row.Cells[1].Value = o.Kind;
                    FillTypes(row, o.Type);
                    row.Cells[3].Value = o.Width.ToString("0.###");
                    row.Cells[4].Value = o.Height.ToString("0.###");
                    row.Cells[5].Value = o.Sill > 0 ? o.Sill.ToString("0.###") : "";
                    row.Cells[6].Value = o.LintelBottom > 0 ? o.LintelBottom.ToString("0.###") : "";
                    row.Cells[7].Value = o.BlockName;
                    row.Cells[8].Value = o.Count.ToString();
                }
        }

        private void FillColumns(MeasureBook book)
        {
            foreach (var c in book.Columns)
                _columns.Rows.Add(c.Mark, c.Width.ToString("0.###"), c.Depth.ToString("0.###"), c.Name, c.Count.ToString());
        }

        private void FillMaps(MeasureBook book, IList<KeyValuePair<string, int>> labels)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lengths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (labels != null)
                foreach (var l in labels) lengths[l.Key] = l.Value;
            foreach (var map in book.Maps)
            {
                int len;
                _maps.Rows.Add(map.Label, lengths.TryGetValue(map.Label, out len) && len >= 0 ? MeasureCommands.M(len) : "", map.Mark);
                seen.Add(map.Label);
            }
            if (labels == null) return;
            foreach (var l in labels)
            {
                if (seen.Contains(l.Key)) continue;
                var hit = l.Value >= 0 ? book.SuggestOpening(l.Value) : null;
                _maps.Rows.Add(l.Key, l.Value >= 0 ? MeasureCommands.M(l.Value) : "", hit == null ? "" : hit.Mark);
            }
        }

        private static string Cell(DataGridViewRow row, int i)
        {
            return (row.Cells[i].Value ?? "").ToString().Trim();
        }

        private static double Num(DataGridViewRow row, int i)
        {
            double v;
            return double.TryParse(Cell(row, i), out v) ? v : 0;
        }
    }
}
