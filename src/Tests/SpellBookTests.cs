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

        [Test] static void Auto_learn_makes_the_server_teach_it_and_the_check_says_so()
        {
            var sla = EmptySla();
            int am = sla.Layout.FieldByName("AcquireMethod");
            A.True(am >= 0, "AcquireMethod field");
            SpellBook.AddLink(sla, 900600, 26, 1);                          // trainer / .learn only
            A.False(SpellBook.AutoLearned(sla, 900600), "not automatic by default");
            A.Eq(1, SpellBook.SetAutoLearn(sla, 900600, true), "one row switched");
            A.True(SpellBook.AutoLearned(sla, 900600), "automatic");
            var back = DbcTable.Read("SkillLineAbility.dbc", sla.Write());
            A.Eq(SpellBook.LearnedOnSkillValue, SpellBook.Links(back, 900600)[0].V[am], "AcquireMethod 1 survives a write");
            SpellBook.AddLink(sla, 900601, 8, 128, autoLearn: true);
            A.True(SpellBook.AutoLearned(sla, 900601), "AddLink(autoLearn)");

            var spells = Server("Spell.dbc");
            if (spells == null) return;
            var t = spells.Clone(); var copy = t.Find(1243).Clone(); copy.V[0] = 900600; t.Rows.Add(copy); t.InvalidateIndex();
            var sp = new SpellData(t, copy); sp.Set("Attributes", "0x0", out _);
            var manual = EmptySla(); SpellBook.AddLink(manual, 900600, 26, 1);
            var x = new SpellBook.Input { Spell = sp, Links = manual, SkillLine = Server("SkillLine.dbc") };
            var issues = SpellBook.Check(x);
            A.True(issues.Any(i => i.Fix == "autolearn" && i.Text.Contains(".learn 900600")), "nobody learns it by themselves: " + string.Join(" | ", issues));
            SpellBook.SetAutoLearn(manual, 900600, true);
            issues = SpellBook.Check(x);
            A.False(issues.Any(i => i.Fix == "autolearn"), "fixed");
            A.Contains(issues[0].Text, "learns it by itself");
            // the game's copy still teaches it the old way: ship again
            var oldClient = EmptySla(); SpellBook.AddLink(oldClient, 900600, 26, 1);
            x.ClientKnown = true; x.ClientSpell = new SpellData(t, copy); x.ClientLinks = oldClient;
            A.True(SpellBook.Check(x).Any(i => i.Fix == "ship" && i.Text.Contains("learns it the old way")), "client copy out of date");
        }

        /// <summary>The 3.3.5a client's own rule (read from WoW.exe): "?" patches sorted by their path under Data, highest first.</summary>
        [Test] static void Load_order_matches_the_real_client()
        {
            int R(string n) => ClientDbcSource.Rank(n, false);
            int L(string n) => ClientDbcSource.Rank(n, true);
            A.True(R("patch-Z.MPQ") > L("patch-enUS-Z.MPQ"), "Data\\patch-Z beats Data\\enUS\\patch-enUS-Z (\"patch\" sorts above \"enUS\\\\patch\")");
            A.True(R("patch-2.MPQ") > L("patch-enUS-Z.MPQ"), "any base ? patch beats every enUS one");
            A.True(R("patch-Z.MPQ") > R("patch-A.MPQ") && R("patch-A.MPQ") > R("patch-9.MPQ") && R("PATCH-D.MPQ") == R("patch-d.mpq"), "letters above digits, case doesn't matter");
            A.True(L("patch-enUS-Z.MPQ") > L("patch-enUS-4.MPQ"), "within the locale folder, later letters win");
            A.True(L("patch-enUS-2.MPQ") > R("patch.MPQ") && R("patch.MPQ") > L("patch-enUS.MPQ") && L("patch-enUS.MPQ") > L("locale-enUS.MPQ"), "patch.MPQ, then patch-enUS.MPQ, then the base archives");
            A.True(L("patch-ruRU-2.MPQ") > R("patch-Z.MPQ"), "ruRU sorts above \"patch-\", so its locale patches come first");
            A.Eq(-1, L("base-enUS.MPQ"), "launcher archive");
        }

        [Test] static void Send_to_game_picks_a_winning_name_or_updates_the_patch_that_loads_last()
        {
            (string, int, bool) Root(string n, bool holds = true) => (n, ClientDbcSource.Rank(n, false), holds);
            (string, int, bool) Loc(string n, bool holds = true) => (@"enUS\" + n, ClientDbcSource.Rank(n, true), holds);
            // the director's install: someone's patch-Z (and a copy as patch-enUS-4) hold Spell.dbc; Studio's own patch-enUS-Z is below them
            var plan = new GameShip.Plan { Locale = "enUS" };
            GameShip.PickName(plan, new[] { Root("patch-3.MPQ"), Root("PATCH-Y.MPQ", false), Root("patch-Z.MPQ"), Loc("patch-enUS-4.MPQ"), Loc("patch-enUS-Z.MPQ") }, new[] { "patch-enUS-Z.MPQ" });
            A.True(plan.InPlace, "nothing can load after patch-Z: update it in place");
            A.Eq("patch-Z.MPQ", plan.PatchName); A.Eq(null, plan.TargetDir);
            A.True(plan.Outranked.Contains(@"enUS\patch-enUS-Z.MPQ"), "Studio's old locale patch is reported as unused");
            // only base patches up to M: the next base letter wins
            var p2 = new GameShip.Plan { Locale = "enUS" };
            GameShip.PickName(p2, new[] { Root("patch-M.MPQ"), Loc("patch-enUS-3.MPQ") }, null);
            A.Eq("patch-N.MPQ", p2.PatchName); A.Eq(null, p2.TargetDir); A.False(p2.InPlace, "new patch");
            // only locale patches hold them: a base patch-2 already wins
            var p3 = new GameShip.Plan { Locale = "enUS" };
            GameShip.PickName(p3, new[] { Loc("patch-enUS-Z.MPQ") }, null);
            A.Eq("patch-2.MPQ", p3.PatchName);
            // Studio's own base patch that still wins is reused
            var p4 = new GameShip.Plan { Locale = "enUS" };
            GameShip.PickName(p4, new[] { Root("patch-M.MPQ"), Root("patch-T.MPQ") }, new[] { "patch-T.MPQ" });
            A.Eq("patch-T.MPQ", p4.PatchName, "reuse own");
            A.True(PatchNames.IsLoadable("patch-Z.MPQ", out _) && PatchNames.IsLocaleLoadable("patch-enUS-Z.MPQ", "enUS", out _), "names");
        }

        [Test] static void Replacing_files_in_an_archive_keeps_everything_else()
        {
            var path = Path.Combine(Path.GetTempPath(), "studio_test_inplace_" + Guid.NewGuid().ToString("N") + ".MPQ");
            try
            {
                var big = new byte[50000]; new Random(3).NextBytes(big);
                using (var fs = File.Create(path)) MpqWriter.Write(fs, new[] { (@"DBFilesClient\Spell.dbc", new byte[] { 1, 2, 3 }), (@"Item\Sword.blp", big), (@"Sound\Hit.wav", new byte[] { 9, 9 }) });
                var spell = new byte[20000]; new Random(5).NextBytes(spell);
                MpqWriter.ReplaceFiles(path, new[] { (@"DBFilesClient\Spell.dbc", spell), (@"DBFilesClient\SkillLineAbility.dbc", new byte[] { 7, 7, 7 }) });
                using var r = new MpqReader(path);
                A.True(r.Read(@"DBFilesClient\Spell.dbc").AsSpan().SequenceEqual(spell), "replaced");
                A.True(r.Read(@"DBFilesClient\SkillLineAbility.dbc").AsSpan().SequenceEqual(new byte[] { 7, 7, 7 }), "added");
                A.True(r.Read(@"Item\Sword.blp").AsSpan().SequenceEqual(big), "untouched file intact");
                A.True(r.Read(@"Sound\Hit.wav").AsSpan().SequenceEqual(new byte[] { 9, 9 }), "untouched small file intact");
                var list = System.Text.Encoding.UTF8.GetString(r.Read("(listfile)"));
                A.Contains(list, @"DBFilesClient\SkillLineAbility.dbc"); A.Contains(list, @"Item\Sword.blp");
                A.Eq(0, MpqWriter.Verify(path, new[] { (@"DBFilesClient\Spell.dbc", spell) }).Count, "verifies");
            }
            finally { try { File.Delete(path); } catch { } }
        }

        /// <summary>The director's second failure: Studio itself keeps the client's archives open, and Windows won't rename over an open file.</summary>
        [Test] static void Updating_a_patch_in_place_works_while_Studio_has_it_open()
        {
            var root = Path.Combine(Path.GetTempPath(), "studio_test_open_" + Guid.NewGuid().ToString("N"));
            var data = Path.Combine(root, "Data"); Directory.CreateDirectory(data);
            try
            {
                var target = Path.Combine(data, "patch-Z.MPQ");
                var other = new byte[5000]; new Random(2).NextBytes(other);
                using (var fs = File.Create(target)) MpqWriter.Write(fs, new[] { (@"DBFilesClient\Spell.dbc", new byte[] { 1 }), (@"Character\Other.m2", other) });
                using var held = new MpqReader(target);                       // like Studio's own client reader
                var inst = new PatchInstaller(Path.Combine(root, "state.json"), Path.Combine(root, "backups"), _ => new List<string>());
                var rec = inst.UpdateInPlace(root, "patch-Z.MPQ", new[] { (@"DBFilesClient\Spell.dbc", new byte[] { 4, 5, 6 }) });
                A.True(rec.InPlace && File.Exists(rec.BackupOfPrevious), "recorded with a backup");
                using var r = new MpqReader(target);
                A.True(r.Read(@"DBFilesClient\Spell.dbc").AsSpan().SequenceEqual(new byte[] { 4, 5, 6 }), "updated");
                A.True(r.Read(@"Character\Other.m2").AsSpan().SequenceEqual(other), "rest intact");
                A.False(File.Exists(target + ".studio-new"), "temp file removed");
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        /// <summary>The director's failure: Studio's patch in Data\enUS lost to an older Data\patch-Z with the hidden row. Updating patch-Z in place wins.</summary>
        [Test] static void A_spell_sent_into_the_patch_that_loads_last_is_what_the_client_reads()
        {
            var spells = Server("Spell.dbc");
            if (spells == null) return;
            var dir = Path.Combine(Path.GetTempPath(), "studio_test_chain_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(dir, "enUS"));
            try
            {
                var copy = spells.Find(1243).Clone(); copy.V[0] = 900700;
                var older = spells.Clone(); var hidden = copy.Clone(); older.Rows.Add(hidden); older.InvalidateIndex();
                new SpellData(older, hidden).Set("Attributes", "0x1C0", out _);
                var other = new byte[3000]; new Random(1).NextBytes(other);
                using (var fs = File.Create(Path.Combine(dir, "patch-Z.MPQ"))) MpqWriter.Write(fs, new[] { (@"DBFilesClient\Spell.dbc", older.Write()), (@"Character\Other.m2", other) });
                // what an older Studio sent: the visible row, but in Data\enUS\patch-enUS-Z.MPQ
                var mine = spells.Clone(); var vis = copy.Clone(); mine.Rows.Add(vis); mine.InvalidateIndex();
                new SpellData(mine, vis).Set("Attributes", "0x0", out _);
                var sla = EmptySla(); SpellBook.AddLink(sla, 900700, 26, 1, autoLearn: true);
                using (var fs = File.Create(Path.Combine(dir, "enUS", "patch-enUS-Z.MPQ"))) MpqWriter.Write(fs, new[] { (@"DBFilesClient\Spell.dbc", mine.Write()) });
                using (var c = new ClientFiles(dir))
                {
                    var t = DbcTable.Read("Spell.dbc", c.Read(@"DBFilesClient\Spell.dbc", out var from));
                    A.Eq("patch-Z.MPQ", from, "the client reads patch-Z, not the locale patch");
                    A.Eq(0x1C0u, new SpellData(t, t.Find(900700)).U("Attributes"), "so it sees the hidden row: known but not in the book, /cast can't find it");
                }
                // Send to game now: the plan updates patch-Z in place
                var src = new ClientDbcSource(dir, null);
                var plan = new GameShip.Plan { Locale = "enUS" };
                GameShip.PickName(plan, src.Archives.Select(a => (a.Name, a.Rank, a.Dbcs.Contains("Spell.dbc"))), new[] { "patch-enUS-Z.MPQ" });
                A.True(plan.InPlace && plan.PatchName == "patch-Z.MPQ", "in place: " + plan.PatchName);
                MpqWriter.ReplaceFiles(Path.Combine(dir, "patch-Z.MPQ"), new[] { (@"DBFilesClient\Spell.dbc", mine.Write()), (@"DBFilesClient\SkillLineAbility.dbc", sla.Write()) });
                using (var c = new ClientFiles(dir))
                {
                    var t = DbcTable.Read("Spell.dbc", c.Read(@"DBFilesClient\Spell.dbc", out var from));
                    A.Eq("patch-Z.MPQ", from);
                    var sd = new SpellData(t, t.Find(900700));
                    A.Eq(0u, sd.U("Attributes") & (SpellBook.AttrDoNotDisplay | SpellBook.AttrPassive), "visible now");
                    A.Eq(new SpellData(mine, vis).Name, sd.Name, "named as on the server");
                    var slaWon = DbcTable.Read("SkillLineAbility.dbc", c.Read(@"DBFilesClient\SkillLineAbility.dbc", out _));
                    A.True(SpellBook.AutoLearned(slaWon, 900700), "linked and learned automatically");
                    A.True(c.Read(@"Character\Other.m2", out _).AsSpan().SequenceEqual(other), "the rest of patch-Z is untouched");
                }
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
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
