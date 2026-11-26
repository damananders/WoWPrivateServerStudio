using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using AndersCraftStudio.Core;
using AndersCraftStudio.Core.Live;

namespace AndersCraftStudio
{
    /// <summary>
    /// --screens &lt;dir&gt; [--demo-conf]: walks every screen off-screen and saves PNGs, then exits.
    /// Uses a throw-away settings/changesets folder. With --demo-conf the browser reads through the worldserver.conf
    /// login in a READ ONLY session (for screenshots before studio_ro exists). Nothing is ever applied.
    /// </summary>
    static class Screens
    {

        static async Task Wait(int ms = 400) { await Task.Delay(ms); }

        public static async Task Run(MainWindow w, string dir)
        {
            Directory.CreateDirectory(dir);
            var S = w.S;
            bool demo = S.DemoConf;
            S.DemoConf = false;

            // ---- wizard
            var wiz = w.Wizard;
            await w.Snap("01_wizard_welcome.png");
            wiz.Go(2);
            var conf = S.Profile?.ConfPath; wiz.UseConf(conf != null && File.Exists(conf) ? conf : null);
            await Wait(1800);
            wiz.Go(2);
            await w.Snap("02_wizard_find_server.png");
            wiz.Go(3);
            wiz.TestAccountsNow();          // real login attempt with studio_ro / studio_rw: only reads grants if it works
            await Wait(3500);
            await w.Snap("03_wizard_accounts.png");
            wiz.Go(4);
            await w.Snap("04_wizard_client.png");
            S.DemoConf = demo;              // lets the wizard read the table list for the sandbox plan
            wiz.Go(5);
            wiz.BuildSandboxScript();
            await Wait(2500);
            await w.Snap("05_wizard_sandbox.png");

            // ---- finish → browser without a verified account
            S.DemoConf = false;
            wiz.FinishNow();
            var verified = S.Profile.AccountsVerifiedUtc;
            S.Profile.AccountsVerifiedUtc = null;          // show the "finish step 3" state once
            w.WizardDone(); w.Navigate("sql");
            await Wait(600);
            w.HideToast();
            await w.Snap("06_sql_setup_needed.png");
            S.Profile.AccountsVerifiedUtc = verified;
            if (!demo && !S.ScreensRealAccount) return;

            // ---- browser with data: your studio_ro account, or the read-only demo connection
            S.DemoConf = demo && !S.ScreensRealAccount;
            w.WizardDone(); w.Navigate("sql");
            await Wait(1500);
            var sql = (SqlBrowserPage)w.GetPage("sql");
            sql.OpenTable("world", "creature_template");
            w.HideToast();
            await Wait(800);
            await w.Snap("07_sql_browser.png");
            sql.ScreensFilter("name LIKE '%Wolf%'");
            await Wait(800);
            sql.ScreensEditCell("minlevel", "12");
            await Wait(600);
            await w.Snap("08_sql_browser_staged_edit.png");
            sql.SetQuery("SELECT entry, name, subname, minlevel, maxlevel, faction, `rank`\nFROM creature_template\nWHERE name LIKE '%Defias%'\nORDER BY entry\nLIMIT 50;");
            sql.RunQuery();
            await Wait(900);
            w.HideToast();
            await w.Snap("09_query_select.png");
            sql.SetQuery("-- typed write statements are staged, never run\nUPDATE creature_template\nSET HealthModifier = HealthModifier * 2\nWHERE entry IN (38, 116);");
            sql.RunQuery();
            await Wait(900);
            await w.Snap("10_query_write_staged.png");
            sql.SetQuery("DROP TABLE creature_template;");
            sql.RunQuery();
            await Wait(500);
            sql.SetQuery("GRANT ALL ON *.* TO 'x'@'%';");
            sql.RunQuery();
            await Wait(500);
            await w.Snap("11_query_blocked.png");

            // ---- changes
            w.Navigate("changes");
            await Wait(700);
            await w.Snap("12_changes_diff.png");
            var ch = (ChangesPage)w.GetPage("changes");
            ch.ScreensDetailTab(1); await Wait(300);
            await w.Snap("13_changes_sql.png");
            ch.ScreensDetailTab(2); await Wait(300);
            await w.Snap("14_changes_undo_sql.png");
            var raw = S.Changes.Pending.Changes.FirstOrDefault(c => c.Kind == ChangeKind.Raw && !c.Structure);
            if (raw != null) { ch.Select(raw.Id); ch.ScreensDetailTab(2); await Wait(300); await w.Snap("15_changes_typed_sql_undo.png"); }
            ch.ScreensDetailTab(0);
            ch.ScreensConfirm = "APPLY";
            await Wait(300);
            await w.Snap("16_changes_apply_panel.png");

            // ---- history: a clearly-labelled sample record in the throw-away folder (no apply happened)
            var sample = new Changeset
            {
                Name = "Sample record for this screenshot (nothing was applied)", State = ChangesetState.Applied, AppliedTarget = "Sandbox", AppliedDatabase = S.Profile.SandboxDb,
                Changes = S.Changes.Pending.Changes.Where(c => !c.Structure).Take(2).ToList(),
            };
            sample.Log.Add(new ApplyRecord { Utc = DateTime.UtcNow, Target = "Sandbox", Database = S.Profile.SandboxDb, BackupDir = Path.Combine(S.Profiles.Settings.BackupRoot, "(sample)"), Success = true, Kind = "Apply", Message = "sample" });
            S.Changes.Save(sample);
            ch.ScreensShowHistory(sample.Id);
            await Wait(400);
            await w.Snap("17_history_rollback.png");

            // ---- settings, placeholders, search
            w.Navigate("settings");
            await Wait(400);
            w.HideToast();
            await w.Snap("18_settings.png");
            w.Navigate("creatures");
            await Wait(300);
            await w.Snap("19_coming_soon.png");
            w.Navigate("sql");
            await Wait(300);
            await w.SnapSearch("20_search.png", "loot");

            await Milestone2(w);
            await Milestone3(w);
            await Milestone4(w);
            await Milestone5(w);
            await Milestone6(w);
            await Milestone7(w);
            await Milestone8(w);
            await Milestone9(w);
            await Milestone10(w);
            await Milestone11(w);
        }

        /// <summary>The facelift check: Home and a creature record, same content every run (before / after).</summary>
        public static async Task RunM14(MainWindow w, string dir)
        {
            Directory.CreateDirectory(dir);
            w.Navigate("home"); await Wait(1200); w.HideToast();
            await w.Snap("170_facelift_home.png");
            EntityEditorPage pg = null;
            w.Navigate("creatures", p => pg = (EntityEditorPage)p);
            pg.Open(448); w.HideToast(); await Wait(2500);
            await w.Snap("171_facelift_creature.png");
        }

        public static async Task RunM13(MainWindow w, string dir)
        {
            Directory.CreateDirectory(dir);
            await Milestone13(w, dir);
        }

