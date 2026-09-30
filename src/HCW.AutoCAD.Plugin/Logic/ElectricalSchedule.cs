using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HCW.AutoCAD.Plugin.Logic
{
    public class ElRow
    {
        public string Board = "";
        public string Point = "";
        /// <summary>Direct, or "2 Way", "3 Way" when the point is wired to that many boards.</summary>
        public string Connection = "";
    }

    /// <summary>Turns the wire networks into schedules and checks. No CAD types are used here.</summary>
    public static class ElectricalSchedule
    {
        /// <summary>SB-02 before SB-10: compares the letters, then the number.</summary>
        public static int NaturalCompare(string a, string b)
        {
            var ma = Regex.Match(a ?? "", @"^(.*?)(\d+)$");
            var mb = Regex.Match(b ?? "", @"^(.*?)(\d+)$");
            if (!ma.Success || !mb.Success) return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            int c = string.Compare(ma.Groups[1].Value, mb.Groups[1].Value, StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
            return long.Parse(ma.Groups[2].Value).CompareTo(long.Parse(mb.Groups[2].Value));
        }

        /// <summary>For each point, the distinct boards it is wired to (through any circuit it belongs to).</summary>
        public static Dictionary<int, List<int>> BoardsOfPoints(IList<ElNet> nets)
        {
            var result = new Dictionary<int, List<int>>();
            foreach (var net in nets)
                foreach (int p in net.Points)
                {
                    if (!result.ContainsKey(p)) result[p] = new List<int>();
                    foreach (int b in net.Boards)
                        if (!result[p].Contains(b)) result[p].Add(b);
                }
            return result;
        }

        /// <summary>Board, point and connection rows, in board then point order.</summary>
        public static List<ElRow> ByBoard(IList<ElNode> nodes, IList<ElNet> nets)
        {
            var boards = BoardsOfPoints(nets);
            var rows = new List<ElRow>();
            foreach (var entry in boards)
            {
                int count = entry.Value.Count;
                string connection = count == 1 ? "Direct" : count + " Way";
                foreach (int b in entry.Value)
                    rows.Add(new ElRow { Board = nodes[b].Id, Point = nodes[entry.Key].Id, Connection = connection });
            }
            return rows.OrderBy(r => r.Board, Comparer<string>.Create(NaturalCompare))
                       .ThenBy(r => r.Point, Comparer<string>.Create(NaturalCompare)).ToList();
        }

        /// <summary>Each point with the boards it is wired to, in point order.</summary>
        public static List<KeyValuePair<string, string>> ByPoint(IList<ElNode> nodes, IList<ElNet> nets)
        {
            var boards = BoardsOfPoints(nets);
            return boards
                .Select(e => new KeyValuePair<string, string>(nodes[e.Key].Id,
                    string.Join(", ", e.Value.Select(b => nodes[b].Id).OrderBy(x => x, Comparer<string>.Create(NaturalCompare)))))
                .OrderBy(k => k.Key, Comparer<string>.Create(NaturalCompare)).ToList();
        }

        /// <summary>Points that no board reaches, and boards that reach no point (indexes into <paramref name="nodes"/>).</summary>
        public static List<int> Unconnected(IList<ElNode> nodes, IList<ElNet> nets)
        {
            var linked = new HashSet<int>();
            foreach (var net in nets)
            {
                if (net.Boards.Count == 0 || net.Points.Count == 0) continue;
                foreach (int i in net.Boards.Concat(net.Points)) linked.Add(i);
            }
            return Enumerable.Range(0, nodes.Count).Where(i => !linked.Contains(i)).ToList();
        }
    }
}
