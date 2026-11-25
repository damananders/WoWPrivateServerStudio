using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace AndersCraftStudio.Core.Build
{
    // ============================================================================ NPCs
    public sealed class Spawn
    {
        public int Map; public double X, Y, Z, O;
        public override string ToString() => $"map {Map} at {X:0.#}, {Y:0.#}, {Z:0.#}";

        /// <summary>Reads the output of the in-game ".gps" command (or plain "map x y z [o]").</summary>
        public static Spawn Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            double N(string key) { var m = Regex.Match(text, key + @"\s*:?\s*(-?\d+(?:\.\d+)?)", RegexOptions.IgnoreCase); return m.Success ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : double.NaN; }
            var x = N(@"\bX"); var y = N(@"\bY"); var z = N(@"\bZ"); var map = N(@"\bMap"); var o = N(@"\bOrientation");
            if (!double.IsNaN(x) && !double.IsNaN(y) && !double.IsNaN(z)) return new Spawn { Map = double.IsNaN(map) ? 0 : (int)map, X = x, Y = y, Z = z, O = double.IsNaN(o) ? 0 : o };
            var nums = Regex.Matches(text, @"-?\d+(?:\.\d+)?").Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToList();
            if (nums.Count >= 4) return new Spawn { Map = (int)nums[0], X = nums[1], Y = nums[2], Z = nums[3], O = nums.Count > 4 ? nums[4] : 0 };
            return null;
        }
    }

    public sealed class NpcDraft
    {
        public long Entry;
        public string Name = "New NPC", Title = "";
        /// <summary>What it is: see <see cref="NpcBuilder.Kinds"/>.</summary>
        public string Kind = "Friendly";
        public int MinLevel = 10, MaxLevel = 10;
        /// <summary>Who it's friendly to: see <see cref="NpcBuilder.Sides"/>.</summary>
        public string Side = "Everyone";
        /// <summary>How tough: see <see cref="NpcBuilder.Strengths"/>.</summary>
        public string Strength = "Normal";
        public long Display; public double Scale = 1;
        /// <summary>Services shown when players talk to it (keys of <see cref="NpcBuilder.Services"/>).</summary>
        public HashSet<string> Services = new HashSet<string>();
        public double Wander;
        public int RespawnSeconds = 300;
        public List<Spawn> Spawns = new List<Spawn>();
        public long Weapon1, Weapon2, Ranged;
        public List<long> Sells = new List<long>();
        /// <summary>What it says when a player talks to it (empty = nothing special).</summary>
        public string Greeting = "";
        /// <summary>Teaches what this other trainer teaches (its creature entry), 0 = nothing.</summary>
        public long TeachesLike;
    }

    public static class NpcBuilder
    {
        public static readonly (string key, string what)[] Kinds =
        {
            ("Friendly", "Just stands there and is nice (flavour, quest givers, guards)."),
            ("Vendor", "Sells things."), ("Trainer", "Teaches spells or a profession."), ("Quest giver", "Gives quests (\"!\" over its head)."),
            ("Innkeeper", "Lets players set their hearthstone."), ("Enemy", "Attacks players on sight."), ("Critter", "Harmless wildlife."),
        };
        public static readonly (string key, long faction, string what)[] Sides =
        {
            ("Everyone", 35, "Friendly to every player."), ("Alliance", 11, "Friendly to the Alliance (Stormwind), hostile to the Horde."), ("Horde", 85, "Friendly to the Horde (Orgrimmar), hostile to the Alliance."),
            ("Hostile", 14, "Attacks every player (monsters)."), ("Neutral", 7, "Fights back only when attacked (animals)."),
        };
        public static readonly (string key, double health, double damage, int rank, string what)[] Strengths =
        {
            ("Weak", 0.5, 0.5, 0, "Half the health and damage of its level."), ("Normal", 1, 1, 0, "Like any creature of its level."), ("Tough", 2, 1.5, 0, "Twice the health."),
            ("Elite", 3, 2, 1, "A group of players needed (elite dragon)."), ("Rare", 4, 2, 2, "A rare elite (silver dragon)."), ("Boss", 15, 4, 3, "A dungeon boss (skull level)."),
        };
        /// <summary>Services and their npcflag bits.</summary>
        public static readonly (string key, long bit, string what)[] Services =
        {
            ("Talk", 1, "Has a chat window (needed for most services)."), ("Quests", 2, "Gives or takes quests."), ("Trainer", 16, "Teaches."), ("Vendor", 128, "Sells things."),
            ("Repairs", 4096, "Repairs armour (also tick Vendor)."), ("Flight master", 8192, "Flight paths."), ("Innkeeper", 65536, "Sets the hearthstone."), ("Banker", 131072, "Opens the bank."),
            ("Stable master", 4194304, "Looks after hunter pets."), ("Auctioneer", 2097152, "Opens the auction house."),
        };

        public static long Flags(IEnumerable<string> services) => services.Select(s => Services.FirstOrDefault(x => x.key == s).bit).Aggregate(0L, (a, b) => a | b);
        public static HashSet<string> FromFlags(long flags) => new HashSet<string>(Services.Where(s => (flags & s.bit) != 0).Select(s => s.key));

        /// <summary>The services a kind usually has.</summary>
        public static IEnumerable<string> DefaultServices(string kind) => kind switch
        {
            "Vendor" => new[] { "Talk", "Vendor" }, "Trainer" => new[] { "Talk", "Trainer" }, "Quest giver" => new[] { "Talk", "Quests" },
            "Innkeeper" => new[] { "Talk", "Innkeeper", "Vendor" }, "Friendly" => new[] { "Talk" }, _ => Array.Empty<string>(),
        };

        public sealed class NpcRows
        {
            public BRow Template, Model, Equip, Gossip, Text, Trainer;
            public List<BRow> Spawns = new List<BRow>(), Vendor = new List<BRow>();
            public IEnumerable<BRow> All => new[] { Template, Model, Equip, Gossip, Text, Trainer }.Where(r => r != null).Concat(Spawns).Concat(Vendor);
            public NpcRows Clone() => new NpcRows { Template = Template?.Clone(), Model = Model?.Clone(), Equip = Equip?.Clone(), Gossip = Gossip?.Clone(), Text = Text?.Clone(), Trainer = Trainer?.Clone(), Spawns = Spawns.Select(r => r.Clone()).ToList(), Vendor = Vendor.Select(r => r.Clone()).ToList() };
        }

        static string Id(long id) => id.ToString(CultureInfo.InvariantCulture);

        public static NpcRows Load(ISqlReader db, long entry)
        {
            var t = Rows.One(db, "creature_template", new[] { "entry" }, "entry = " + Id(entry));
            if (t == null) return null;
            var r = new NpcRows
            {
                Template = t,
                Model = Rows.One(db, "creature_template_model", new[] { "CreatureID", "Idx" }, $"CreatureID = {Id(entry)} AND Idx = 0"),
                Equip = Rows.One(db, "creature_equip_template", new[] { "CreatureID", "ID" }, $"CreatureID = {Id(entry)} AND ID = 1"),
                Trainer = Rows.One(db, "creature_default_trainer", new[] { "CreatureId" }, "CreatureId = " + Id(entry)),
            };
            long menu = t.L("gossip_menu_id");
            if (menu > 0)
            {
                r.Gossip = Rows.One(db, "gossip_menu", new[] { "MenuID", "TextID" }, "MenuID = " + Id(menu));
                if (r.Gossip != null) r.Text = Rows.One(db, "npc_text", new[] { "ID" }, "ID = " + Id(r.Gossip.L("TextID")));
            }
            r.Spawns.AddRange(Rows.Read(db, "creature", new[] { "guid" }, $"id1 = {Id(entry)} ORDER BY guid", 50));
            r.Vendor.AddRange(Rows.Read(db, "npc_vendor", new[] { "entry", "item", "ExtendedCost" }, $"entry = {Id(entry)} ORDER BY slot", 300));
            return r;
        }

        public static NpcDraft ToDraft(NpcRows r)
        {
            var t = r.Template;
            var d = new NpcDraft
            {
                Entry = t.L("entry"), Name = t.S("name"), Title = t.S("subname"), MinLevel = (int)t.L("minlevel"), MaxLevel = (int)t.L("maxlevel"),
                Services = FromFlags(t.L("npcflag")), Display = r.Model?.L("CreatureDisplayID") ?? t.L("modelid1"), Scale = r.Model?.D("DisplayScale") ?? (t.Has("scale") ? t.D("scale") : 1),
                Weapon1 = r.Equip?.L("ItemID1") ?? 0, Weapon2 = r.Equip?.L("ItemID2") ?? 0, Ranged = r.Equip?.L("ItemID3") ?? 0, Greeting = r.Text?.S("text0_0") ?? "",
                TeachesLike = 0,
            };
            if (d.Scale <= 0) d.Scale = 1;
            long fac = t.L("faction");
            d.Side = Sides.FirstOrDefault(s => s.faction == fac).key ?? (fac == 12 || fac == 11 ? "Alliance" : fac == 29 || fac == 85 ? "Horde" : "Everyone");
            int rank = (int)t.L("rank"); double hp = t.Has("HealthModifier") ? t.D("HealthModifier") : 1;
            d.Strength = Strengths.OrderBy(s => Math.Abs(s.rank - rank) * 10 + Math.Abs(s.health - hp)).First().key;
            d.Kind = d.Services.Contains("Vendor") ? "Vendor" : d.Services.Contains("Trainer") ? "Trainer" : d.Services.Contains("Quests") ? "Quest giver" : d.Services.Contains("Innkeeper") ? "Innkeeper" : (d.Side is "Hostile" ? "Enemy" : "Friendly");
            var s0 = r.Spawns.FirstOrDefault();
            if (s0 != null) { d.Wander = s0.D("wander_distance"); d.RespawnSeconds = (int)s0.L("spawntimesecs"); }
            d.Spawns = r.Spawns.Select(s => new Spawn { Map = (int)s.L("map"), X = s.D("position_x"), Y = s.D("position_y"), Z = s.D("position_z"), O = s.D("orientation") }).ToList();
            d.Sells = r.Vendor.Select(v => v.L("item")).ToList();
            return d;
        }

        public static List<string> Check(NpcDraft d)
        {
            var p = new List<string>();
            if (string.IsNullOrWhiteSpace(d.Name)) p.Add("It needs a name.");
            if (d.MinLevel < 1 || d.MaxLevel > 83 || d.MinLevel > d.MaxLevel) p.Add("Levels go from 1 to 83, lowest first.");
            if (d.Display <= 0) p.Add("Pick how it looks (a model).");
            if (d.Scale <= 0 || d.Scale > 20) p.Add("Size goes from 0.1 to 20.");
            if (d.Services.Contains("Vendor") && d.Sells.Count == 0) p.Add("It's a vendor but sells nothing yet (add items under What it sells).");
            if (d.Sells.Count > 150) p.Add("At most 150 things to sell.");
            if (d.Wander < 0 || d.Wander > 100) p.Add("Wander distance goes from 0 to 100 yards.");
            return p;
        }

        /// <summary>Writes the draft into a copy of <paramref name="rows"/> (as read, or blank for a new NPC).</summary>
        public static NpcRows Apply(NpcDraft d, NpcRows rows, ISqlReader db, Changeset cs, List<string> notes, Func<string, BRow> blank)
        {
            var r = rows.Clone();
            var t = r.Template;
            void Set(BRow row, string c, object v) { if (row != null && row.Has(c)) row[c] = v; }
            Set(t, "entry", d.Entry); Set(t, "name", d.Name); Set(t, "subname", d.Title ?? ""); Set(t, "minlevel", d.MinLevel); Set(t, "maxlevel", d.MaxLevel);
            Set(t, "faction", Sides.First(s => s.key == d.Side).faction);
            var st = Strengths.First(s => s.key == d.Strength);
            Set(t, "HealthModifier", st.health); Set(t, "DamageModifier", st.damage); Set(t, "rank", st.rank);
            long flags = Flags(d.Services);
            Set(t, "npcflag", flags);
            if (d.Kind == "Critter") { Set(t, "type", 8); Set(t, "faction", 188); }
            if (t.L("unit_class") == 0) Set(t, "unit_class", 1);
            foreach (var (c, v) in new (string, object)[] { ("speed_walk", 1.0), ("speed_run", 1.14286), ("BaseAttackTime", 2000L), ("RangeAttackTime", 2000L), ("ManaModifier", 1.0), ("ArmorModifier", 1.0), ("ExperienceModifier", 1.0), ("detection_range", 20.0), ("exp", 2L), ("RegenHealth", 1L), ("BaseVariance", 1.0), ("RangeVariance", 1.0) })
                if (t.Has(c) && t.D(c) == 0) t[c] = v;
            if (t.Has("type") && t.L("type") == 0) t["type"] = 7L;
            // look
            if (t.Has("modelid1")) { Set(t, "modelid1", d.Display); Set(t, "scale", d.Scale); }
            else
            {
                r.Model ??= blank("creature_template_model") ?? new BRow("creature_template_model", "CreatureID", "Idx").Put("CreatureID", 0L).Put("Idx", 0L).Put("CreatureDisplayID", 0L).Put("DisplayScale", 1.0).Put("Probability", 1.0).Put("VerifiedBuild", 0L);
                Set(r.Model, "CreatureID", d.Entry); Set(r.Model, "Idx", 0); Set(r.Model, "CreatureDisplayID", d.Display); Set(r.Model, "DisplayScale", d.Scale); Set(r.Model, "Probability", 1.0);
            }
            // weapons
            if (d.Weapon1 + d.Weapon2 + d.Ranged > 0)
            {
                r.Equip ??= blank("creature_equip_template") ?? new BRow("creature_equip_template", "CreatureID", "ID").Put("CreatureID", 0L).Put("ID", 1L).Put("ItemID1", 0L).Put("ItemID2", 0L).Put("ItemID3", 0L).Put("VerifiedBuild", 0L);
                Set(r.Equip, "CreatureID", d.Entry); Set(r.Equip, "ID", 1); Set(r.Equip, "ItemID1", d.Weapon1); Set(r.Equip, "ItemID2", d.Weapon2); Set(r.Equip, "ItemID3", d.Ranged);
            }
            else r.Equip = null;
            // where it stands
            var tmpl = r.Spawns.FirstOrDefault() ?? blank("creature") ?? new BRow("creature", "guid");
            var oldSpawns = r.Spawns;
            r.Spawns = new List<BRow>();
            long nextGuid = 0;
            for (int i = 0; i < d.Spawns.Count; i++)
            {
                var s = d.Spawns[i];
                var row = i < oldSpawns.Count ? oldSpawns[i].Clone() : tmpl.Clone();
                if (i >= oldSpawns.Count)
                {
                    if (nextGuid == 0) nextGuid = db != null ? Rows.FreeId(db, cs, "creature", "guid", 1) : 1000000;
                    row.Put("guid", nextGuid++);
                    foreach (var (c, v) in new (string, object)[] { ("spawnMask", 1L), ("phaseMask", 1L), ("curhealth", 1L), ("curmana", 0L), ("equipment_id", 0L), ("currentwaypoint", 0L), ("npcflag", 0L), ("unit_flags", 0L), ("dynamicflags", 0L) }) if (row.Has(c)) row[c] = v;
                }
                row.Put(row.Has("id") && !row.Has("id1") ? "id" : "id1", d.Entry);
                row.Put("map", (long)s.Map); row.Put("position_x", s.X); row.Put("position_y", s.Y); row.Put("position_z", s.Z); row.Put("orientation", s.O);
                row.Put("spawntimesecs", (long)Math.Max(1, d.RespawnSeconds)); row.Put("wander_distance", d.Wander); row.Put("MovementType", d.Wander > 0 ? 1L : 0L);
                if (row.Has("equipment_id")) row["equipment_id"] = r.Equip != null ? 1L : 0L;
                r.Spawns.Add(row);
            }
            // what it sells
            var oldVendor = r.Vendor.ToDictionary(v => v.L("item"), v => v);
            r.Vendor = d.Sells.Distinct().Select((item, i) =>
            {
                var row = oldVendor.TryGetValue(item, out var ov) ? ov.Clone() : new BRow("npc_vendor", "entry", "item", "ExtendedCost").Put("entry", d.Entry).Put("slot", 0L).Put("item", item).Put("maxcount", 0L).Put("incrtime", 0L).Put("ExtendedCost", 0L).Put("VerifiedBuild", 0L);
                row.Put("entry", d.Entry); row.Put("slot", (long)i);
                return row;
            }).ToList();
            // what it says
            if (!string.IsNullOrWhiteSpace(d.Greeting))
            {
                if (r.Text == null)
                {
                    // gaps are used: these IDs are small columns on some servers
                    long textId = db != null ? Rows.FreeGap(db, cs, "npc_text", "ID", 900000) : 900000;
                    long menuId = db != null ? Rows.FreeGap(db, cs, "gossip_menu", "MenuID", 90000) : 90000;
                    r.Text = blank("npc_text") ?? new BRow("npc_text", "ID").Put("ID", 0L).Put("text0_0", "").Put("text0_1", "").Put("BroadcastTextID0", 0L).Put("lang0", 0L).Put("Probability0", 0.0);
                    r.Text.Put("ID", textId); if (r.Text.Has("Probability0")) r.Text["Probability0"] = 1.0;
                    r.Gossip = new BRow("gossip_menu", "MenuID", "TextID").Put("MenuID", menuId).Put("TextID", textId);
                    Set(t, "gossip_menu_id", menuId);
                }
                Set(r.Text, "text0_0", d.Greeting); Set(r.Text, "text0_1", d.Greeting);
                if (!d.Services.Contains("Talk")) { flags |= 1; Set(t, "npcflag", flags); }
            }
            // what it teaches
            if (d.TeachesLike > 0 && db != null)
            {
                var other = Rows.One(db, "creature_default_trainer", new[] { "CreatureId" }, "CreatureId = " + Id(d.TeachesLike));
                if (other == null) notes?.Add($"Creature {d.TeachesLike} isn't a trainer, so there's nothing to copy.");
                else { r.Trainer = new BRow("creature_default_trainer", "CreatureId").Put("CreatureId", d.Entry).Put("TrainerId", other.L("TrainerId")); }
            }
            else if (d.TeachesLike == 0 && rows.Trainer != null && !d.Services.Contains("Trainer")) r.Trainer = null;
            if (d.Kind == "Trainer" && r.Trainer == null) notes?.Add("A trainer needs something to teach: pick a trainer to copy under What it teaches.");
            return r;
        }

        /// <summary>Stages the NPC: new and copy-as-new add everything; edit stages only what changed.</summary>
        public static List<Change> Stage(StartMode mode, NpcRows original, NpcRows result, Changeset cs)
            => Rows.Stage(mode == StartMode.Edit ? original.All : Enumerable.Empty<BRow>(), result.All, cs, "Creatures");

        /// <summary>For copy-as-new: the original's rows with the new entry and no spawns or text links (those are made fresh).</summary>
        public static NpcRows AsCopy(NpcRows r, long newEntry)
        {
            var c = r.Clone();
            c.Template["entry"] = newEntry;
            if (c.Template.Has("name")) c.Template["name"] = c.Template.S("name");
            c.Spawns.Clear();
            c.Model = c.Model?.Put("CreatureID", newEntry);
            c.Equip = c.Equip?.Put("CreatureID", newEntry);
            c.Trainer = c.Trainer?.Put("CreatureId", newEntry);
            c.Vendor.ForEach(v => v.Put("entry", newEntry));
            c.Gossip = null; c.Text = null;
            if (c.Template.Has("gossip_menu_id")) c.Template["gossip_menu_id"] = r.Template.L("gossip_menu_id");   // shares the original's chat text until changed
            return c;
        }
    }

    // ============================================================================ behaviours (SmartAI made simple)
    public sealed class Behaviour
    {
        public string Key, Name, What;
        /// <summary>The fields it asks for: (key, label, hint, kind: int/text/spell/creature/emote/choice, default, choices).</summary>
        public List<(string key, string label, string hint, string kind, string def, string[] choices)> Inputs = new List<(string, string, string, string, string, string[])>();
    }

    public static class Behaviours
    {
        public static readonly List<Behaviour> All = new List<Behaviour>
        {
            B("cast_timer", "Casts a spell again and again in a fight", "While fighting, it casts a spell every few seconds.",
                ("spell", "Spell", "The spell it casts.", "spell", "133", null), ("every", "Every (seconds)", "How often.", "int", "10", null), ("on", "On", null, "choice", "Whoever it's fighting", new[] { "Whoever it's fighting", "Itself" })),
            B("cast_health", "Casts a spell when its health drops", "Once, when its health falls below a percentage (an enrage, a heal, a shield).",
                ("pct", "When health is below (%)", null, "int", "30", null), ("spell", "Spell", null, "spell", "8599", null), ("on", "On", null, "choice", "Itself", new[] { "Itself", "Whoever it's fighting" })),
            B("say_aggro", "Says something when a fight starts", "A line in chat (or a yell) when it starts fighting.",
                ("text", "What it says", "$N is the player's name.", "text", "You will regret this, $N!", null), ("how", "How", null, "choice", "Say", new[] { "Say", "Yell" })),
            B("say_death", "Says something when it dies", "A last line when it's killed.",
                ("text", "What it says", null, "text", "I... have failed...", null), ("how", "How", null, "choice", "Say", new[] { "Say", "Yell" })),
            B("say_health", "Yells at low health", "Once, when its health falls below a percentage.",
                ("pct", "When health is below (%)", null, "int", "20", null), ("text", "What it yells", null, "text", "You won't defeat me so easily!", null)),
            B("summon", "Calls for reinforcements", "Once, when its health falls below a percentage, it summons helpers next to it.",
                ("pct", "When health is below (%)", null, "int", "50", null), ("creature", "Helper", "The creature it summons.", "creature", "", null), ("count", "How many", null, "int", "2", null)),
            B("flee", "Runs for help when nearly dead", "At 15% health it runs away to find friends.", Array.Empty<(string, string, string, string, string, string[])>()),
            B("greet", "Greets players who walk by", "Out of combat, when a player comes close, it says a line (at most once a minute).",
                ("text", "What it says", null, "text", "Greetings, $N!", null), ("range", "How close (yards)", null, "int", "10", null)),
            B("emote", "Does an animation now and then", "Out of combat, it plays an emote every so often (wave, bow, cheer, talk…).",
                ("emote", "Animation", null, "emote", "1", null), ("every", "Every (seconds)", null, "int", "30", null)),
            B("talk_credit", "Counts for a quest when talked to", "When a player opens its chat window, they get credit as if they'd killed it (for \"speak with\" quests).", Array.Empty<(string, string, string, string, string, string[])>()),
            B("spawn_cast", "Casts a spell on itself when it appears", "When it spawns or respawns (a glow, a shield, a mount).",
                ("spell", "Spell", null, "spell", "", null)),
        };

        static Behaviour B(string key, string name, string what, params (string, string, string, string, string, string[])[] inputs)
            => new Behaviour { Key = key, Name = name, What = what, Inputs = inputs.ToList() };

        public static readonly (string name, long id)[] Emotes = { ("Talk", 1), ("Bow", 2), ("Wave", 3), ("Cheer", 4), ("Exclamation", 5), ("Question", 6), ("Eat", 7), ("Laugh", 11), ("Shout", 22), ("Point", 25), ("Salute", 66), ("Flex", 23), ("Kneel", 16), ("Applaud", 21) };

        public sealed class Result { public List<SmartRow> Scripts = new List<SmartRow>(); public List<BRow> Texts = new List<BRow>(); public string Sentence; }

        /// <summary>The rows a behaviour adds to a creature. <paramref name="nextId"/> is its next free script row; <paramref name="nextGroup"/> its next free text group.</summary>
        public static Result Make(string key, IDictionary<string, string> v, long creature, string creatureName, int nextId, int nextGroup)
        {
            string S(string k) => v.TryGetValue(k, out var s) ? (s ?? "").Trim() : "";
            long N(string k, long def = 0) => long.TryParse(S(k), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : def;
            var res = new Result();
            SmartRow Row(int ev, int act, int target, string comment) { var r = new SmartRow { EntryOrGuid = creature, SourceType = 0, Id = nextId + res.Scripts.Count, EventType = ev, ActionType = act, TargetType = target, Comment = $"{creatureName} - {comment} (Studio)" }; res.Scripts.Add(r); return r; }
            void Text(string text, bool yell) => res.Texts.Add(new BRow("creature_text", "CreatureID", "GroupID", "ID").Put("CreatureID", creature).Put("GroupID", (long)nextGroup).Put("ID", 0L).Put("Text", text)
                .Put("Type", yell ? 14L : 12L).Put("Language", 0L).Put("Probability", 100.0).Put("Emote", 0L).Put("Duration", 0L).Put("Sound", 0L).Put("BroadcastTextId", 0L).Put("TextRange", 0L).Put("comment", creatureName + " (Studio)"));
            switch (key)
            {
                case "cast_timer":
                {
                    long ms = Math.Max(1, N("every", 10)) * 1000;
                    var r = Row(0, 11, S("on") == "Itself" ? 1 : 2, "In Combat - Cast spell");
                    r.EventParams[0] = Math.Min(ms, 3000); r.EventParams[1] = Math.Min(ms, 5000); r.EventParams[2] = ms; r.EventParams[3] = ms + 2000; r.ActionParams[0] = N("spell");
                    break;
                }
                case "cast_health":
                {
                    var r = Row(2, 11, S("on") == "Itself" ? 1 : 2, "Health below - Cast spell");
                    r.EventParams[0] = 0; r.EventParams[1] = Math.Max(1, Math.Min(99, N("pct", 30))); r.ActionParams[0] = N("spell");
                    r.EventFlags = 1;   // once per spawn
                    break;
                }
                case "say_aggro": case "say_death":
                {
                    Text(S("text"), S("how") == "Yell");
                    var r = Row(key == "say_aggro" ? 4 : 6, 1, key == "say_aggro" ? 2 : 1, key == "say_aggro" ? "On Aggro - Say" : "On Death - Say");
                    r.ActionParams[0] = nextGroup;
                    break;
                }
                case "say_health":
                {
                    Text(S("text"), true);
                    var r = Row(2, 1, 1, "Health below - Yell"); r.EventParams[1] = Math.Max(1, Math.Min(99, N("pct", 20))); r.EventFlags = 1; r.ActionParams[0] = nextGroup;
                    break;
                }
                case "summon":
                {
                    long n = Math.Max(1, Math.Min(10, N("count", 2)));
                    for (int i = 0; i < n; i++)
                    {
                        var r = Row(2, 12, 1, "Health below - Summon helper"); r.EventParams[1] = Math.Max(1, Math.Min(99, N("pct", 50))); r.EventFlags = 1;
                        r.ActionParams[0] = N("creature"); r.ActionParams[1] = 4; r.ActionParams[2] = 60000;   // stays until 60 s out of combat
                    }
                    break;
                }
                case "flee": { var r = Row(2, 25, 1, "Health below 15% - Flee for help"); r.EventParams[1] = 15; r.EventFlags = 1; r.ActionParams[0] = 1; break; }
                case "greet":
                {
                    Text(S("text"), false);
                    var r = Row(10, 1, 7, "Sees a player - Say greeting"); r.EventParams[0] = 1; r.EventParams[1] = Math.Max(1, Math.Min(50, N("range", 10))); r.EventParams[2] = 60000; r.EventParams[3] = 60000; r.ActionParams[0] = nextGroup;
                    break;
                }
                case "emote":
                {
                    long ms = Math.Max(5, N("every", 30)) * 1000;
                    var r = Row(1, 5, 1, "Out of Combat - Play emote"); r.EventParams[0] = ms / 2; r.EventParams[1] = ms; r.EventParams[2] = ms; r.EventParams[3] = ms + ms / 2; r.ActionParams[0] = N("emote", 1);
                    break;
                }
                case "talk_credit": { var r = Row(64, 33, 7, "On Gossip Hello - Quest credit"); r.ActionParams[0] = creature; break; }
                case "spawn_cast": { var r = Row(11, 11, 1, "On Respawn - Cast spell on itself"); r.ActionParams[0] = N("spell"); break; }
                default: throw new ArgumentException("Unknown behaviour " + key);
            }
            return res;
        }

        /// <summary>Plain problems with the inputs (empty = fine).</summary>
        public static List<string> Check(string key, IDictionary<string, string> v)
        {
            var p = new List<string>();
            var b = All.FirstOrDefault(x => x.Key == key);
            if (b == null) { p.Add("Pick a behaviour."); return p; }
            foreach (var i in b.Inputs)
            {
                var s = v.TryGetValue(i.key, out var x) ? (x ?? "").Trim() : "";
                if (i.kind is "int" or "spell" or "creature" or "emote")
                {
                    if (!long.TryParse(s, out var n) || n <= 0) p.Add($"{i.label}: enter a number above 0" + (i.kind is "spell" or "creature" ? " (or pick one)." : "."));
                    else if (i.key == "pct" && n > 99) p.Add($"{i.label}: 1 to 99.");
                }
                if (i.kind == "text" && s.Length == 0) p.Add($"{i.label}: write something.");
            }
            return p;
        }
    }
}
