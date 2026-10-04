using System;
using System.Linq;
using HCW.AutoCAD.Plugin;
using Xunit;

namespace HCW.Logic.Tests
{
    public class LayerDataTests
    {
        private static void NoDuplicates(System.Collections.Generic.IEnumerable<string> names)
        {
            var dup = names.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.Empty(dup);
        }

        [Fact] public void HcwNamesAreUnique() => NoDuplicates(LayerData.Hcw.Select(l => l.Name));
        [Fact] public void VhNamesAreUnique() => NoDuplicates(LayerData.Vh.Select(l => l.Name));
        [Fact] public void BpltNamesAreUnique() => NoDuplicates(LayerData.Bplt.Select(l => l.Name));
    }
}
