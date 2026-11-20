using System;
using System.Collections.Generic;
using System.Linq;

namespace AndersCraftStudio.Core
{
    public enum FieldKind { Text, LongText, Int, Float, Bool, Enum, Flags, Ref, Money }

    /// <summary>One editable column on a form, with the help Studio shows for it. All help text is Studio's own.</summary>
    public sealed class FieldDef
    {
        public string Tab, Group, Column, Label, Guide, Tip;
        public FieldKind Kind;
        /// <summary>null = the entity's main table; otherwise a side table keyed by the same ID (e.g. quest_template_addon).</summary>
        public string Table;
        public Dictionary<long, string> Options;
        public Dictionary<ulong, string> Bits;
        /// <summary>For Ref fields: creature, item, quest, gameobject (DB) or a DbcNames kind (spell, factiontemplate, faction, family, area, map, skill, title, itemicon, creaturedisplay).</summary>
        public string RefKind;
        public string Hint => "Column: " + (Table != null ? Table + "." : "") + Column;
    }

    public sealed class EntityDef
    {
        public string Key, Title, Singular, Table, KeyCol, NameCol, Glyph;
        /// <summary>Extra column shown under each name in the search list (SQL expression on the main table).</summary>
        public string ListExtra;
        public string[] Tabs;
        public List<FieldDef> Fields = new List<FieldDef>();
        /// <summary>Side tables that share the entity's ID: table → its key column.</summary>
        public Dictionary<string, string> SideTables = new Dictionary<string, string>();
        /// <summary>When set, a column whose value changes what other fields mean (game object type); the form relabels them.</summary>
        public string TypeColumn;
        public IEnumerable<FieldDef> InTab(string tab) => Fields.Where(f => f.Tab == tab);
    }

    public static class EntityDefs
    {
        // built lazily, after all the option tables below are initialised
        static EntityDef creature, item, quest, gameObject, npcText, pageText, broadcastText, gameTele;
        public static EntityDef Creature => creature ??= BuildCreature();
        public static EntityDef Item => item ??= BuildItem();
        public static EntityDef Quest => quest ??= BuildQuest();
        public static EntityDef GameObject => gameObject ??= BuildGameObject();
        public static EntityDef NpcText => npcText ??= BuildNpcText();
        public static EntityDef PageText => pageText ??= BuildPageText();
        public static EntityDef BroadcastText => broadcastText ??= BuildBroadcastText();
        public static EntityDef GameTele => gameTele ??= BuildGameTele();
        public static IEnumerable<EntityDef> All => new[] { Creature, Item, Quest, GameObject, NpcText, PageText, BroadcastText, GameTele };
        public static EntityDef ByKey(string k) => All.FirstOrDefault(e => e.Key == k);

        // ------------------------------------------------------------------ shared lists
        public static readonly Dictionary<long, string> Expansion = new Dictionary<long, string> { [0] = "Classic", [1] = "The Burning Crusade", [2] = "Wrath of the Lich King" };

        public static readonly Dictionary<long, string> CreatureRank = new Dictionary<long, string> { [0] = "Normal", [1] = "Elite", [2] = "Rare elite", [3] = "Boss", [4] = "Rare" };

        public static readonly Dictionary<long, string> CreatureType = new Dictionary<long, string>
        { [0] = "None", [1] = "Beast", [2] = "Dragonkin", [3] = "Demon", [4] = "Elemental", [5] = "Giant", [6] = "Undead", [7] = "Humanoid", [8] = "Critter", [9] = "Mechanical", [10] = "Not specified", [11] = "Totem", [12] = "Non-combat pet", [13] = "Gas cloud" };

        public static readonly Dictionary<long, string> UnitClass = new Dictionary<long, string> { [1] = "Warrior (health only)", [2] = "Paladin (health + mana)", [4] = "Rogue (health, more damage)", [8] = "Mage (more mana, less health)" };

        public static readonly Dictionary<long, string> SpellSchool = new Dictionary<long, string> { [0] = "Physical", [1] = "Holy", [2] = "Fire", [3] = "Nature", [4] = "Frost", [5] = "Shadow", [6] = "Arcane" };

        public static readonly Dictionary<long, string> MovementType = new Dictionary<long, string> { [0] = "Stand still", [1] = "Wander randomly", [2] = "Follow waypoints" };

        public static readonly Dictionary<ulong, string> NpcFlags = new Dictionary<ulong, string>
        {
            [0x1] = "Gossip (can talk)", [0x2] = "Quest giver", [0x10] = "Trainer", [0x20] = "Class trainer", [0x40] = "Profession trainer",
            [0x80] = "Vendor", [0x100] = "Ammo vendor", [0x200] = "Food vendor", [0x400] = "Poison vendor", [0x800] = "Reagent vendor",
            [0x1000] = "Repairs gear", [0x2000] = "Flight master", [0x4000] = "Spirit healer", [0x8000] = "Spirit guide", [0x10000] = "Innkeeper",
            [0x20000] = "Banker", [0x40000] = "Guild charters", [0x80000] = "Tabard designer", [0x100000] = "Battlemaster", [0x200000] = "Auctioneer",
            [0x400000] = "Stable master", [0x800000] = "Guild banker", [0x1000000] = "Click to use (spellclick)", [0x4000000] = "Mailbox",
        };

        public static readonly Dictionary<ulong, string> UnitFlags = new Dictionary<ulong, string>
        {
            [0x2] = "Can't be attacked", [0x4] = "Player loses control", [0x8] = "Attackable by players (PvP)", [0x100] = "Ignores players",
            [0x200] = "Ignores NPCs", [0x1000] = "PvP flagged", [0x2000] = "Silenced", [0x20000] = "Pacified (won't fight)", [0x40000] = "Stunned",
            [0x2000000] = "Can't be selected", [0x4000000] = "Skinnable", [0x20000000] = "Weapons sheathed",
        };

        public static readonly Dictionary<ulong, string> UnitFlags2 = new Dictionary<ulong, string>
        {
            [0x1] = "Feign death look", [0x8] = "Comprehend language", [0x20] = "Mirror image", [0x400] = "Regenerates power", [0x800] = "Spell click while in combat",
        };

        public static readonly Dictionary<ulong, string> FlagsExtra = new Dictionary<ulong, string>
        {
            [0x1] = "Instance bind on kill", [0x2] = "Civilian (won't aggro)", [0x4] = "Can't parry", [0x10] = "Can't block", [0x20] = "Can't deal crushing blows",
            [0x40] = "No XP when killed", [0x80] = "Trigger (invisible helper)", [0x100] = "No taunt", [0x2000] = "Guard (helps players)",
            [0x8000] = "Can't crit", [0x40000] = "Doesn't enter combat",
        };

        public static readonly Dictionary<long, string> AiNames = new Dictionary<long, string>();
        public static readonly string[] AiNameList = { "", "SmartAI", "AggressorAI", "ReactorAI", "PassiveAI", "NullAI", "CritterAI", "GuardAI", "PetAI", "TotemAI", "CombatAI", "ArcherAI", "TurretAI", "VehicleAI" };

        public static readonly Dictionary<long, string> ItemQuality = new Dictionary<long, string>
        { [0] = "Poor (grey)", [1] = "Common (white)", [2] = "Uncommon (green)", [3] = "Rare (blue)", [4] = "Epic (purple)", [5] = "Legendary (orange)", [6] = "Artifact", [7] = "Heirloom (bind to account)" };

        public static readonly Dictionary<long, string> ItemClass = new Dictionary<long, string>
        { [0] = "Consumable", [1] = "Container", [2] = "Weapon", [3] = "Gem", [4] = "Armor", [5] = "Reagent", [6] = "Projectile", [7] = "Trade goods", [9] = "Recipe", [11] = "Quiver", [12] = "Quest", [13] = "Key", [15] = "Miscellaneous", [16] = "Glyph" };

        public static readonly Dictionary<long, string> InventoryType = new Dictionary<long, string>
        {
            [0] = "Not equippable", [1] = "Head", [2] = "Neck", [3] = "Shoulder", [4] = "Shirt", [5] = "Chest", [6] = "Waist", [7] = "Legs", [8] = "Feet", [9] = "Wrist",
            [10] = "Hands", [11] = "Finger", [12] = "Trinket", [13] = "One-hand", [14] = "Shield", [15] = "Ranged (bow)", [16] = "Back", [17] = "Two-hand", [18] = "Bag",
            [19] = "Tabard", [20] = "Robe (chest)", [21] = "Main hand", [22] = "Off hand", [23] = "Held in off hand", [24] = "Ammo", [25] = "Thrown", [26] = "Ranged (gun/wand)", [27] = "Quiver", [28] = "Relic",
        };

