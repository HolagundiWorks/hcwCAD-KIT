using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using HCW.AutoCAD.Plugin.Logic;

namespace HCW.AutoCAD.Plugin.UI
{
    /// <summary>
    /// Says which block is which electrical item. Roles on the left (switchboard, light point, fan point, switches, sockets,
    /// AC, geyser ...), every block in the drawing on the right; tick the blocks that play the selected role.
    /// A block has one role, so ticking it for one role takes it off another.
    /// </summary>
    public class BlockRolesForm : Form
    {
        private readonly IList<ElKind> _kinds;
        private readonly IList<string> _blocks;
        private readonly Dictionary<string, List<string>> _map;
        private readonly ListBox _roles = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        private readonly CheckedListBox _names = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
        private readonly Label _owner = new Label { Dock = DockStyle.Bottom, Height = 34 };
        private bool _loading;

        /// <summary>Set when the dialog closes with Retry: the code of the role that asked to pick blocks from the drawing.</summary>
        public string PickRole = "";

        public Dictionary<string, List<string>> Map => _map;

        public BlockRolesForm(IList<string> blocks, IList<ElKind> kinds, Dictionary<string, List<string>> map, string selectedCode)
        {
            _blocks = blocks.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            _kinds = kinds;
            _map = map.ToDictionary(e => e.Key, e => new List<string>(e.Value), StringComparer.OrdinalIgnoreCase);

            Text = "hcwCAD-KIT — Electrical blocks";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            ClientSize = new Size(720, 520);
            MinimumSize = new Size(560, 380);

            var left = new Panel { Dock = DockStyle.Left, Width = 250, Padding = new Padding(8, 8, 4, 8) };
            left.Controls.Add(_roles);
            left.Controls.Add(new Label { Text = "Which item is it?", Dock = DockStyle.Top, Height = 22, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) });

            var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 8, 8, 8) };
            right.Controls.Add(_names);
            var pick = new Button { Text = "Pick from drawing…", Dock = DockStyle.Bottom, Height = 30 };
            pick.Click += (s, e) => { PickRole = SelectedCode(); DialogResult = DialogResult.Retry; Close(); };
            right.Controls.Add(pick);
            right.Controls.Add(_owner);
            right.Controls.Add(new Label { Text = "Blocks in this drawing (tick the ones that are this item)", Dock = DockStyle.Top, Height = 22, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) });

            Controls.Add(right);
            Controls.Add(left);

            var bar = new Panel { Dock = DockStyle.Bottom, Height = 46 };
            var ok = new Button { Text = "OK", Top = 8, Width = 90, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Top = 8, Width = 90, DialogResult = DialogResult.Cancel };
            bar.Controls.Add(ok);
            bar.Controls.Add(cancel);
            Controls.Add(bar);
            AcceptButton = ok;
            CancelButton = cancel;
            EventHandler place = (s, e) => { ok.Left = bar.Width - 200; cancel.Left = bar.Width - 102; };
            Resize += place;
            Load += place;

            FillRoles();
            int at = Math.Max(0, kinds.ToList().FindIndex(k => k.Code == selectedCode));
            _roles.SelectedIndex = at;
            _roles.SelectedIndexChanged += (s, e) => { if (!_loading) ShowBlocks(); };
            _names.ItemCheck += OnCheck;
            ShowBlocks();
        }

        private string SelectedCode() => _roles.SelectedIndex >= 0 ? _kinds[_roles.SelectedIndex].Code : _kinds[0].Code;

        private void FillRoles()
        {
            int keep = _roles.SelectedIndex;
            _roles.Items.Clear();
            foreach (var k in _kinds)
                _roles.Items.Add(k.Label + "  (" + _map[k.Code].Count + ")");
            if (keep >= 0 && keep < _roles.Items.Count) _roles.SelectedIndex = keep;
        }

        private void ShowBlocks()
        {
            _loading = true;
            _names.Items.Clear();
            string code = SelectedCode();
            foreach (var name in _blocks)
                _names.Items.Add(name, _map[code].Contains(name, StringComparer.OrdinalIgnoreCase));
            _loading = false;
            _owner.Text = "";
        }

        private void OnCheck(object sender, ItemCheckEventArgs e)
        {
            if (_loading) return;
            string code = SelectedCode();
            string name = (string)_names.Items[e.Index];
            if (e.NewValue == CheckState.Checked)
            {
                foreach (var list in _map.Values) list.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
                _map[code].Add(name);
            }
            else _map[code].RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            // the role list shows how many blocks each role has; refresh it once this click has finished
            BeginInvoke(new Action(() =>
            {
                _loading = true;
                FillRoles();
                _loading = false;
                var taken = _kinds.Where(k => k.Code != code && _map[k.Code].Contains(name, StringComparer.OrdinalIgnoreCase)).Select(k => k.Label).ToList();
                _owner.Text = taken.Count > 0 ? name + " was " + string.Join(", ", taken) + "; it is now this item." : "";
            }));
        }
    }
}
