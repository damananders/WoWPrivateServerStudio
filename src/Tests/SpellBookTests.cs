using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AndersCraftStudio.Core;

namespace AndersCraftStudio.Tests
{
    /// <summary>Spellbook visibility: SkillLineAbility links, the "will it show?" check, the guided recipe, and Send to game.</summary>
    static class SpellBookTests
    {
        static DbcTable Server(string f) { var p = Path.Combine(DevPaths.ServerDbc, f); return File.Exists(p) ? DbcTable.Read(f, File.ReadAllBytes(p)) : null; }

        /// <summary>A minimal SkillLineAbility table, built in memory.</summary>
        static DbcTable EmptySla()
        {
            return DbcTable.Read("SkillLineAbility.dbc", MakeDbc(14));
        }

        static byte[] MakeDbc(int fields)
        {
            using var ms = new MemoryStream(); using var bw = new BinaryWriter(ms);
            bw.Write(0x43424457u); bw.Write(0); bw.Write(fields); bw.Write(fields * 4); bw.Write(1); bw.Write((byte)0);
            return ms.ToArray();
        }

        [Test] static void Learnable_spell_gets_a_SkillLineAbility_row_that_survives_a_write()
        {
            var sla = EmptySla();
            var row = SpellBook.AddLink(sla, 900500, 8, 128);   // Mage › Fire
            A.Eq(1, SpellBook.Links(sla, 900500).Count);
            var back = DbcTable.Read("SkillLineAbility.dbc", sla.Write());
            var l = SpellBook.Links(back, 900500).Single();
            A.Eq(8u, l.V[back.Layout.FieldByName("SkillLine")], "skill line");
            A.Eq(128u, l.V[back.Layout.FieldByName("ClassMask")], "class mask");
            A.Eq(row.V[0], l.V[0], "id");
            // linking again for another class on the same tab widens the mask instead of adding a row
            SpellBook.AddLink(sla, 900500, 8, 2);
            A.Eq(1, SpellBook.Links(sla, 900500).Count); A.Eq(130u, SpellBook.Links(sla, 900500)[0].V[sla.Layout.FieldByName("ClassMask")]);
        }

        [Test] static void Spellbook_check_names_each_reason_and_passes_when_wired()
        {
            var spells = Server("Spell.dbc");
            if (spells == null) return;   // needs the server's DBCs
            var t = spells.Clone();
            var src = t.Find(1243);
            var copy = src.Clone(); copy.V[0] = 900501; t.Rows.Add(copy); t.InvalidateIndex();
            var sp = new SpellData(t, copy);
            sp.Set("Attributes", "0x1C0", out _);                 // passive + do not display, like the director's 365004
            var sla = EmptySla();
            var x = new SpellBook.Input { Spell = sp, Links = sla, SkillLine = Server("SkillLine.dbc") };
            var issues = SpellBook.Check(x);
            A.True(issues.Any(i => i.Level == "bad" && i.Fix == "unhide"), "hidden flagged");
            A.True(issues.Any(i => i.Level == "warn" && i.Fix == "unpassive"), "passive flagged");
            A.True(issues.Any(i => i.Level == "bad" && i.Fix == "link"), "missing link flagged");
            A.False(issues.Any(i => i.Level == "ok"), "not ok yet");
            // the client still has the old row
            var clientCopy = new SpellData(spells.Clone(), copy.Clone());
            x.ClientKnown = true; x.ClientSpell = clientCopy; x.ClientLinks = EmptySla();
            sp.Set("Attributes", "0x0", out _);
            SpellBook.AddLink(sla, 900501, 8, 128);
            issues = SpellBook.Check(x);
            A.True(issues.Any(i => i.Fix == "ship" && i.Text.Contains("older version")), "client out of date");
            A.True(issues.Any(i => i.Fix == "ship" && i.Text.Contains("no spellbook link")), "client lacks the link");
            // same in the client: green
            x.ClientSpell = new SpellData(t, copy); x.ClientLinks = sla;
            issues = SpellBook.Check(x);
            A.True(issues[0].Level == "ok" && issues.All(i => i.Level != "bad"), "ok: " + string.Join(" | ", issues));
            A.Contains(issues[0].Text, "Mage");
            // a self-trigger is called out
            sp.Set("EffectTriggerSpell_1", "900501", out _);
            A.True(SpellBook.Check(x).Any(i => i.Fix == "selftrigger"), "self trigger");
            // the class you test with must be linked
            sp.Set("EffectTriggerSpell_1", "0", out _);
            x.ClassMask = 1;
            A.True(SpellBook.Check(x).Any(i => i.Level == "bad" && i.Text.Contains("Warrior")), "class excluded");
        }