        public static readonly Dictionary<long, string> Bonding = new Dictionary<long, string> { [0] = "No binding", [1] = "Binds when picked up", [2] = "Binds when equipped", [3] = "Binds when used", [4] = "Quest item", [5] = "Quest item (alt)" };

        public static readonly Dictionary<long, string> StatType = new Dictionary<long, string>
        {
            [0] = "Mana", [1] = "Health", [3] = "Agility", [4] = "Strength", [5] = "Intellect", [6] = "Spirit", [7] = "Stamina", [12] = "Defense rating", [13] = "Dodge rating",
            [14] = "Parry rating", [15] = "Block rating", [16] = "Melee hit rating", [17] = "Ranged hit rating", [18] = "Spell hit rating", [19] = "Melee crit rating", [20] = "Ranged crit rating",
            [21] = "Spell crit rating", [28] = "Melee haste rating", [29] = "Ranged haste rating", [30] = "Spell haste rating", [31] = "Hit rating", [32] = "Crit rating", [35] = "Resilience rating",
            [36] = "Haste rating", [37] = "Expertise rating", [38] = "Attack power", [39] = "Ranged attack power", [43] = "Mana per 5 sec", [44] = "Armor penetration rating",
            [45] = "Spell power", [46] = "Health per 5 sec", [47] = "Spell penetration", [48] = "Block value",
        };

        public static readonly Dictionary<long, string> SpellTrigger = new Dictionary<long, string> { [0] = "On use", [1] = "When equipped", [2] = "Chance on hit", [4] = "Soulstone", [5] = "Use with no delay", [6] = "Learn spell (recipes)" };

        public static readonly Dictionary<long, string> Sheath = new Dictionary<long, string> { [0] = "None", [1] = "Two-hander on back", [2] = "Staff on back", [3] = "One-hander at side", [4] = "Shield on back", [5] = "Enchanter's rod", [6] = "Off-hand at side" };

        public static readonly Dictionary<long, string> Material = new Dictionary<long, string> { [-1] = "Consumable", [0] = "Not defined", [1] = "Metal", [2] = "Wood", [3] = "Liquid", [4] = "Jewelry", [5] = "Chain", [6] = "Plate", [7] = "Cloth", [8] = "Leather" };

        public static readonly Dictionary<long, string> RepRank = new Dictionary<long, string> { [0] = "Hated", [1] = "Hostile", [2] = "Unfriendly", [3] = "Neutral", [4] = "Friendly", [5] = "Honored", [6] = "Revered", [7] = "Exalted" };

        public static readonly Dictionary<ulong, string> ItemFlags = new Dictionary<ulong, string>
        {
            [0x2] = "Conjured (vanishes on logout)", [0x4] = "Can be opened (has loot)", [0x8] = "Heroic label", [0x20] = "Can't be destroyed", [0x80] = "No cooldown when equipped",
            [0x800] = "Everyone in the party can loot", [0x1000] = "Refundable", [0x80000] = "Unique when equipped", [0x400000] = "Throwable",
            [0x800000] = "Usable in shapeshift forms", [0x8000000] = "Bind to account",
        };

        public static readonly Dictionary<ulong, string> SocketColor = new Dictionary<ulong, string> { [1] = "Meta", [2] = "Red", [4] = "Yellow", [8] = "Blue" };

        public static readonly Dictionary<ulong, string> Classes = new Dictionary<ulong, string>
        { [0x1] = "Warrior", [0x2] = "Paladin", [0x4] = "Hunter", [0x8] = "Rogue", [0x10] = "Priest", [0x20] = "Death Knight", [0x40] = "Shaman", [0x80] = "Mage", [0x100] = "Warlock", [0x400] = "Druid" };

        public static readonly Dictionary<ulong, string> Races = new Dictionary<ulong, string>
        { [0x1] = "Human", [0x2] = "Orc", [0x4] = "Dwarf", [0x8] = "Night Elf", [0x10] = "Undead", [0x20] = "Tauren", [0x40] = "Gnome", [0x80] = "Troll", [0x200] = "Blood Elf", [0x400] = "Draenei" };

        public static readonly Dictionary<long, string> QuestType = new Dictionary<long, string> { [0] = "Auto-complete", [1] = "Disabled", [2] = "Normal" };

        public static readonly Dictionary<long, string> QuestInfo = new Dictionary<long, string>
        { [0] = "Normal", [1] = "Group", [21] = "Life", [41] = "PvP", [62] = "Raid", [81] = "Dungeon", [82] = "World event", [83] = "Legendary", [84] = "Escort", [85] = "Heroic", [88] = "Raid (10)", [89] = "Raid (25)" };

        public static readonly Dictionary<ulong, string> QuestFlags = new Dictionary<ulong, string>
        {
            [0x1] = "Fails if you die", [0x2] = "Party members get asked to accept", [0x4] = "Exploration objective", [0x8] = "Can be shared", [0x20] = "Epic",
            [0x40] = "Raid", [0x200] = "Rewards hidden until done", [0x400] = "Completes automatically (tracking)", [0x1000] = "Daily", [0x2000] = "Flags you for PvP",
            [0x8000] = "Weekly", [0x10000] = "Auto-complete", [0x80000] = "Auto-accept",
        };

        public static readonly Dictionary<ulong, string> QuestSpecialFlags = new Dictionary<ulong, string>
        { [0x1] = "Repeatable", [0x2] = "Completed by a script/event", [0x4] = "Auto-accept", [0x8] = "Dungeon Finder quest", [0x10] = "Monthly", [0x20] = "Objective is a script cast" };

        public static readonly Dictionary<long, string> XpDifficulty = new Dictionary<long, string>
        { [0] = "None", [1] = "Tiny", [2] = "Small", [3] = "Below normal", [4] = "Normal", [5] = "Above normal", [6] = "Large", [7] = "Huge", [8] = "Maximum" };

        public static readonly Dictionary<long, string> Language = new Dictionary<long, string>
        {
            [0] = "Universal (everyone understands)", [1] = "Orcish", [2] = "Darnassian", [3] = "Taurahe", [6] = "Dwarvish", [7] = "Common", [8] = "Demonic", [9] = "Titan",
            [10] = "Thalassian", [11] = "Draconic", [12] = "Kalimag", [13] = "Gnomish", [14] = "Troll", [33] = "Gutterspeak", [35] = "Draenei", [36] = "Zombie", [37] = "Gnomish binary", [38] = "Goblin binary",
        };

        public static readonly Dictionary<long, string> Emote = new Dictionary<long, string>
        {
            [0] = "None", [1] = "Talk", [2] = "Bow", [3] = "Wave", [4] = "Cheer", [5] = "Exclaim", [6] = "Question", [7] = "Eat", [10] = "Dance (keeps dancing)", [11] = "Laugh",
            [14] = "Rude", [15] = "Roar", [16] = "Kneel", [17] = "Kiss", [18] = "Cry", [19] = "Chicken", [20] = "Beg", [21] = "Applaud", [22] = "Shout", [23] = "Flex", [24] = "Shy",
            [25] = "Point", [66] = "Salute", [94] = "Dance (once)", [273] = "Nod yes", [274] = "Shake no", [275] = "Train",
        };

        public static readonly Dictionary<long, string> Visibility = new Dictionary<long, string>
        { [0] = "Normal", [1] = "Tiny (seen only up close)", [2] = "Small", [3] = "Large", [4] = "Gigantic (seen from far away)", [5] = "Infinite (whole map)" };

        public static readonly Dictionary<long, string> GroundMove = new Dictionary<long, string> { [0] = "Can't walk", [1] = "Walks and runs (normal)", [2] = "Hovers above the ground" };
        public static readonly Dictionary<long, string> FlightMove = new Dictionary<long, string> { [0] = "Can't fly", [1] = "Floats (no gravity)", [2] = "Flies" };
        public static readonly Dictionary<long, string> ChaseMove = new Dictionary<long, string> { [0] = "Runs after targets (normal)", [1] = "Walks or runs as needed", [2] = "Always walks" };
        public static readonly Dictionary<long, string> RandomMove = new Dictionary<long, string> { [0] = "Walks (normal)", [1] = "Sometimes runs", [2] = "Always runs" };

        public static readonly Dictionary<ulong, string> GameObjectFlags = new Dictionary<ulong, string>
        {
            [0x1] = "In use (looks activated)", [0x2] = "Locked", [0x4] = "Needs a quest or condition to use", [0x8] = "Transport", [0x10] = "Can't be selected",
            [0x20] = "Never despawns", [0x40] = "Triggered by a script", [0x200] = "Damaged", [0x400] = "Destroyed",
        };

