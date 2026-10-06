using System;
using System.Collections.Generic;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>
    /// Which layer each kind of drawn item goes on. The tools name their layers the HCW way (A-WALL, A-DOOR ...); when the drawing is for permit
    /// scrutiny the same items go on the building permit layers (BP-BUILDING-CUT, BP-DOOR ...) so another program can pick them up by layer. No CAD types are used here.
    /// </summary>
    public static class LayerRoles
    {
        /// <summary>HCW layer name = building permit layer name, separated by semicolons. Layers not listed keep their HCW name (text, dimensions, grid, take-off and electrical layers).</summary>
        public const string DefaultMap =
            "A-WALL=BP-BUILDING-CUT; A-DOOR=BP-DOOR; A-WIND=BP-WINDOW; A-COL=BP-STRUC-REF; ROOM-RECT=BP-ROOM; ROOM-LABELS=BP-ROOM; " +
            "A-LIFT-MR=BP-LIFT; A-LIFT-SEC=BP-SECTION; A-LIFT-LVL=BP-LEVEL; A-LIFT-HATCH=BP-SECTION; " +
            "A-ESCALATOR=BP-STAIR; A-ESCALATOR-STEPS=BP-STAIR; A-ESCALATOR-RAIL=BP-STAIR; A-ESCALATOR-LVL=BP-LEVEL; " +
            "A-RAIL=BP-STAIR; A-RAIL-ELEV=BP-STAIR; A-RAIL-BAL=BP-STAIR; A-RAIL-LVL=BP-LEVEL; " +
            "AECSTAIR-PLAN=BP-STAIR; AECSTAIR-TREAD=BP-STAIR; AECSTAIR-NOSING=BP-STAIR; AECSTAIR-ARROW=BP-STAIR; AECSTAIR-WELL=BP-STAIR; AECSTAIR-TEXT=AN-TEXT; " +
            "AECSTAIR-RCC=BP-SECTION; AECSTAIR-LEVEL=BP-LEVEL; AECSTAIR-HATCH=BP-SECTION; AECSTAIR-HEADROOM=BP-SECTION; AECSTAIR-RAIL=BP-SECTION; AECSTAIR-REBAR=BP-SECTION; AECSTAIR-BEYOND=BP-SECTION";

        public static Dictionary<string, string> Parse(string text)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in (text ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = row.Split(new[] { '=' }, 2);
                if (eq.Length != 2) continue;
                string from = eq[0].Trim(), to = eq[1].Trim();
                if (from.Length > 0 && to.Length > 0) map[from] = to;
            }
            return map;
        }

        /// <summary>
        /// The layer an item is drawn on. In HCW output (<paramref name="buildingPermit"/> false) that is its HCW name. For the building permit layers it is the map's entry for
        /// the HCW name, else <paramref name="permitName"/> when the caller knows a better one for this item, else the HCW name unchanged.
        /// </summary>
        public static string Resolve(string hcwName, bool buildingPermit, IDictionary<string, string> map, string permitName = null)
        {
            if (!buildingPermit) return hcwName;
            string to;
            if (map != null && map.TryGetValue(hcwName, out to)) return to;
            return permitName ?? hcwName;
        }

        /// <summary>The layers a scrutiny program can expect the tools to draw on in building permit output, without repeats.</summary>
        public static List<string> PermitLayers(IDictionary<string, string> map) =>
            map.Values.Where(v => v.StartsWith("BP-", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
