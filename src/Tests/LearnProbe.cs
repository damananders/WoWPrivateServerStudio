using System;
using System.IO;
using System.Linq;
using AndersCraftStudio.Core;

namespace AndersCraftStudio.Tests
{
    /// <summary>--learn-probe &lt;spellId&gt; [workFolder]: read-only look at one spell in the server copy, the client and a patch work folder.</summary>
    static class LearnProbe
    {
        public static int Run(string[] a)
        {
            uint id = uint.Parse(a[0]);
            string work = a.Length > 1 ? a[1] : null;
            void Show(string where, byte[] spell, byte[] sla)
            {
                Console.WriteLine("== " + where);
                if (spell == null) { Console.WriteLine("  Spell.dbc: (none)"); return; }
                var t = DbcTable.Read("Spell.dbc", spell);
                var r = t.Find(id);
                if (r == null) { Console.WriteLine($"  Spell.dbc: {t.Rows.Count} rows, {id} NOT PRESENT"); }
                else
                {
                    string F(string n) { int i = Enumerable.Range(0, t.Layout.FieldCount).FirstOrDefault(k => t.Layout.FieldName(k) == n, -1); return i < 0 ? "?" : t.FieldText(r, i); }
                    Console.WriteLine($"  Spell.dbc: present. Name={F("Name_enUS")} Attributes={F("Attributes")} AttributesEx={F("AttributesEx")} Effect1={F("Effect_1")} Aura1={F("EffectAura_1")} Icon={F("SpellIconID")} Cast={F("CastingTimeIndex")} Power={F("PowerType")} Range={F("RangeIndex")} Targets={F("Targets")} ImplicitA1={F("ImplicitTargetA_1")}");
                }
                if (sla == null) { Console.WriteLine("  SkillLineAbility.dbc: (none)"); return; }
                var s = DbcTable.Read("SkillLineAbility.dbc", sla);
                var hits = s.Rows.Where(x => x.V[2] == id).ToList();
                Console.WriteLine($"  SkillLineAbility rows for {id}: {hits.Count}" + string.Join("", hits.Select(h => $"  [id {h.V[0]} skill {h.V[1]} race {h.V[3]} class {h.V[4]}]")));
            }
            var server = DevPaths.ServerDbc;
            byte[] R(string dir, string f) { var p = Path.Combine(dir, f); return File.Exists(p) ? File.ReadAllBytes(p) : null; }
            Show("server copy " + server, R(server, "Spell.dbc"), R(server, "SkillLineAbility.dbc"));
            using (var c = new ClientFiles(DevPaths.ClientData))
            {
                var sp = c.Read(@"DBFilesClient\Spell.dbc", out var from1);
                var sl = c.Read(@"DBFilesClient\SkillLineAbility.dbc", out var from2);
                Show($"client (Spell.dbc from {from1}, SkillLineAbility from {from2})", sp, sl);
            }
            var src = new ClientDbcSource(DevPaths.ClientData);
            foreach (var ar in src.Archives.OrderBy(x => x.Rank))
                if (ar.Dbcs.Contains("Spell.dbc") || ar.Dbcs.Contains("SkillLineAbility.dbc"))
                    Console.WriteLine($"  archive rank {ar.Rank} {ar.Name}: " + string.Join(", ", new[] { "Spell.dbc", "SkillLineAbility.dbc" }.Where(ar.Dbcs.Contains)));
            if (work != null && a.Length > 2)
            {
                var store = new ChangesetStore(a[2]);   // read only: nothing is saved
                var plan = GameShip.Make(src, store.History(), store.Pending.Changes.Where(c => c.Selected).ToList(), work, null, f => { var pth = Path.Combine(server, f); return File.Exists(pth) ? DbcTable.Read(f, File.ReadAllBytes(pth)) : null; });
                foreach (var n in plan.Notes) Console.WriteLine("  note: " + n);
                Console.WriteLine($"== send-to-game plan: {plan.PatchName} in {plan.TargetDir ?? "Data"} · {plan.Why} · {plan.Files.Count} file(s): {string.Join(", ", plan.Files.Keys)}");
                foreach (var p in plan.Problems) Console.WriteLine("  problem: " + p);
                if (plan.Files.TryGetValue(@"DBFilesClient\Spell.dbc", out var merged)) Show("merged for the patch", merged, null);
            }
            if (work != null) Show("work folder " + work, R(Path.Combine(work, "DBFilesClient"), "Spell.dbc"), R(Path.Combine(work, "DBFilesClient"), "SkillLineAbility.dbc"));
            return 0;
        }
    }
}