        static EntityDefs() { for (int i = 0; i < AiNameList.Length; i++) AiNames[i] = AiNameList[i] == "" ? "(none: use C++ script or default)" : AiNameList[i]; }

        // ------------------------------------------------------------------ builders
        sealed class B
        {
            readonly EntityDef e; string tab, group, table;
            public B(EntityDef e) { this.e = e; }
            public B Tab(string t) { tab = t; group = null; table = null; return this; }
            public B Group(string g, string side = null) { group = g; table = side; return this; }
            public FieldDef F(string col, string label, FieldKind kind, string guide, string tip, Dictionary<long, string> opts = null, Dictionary<ulong, string> bits = null, string refKind = null)
            {
                var f = new FieldDef { Tab = tab, Group = group, Table = table, Column = col, Label = label, Kind = kind, Guide = guide, Tip = tip, Options = opts, Bits = bits, RefKind = refKind };
                e.Fields.Add(f);
                return f;
            }
        }

        static EntityDef BuildCreature()
        {
            var e = new EntityDef
            {
                Key = "creatures", Title = "Creatures", Singular = "creature", Table = "creature_template", KeyCol = "entry", NameCol = "name", Glyph = "",
                ListExtra = "CONCAT('Level ', minlevel, IF(maxlevel <> minlevel, CONCAT('-', maxlevel), ''), IF(subname <> '', CONCAT(' · ', subname), ''))",
                Tabs = new[] { "Basics", "Stats & combat", "Look & movement", "Spawns", "Equipment", "Vendor/trainer", "Loot", "SmartAI", "Extras" },
            };
            e.SideTables["creature_template_addon"] = "entry";
            e.SideTables["creature_template_movement"] = "CreatureId";
            e.SideTables["creature_onkill_reputation"] = "creature_id";
            var b = new B(e);
            b.Tab("Basics").Group("Identity");
            b.F("name", "Name", FieldKind.Text, "The name players see above the creature and in its tooltip.", "Keep it short; long names get cut off on nameplates.");
            b.F("subname", "Title under the name", FieldKind.Text, "The second line shown in angle brackets, like <Innkeeper> or <Weapon Merchant>.", "Leave empty for ordinary monsters.");
            b.F("IconName", "Mouse cursor", FieldKind.Text, "The cursor shown when hovering it: e.g. Speak, Buy, Repair, Trainer, Taxi, Interact, LootAll.", "Match the cursor to what the NPC does, so players know they can click it.");
            b.F("minlevel", "Lowest level", FieldKind.Int, "Each spawn picks a level between the lowest and highest level.", "Set both to the same value for a fixed level.");
            b.F("maxlevel", "Highest level", FieldKind.Int, "Upper end of the level range. Health, damage and armor scale with level.", "Bosses are usually 2-3 levels above the zone, or 83 in Northrend raids.");
            b.F("exp", "Expansion stat table", FieldKind.Enum, "Which expansion's base stats are used for health and damage at this level.", "Wrath creatures above level 70 should use Wrath of the Lich King, or they end up very weak.", Expansion);
            b.F("faction", "Faction", FieldKind.Ref, "Decides who the creature is friendly, neutral or hostile to (a FactionTemplate ID).", "Copy the faction of a similar creature in the same zone for predictable behaviour.", refKind: "factiontemplate");
            b.F("rank", "Rank", FieldKind.Enum, "Elite, rare and boss ranks change the portrait frame and give extra health.", "Rare creatures get a silver dragon frame; boss shows a skull level.", CreatureRank);
            b.F("type", "Creature type", FieldKind.Enum, "Beast, humanoid, undead... Affects which spells and skills work on it (skinning, Hibernate, Shackle).", "Beasts with a family can be tamed by hunters if the tameable type flag is set.", CreatureType);
            b.F("family", "Beast family", FieldKind.Ref, "For beasts: wolf, cat, spider... decides pet abilities and diet if tamed.", "Use 0 for non-beasts.", refKind: "family");
            b.F("unit_class", "Power type (class)", FieldKind.Enum, "Gives the creature mana or not, and adjusts base health and damage.", "Casters should be Paladin or Mage so they have mana for spells.", UnitClass);
            b.Group("Interaction");
            b.F("npcflag", "What talking to it does", FieldKind.Flags, "Tick every service this NPC offers. Each one adds an option or window when a player right-clicks it.", "Vendors also need items in npc_vendor; trainers need a trainer list.", bits: NpcFlags);
            b.F("gossip_menu_id", "Gossip menu", FieldKind.Int, "Which chat window (gossip_menu) opens when you talk to it. 0 = default greeting.", "Gossip only shows if the Gossip flag is ticked above.");
            b.F("AIName", "AI type", FieldKind.Enum, "The built-in brain. SmartAI means behaviour comes from smart_scripts (see the SmartAI tab).", "Pick SmartAI if you want to script it without C++.", AiNames);
            b.F("ScriptName", "C++ script", FieldKind.Text, "Name of a C++ script compiled into the server. Overrides AIName when set.", "Only set this to a script that exists in your build, or the creature falls back to default AI.");
            b.F("MovementType", "Default movement", FieldKind.Enum, "How spawns of this creature move by default (each spawn can override it).", "Wander distance is set per spawn on the Spawns tab.", MovementType);
            b.Group("Linked creatures");
            b.F("difficulty_entry_1", "Heroic / 25-man version", FieldKind.Ref, "In dungeons and raids, the creature used on heroic (or 25-player) difficulty.", "Leave 0 if it's the same in every difficulty.", refKind: "creature");
            b.F("difficulty_entry_2", "Heroic 10 version", FieldKind.Ref, "Raid difficulty 3 (10-player heroic) replacement.", "Only used in raids.", refKind: "creature");
            b.F("difficulty_entry_3", "Heroic 25 version", FieldKind.Ref, "Raid difficulty 4 (25-player heroic) replacement.", "Only used in raids.", refKind: "creature");
            b.F("KillCredit1", "Counts as (kill credit 1)", FieldKind.Ref, "Killing this creature also counts as killing this other creature for quests.", "Handy when several look-alike creatures should all count for one quest.", refKind: "creature");
            b.F("KillCredit2", "Counts as (kill credit 2)", FieldKind.Ref, "A second creature this one gives kill credit for.", "Rarely needed.", refKind: "creature");

            b.Tab("Stats & combat").Group("Toughness");
            b.F("HealthModifier", "Health multiplier", FieldKind.Float, "Multiplies the normal health for its level and rank. 1 = normal, 2 = double.", "Small changes go a long way: 1.5 already feels much tankier.");
            b.F("ManaModifier", "Mana multiplier", FieldKind.Float, "Multiplies base mana. Only matters if the power type gives mana.", "Raise it for casters that run out of mana too fast.");
            b.F("ArmorModifier", "Armor multiplier", FieldKind.Float, "Multiplies base armor, which reduces physical damage taken.", "1 is right for nearly everything.");
            b.F("RegenHealth", "Regenerates health", FieldKind.Bool, "When ticked, it heals back up when out of combat.", "Untick for training dummies or event creatures that should stay hurt.");
            b.Group("Damage");
            b.F("DamageModifier", "Damage multiplier", FieldKind.Float, "Multiplies melee damage. 1 = normal for its level.", "Elites in dungeons are usually around 1.5-2.");
            b.F("BaseAttackTime", "Melee swing time (ms)", FieldKind.Int, "Milliseconds between melee swings. 2000 = one swing every 2 seconds.", "Slower swings with the same multiplier hit harder per swing.");
            b.F("RangeAttackTime", "Ranged attack time (ms)", FieldKind.Int, "Milliseconds between ranged attacks, if it shoots.", "Leave at 2000 unless it uses a bow or gun.");
            b.F("BaseVariance", "Melee damage spread", FieldKind.Float, "How much each hit varies. 1 = normal spread.", "Lower values make damage more predictable.");
            b.F("RangeVariance", "Ranged damage spread", FieldKind.Float, "Variation of ranged hits.", "Usually 1.");
            b.F("dmgschool", "Melee damage school", FieldKind.Enum, "The school of its melee hits. Physical is normal; anything else ignores armor and uses resistances.", "A fire elemental hitting with Fire damage feels right but bypasses armor.", SpellSchool);
            b.Group("Movement & senses");
            b.F("speed_walk", "Walk speed", FieldKind.Float, "Walking speed multiplier. 1 = normal.", "Patrolling guards look natural around 1.");
            b.F("speed_run", "Run speed", FieldKind.Float, "Running speed multiplier. About 1.14 is a normal run.", "Above 1.4 players can't outrun it on foot.");
            b.F("speed_swim", "Swim speed", FieldKind.Float, "Swimming speed multiplier.", "Usually 1.");
            b.F("speed_flight", "Flight speed", FieldKind.Float, "Flying speed multiplier for creatures that fly.", "Usually 1.");
            b.F("detection_range", "Aggro detection range", FieldKind.Float, "How far away (yards) it can notice targets. The real aggro radius also depends on level difference.", "20 is typical. Raise it for sentries.");
            b.F("HoverHeight", "Hover height", FieldKind.Float, "How high above the ground a hovering creature floats.", "Only matters for creatures that hover.");
            b.Group("Behaviour flags");
            b.F("unit_flags", "Unit flags", FieldKind.Flags, "Special states, such as not attackable or not selectable.", "Combining 'Can't be attacked' with 'Can't be selected' makes an invisible helper.", bits: UnitFlags);
            b.F("unit_flags2", "Unit flags 2", FieldKind.Flags, "More rarely used states.", "Most creatures need none of these.", bits: UnitFlags2);
            b.F("flags_extra", "Server-side extras", FieldKind.Flags, "Rules the server applies that the game client never sees.", "'Civilian' stops a guard from attacking low-level players on sight.", bits: FlagsExtra);
            b.F("RacialLeader", "Racial leader", FieldKind.Bool, "Marks a faction leader (like a capital city's ruler): killing it gives honor.", "Only for real faction leaders.");
            b.F("ExperienceModifier", "XP multiplier", FieldKind.Float, "Multiplies the experience a kill gives.", "0 gives no XP at all.");
            b.Group("Loot & money", null);
            b.F("mingold", "Least money dropped", FieldKind.Money, "Smallest amount of copper it drops. 100 copper = 1 silver.", "Leave both at 0 for creatures that shouldn't carry coins.");
            b.F("maxgold", "Most money dropped", FieldKind.Money, "Largest amount of copper it drops.", "Keep it in line with other creatures of the same level.");

            b.Tab("Look & movement").Group("Look and pose (every spawn)", "creature_template_addon");
            b.F("mount", "Riding on", FieldKind.Ref, "A mount model the creature sits on (CreatureDisplayInfo ID). 0 = on foot.", "Pick a horse, wolf or drake display to put a rider on it.", refKind: "creaturedisplay");
            b.F("bytes1", "Stand state", FieldKind.Int, "How it stands. The lowest byte: 0 standing, 1 sitting, 3 sleeping, 7 dead, 8 kneeling. Higher bytes hold rarely used visual states.", "1 sits it on the ground; 8 makes it kneel, which suits worshippers and prisoners.");
            b.F("bytes2", "Weapon pose", FieldKind.Int, "The lowest byte is how weapons are held: 0 put away, 1 melee in hand, 2 ranged in hand. Higher bytes hold PvP flags.", "1 makes guards hold their swords out.");
            b.F("emote", "Looping animation", FieldKind.Int, "An animation it keeps playing (an emote state ID), e.g. 10 dance, 28 work (weapon away), 69 use something standing, 173 work, 233 mine, 234 chop wood.", "0 = no looping animation.");
            b.F("visibilityDistanceType", "Seen from", FieldKind.Enum, "How far away players can see it.", "Large or Gigantic suits big bosses and landmarks.", Visibility);
            b.F("auras", "Auras always on", FieldKind.Text, "Spell IDs it always has active, separated by spaces (e.g. a glow, a ghost look or stealth).", "Use the Spells workspace to look up IDs.");
            b.F("path_id", "Waypoint path", FieldKind.Int, "A waypoint_data path every spawn of it walks (World tables › Waypoints). 0 = none; spawns can also have their own.", "The spawns also need movement type Follow waypoints.");
            b.Group("Movement abilities", "creature_template_movement");
            b.F("Ground", "On the ground", FieldKind.Enum, "Whether it can move on the ground.", "Hover suits ghosts and elementals.", GroundMove);
            b.F("Swim", "Can swim", FieldKind.Bool, "Whether it can follow targets into water.", "Untick to keep land creatures out of lakes.");
            b.F("Flight", "In the air", FieldKind.Enum, "Whether it can fly.", "Flies lets it chase players who are flying.", FlightMove);
            b.F("Rooted", "Rooted in place", FieldKind.Bool, "When ticked, it never moves (turrets, totems, trees).", "It can still turn and attack in range.");
            b.F("Chase", "When chasing", FieldKind.Enum, "How it follows a target in combat.", "Always walks makes slow, menacing bosses.", ChaseMove);
            b.F("Random", "When wandering", FieldKind.Enum, "How it moves when wandering randomly.", "Normal is right for nearly everything.", RandomMove);
            b.F("InteractionPauseTimer", "Pause when talked to (ms)", FieldKind.Int, "How long it stops walking when a player talks to it. -1 = the server's default.", "Longer pauses suit quest givers who walk around.");

            b.Tab("Loot").Group("Reputation for killing it", "creature_onkill_reputation");
            b.F("RewOnKillRepFaction1", "Faction 1", FieldKind.Ref, "A faction whose reputation changes when it's killed.", "Use a negative value below to lose reputation instead.", refKind: "faction");
            b.F("RewOnKillRepValue1", "Faction 1 amount", FieldKind.Float, "Reputation points per kill (negative = lost).", "5 is typical for ordinary mobs, 100+ for bosses.");
            b.F("MaxStanding1", "Faction 1 stops at", FieldKind.Enum, "Kills stop giving reputation once you reach this standing.", "Honored or Revered caps are common for repeatable kills.", RepRank);
            b.F("IsTeamAward1", "Faction 1 for the whole team", FieldKind.Bool, "Gives it to the faction's parent team as well.", "Rarely used.");
            b.F("RewOnKillRepFaction2", "Faction 2", FieldKind.Ref, "A second faction affected by the kill.", "Often the rival faction losing reputation.", refKind: "faction");
            b.F("RewOnKillRepValue2", "Faction 2 amount", FieldKind.Float, "Reputation points for faction 2 (negative = lost).", "Leave 0 if only one faction is involved.");
            b.F("MaxStanding2", "Faction 2 stops at", FieldKind.Enum, "Cap for faction 2.", "Exalted means no cap.", RepRank);
            b.F("IsTeamAward2", "Faction 2 for the whole team", FieldKind.Bool, "Gives it to faction 2's parent team as well.", "Rarely used.");
            b.F("TeamDependent", "Depends on the killer's side", FieldKind.Bool, "When ticked, Alliance killers get faction 1 and Horde killers get faction 2.", "Used for creatures both sides fight, like in Wintergrasp.");

            b.Tab("Loot").Group("Loot tables");
            b.F("lootid", "Loot table ID", FieldKind.Int, "Which creature_loot_template entry it drops. Usually the same number as the creature's own ID.", "0 means no item loot at all.");
            b.F("pickpocketloot", "Pickpocket table ID", FieldKind.Int, "What rogues can steal from it (pickpocketing_loot_template).", "Only humanoids can usually be pickpocketed.");
            b.F("skinloot", "Skinning table ID", FieldKind.Int, "What skinning gives (skinning_loot_template). Also used for herbalism/mining on special creatures.", "Needs the Skinnable flag on the corpse.");
            return e;
        }

