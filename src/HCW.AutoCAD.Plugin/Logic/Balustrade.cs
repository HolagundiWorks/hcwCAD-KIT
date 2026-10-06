using System;
using System.Collections.Generic;
using System.Globalization;

namespace HCW.AutoCAD.Plugin.Logic
{
    public class BalustradeOptions
    {
        /// <summary>Top of the handrail above the floor (or the nosing line on a stair).</summary>
        public double HandrailHeight = 900;
        public double RailDepth = 50;
        public double PostSize = 50;
        public double BalusterSize = 12;
        /// <summary>The largest clear gap between balusters (and between a baluster and a post): a 100 mm sphere must not pass.</summary>
        public double MaxClearGap = 100;
        /// <summary>The gap left under the balusters above the floor or tread.</summary>
        public double BottomClear = 100;
        /// <summary>Slope of the flight the balustrade follows, in degrees (0 on a level floor).</summary>
        public double SlopeDeg;
        public double TextHeight = 125;
    }

    public class BalustradeResult
    {
        public GDrawing Drawing;
        public int Posts, Balusters;
        public double ClearGap;
    }

    /// <summary>
    /// A balustrade in elevation along a run: floor or flight line, handrail, posts where the plan has them and balusters between them so no
    /// gap is wider than the limit. x runs along the plan distance of the run (horizontal), y is the height above the floor at x = 0.
    /// Roles: LEVEL (the floor line), RAIL (the handrail), POST, BALUSTER, TEXT.
    /// </summary>
    public static class Balustrade
    {
        public static BalustradeResult Build(double length, IList<double> postAt, BalustradeOptions o, out string error)
        {
            error = null;
            if (length <= 0) { error = "the run has no length"; return null; }
            if (o.HandrailHeight <= o.RailDepth + o.BottomClear) { error = "the handrail height (" + o.HandrailHeight + ") leaves no room for balusters"; return null; }
            if (o.BalusterSize <= 0 || o.MaxClearGap <= 0 || o.PostSize <= 0) { error = "baluster size, post size and the largest gap must be more than 0"; return null; }
            if (o.SlopeDeg < 0 || o.SlopeDeg >= 60) { error = "the slope must be from 0 to under 60 degrees"; return null; }

            double tan = Math.Tan(o.SlopeDeg * Math.PI / 180);
            Func<double, double> floor = x => x * tan;
            var res = new BalustradeResult { Drawing = new GDrawing() };
            var g = res.Drawing;
            Func<double, double, PlanPoint> p = (x, y) => new PlanPoint(x, y);

            var posts = new List<double>(postAt);
            posts.Sort();
            if (posts.Count == 0) { posts.Add(0); posts.Add(length); }
            res.Posts = posts.Count;

            double top = o.HandrailHeight, rail0 = top - o.RailDepth;
            double x0 = posts[0], x1 = posts[posts.Count - 1];
            g.Polys.Add(new GPoly { Layer = "LEVEL", Pts = { p(x0 - 300, floor(x0 - 300)), p(x1 + 300, floor(x1 + 300)) } });
            g.Polys.Add(new GPoly
            {
                Layer = "RAIL", Closed = true,
                Pts = { p(x0, floor(x0) + rail0), p(x1, floor(x1) + rail0), p(x1, floor(x1) + top), p(x0, floor(x0) + top) },
            });
            foreach (double x in posts)
                g.Polys.Add(new GPoly
                {
                    Layer = "POST", Closed = true,
                    Pts = { p(x - o.PostSize / 2, floor(x)), p(x + o.PostSize / 2, floor(x)), p(x + o.PostSize / 2, floor(x) + rail0), p(x - o.PostSize / 2, floor(x) + rail0) },
                });

            double worst = 0;
            for (int s = 0; s + 1 < posts.Count; s++)
            {
                double face0 = posts[s] + o.PostSize / 2, face1 = posts[s + 1] - o.PostSize / 2;
                double clearSpan = face1 - face0;
                if (clearSpan <= o.MaxClearGap) { worst = Math.Max(worst, Math.Max(0, clearSpan)); continue; }
                // The fewest balusters that keep every gap within the limit.
                int n = (int)Math.Ceiling((clearSpan - o.MaxClearGap) / (o.MaxClearGap + o.BalusterSize) - 1e-9);
                n = Math.Max(1, n);
                double gap = (clearSpan - n * o.BalusterSize) / (n + 1);
                worst = Math.Max(worst, gap);
                for (int k = 0; k < n; k++)
                {
                    double xc = face0 + gap * (k + 1) + o.BalusterSize * (k + 0.5);
                    g.Polys.Add(new GPoly
                    {
                        Layer = "BALUSTER", Closed = true,
                        Pts =
                        {
                            p(xc - o.BalusterSize / 2, floor(xc) + o.BottomClear), p(xc + o.BalusterSize / 2, floor(xc) + o.BottomClear),
                            p(xc + o.BalusterSize / 2, floor(xc) + rail0), p(xc - o.BalusterSize / 2, floor(xc) + rail0),
                        },
                    });
                    res.Balusters++;
                }
            }
            res.ClearGap = worst;
            g.Dims.Add(new GDim { A = p(x0, floor(x0)), B = p(x0, floor(x0) + top), Line = p(x0 - 600, 0), Text = "HANDRAIL " + top.ToString("0", CultureInfo.InvariantCulture) });
            g.Texts.Add(new GText
            {
                Text = res.Posts + " POSTS, " + res.Balusters + " BALUSTERS, MAX GAP " + worst.ToString("0", CultureInfo.InvariantCulture),
                X = (x0 + x1) / 2, Y = floor((x0 + x1) / 2) - o.TextHeight * 2, Height = o.TextHeight, Centre = true,
            });
            return res;
        }
    }
}
