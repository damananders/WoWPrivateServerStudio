using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AndersCraftStudio.Core
{
    /// <summary>How a SmartAI parameter is shown and checked.</summary>
    public enum SP { Int, Ms, Pct, Spell, Creature, GameObject, Quest, Item, Emote, Faction, Bool, PhaseMask, TextGroup, Yards, Sound, Script, Map, Flags, Float }

    public sealed class SParam
    {
        public string Label, Hint; public SP Kind;
        public SParam(string label, SP kind = SP.Int, string hint = null) { Label = label; Kind = kind; Hint = hint; }
    }

    public sealed class SType
    {
        public int Id; public string Name, Desc, Sentence; public SParam[] P;
        public bool Obsolete;
        public override string ToString() => $"{Id} · {Name}";
    }

    /// <summary>One smart_scripts row.</summary>
    public sealed class SmartRow
    {
        public long EntryOrGuid; public int SourceType, Id, Link, EventType, EventChance = 100, EventPhaseMask, EventFlags, ActionType, TargetType;
        public long[] EventParams = new long[6], ActionParams = new long[6], TargetParams = new long[4];
        public float X, Y, Z, O;
        public string Comment = "";
        public SmartRow Clone()
        {
            var r = (SmartRow)MemberwiseClone();
            r.EventParams = (long[])EventParams.Clone(); r.ActionParams = (long[])ActionParams.Clone(); r.TargetParams = (long[])TargetParams.Clone();
            return r;
        }

        public static readonly string[] Columns =
        {
            "entryorguid", "source_type", "id", "link", "event_type", "event_phase_mask", "event_chance", "event_flags",
            "event_param1", "event_param2", "event_param3", "event_param4", "event_param5", "event_param6",
            "action_type", "action_param1", "action_param2", "action_param3", "action_param4", "action_param5", "action_param6",
            "target_type", "target_param1", "target_param2", "target_param3", "target_param4", "target_x", "target_y", "target_z", "target_o", "comment",
        };
        public static readonly string[] KeyColumns = { "entryorguid", "source_type", "id", "link" };

        public RowValues ToValues()
        {
            var v = new RowValues();
            string L(long x) => x.ToString(CultureInfo.InvariantCulture);
            string F(float x) => SqlGen.Literal(x);
            v.Set("entryorguid", L(EntryOrGuid)).Set("source_type", L(SourceType)).Set("id", L(Id)).Set("link", L(Link))
             .Set("event_type", L(EventType)).Set("event_phase_mask", L(EventPhaseMask)).Set("event_chance", L(EventChance)).Set("event_flags", L(EventFlags));
            for (int i = 0; i < 6; i++) v.Set("event_param" + (i + 1), L(EventParams[i]));
            v.Set("action_type", L(ActionType));
            for (int i = 0; i < 6; i++) v.Set("action_param" + (i + 1), L(ActionParams[i]));
            v.Set("target_type", L(TargetType));
            for (int i = 0; i < 4; i++) v.Set("target_param" + (i + 1), L(TargetParams[i]));
            v.Set("target_x", F(X)).Set("target_y", F(Y)).Set("target_z", F(Z)).Set("target_o", F(O));
            v.Set("comment", SqlGen.Literal(Comment ?? ""), Comment ?? "");
            return v;
        }

        public static SmartRow FromValues(Func<string, object> get)
        {
            long N(string c) { var o = get(c); return o == null || o is DBNull ? 0 : Convert.ToInt64(o, CultureInfo.InvariantCulture); }
            float Fl(string c) { var o = get(c); return o == null || o is DBNull ? 0 : Convert.ToSingle(o, CultureInfo.InvariantCulture); }
            var r = new SmartRow
            {
                EntryOrGuid = N("entryorguid"), SourceType = (int)N("source_type"), Id = (int)N("id"), Link = (int)N("link"),
                EventType = (int)N("event_type"), EventPhaseMask = (int)N("event_phase_mask"), EventChance = (int)N("event_chance"), EventFlags = (int)N("event_flags"),
                ActionType = (int)N("action_type"), TargetType = (int)N("target_type"), X = Fl("target_x"), Y = Fl("target_y"), Z = Fl("target_z"), O = Fl("target_o"),
                Comment = Convert.ToString(get("comment") ?? ""),
            };
            for (int i = 0; i < 6; i++) { r.EventParams[i] = N("event_param" + (i + 1)); r.ActionParams[i] = N("action_param" + (i + 1)); }
            for (int i = 0; i < 4; i++) r.TargetParams[i] = N("target_param" + (i + 1));
            return r;
        }
    }

    /// <summary>
    /// What every SmartAI event, action and target type means, in Studio's own words (built from the AzerothCore source:
    /// SmartScriptMgr.h), with labelled parameters and a sentence template per type.
    /// Template tokens: {1}..{6} a parameter, {r12} / {r34} a millisecond range from two parameters.
    /// </summary>
    public static class SmartAI
    {
        public static readonly Dictionary<int, string> SourceTypes = new Dictionary<int, string>
        { [0] = "Creature", [1] = "Game object", [2] = "Area trigger", [9] = "Action list (timed)" };

        public static readonly Dictionary<ulong, string> EventFlagBits = new Dictionary<ulong, string>
        {
            [0x001] = "Not repeatable (fires once)", [0x002] = "Only in normal / 10-player", [0x004] = "Only in heroic / 25-player",
            [0x008] = "Only in 10-player heroic", [0x010] = "Only in 25-player heroic", [0x080] = "Debug builds only",
            [0x100] = "Keep running state on reset", [0x200] = "Also while charmed",
        };

        public static readonly Dictionary<ulong, string> CastFlagBits = new Dictionary<ulong, string>
        {
            [0x001] = "Interrupt current cast", [0x002] = "Triggered (instant, free)", [0x020] = "Only if target lacks the aura",
            [0x040] = "Stop moving while casting", [0x080] = "Only with 2+ enemies", [0x100] = "Only on mana users",
            [0x200] = "Move closer if out of sight", [0x400] = "Main spell (sets chase range)",
        };

        static SParam P(string l, SP k = SP.Int, string h = null) => new SParam(l, k, h);
        static readonly SParam IMin = P("First after (min ms)", SP.Ms, "Delay before the first time, lower bound"), IMax = P("First after (max ms)", SP.Ms, "Delay before the first time, upper bound"),
            RMin = P("Then every (min ms)", SP.Ms, "Time between repeats, lower bound; 0 = don't repeat"), RMax = P("Then every (max ms)", SP.Ms, "Time between repeats, upper bound"),
            CMin = P("Cooldown (min ms)", SP.Ms, "Ignore new triggers for at least this long"), CMax = P("Cooldown (max ms)", SP.Ms, "…and at most this long");

        static readonly List<SType> events = new List<SType>();
        static readonly List<SType> actions = new List<SType>();
        static readonly List<SType> targets = new List<SType>();
        public static IReadOnlyList<SType> Events => events;
        public static IReadOnlyList<SType> Actions => actions;
        public static IReadOnlyList<SType> Targets => targets;

        static void E(int id, string name, string desc, string sentence, params SParam[] p) => events.Add(new SType { Id = id, Name = name, Desc = desc, Sentence = sentence, P = p });
        static void A(int id, string name, string desc, string sentence, params SParam[] p) => actions.Add(new SType { Id = id, Name = name, Desc = desc, Sentence = sentence, P = p });
        static void T(int id, string name, string desc, string sentence, params SParam[] p) => targets.Add(new SType { Id = id, Name = name, Desc = desc, Sentence = sentence, P = p });

        public static SType Event(int id) => events.FirstOrDefault(e => e.Id == id);
        public static SType Action(int id) => actions.FirstOrDefault(e => e.Id == id);
        public static SType Target(int id) => targets.FirstOrDefault(e => e.Id == id);

        static SmartAI()
        {
            // ------------------------------------------------------------------ events
            E(0, "In combat timer", "Repeats on a timer while the creature is fighting.", "in combat|every {r34} (first after {r12})", IMin, IMax, RMin, RMax);
            E(1, "Out of combat timer", "Repeats on a timer while the creature is not fighting.", "out of combat|every {r34} (first after {r12})", IMin, IMax, RMin, RMax);
            E(2, "Health between", "Fires while its own health is inside a percentage band.", "its health is between {1}% and {2}%|re-checked every {r34}", P("Health from %", SP.Pct), P("Health to %", SP.Pct), RMin, RMax);
            E(3, "Mana between", "Fires while its own mana is inside a percentage band.", "its mana is between {1}% and {2}%|re-checked every {r34}", P("Mana from %", SP.Pct), P("Mana to %", SP.Pct), RMin, RMax);
            E(4, "Aggro", "Fires once when the creature enters combat.", "it enters combat", Array.Empty<SParam>());
            E(5, "Kills a unit", "Fires when the creature kills something.", "it kills {4:creature|any unit}{3:bool| (players only)}", CMin, CMax, P("Players only", SP.Bool), P("Only this creature", SP.Creature, "0 = any"));
            E(6, "Death", "Fires when the creature dies.", "it dies", Array.Empty<SParam>());
            E(7, "Evade", "Fires when the creature gives up the fight and runs home.", "it evades", Array.Empty<SParam>());
            E(8, "Hit by spell", "Fires when a spell (or any spell of a school) lands on the creature.", "it is hit by {1:spell|any spell}", P("Spell", SP.Spell, "0 = any spell"), P("School mask", SP.Int, "1 physical, 2 holy, 4 fire, 8 nature, 16 frost, 32 shadow, 64 arcane; 0 = any"), CMin, CMax);
            E(9, "Target in range", "Fires while the current target is within a distance band.", "its target is {5}-{6} yards away|re-checked every {r34}", IMin, IMax, RMin, RMax, P("Min distance", SP.Yards), P("Max distance", SP.Yards));
            E(10, "Sees someone (out of combat)", "Fires when a unit comes into line of sight while not fighting.", "out of combat it sees a {1:hostility} unit within {2} yards", P("Who (0 hostile, 1 not hostile, 2 any)", SP.Int), P("Max distance", SP.Yards), CMin, CMax, P("Players only", SP.Bool));
            E(11, "Respawn", "Fires when the creature respawns.", "it respawns", P("Limit (0 any, 1 map, 2 zone)"), P("Map", SP.Map), P("Zone"));
            E(12, "Target health between", "Fires while the target's health is inside a percentage band.", "its target's health is between {1}% and {2}%|re-checked every {r34}", P("Health from %", SP.Pct), P("Health to %", SP.Pct), RMin, RMax);
            E(13, "Target is casting", "Fires when the current target starts casting (useful for interrupts).", "its target is casting {3:spell|a spell}|re-checked every {r12}", RMin, RMax, P("Only this spell", SP.Spell, "0 = any"));
            E(14, "Friend is hurt", "Fires when a friendly unit nearby is missing at least this much health.", "a friend within {2} yards is missing {1} health|re-checked every {r34}", P("Health missing (points)"), P("Radius", SP.Yards), RMin, RMax);
            E(15, "Friend is crowd-controlled", "Fires when a friendly unit nearby is stunned, feared and so on.", "a friend within {1} yards is crowd-controlled|re-checked every {r23}", P("Radius", SP.Yards), RMin, RMax);
            E(16, "Friend misses a buff", "Fires when a friendly unit nearby lacks an aura.", "a friend within {2} yards lacks {1:spell}|re-checked every {r34}", P("Buff", SP.Spell), P("Radius", SP.Yards), RMin, RMax, P("Only in combat", SP.Bool));
            E(17, "Summoned a unit", "Fires when the creature summons something.", "it summons {1:creature|a unit}", P("Creature", SP.Creature, "0 = any"), CMin, CMax);
            E(18, "Target mana between", "Fires while the target's mana is inside a percentage band.", "its target's mana is between {1}% and {2}%|re-checked every {r34}", P("Mana from %", SP.Pct), P("Mana to %", SP.Pct), RMin, RMax);
            E(19, "Quest accepted from it", "Fires when a player accepts a quest from this NPC.", "a player accepts {1:quest|any quest} from it", P("Quest", SP.Quest, "0 = any"), CMin, CMax);
            E(20, "Quest handed in to it", "Fires when a player turns in a quest to this NPC.", "a player hands in {1:quest|any quest}", P("Quest", SP.Quest, "0 = any"), CMin, CMax);
            E(21, "Reached home", "Fires when the creature arrives back at its spawn point after evading.", "it gets back home", Array.Empty<SParam>());
            E(22, "Receives an emote", "Fires when a player does an emote at the creature (/wave, /kiss...).", "a player does emote {1} at it", P("Text emote ID", SP.Emote), CMin, CMax);
            E(23, "Has aura", "Fires while the creature has a number of stacks of an aura.", "it has {2} stack(s) of {1:spell}|re-checked every {r34}", P("Aura", SP.Spell), P("Stacks"), RMin, RMax);
            E(24, "Target has aura", "Fires while the target has a number of stacks of an aura.", "its target has {2} stack(s) of {1:spell}|re-checked every {r34}", P("Aura", SP.Spell), P("Stacks"), RMin, RMax);
            E(25, "Reset", "Fires when the creature resets: after combat, on spawn and respawn.", "it resets", Array.Empty<SParam>());
            E(26, "Sees someone (in combat)", "Fires when a unit comes into line of sight while fighting.", "in combat it sees a {1:hostility} unit within {2} yards", P("Who (0 hostile, 1 not hostile, 2 any)", SP.Int), P("Max distance", SP.Yards), CMin, CMax, P("Players only", SP.Bool));
            E(27, "Passenger boards", "Fires when someone gets into this vehicle.", "a passenger gets in", CMin, CMax);
            E(28, "Passenger leaves", "Fires when someone gets out of this vehicle.", "a passenger gets out", CMin, CMax);
            E(29, "Charmed", "Fires when the creature is mind-controlled, or freed again.", "it is {1:bool|charmed by|released from} control", P("On release instead", SP.Bool));
            E(30, "Charmed target", "Fires for the target of a charm.", "it becomes the target of a charm", Array.Empty<SParam>());
            E(31, "Its spell hits", "Fires when a spell cast by the creature lands on a target.", "its {1:spell|spell} hits a target", P("Spell", SP.Spell, "0 = any"), P("School mask"), CMin, CMax);
            E(32, "Takes damage", "Fires when the creature takes an amount of damage in one hit.", "it takes {1}-{2} damage", P("Min damage"), P("Max damage"), CMin, CMax, P("Health % mode", SP.Pct, "If set: fires once when health drops below this %"));
            E(33, "Deals damage", "Fires when the creature deals an amount of damage in one hit.", "it deals {1}-{2} damage", P("Min damage"), P("Max damage"), CMin, CMax);
            E(34, "Movement point reached", "Fires when a scripted movement reaches a point.", "it reaches movement point {2}", P("Movement type (0 any)"), P("Point ID"), P("Path ID (0 any)"));
            E(35, "Summon despawned", "Fires when one of its summons despawns.", "its summon {1:creature|} despawns", P("Creature", SP.Creature, "0 = any"), CMin, CMax);
            E(36, "Corpse removed", "Fires when its corpse disappears.", "its corpse is removed", Array.Empty<SParam>());
            E(37, "AI starts", "Fires once when the AI is created (very early, before spawning is complete).", "its AI starts up", Array.Empty<SParam>());
            E(38, "Data set", "Fires when another script sets a data value on it (see action Set data).", "data {1} is set to {2}", P("Data field"), P("Value"), CMin, CMax);
            E(39, "Escort started", "Fires when an escort path starts.", "its escort starts", P("Point (0 any)"), P("Path (0 any)"));
            E(40, "Escort point reached", "Fires when an escort reaches a waypoint.", "it reaches escort point {1:int|any point}", P("Point (0 any)"), P("Path (0 any)"));
            E(41, "Player boards transport", "Transport only: a player boards.", "a player boards the transport", Array.Empty<SParam>());
            E(42, "Creature boards transport", "Transport only: a creature boards.", "creature {1:creature|} boards the transport", P("Creature", SP.Creature, "0 = any"));
            E(43, "Player leaves transport", "Transport only: a player leaves.", "a player leaves the transport", Array.Empty<SParam>());
            E(44, "Transport arrives", "Transport only: it reaches a point.", "the transport reaches point {1}", P("Point"));
            E(45, "Player enters instance", "Instance script only: a player enters.", "a player enters the instance", P("Team (0 any)"), CMin, CMax);
            E(46, "Area trigger entered", "Area-trigger script only: someone steps in.", "a player enters area trigger {1:int|}", P("Trigger (0 any)"));
            E(47, "Quest accepted", "Quest script only: the quest is accepted.", "the quest is accepted", Array.Empty<SParam>());
            E(48, "Quest objective done", "Quest script only: an objective completes.", "a quest objective completes", Array.Empty<SParam>());
            E(49, "Quest completed", "Quest script only: all objectives are done.", "the quest is completed", Array.Empty<SParam>());
            E(50, "Quest rewarded", "Quest script only: the reward is handed out.", "the quest is rewarded", Array.Empty<SParam>());
            E(51, "Quest failed", "Quest script only: the quest fails.", "the quest fails", Array.Empty<SParam>());
            E(52, "Finished talking", "Fires when a line from creature_text has finished (its duration has passed).", "line group {1} of {2:creature|itself} has been said", P("Text group", SP.TextGroup), P("Speaker", SP.Creature, "0 = any / itself"));
            E(53, "Receives healing", "Fires when the creature is healed for an amount.", "it is healed for {1}-{2}", P("Min heal"), P("Max heal"), CMin, CMax);
            E(54, "Just summoned", "Fires right after this creature has been summoned.", "it has just been summoned", Array.Empty<SParam>());
            E(55, "Escort paused", "Fires when the escort pauses.", "its escort pauses", P("Point (0 any)"), P("Path (0 any)"));
            E(56, "Escort resumed", "Fires when the escort continues.", "its escort resumes", P("Point (0 any)"), P("Path (0 any)"));
            E(57, "Escort stopped", "Fires when the escort is stopped.", "its escort is stopped", P("Point (0 any)"), P("Path (0 any)"));
            E(58, "Escort ended", "Fires when the escort path is finished.", "its escort ends", P("Point (0 any)"), P("Path (0 any)"));
            E(59, "Timed event", "Fires when a timer created with action Create timed event runs out.", "timed event {1} goes off", P("Timed event ID"));
            E(60, "Timer (always)", "Repeats on a timer whether or not the creature is fighting.", "the timer ticks|every {r34} (first after {r12})", IMin, IMax, RMin, RMax);
            E(61, "Linked", "Runs straight after the row that links to this one. Use it to do several things at once.", "the linked row runs", Array.Empty<SParam>());
            E(62, "Gossip option chosen", "Fires when a player picks an option in its chat menu.", "a player picks option {2} in menu {1}", P("Menu ID"), P("Option ID"));
            E(63, "Created", "Fires once when the object is first created.", "it is created", Array.Empty<SParam>());
            E(64, "Gossip opened", "Fires when a player opens its chat window (or uses the object).", "a player talks to it", P("Filter (0 both, 1 talk only, 2 use only)"));
            E(65, "Follow done", "Fires when a follow (action Follow) reaches its end.", "it finishes following", Array.Empty<SParam>());
            E(66, "Phase changed", "Fires when the script's event phase changes to one in the mask.", "its event phase changes", P("Phase mask", SP.PhaseMask));
            E(67, "Behind target", "Fires while it stands behind its target.", "it is behind its target|re-checked every {r34}", IMin, IMax, RMin, RMax, P("Min distance", SP.Yards), P("Max distance", SP.Yards));
            E(68, "World event starts", "Fires when a game event (holiday) starts.", "game event {1} starts", P("Game event ID"));
            E(69, "World event ends", "Fires when a game event (holiday) ends.", "game event {1} ends", P("Game event ID"));
            E(70, "Object state changed", "Game objects: the door/button state changes.", "its state becomes {1}", P("State (0 active, 1 ready, 2 alternative)"));
            E(71, "Object event", "Game objects: a spell or script sends an event ID.", "it receives event {1}", P("Event ID"));
            E(72, "Action done", "Fires when another script reports an action ID as done.", "action {1} is reported done", P("Action ID"));
            E(73, "Clicked (spellclick)", "Fires when a player clicks it to use it (spellclick).", "a player clicks it", Array.Empty<SParam>());
            E(74, "Friend below health %", "Fires when a friendly unit nearby is under a health percentage.", "a friend within {6} yards is below {5}% health|re-checked every {r34}", IMin, IMax, RMin, RMax, P("Health %", SP.Pct), P("Radius", SP.Yards));
            E(75, "Near a creature", "Fires when a specific creature comes within a distance.", "creature {2:creature|a creature} is within {3} yards|re-checked every {4} ms", P("Creature spawn GUID"), P("Creature", SP.Creature), P("Distance", SP.Yards), P("Repeat (ms)", SP.Ms));
            E(76, "Near an object", "Fires when a specific game object comes within a distance.", "object {2:gameobject|an object} is within {3} yards|re-checked every {4} ms", P("Object spawn GUID"), P("Object", SP.GameObject), P("Distance", SP.Yards), P("Repeat (ms)", SP.Ms));
            E(77, "Counter set", "Fires when a counter (action Set counter) reaches a value.", "counter {1} reaches {2}", P("Counter ID"), P("Value"), CMin, CMax);
            E(82, "Summon dies", "Fires when one of its summons dies.", "its summon {1:creature|} dies", P("Creature", SP.Creature, "0 = any"), CMin, CMax);
            E(101, "Players nearby (at least)", "Fires when at least N players are within a radius.", "at least {1} player(s) are within {2} yards|every {r45} (first after {3} ms)", P("Players"), P("Radius", SP.Yards), P("First check (ms)", SP.Ms), RMin, RMax);
            E(102, "Players nearby (fewer than)", "Fires when fewer than N players are within a radius.", "fewer than {1} player(s) are within {2} yards|every {r45} (first after {3} ms)", P("Players"), P("Radius", SP.Yards), P("First check (ms)", SP.Ms), RMin, RMax);
            E(103, "Units nearby (at least)", "Fires when at least N creatures/objects of an entry are near.", "at least {3} of {2} are within {4} yards", P("Type (0 creature, 1 object)"), P("Entry"), P("Count"), P("Radius", SP.Yards), P("Repeat (ms)", SP.Ms));
            E(104, "Units nearby (fewer than)", "Fires when fewer than N creatures/objects of an entry are near.", "fewer than {3} of {2} are within {4} yards", P("Type (0 creature, 1 object)"), P("Entry"), P("Count"), P("Radius", SP.Yards), P("Repeat (ms)", SP.Ms));
            E(105, "Enemy casting nearby", "Fires when an enemy within range is casting.", "an enemy {5}-{6} yards away is casting|every {r34}", IMin, IMax, RMin, RMax, P("Min distance", SP.Yards), P("Max distance", SP.Yards));
            E(106, "Enemy in range", "Fires when any enemy is within a distance band.", "an enemy is {5}-{6} yards away|every {r34}", IMin, IMax, RMin, RMax, P("Min distance", SP.Yards), P("Max distance", SP.Yards));
            E(107, "Summon evades", "Fires when one of its summons evades.", "its summon {1:creature|} evades", P("Creature", SP.Creature, "0 = any"), CMin, CMax);
            E(108, "Waypoint reached", "Fires when it reaches a waypoint of a path (action Start waypoint path).", "it reaches waypoint {1:int|any point}", P("Point (0 any)"), P("Path (0 any)"));
            E(109, "Waypoint path ended", "Fires when a waypoint path is finished.", "it finishes its waypoint path", P("Point (0 any)"), P("Path (0 any)"));
            E(110, "Target in melee range", "Fires while the target is (or isn't) in melee range.", "its target is {6:bool|in|out of} melee range|every {r34}", IMin, IMax, RMin, RMax, P("Extra distance", SP.Yards), P("Invert (out of range)", SP.Bool));
            foreach (var (id, n) in new[] { (78, "Scene start"), (79, "Scene trigger"), (80, "Scene cancel"), (81, "Scene complete") })
                events.Add(new SType { Id = id, Name = n + " (not in 3.3.5a)", Desc = "Exists for newer clients; does nothing on 3.3.5a.", Sentence = "a scene event happens", P = Array.Empty<SParam>(), Obsolete = true });

            // ------------------------------------------------------------------ actions
            A(0, "Do nothing", "No action. Useful as a placeholder or to only trigger links.", "do nothing", Array.Empty<SParam>());
            A(1, "Say a line", "Says a random line from a creature_text group (say, yell, emote or whisper as set there).", "say line group {1}", P("Text group", SP.TextGroup), P("Then fire 'Finished talking' after (ms)", SP.Ms), P("Use target as invoker", SP.Bool), P("Speak to the target", SP.Bool));
            A(2, "Change faction", "Changes its faction (who it's friendly or hostile to). 0 puts back the original.", "change faction to {1:faction|its original}", P("Faction template", SP.Faction, "0 = original"));
            A(3, "Change look", "Turns into another creature's model, or a model ID. Both 0 = original look.", "change its look to {1:creature|model {2}}", P("Look like creature", SP.Creature), P("Or model ID"));
            A(4, "Play sound", "Plays a sound.", "play sound {1}", P("Sound ID", SP.Sound), P("Only for the target", SP.Bool), P("Distance mode"));
            A(5, "Play emote", "Plays an animation once (wave, bow, cheer...).", "play emote {1}", P("Emote ID", SP.Emote));
            A(6, "Fail quest", "Makes the target's quest fail.", "fail quest {1:quest}", P("Quest", SP.Quest));
            A(7, "Offer quest", "Opens a quest offer for the target (or adds it straight to the log).", "offer quest {1:quest}", P("Quest", SP.Quest), P("Add directly", SP.Bool));
            A(8, "Set react state", "Sets how it reacts to enemies: 0 passive, 1 defensive, 2 aggressive.", "become {1:react}", P("State (0 passive, 1 defensive, 2 aggressive)"));
            A(9, "Activate object", "Activates the target game object (open a door, use a button).", "activate", Array.Empty<SParam>());
            A(10, "Random emote", "Plays one of several emotes at random.", "play a random emote", P("Emote 1", SP.Emote), P("Emote 2", SP.Emote), P("Emote 3", SP.Emote), P("Emote 4", SP.Emote), P("Emote 5", SP.Emote), P("Emote 6", SP.Emote));
            A(11, "Cast spell", "Casts a spell on the chosen target.", "cast {1:spell}", P("Spell", SP.Spell), P("Cast flags", SP.Flags, "Interrupt, triggered, only if aura missing..."), P("Triggered flags"), P("Max targets (0 all)"));
            A(12, "Summon creature", "Summons a creature at the target's position.", "summon {1:creature}", P("Creature", SP.Creature), P("Summon type (1-8)", SP.Int, "How long it stays: 1 timed or dead, 3 timed despawn, 4 timed out of combat, 6 corpse timed, 8 manual..."), P("Duration (ms)", SP.Ms), P("Attack the invoker", SP.Bool), P("Attack the script owner", SP.Bool), P("Flags"));
            A(13, "Change threat (single)", "Changes threat on the target by a percentage.", "change threat by {1}%", P("Threat % (+/-)"), P("Negative %"));
            A(14, "Change threat (all)", "Changes threat on everyone by a percentage.", "change everyone's threat by {1}%", P("Threat % (+/-)"), P("Negative %"));
            A(15, "Quest: explored", "Completes an exploration/event objective for the target.", "complete the exploration objective of {1:quest}", P("Quest", SP.Quest));
            A(17, "Set emote state", "Loops an emote (work, sit, dance...) until changed. 0 stops.", "keep playing emote {1}", P("Emote ID", SP.Emote));
            A(18, "Add unit flags", "Sets unit flags (not attackable, not selectable...).", "add unit flags {1}", P("Flags", SP.Flags), P("Flag field"));
            A(19, "Remove unit flags", "Clears unit flags.", "remove unit flags {1}", P("Flags", SP.Flags), P("Flag field"));
            A(20, "Auto attack", "Turns melee auto-attack on or off.", "{1:bool|stop|start} auto-attacking", P("Attack", SP.Bool));
            A(21, "Combat movement", "Allows or stops moving to chase the target (casters that stand still).", "{1:bool|stop|allow} moving in combat", P("Allow", SP.Bool));
            A(22, "Set phase", "Sets the script's event phase (1-12; 0 = none). Rows with a phase mask only run in their phases.", "switch to phase {1}", P("Phase"));
            A(23, "Change phase by", "Adds to (or subtracts from) the current phase.", "move the phase by {1}", P("Add"), P("Subtract"));
            A(24, "Evade", "Leaves combat and runs home.", "evade", P("Go to respawn position", SP.Bool));
            A(25, "Flee for help", "Runs away to find help.", "flee for help", P("With emote", SP.Bool));
            A(26, "Quest: group event", "Completes an event objective for the target's whole group.", "complete the group objective of {1:quest}", P("Quest", SP.Quest));
            A(27, "Stop combat", "Stops fighting without evading.", "stop fighting", Array.Empty<SParam>());
            A(28, "Remove aura", "Removes an aura from the target (0 = all auras).", "remove {1:spell|all auras}", P("Aura", SP.Spell, "0 = all"), P("Charges"));
            A(29, "Follow", "Follows the target at a distance and angle.", "follow at {1} yards", P("Distance", SP.Yards), P("Angle"), P("End at creature", SP.Creature), P("Credit"), P("Credit type (0 kill, 1 event)"), P("Dead may count", SP.Bool));
            A(30, "Random phase", "Switches to one of several phases at random.", "switch to a random phase", P("Phase 1"), P("Phase 2"), P("Phase 3"), P("Phase 4"), P("Phase 5"), P("Phase 6"));
            A(31, "Random phase range", "Switches to a random phase in a range.", "switch to a phase between {1} and {2}", P("From phase"), P("To phase"));
            A(32, "Reset object", "Resets the target game object.", "reset", Array.Empty<SParam>());
            A(33, "Kill credit", "Gives the target kill credit for a creature (quest objectives).", "give kill credit for {1:creature}", P("Creature", SP.Creature));
            A(34, "Set instance data", "Sets an instance data field (instance scripts).", "set instance data {1} to {2}", P("Field"), P("Value"), P("Type (0 data, 1 boss state)"));
            A(35, "Set instance data (GUID)", "Stores the target's GUID in an instance field.", "store the target in instance field {1}", P("Field"));
            A(36, "Change template", "Turns into another creature template.", "become {1:creature}", P("Creature", SP.Creature), P("Update level", SP.Bool));
            A(37, "Die", "Kills itself.", "die", P("Delay (ms)", SP.Ms));
            A(38, "Aggro the zone", "Puts every player in range into combat with it (boss pull).", "pull everyone in the zone into combat", P("Range (outside dungeons)", SP.Yards));
            A(39, "Call for help", "Calls nearby friends to join the fight.", "call for help within {1} yards", P("Radius", SP.Yards), P("With emote", SP.Bool));
            A(40, "Sheath weapons", "0 unarmed, 1 melee drawn, 2 ranged drawn.", "set weapons to {1}", P("Sheath (0 none, 1 melee, 2 ranged)"));
            A(41, "Despawn", "Removes the target after a delay.", "despawn after {1} ms", P("Delay (ms)", SP.Ms), P("Respawn after (s)"));
            A(42, "Can't drop below", "Makes it unkillable below a health value (positive % or negative flat).", "not drop below {1}% health", P("Health (+%/-flat)"));
            A(43, "Mount", "Mounts on a creature's model or a model ID; both 0 dismounts.", "mount {1:creature|model {2}}", P("Mount creature", SP.Creature), P("Or model ID"));
            A(44, "Set phase mask (world)", "Moves it to other world phases (what players can see).", "set its world phase mask to {1}", P("Phase mask"));
            A(45, "Set data", "Sets a data value on the target; its script reacts via event Data set.", "set data {1} = {2} on", P("Field"), P("Value"));
            A(46, "Move forward", "Moves forward a distance.", "move forward {1} yards", P("Distance", SP.Yards));
            A(47, "Visible", "Shows or hides it.", "{1:bool|hide|show} itself", P("Visible", SP.Bool));
            A(48, "Keep active", "Keeps its grid loaded and running even with no players near.", "{1:bool|stop staying|stay} active", P("Active", SP.Bool));
            A(49, "Attack", "Starts attacking the target.", "attack", Array.Empty<SParam>());
            A(50, "Summon object", "Summons a game object at the target's position.", "summon object {1:gameobject}", P("Game object", SP.GameObject), P("Despawn after (s)"), P("At the target", SP.Bool), P("Summon type"));
            A(51, "Kill", "Kills the target outright.", "kill", Array.Empty<SParam>());
            A(52, "Taxi flight", "Sends the target on a flight path.", "fly along taxi path {1}", P("Taxi path ID"));
            A(53, "Start escort", "Starts walking an escort path (waypoints table).", "start escort path {2}", P("Run", SP.Bool), P("Path ID"), P("Repeat", SP.Bool), P("Quest", SP.Quest), P("Despawn after (ms)", SP.Ms), P("React state"));
            A(54, "Pause escort", "Pauses the escort for a while.", "pause the escort for {1} ms", P("Time (ms)", SP.Ms));
            A(55, "Stop escort", "Stops the escort.", "stop the escort", P("Despawn after (ms)", SP.Ms), P("Quest", SP.Quest), P("Fail the quest", SP.Bool));
            A(56, "Give item", "Gives the target items.", "give {2}x {1:item}", P("Item", SP.Item), P("Count"));
            A(57, "Take item", "Removes items from the target.", "take {2}x {1:item}", P("Item", SP.Item), P("Count"));
            A(58, "Use AI template", "Installs a built-in behaviour (caster, turret, passive...).", "use AI template {1}", P("Template (1 basic, 2 caster, 3 turret, 4 passive, 5 cage, 6 cage NPC)"), P("Param 2"), P("Param 3"), P("Param 4"), P("Param 5"), P("Param 6"));
            A(59, "Run / walk", "Switches between running and walking.", "{1:bool|walk|run}", P("Run", SP.Bool));
            A(60, "Fly", "Turns flying on or off.", "{1:bool|land|fly}", P("Fly", SP.Bool));
            A(61, "Swim", "Turns swimming on or off.", "{1:bool|stop swimming|swim}", P("Swim", SP.Bool));
            A(62, "Teleport", "Teleports the target to a map and the row's x, y, z, o.", "teleport to map {1:map} at the given position", P("Map", SP.Map));
            A(63, "Set counter", "Adds to or sets a counter (fires event Counter set).", "set counter {1} to {2}", P("Counter ID"), P("Value"), P("Reset first", SP.Bool));
            A(64, "Remember targets", "Stores the chosen targets under an ID (target type Stored uses them).", "remember the targets as #{1}", P("Storage ID"));
            A(65, "Resume escort", "Continues a paused escort.", "resume the escort", Array.Empty<SParam>());
            A(66, "Face", "Turns to face the target (or home orientation).", "turn to face", P("Quick (no animation)", SP.Bool), P("Random", SP.Bool), P("Turn angle"));
            A(67, "Create timed event", "Creates a timer that fires event Timed event.", "start timed event {1} after {r23}", P("Timed event ID"), IMin, IMax, RMin, RMax, P("Chance %", SP.Pct));
            A(68, "Play movie", "Plays a cinematic movie for the target.", "play movie {1}", P("Movie ID"));
            A(69, "Move to position", "Moves to the target's position (or the row's x, y, z).", "move to the position", P("Point ID"), P("On transport", SP.Bool), P("Controlled", SP.Bool), P("Stop distance", SP.Yards));
            A(70, "Respawn", "Respawns the target.", "respawn", P("Force", SP.Bool), P("Object respawn time (s)"));
            A(71, "Equip", "Changes what it holds (equipment set or explicit items).", "equip set {1}", P("Equipment set"), P("Slot mask (1 main, 2 off, 4 ranged)"), P("Main hand", SP.Item), P("Off hand", SP.Item), P("Ranged", SP.Item));
            A(72, "Close gossip", "Closes the chat window.", "close the chat window", Array.Empty<SParam>());
            A(73, "Fire timed event", "Fires a timed event right away.", "fire timed event {1} now", P("Timed event ID"));
            A(74, "Remove timed event", "Cancels a timed event.", "cancel timed event {1}", P("Timed event ID"));
            A(75, "Add aura", "Puts an aura on the target without casting.", "apply {1:spell}", P("Aura", SP.Spell));
            A(76, "Act as another object", "Lets this script control the target object (advanced; can crash if misused).", "take over", Array.Empty<SParam>());
            A(77, "Stop acting as another", "Undoes 'Act as another object'.", "go back to itself", Array.Empty<SParam>());
            A(78, "Reset script", "Resets the script (fires event Reset).", "reset its script", Array.Empty<SParam>());
            A(79, "Ranged distance", "Sets how far away it stays when fighting at range.", "keep {1} yards away when fighting", P("Distance", SP.Yards), P("Angle"));
            A(80, "Run action list", "Starts a timed action list (source type 9 rows with this ID).", "run action list {1:script}", P("Action list ID", SP.Script), P("Stop after combat", SP.Bool), P("Timer (0 out of combat, 1 in combat, 2 always)"));
            A(81, "Set NPC flags", "Replaces its NPC flags (vendor, quest giver...).", "set NPC flags to {1}", P("NPC flags", SP.Flags));
            A(82, "Add NPC flags", "Adds NPC flags.", "add NPC flags {1}", P("NPC flags", SP.Flags));
            A(83, "Remove NPC flags", "Removes NPC flags.", "remove NPC flags {1}", P("NPC flags", SP.Flags));
            A(84, "Make target say", "Makes the target (even a player) say a creature_text line.", "make it say line group {1}", P("Text group", SP.TextGroup));
            A(85, "Make target cast on itself", "The target casts the spell on itself.", "make it cast {1:spell} on itself", P("Spell", SP.Spell), P("Cast flags", SP.Flags), P("Triggered flags"), P("Max targets"));
            A(86, "Cross cast", "Other units (caster target) cast the spell on the chosen targets.", "have others cast {1:spell}", P("Spell", SP.Spell), P("Cast flags", SP.Flags), P("Caster target type"), P("Caster param 1"), P("Caster param 2"), P("Caster param 3"));
            A(87, "Random action list", "Runs one of several action lists at random.", "run a random action list", P("List 1", SP.Script), P("List 2", SP.Script), P("List 3", SP.Script), P("List 4", SP.Script), P("List 5", SP.Script), P("List 6", SP.Script));
            A(88, "Random action list in range", "Runs a random action list between two IDs.", "run a random action list from {1} to {2}", P("From list", SP.Script), P("To list", SP.Script));
            A(89, "Wander", "Wanders randomly within a distance.", "wander within {1} yards", P("Distance", SP.Yards));
            A(90, "Set stand state bytes", "Sets stand state (sit, sleep, kneel...).", "set stand state {1}", P("Value"), P("Field"));
            A(91, "Clear stand state bytes", "Clears a stand state.", "clear stand state {1}", P("Value"), P("Field"));
            A(92, "Interrupt spell", "Interrupts a spell being cast.", "interrupt {2:spell|its cast}", P("Include delayed", SP.Bool), P("Spell", SP.Spell), P("Instant too", SP.Bool));
            A(93, "Object animation", "Plays a custom animation on a game object.", "play object animation {1}", P("Animation ID"));
            A(94, "Set dynamic flags", "Replaces dynamic flags (lootable, tapped...).", "set dynamic flags {1}", P("Flags", SP.Flags));
            A(95, "Add dynamic flags", "Adds dynamic flags.", "add dynamic flags {1}", P("Flags", SP.Flags));
            A(96, "Remove dynamic flags", "Removes dynamic flags.", "remove dynamic flags {1}", P("Flags", SP.Flags));
            A(97, "Jump", "Jumps to the target's position (or the row's x, y, z).", "jump to the position", P("Horizontal speed"), P("Vertical speed"), P("Jump in place", SP.Bool));
            A(98, "Send gossip menu", "Shows a gossip menu to the target.", "show menu {1}", P("Menu ID"), P("Text ID"));
            A(99, "Object loot state", "Sets a game object's loot state.", "set loot state {1}", P("State"));
            A(100, "Send targets", "Passes stored targets to another creature's script.", "send stored targets #{1}", P("Storage ID"));
            A(101, "Set home position", "Sets where it returns to after evading.", "set its home here", P("Use spawn position", SP.Bool));
            A(102, "Health regeneration", "Turns health regeneration on or off.", "{1:bool|stop|start} regenerating health", P("Regenerate", SP.Bool));
            A(103, "Root", "Roots it in place, or frees it.", "{1:bool|unroot|root} itself", P("Rooted", SP.Bool));
            A(104, "Set object flags", "Replaces game object flags.", "set object flags {1}", P("Flags", SP.Flags));
            A(105, "Add object flags", "Adds game object flags.", "add object flags {1}", P("Flags", SP.Flags));
            A(106, "Remove object flags", "Removes game object flags.", "remove object flags {1}", P("Flags", SP.Flags));
            A(107, "Summon creature group", "Summons a group from creature_summon_groups.", "summon creature group {1}", P("Group ID"), P("Attack the invoker", SP.Bool), P("Attack the script owner", SP.Bool));
            A(108, "Set power", "Sets a power (mana, rage...) to a value.", "set power {1} to {2}", P("Power type (0 mana, 1 rage, 3 energy)"), P("Value"));
            A(109, "Add power", "Adds to a power.", "add {2} to power {1}", P("Power type"), P("Value"));
            A(110, "Remove power", "Takes from a power.", "take {2} from power {1}", P("Power type"), P("Value"));
            A(111, "Stop world event", "Stops a game event.", "stop game event {1}", P("Game event ID"));
            A(112, "Start world event", "Starts a game event.", "start game event {1}", P("Game event ID"));
            A(113, "Walk to nearest waypoint", "Starts the closest of up to six waypoint paths.", "walk the nearest waypoint path", P("Path 1"), P("Path 2"), P("Path 3"), P("Path 4"), P("Path 5"), P("Path 6"));
            A(114, "Rise up", "Rises straight up (e.g. a flying boss taking off).", "rise up {1} yards", P("Distance", SP.Yards));
            A(115, "Random sound", "Plays one of several sounds.", "play a random sound", P("Sound 1", SP.Sound), P("Sound 2", SP.Sound), P("Sound 3", SP.Sound), P("Sound 4", SP.Sound), P("Only for target", SP.Bool), P("Distance mode"));
            A(116, "Corpse delay", "Sets how long its corpse stays.", "keep its corpse for {1} s", P("Seconds"));
            A(117, "Disable evade", "Stops it from evading (or allows it again).", "{1:bool|allow|disable} evading", P("Disable", SP.Bool));
            A(118, "Object state", "Sets a game object's state (open/closed).", "set object state {1}", P("State"));
            A(121, "Sight distance", "Sets how far it can see.", "see up to {1} yards", P("Distance", SP.Yards));
            A(122, "Flee", "Runs away in fear for a while.", "flee for {1} ms", P("Time (ms)", SP.Ms));
            A(123, "Add threat", "Adds or removes a flat amount of threat.", "add {1} threat", P("Add"), P("Remove"));
            A(124, "Load equipment", "Loads an equipment set from creature_equip_template.", "load equipment set {1}", P("Set ID"), P("Force", SP.Bool));
            A(125, "Fire random timed event", "Fires a random timed event between two IDs.", "fire a timed event from {1} to {2}", P("From ID"), P("To ID"));
            A(126, "Remove all objects", "Removes all game objects it summoned.", "remove its summoned objects", Array.Empty<SParam>());
            A(131, "Spawn spawn group", "Spawns a spawn group.", "spawn group {1}", P("Group ID"), P("Ignore respawn", SP.Bool), P("Force", SP.Bool));
            A(132, "Despawn spawn group", "Despawns a spawn group.", "despawn group {1}", P("Group ID"), P("Delete respawn times", SP.Bool));
            A(134, "Invoker casts", "The unit that triggered the event casts the spell on the targets.", "have the invoker cast {1:spell}", P("Spell", SP.Spell), P("Cast flags", SP.Flags), P("Triggered flags"), P("Max targets"));
            A(135, "Play cinematic", "Plays an in-game cinematic for the target.", "play cinematic {1}", P("Cinematic ID"));
            A(136, "Movement speed", "Changes a movement speed.", "set speed {1} to {2}.{3}", P("Movement type"), P("Speed (whole)"), P("Speed (fraction)"));
            A(142, "Set health %", "Sets its health to a percentage.", "set health to {1}%", P("Percent", SP.Pct));
            A(201, "Move to target", "Moves to the target's position.", "move to the target (point {1})", P("Point ID"));
            A(203, "Leave vehicle", "Gets out of the vehicle it's in.", "leave its vehicle", Array.Empty<SParam>());
            A(204, "Movement flags", "Sets unit movement flags.", "set movement flags {1}", P("Flags", SP.Flags));
            A(205, "Combat distance", "Sets how close it stays in combat.", "keep {1} yards away in combat", P("Distance", SP.Yards));
            A(206, "Dismount", "Gets off its mount.", "dismount", Array.Empty<SParam>());
            A(207, "Hover", "Turns hovering on or off.", "{1:bool|stop hovering|hover}", P("Hover", SP.Bool));
            A(208, "Add immunity", "Makes it immune to a mechanic, school or effect.", "become immune ({1}/{2})", P("Type"), P("ID"), P("Value"));
            A(209, "Remove immunity", "Removes an immunity.", "lose immunity ({1}/{2})", P("Type"), P("ID"), P("Value"));
            A(210, "Fall", "Falls to the ground.", "fall", Array.Empty<SParam>());
            A(211, "Event flag reset", "Controls whether event flags reset.", "set event flag reset to {1}", P("On", SP.Bool));
            A(212, "Stop motion", "Stops moving.", "stop moving", P("Stop movement", SP.Bool), P("Movement expired", SP.Bool));
            A(213, "No environment update", "Stops environment updates for it.", "ignore environment updates", Array.Empty<SParam>());
            A(214, "Zone under attack", "Sends the 'zone under attack' warning.", "announce that the zone is under attack", Array.Empty<SParam>());
            A(215, "Load grid", "Loads the map grid around it.", "load the grid", Array.Empty<SParam>());
            A(216, "Play music", "Plays music.", "play music {1}", P("Sound ID", SP.Sound), P("Only for target", SP.Bool), P("Type"));
            A(217, "Random music", "Plays one of several music tracks.", "play random music", P("Sound 1", SP.Sound), P("Sound 2", SP.Sound), P("Sound 3", SP.Sound), P("Sound 4", SP.Sound), P("Only for target", SP.Bool), P("Type"));
            A(218, "Cast with custom values", "Casts a spell with custom base points.", "cast {1:spell} with custom values", P("Spell", SP.Spell), P("Cast flags", SP.Flags), P("Value 1"), P("Value 2"), P("Value 3"));
            A(219, "Cone summon", "Summons creatures in a cone pattern.", "summon {1:creature} in a cone", P("Creature", SP.Creature), P("Duration (ms, 0 forever)", SP.Ms), P("Ring spacing"), P("Spacing in a row"), P("Cone length"), P("Cone width (degrees)"));
            A(220, "Player says", "Makes the target player say an acore_string line.", "make the player say string {1}", P("String ID"), P("Yell", SP.Bool));
            A(221, "Vortex summon", "Summons creatures in a spiral.", "summon {1:creature} in a spiral", P("Creature", SP.Creature), P("Duration (ms)", SP.Ms), P("Spiral scale"), P("Spiral look"), P("Max range"), P("Angle step"));
            A(222, "Encounter start", "Resets cooldowns and removes Heroism-type debuffs on the targets.", "start the encounter", Array.Empty<SParam>());
            A(223, "Do action", "Sends an action ID to the target's AI.", "send action {1}", P("Action ID"));
            A(224, "Stop attacking", "Stops attacking.", "stop attacking", Array.Empty<SParam>());
            A(225, "Send GUID", "Sends its (or the invoker's) GUID to the target.", "send its GUID", P("Use invoker", SP.Bool), P("Index"));
            A(226, "Scripted spawn", "Controls a scripted spawn state.", "set scripted spawn state {1}", P("State"), P("Spawn timer min"), P("Spawn timer max"), P("Respawn delay"), P("Corpse delay"), P("Don't despawn", SP.Bool));
            A(227, "Set scale", "Changes its size.", "set its scale to {1}%", P("Scale (%)"));
            A(228, "Summon in a circle", "Summons creatures around itself.", "summon {1:creature} in a circle", P("Creature", SP.Creature), P("Duration (ms)", SP.Ms), P("Count"), P("Start angle"), P("Angle step"), P("Distance", SP.Yards));
            A(229, "Spell visual", "Plays a spell visual.", "play spell visual {1}", P("Visual ID"), P("Impact visual ID"));
            A(230, "Follow group", "Makes summons follow in formation.", "set group follow {1}", P("Follow state"), P("Follow type"), P("Distance", SP.Yards));
            A(231, "Face target", "Turns to face a specific target.", "turn to face (type {1})", P("Type"), P("Target type"), P("Param 1"), P("Param 2"), P("Param 3"), P("Param 4"));
            A(232, "Start waypoint path", "Starts walking a waypoint path.", "walk waypoint path {1}", P("Path ID"), P("Repeat", SP.Bool), P("Path source"));
            A(233, "Random waypoint path", "Walks one of two paths at random.", "walk path {1} or {2}", P("Path 1"), P("Path 2"), P("Repeat", SP.Bool));
            A(234, "Stop movement", "Stops all movement.", "stop all movement", Array.Empty<SParam>());
            A(235, "Pause movement", "Pauses movement for a while.", "pause movement for {1} ms", P("Time (ms)", SP.Ms));
            A(236, "Resume movement", "Resumes movement.", "resume movement", P("Timer override (ms)", SP.Ms));
            A(237, "World script", "Runs a world script event.", "run world script {1}", P("Event ID"), P("Param"));
            A(238, "Disable rewards", "Stops it giving reputation and/or loot.", "disable rewards", P("No reputation", SP.Bool), P("No loot", SP.Bool));
            A(239, "Animation tier", "Sets its animation tier (ground, hover, fly...).", "set animation tier {1}", P("Tier"));
            A(240, "Set gossip menu", "Changes its gossip menu.", "use gossip menu {1}", P("Menu ID"));
            A(241, "Summon object group", "Summons a group of game objects.", "summon object group {1}", P("Group ID"));
            A(242, "Increase data", "Adds to a data value.", "add {2} to data {1}", P("Field"), P("Increment"));
            foreach (var (id, n) in new[] { (16, "Reserved"), (119, "Can fly (unsupported)"), (120, "Remove aura type (unsupported)"), (127, "Remove movement (unsupported)"), (128, "Anim kit (not in 3.3.5a)"), (129, "Scene play (not in 3.3.5a)"), (130, "Scene cancel (not in 3.3.5a)"), (133, "Respawn by spawn ID (unsupported)") })
                actions.Add(new SType { Id = id, Name = n, Desc = "Not usable on this server version.", Sentence = "do nothing (unsupported action)", P = Array.Empty<SParam>(), Obsolete = true });

            // ------------------------------------------------------------------ targets
            T(0, "None", "No target.", "", Array.Empty<SParam>());
            T(1, "Self", "The creature or object itself.", "itself", Array.Empty<SParam>());
            T(2, "Current victim", "Whoever it is fighting (top of its threat list).", "its victim", Array.Empty<SParam>());
            T(3, "Second on threat", "The second-highest on its threat list.", "the second on its threat list", P("Max distance", SP.Yards), P("Players only", SP.Bool), P("Power type + 1"), P("Missing aura", SP.Spell));
            T(4, "Last on threat", "The lowest on its threat list.", "the last on its threat list", P("Max distance", SP.Yards), P("Players only", SP.Bool), P("Power type + 1"), P("Missing aura", SP.Spell));
            T(5, "Random enemy", "Anyone at random from its threat list.", "a random enemy", P("Max distance", SP.Yards), P("Players only", SP.Bool), P("Power type + 1"), P("Missing aura", SP.Spell));
            T(6, "Random enemy (not top)", "Anyone at random except whoever has the most threat.", "a random enemy other than its victim", P("Max distance", SP.Yards), P("Players only", SP.Bool), P("Power type + 1"), P("Missing aura", SP.Spell));
            T(7, "Invoker", "The unit that made this event happen.", "whoever triggered it", Array.Empty<SParam>());
            T(8, "Position", "The x, y, z, o written on this row.", "the given position", Array.Empty<SParam>());
            T(9, "Creatures in range", "Creatures of an entry between two distances.", "{1:creature|creatures} {2}-{3} yards away", P("Creature", SP.Creature, "0 = any"), P("Min distance", SP.Yards), P("Max distance", SP.Yards), P("Alive (0 both, 1 alive, 2 dead)"));
            T(10, "Creature by GUID", "One specific spawned creature.", "spawn #{1} of {2:creature}", P("Spawn GUID"), P("Creature", SP.Creature));
            T(11, "Creatures within distance", "Creatures of an entry within a distance.", "{1:creature|creatures} within {2} yards", P("Creature", SP.Creature, "0 = any"), P("Max distance", SP.Yards), P("Alive (0 both, 1 alive, 2 dead)"));
            T(12, "Stored targets", "Targets saved earlier with action Remember targets.", "remembered targets #{1}", P("Storage ID"));
            T(13, "Objects in range", "Game objects of an entry between two distances.", "{1:gameobject|objects} {2}-{3} yards away", P("Object", SP.GameObject, "0 = any"), P("Min distance", SP.Yards), P("Max distance", SP.Yards));
            T(14, "Object by GUID", "One specific spawned game object.", "object spawn #{1}", P("Spawn GUID"), P("Object", SP.GameObject));
            T(15, "Objects within distance", "Game objects of an entry within a distance.", "{1:gameobject|objects} within {2} yards", P("Object", SP.GameObject, "0 = any"), P("Max distance", SP.Yards));
            T(16, "Invoker's party", "The party of whoever triggered the event.", "the invoker's party", P("Include pets", SP.Bool));
            T(17, "Players in range", "Players between two distances.", "players {1}-{2} yards away", P("Min distance", SP.Yards), P("Max distance", SP.Yards), P("Max count"));
            T(18, "Players within distance", "Players within a distance.", "players within {1} yards", P("Max distance", SP.Yards));
            T(19, "Closest creature", "The nearest creature of an entry.", "the nearest {1:creature|creature}", P("Creature", SP.Creature, "0 = any"), P("Max distance", SP.Yards), P("Dead ones", SP.Bool));
            T(20, "Closest object", "The nearest game object of an entry.", "the nearest {1:gameobject|object}", P("Object", SP.GameObject, "0 = any"), P("Max distance", SP.Yards));
            T(21, "Closest player", "The nearest player.", "the nearest player", P("Max distance", SP.Yards));
            T(22, "Invoker's vehicle", "The vehicle of whoever triggered the event.", "the invoker's vehicle", Array.Empty<SParam>());
            T(23, "Owner or summoner", "Whoever owns or summoned it.", "its owner", Array.Empty<SParam>());
            T(24, "Whole threat list", "Everyone on its threat list.", "everyone it's fighting", P("Max distance", SP.Yards), P("Players only", SP.Bool));
            T(25, "Closest enemy", "The nearest hostile unit.", "the nearest enemy", P("Max distance", SP.Yards), P("Players only", SP.Bool));
            T(26, "Closest friend", "The nearest friendly unit.", "the nearest friend", P("Max distance", SP.Yards), P("Players only", SP.Bool));
            T(27, "Loot recipients", "Players who tagged it (get kill credit).", "everyone who tagged it", Array.Empty<SParam>());
            T(28, "Farthest", "The farthest unit on its threat list.", "the farthest enemy", P("Max distance", SP.Yards), P("Players only", SP.Bool), P("In line of sight", SP.Bool), P("Min distance", SP.Yards));
            T(29, "Vehicle passenger", "A passenger in a seat of this vehicle.", "the passenger in seat {1}", P("Seat"));
            T(201, "Players with aura", "Players that have (or lack) an aura.", "players {2:bool|with|without} {1:spell}", P("Aura", SP.Spell), P("Without it instead", SP.Bool), P("Max distance", SP.Yards), P("Min distance", SP.Yards));
            T(202, "Random point", "Random points around it (or around the row's position).", "random points within {1} yards", P("Radius", SP.Yards), P("Count"), P("Around itself", SP.Bool));
            T(203, "Players by role", "Players by role: 1 tank, 2 healer, 4 damage.", "players with role mask {2}", P("Max distance", SP.Yards), P("Role mask"), P("Max count"));
            T(204, "Its summons", "Creatures it summoned.", "its summoned {1:creature|creatures}", P("Creature", SP.Creature, "0 = any"));
            T(205, "Instance storage", "A unit stored by the instance script.", "instance unit #{1}", P("Data index"), P("Type (1 creature, 2 object)"));
            T(206, "Formation", "Members or leader of its formation.", "its formation", P("Who (0 members, 1 leader, 2 all)"), P("Creature", SP.Creature), P("Exclude itself", SP.Bool));
            T(207, "Shared owner", "Units with the same owner.", "units with the same owner", P("Type (1 creature, 2 object)"), P("Entry"), P("Max distance", SP.Yards));
        }

        // ================================================================== sentences
        static string Secs(long ms) => ms % 1000 == 0 ? (ms / 1000).ToString(CultureInfo.InvariantCulture) : (ms / 1000.0).ToString("0.#", CultureInfo.InvariantCulture);
        public static string MsRange(long a, long b)
        {
            if (a == 0 && b == 0) return "at once";
            if (b < a) b = a;
            return a == b ? Secs(a) + "s" : $"{Secs(a)}-{Secs(b)}s";
        }

        /// <summary>Fills a template. resolve(kind, id) returns a name (spell, creature, quest...) or null.</summary>
        /// <summary>Add the number after names in sentences (Settings › Show technical names).</summary>
        public static bool ShowIds { get; set; }

        public static string Fill(string tpl, long[] p, SParam[] defs, Func<SP, long, string> resolve)
        {
            if (string.IsNullOrEmpty(tpl)) return "";
            return Regex.Replace(tpl, @"\{(r(\d)(\d)|(\d)(?::(\w+)(?:\|([^|}]*))?(?:\|([^}]*))?)?)\}", m =>
            {
                if (m.Groups[2].Success) return MsRange(Get(p, int.Parse(m.Groups[2].Value)), Get(p, int.Parse(m.Groups[3].Value)));
                int i = int.Parse(m.Groups[4].Value);
                long v = Get(p, i);
                var kind = m.Groups[5].Success ? m.Groups[5].Value : null;
                string alt1 = m.Groups[6].Success ? m.Groups[6].Value : null, alt2 = m.Groups[7].Success ? m.Groups[7].Value : null;
                switch (kind)
                {
                    case null: case "int": return v == 0 && alt1 != null ? alt1 : v.ToString(CultureInfo.InvariantCulture);
                    case "bool": return alt2 != null ? (v != 0 ? alt2 : alt1) : (v != 0 ? alt1 ?? "" : "");
                    case "hostility": return v == 0 ? "hostile" : v == 1 ? "friendly" : "";
                    case "react": return v == 0 ? "passive" : v == 1 ? "defensive" : "aggressive";
                    default:
                        if (v == 0 && alt1 != null) return alt1;
                        var sp = kind switch { "spell" => SP.Spell, "creature" => SP.Creature, "gameobject" => SP.GameObject, "quest" => SP.Quest, "item" => SP.Item, "faction" => SP.Faction, "map" => SP.Map, "script" => SP.Script, _ => SP.Int };
                        var n = resolve?.Invoke(sp, v);
                        return n != null ? (ShowIds ? $"{n} ({v})" : n) : $"#{v}";
                }
            });
        }

        static long Get(long[] p, int i) => i >= 1 && i <= p.Length ? p[i - 1] : 0;

        public static List<int> Phases(int mask) { var l = new List<int>(); for (int i = 0; i < 12; i++) if ((mask & (1 << i)) != 0) l.Add(i + 1); return l; }

        /// <summary>"When in combat, cast Fireball (133) on its victim, every 8-12s (first after 0-2s)."</summary>
        public static string Sentence(SmartRow r, Func<SP, long, string> resolve)
        {
            var e = Event(r.EventType); var a = Action(r.ActionType); var t = Target(r.TargetType);
            string ev = e == null ? $"event {r.EventType} happens" : Fill(e.Sentence, r.EventParams, e.P, resolve);
            string tail = null;
            int bar = ev.IndexOf('|');
            if (bar >= 0) { tail = ev.Substring(bar + 1); ev = ev.Substring(0, bar); if (tail.Contains("at once") && tail.StartsWith("every at once")) tail = null; }
            if (tail != null && e != null && (r.EventFlags & 1) != 0) tail = null;
            string ac = a == null ? $"do action {r.ActionType}" : Fill(a.Sentence, r.ActionParams, a.P, resolve);
            string tg = t == null ? $"target type {r.TargetType}" : Fill(t.Sentence, r.TargetParams, t.P, resolve);
            var sb = new StringBuilder();
            sb.Append(r.EventType == 61 ? "Then" : "When " + ev).Append(", ").Append(ac);
            if (!string.IsNullOrEmpty(tg) && r.TargetType != 1 || (r.TargetType == 1 && NeedsTarget(r.ActionType))) sb.Append(" on ").Append(tg);
            if (!string.IsNullOrEmpty(tail)) sb.Append(", ").Append(tail);
            var extras = new List<string>();
            if (r.EventChance > 0 && r.EventChance < 100) extras.Add($"{r.EventChance}% chance");
            if (r.EventPhaseMask != 0) extras.Add("phase " + string.Join(", ", Phases(r.EventPhaseMask)));
            if ((r.EventFlags & 1) != 0) extras.Add("once");
            if ((r.EventFlags & 0x1E) != 0) extras.Add("some difficulties only");
            if (extras.Count > 0) sb.Append(" (").Append(string.Join(", ", extras)).Append(')');
            sb.Append('.');
            var s = sb.ToString();
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        static bool NeedsTarget(int action) => action == 11 || action == 75 || action == 28 || action == 62;

        // ================================================================== validation
        public sealed class Issue { public int RowIndex; public string Level, Text; public override string ToString() => $"{Level}: {Text}"; }

        /// <summary>Checks a script's rows. exists(kind, id) returns false when an ID is unknown (null when it can't tell).</summary>
        public static List<Issue> Validate(IList<SmartRow> rows, Func<SP, long, bool?> exists)
        {
            var res = new List<Issue>();
            void Add(int i, string lvl, string t) => res.Add(new Issue { RowIndex = i, Level = lvl, Text = t });
            var ids = rows.Select(r => r.Id).ToList();
            foreach (var g in rows.GroupBy(r => r.Id).Where(g => g.Count() > 1)) Add(rows.IndexOf(g.ElementAt(1)), "Error", $"Two rows share id {g.Key}; each row of a script needs its own id.");
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                var e = Event(r.EventType); var a = Action(r.ActionType); var t = Target(r.TargetType);
                if (e == null) Add(i, "Error", $"Row {r.Id}: event type {r.EventType} doesn't exist on AzerothCore.");
                else if (e.Obsolete) Add(i, "Warning", $"Row {r.Id}: event \"{e.Name}\" does nothing on 3.3.5a.");
                if (a == null) Add(i, "Error", $"Row {r.Id}: action type {r.ActionType} doesn't exist on AzerothCore.");
                else if (a.Obsolete) Add(i, "Warning", $"Row {r.Id}: action \"{a.Name}\" isn't supported.");
                if (t == null) Add(i, "Error", $"Row {r.Id}: target type {r.TargetType} doesn't exist on AzerothCore.");
                if (r.EventChance <= 0) Add(i, "Warning", $"Row {r.Id}: chance is 0%, so it never fires.");
                if (r.EventChance > 100) Add(i, "Error", $"Row {r.Id}: chance can't be above 100%.");
                if ((r.EventPhaseMask & ~0xFFF) != 0) Add(i, "Error", $"Row {r.Id}: phase mask uses bits beyond phase 12.");
                if (r.Link != 0)
                {
                    var linked = rows.FirstOrDefault(x => x.Id == r.Link);
                    if (linked == null) Add(i, "Error", $"Row {r.Id} links to row {r.Link}, which doesn't exist.");
                    else if (linked.EventType != 61) Add(i, "Error", $"Row {r.Id} links to row {r.Link}, but that row's event isn't \"Linked\" (61), so the link is ignored.");
                    if (r.Link == r.Id) Add(i, "Error", $"Row {r.Id} links to itself.");
                }
                if (r.EventType == 61 && !rows.Any(x => x.Link == r.Id)) Add(i, "Warning", $"Row {r.Id} is a \"Linked\" row, but no row links to it, so it never runs.");
                if (e != null && e.P.Length >= 4 && e.P[2] == RMin && r.EventParams[2] > r.EventParams[3] && r.EventParams[3] != 0) Add(i, "Warning", $"Row {r.Id}: repeat minimum is larger than the maximum.");
                if (e != null && e.P.Length >= 2 && e.P[0] == IMin && r.EventParams[0] > r.EventParams[1] && r.EventParams[1] != 0) Add(i, "Warning", $"Row {r.Id}: first-time minimum is larger than the maximum.");
                void CheckRefs(SType st, long[] p, string where)
                {
                    if (st == null) return;
                    for (int k = 0; k < st.P.Length && k < p.Length; k++)
                    {
                        var kind = st.P[k].Kind;
                        if (p[k] == 0 || !(kind == SP.Spell || kind == SP.Creature || kind == SP.GameObject || kind == SP.Quest || kind == SP.Item || kind == SP.Script || kind == SP.TextGroup)) continue;
                        var ok = exists?.Invoke(kind, p[k]);
                        if (ok == false) Add(i, "Warning", $"Row {r.Id}: {where} \"{st.P[k].Label}\" = {p[k]} doesn't exist ({kind.ToString().ToLowerInvariant()} not found).");
                    }
                }
                CheckRefs(e, r.EventParams, "event"); CheckRefs(a, r.ActionParams, "action"); CheckRefs(t, r.TargetParams, "target");
                if (a != null && (r.ActionType == 11 || r.ActionType == 75) && r.TargetType == 0) Add(i, "Warning", $"Row {r.Id}: \"{a.Name}\" has no target.");
            }
            return res;
        }
    }
}