        static EntityDef BuildItem()
        {
            var e = new EntityDef
            {
                Key = "items", Title = "Items", Singular = "item", Table = "item_template", KeyCol = "entry", NameCol = "name", Glyph = "",
                ListExtra = "CONCAT('Item level ', ItemLevel, IF(RequiredLevel > 0, CONCAT(' · requires ', RequiredLevel), ''))",
                Tabs = new[] { "Basics", "Stats", "Weapon/armor", "Requirements", "Spells", "Prices", "Display", "Contains & enchants" },
            };
            var b = new B(e);
            b.Tab("Basics").Group("Identity");
            b.F("name", "Name", FieldKind.Text, "The item name in tooltips, bags and chat links. Its colour comes from the quality.", "Keep names unique so players can search the auction house for them.");
            b.F("description", "Flavour text", FieldKind.Text, "The yellow quoted line at the bottom of the tooltip.", "One short sentence works best.");
            b.F("Quality", "Quality", FieldKind.Enum, "Colour and rarity. Affects how much it sells for and who can roll on it in groups.", "Higher quality alone doesn't add stats; set those on the Stats tab.", ItemQuality);
            b.F("ItemLevel", "Item level", FieldKind.Int, "Overall power rating. Used for disenchanting and for some automatic values.", "Keep it close to items with similar stats.");
            b.F("class", "Item class", FieldKind.Enum, "The broad kind of item. Together with the subclass, it decides who can use it.", "Changing the class of an existing item can confuse players' bags; prefer a new item.", ItemClass);
            b.F("subclass", "Subclass", FieldKind.Int, "The specific kind within the class. Weapons: 0 one-hand axe, 1 two-hand axe, 4 one-hand mace, 7 one-hand sword, 8 two-hand sword, 10 staff, 13 fist, 15 dagger, 2 bow, 3 gun, 19 wand. Armor: 1 cloth, 2 leather, 3 mail, 4 plate, 6 shield.", "The subclass decides which weapon or armor skill is needed to equip it.");
            b.F("InventoryType", "Equip slot", FieldKind.Enum, "Where it goes when equipped, or Not equippable.", "Two-hand weapons block the off hand.", InventoryType);
            b.F("bonding", "Binding", FieldKind.Enum, "When the item becomes soulbound to a player.", "Bind on equip items can be traded until worn.", Bonding);
            b.Group("Stacking & limits");
            b.F("stackable", "Stack size", FieldKind.Int, "How many fit in one bag slot.", "1 for gear; 20 for potions; 200 for many trade goods.");
            b.F("maxcount", "Most a player can own", FieldKind.Int, "0 = no limit. 1 makes it Unique.", "Use 1 for quest items and special trinkets.");
            b.F("ContainerSlots", "Bag slots", FieldKind.Int, "For bags: how many slots it adds.", "Only used when the class is Container.");
            b.F("startquest", "Starts a quest", FieldKind.Ref, "Right-clicking the item offers this quest (like a wanted poster you loot).", "The quest should also exist in the Quests workspace.", refKind: "quest");
            b.F("Flags", "Item flags", FieldKind.Flags, "Special behaviour such as conjured, openable or unique-equipped.", "Openable items need a matching item_loot_template entry.", bits: ItemFlags);
            b.F("ScriptName", "C++ script", FieldKind.Text, "Name of a C++ item script, if any.", "Leave empty unless you have the script in your build.");

            b.Tab("Stats").Group("Primary stats");
            for (int i = 1; i <= 10; i++)
            {
                b.F("stat_type" + i, $"Stat {i}: type", FieldKind.Enum, "Which stat this line adds (Stamina, Strength, crit rating...).", "Leave unused lines at type 0 with value 0.", StatType);
                b.F("stat_value" + i, $"Stat {i}: amount", FieldKind.Int, "How much of the chosen stat the item gives.", "Negative values remove the stat, which players rarely enjoy.");
            }
            b.Group("Resistances");
            foreach (var (c, n) in new[] { ("holy_res", "Holy"), ("fire_res", "Fire"), ("nature_res", "Nature"), ("frost_res", "Frost"), ("shadow_res", "Shadow"), ("arcane_res", "Arcane") })
                b.F(c, n + " resistance", FieldKind.Int, $"Adds {n.ToLowerInvariant()} resistance, reducing {n.ToLowerInvariant()} spell damage taken.", "Resistance gear is mostly useful for specific raid bosses.");

            b.Tab("Weapon/armor").Group("Weapon damage");
            b.F("dmg_min1", "Minimum damage", FieldKind.Float, "Lowest damage of a normal hit.", "Damage per second = average damage ÷ (speed in seconds).");
            b.F("dmg_max1", "Maximum damage", FieldKind.Float, "Highest damage of a normal hit.", "Keep the max about 1.5-2x the min for a normal feel.");
            b.F("dmg_type1", "Damage school", FieldKind.Enum, "School of the main damage. Physical for nearly all weapons.", "Wands use a magic school.", SpellSchool);
            b.F("dmg_min2", "Bonus damage (min)", FieldKind.Float, "Optional second damage line, shown as '+X Fire damage'.", "Leave at 0 for normal weapons.");
            b.F("dmg_max2", "Bonus damage (max)", FieldKind.Float, "Upper end of the bonus damage line.", "Leave at 0 for normal weapons.");
            b.F("dmg_type2", "Bonus damage school", FieldKind.Enum, "School of the bonus damage.", "A fire-coloured name with Fire bonus damage reads well.", SpellSchool);
            b.F("delay", "Weapon speed (ms)", FieldKind.Int, "Milliseconds between swings. 2600 shows as Speed 2.60.", "Slow two-handers (3.4-3.8) favour big hits.");
            b.F("ammo_type", "Ammo type", FieldKind.Int, "For bows and guns: 2 arrows, 3 bullets.", "0 for everything else.");
            b.F("RangedModRange", "Range bonus", FieldKind.Float, "Extra range for ranged weapons.", "Usually 100 for bows, guns and crossbows.");
            b.Group("Armor");
            b.F("armor", "Armor", FieldKind.Int, "Armor value added when equipped.", "Plate gives roughly twice the armor of leather at the same level.");
            b.F("block", "Block value", FieldKind.Int, "For shields: damage absorbed by a successful block.", "Only used on shields.");
            b.F("ArmorDamageModifier", "Armor modifier", FieldKind.Float, "Fine-tunes the armor value. Usually 0.", "Leave at 0.");
            b.F("MaxDurability", "Durability", FieldKind.Int, "How much wear it takes before breaking. 0 = never needs repair.", "Gear normally has 25-120.");
            b.F("sheath", "How it's worn when sheathed", FieldKind.Enum, "Where the model sits on the character when not in use.", "Match the weapon type, or it floats oddly.", Sheath);
            b.Group("Sockets");
            for (int i = 1; i <= 3; i++) b.F("socketColor_" + i, $"Socket {i} colour", FieldKind.Flags, "The gem colour this socket accepts. Leave empty for no socket.", "Matching all socket colours activates the socket bonus.", bits: SocketColor);
            b.F("socketBonus", "Socket bonus (enchant ID)", FieldKind.Int, "The bonus granted when all sockets match their colours (a SpellItemEnchantment ID).", "Copy the ID from a similar item.");
            b.F("GemProperties", "Gem properties", FieldKind.Int, "For gems: which bonus the gem gives (GemProperties ID).", "Only for items of class Gem.");

            b.Tab("Requirements").Group("Who can use it");
            b.F("RequiredLevel", "Required level", FieldKind.Int, "Minimum character level to equip or use it.", "Usually a few levels below the item level for leveling gear.");
            b.F("AllowableClass", "Classes", FieldKind.Flags, "Which classes can use it. Leave all unticked (value -1 or 0) for every class.", "Class-locked items show 'Classes: ...' in the tooltip.", bits: Classes);
            b.F("AllowableRace", "Races", FieldKind.Flags, "Which races can use it. Leave all unticked for everyone.", "Faction-specific rewards usually lock races.", bits: Races);
            b.F("RequiredSkill", "Required skill", FieldKind.Ref, "A skill the player needs (e.g. Engineering for goggles).", "Pair it with a skill rank below.", refKind: "skill");
            b.F("RequiredSkillRank", "Required skill rank", FieldKind.Int, "Minimum rank in the required skill.", "0 if no skill is needed.");
            b.F("requiredspell", "Required spell", FieldKind.Ref, "A spell the player must know (e.g. a profession specialisation).", "Rarely used.", refKind: "spell");
            b.F("RequiredReputationFaction", "Reputation faction", FieldKind.Ref, "A faction you need standing with to buy or use it.", "Combine with the required standing below.", refKind: "faction");
            b.F("RequiredReputationRank", "Required standing", FieldKind.Enum, "Minimum reputation with that faction.", "Friendly to Exalted are the useful ones.", RepRank);
            b.F("requiredhonorrank", "Required PvP rank", FieldKind.Int, "Old PvP rank requirement. 0 = none.", "Leave at 0.");
            b.F("Map", "Only usable on map", FieldKind.Ref, "Restricts use to one map (e.g. an instance).", "0 = anywhere.", refKind: "map");
            b.F("area", "Only usable in area", FieldKind.Ref, "Restricts use to one zone or area.", "0 = anywhere.", refKind: "area");
            b.F("HolidayId", "Only during holiday", FieldKind.Int, "Usable only while this holiday event is active.", "0 = always.");
            b.F("ItemLimitCategory", "Limit category", FieldKind.Int, "Shared carry limit with similar items (e.g. healthstones).", "0 = no shared limit.");

            b.Tab("Spells");
            for (int i = 1; i <= 5; i++)
            {
                b.Group($"Spell {i}");
                b.F("spellid_" + i, "Spell", FieldKind.Ref, "The spell this item casts or grants.", "Use the picker to find a spell by name.", refKind: "spell");
                b.F("spelltrigger_" + i, "When it triggers", FieldKind.Enum, "Use = click it; Equip = passive while worn; Chance on hit = procs from weapon hits.", "Equip effects show in green 'Equip:' lines.", SpellTrigger);
                b.F("spellcharges_" + i, "Charges", FieldKind.Int, "Uses before it's spent. 0 = unlimited; negative = item is consumed when charges run out.", "Potions use -1.");
                b.F("spellppmRate_" + i, "Procs per minute", FieldKind.Float, "For chance-on-hit: average procs per minute, independent of weapon speed.", "Around 1-2 is typical.");
                b.F("spellcooldown_" + i, "Cooldown (ms)", FieldKind.Int, "Cooldown of this spell after use. -1 = use the spell's own cooldown.", "60000 = 1 minute.");
                b.F("spellcategory_" + i, "Cooldown category", FieldKind.Int, "Items in the same category share a cooldown (e.g. all healing potions).", "0 = no shared cooldown.");
                b.F("spellcategorycooldown_" + i, "Category cooldown (ms)", FieldKind.Int, "Length of the shared category cooldown.", "-1 = default.");
            }

            b.Tab("Prices").Group("Vendor prices");
            b.F("BuyPrice", "Buy price", FieldKind.Money, "What a vendor charges, in copper (100 copper = 1 silver, 10,000 = 1 gold).", "Usually 4-5x the sell price.");
            b.F("SellPrice", "Sell price", FieldKind.Money, "What a vendor pays you for it, in copper. 0 = can't be sold.", "Grey junk often sells for a few silver.");
            b.F("BuyCount", "Bought in stacks of", FieldKind.Int, "How many you get per purchase.", "Ammo and reagents are often sold in 5s or 20s.");

            b.Tab("Display").Group("Appearance");
            b.F("displayid", "Model / icon", FieldKind.Ref, "Which 3D model and bag icon the item uses (ItemDisplayInfo ID).", "Borrow the display ID of an existing item with the look you want.", refKind: "itemicon");
            b.F("Material", "Material sound", FieldKind.Enum, "The sound it makes when moved in bags.", "Match the armor type: plate, mail, leather or cloth.", Material);
            b.F("itemset", "Item set", FieldKind.Int, "Set ID (ItemSet.dbc). Wearing several pieces gives set bonuses.", "0 = not part of a set.");
            b.F("RandomProperty", "Random enchant group", FieldKind.Int, "Gives a random 'of the Bear' style suffix from item_enchantment_template.", "0 = fixed stats.");
            b.F("RandomSuffix", "Random suffix group", FieldKind.Int, "Like the random enchant group, but scaled to item level.", "Use either this or the random enchant group, not both.");
            b.F("PageText", "Readable text page", FieldKind.Ref, "Makes the item readable (page_text ID), like letters and books. Open it to edit the pages.", "0 = not readable.", refKind: "pagetext");
            b.F("lockid", "Lock", FieldKind.Int, "For lockboxes: which lock (Lock.dbc) must be picked or opened.", "0 = not locked.");
            b.F("duration", "Lifetime (seconds)", FieldKind.Int, "The item disappears after this long. 0 = forever.", "Good for temporary event items.");
            b.F("FoodType", "Pet food type", FieldKind.Int, "For food: which hunter pet diets accept it (1 meat, 2 fish, 3 cheese, 4 bread, 5 fungus, 6 fruit).", "0 for non-food.");
            return e;
        }

