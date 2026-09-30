using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace HCW.AutoCAD.Plugin.UI
{
    /// <summary>Pick, edit, or save a named note set before it is placed on the sheet.</summary>
    public class TitleNotesForm : Form
    {
        private readonly ListBox _list;
        private readonly TextBox _body;
        private readonly TextBox _name;

        public string SelectedName { get; private set; }
        public string SelectedBody { get; private set; }
        public bool PlaceNew { get; private set; }

        public TitleNotesForm()
        {
            Text = "hcwCAD-KIT — Sheet notes";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            ClientSize = new Size(640, 460);

            _list = new ListBox { Left = 8, Top = 8, Width = 200, Height = 360 };
            foreach (var name in TitleNoteLibrary.Names()) _list.Items.Add(name);
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
            _list.SelectedIndexChanged += (s, e) => ShowSelected();
            Controls.Add(_list);

            _name = new TextBox { Left = 216, Top = 8, Width = 408 };
            Controls.Add(_name);
            _body = new TextBox
            {
                Left = 216, Top = 36, Width = 408, Height = 332,
                Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true
            };
            Controls.Add(_body);

            var newer = new Button { Text = "New set", Left = 104, Top = 380, Width = 90 };
            newer.Click += (s, e) =>
            {
                _list.ClearSelected();
                _name.Text = "";
                _body.Text = "";
                _name.Focus();
            };
            var addNote = new Button { Text = "Add note", Left = 200, Top = 380, Width = 90 };
            addNote.Click += (s, e) => AddNoteLine();
            var save = new Button { Text = "Save set", Left = 296, Top = 380, Width = 90 };
            save.Click += (s, e) => SaveCurrent();
            var place = new Button { Text = "Place", Left = 392, Top = 380, Width = 80, DialogResult = DialogResult.OK };
            place.Click += (s, e) =>
            {
                PlaceNew = true;
                SelectedName = _name.Text.Trim();
                SelectedBody = _body.Text ?? "";
            };
            var apply = new Button { Text = "Update", Left = 478, Top = 380, Width = 80, DialogResult = DialogResult.Yes };
            apply.Click += (s, e) =>
            {
                PlaceNew = false;
                SelectedName = _name.Text.Trim();
                SelectedBody = _body.Text ?? "";
            };
            Controls.Add(newer);
            Controls.Add(addNote);
            Controls.Add(save);
            Controls.Add(place);
            Controls.Add(apply);

            var hint = new Label
            {
                Left = 8, Top = 416, Width = 620, Height = 32,
                Text = "New set starts a blank set. Add note appends the next numbered line. Place drops it on the sheet. Update rewrites a notes block already on the drawing."
            };
            Controls.Add(hint);
            var cancel = new Button { Text = "Cancel", Left = 8, Top = 380, Width = 90, DialogResult = DialogResult.Cancel };
            Controls.Add(cancel);
            CancelButton = cancel;
            ShowSelected();
        }

        private void AddNoteLine()
        {
            string body = (_body.Text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            int count = 0;
            foreach (var line in body.Split('\n'))
                if (line.Trim().Length > 0) count++;
            if (body.Length > 0 && !body.EndsWith("\n")) body += "\n";
            _body.Text = body + (count + 1) + ". ";
            _body.SelectionStart = _body.Text.Length;
            _body.Focus();
        }

        private void ShowSelected()
        {
            if (_list.SelectedItem == null) return;
            string name = _list.SelectedItem.ToString();
            _name.Text = name;
            _body.Text = TitleNoteLibrary.Get(name);
        }

        private void SaveCurrent()
        {
            string name = _name.Text.Trim();
            if (name.Length == 0) return;
            TitleNoteLibrary.SaveOne(name, _body.Text ?? "");
            if (!_list.Items.Cast<string>().Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                _list.Items.Add(name);
            _list.SelectedItem = _list.Items.Cast<string>().First(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