        /// <summary>The builders, with sample content, staged into the throw-away changeset; then every staged column is checked against the real tables (read-only).</summary>
        static async Task Milestone13(MainWindow w, string dir)
        {
            var S = w.S;
            w.Navigate("quests"); await Wait(500);
            var qw = new QuestBuildWindow(w); qw.Show(); await Wait(500);
            await w.SnapWindow(qw, "150_quest_builder_start.png");
            qw.ScreensSample();
            foreach (var (st, file) in new[] { (1, "151_quest_builder_basics.png"), (3, "152_quest_builder_objectives.png"), (4, "153_quest_builder_rewards.png"), (7, "154_quest_builder_review.png") })
            { qw.ScreensGo(st); await Wait(800); await w.SnapWindow(qw, file); }
            qw.ScreensStage();
            var vw = new QuestBuildWindow(w, 7); vw.Show(); await Wait(700);
            vw.ScreensGo(0); await Wait(500); await w.SnapWindow(vw, "155_quest_builder_from_game.png");
            vw.ScreensGo(7); await Wait(800); await w.SnapWindow(vw, "156_quest_builder_copy_review.png");
            vw.Close();
            var nw = new NpcBuildWindow(w); nw.Show(); await Wait(500);
            nw.ScreensSample();
            foreach (var (st, file) in new[] { (1, "157_npc_builder_kind.png"), (2, "158_npc_builder_look.png"), (4, "159_npc_builder_services.png"), (5, "160_npc_builder_where.png"), (6, "161_npc_builder_review.png") })
            { nw.ScreensGo(st); await Wait(st == 2 ? 2500 : 800); await w.SnapWindow(nw, file); }
            nw.ScreensStage();
            var bw = new BehaviourWindow(w); bw.Show(); await Wait(400);
            bw.ScreensGo(1); await Wait(500); await w.SnapWindow(bw, "162_behaviour_builder_pick.png");
            bw.ScreensSet(68, "say_aggro"); await Wait(600); await w.SnapWindow(bw, "163_behaviour_builder_details.png");
            bw.ScreensGo(3); await Wait(700); await w.SnapWindow(bw, "164_behaviour_builder_review.png");
            bw.ScreensStage();
            // check: every staged column exists in its table (read-only INFORMATION_SCHEMA)
            var lines = new List<string>();
            using (var db = S.OpenRead(S.Profile.WorldDb))
                foreach (var g in S.Changes.Pending.Changes.Where(c => c.Dbc == null).GroupBy(c => c.Table))
                {
                    var cols = new HashSet<string>(db.Query("SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = " + SqlGen.Literal(g.Key), 500).Rows.Select(r => Convert.ToString(r[0])), StringComparer.OrdinalIgnoreCase);
                    var missing = g.SelectMany(c => c.Fields.Select(f => f.Name).Concat(c.Key.Select(k => k.Column))).Distinct().Where(c => !cols.Contains(c)).ToList();
                    lines.Add($"{g.Key}: {g.Count()} change(s) [{string.Join(", ", g.Select(c => c.Kind).Distinct())}]" + (cols.Count == 0 ? " TABLE MISSING" : missing.Count > 0 ? " MISSING COLUMNS: " + string.Join(", ", missing) : " ok"));
                    foreach (var c in g.Take(3)) lines.Add("    " + c.Summary + " | " + string.Join("; ", c.Fields.Take(12).Select(f => f.Name + "=" + (f.AfterDisplay ?? f.After))));
                }
            File.WriteAllLines(Path.Combine(dir, "builder_check.txt"), lines);
        }

        public static async Task RunM12(MainWindow w, string dir)
        {
            Directory.CreateDirectory(dir);
            await Milestone12(w);
        }

        /// <summary>Server control with sample settings, characters and accounts (nothing is started, stopped or sent).</summary>
        static async Task Milestone12(MainWindow w)
        {
            w.Hub.ScreensSample(Path.Combine(Path.GetTempPath(), "studio_screens_server"));
            w.Navigate("sc_dash"); await w.Hub.Refresh(true); await Wait(900); w.HideToast();
            await w.Snap("140_server_dashboard.png");
            var realms = w.Hub.Cfg.Realms;
            var races = new[] { "Human", "Orc", "Dwarf", "Night Elf", "Undead", "Tauren", "Gnome", "Troll", "Goblin", "Blood Elf", "Draenei" };
            var classes = new[] { "Warrior", "Paladin", "Hunter", "Rogue", "Priest", "Death Knight", "Shaman", "Mage", "Warlock", "Druid", "Druid" };
            var zones = new[] { "Dalaran", "Icecrown", "Stormwind City", "Orgrimmar", "Sholazar Basin", "Elwynn Forest", "Wintergrasp", "The Storm Peaks" };
            var names = new[] { "Aldren", "Brisa", "Corvin", "Dagny", "Ebbe", "Fenna", "Gorm", "Hilde" };
            var rows = names.Select((n, i) => new CharRow { Guid = (uint)(100 + i), Name = n, Level = new[] { 80, 72, 80, 64, 80, 23, 80, 58 }[i], Race = races[(i * 3) % 11], Class = classes[(i * 5) % 11],
                Realm = realms[i % 2], Account = "PLAYER" + (i + 1), Online = i % 3 != 2, Zone = zones[i], Money = (i + 1) * 1234567L, TotalTime = (i + 2) * 86000L, LogoutUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - i * 7200 }).ToList();
            ScPlayersPage pl = null; w.Navigate("sc_players", p => pl = (ScPlayersPage)p); pl.ScreensData(rows); await Wait(600);
            await w.Snap("141_server_players.png");
            var pw = new PlayerWindow(w, rows[0]); pw.Show(); await Wait(700);
            await w.SnapWindow(pw, "142_server_player_window.png"); pw.Close();
            ScAccountsPage ac = null; w.Navigate("sc_accounts", p => ac = (ScAccountsPage)p);
            ac.ScreensData(Enumerable.Range(1, 8).Select(i => new AccountRow { Id = (uint)i, Name = "PLAYER" + i, Rank = i == 1 ? 3 : i == 2 ? 1 : 0, Expansion = 2, Online = i % 3 != 0, Characters = 1 + i % 3, LastLogin = DateTime.Now.AddHours(-i * 5).ToString("yyyy-MM-dd HH:mm"), Banned = i == 7, Muted = i == 5 }).ToList());
            await Wait(500); await w.Snap("143_server_accounts.png");
            w.Navigate("sc_console"); await Wait(500); await w.Snap("144_server_console.png");
            w.Navigate("sc_logs"); await Wait(700); await w.Snap("145_server_logs.png");
            w.Navigate("sc_schedule"); await Wait(500); await w.Snap("146_server_schedule.png");
            w.Navigate("sc_backups"); await Wait(500); await w.Snap("147_server_backups.png");
            w.Navigate("sc_settings"); await Wait(500); await w.Snap("148_server_settings.png");
        }

        public static async Task RunM11(MainWindow w, string dir)
        {
            Directory.CreateDirectory(dir);
            await Milestone11(w);
        }