        static EntityDef BuildQuest()
        {
            var e = new EntityDef
            {
                Key = "quests", Title = "Quests", Singular = "quest", Table = "quest_template", KeyCol = "ID", NameCol = "LogTitle", Glyph = "",
                ListExtra = "CONCAT('Level ', QuestLevel, ' · min ', MinLevel)",
                Tabs = new[] { "Basics", "Objectives", "Rewards", "Texts", "Starters/enders", "Chain" },
            };
            e.SideTables["quest_template_addon"] = "ID";
            e.SideTables["quest_request_items"] = "ID";
            e.SideTables["quest_offer_reward"] = "ID";
            const string A = "quest_template_addon";
            var b = new B(e);
            b.Tab("Basics").Group("Identity");
            b.F("LogTitle", "Quest title", FieldKind.Text, "The name in the quest log and on the quest giver's list.", "Short titles fit the quest tracker better.");
            b.F("QuestLevel", "Quest level", FieldKind.Int, "Shown as [level] and decides the colour and the experience reward. -1 = same as the player.", "Use -1 for holiday quests that any level can do.");
            b.F("MinLevel", "Minimum level", FieldKind.Int, "Players below this level can't pick it up.", "Usually a few levels below the quest level.");
            b.F("QuestType", "Quest type", FieldKind.Enum, "Normal for most quests. Auto-complete finishes as soon as it's accepted; Disabled hides it.", "Disabled is a quick way to switch a quest off without deleting it.", QuestType);
            b.F("QuestInfoID", "Category tag", FieldKind.Enum, "Tag shown in the log, like Group, Dungeon or Raid.", "Group quests suggest how many players you need below.", QuestInfo);
            b.F("SuggestedGroupNum", "Suggested players", FieldKind.Int, "Shown as 'Suggested Players [N]'.", "0 = solo.");
            b.F("QuestSortID", "Zone or category", FieldKind.Int, "Positive = zone (AreaTable ID) the quest is listed under; negative = a special category (QuestSort ID, e.g. -61 Warlock).", "Players find quests in their log under this heading.");
            b.F("Flags", "Quest flags", FieldKind.Flags, "Special rules: sharable, daily, weekly, raid...", "Daily quests also need a repeatable special flag on the addon table.", bits: QuestFlags);
            b.F("TimeAllowed", "Time limit (seconds)", FieldKind.Int, "If set, the quest fails when the timer runs out.", "0 = no timer.");
            b.F("AllowableRaces", "Races", FieldKind.Flags, "Which races can take it. Leave all unticked for everyone.", "Tick all Alliance races to make an Alliance-only quest.", bits: Races);
            b.Group("Text");
            b.F("LogDescription", "Objective summary", FieldKind.LongText, "The short 'what to do' text at the top of the quest log.", "Name exact counts here, e.g. 'Kill 10 Kobold Vermin'.");
            b.F("QuestDescription", "Story text", FieldKind.LongText, "The full text the quest giver says when offering the quest. $N = player name, $C = class, $R = race.", "Break long text with $B for new lines.");
            b.F("AreaDescription", "Area text", FieldKind.LongText, "Extra objective text, mostly used for exploration quests.", "Usually empty.");
            b.F("QuestCompletionLog", "Completion log text", FieldKind.LongText, "Text shown in the log once objectives are done (e.g. 'Return to X').", "Tell the player exactly who to go back to.");
            b.Group("Extra rules", A);
            b.F("MaxLevel", "Maximum level", FieldKind.Int, "Players above this level can't take it. 0 = no cap.", "Useful for low-level event quests.");
            b.F("AllowableClasses", "Classes", FieldKind.Flags, "Which classes can take it. Leave all unticked for everyone.", "Class quests should also use a negative QuestSortID class category.", bits: Classes);
            b.F("SpecialFlags", "Special flags", FieldKind.Flags, "Server-side rules such as repeatable.", "A daily quest needs both the Daily flag and Repeatable here.", bits: QuestSpecialFlags);

            b.Tab("Objectives");
            for (int i = 1; i <= 4; i++)
            {
                b.Group($"Kill or use {i}");
                b.F("RequiredNpcOrGo" + i, "Creature (or -object)", FieldKind.Ref, "Creature to kill (positive ID) or game object to use (negative ID).", "Use kill credit on other creatures to let look-alikes count too.", refKind: "creature");
                b.F("RequiredNpcOrGoCount" + i, "How many", FieldKind.Int, "How many kills or uses are needed.", "10-12 kills is a classic quest size.");
                b.F("ObjectiveText" + i, "Custom objective text", FieldKind.Text, "Replaces the default 'X slain' line in the tracker.", "Use it when the objective isn't a kill, e.g. 'Totem placed'.");
            }
            for (int i = 1; i <= 6; i++)
            {
                b.Group($"Collect {i}");
                b.F("RequiredItemId" + i, "Item to collect", FieldKind.Ref, "An item the player must have when handing in.", "Quest items should drop only while the quest is active (loot tables, Milestone 3).", refKind: "item");
                b.F("RequiredItemCount" + i, "How many", FieldKind.Int, "How many of that item are needed.", "The items are taken when the quest is completed.");
            }
            b.Group("Other requirements");
            b.F("StartItem", "Item given on accept", FieldKind.Ref, "An item handed to the player when they accept (like a letter to deliver).", "It's removed if the quest is abandoned.", refKind: "item");
            b.F("RequiredPlayerKills", "Players to kill", FieldKind.Int, "Number of enemy players to defeat (PvP quests).", "0 for normal quests.");
            b.F("RequiredFactionId1", "Reputation faction 1", FieldKind.Ref, "A faction you must reach a certain standing with.", "Pair it with the value below.", refKind: "faction");
            b.F("RequiredFactionValue1", "Reputation needed 1", FieldKind.Int, "Reputation points needed with faction 1 (e.g. 9000 = Honored).", "Friendly starts at 3000, Honored 9000, Revered 21000, Exalted 42000.");
            b.F("RequiredFactionId2", "Reputation faction 2", FieldKind.Ref, "A second reputation requirement.", "Rarely used.", refKind: "faction");
            b.F("RequiredFactionValue2", "Reputation needed 2", FieldKind.Int, "Reputation points needed with faction 2.", "Rarely used.");
            b.Group("Other requirements", A);
            b.F("SourceSpellID", "Spell cast on accept", FieldKind.Ref, "A spell cast on the player when they accept (e.g. a disguise).", "0 = none.", refKind: "spell");
            b.F("RequiredSkillID", "Required skill", FieldKind.Ref, "A profession or skill needed to take it.", "Profession quests use this.", refKind: "skill");
            b.F("RequiredSkillPoints", "Required skill rank", FieldKind.Int, "Minimum rank in that skill.", "0 if no skill is needed.");

            b.Tab("Rewards").Group("Experience & money");
            b.F("RewardXPDifficulty", "Experience reward", FieldKind.Enum, "XP tier, scaled by the quest level. Normal is right for most quests.", "Players at max level get extra money instead of XP.", XpDifficulty);
            b.F("RewardMoney", "Money reward", FieldKind.Money, "Copper given on completion (negative = the quest costs money).", "100 copper = 1 silver, 10,000 = 1 gold.");
            b.F("RewardHonor", "Honor reward", FieldKind.Int, "Honor points given.", "For PvP quests.");
            b.F("RewardTalents", "Talent points", FieldKind.Int, "Bonus talent points (used by Death Knight starting quests).", "Almost always 0.");
            b.F("RewardArenaPoints", "Arena points", FieldKind.Int, "Arena points given.", "Rarely used.");
            b.Group("Items always given");
            for (int i = 1; i <= 4; i++)
            {
                b.F("RewardItem" + i, $"Item {i}", FieldKind.Ref, "An item every player gets on completion.", "Shown as 'You will receive'.", refKind: "item");
                b.F("RewardAmount" + i, $"Item {i} count", FieldKind.Int, "How many of that item.", "Usually 1.");
            }
            b.Group("Pick one of");
            for (int i = 1; i <= 6; i++)
            {
                b.F("RewardChoiceItemID" + i, $"Choice {i}", FieldKind.Ref, "One of the items the player chooses between.", "Offer one option per role (tank, healer, damage) where you can.", refKind: "item");
                b.F("RewardChoiceItemQuantity" + i, $"Choice {i} count", FieldKind.Int, "How many of that choice.", "Usually 1.");
            }
            b.Group("Spells & titles");
            b.F("RewardSpell", "Spell cast on completion", FieldKind.Ref, "A spell cast on the player when they finish (e.g. teaching a skill).", "0 = none.", refKind: "spell");
            b.F("RewardDisplaySpell", "Spell shown as reward", FieldKind.Ref, "A spell displayed in the reward panel.", "Usually the same as the spell cast.", refKind: "spell");
            b.F("RewardTitle", "Title", FieldKind.Ref, "A title granted on completion.", "0 = none.", refKind: "title");
            b.Group("Reputation");
            for (int i = 1; i <= 5; i++)
            {
                b.F("RewardFactionID" + i, $"Faction {i}", FieldKind.Ref, "A faction whose reputation changes on completion.", "Most quests reward the faction of the quest giver.", refKind: "faction");
                b.F("RewardFactionValue" + i, $"Faction {i} tier", FieldKind.Int, "Reputation tier (1-9) from the standard reputation table; negative loses reputation.", "Use the override below for an exact amount.");
                b.F("RewardFactionOverride" + i, $"Faction {i} exact amount", FieldKind.Int, "Exact reputation points x100 (e.g. 25000 = 250 rep). Overrides the tier when non-zero.", "Leave 0 to use the tier.");
            }
            b.Group("Mail", A);
            b.F("RewardMailTemplateID", "Follow-up mail", FieldKind.Int, "A mail_template sent to the player after completion.", "Great for 'thank you' letters with an item attached.");
            b.F("RewardMailDelay", "Mail delay (seconds)", FieldKind.Int, "How long after completion the mail arrives.", "86400 = one day.");

            b.Tab("Texts").Group("While the quest is unfinished", "quest_request_items");
            b.F("CompletionText", "What the NPC says when you return", FieldKind.LongText, "Shown when the player talks to the quest ender before handing in (the 'Do you have the items?' page). $N = name, $C = class, $R = race, $B = new line.", "Keep it short; the player is about to hand in.");
            b.F("EmoteOnComplete", "Emote if everything's done", FieldKind.Enum, "Animation the NPC plays on that page when the objectives are complete.", "Cheer or Bow fit a job well done.", Emote);
            b.F("EmoteOnIncomplete", "Emote if it isn't done", FieldKind.Enum, "Animation played when objectives are still missing.", "Question or Shake no fit here.", Emote);
            b.Group("When the quest is handed in", "quest_offer_reward");
            b.F("RewardText", "Thank-you text", FieldKind.LongText, "What the quest ender says on the reward page. $N = name, $C = class, $R = race, $B = new line.", "This is the payoff of the story: make it count.");
            for (int i = 1; i <= 4; i++)
            {
                b.F("Emote" + i, $"Emote {i}", FieldKind.Enum, "An animation the NPC plays on the reward page.", "Several emotes play one after another.", Emote);
                b.F("EmoteDelay" + i, $"Emote {i} delay (ms)", FieldKind.Int, "Milliseconds before that emote starts.", "Space them about a second apart.");
            }

            b.Tab("Chain").Group("Quest chain");
            b.F("RewardNextQuest", "Offer next right away", FieldKind.Ref, "When this quest is handed in, the same NPC immediately offers this follow-up.", "Use for story chains that continue at the same NPC.", refKind: "quest");
            b.Group("Quest chain", A);
            b.F("PrevQuestID", "Requires quest", FieldKind.Ref, "A quest that must be done before this one appears (negative = must be active, not done).", "This is how chains lock the next step.", refKind: "quest");
            b.F("NextQuestID", "Unlocks quest", FieldKind.Ref, "A quest this one unlocks.", "Usually set on one side of the chain only.", refKind: "quest");
            b.F("ExclusiveGroup", "Exclusive group", FieldKind.Int, "Quests sharing a positive group: doing one hides the others. Negative: all must be done.", "Used for 'choose one path' story branches.");
            b.F("BreadcrumbForQuestId", "Breadcrumb for", FieldKind.Ref, "Marks this as a 'go talk to X' lead-in for another quest.", "The breadcrumb disappears once the target quest is taken.", refKind: "quest");
            return e;
        }

