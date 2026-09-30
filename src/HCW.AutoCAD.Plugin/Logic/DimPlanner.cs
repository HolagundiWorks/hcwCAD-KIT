using System;
using System.Collections.Generic;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    public enum PlanSide { Bottom, Top, Left, Right }

    public struct PlanPoint
    {
        public double X, Y;
        public PlanPoint(double x, double y) { X = x; Y = y; }
    }

    /// <summary>One row of dimensions along a side of the plan.</summary>
    public class PlanChain
    {
        public PlanSide Side;
        /// <summary>Openings, Structure, Grid or Overall.</summary>
        public string Kind;
        /// <summary>Position in the stack, 0 nearest the plan.</summary>
        public int Level;
        public List<KeyValuePair<double, double>> Segments = new List<KeyValuePair<double, double>>();
    }

    /// <summary>The geometry AUTODIM reads, in drawing units.</summary>
    public class PlanInput
    {
        public List<PlanPoint> Structural = new List<PlanPoint>();
        public List<PlanPoint> Jambs = new List<PlanPoint>();
        public List<double> GridX = new List<double>();
        public List<double> GridY = new List<double>();
        /// <summary>Depth of the outer band, from the plan edge, whose points belong to a side.</summary>
        public double Band;
        /// <summary>Points closer than this along a side are one point.</summary>
        public double Merge;
        /// <summary>Dimensions shorter than this are not made.</summary>
        public double MinLength;
        /// <summary>All, Overall, Grid, Structure or Openings.</summary>
        public string Levels = "All";
    }

    /// <summary>Decides which dimension chains AUTODIM makes. No CAD types are used here, so it can be tested.</summary>
    public static class DimPlanner
    {
        public static List<PlanChain> Chains(PlanInput plan, IEnumerable<PlanSide> sides, out int repeated)
        {
            repeated = 0;
            var result = new List<PlanChain>();
            if (plan.Structural.Count == 0) return result;

            double minX = plan.Structural.Min(p => p.X), maxX = plan.Structural.Max(p => p.X);
            double minY = plan.Structural.Min(p => p.Y), maxY = plan.Structural.Max(p => p.Y);

            foreach (var side in sides)
            {
                bool horizontal = side == PlanSide.Bottom || side == PlanSide.Top;
                Func<PlanPoint, double> along = p => horizontal ? p.X : p.Y;
                Func<PlanPoint, bool> inBand = p =>
                    side == PlanSide.Bottom ? p.Y <= minY + plan.Band :
                    side == PlanSide.Top ? p.Y >= maxY - plan.Band :
                    side == PlanSide.Left ? p.X <= minX + plan.Band : p.X >= maxX - plan.Band;

                double lo = horizontal ? minX : minY, hi = horizontal ? maxX : maxY;
                var wall = DimChains.Merge(plan.Structural.Where(inBand).Select(along), plan.Merge);
                var all = DimChains.Merge(plan.Structural.Where(inBand).Concat(plan.Jambs.Where(inBand)).Select(along), plan.Merge);
                var overall = DimChains.Merge(plan.Structural.Select(along), plan.Merge);
                var grid = DimChains.Merge((horizontal ? plan.GridX : plan.GridY).Where(v => v >= lo - plan.Band && v <= hi + plan.Band), plan.Merge);

                bool every = plan.Levels == "All";
                // nearest the plan first: openings, structure, grid, overall
                var chains = new List<PlanChain>();
                if ((every || plan.Levels == "Openings") && all.Count > wall.Count)
                    chains.Add(new PlanChain { Kind = "Openings", Segments = DimChains.Segments(all, plan.MinLength) });
                if (every || plan.Levels == "Structure")
                    chains.Add(new PlanChain { Kind = "Structure", Segments = DimChains.Segments(wall, plan.MinLength) });
                if ((every || plan.Levels == "Grid") && grid.Count >= 2)
                    chains.Add(new PlanChain { Kind = "Grid", Segments = DimChains.Segments(grid, plan.MinLength) });
                if (every || plan.Levels == "Overall")
                    chains.Add(new PlanChain { Kind = "Overall", Segments = DimChains.Overall(overall, plan.MinLength) });

                // a chain that repeats the one inside it adds nothing
                for (int i = chains.Count - 1; i > 0; i--)
                    if (DimChains.Same(chains[i].Segments, chains[i - 1].Segments, plan.Merge))
                    {
                        repeated += chains[i].Segments.Count;
                        chains.RemoveAt(i);
                    }
                chains.RemoveAll(c => c.Segments.Count == 0);

                for (int level = 0; level < chains.Count; level++)
                {
                    chains[level].Side = side;
                    chains[level].Level = level;
                    result.Add(chains[level]);
                }
            }
            return result;
        }
    }
}