        /// <summary>The spell overhaul: guided Create a spell, the spellbook check, and Send to game (plan only, nothing installed).</summary>
        static async Task Milestone11(MainWindow w)
        {
            SpellEditorPage se = null;
            w.Navigate("spells", p => se = (SpellEditorPage)p);
            await Wait(600);
            var tabs = SpellBook.ClassTabs(se.WorkTable("SkillLine.dbc"), se.WorkTable("SkillRaceClassInfo.dbc"));
            var dlg = new CreateSpellWindow(w, se, tabs);
            dlg.ScreensSet(SpellRecipe.Kind.Damage, "Ember Lance", 180);
            dlg.Show(); await Wait(900);
            await w.SnapWindow(dlg, "126_spells_create_guided.png");
            dlg.Close();
            // the same spell made for real (throw-away changeset), shown in the editor with its spellbook check
            se.ScreensCreate(new SpellRecipe.Recipe { Kind = SpellRecipe.Kind.Damage, Name = "Ember Lance", Amount = 180, CastMs = 1500, CooldownSec = 6, RangeYards = 30, School = 4 }, tabs.FirstOrDefault(t => t.SkillLine == 8));
            await Wait(900); w.HideToast();
            await w.Snap("127_spells_spellbook_check.png");
            // a spell like the one in the bug report: hidden + passive, no spellbook link
            se.ScreensSet("Attributes", "0x1C0");
            se.ScreensUnlink();
            await Wait(600);
            await w.Snap("128_spells_spellbook_problems.png");
            se.ScreensSet("Attributes", "0x0");
            se.ScreensRelink(tabs.FirstOrDefault(t => t.SkillLine == 8));
            se.ScreensStage(); await Wait(600); w.HideToast();
            var ship = new GameShipWindow(w);
            ship.Show(); await Wait(800);
            await w.SnapWindow(ship, "129_spells_send_to_game.png");
            ship.Close();
        }

        public static async Task RunM10(MainWindow w, string dir)
        {
            Directory.CreateDirectory(dir);
            await Milestone10(w);
        }

        /// <summary>The Changes screen after the 0.9 fixes: a sent DBC edit with its status, and an Apply that stopped at one change.</summary>
        static async Task Milestone10(MainWindow w)
        {
            var S = w.S;
            var keep = S.Changes.Pending.Changes.ToList();
            S.Changes.Pending.Changes.Clear();
            // a DBC edit already written to the server copy (sample values; the throw-away changeset only)
            var dbc = new Change
            {
                Kind = ChangeKind.Update, Schema = DbcStager.Schema, Table = "Spell.dbc", Category = "Client data (DBC)", Summary = "Edit Spell.dbc 900001 (Sample spell): CastingTimeIndex",
                Key = new List<KeyPart> { new KeyPart("ID", "900001") },
                Dbc = new DbcPatch { File = "Spell.dbc", Source = "server", Key = 900001, SentToServerUtc = DateTime.UtcNow.AddMinutes(-3), ServerBackupDir = Path.Combine(S.Profiles.Settings.BackupRoot, "dbc_(sample)"), Fields = { new DbcFieldEdit { Field = 28, Name = "CastingTimeIndex", Before = "1", After = "6" } } },
            };
            dbc.Fields.Add(new FieldChange { Name = "CastingTimeIndex", Before = "1", After = "6", BeforeDisplay = "1", AfterDisplay = "6" });
            S.Changes.Pending.Changes.Add(dbc);
            S.SaveChanges();
            w.Navigate("changes");
            var ch = (ChangesPage)w.GetPage("changes");
            ch.Select(dbc.Id);
            await Wait(700);
            w.HideToast();
            await w.Snap("124_changes_dbc_sent_status.png");
            // a database edit that Apply stopped at (shown as it would be; nothing is applied)
            S.Changes.Pending.Changes.Clear();
            var ed = ChangeFactory.StageCellEdit(S.Changes.Pending, "quest_template", new List<KeyPart> { new KeyPart("ID", "900100") }, "LogTitle", "'A sample quest'", "A sample quest", "'A better sample quest'", "A better sample quest");
            S.SaveChanges();
            ch.Select(ed.Id);
            ch.ScreensConflict(ed.Id, ConflictKind.Changed, $"\"{ed.Summary}\": in the database, LogTitle is now 'A sample quest (edited)', but when you staged this edit it was 'A sample quest'. Something changed that row since (an earlier Apply, the game server, or another tool). Nothing was changed.");
            await Wait(600);
            await w.Snap("125_changes_apply_stopped.png");
            ch.ScreensConflict(null, ConflictKind.None, null);
            S.Changes.Pending.Changes.Clear();
            S.Changes.Pending.Changes.AddRange(keep);
            S.SaveChanges();
            // the "How to use this" guides, every part
            w.Navigate("creatures"); await Wait(300);
            foreach (var g in Guides.All)
                for (int i = 0; i < g.Parts.Count; i++)
                {
                    GuideWindow.ShowFor(w, g.Key);
                    GuideWindow.Open.ScreensPart(i);
                    await Wait(500);
                    await w.SnapWindow(GuideWindow.Open, $"130_guide_{g.Key}{(g.Parts.Count > 1 ? "_" + (i + 1) : "")}.png");
                }
            GuideWindow.Open?.Close();
        }

        static IEnumerable<string> WmoDisplays(Studio S)
        {
            var f = S.Models.Raw("GameObjectDisplayInfo.dbc");
            if (f == null) yield break;
            for (int i = 0; i < f.Records; i++) { var s = f.Str(i, 1); if (s != null && s.EndsWith(".wmo", StringComparison.OrdinalIgnoreCase) && S.Models.Read(s) != null) yield return f.UInt(i, 0).ToString(); }
        }

        public static async Task RunM9(MainWindow w, string dir)
        {
            Directory.CreateDirectory(dir);
            await Milestone9(w);
        }