        /// <summary>SQL that turns gameobject_template.type into its Studio name (for search lists).</summary>
        static string GoTypeSql() => "ELT(type + 1, " + string.Join(", ", Enumerable.Range(0, 36).Select(i => SqlGen.Literal(GameObjectData.TypeName(i)))) + ")";

        public const int GameObjectDataSlots = 24;

        static EntityDef BuildGameObject()
        {
            var e = new EntityDef
            {
                Key = "gameobjects", Title = "Game objects", Singular = "game object", Table = "gameobject_template", KeyCol = "entry", NameCol = "name", Glyph = "",
                ListExtra = "CONCAT(COALESCE(" + GoTypeSql() + ", CONCAT('type ', type)), IF(size <> 1, CONCAT(' · size ', ROUND(size, 2)), ''))",
                Tabs = new[] { "Basics", "Type data", "Spawns", "Loot", "Extras" },
                TypeColumn = "type",
            };
            e.SideTables["gameobject_template_addon"] = "entry";
            const string A = "gameobject_template_addon";
            var b = new B(e);
            b.Tab("Basics").Group("Identity");
            b.F("name", "Name", FieldKind.Text, "The name shown when the mouse is over the object, and in quest objectives.", "Plain object names read best: Iron Chest, Wanted Poster, Mailbox.");
            b.F("type", "Object type", FieldKind.Enum, "What kind of object this is. The type decides what Data0-Data23 mean, so the Type data tab changes when you change it.", "Changing the type of an existing object also changes the meaning of every data value; check the Type data tab afterwards.", GameObjectData.Types);
            b.F("displayId", "Model", FieldKind.Int, "Which 3D model it uses (GameObjectDisplayInfo ID).", "Borrow the model number of an existing object with the look you want.");
            b.F("size", "Size", FieldKind.Float, "Scale of the model. 1 = normal size.", "Large values also make the click area bigger.");
            b.F("IconName", "Mouse cursor", FieldKind.Text, "Cursor shown over it, e.g. Point, Interact, Quest, PickLock, Gunner.", "Leave empty for the default hand.");
            b.F("castBarCaption", "Cast bar text", FieldKind.Text, "Text on the cast bar while a player uses it (e.g. Opening, Gathering).", "Empty = the default for the type.");
            b.Group("Scripts");
            b.F("AIName", "AI type", FieldKind.Text, "SmartGameObjectAI makes it run smart_scripts rows (source type 1). Empty = no AI.", "Use the SmartAI builder to write the behaviour.");
            b.F("ScriptName", "C++ script", FieldKind.Text, "Name of a C++ script compiled into the server.", "Only set this to a script that exists in your build.");

            b.Tab("Type data").Group("Type data");
            for (int i = 0; i < GameObjectDataSlots; i++)
                b.F("Data" + i, "Data " + i, FieldKind.Int, "A setting whose meaning depends on the object type. Studio labels it for the type picked on the Basics tab.", "If a slot isn't used by the type, leave it at 0.");

            b.Tab("Extras").Group("Faction, flags and money", A);
            b.F("faction", "Faction", FieldKind.Ref, "Who can use it (FactionTemplate ID). 0 = everyone.", "Battleground flags and banners use a faction so only one side can click them.", refKind: "factiontemplate");
            b.F("flags", "Object flags", FieldKind.Flags, "Special states, such as locked or can't be selected.", "'Needs a quest or condition' hides the sparkle until the player has the quest.", bits: GameObjectFlags);
            b.F("mingold", "Least money inside", FieldKind.Money, "For chests: smallest amount of copper inside.", "Leave both at 0 for no coins.");
            b.F("maxgold", "Most money inside", FieldKind.Money, "For chests: largest amount of copper inside.", "Keep it in line with creatures of the same level.");
            return e;
        }

