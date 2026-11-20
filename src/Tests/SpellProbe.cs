using System;
using System.IO;
using AndersCraftStudio.Core;

namespace AndersCraftStudio.Tests
{
    /// <summary>--spell-tip id [id…]: prints Studio's tooltip and effect summaries for spells from the server's Spell.dbc (read-only).</summary>
    static class SpellProbe
    {
        public static DbcSpellRefs Refs(out DbcTable spells)
        {
            var dir = DbcRoundTrip.DefaultDir;
            var refs = new DbcSpellRefs(f => DbcTable.Load(Path.Combine(dir, f)));
            spells = refs.Get("Spell.dbc");
            return refs;
        }

        public static int Run(string[] ids)
        {
            var refs = Refs(out var t);
            foreach (var a in ids)
            {
                var r = t.Find(uint.Parse(a));
                if (r == null) { Console.WriteLine(a + ": not found"); continue; }
                var tip = SpellTooltip.Build(new SpellData(t, r), refs);
                Console.WriteLine("---- " + a);
                foreach (var l in tip.Lines()) Console.WriteLine("  " + l);
                foreach (var e in tip.Effects) Console.WriteLine("  • " + e);
                if (!string.IsNullOrEmpty(tip.AuraText)) Console.WriteLine("  aura: " + tip.AuraText);
            }
            return 0;
        }
    }
}
