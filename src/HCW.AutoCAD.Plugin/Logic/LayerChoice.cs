using System;
using System.Collections.Generic;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>Which layers hold the walls, windows and doors, columns and furniture of a plan. No CAD types are used here.</summary>
    public class LayerChoice
    {
        public List<string> Walls = new List<string>();
        public List<string> Windows = new List<string>();
        public List<string> Columns = new List<string>();
        public List<string> Furniture = new List<string>();
        /// <summary>Leave everything else switched off after the dimensions are made.</summary>
        public bool LeaveIsolated = true;

        /// <summary>The layers to keep visible while dimensioning: walls, windows and columns (furniture is hidden).</summary>
        public IEnumerable<string> Shown() => Walls.Concat(Windows).Concat(Columns);

        private static readonly string[][] Words =
        {
            new[] { "WALL", "BUILDING-CUT" },
            new[] { "WIND", "DOOR", "OPEN", "GLAZ", "FENES" },
            new[] { "COL", "STRUCT" },
            new[] { "FURN", "FIXT", "EQUIP" }
        };

        /// <summary>Layers whose names suggest each role, used as the starting choice the first time.</summary>
        public static LayerChoice Guess(IEnumerable<string> layers)
        {
            var choice = new LayerChoice();
            var all = layers.ToList();
            Func<int, List<string>> pick = i => all.Where(l => Words[i].Any(w => l.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            choice.Walls = pick(0);
            choice.Windows = pick(1);
            choice.Columns = pick(2);
            choice.Furniture = pick(3);
            // a layer belongs to one role: walls win over the others
            choice.Windows.RemoveAll(l => choice.Walls.Contains(l));
            choice.Columns.RemoveAll(l => choice.Walls.Contains(l) || choice.Windows.Contains(l));
            choice.Furniture.RemoveAll(l => choice.Walls.Contains(l) || choice.Windows.Contains(l) || choice.Columns.Contains(l));
            return choice;
        }

        public List<string> ToLines()
        {
            return new List<string>
            {
                Fields.Join(new[] { "W" }.Concat(Walls).ToArray()),
                Fields.Join(new[] { "O" }.Concat(Windows).ToArray()),
                Fields.Join(new[] { "C" }.Concat(Columns).ToArray()),
                Fields.Join(new[] { "F" }.Concat(Furniture).ToArray()),
                Fields.Join("I", LeaveIsolated ? "1" : "0")
            };
        }

        public static LayerChoice FromLines(IEnumerable<string> lines)
        {
            var choice = new LayerChoice();
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line) || line == "EMPTY") continue;
                var p = Fields.Split(line);
                var names = p.Skip(1).Where(n => n.Length > 0).ToList();
                switch (p[0])
                {
                    case "W": choice.Walls = names; break;
                    case "O": choice.Windows = names; break;
                    case "C": choice.Columns = names; break;
                    case "F": choice.Furniture = names; break;
                    case "I": choice.LeaveIsolated = p.Length < 2 || p[1] != "0"; break;
                }
            }
            return choice;
        }

        public bool Any => Walls.Count + Windows.Count + Columns.Count + Furniture.Count > 0;

        /// <summary>Drops names that are no longer layers in the drawing.</summary>
        public void KeepOnly(IEnumerable<string> existing)
        {
            var set = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
            Walls.RemoveAll(l => !set.Contains(l));
            Windows.RemoveAll(l => !set.Contains(l));
            Columns.RemoveAll(l => !set.Contains(l));
            Furniture.RemoveAll(l => !set.Contains(l));
        }
    }
}