        static EntityDef BuildNpcText()
        {
            var e = new EntityDef
            {
                Key = "npctext", Title = "NPC text", Singular = "NPC text", Table = "npc_text", KeyCol = "ID", NameCol = "text0_0",
                ListExtra = "CONCAT(IF(COALESCE(text1_0, '') <> '' OR COALESCE(text1_1, '') <> '', 'several variants', 'one variant'), IF(BroadcastTextID0 <> 0, CONCAT(' · broadcast text ', BroadcastTextID0), ''))",
                Tabs = new[] { "Variants 1-4", "Variants 5-8", "Used by" },
            };
            var b = new B(e);
            for (int v = 0; v < 8; v++)
            {
                b.Tab(v < 4 ? "Variants 1-4" : "Variants 5-8").Group($"Variant {v + 1}");
                b.F($"text{v}_0", "What the NPC says", FieldKind.LongText, "The greeting in the gossip window. When several variants are filled in, the server picks one at random using their chances. $N = player name, $C = class, $R = race, $B = new line.", "Short paragraphs read best; the window is narrow.");
                b.F($"text{v}_1", "Female version", FieldKind.LongText, "Shown instead of the first text to female characters (e.g. 'sister' instead of 'brother'). Leave empty to show the same text to everyone.", "Only fill this in when the wording really differs.");
                b.F($"Probability{v}", "Chance", FieldKind.Float, "How likely this variant is picked, as a weight compared with the other filled-in variants: two variants with 50 and 50 (or 1 and 1) are each picked half the time.", "Only the proportions matter; many existing texts use percentages that add up to 100.");
                b.F($"lang{v}", "Language", FieldKind.Enum, "Language the text is spoken in. Players who don't know it see gibberish.", "Universal is right for gossip windows.", Language);
                b.F($"BroadcastTextID{v}", "Broadcast text", FieldKind.Int, "BroadcastText ID that holds the official localized text. When set, the client may show that text instead.", "Leave 0 for your own new texts.");
                for (int k = 0; k < 3; k++)
                {
                    b.F($"em{v}_{k * 2 + 1}", $"Emote {k + 1}", FieldKind.Enum, "An animation the NPC plays while the window opens.", "Talk, Bow, Wave and Point suit most greetings.", Emote);
                    b.F($"em{v}_{k * 2}", $"Emote {k + 1} delay (ms)", FieldKind.Int, "Milliseconds to wait before playing that emote.", "Stagger several emotes by a second or two.");
                }
            }
            return e;
        }

