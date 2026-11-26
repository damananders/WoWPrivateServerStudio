using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace AndersCraftStudio.Core
{
    /// <summary>
    /// What it takes for a learned spell to show up in a 3.3.5a spellbook, and the plumbing Studio adds for it:
    ///  • Spell.dbc must not be marked "Do not display" (Attributes 0x80). "Passive" (0x40) shows it as a greyed passive.
    ///  • SkillLineAbility.dbc must link the spell to a skill line the character has; the book shows a spell under the tab of
    ///    that skill line (a class's spec lines are always known by that class).
    ///  • The game client must have the same Spell.dbc and SkillLineAbility.dbc rows as the server: the client decides what
    ///    the book shows from its own copy, so an edit only on the server is learned but never displayed.
    /// </summary>
    public static class SpellBook
    {
        public const uint AttrPassive = 0x40, AttrDoNotDisplay = 0x80;

        public static readonly (uint mask, string name)[] Classes =
        {
            (1, "Warrior"), (2, "Paladin"), (4, "Hunter"), (8, "Rogue"), (16, "Priest"), (32, "Death Knight"),
            (64, "Shaman"), (128, "Mage"), (256, "Warlock"), (1024, "Druid"),
        };
        public const uint AllClasses = 1 | 2 | 4 | 8 | 16 | 32 | 64 | 128 | 256 | 1024;

        public static string ClassNames(uint mask)
        {
            if (mask == 0 || (mask & AllClasses) == AllClasses) return "every class";
            return string.Join(", ", Classes.Where(c => (mask & c.mask) != 0).Select(c => c.name));
        }

        /// <summary>A spellbook tab you can link a spell to: a class skill line (e.g. Fire for mages) and which classes have it.</summary>
        public sealed class Tab
        {
            public uint SkillLine; public string Name; public uint ClassMask;
            public override string ToString() => $"{ClassNames(ClassMask)} › {Name}";
        }

        /// <summary>The class skill lines (spellbook tabs) from SkillLine.dbc (category 7) and the classes that have each one (SkillRaceClassInfo.dbc).</summary>
        public static List<Tab> ClassTabs(DbcTable skillLine, DbcTable raceClassInfo)
        {
            var res = new List<Tab>();
            if (skillLine == null) return res;
            int cat = skillLine.Layout.FieldByName("CategoryID"), nm = skillLine.Layout.FieldByName("DisplayName_enUS");
            var masks = new Dictionary<uint, uint>();
            if (raceClassInfo != null)
            {
                int sk = raceClassInfo.Layout.FieldByName("SkillID"), cm = raceClassInfo.Layout.FieldByName("ClassMask");
                foreach (var r in raceClassInfo.Rows) { masks.TryGetValue(r.V[sk], out var m); masks[r.V[sk]] = m | r.V[cm]; }
            }
            foreach (var r in skillLine.Rows)
            {
                if (cat < 0 || r.V[cat] != 7) continue;
                masks.TryGetValue(r.V[0], out var mask);
                mask &= AllClasses;
                if (mask == 0 || mask == AllClasses) continue;        // class tabs only (one class each)
                var name = nm >= 0 ? r.S[nm] : null;
                if (string.IsNullOrWhiteSpace(name)) continue;
                res.Add(new Tab { SkillLine = r.V[0], Name = name, ClassMask = mask });
            }
            return res.OrderBy(t => Classes.ToList().FindIndex(c => (t.ClassMask & c.mask) != 0)).ThenBy(t => t.Name).ToList();
        }

        /// <summary>SkillLineAbility rows that put <paramref name="spell"/> in a spellbook.</summary>
        public static List<DbcRow> Links(DbcTable sla, uint spell)
        {
            if (sla == null) return new List<DbcRow>();
            int sp = sla.Layout.FieldByName("Spell");
            return sla.Rows.Where(r => r.V[sp] == spell).ToList();
        }

        /// <summary>SkillLineAbility AcquireMethod: 1 = the server teaches it to every character with the skill line (at login and
        /// whenever the skill changes; AzerothCore Player::learnSkillRewardedSpells). 0 = only a trainer, a quest or .learn teaches it.</summary>
        public const uint LearnedOnSkillValue = 1;

        /// <summary>True when every linked character gets the spell by itself (at least one link teaches it automatically).</summary>
        public static bool AutoLearned(DbcTable sla, uint spell)
        {
            if (sla == null) return false;
            int am = sla.Layout.FieldByName("AcquireMethod");
            return am >= 0 && Links(sla, spell).Any(r => r.V[am] == LearnedOnSkillValue || r.V[am] == 2);
        }

        /// <summary>Makes every link of <paramref name="spell"/> teach it automatically (or not). Returns how many rows changed.</summary>
        public static int SetAutoLearn(DbcTable sla, uint spell, bool on)
        {
            int am = sla.Layout.FieldByName("AcquireMethod"), n = 0;
            if (am < 0) return 0;
            foreach (var r in Links(sla, spell))
            {
                uint want = on ? LearnedOnSkillValue : 0;
                if (r.V[am] != want) { r.V[am] = want; n++; }
            }
            return n;
        }

        /// <summary>
        /// Adds a SkillLineAbility row linking <paramref name="spell"/> to <paramref name="skillLine"/> for <paramref name="classMask"/>.
        /// With <paramref name="autoLearn"/> every character of that class learns it by itself the next time they log in;
        /// without it a trainer, a quest or .learn has to teach it. Returns the row (already in the table).
        /// </summary>
        public static DbcRow AddLink(DbcTable sla, uint spell, uint skillLine, uint classMask, uint idStart = 20000, bool autoLearn = false)
        {
            int am = sla.Layout.FieldByName("AcquireMethod");
            var existing = Links(sla, spell).FirstOrDefault(r => r.V[sla.Layout.FieldByName("SkillLine")] == skillLine);
            if (existing != null)
            {
                existing.V[sla.Layout.FieldByName("ClassMask")] |= classMask;
                if (autoLearn && am >= 0) existing.V[am] = LearnedOnSkillValue;
                return existing;
            }
            var row = new DbcRow { V = new uint[sla.Layout.FieldCount], S = new string[sla.Layout.FieldCount] };
            row.V[0] = sla.NextFreeId(Math.Max(idStart, sla.Rows.Count == 0 ? 1 : sla.Rows.Max(r => r.V[0]) + 1));
            row.V[sla.Layout.FieldByName("SkillLine")] = skillLine;
            row.V[sla.Layout.FieldByName("Spell")] = spell;
            row.V[sla.Layout.FieldByName("ClassMask")] = classMask;
            if (autoLearn && am >= 0) row.V[am] = LearnedOnSkillValue;
            sla.Rows.Add(row); sla.InvalidateIndex();
            return row;
        }

        public sealed class Issue
        {
            /// <summary>"bad" (won't show), "warn" (shows, but probably not how you want), "ok".</summary>
            public string Level, Text, Fix, FixLabel;
            public override string ToString() => Level + ": " + Text;
        }

        public sealed class Input
        {
            /// <summary>The spell as you're editing it (source copy + staged and unstaged edits).</summary>
            public SpellData Spell;
            /// <summary>SkillLineAbility.dbc as you're editing it.</summary>
            public DbcTable Links;
            public DbcTable SkillLine;
            /// <summary>The spell and links as the game client loads them today (null = no client folder set).</summary>
            public SpellData ClientSpell; public DbcTable ClientLinks; public bool ClientKnown;
            public Func<uint, bool> IconExists, VisualExists;
            /// <summary>The class you'll test with (0 = any).</summary>
            public uint ClassMask;
        }

        /// <summary>"Will this show in the spellbook?" Each problem in plain words, with a fix Studio can make where there is one.</summary>
        public static List<Issue> Check(Input x)
        {
            var res = new List<Issue>();
            var s = x.Spell;
            if (s == null) { res.Add(new Issue { Level = "bad", Text = "No spell is open." }); return res; }
            uint id = s.Id, attr = s.U("Attributes");
            void Add(string level, string text, string fix = null, string label = null) => res.Add(new Issue { Level = level, Text = text, Fix = fix, FixLabel = label });

            if ((attr & AttrDoNotDisplay) != 0)
                Add("bad", "It's marked \"Do not display\" (Attributes), so the game never shows it in the spellbook or on action bars, even when it's learned.", "unhide", "Show it");
            if ((attr & AttrPassive) != 0)
                Add("warn", "It's Passive: it shows in the spellbook as a greyed \"Passive\" and can't be clicked or put on a bar. Untick Passive if it should be a button.", "unpassive", "Make it a button");
            if (string.IsNullOrWhiteSpace(s.Name)) Add("warn", "It has no name, so its spellbook entry and tooltip are blank.");

            var links = Links(x.Links, id);
            if (links.Count == 0)
                Add("bad", "It isn't linked to a spellbook tab (no SkillLineAbility.dbc row). A learned spell without one is known, but the book has nowhere to put it.", "link", "Add to a spellbook tab…");
            else
            {
                int skf = x.Links.Layout.FieldByName("SkillLine"), cmf = x.Links.Layout.FieldByName("ClassMask");
                foreach (var l in links)
                {
                    var line = x.SkillLine?.Find(l.V[skf]);
                    if (x.SkillLine != null && line == null) Add("bad", $"Its spellbook link points at skill line {l.V[skf]}, which isn't in SkillLine.dbc.", "link", "Pick a tab…");
                }
                uint mask = links.Aggregate(0u, (m, l) => m | (l.V[cmf] == 0 ? AllClasses : l.V[cmf]));
                if (x.ClassMask != 0 && (mask & x.ClassMask) == 0)
                    Add("bad", $"It's linked for {ClassNames(mask)} only, not for {ClassNames(x.ClassMask)}.", "link", "Add that class…");
                if (!AutoLearned(x.Links, id))
                    Add("warn", $"Nobody gets it by themselves: until a character learns it (a trainer, a quest, or the GM command .learn {id} on that character), it isn't in their spellbook and /cast can't find it. Let every {ClassNames(mask)} character learn it automatically the next time they log in?", "autolearn", "Everyone learns it automatically");
            }

            for (int i = 1; i <= 3; i++)
                if (s.Trigger(i) == id && s.Effect(i) != 0)
                    Add("warn", $"Effect {i} triggers this same spell, so it would set itself off again and again. Clear Trigger spell on effect {i} unless you mean it.", "selftrigger", "Clear it");
            var icon = s.U("SpellIconID");
            if (x.IconExists != null && (icon == 0 || !x.IconExists(icon))) Add("warn", icon == 0 ? "It has no icon: the spellbook shows a question mark." : $"Its icon ({icon}) isn't in the game client's SpellIcon.dbc yet: the spellbook shows a question mark until the icon is shipped too.");
            var vis = s.U("SpellVisualID_1");
            if (vis != 0 && x.VisualExists != null && !x.VisualExists(vis)) Add("warn", $"Its visual ({vis}) isn't in SpellVisual.dbc: it casts with no animation or effect.");

            // the client decides what the book shows, from its own copy
            if (x.ClientKnown)
            {
                if (x.ClientSpell == null)
                    Add("bad", "The game client doesn't have this spell yet, so the client can't show it. Send it to the game (build and install the patch).", "ship", "Send to game…");
                else
                {
                    var diffs = new List<string>();
                    uint cattr = x.ClientSpell.U("Attributes");
                    if (cattr != attr) diffs.Add($"Attributes are {Hex(cattr)} there ({DescribeAttr(cattr)}), {Hex(attr)} here");
                    if (x.ClientSpell.Name != s.Name) diffs.Add("the name differs");
                    if (x.ClientSpell.U("SpellIconID") != icon) diffs.Add("the icon differs");
                    for (int i = 1; i <= 3; i++) if (x.ClientSpell.Effect(i) != s.Effect(i) || x.ClientSpell.Aura(i) != s.Aura(i)) { diffs.Add($"effect {i} differs"); break; }
                    if (diffs.Count > 0)
                        Add("bad", "The game client still has an older version of this spell: " + string.Join("; ", diffs) + ". Send it to the game so both match.", "ship", "Send to game…");
                    if (links.Count > 0 && Links(x.ClientLinks, id).Count == 0)
                        Add("bad", "The game client has no spellbook link for it yet (its SkillLineAbility.dbc is older). Send it to the game.", "ship", "Send to game…");
                    else if (links.Count > 0 && AutoLearned(x.Links, id) != AutoLearned(x.ClientLinks, id))
                        Add("bad", "The game's copy still says who learns it the old way. Send it to the game so the server teaches it.", "ship", "Send to game…");
                }
            }

            if (!res.Any(r => r.Level == "bad"))
            {
                var where = links.Count == 0 ? "" : " Tab: " + string.Join(", ", links.Select(l => TabName(x, l))) + ".";
                var who = links.Count > 0 && AutoLearned(x.Links, id)
                    ? " Every character of those classes learns it by itself when they next log in (after the server has restarted with it)."
                    : " It shows once a character has learned it.";
                res.Insert(0, new Issue { Level = "ok", Text = "Will show in the spellbook." + who + where + (x.ClientKnown ? " The game client has the same version." : " (No client folder set, so the client's copy wasn't checked.)") });
            }
            return res;
        }

        static string TabName(Input x, DbcRow l)
        {
            int skf = x.Links.Layout.FieldByName("SkillLine"), cmf = x.Links.Layout.FieldByName("ClassMask");
            var line = x.SkillLine?.Find(l.V[skf]);
            int nm = x.SkillLine?.Layout.FieldByName("DisplayName_enUS") ?? -1;
            var name = line != null && nm >= 0 ? line.S[nm] : "skill line " + l.V[skf];
            return $"{name} ({ClassNames(l.V[cmf])})";
        }

        static string Hex(uint v) => "0x" + v.ToString("X", CultureInfo.InvariantCulture);
        public static string DescribeAttr(uint a)
        {
            var parts = new List<string>();
            if ((a & AttrDoNotDisplay) != 0) parts.Add("hidden");
            if ((a & AttrPassive) != 0) parts.Add("passive");
            return parts.Count == 0 ? "visible" : string.Join(" + ", parts);
        }
    }

    /// <summary>
    /// "Send to game": one client patch holding every DBC edit you've sent out or staged, built so it wins over the patches
    /// already in the client, and installed with a backup.
    /// </summary>
    public static class GameShip
    {
        public sealed class Plan
        {
            public string PatchName, TargetDir, Locale, Why;
            /// <summary>True when no new patch can load after the one that holds these tables: Studio puts its files into that
            /// patch instead (backed up first). <see cref="PatchName"/> / <see cref="TargetDir"/> then name that patch.</summary>
            public bool InPlace;
            /// <summary>Patches Studio installed before that no longer win (they can be removed).</summary>
            public readonly List<string> Outranked = new List<string>();
            public readonly Dictionary<string, byte[]> Files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            public readonly List<string> Problems = new List<string>(), Notes = new List<string>();
            public bool Ok => Problems.Count == 0 && PatchName != null && Files.Count > 0;
        }

        /// <summary>The client's locale folder (Data\enUS…): the one holding a locale-xxXX.MPQ.</summary>
        public static string Locale(string dataDir)
        {
            if (!Directory.Exists(dataDir)) return null;
            foreach (var d in Directory.GetDirectories(dataDir))
                if (Directory.GetFiles(d, "locale-*.mpq").Length > 0) return Path.GetFileName(d);
            return null;
        }

        /// <summary>
        /// Builds the file list. Each DBC starts from the patch work folder's copy (which holds what was sent before) or the
        /// client's own, then every DBC edit in History (oldest first) and the staged ones are replayed on it; rows that already
        /// hold the values are skipped, so replaying is safe. Other files in the work folder (icons, models, sounds) come along.
        /// </summary>
        /// <param name="server">The server's DBC copy. Spells you've edited are made identical to the server's row (the server
        /// is what you edited against), with their spellbook links and icon rows, so client and server can't drift apart.</param>
        public static Plan Make(ClientDbcSource client, IEnumerable<Changeset> history, IList<Change> staged, string workFolder, IEnumerable<string> studioInstalled, Func<string, DbcTable> server = null)
        {
            var plan = new Plan();
            var sent = history.Where(h => h.State == ChangesetState.Applied && h.Changes.Any(DbcStager.IsDbc)).OrderBy(h => h.CreatedUtc).ToList();
            var allDbc = sent.SelectMany(h => h.Changes).Concat(staged).Where(DbcStager.IsDbc).ToList();
            var dbcFiles = allDbc.Select(c => c.Table).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var workDbc = workFolder == null ? null : Path.Combine(workFolder, "DBFilesClient");
            if (workDbc != null && Directory.Exists(workDbc))
                foreach (var f in Directory.GetFiles(workDbc, "*.dbc")) if (!dbcFiles.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)) dbcFiles.Add(Path.GetFileName(f));

            // the spells you've edited, taken whole from the server's copy
            var serverSpell = server?.Invoke("Spell.dbc");
            var synced = new HashSet<uint>(allDbc.Where(c => c.Dbc.ById && string.Equals(c.Table, "Spell.dbc", StringComparison.OrdinalIgnoreCase) && serverSpell?.Find(c.Dbc.Key) != null).Select(c => c.Dbc.Key));
            var rowsFromServer = new Dictionary<string, List<DbcRow>>(StringComparer.OrdinalIgnoreCase);
            DbcTable serverSla = null, serverIcons = null;
            if (synced.Count > 0)
            {
                rowsFromServer["Spell.dbc"] = synced.Select(k => serverSpell.Find(k)).ToList();
                serverSla = server("SkillLineAbility.dbc");
                if (serverSla != null) { var l = synced.SelectMany(k => SpellBook.Links(serverSla, k)).ToList(); if (l.Count > 0) rowsFromServer["SkillLineAbility.dbc"] = l; }
                serverIcons = server("SpellIcon.dbc");
                int icf = serverSpell.Layout.FieldByName("SpellIconID");
                if (serverIcons != null) { var l = synced.Select(k => serverIcons.Find(serverSpell.Find(k).V[icf])).Where(r => r != null).ToList(); if (l.Count > 0) rowsFromServer["SpellIcon.dbc"] = l; }
                foreach (var f in rowsFromServer.Keys) if (!dbcFiles.Contains(f, StringComparer.OrdinalIgnoreCase)) dbcFiles.Add(f);
                plan.Notes.Add($"Spell(s) {string.Join(", ", synced.OrderBy(x => x))}: the game gets the server's exact row, with its spellbook link(s) and icon.");
            }

            foreach (var file in dbcFiles)
            {
                var wf = workDbc == null ? null : Path.Combine(workDbc, file);
                byte[] bytes = wf != null && File.Exists(wf) ? File.ReadAllBytes(wf) : client?.Read(file);
                if (bytes == null) { plan.Problems.Add($"{file}: the game client doesn't have it, so there's nothing to patch."); continue; }
                var t = DbcTable.Read(file, bytes);
                bool isSpell = string.Equals(file, "Spell.dbc", StringComparison.OrdinalIgnoreCase);
                var batches = sent.Select(h => h.Changes).Append(staged.ToList())
                    .Select(cs => cs.Where(c => DbcStager.IsDbc(c) && string.Equals(c.Table, file, StringComparison.OrdinalIgnoreCase) && !(isSpell && c.Dbc.ById && synced.Contains(c.Dbc.Key))).ToList()).Where(l => l.Count > 0).ToList();
                for (int bi = 0; bi < batches.Count; bi++)
                {
                    var mine = SkipSuperseded(t, batches[bi], batches.Skip(bi + 1).SelectMany(x => x));
                    if (mine.Count == 0) continue;
                    var probs = new List<string>(); var notes = new List<string>();
                    var next = DbcPatcher.Apply(t, mine, false, probs, notes);
                    if (next == null) { plan.Problems.AddRange(probs.Select(p => file + ": " + p)); break; }
                    t = next;
                }
                if (rowsFromServer.TryGetValue(file, out var take))
                    foreach (var r in take)
                        if (!CopyRow(r, t)) { plan.Problems.Add($"{file}: the server's copy has a different layout from the client's, so row {r.V[0]} can't be copied."); break; }
                plan.Files["DBFilesClient\\" + file] = t.Write();
            }
            if (workFolder != null && Directory.Exists(workFolder))
                foreach (var f in Directory.GetFiles(workFolder, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(workFolder, f);
                    if (rel.Equals("studio_manifest.json", StringComparison.OrdinalIgnoreCase) || plan.Files.ContainsKey(rel)) continue;
                    plan.Files[rel] = File.ReadAllBytes(f);
                }

            // a name that loads after every archive holding one of these DBCs
            plan.Locale = client == null ? null : Locale(client.DataDir);
            var archives = client?.Archives.Select(a => (name: a.Name, rank: a.Rank, holds: a.Dbcs.Any(d => dbcFiles.Contains(d, StringComparer.OrdinalIgnoreCase)))).ToList()
                           ?? new List<(string, int, bool)>();
            PickName(plan, archives, studioInstalled);
            return plan;
        }

        /// <summary>Puts <paramref name="r"/> (from another copy of the same table) into <paramref name="t"/>, replacing the row with its ID.</summary>
        public static bool CopyRow(DbcRow r, DbcTable t)
        {
            if (r.V.Length != t.Layout.FieldCount) return false;
            var c = r.Clone();
            for (int i = 0; i < c.V.Length; i++) if (t.Layout.FieldIsString[i]) c.V[i] = uint.MaxValue;   // re-interned on write
            var old = t.Find(c.V[0]);
            if (old != null) t.Rows[t.Rows.IndexOf(old)] = c; else t.Rows.Add(c);
            t.InvalidateIndex();
            return true;
        }

        /// <summary>
        /// Leaves out field edits a later edit has already overtaken in this copy (the copy holds a value a later edit wrote),
        /// so replaying History on a copy that already has some of it doesn't trip over the older "before" values.
        /// </summary>
        static List<Change> SkipSuperseded(DbcTable t, List<Change> batch, IEnumerable<Change> later)
        {
            var laterList = later.ToList();
            var res = new List<Change>();
            foreach (var c in batch)
            {
                var row = c.Kind == ChangeKind.Update && c.Dbc.ById ? t.Find(c.Dbc.Key) : null;
                if (row == null) { res.Add(c); continue; }
                var keep = new List<DbcFieldEdit>();
                foreach (var f in c.Dbc.Fields)
                {
                    var cur = t.FieldText(row, f.Field);
                    bool ahead = cur != f.Before && cur != f.After &&
                                 laterList.Any(l => l.Dbc.ById && l.Dbc.Key == c.Dbc.Key && l.Dbc.Fields.Any(lf => lf.Field == f.Field && lf.After == cur));
                    if (!ahead) keep.Add(f);
                }
                if (keep.Count == c.Dbc.Fields.Count) { res.Add(c); continue; }
                if (keep.Count == 0) continue;
                res.Add(new Change { Kind = c.Kind, Schema = c.Schema, Table = c.Table, Summary = c.Summary, Key = c.Key, Dbc = new DbcPatch { File = c.Dbc.File, Source = c.Dbc.Source, ById = c.Dbc.ById, Key = c.Dbc.Key, FieldCount = c.Dbc.FieldCount, Fields = keep } });
            }
            return res;
        }

        /// <summary>
        /// Picks where the files go: a patch Studio installed before if it still loads after every other archive holding these
        /// tables (it's replaced), else the first free name that does. The client ranks every Data\patch-X.MPQ above every
        /// Data\enUS\patch-enUS-X.MPQ (see <see cref="ClientDbcSource.Rank"/>). When the patch that holds these tables already
        /// loads last (someone's patch-Z.MPQ), no new name can win, so Studio updates that patch in place.
        /// </summary>
        public static void PickName(Plan plan, IEnumerable<(string name, int rank, bool holds)> archives, IEnumerable<string> studioInstalled)
        {
            var mine = new HashSet<string>(studioInstalled ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var others = archives.Where(a => !mine.Contains(Path.GetFileName(a.name))).ToList();
            var holders = others.Where(a => a.holds).ToList();
            int top = holders.Count == 0 ? 0 : holders.Max(a => a.rank);
            var taken = new HashSet<int>(others.Select(a => a.rank));
            var candidates = new List<(int rank, string name, string dir)>();
            foreach (var c in "23456789ABCDEFGHIJKLMNOPQRSTUVWXYZ")
            {
                var bn = $"patch-{c}.MPQ";
                candidates.Add((ClientDbcSource.Rank(bn, false), bn, null));
                if (plan.Locale != null) { var ln = $"patch-{plan.Locale}-{c}.MPQ"; candidates.Add((ClientDbcSource.Rank(ln, true), ln, plan.Locale)); }
            }
            var pick = candidates.Where(c => c.rank > top && mine.Contains(c.name)).OrderBy(c => c.rank).FirstOrDefault();
            if (pick.name == null) pick = candidates.Where(c => c.rank > top && !taken.Contains(c.rank)).OrderBy(c => c.rank).FirstOrDefault();
            var topName = holders.OrderByDescending(a => a.rank).FirstOrDefault().name;
            foreach (var m in archives.Where(a => mine.Contains(Path.GetFileName(a.name)) && a.rank < top)) plan.Outranked.Add(m.name);
            if (pick.name == null)
            {
                // nothing can load after it: put the files into it
                plan.InPlace = true;
                plan.PatchName = Path.GetFileName(topName); plan.TargetDir = Path.GetDirectoryName(topName) is string d && d.Length > 0 ? d : null;
                plan.Why = $"{topName} loads after every name a new patch could have and holds older copies of these tables, so Studio puts your versions into it (it's backed up first; Uninstall puts it back).";
            }
            else
            {
                plan.PatchName = pick.name; plan.TargetDir = pick.dir;
                plan.Why = topName == null ? "No other patch holds these tables." : $"It loads after {topName}, the last patch that holds these tables today, so your version wins.";
            }
            if (plan.Outranked.Count > 0)
                plan.Notes.Add("No longer used: " + string.Join(", ", plan.Outranked) + " (sent before by Studio) loads below " + topName + ", so the game never read it. You can remove it under Client patches › Installed.");
        }
    }
}
