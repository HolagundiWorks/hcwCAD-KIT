using System;
using System.Drawing;
using System.Windows.Forms;

namespace HCW.AutoCAD.Plugin.UI
{
    /// <summary>
    /// Replaces the original DCL room-type list_box dialog (room:m:create-dcl /
    /// c:mroom etc in the LISP source) with a native WinForms list, plus a
    /// "Custom..." entry for a free-typed room name.
    /// </summary>
    public class RoomPickerForm : Form
    {
        private readonly ListBox _list;
        private readonly TextBox _custom;
        public string SelectedRoomType { get; private set; }

        public RoomPickerForm(string[] roomTypes)
        {
            Text = "Select Room Type";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false; MaximizeBox = false;
            ClientSize = new Size(280, 420);

            _list = new ListBox { Left = 10, Top = 10, Width = 260, Height = 330 };
            foreach (var t in roomTypes) _list.Items.Add(t);
            _list.Items.Add("Custom...");
            _list.SelectedIndex = 0;
            _list.SelectedIndexChanged += (s, e) => _custom.Enabled = (string)_list.SelectedItem == "Custom...";
            Controls.Add(_list);

            var lbl = new Label { Left = 10, Top = 348, Width = 260, Text = "Custom room type (if selected above):" };
            Controls.Add(lbl);
            _custom = new TextBox { Left = 10, Top = 368, Width = 260, Enabled = false };
            Controls.Add(_custom);

            var ok = new Button { Left = 95, Top = 392, Width = 80, Text = "OK", DialogResult = DialogResult.OK };
            var cancel = new Button { Left = 185, Top = 392, Width = 85, Text = "Cancel", DialogResult = DialogResult.Cancel };
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;

            ok.Click += (s, e) =>
            {
                SelectedRoomType = (string)_list.SelectedItem == "Custom..." ? _custom.Text.Trim() : (string)_list.SelectedItem;
            };
        }
    }
}
