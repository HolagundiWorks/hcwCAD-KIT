using System;
using System.Collections.Generic;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>The electrical points by room: which room each point stands in, and a count of each kind per room.</summary>
    public static class ElectricalRooms
    {
        public const string NoRoom = "(no room)";

        /// <summary>The name of the room whose outline contains the point; the smallest one when rooms are nested; <see cref="NoRoom"/> when none does.</summary>
        public static string RoomOf(P2 point, IList<RoomInput> rooms, IList<string> names)
        {
            string best = NoRoom; double bestArea = double.MaxValue;
            for (int i = 0; i < rooms.Count; i++)
            {
                if (!PlanarRooms.Contains(rooms[i].Outline, point)) continue;
                double a = Math.Abs(PlanarRooms.SignedArea(rooms[i].Outline));
                if (a < bestArea) { bestArea = a; best = string.IsNullOrWhiteSpace(names[i]) ? "ROOM " + (i + 1) : names[i]; }
            }
            return best;
        }

        /// <summary>Room, point, type and board for every point that stands in a room, in room then point order.</summary>
        public static List<string[]> Rows(IList<ElNode> nodes, IList<ElNet> nets, IList<RoomInput> rooms, IList<string> names)
        {
            var boards = ElectricalSchedule.BoardsOfPoints(nets);
            var rows = new List<string[]>();
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].IsBoard) continue;
                string room = RoomOf(new P2(nodes[i].Box.CentreX, nodes[i].Box.CentreY), rooms, names);
                List<int> bs;
                string board = boards.TryGetValue(i, out bs) ? string.Join(", ", bs.Select(b => nodes[b].Id).OrderBy(x => x, Comparer<string>.Create(ElectricalSchedule.NaturalCompare))) : "not wired";
                rows.Add(new[] { room, nodes[i].Id, ElectricalKinds.LabelOf(nodes[i].Code), board });
            }
            return rows.OrderBy(r => r[0] == NoRoom ? 1 : 0).ThenBy(r => r[0], StringComparer.OrdinalIgnoreCase)
                       .ThenBy(r => r[1], Comparer<string>.Create(ElectricalSchedule.NaturalCompare)).ToList();
        }

        public static string[] Header => new[] { "Room", "Point", "Type", "SB" };
    }
}
