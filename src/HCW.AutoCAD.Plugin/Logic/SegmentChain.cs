using System;
using System.Collections.Generic;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>Joins loose line segments into runs, so exploded outlines become polylines again.</summary>
    public static class SegmentChain
    {
        public class Run
        {
            public List<P2> Points = new List<P2>();
            public bool Closed;
        }

        /// <summary>Segments whose end points meet within tol are chained; a run that returns to its start is closed.</summary>
        public static List<Run> Join(IList<Seg> segs, double tol)
        {
            var used = new bool[segs.Count];
            var runs = new List<Run>();
            for (int i = 0; i < segs.Count; i++)
            {
                if (used[i]) continue;
                used[i] = true;
                var pts = new List<P2> { segs[i].A, segs[i].B };

                Extend(segs, used, pts, tol, atEnd: true);
                bool closed = pts.Count > 2 && pts[0].DistanceTo(pts[pts.Count - 1]) <= tol;
                if (!closed) Extend(segs, used, pts, tol, atEnd: false);
                closed = pts.Count > 2 && pts[0].DistanceTo(pts[pts.Count - 1]) <= tol;
                if (closed) pts.RemoveAt(pts.Count - 1);
                runs.Add(new Run { Points = pts, Closed = closed });
            }
            return runs;
        }

        private static void Extend(IList<Seg> segs, bool[] used, List<P2> pts, double tol, bool atEnd)
        {
            while (true)
            {
                var tip = atEnd ? pts[pts.Count - 1] : pts[0];
                if (atEnd && pts.Count > 2 && pts[0].DistanceTo(tip) <= tol) return;
                int found = -1; bool flip = false;
                for (int j = 0; j < segs.Count && found < 0; j++)
                {
                    if (used[j]) continue;
                    if (segs[j].A.DistanceTo(tip) <= tol) { found = j; flip = false; }
                    else if (segs[j].B.DistanceTo(tip) <= tol) { found = j; flip = true; }
                }
                if (found < 0) return;
                used[found] = true;
                var next = flip ? segs[found].A : segs[found].B;
                if (atEnd) pts.Add(next); else pts.Insert(0, next);
            }
        }
    }
}