        [Test] static void Class_tabs_come_from_SkillLine_and_SkillRaceClassInfo()
        {
            var tabs = SpellBook.ClassTabs(Server("SkillLine.dbc"), Server("SkillRaceClassInfo.dbc"));
            if (tabs.Count == 0) return;
            A.True(tabs.Any(t => t.SkillLine == 8 && t.ClassMask == 128), "Mage › Fire");
            A.True(tabs.Any(t => t.SkillLine == 56 && t.ClassMask == 16), "Priest › Holy");
            A.True(tabs.All(t => SpellBook.Classes.Count(c => (t.ClassMask & c.mask) != 0) == 1), "one class per tab");
        }

        [Test] static void Guided_recipe_makes_a_visible_learnable_spell_for_each_kind()
        {
            var spells = Server("Spell.dbc");
            if (spells == null) return;
            Func<string, DbcTable> table = f => Server(f);
            var tabs = SpellBook.ClassTabs(Server("SkillLine.dbc"), Server("SkillRaceClassInfo.dbc"));
            uint nid = 900600;
            foreach (SpellRecipe.Kind k in Enum.GetValues(typeof(SpellRecipe.Kind)))
            {
                var t = spells.Clone();
                var copy = t.Find(SpellRecipe.TemplateOf(k)).Clone(); copy.V[0] = ++nid; t.Rows.Add(copy); t.InvalidateIndex();
                var sp = new SpellData(t, copy);
                var r = new SpellRecipe.Recipe { Kind = k, Name = "Test " + k, Amount = 250, CastMs = k == SpellRecipe.Kind.Heal ? 2500 : 1500, CooldownSec = 6, RangeYards = k == SpellRecipe.Kind.Buff ? 0 : 30, DurationSec = 600, IconId = sp.U("SpellIconID"), Boost = SpellRecipe.Boosts.Last() };
                var notes = SpellRecipe.Apply(sp, r, table);
                // read it back from bytes, as the patch would carry it
                var back = DbcTable.Read("Spell.dbc", t.Write());
                var b = new SpellData(back, back.Find(nid));
                A.Eq("Test " + k, b.Name, k + " name");
                A.Eq(k == SpellRecipe.Kind.Passive, (b.U("Attributes") & SpellBook.AttrPassive) != 0, k + " passive flag");
                A.Eq(0u, b.U("Attributes") & SpellBook.AttrDoNotDisplay, k + " visible");
                A.Eq(250, b.Points(1).max, k + " amount");
                A.Eq(0, b.Effect(2), k + " template leftovers cleared");
                A.Eq(k == SpellRecipe.Kind.Passive ? 0u : 6000u, b.U("RecoveryTime"), k + " cooldown");
                A.Contains(b.S("Description_enUS"), "$s1");
                if (k == SpellRecipe.Kind.Buff) A.Eq(200u, b.U("EffectAura_1"), "xp aura");
                var sla = Server("SkillLineAbility.dbc") ?? EmptySla();
                var tab = tabs.FirstOrDefault(x => x.SkillLine == 8) ?? new SpellBook.Tab { SkillLine = 8, ClassMask = 128 };
                SpellBook.AddLink(sla, nid, tab.SkillLine, tab.ClassMask);
                var slaBack = DbcTable.Read("SkillLineAbility.dbc", sla.Write());
                var issues = SpellBook.Check(new SpellBook.Input { Spell = b, Links = slaBack, SkillLine = Server("SkillLine.dbc"), ClassMask = 128 });
                A.False(issues.Any(i => i.Level == "bad"), k + ": " + string.Join(" | ", issues));
                A.Eq("ok", issues[0].Level, k + " green");
            }
        }

