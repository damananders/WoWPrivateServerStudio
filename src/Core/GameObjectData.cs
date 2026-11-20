using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AndersCraftStudio.Core
{
    /// <summary>
    /// What gameobject_template's Data0..Data23 mean for each object type (3.3.5a). The slot order follows the server's
    /// own layout; every label and explanation is written by Studio.
    /// </summary>
    public static class GameObjectData
    {
        public sealed class Slot
        {
            public int Index;
            public string Label, Guide, RefKind;
            public bool IsBool;
        }

        public static readonly Dictionary<long, string> Types = new Dictionary<long, string>
        {
            [0] = "Door", [1] = "Button / lever", [2] = "Quest giver", [3] = "Chest (lootable)", [4] = "Binder", [5] = "Generic (decoration)",
            [6] = "Trap", [7] = "Chair", [8] = "Spell focus (anvil, forge…)", [9] = "Readable text (book, sign)", [10] = "Goober (click to use)",
            [11] = "Transport (elevator)", [12] = "Area damage", [13] = "Camera (cinematic)", [14] = "Map object", [15] = "Moving transport (ship, zeppelin)",
            [16] = "Duel flag", [17] = "Fishing bobber", [18] = "Summoning ritual", [19] = "Mailbox", [20] = "(unused type 20)", [21] = "Guard post",
            [22] = "Spellcaster (portal, stone)", [23] = "Meeting stone", [24] = "Flag stand (battleground)", [25] = "Fishing pool", [26] = "Dropped flag",
            [27] = "Mini-game", [28] = "(unused type 28)", [29] = "Capture point", [30] = "Aura generator", [31] = "Dungeon difficulty", [32] = "Barber chair",
            [33] = "Destructible building", [34] = "Guild bank", [35] = "Trapdoor",
        };

        // type|index|label|guide|refkind ("b" = yes/no switch). Studio's own words.
        static readonly string[] Lines =
        {
            "0|0|Starts open|1 = the door is open when the server starts.|b",
            "0|1|Lock|Lock.dbc ID a key or skill must open. 0 = no lock.|",
            "0|2|Closes again after (ms)|Milliseconds before it shuts by itself. 0 = stays open.|",
            "0|3|Can be damaged|0 = immune to damage, 1 = siege weapons can hurt it.|b",
            "0|4|Text when opened|Broadcast text shown when it opens. 0 = none.|",
            "0|5|Text when closed|Broadcast text shown when it closes. 0 = none.|",
            "0|6|Ignored by pathfinding|1 = creatures path through it as if it weren't there.|b",

            "1|0|Starts pressed|1 = already in its used state at server start.|b",
            "1|1|Lock|Lock.dbc ID needed to use it. 0 = anyone can press it.|",
            "1|2|Resets after (ms)|Milliseconds before it pops back up. 0 = never.|",
            "1|3|Linked trap|A trap game object (entry) set off when it's pressed.|gameobject",
            "1|4|Can be damaged|1 = it can take damage.|b",
            "1|5|Large object|1 = visible from further away.|b",
            "1|6|Text when used|Broadcast text shown when used.|",
            "1|7|Text when reset|Broadcast text shown when it resets.|",
            "1|8|Needs line of sight|1 = the player must see it to use it.|b",

            "2|0|Lock|Lock.dbc ID needed to open it. 0 = none.|",
            "2|1|Quest list|Quest list ID (old field; quests usually come from gameobject_queststarter).|",
            "2|2|Page material|PageTextMaterial ID for the letter background.|",
            "2|3|Gossip menu|gossip_menu ID shown when clicked.|",
            "2|4|Custom animation|Animation played when used.|",
            "2|5|Can be damaged|1 = it can take damage.|b",
            "2|6|Text when used|Broadcast text shown when used.|",
            "2|7|Needs line of sight|1 = the player must see it to use it.|b",
            "2|8|Usable while mounted|1 = works without dismounting.|b",
            "2|9|Large object|1 = visible from further away.|b",

            "3|0|Lock|Lock.dbc ID (lockpicking, herbalism, mining, a key…). 0 = anyone can open it.|",
            "3|1|Loot table|gameobject_loot_template entry: what's inside. See the Loot tab.|",
            "3|2|Refills after (s)|Seconds before it can be looted again. 0 = when the object respawns.|",
            "3|3|Used up when looted|1 = disappears once looted (herbs, ore, quest crates).|b",
            "3|4|Fewest opens|For herbs and veins: the least number of times it can be gathered.|",
            "3|5|Most opens|For herbs and veins: the most times it can be gathered.|",
            "3|6|Event when opened|event_scripts ID run when it's opened.|",
            "3|7|Linked trap|A trap game object (entry) set off when it's opened.|gameobject",
            "3|8|Quest needed|Only lootable while this quest is active.|quest",
            "3|9|Minimum level|Players below this level can't open it.|",
            "3|10|Needs line of sight|1 = the player must see it.|b",
            "3|11|Stays after looting|1 = the chest stays after loot is taken.|b",
            "3|12|Not in combat|1 = can't be opened while in combat.|b",
            "3|13|Log the loot|1 = the server logs who looted what.|b",
            "3|14|Text when opened|Broadcast text shown when opened.|",
            "3|15|Group loot rules|1 = loot is shared with group loot rules (round robin, need/greed).|b",
            "3|16|Floating tooltip|1 = a floating tooltip like quest objects.|b",

            "5|0|Floating tooltip|1 = shows a floating tooltip.|b",
            "5|1|Highlight|1 = glows when the mouse is over it.|b",
            "5|2|Server only|1 = never sent to players (invisible helper).|b",
            "5|3|Large object|1 = visible from further away.|b",
            "5|4|Floats on water|1 = bobs on the water surface.|b",
            "5|5|Quest needed|Only shown while this quest is active.|quest",

            "6|0|Lock|Lock.dbc ID needed to disarm it.|",
            "6|1|Level|Caster level of the trap's spell.|",
            "6|2|Trigger radius|Yards: how close someone must come to set it off.|",
            "6|3|Spell|The spell the trap casts.|spell",
            "6|4|Charges|0 = no charges, 1 = goes away after one use, 2 = one use per cooldown.|",
            "6|5|Cooldown (s)|Seconds between triggers.|",
            "6|6|Despawn after (ms)|Milliseconds until it disappears. 0 = stays.|",
            "6|7|Arm delay (s)|Seconds after spawning before it can go off.|",
            "6|8|Server only|1 = never sent to players.|b",
            "6|9|Stealthed|1 = hidden like a hunter trap.|b",
            "6|10|Large object|1 = visible from further away.|b",
            "6|11|Invisible|1 = completely invisible.|b",
            "6|12|Text when triggered|Broadcast text when it goes off.|",
            "6|13|Text when reset|Broadcast text when it resets.|",
            "6|14|Ignores totems|1 = totems can't set it off.|b",

            "7|0|Seats|How many characters can sit on it.|",
            "7|1|Seat height|0 low, 1 medium, 2 high (how far the character drops).|",
            "7|2|Only its creator|1 = only whoever summoned it may sit.|b",
            "7|3|Event when used|event_scripts ID run when someone sits.|",

            "8|0|Focus type|SpellFocusObject.dbc ID (anvil, forge, cooking fire…) that spells look for.|",
            "8|1|Range (yards)|How close a player must be for the focus to count.|",
            "8|2|Linked trap|A trap game object (entry) linked to it.|gameobject",
            "8|3|Server only|1 = never sent to players.|b",
            "8|4|Quest needed|Only counts while this quest is active.|quest",
            "8|5|Large object|1 = visible from further away.|b",
            "8|6|Floating tooltip|1 = shows a floating tooltip.|b",

            "9|0|Page|The page_text shown when it's read (first page of a chain).|pagetext",
            "9|1|Language|Language ID the text is written in.|",
            "9|2|Page material|PageTextMaterial ID for the paper look.|",
            "9|3|Usable while mounted|1 = works without dismounting.|b",

            "10|0|Lock|Lock.dbc ID needed to use it. 0 = none.|",
            "10|1|Quest needed|Only usable while this quest is active.|quest",
            "10|2|Event when used|event_scripts ID run when it's used.|",
            "10|3|Resets after (ms)|Milliseconds before it can be used again.|",
            "10|4|Custom animation|Animation played when used.|",
            "10|5|Used up|1 = disappears after one use.|b",
            "10|6|Cooldown (s)|Seconds between uses.|",
            "10|7|Page|page_text shown when used (for readable goobers).|pagetext",
            "10|8|Language|Language ID of the page.|",
            "10|9|Page material|PageTextMaterial ID.|",
            "10|10|Spell|Spell cast on the user.|spell",
            "10|11|Can be damaged|1 = it can take damage.|b",
            "10|12|Linked trap|A trap game object (entry) set off when it's used.|gameobject",
            "10|13|Large object|1 = visible from further away.|b",
            "10|14|Text when used|Broadcast text shown when used.|",
            "10|15|Text when reset|Broadcast text shown when it resets.|",
            "10|16|Needs line of sight|1 = the player must see it.|b",
            "10|17|Usable while mounted|1 = works without dismounting.|b",
            "10|18|Floating tooltip|1 = shows a floating tooltip.|b",
            "10|19|Gossip menu|gossip_menu ID shown when clicked.|",
            "10|20|World state|1 = using it sets a world state.|b",

            "11|0|Pause at (ms)|Time in its path where it pauses.|",
            "11|1|Starts open|1 = starts at the open position.|b",
            "11|2|Closes after (ms)|Milliseconds before it returns.|",
            "11|3|Event at stop 1|event_scripts ID at the first pause.|",
            "11|4|Event at stop 2|event_scripts ID at the second pause.|",

            "12|0|Lock|Lock.dbc ID.|",
            "12|1|Radius|Yards around it that take damage.|",
            "12|2|Least damage|Smallest damage per hit.|",
            "12|3|Most damage|Largest damage per hit.|",
            "12|4|Damage school|0 physical, 1 holy, 2 fire, 3 nature, 4 frost, 5 shadow, 6 arcane.|",
            "12|5|Stops after (ms)|Milliseconds until it stops hurting.|",
            "12|6|Text when started|Broadcast text when it starts.|",
            "12|7|Text when stopped|Broadcast text when it stops.|",

            "13|0|Lock|Lock.dbc ID.|",
            "13|1|Cinematic|CinematicSequences.dbc ID played to the user.|",
            "13|2|Event when used|event_scripts ID.|",
            "13|3|Text when used|Broadcast text when used.|",

            "15|0|Taxi path|TaxiPath.dbc ID the ship or zeppelin follows.|",
            "15|1|Speed|Movement speed.|",
            "15|2|Acceleration|How fast it speeds up.|",
            "15|3|Event at start|event_scripts ID when it departs.|",
            "15|4|Event at stop|event_scripts ID when it arrives.|",
            "15|5|Transport physics|TransportPhysics.dbc ID (how it rocks).|",
            "15|6|Map|Which map the transport belongs to.|map",
            "15|7|World state|World state ID tied to it.|",
            "15|8|Can be stopped|1 = scripts can stop it.|b",

            "18|0|People needed|How many players must help finish the ritual.|",
            "18|1|Spell|Spell cast when the ritual completes.|spell",
            "18|2|Helper animation spell|Spell the helpers channel while clicking.|spell",
            "18|3|Stays after use|1 = the portal stays after the ritual.|b",
            "18|4|Caster target spell|Spell cast on the summoner's target.|spell",
            "18|5|Caster target count|How many targets that spell hits.|",
            "18|6|Same group only|1 = only group members can help.|b",
            "18|7|No target check|1 = skips the target check.|b",

            "21|0|Creature|Creature summoned to guard the spot.|creature",
            "21|1|Charges|How many guards it can summon.|",

            "22|0|Spell|Spell cast on whoever clicks it (portals, stones).|spell",
            "22|1|Charges|Uses before it's gone. -1 = unlimited, 0 = one.|",
            "22|2|Party only|1 = only the owner's party can use it.|b",
            "22|3|Usable while mounted|1 = works without dismounting.|b",
            "22|4|Large object|1 = visible from further away.|b",

            "23|0|Lowest level|Minimum level to use the meeting stone.|",
            "23|1|Highest level|Maximum level to use it.|",
            "23|2|Area|AreaTable ID of the dungeon it summons to.|area",

            "24|0|Lock|Lock.dbc ID.|",
            "24|1|Pick-up spell|Spell cast when the flag is taken.|spell",
            "24|2|Radius|Yards for taking the flag.|",
            "24|3|Carrier aura|Aura on the player while carrying the flag.|spell",
            "24|4|Return spell|Spell cast when the flag returns.|spell",
            "24|5|Can be damaged|1 = it can take damage.|b",
            "24|6|Text when taken|Broadcast text when picked up.|",
            "24|7|Needs line of sight|1 = the player must see it.|b",

            "25|0|Radius|Yards of the fishing pool.|",
            "25|1|Loot table|gameobject_loot_template entry: the fish. See the Loot tab.|",
            "25|2|Fewest catches|Least number of catches before it disappears.|",
            "25|3|Most catches|Most catches before it disappears.|",
            "25|4|Lock|Lock.dbc ID (fishing skill).|",

            "26|0|Lock|Lock.dbc ID.|",
            "26|1|Event when taken|event_scripts ID.|",
            "26|2|Pick-up spell|Spell cast when picked up.|spell",
            "26|3|Can be damaged|1 = it can take damage.|b",
            "26|4|Text when taken|Broadcast text when picked up.|",

            "27|0|Game type|Which mini-game it starts.|",

            "29|0|Radius|Yards that count as standing on the point.|",
            "29|1|Spell|Spell on players inside.|spell",
            "29|2|World state 1|World state shown on the HUD.|",
            "29|3|World state 2|Second world state (progress bar).|",
            "29|4|Event: Alliance wins|event_scripts ID.|",
            "29|5|Event: Horde wins|event_scripts ID.|",
            "29|6|Event: Alliance contests|event_scripts ID.|",
            "29|7|Event: Horde contests|event_scripts ID.|",
            "29|8|Event: Alliance progress|event_scripts ID.|",
            "29|9|Event: Horde progress|event_scripts ID.|",
            "29|10|Event: neutral from Alliance|event_scripts ID.|",
            "29|11|Event: neutral from Horde|event_scripts ID.|",
            "29|12|Neutral zone (%)|Width of the neutral band on the bar.|",
            "29|13|World state 3|Third world state.|",
            "29|14|Least advantage|Fewest extra players needed to move the bar.|",
            "29|15|Most advantage|Most extra players that still speed it up.|",
            "29|16|Fastest capture (s)|Seconds with the most advantage.|",
            "29|17|Slowest capture (s)|Seconds with the least advantage.|",
            "29|18|Large object|1 = visible from further away.|b",
            "29|19|Highlight|1 = glows on mouse-over.|b",
            "29|20|Starting value|Where the bar starts (0-100).|",
            "29|21|One direction only|1 = the bar can only move one way.|b",

            "30|0|Starts active|1 = the aura is on at server start.|b",
            "30|1|Radius|Yards the aura reaches.|",
            "30|2|Aura 1|First aura applied nearby.|spell",
            "30|3|Condition 1|Condition for aura 1.|",
            "30|4|Aura 2|Second aura.|spell",
            "30|5|Condition 2|Condition for aura 2.|",
            "30|6|Server only|1 = never sent to players.|b",

            "31|0|Map|The dungeon map it belongs to.|map",
            "31|1|Difficulty|0 normal, 1 heroic (dungeon) / 25-player (raid)…|",

            "32|0|Chair height|How high the barber chair seat is.|",
            "32|1|Height offset|Fine height adjustment.|",

            "33|0|Hits while intact|Hits it takes before it's damaged.|",
            "33|1|Credit creature|Creature that gets kill credit when it's destroyed.|creature",
            "33|2|Intact name|State name while intact.|",
            "33|3|Event: intact|event_scripts ID when intact.|",
            "33|4|Damaged look|GameObjectDisplayInfo ID while damaged.|",
            "33|5|Hits while damaged|Hits it takes before it's destroyed.|",
            "33|9|Event: damaged|event_scripts ID when damaged.|",
            "33|10|Destroyed look|GameObjectDisplayInfo ID when destroyed.|",
            "33|14|Event: destroyed|event_scripts ID when destroyed.|",
            "33|16|Rebuild time (s)|Seconds before it rebuilds.|",
            "33|18|Destructible data|DestructibleModelData.dbc ID.|",
            "33|19|Event: rebuilding|event_scripts ID when rebuilding starts.|",
            "33|22|Event: damage taken|event_scripts ID each time it's hit.|",

            "35|0|When to pause|0 = pause when open, 1 = pause when closed.|",
            "35|1|Starts open|1 = open at server start.|b",
            "35|2|Closes after (ms)|Milliseconds before it closes again.|",
        };

        static Dictionary<long, Dictionary<int, Slot>> map;

        static Dictionary<long, Dictionary<int, Slot>> Map()
        {
            if (map != null) return map;
            var m = new Dictionary<long, Dictionary<int, Slot>>();
            foreach (var line in Lines)
            {
                var p = line.Split('|');
                long t = long.Parse(p[0], CultureInfo.InvariantCulture);
                if (!m.TryGetValue(t, out var d)) m[t] = d = new Dictionary<int, Slot>();
                var idx = int.Parse(p[1], CultureInfo.InvariantCulture);
                d[idx] = new Slot { Index = idx, Label = p[2], Guide = p[3], IsBool = p[4] == "b", RefKind = p[4] == "b" || p[4] == "" ? null : p[4] };
            }
            return map = m;
        }

        /// <summary>What Data<paramref name="index"/> means for an object of this type, or null when the type doesn't use it.</summary>
        public static Slot For(long type, int index) => Map().TryGetValue(type, out var d) && d.TryGetValue(index, out var s) ? s : null;

        /// <summary>All slots a type uses, in order.</summary>
        public static IEnumerable<Slot> SlotsOf(long type) => Map().TryGetValue(type, out var d) ? d.Values.OrderBy(s => s.Index) : Enumerable.Empty<Slot>();

        /// <summary>Which data slot holds the loot table for this type (chests, fishing pools), or -1.</summary>
        public static int LootSlot(long type) => type == 3 || type == 25 ? 1 : -1;

        /// <summary>Which data slot holds a page_text ID for this type (readable objects), or -1.</summary>
        public static int PageSlot(long type) => type == 9 ? 0 : type == 10 ? 7 : -1;

        public static string TypeName(long type) => Types.TryGetValue(type, out var n) ? n : "type " + type;

        /// <summary>
        /// The form field for a DataN column as an object of this type uses it (label, guide, control kind), or null when the
        /// type doesn't use that slot. Other fields come back unchanged.
        /// </summary>
        public static FieldDef Relabel(FieldDef f, long type)
        {
            if (f == null || !f.Column.StartsWith("Data", StringComparison.Ordinal) || !int.TryParse(f.Column.Substring(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx)) return f;
            var s = For(type, idx);
            if (s == null) return null;
            return new FieldDef
            {
                Tab = f.Tab, Group = TypeName(type), Table = f.Table, Column = f.Column, Label = s.Label, Guide = s.Guide,
                Tip = s.IsBool ? "A yes/no switch stored as 1 or 0." : s.RefKind != null ? "Use Pick… to find it by name." : "Leave it at 0 to use the default.",
                Kind = s.IsBool ? FieldKind.Bool : s.RefKind != null ? FieldKind.Ref : FieldKind.Int, RefKind = s.RefKind,
            };
        }
    }
}