        static EntityDef BuildBroadcastText()
        {
            var e = new EntityDef
            {
                Key = "broadcast", Title = "Broadcast text", Singular = "broadcast text", Table = "broadcast_text", KeyCol = "ID", NameCol = "MaleText",
                ListExtra = "CONCAT('language ', LanguageID, IF(SoundEntriesId <> 0, CONCAT(' · sound ', SoundEntriesId), ''))",
                Tabs = new[] { "Text", "Used by" },
            };
            var b = new B(e);
            b.Tab("Text").Group("Text");
            b.F("MaleText", "Text", FieldKind.LongText, "The line itself (used by creature_text, gossip and quests that point at this broadcast text). $N = player name, $B = new line.", "Your own new lines usually go straight into creature_text or npc_text instead; use this when something asks for a BroadcastTextID.");
            b.F("FemaleText", "Female version", FieldKind.LongText, "Shown instead to female characters. Empty = same text for everyone.", "Only fill it in when the wording differs.");
            b.F("LanguageID", "Language", FieldKind.Enum, "Language it's spoken in.", "Universal is understood by everyone.", Language);
            b.Group("Sound and emotes");
            b.F("SoundEntriesId", "Sound", FieldKind.Int, "SoundEntries ID played with the line (e.g. a voice-over). 0 = silent.", "Asset import › Sound gives you an ID for your own recordings.");
            for (int i = 1; i <= 3; i++)
            {
                b.F("EmoteID" + i, $"Emote {i}", FieldKind.Enum, "An animation played with the line.", "Talk suits most lines.", Emote);
                b.F("EmoteDelay" + i, $"Emote {i} delay (ms)", FieldKind.Int, "Milliseconds before that emote.", "Space emotes about a second apart.");
            }
            b.F("Flags", "Flags", FieldKind.Int, "Client display flags. 0 for normal lines.", "Leave at 0.");
            return e;
        }

        static EntityDef BuildGameTele()
        {
            var e = new EntityDef
            {
                Key = "tele", Title = "Teleport locations", Singular = "teleport location", Table = "game_tele", KeyCol = "id", NameCol = "name",
                ListExtra = "CONCAT('map ', map, ' · ', ROUND(position_x), ', ', ROUND(position_y), ', ', ROUND(position_z))",
                Tabs = new[] { "Location" },
            };
            var b = new B(e);
            b.Tab("Location").Group("Where .tele takes you");
            b.F("name", "Name", FieldKind.Text, "What GMs type after .tele (e.g. .tele Stormwind). No spaces.", "Short, memorable names work best.");
            b.F("map", "Map", FieldKind.Ref, "The map (continent or instance).", "0 Eastern Kingdoms, 1 Kalimdor, 530 Outland, 571 Northrend.", refKind: "map");
            b.F("position_x", "X", FieldKind.Float, "Position on the map. Stand on the spot in game and type .gps to read it.", "Copy all four values from .gps.");
            b.F("position_y", "Y", FieldKind.Float, "Position on the map (.gps).", "Copy all four values from .gps.");
            b.F("position_z", "Z (height)", FieldKind.Float, "Height. Too low and players fall through the ground.", "Add 1 to the .gps value to be safe.");
            b.F("orientation", "Facing", FieldKind.Float, "Which way the player faces, in radians (0 = north, 3.14 = south).", "Copy it from .gps.");
            return e;
        }

        static EntityDef BuildPageText()
        {
            var e = new EntityDef
            {
                Key = "pagetext", Title = "Page text", Singular = "page", Table = "page_text", KeyCol = "ID", NameCol = "Text",
                ListExtra = "IF(NextPageID <> 0, CONCAT('continues on page ', NextPageID), 'last page')",
                Tabs = new[] { "Page", "Used by" },
            };
            var b = new B(e);
            b.Tab("Page").Group("Page");
            b.F("Text", "Text on the page", FieldKind.LongText, "What the player reads on this page of a book, letter, plaque or sign. $N = player name, $B = new line.", "About 600 characters fit on one page; continue on a next page for more.");
            b.F("NextPageID", "Next page", FieldKind.Ref, "The page shown when the player clicks the arrow. 0 = this is the last page.", "Chains of pages are how books work: page 1 → page 2 → page 3.", refKind: "pagetext");
            return e;
        }
    }
}