        static async Task Milestone9(MainWindow w)
        {
            var S = w.S;
            string Q(string sql) { try { using var db = S.OpenRead(S.Profile.WorldDb); return db.Scalar(sql); } catch { return null; } }
            async Task<EntityEditorPage> Open(string ws, long id, string tab)
            {
                EntityEditorPage pg = null;
                w.Navigate(ws, p => pg = (EntityEditorPage)p);
                pg.Open(id);
                if (tab != null) pg.ShowTab(tab);
                w.HideToast();
                await Wait(1500);
                return pg;
            }
            async Task Shot(EntityEditorPage pg, string file, int anim = -1, double frame = -1, bool wire = false)
            {
                var v = pg.lastViewer;
                if (v == null) return;
                if (anim >= 0) { v.SelectAnimation(anim); await Wait(300); }
                if (frame >= 0) v.ShowFrame(frame);
                if (wire) v.ScreensWire(true);
                ((FrameworkElement)v.View).BringIntoView(); await Wait(800);
                await w.Snap(file);
                if (wire) v.ScreensWire(false);
            }
            var wolf = await Open("creatures", 69, "Basics");                 // Diseased Timber Wolf
            await Shot(wolf, "110_viewer_creature_wolf.png", 4, 400);
            await Shot(wolf, "111_viewer_wireframe.png", 0, 200, true);
            var sword = await Open("items", 25, "Display");                    // Worn Shortsword
            await Shot(sword, "112_viewer_item_sword.png");
            if (long.TryParse(Q("SELECT g.entry FROM gameobject_template g WHERE g.type = 3 AND g.name LIKE '%Chest%' AND g.displayId IN (10, 259, 75, 2450) ORDER BY g.entry LIMIT 1") ?? Q("SELECT entry FROM gameobject_template WHERE type = 3 AND name LIKE '%Chest%' ORDER BY entry LIMIT 1"), out var chest))
            {
                var go = await Open("gameobjects", chest, "Basics");
                await Shot(go, "113_viewer_gameobject_chest.png");
            }
            var boss = await Open("creatures", 448, "Basics");                 // Hogger
            await Shot(boss, "114_viewer_creature_hogger.png", 16, 300);
            // dressed characters and a building
            var guard = await Open("creatures", 68, "Basics");                 // Stormwind City Guard
            await Shot(guard, "118_viewer_character_guard.png", 0, 300);
            var marshal = await Open("creatures", 197, "Basics");              // Marshal McBride
            await Shot(marshal, "119_viewer_character_marshal.png", 0, 300);
            if (long.TryParse(Q("SELECT g.entry FROM gameobject_template g WHERE g.displayId IN (" + string.Join(",", WmoDisplays(S).Take(40)) + ") ORDER BY g.entry LIMIT 1"), out var wmoGo))
            {
                var bg = await Open("gameobjects", wmoGo, "Basics");
                await Shot(bg, "120_viewer_wmo_building.png");
            }
            // spell effects: particles and ribbons (Fireball, Frostbolt)
            foreach (var (spellId, file) in new[] { (133u, "121_viewer_spell_fireball.png"), (116u, "122_viewer_spell_frostbolt.png") })
            {
                SpellEditorPage se = null;
                w.Navigate("spells", p => se = (SpellEditorPage)p);
                se.Open(spellId, true); await Wait(800);
                se.ScreensTab("Visuals"); await Wait(1500);
                var ev = se.lastEffects;
                if (ev != null) { ev.WarmUpEffects(1.5); ((FrameworkElement)ev.View).BringIntoView(); await Wait(800); await w.Snap(file); }
            }
            // body armour textures previewed on a character
            {
                var png = Path.Combine(Path.GetTempPath(), "studio_screens_armor.png");
                MakeDemoPng(png);
                AssetsPage ap2 = null;
                w.Navigate("assets", p => ap2 = (AssetsPage)p);
                ap2.ScreensKind("armortex"); await Wait(300);
                ap2.ScreensArmor(3, png); ap2.ScreensArmor(4, png); ap2.ScreensArmor(0, png); await Wait(1500);
                ap2.LastArmorPreview?.BringIntoView(); await Wait(800);
                await w.Snap("123_assets_armor_textures.png");
            }
            // asset import: preview a model from disk (the wolf, copied out of the client into a temp folder)
            var tmp = Path.Combine(Path.GetTempPath(), "studio_screens_model");
            Directory.CreateDirectory(tmp);
            foreach (var f in new[] { @"Creature\Wolf\Wolf.m2", @"Creature\Wolf\Wolf00.skin" }.Concat((S.Models.Creature(long.TryParse(Q("SELECT CreatureDisplayID FROM creature_template_model WHERE CreatureID = 69 LIMIT 1"), out var wd) ? wd : 0).CreatureSkins ?? new string[0]).Where(x => !string.IsNullOrEmpty(x)).Select(x => @"Creature\Wolf\" + x + ".blp")))
            {
                var b = S.Models.Read(f);
                if (b != null) File.WriteAllBytes(Path.Combine(tmp, Path.GetFileName(f)), b);
            }
            if (File.Exists(Path.Combine(tmp, "Wolf.m2")))
            {
                AssetsPage ap = null;
                w.Navigate("assets", p => ap = (AssetsPage)p);
                ap.ScreensKind("model"); await Wait(300);
                ap.UseFile(Path.Combine(tmp, "Wolf.m2")); await Wait(1500);
                await w.Snap("115_viewer_asset_import.png");
            }
            // several DBCs open as tabs
            DbcPage dp = null;
            w.Navigate("dbc", p => dp = (DbcPage)p);
            foreach (var f in new[] { "Spell.dbc", "Map.dbc", "Faction.dbc", "ItemDisplayInfo.dbc" }) { dp.OpenTable(f); await Wait(500); }
            dp.OpenTable("Map.dbc"); await Wait(600);
            await w.Snap("116_dbc_tabs.png");
            // script tables edited by matching every column
            var sid = Q("SELECT id FROM event_scripts GROUP BY id HAVING COUNT(*) BETWEEN 2 AND 6 LIMIT 1");
            if (sid != null)
            {
                WorldTablesPage wt = null;
                w.Navigate("world", p => wt = (WorldTablesPage)p);
                wt.ScreensLoad("scripts", sid); await Wait(900);
                await w.Snap("117_world_scripts_keyless.png");
            }
        }

        public static async Task RunM8(MainWindow w, string dir)
        {
            Directory.CreateDirectory(dir);
            await Milestone8(w);
        }

        static async Task Milestone8(MainWindow w)
        {
            var S = w.S;
            string Q(string sql) { try { using var db = S.OpenRead(S.Profile.WorldDb); return db.Scalar(sql); } catch { return null; } }
            async Task<EntityEditorPage> Open(string ws, long id, string tab)
            {
                EntityEditorPage pg = null;
                w.Navigate(ws, p => pg = (EntityEditorPage)p);
                pg.Open(id);
                if (tab != null) pg.ShowTab(tab);
                w.HideToast();
                await Wait(900);
                return pg;
            }
            // a creature without a template addon: the stand-in row, edited
            if (long.TryParse(Q("SELECT c.entry FROM creature_template c LEFT JOIN creature_template_addon a ON a.entry = c.entry WHERE a.entry IS NULL AND c.entry BETWEEN 40 AND 2000 ORDER BY c.entry LIMIT 1"), out var cr))
            {
                var pg = await Open("creatures", cr, "Look & movement");
                pg.ScreensEdit("emote", "69"); pg.ScreensFocus("emote"); await Wait(400);
                await w.Snap("100_creature_look_movement.png");
                pg.ScreensDiscard();
                await Open("creatures", cr, "Extras");
                await w.Snap("101_creature_extras.png");
            }
            if (long.TryParse(Q("SELECT q.ID FROM quest_template q JOIN quest_offer_reward r ON r.ID = q.ID JOIN quest_request_items i ON i.ID = q.ID WHERE q.ID < 1000 ORDER BY q.ID LIMIT 1"), out var qid))
            {
                await Open("quests", qid, "Texts");
                await w.Snap("102_quest_texts.png");
                await Open("quests", qid, "Starters/enders");
                await w.Snap("103_quest_givers_editable.png");
            }
            WorldTablesPage wt = null;
            w.Navigate("world", p => wt = (WorldTablesPage)p);
            var pool = Q("SELECT pool_entry FROM pool_gameobject GROUP BY pool_entry HAVING COUNT(*) BETWEEN 3 AND 8 LIMIT 1");
            if (pool != null) { wt.ScreensLoad("pools", pool); await Wait(900); await w.Snap("104_world_pools.png"); }
            if (long.TryParse(Q("SELECT id FROM game_tele ORDER BY id LIMIT 1"), out var tele)) { await Open("tele", tele, null); await w.Snap("105_teleports.png"); }
            // admin console: the login screen and a confirmation (nothing connects or runs)
            AdminPage adm = null;
            w.Navigate("admin", p => adm = (AdminPage)p); adm.ScreensNeutralLogin(); await Wait(500);
            await w.Snap("106_admin_login.png");
            var dlg = new AdminConfirmWindow(w, "Drop table my_test_table", new[] { "DROP TABLE `my_test_table`;" }, AdminRisk.Destructive, "my_test_table", "First save a copy of my_test_table (structure + data) to Documents\\AndersCraft Studio\\dumps");
            dlg.Show(); await Wait(400);
            await w.SnapWindow(dlg, "107_admin_confirm.png");
            dlg.Close();
        }

        /// <summary>--screens dir --m7: only the 0.7 screens (game objects, NPC text, page text, CSV import, item models).</summary>
        public static async Task RunM7(MainWindow w, string dir)
        {
            Directory.CreateDirectory(dir);
            await Milestone7(w);
        }

        static async Task Milestone7(MainWindow w)
        {
            var S = w.S;
            string Q(string sql) { try { using var db = S.OpenRead(S.Profile.WorldDb); return db.Scalar(sql); } catch { return null; } }
            async Task<EntityEditorPage> Open(string ws, long id, string tab)
            {
                EntityEditorPage pg = null;
                w.Navigate(ws, p => pg = (EntityEditorPage)p);
                pg.Open(id);
                if (tab != null) pg.ShowTab(tab);
                w.HideToast();
                await Wait(800);
                return pg;
            }
            // a chest with loot: type-aware data, then its loot
            if (long.TryParse(Q("SELECT g.entry FROM gameobject_template g WHERE g.type = 3 AND g.Data1 > 0 AND g.name LIKE '%Chest%' AND EXISTS (SELECT 1 FROM gameobject_loot_template l WHERE l.Entry = g.Data1) ORDER BY g.entry LIMIT 1"), out var chest))
            {
                var go = await Open("gameobjects", chest, "Type data");
                go.ScreensFocus("Data1"); await Wait(300);
                await w.Snap("95_gameobject_chest_type_data.png");
                await Open("gameobjects", chest, "Loot");
                await w.Snap("96_gameobject_loot.png");
                await Open("gameobjects", chest, "Basics");
                await w.Snap("96b_gameobject_basics.png");
            }
            // a book: page text read as a chain
            if (long.TryParse(Q("SELECT p.ID FROM page_text p WHERE p.NextPageID > 0 AND EXISTS (SELECT 1 FROM item_template i WHERE i.PageText = p.ID) ORDER BY p.ID LIMIT 1"), out var page))
            {
                await Open("pagetext", page, "Page");
                await w.Snap("97_pagetext_book.png");
                await Open("pagetext", page, "Used by");
                await w.Snap("97b_pagetext_used_by.png");
            }
            // NPC text with several variants
            if (long.TryParse(Q("SELECT n.ID FROM npc_text n WHERE COALESCE(n.text1_0, '') <> '' AND COALESCE(n.text2_0, '') <> '' AND EXISTS (SELECT 1 FROM gossip_menu g WHERE g.TextID = n.ID) ORDER BY n.ID LIMIT 1"), out var nt))
            {
                var pg = await Open("npctext", nt, "Variants 1-4");
                pg.ScreensFocus("Probability0"); await Wait(300);
                await w.Snap("98_npctext_variants.png");
                await Open("npctext", nt, "Used by");
                await w.Snap("98b_npctext_used_by.png");
            }
            // CSV import into item_template: two price changes and one new item, previewed (nothing staged)
            SqlBrowserPage sb = null;
            w.Navigate("sql", p => sb = (SqlBrowserPage)p);
            sb.OpenTable("world", "item_template"); await Wait(700);
            var csv = Path.Combine(Path.GetTempPath(), "studio_screens_items.csv");
            File.WriteAllText(csv, "entry,name,BuyPrice,SellPrice\n25,Worn Shortsword,45,9\n159,Refreshing Spring Water,30,1\n" + S.Profiles.Settings.CustomIdStart + ",Studio Test Blade,5000,1000\n");
            var dlg = sb.ScreensCsvImport(csv);
            if (dlg != null)
            {
                dlg.Show(); await Wait(400);
                dlg.ScreensPreview(); await Wait(600);
                await w.SnapWindow(dlg, "99_sql_csv_import_preview.png");
                dlg.Close();
            }
            // item model import form
            AssetsPage ap = null;
            w.Navigate("assets", p => ap = (AssetsPage)p);
            ap.ScreensKind("itemmodel"); await Wait(500);
            await w.Snap("99b_assets_item_model.png");
        }

        /// <summary>--screens dir --fresh: what a brand-new user sees (no profile, nothing found on the drives).</summary>
        public static async Task RunFresh(MainWindow w, string dir)
        {
            Directory.CreateDirectory(dir);
            await w.Snap("90_fresh_wizard_welcome.png");
            w.Wizard.Go(2);
            w.Wizard.AutoDetectNow();
            await Wait(2500);
            await w.Snap("91_fresh_wizard_nothing_found.png");
            w.WizardDone();
            await Wait(500); w.HideToast();
            await w.Snap("92_fresh_home_not_set_up.png");
            w.Navigate("creatures"); await Wait(300);
            await w.Snap("93_fresh_workspace_not_set_up.png");
            w.Navigate("help"); await Wait(400);
            await w.Snap("94_help_getting_started.png");
        }

        static async Task Milestone6(MainWindow w)
        {
            var S = w.S;
            // home
            w.Navigate("home"); await Wait(900); w.HideToast();
            await w.Snap("83_home.png");
            // templates: preview a boss clone (nothing staged), then stage a vendor
            TemplatesPage tp = null;
            w.Navigate("templates", p => tp = (TemplatesPage)p);
            tp.ScreensOpen("clone-boss", new Dictionary<string, string> { ["creature"] = "448", ["level"] = "60", ["health"] = "3", ["damage"] = "2" });
            tp.Run(false); await Wait(800);
            await w.Snap("84_templates_preview.png");
            tp.ScreensOpen("vendor", new Dictionary<string, string> { ["look"] = "66", ["name"] = "Studio Supplier", ["items"] = "159, 4540, 2512, 2515" });
            tp.Run(true); await Wait(800); w.HideToast();
            await w.Snap("85_templates_vendor_staged.png");
            // world tables
            WorldTablesPage wt = null;
            w.Navigate("world", p => wt = (WorldTablesPage)p);
            long gossipSrc = 0;
            try { using var db = S.OpenRead(S.Profile.WorldDb); gossipSrc = long.Parse(db.Scalar("SELECT SourceGroup FROM conditions WHERE SourceTypeOrReferenceId = 15 GROUP BY SourceGroup HAVING COUNT(*) BETWEEN 2 AND 5 LIMIT 1") ?? "0"); } catch { }
            wt.ScreensLoad("conditions", gossipSrc.ToString(), "", 15); await Wait(900);
            await w.Snap("86_world_conditions.png");
            long path = 0;
            try { using var db = S.OpenRead(S.Profile.WorldDb); path = long.Parse(db.Scalar("SELECT id FROM waypoint_data GROUP BY id HAVING COUNT(*) BETWEEN 4 AND 12 LIMIT 1") ?? "0"); } catch { }
            wt.ScreensLoad("waypoints", path.ToString()); await Wait(900);
            await w.Snap("87_world_waypoints.png");
            // asset import: an icon made from a picture, into the temp work folder
            AssetsPage ap = null;
            w.Navigate("assets", p => ap = (AssetsPage)p);
            ap.ScreensKind("icon");
            var png = Path.Combine(Path.GetTempPath(), "studio_screens_icon.png");
            MakeDemoPng(png);
            ap.UseFile(png); await Wait(400);
            ap.Import(); await Wait(800); w.HideToast();
            await w.Snap("88_assets_icon_imported.png");
            // SQL browser structure tab
            SqlBrowserPage sb = null;
            w.Navigate("sql", p => sb = (SqlBrowserPage)p);
            sb.OpenTable(null, "creature_template"); await Wait(600);
            sb.ScreensStructure(); await Wait(700);
            await w.Snap("89_sql_structure.png");
            // DBC compare dialog
            DbcPage dp = null;
            w.Navigate("dbc", p => dp = (DbcPage)p);
            dp.OpenTable("ChrClasses.dbc"); await Wait(500);
            dp.ScreensColumns(); await Wait(600);
            if (dp.ScreensLastWindow != null) { await w.SnapWindow(dp.ScreensLastWindow, "89b_dbc_columns.png"); dp.ScreensLastWindow.Close(); }
            AboutWindow.ShowAbout(w); await Wait(500);
            if (AboutWindow.Last != null) { await w.SnapWindow(AboutWindow.Last, "89c_about.png"); AboutWindow.Last.Close(); }
        }

        static void MakeDemoPng(string path)
        {
            int n = 128;
            var px = new byte[n * n * 4];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                double dx = x - n / 2.0, dy = y - n / 2.0, r = Math.Sqrt(dx * dx + dy * dy) / (n / 2.0);
                int q = (y * n + x) * 4;
                px[q] = (byte)Math.Clamp(40 + 60 * r, 0, 255); px[q + 1] = (byte)Math.Clamp(180 - 140 * r, 0, 255); px[q + 2] = (byte)Math.Clamp(250 - 80 * r, 0, 255); px[q + 3] = 255;
                if (Math.Abs(dx) < 6 || Math.Abs(dy) < 6) { px[q] = 230; px[q + 1] = 240; px[q + 2] = 255; }
            }
            var bmp = System.Windows.Media.Imaging.BitmapSource.Create(n, n, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, px, n * 4);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder(); enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var fs = File.Create(path); enc.Save(fs);
        }

        static async Task Milestone5(MainWindow w)
        {
            // ---- Spell editor: reads Spell.dbc and the client's icons; edits go only into the temp changeset
            // start from the real Fireball: drop the M4 screenshot's staged test edit of Spell.dbc
            w.S.Changes.Pending.Changes.RemoveAll(c => c.Dbc != null && c.Table == "Spell.dbc");
            w.S.SaveChanges();
            SpellEditorPage sp = null;
            w.Navigate("spells", p => sp = (SpellEditorPage)p);
            await Wait(600);
            sp.ScreensSearch("Fireball");
            sp.Open(133); w.HideToast(); await Wait(900);
            await w.Snap("72_spells_overview.png");
            sp.ScreensTab("Effects"); await Wait(600);
            await w.Snap("73_spells_effects.png");
            sp.ScreensTab("Targeting & range"); await Wait(500);
            await w.Snap("74_spells_range_link.png");
            sp.ScreensTab("Attributes"); await Wait(600);
            await w.Snap("75_spells_attributes.png");
            sp.ScreensTab("Visuals"); await Wait(600);
            await w.Snap("76_spells_visuals.png");
            var ip = sp.ScreensIconPicker();
            ip.Show(); ip.Filter("fire"); await Wait(900);
            await w.SnapWindow(ip, "77_spells_icon_picker.png");
            ip.Close();
            // duplicate as new, rename, change damage, live tooltip
            sp.ScreensDuplicate(); w.HideToast(); await Wait(600);
            sp.ScreensTab("Overview");
            sp.ScreensSet("Name_enUS", "Studio Firebolt");
            sp.ScreensSet("NameSubtext_enUS", "Custom");
            sp.ScreensSet("EffectBasePoints_1", "199");
            sp.ScreensSet("EffectDieSides_1", "101");
            sp.ScreensSet("Description_enUS", "Hurls a blazing bolt that causes $s1 Fire damage and an additional $o2 Fire damage over $d.");
            await Wait(700);
            await w.Snap("78_spells_new_spell_live_tooltip.png");
            sp.ScreensCompare(); await Wait(600);
            if (Dialog.Last != null) { await w.SnapWindow(Dialog.Last, "79_spells_compare.png"); Dialog.Last.Close(); Dialog.Last = null; }
            sp.ScreensStage(); w.HideToast();
            sp.ScreensStageServer(); w.HideToast();
            sp.ScreensTab("Server side"); await Wait(800);
            await w.Snap("80_spells_server_side.png");
            w.Navigate("changes");
            var ch = (ChangesPage)w.GetPage("changes");
            var spc = w.S.Changes.Pending.Changes.LastOrDefault(c => c.Dbc != null && c.Table == "Spell.dbc");
            if (spc != null) ch.Select(spc.Id);
            await Wait(600);
            await w.Snap("81_changes_new_spell.png");
            w.Navigate("spells");
            await Wait(300);
            await w.SnapSearch("82_search_spells.png", "frostbolt");
        }

        static async Task Milestone4(MainWindow w)
        {
            var S = w.S;
            long Scalar(string sql) { try { using var db = S.OpenRead(S.Profile.WorldDb); return long.Parse(db.Scalar(sql) ?? "0"); } catch { return 0; } }

            // ---- DBC tables (reads the server's Data\dbc and the client's MPQs; edits are staged into the temp changeset only)
            DbcPage dp = null;
            w.Navigate("dbc", p => dp = (DbcPage)p);
            await Wait(500);
            dp.OpenTable("Spell.dbc", 133); w.HideToast(); await Wait(900);
            await w.Snap("61_dbc_spell_find_id.png");
            dp.ScreensFilter("Name:fireball SpellLevel>60"); await Wait(600);
            dp.ScreensSelectFirst(); await Wait(400);
            await w.Snap("62_dbc_filter_and_links.png");
            dp.ScreensTableSearch("zone");
            dp.OpenTable("AreaTable.dbc", 12); dp.ScreensAllLocales(true); await Wait(800);
            dp.ScreensScrollToColumn("AreaName"); await Wait(500);
            await w.Snap("63_dbc_localized_columns.png");
            dp.ScreensAllLocales(false);
            dp.ScreensTableSearch("");
            dp.OpenTable("SpellVisualKit.dbc"); await Wait(800);
            await w.Snap("64_dbc_raw_mode.png");
            dp.OpenTable("Spell.dbc", 133); await Wait(600);
            dp.ScreensEdit(133, "Name_enUS", "Fireball (Studio test)");
            dp.ScreensEdit(133, "RangeIndex", "6");
            dp.OpenTable("Spell.dbc", 133); await Wait(400);
            dp.ScreensScrollToColumn("Name"); await Wait(500);
            await w.Snap("65_dbc_unstaged_edit.png");
            dp.ScreensStage(); await Wait(400);
            w.HideToast();
            dp.ScreensSource("client"); await Wait(1500);
            dp.OpenTable("ChrRaces.dbc"); dp.ScreensOnlyDiff(true); await Wait(1200); dp.ScreensOnlyDiff(false); await Wait(300);
            w.HideToast();
            await w.Snap("66_dbc_client_merged_view.png");
            dp.ScreensOnlyDiff(false);
            dp.ScreensSource("server"); await Wait(400);

            // ---- Changes: DBC diff and the two outputs (not clicked)
            w.Navigate("changes");
            var ch = (ChangesPage)w.GetPage("changes");
            var dbc = S.Changes.Pending.Changes.FirstOrDefault(c => c.Dbc != null);
            if (dbc != null) ch.Select(dbc.Id);
            await Wait(600);
            await w.Snap("67_changes_dbc_diff.png");
            ch.ScreensScrollApplyToEnd(); await Wait(400);
            await w.Snap("68_changes_dbc_outputs.png");

            // ---- SmartAI: creature_text lines of a Talk action
            long talker = Scalar("SELECT s.entryorguid FROM smart_scripts s JOIN creature_text t ON t.CreatureID = s.entryorguid AND t.GroupID = s.action_param1 WHERE s.source_type = 0 AND s.action_type = 1 AND s.entryorguid > 0 GROUP BY s.entryorguid HAVING COUNT(*) BETWEEN 2 AND 6 ORDER BY s.entryorguid LIMIT 1");
            if (talker > 0)
            {
                SmartAIPage sp = null;
                w.Navigate("smartai", p => sp = (SmartAIPage)p);
                sp.Open(0, talker); await Wait(800);
                int idx = sp.Rows.FindIndex(r => r.ActionType == 1);
                if (idx >= 0) { sp.ScreensSelect(idx); await Wait(800); sp.ScreensScrollEditorToEnd(); await Wait(400); }
                w.HideToast();
                await w.Snap("69_smartai_creature_text.png");
            }

            // ---- paged lists: the creature with the most spawns
            long many = Scalar($"SELECT {(Scalar("SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'creature' AND COLUMN_NAME = 'id1'") > 0 ? "id1" : "id")} FROM creature GROUP BY 1 HAVING COUNT(*) > 500 ORDER BY COUNT(*) DESC LIMIT 1");
            if (many > 0)
            {
                EntityEditorPage cr = null;
                w.Navigate("creatures", p => cr = (EntityEditorPage)p);
                cr.Open(many); cr.ShowTab("Spawns"); w.HideToast(); await Wait(1200);
                cr.ListEditorFor("spawns")?.GoPage(1); await Wait(800);
                await w.Snap("70_spawns_paged.png");
            }
            w.Navigate("dbc");
            await Wait(300);
            await w.SnapSearch("71_search_dbc_tables.png", "spell");
        }

        static async Task Milestone3(MainWindow w)
        {
            var S = w.S;
            long Scalar(string sql) { try { using var db = S.OpenRead(S.Profile.WorldDb); return long.Parse(db.Scalar(sql) ?? "0"); } catch { return 0; } }

            // ---- loot
            LootPage lp = null;
            w.Navigate("loot", p => lp = (LootPage)p);
            lp.Open("creature_loot_template", 69);
            w.HideToast(); await Wait(800);
            await w.Snap("50_loot_creature.png");
            lp.RunSim(); await Wait(600);
            lp.ScreensScrollToEnd(); await Wait(400);
            await w.Snap("51_loot_simulator.png");
            long withRef = Scalar("SELECT Entry FROM creature_loot_template WHERE Reference <> 0 GROUP BY Entry HAVING COUNT(*) >= 2 ORDER BY COUNT(*) DESC LIMIT 1");
            if (withRef > 0)
            {
                lp.Open("creature_loot_template", withRef); await Wait(600);
                await w.Snap("52_loot_with_references.png");
                long rf = Math.Abs(Scalar($"SELECT Reference FROM creature_loot_template WHERE Entry = {withRef} AND Reference <> 0 LIMIT 1"));
                lp.ScreensBackTo("creature_loot_template", withRef);
                lp.Open("reference_loot_template", rf); await Wait(600);
                await w.Snap("53_loot_reference_drill_in.png");
            }
            lp.Open("creature_loot_template", 69); await Wait(500);
            if (lp.Editor.Data.Rows.Count > 0) lp.Editor.ScreensEditCell(0, "Chance", "75");
            await Wait(400);
            await w.Snap("54_loot_unstaged_edit.png");
            lp.Editor.Stage(); w.HideToast();

            // ---- SmartAI
            long smart = Scalar("SELECT s.entryorguid FROM smart_scripts s JOIN creature_template c ON c.entry = s.entryorguid WHERE s.source_type = 0 AND c.AIName = 'SmartAI' GROUP BY s.entryorguid HAVING COUNT(*) BETWEEN 3 AND 8 ORDER BY s.entryorguid LIMIT 1");
            SmartAIPage sp = null;
            w.Navigate("smartai", p => sp = (SmartAIPage)p);
            if (smart != 0)
            {
                sp.Open(0, smart); w.HideToast(); await Wait(800);
                await w.Snap("55_smartai_builder.png");
                // add a row: in combat, every 8-12s, cast Fireball on the victim
                var r = new SmartRow { EntryOrGuid = smart, SourceType = 0, Id = sp.Rows.Max(x => x.Id) + 1, EventType = 0, ActionType = 11, TargetType = 2, EventChance = 100 };
                r.EventParams[1] = 2000; r.EventParams[2] = 8000; r.EventParams[3] = 12000; r.ActionParams[0] = 133; r.Comment = "Studio screenshot: example row";
                sp.Rows.Add(r);
                sp.ScreensSelect(sp.Rows.Count - 1); sp.ScreensChanged(); await Wait(600);
                await w.Snap("56_smartai_new_row.png");
                var bad = new SmartRow { EntryOrGuid = smart, SourceType = 0, Id = r.Id + 1, EventType = 4, ActionType = 11, TargetType = 2, Link = 99 };
                bad.ActionParams[0] = 9999999;
                sp.Rows.Add(bad);
                sp.ScreensSelect(sp.Rows.Count - 1); sp.ScreensChanged(); await Wait(600);
                await w.Snap("57_smartai_checks.png");
            }

            // ---- editable lists on the creature editor
            EntityEditorPage cr = null;
            w.Navigate("creatures", p => cr = (EntityEditorPage)p);
            cr.Open(69); cr.ShowTab("Spawns"); w.HideToast(); await Wait(800);
            var spawns = cr.ListEditorFor("spawns");
            if (spawns != null && spawns.Data.Rows.Count > 0) spawns.ScreensEditCell(0, "spawntimesecs", "600");
            await Wait(400);
            await w.Snap("58_creature_spawns_editable.png");
            cr.Open(66); cr.ShowTab("Vendor/trainer"); await Wait(800);
            var vend = cr.ListEditorFor("vendor");
            if (vend != null) vend.ScreensAddRow(dr => { dr["item"] = "2287"; dr["slot"] = "50"; });
            await Wait(400);
            await w.Snap("59_vendor_editable.png");
            vend?.Stage(); w.HideToast();
            w.Navigate("changes");
            await Wait(600);
            await w.Snap("60_changes_with_m3_edits.png");
        }

        static async Task Milestone2(MainWindow w)
        {
            var S = w.S;
            async Task<EntityEditorPage> OpenEntity(string ws, long id, string tab)
            {
                EntityEditorPage pg = null;
                w.Navigate(ws, p => pg = (EntityEditorPage)p);
                pg.Open(id);
                if (tab != null) pg.ShowTab(tab);
                w.HideToast();
                await Wait(700);
                return pg;
            }

            // ---- creatures
            var cr = await OpenEntity("creatures", 197, "Basics");             // Marshal McBride (Northshire quest giver)
            cr.ScreensEdit("subname", "Marshal of Northshire");
            cr.ScreensEdit("npcflag", "3");
            cr.ScreensFocus("npcflag");
            await Wait(500);
            await w.Snap("21_creatures_basics.png");
            cr.ShowTab("Stats & combat");
            cr.ScreensEdit("HealthModifier", "1.5");
            cr.ScreensFocus("HealthModifier");
            await Wait(500);
            await w.Snap("22_creatures_stats.png");
            cr.ScreensStage();
            w.HideToast();
            cr = await OpenEntity("creatures", 69, "Loot");                    // Diseased Timber Wolf
            await w.Snap("23_creatures_loot.png");
            cr.ShowTab("Spawns"); await Wait(600);
            await w.Snap("24_creatures_spawns.png");
            cr = await OpenEntity("creatures", 66, "Vendor/trainer");         // Tharynn Bouden, general goods
            await w.Snap("25_creatures_vendor.png");
            long smart = 0;
            try { using var db = S.OpenRead(S.Profile.WorldDb); smart = long.Parse(db.Scalar("SELECT c.entry FROM creature_template c JOIN smart_scripts s ON s.entryorguid = c.entry AND s.source_type = 0 WHERE c.AIName = 'SmartAI' ORDER BY c.entry LIMIT 1")); } catch { }
            if (smart > 0) { await OpenEntity("creatures", smart, "SmartAI"); await w.Snap("26_creatures_smartai.png"); }

            // ---- picker dialog (drawn off-screen)
            var refs = new Refs(S);
            var pw = new PickerWindow(w, "Pick faction", "Decides who the creature is friendly, neutral or hostile to (a FactionTemplate ID).", q => refs.Search("factiontemplate", q), "Stormwind")
            { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, ShowActivated = false };
            pw.Show();
            await Wait(600);
            await w.SnapWindow(pw, "27_picker_faction.png");
            pw.Close();

            // ---- items
            var it = await OpenEntity("items", 25, "Basics");                  // Worn Shortsword
            it.ScreensEdit("Quality", "3");
            it.ScreensEdit("name", "Worn Shortsword of Testing");
            it.ScreensFocus("Quality");
            await Wait(400);
            await w.Snap("28_items_basics.png");
            it.ShowTab("Weapon/armor"); it.ScreensFocus("delay"); await Wait(400);
            await w.Snap("29_items_weapon.png");
            it = await OpenEntity("items", 19019, "Spells");                   // Thunderfury
            it.ScreensFocus("spellid_1"); await Wait(300);
            await w.Snap("30_items_spells.png");
            it = await OpenEntity("items", 159, "Prices");                     // Refreshing Spring Water
            it.ScreensFocus("BuyPrice"); await Wait(300);
            await w.Snap("31_items_prices.png");

            // ---- quests
            var qs = await OpenEntity("quests", 7, "Basics");                   // Kobold Camp Cleanup
            qs.ScreensEdit("LogTitle", "Kobold Camp Cleanup (revised)");
            qs.ScreensFocus("Flags"); await Wait(300);
            await w.Snap("32_quests_basics.png");
            qs.ShowTab("Objectives"); qs.ScreensFocus("RequiredNpcOrGo1"); await Wait(500);
            await w.Snap("33_quests_objectives.png");
            qs.ShowTab("Rewards"); qs.ScreensFocus("RewardXPDifficulty"); await Wait(500);
            await w.Snap("34_quests_rewards.png");
            qs.ShowTab("Starters/enders"); await Wait(500);
            await w.Snap("35_quests_givers.png");
            qs = await OpenEntity("quests", 33, "Chain"); qs.ScreensFocus("PrevQuestID"); await Wait(300);
            await w.Snap("36_quests_chain.png");

            // ---- duplicate as new → staged INSERT
            it = await OpenEntity("items", 25, "Basics");
            it.ScreensDiscard();
            it.ScreensDuplicate();
            await Wait(500);
            await w.Snap("37_duplicate_toast.png");
            w.Navigate("changes");
            var ch = (ChangesPage)w.GetPage("changes");
            var ins = S.Changes.Pending.Changes.LastOrDefault(c => c.Kind == ChangeKind.Insert);
            if (ins != null) { ch.Select(ins.Id); ch.ScreensDetailTab(1); }
            await Wait(500);
            await w.Snap("38_changes_with_m2_edits.png");
            ch.ScreensDetailTab(0);

            // ---- Ctrl+K entities, client patches, settings
            w.Navigate("creatures");
            await Wait(300);
            await w.SnapSearch("39_search_entities.png", "Defias");
            // ---- client patches: reads the real client folder, builds only into a temp folder, never installs
            var client = S.Profile?.ClientDir ?? "";
            var dbcDir = Path.Combine(ConfParser.ParseFile(S.Profile.ConfPath).DataDir ?? "", "dbc");
            ClientPatchesPage cp = null;
            w.Navigate("patches", p => cp = (ClientPatchesPage)p);
            cp.ScreensPrepare(dbcDir, client);
            await Wait(600);
            await w.Snap("40_client_patches_build.png");
            cp.ScreensBuild();
            await Wait(600);
            await w.Snap("40b_client_patches_built.png");
            cp.ScreensInstalled("CharTitles");
            await Wait(600);
            await w.Snap("40c_client_patches_installed.png");
            cp.ScreensBrowse(Path.Combine(client, "Data", "patch-Z.MPQ"), "DBFilesClient");
            await Wait(800);
            await w.Snap("40d_client_patches_browse.png");
            cp.ScreensBrowse(Path.Combine(client, "Data", "patch-L.mpq"), "");
            await Wait(1500);
            await w.Snap("40e_client_patches_recovered_names.png");
            w.Navigate("settings");
            await Wait(300);
            w.HideToast();
            await w.Snap("41_settings_custom_ids.png");
        }
    }
}
