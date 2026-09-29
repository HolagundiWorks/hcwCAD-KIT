using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace HCW.AutoCAD.Plugin.UI
{
    /// <summary>Create, edit, place, and update the sheet field list.</summary>
    public class SheetFieldsForm : Form
    {
        private readonly DataGridView _grid;

        public bool PlaceNew { get; private set; }

        public SheetFieldsForm(IList<KeyValuePair<string, string>> fields)
        {
            Text = "hcwCAD-KIT — Sheet fields";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            ClientSize = new Size(560, 420);
            MinimumSize = new Size(480, 320);

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                MultiSelect = false
            };
            _grid.Columns.Add("Name", "Field");
            _grid.Columns.Add("Value", "Value");
            _grid.Columns[0].FillWeight = 40;
            foreach (var field in fields)
                _grid.Rows.Add(field.Key, field.Value);
            Controls.Add(_grid);

            var bar = new Panel { Dock = DockStyle.Bottom, Height = 78 };
            var add = new Button { Text = "Add field", Left = 8, Top = 8, Width = 100 };
            var edit = new Button { Text = "Edit field", Left = 114, Top = 8, Width = 100 };
            var remove = new Button { Text = "Remove", Left = 220, Top = 8, Width = 90 };
            add.Click += (s, e) => EditRow(-1);
            edit.Click += (s, e) => { if (CurrentRow() >= 0) EditRow(CurrentRow()); };
            remove.Click += (s, e) => { int i = CurrentRow(); if (i >= 0) _grid.Rows.RemoveAt(i); };
            var place = new Button { Text = "Place on sheet", Left = 8, Top = 42, Width = 130, DialogResult = DialogResult.OK };
            place.Click += (s, e) => PlaceNew = true;
            var update = new Button { Text = "Update selected", Left = 144, Top = 42, Width = 130, DialogResult = DialogResult.Yes };
            update.Click += (s, e) => PlaceNew = false;
            var save = new Button { Text = "Save", Left = 280, Top = 42, Width = 80 };
            save.Click += (s, e) => SheetFieldLibrary.Save(Read());
            var cancel = new Button { Text = "Cancel", Left = 458, Top = 42, Width = 90, DialogResult = DialogResult.Cancel };
            bar.Controls.Add(add);
            bar.Controls.Add(edit);
            bar.Controls.Add(remove);
            bar.Controls.Add(place);
            bar.Controls.Add(update);
            bar.Controls.Add(save);
            bar.Controls.Add(cancel);
            Controls.Add(bar);
            CancelButton = cancel;
            _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) EditRow(e.RowIndex); };
        }

        public List<KeyValuePair<string, string>> Read()
        {
            var list = new List<KeyValuePair<string, string>>();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                string name = (row.Cells[0].Value ?? "").ToString().Trim();
                if (name.Length == 0) continue;
                list.Add(new KeyValuePair<string, string>(name, (row.Cells[1].Value ?? "").ToString()));
            }
            return list;
        }

        private int CurrentRow()
        {
            return _grid.CurrentRow == null ? -1 : _grid.CurrentRow.Index;
        }

        private void EditRow(int index)
        {
            string name = index >= 0 ? ( _grid.Rows[index].Cells[0].Value ?? "").ToString() : "";
            string value = index >= 0 ? (_grid.Rows[index].Cells[1].Value ?? "").ToString() : "";
            using (var dlg = new FieldEditForm(name, value))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (dlg.FieldName.Length == 0) return;
                if (index < 0) _grid.Rows.Add(dlg.FieldName, dlg.FieldValue);
                else
                {
                    _grid.Rows[index].Cells[0].Value = dlg.FieldName;
                    _grid.Rows[index].Cells[1].Value = dlg.FieldValue;
                }
            }
        }

        private class FieldEditForm : Form
        {
            private readonly TextBox _name;
            private readonly TextBox _value;
            public string FieldName => _name.Text.Trim();
            public string FieldValue => _value.Text ?? "";

            public FieldEditForm(string name, string value)
            {
                Text = name.Length == 0 ? "Add field" : "Edit field";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterParent;
                MinimizeBox = false;
                MaximizeBox = false;
                ClientSize = new Size(420, 120);
                Controls.Add(new Label { Left = 8, Top = 12, Width = 70, Text = "Field" });
                _name = new TextBox { Left = 80, Top = 8, Width = 324, Text = name };
                Controls.Add(_name);
                Controls.Add(new Label { Left = 8, Top = 44, Width = 70, Text = "Value" });
                _value = new TextBox { Left = 80, Top = 40, Width = 324, Text = value };
                Controls.Add(_value);
                var ok = new Button { Text = "OK", Left = 224, Top = 80, Width = 86, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancel", Left = 318, Top = 80, Width = 86, DialogResult = DialogResult.Cancel };
                Controls.Add(ok);
                Controls.Add(cancel);
                AcceptButton = ok;
                CancelButton = cancel;
            }
        }
    }
}