        [Test] static void Send_to_game_beats_the_last_patch_and_reuses_its_own()
        {
            var plan = new GameShip.Plan { Locale = "enUS" };
            var arch = new List<(string, int, bool)> { ("patch-3.MPQ", ClientDbcSource.Rank("patch-3.MPQ", false), true), ("patch-Z.MPQ", ClientDbcSource.Rank("patch-Z.MPQ", false), true), (@"enUS\patch-enUS-3.MPQ", ClientDbcSource.Rank("patch-enUS-3.MPQ", true), true) };
            GameShip.PickName(plan, arch, null);
            A.Eq("patch-enUS-Z.MPQ", plan.PatchName, "beats patch-Z from the locale folder");
            A.Eq("enUS", plan.TargetDir); A.Contains(plan.Why, "patch-Z.MPQ");
            // a patch Studio installed before is replaced, not stacked
            var p2 = new GameShip.Plan { Locale = "enUS" };
            GameShip.PickName(p2, arch.Append((@"enUS\patch-enUS-Z.MPQ", ClientDbcSource.Rank("patch-enUS-Z.MPQ", true), true)), new[] { "patch-enUS-Z.MPQ" });
            A.Eq("patch-enUS-Z.MPQ", p2.PatchName, "reuse own");
            // only base patches up to M: the next letter wins
            var p3 = new GameShip.Plan();
            GameShip.PickName(p3, new[] { ("patch-M.MPQ", ClientDbcSource.Rank("patch-M.MPQ", false), true) }, null);
            A.Eq("patch-N.MPQ", p3.PatchName);
            // someone else's locale Z already holds them: nothing can win
            var p4 = new GameShip.Plan { Locale = "enUS" };
            GameShip.PickName(p4, new[] { (@"enUS\patch-enUS-Z.MPQ", ClientDbcSource.Rank("patch-enUS-Z.MPQ", true), true) }, null);
            A.True(p4.PatchName == null && p4.Problems.Count == 1, "blocked");
            A.True(PatchNames.IsLocaleLoadable("patch-enUS-Z.MPQ", "enUS", out _) && !PatchNames.IsLocaleLoadable("patch-Z.MPQ", "enUS", out _), "locale names");
        }

        [Test] static void Send_to_game_replays_sent_edits_on_the_work_copy()
        {
            // a work folder whose Spell-like table lost an earlier edit (what happened to the director's patch folder)
            var dir = Path.Combine(Path.GetTempPath(), "acs_ship_" + Guid.NewGuid().ToString("N"));
            try
            {
                var t = EmptySla();
                var layout = t.Layout;
                SpellBook.AddLink(t, 365004, 8, 128);
                Directory.CreateDirectory(Path.Combine(dir, "DBFilesClient"));
                File.WriteAllBytes(Path.Combine(dir, "DBFilesClient", "SkillLineAbility.dbc"), t.Write());
                File.WriteAllBytes(Path.Combine(dir, "Interface_icon.blp"), new byte[] { 1, 2, 3 });
                var key = t.Rows[0].V[0];
                Change Edit(string before, string after) => new Change
                {
                    Kind = ChangeKind.Update, Schema = DbcStager.Schema, Table = "SkillLineAbility.dbc", Summary = "x",
                    Dbc = new DbcPatch { File = "SkillLineAbility.dbc", Key = key, FieldCount = layout.FieldCount, Fields = { new DbcFieldEdit { Field = layout.FieldByName("ClassMask"), Name = "ClassMask", Before = before, After = after } } },
                };
                var h1 = new Changeset { State = ChangesetState.Applied, CreatedUtc = DateTime.UtcNow.AddMinutes(-5) }; h1.Changes.Add(Edit("0x80", "0x82"));
                var h2 = new Changeset { State = ChangesetState.Applied, CreatedUtc = DateTime.UtcNow.AddMinutes(-1) }; h2.Changes.Add(Edit("0x82", "0x83"));
                var plan = GameShip.Make(null, new[] { h2, h1 }, new List<Change>(), dir, null);
                A.Eq(0, plan.Problems.Count, string.Join(" | ", plan.Problems));
                var back = DbcTable.Read("SkillLineAbility.dbc", plan.Files[@"DBFilesClient\SkillLineAbility.dbc"]);
                A.Eq(0x83u, back.Rows[0].V[layout.FieldByName("ClassMask")], "both edits, oldest first");
                A.True(plan.Files.ContainsKey("Interface_icon.blp"), "other work files come along");
                // replaying again on a copy that already has them is a no-op
                File.WriteAllBytes(Path.Combine(dir, "DBFilesClient", "SkillLineAbility.dbc"), plan.Files[@"DBFilesClient\SkillLineAbility.dbc"]);
                var again = GameShip.Make(null, new[] { h1, h2 }, new List<Change>(), dir, null);
                A.Eq(0, again.Problems.Count, "idempotent");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
    }
}
