using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AndersCraftStudio.Core
{
    /// <summary>
    /// Plain-English names for database tables and columns, used wherever Studio would otherwise show an engine name
    /// (list headers, the Changes screen, builders). The raw name shows only with Settings › Show technical names.
    /// </summary>
    public static class PlainNames
    {
        static readonly Dictionary<string, string> Tables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["creature_template"] = "Creature", ["creature"] = "Where a creature stands (spawn)", ["creature_template_model"] = "Creature look (model)",
            ["creature_equip_template"] = "Creature weapons", ["creature_template_addon"] = "Creature extras (mount, aura, pose)", ["creature_addon"] = "Spawn extras",
            ["creature_text"] = "What a creature says", ["creature_loot_template"] = "Creature drops", ["gameobject_loot_template"] = "Object loot",
            ["item_loot_template"] = "Contents of an item", ["reference_loot_template"] = "Shared loot list", ["pickpocketing_loot_template"] = "Pickpocket loot",
            ["skinning_loot_template"] = "Skinning loot", ["disenchant_loot_template"] = "Disenchant results", ["prospecting_loot_template"] = "Prospecting results",
            ["milling_loot_template"] = "Milling results", ["fishing_loot_template"] = "Fishing loot", ["mail_loot_template"] = "Mail contents", ["spell_loot_template"] = "Spell loot",
            ["quest_template"] = "Quest", ["quest_template_addon"] = "Quest extras (chain, classes, repeat)", ["quest_request_items"] = "Quest hand-in text (not done yet)",
            ["quest_offer_reward"] = "Quest reward text", ["creature_queststarter"] = "Who gives a quest", ["creature_questender"] = "Who takes a quest back",
            ["gameobject_queststarter"] = "Object that gives a quest", ["gameobject_questender"] = "Object that takes a quest back", ["areatrigger_involvedrelation"] = "Place to explore for a quest",
            ["item_template"] = "Item", ["gameobject_template"] = "Object", ["gameobject"] = "Where an object stands (spawn)", ["npc_vendor"] = "What a vendor sells",
            ["npc_text"] = "What an NPC says in its chat window", ["gossip_menu"] = "Chat window", ["gossip_menu_option"] = "Chat window choice", ["smart_scripts"] = "Behaviour script (SmartAI)",
            ["creature_default_trainer"] = "What a trainer teaches", ["trainer"] = "Trainer", ["trainer_spell"] = "Trainer spell", ["conditions"] = "Condition",
            ["waypoint_data"] = "Walking path point", ["game_event"] = "World event", ["game_tele"] = "Teleport location", ["page_text"] = "Book page", ["broadcast_text"] = "Shared text line",
            ["spell_dbc"] = "Server-side spell", ["event_scripts"] = "Event script step", ["spell_scripts"] = "Spell script step", ["areatrigger_teleport"] = "Teleport trigger",
            ["pool_template"] = "Spawn pool", ["creature_formations"] = "Group formation", ["npc_spellclick_spells"] = "Click-to-use spell",
        };

        static readonly Dictionary<string, string> Columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // shared
            ["entry"] = "ID", ["ID"] = "ID", ["name"] = "Name", ["subname"] = "Title under the name", ["comment"] = "Note", ["Comment"] = "Note", ["VerifiedBuild"] = "Checked against game build",
            ["minlevel"] = "Lowest level", ["maxlevel"] = "Highest level", ["faction"] = "Faction (who it's friendly to)", ["npcflag"] = "Services", ["rank"] = "Rank (normal / elite / boss)",
            ["HealthModifier"] = "Health multiplier", ["DamageModifier"] = "Damage multiplier", ["ManaModifier"] = "Mana multiplier", ["ArmorModifier"] = "Armour multiplier", ["lootid"] = "Drops list",
            ["AIName"] = "Behaviour engine", ["ScriptName"] = "Program script", ["gossip_menu_id"] = "Chat window", ["scale"] = "Size", ["unit_flags"] = "Behaviour switches", ["type"] = "Creature type",
            // loot
            ["Entry"] = "Loot list", ["Item"] = "Item", ["Reference"] = "Shared list", ["Chance"] = "Chance (%)", ["QuestRequired"] = "Only for players on the quest", ["LootMode"] = "Difficulty mode",
            ["GroupId"] = "Group (one drop per group)", ["MinCount"] = "At least", ["MaxCount"] = "At most",
            // spawns
            ["guid"] = "Spawn number", ["id1"] = "Creature", ["map"] = "Map", ["zoneId"] = "Zone", ["areaId"] = "Area", ["position_x"] = "X", ["position_y"] = "Y", ["position_z"] = "Height (Z)",
            ["orientation"] = "Facing", ["spawntimesecs"] = "Respawn time (seconds)", ["wander_distance"] = "Wanders (yards)", ["MovementType"] = "Movement", ["spawnMask"] = "Difficulties", ["phaseMask"] = "Phases",
            ["equipment_id"] = "Weapons set", ["curhealth"] = "Health at spawn", ["curmana"] = "Mana at spawn",
            // vendor
            ["slot"] = "Position in the list", ["maxcount"] = "Stock (0 = unlimited)", ["incrtime"] = "Restock time (seconds)", ["ExtendedCost"] = "Special price (badges, honour)",
            // quests
            ["LogTitle"] = "Quest name", ["QuestLevel"] = "Quest level", ["MinLevel"] = "Lowest level to take it", ["QuestInfoID"] = "Quest type", ["SuggestedGroupNum"] = "Group size",
            ["RewardXPDifficulty"] = "Experience reward", ["RewardMoney"] = "Money reward (copper)", ["RewardNextQuest"] = "Offers this quest next", ["QuestDescription"] = "Quest text",
            ["LogDescription"] = "Quest log summary", ["QuestCompletionLog"] = "Text when done", ["AllowableRaces"] = "Races that can take it", ["Flags"] = "Quest switches",
            ["PrevQuestID"] = "Must have done quest first", ["AllowableClasses"] = "Classes that can take it", ["SpecialFlags"] = "Repeat / explore switches", ["CompletionText"] = "Hand-in text (not done yet)",
            ["RewardText"] = "Reward text", ["quest"] = "Quest", ["id"] = "Who", ["QuestSortID"] = "Listed under zone",
            // texts
            ["CreatureID"] = "Creature", ["GroupID"] = "Line group", ["Text"] = "Text", ["Type"] = "How (say / yell / emote)", ["Language"] = "Language", ["Probability"] = "Chance", ["Emote"] = "Animation",
            ["Duration"] = "How long it shows", ["Sound"] = "Sound", ["BroadcastTextId"] = "Shared text line", ["TextRange"] = "Who hears it",
            ["text0_0"] = "What it says", ["text0_1"] = "What it says (to women)", ["MenuID"] = "Chat window", ["TextID"] = "Text",
            // SmartAI
            ["entryorguid"] = "Creature / object", ["source_type"] = "Belongs to", ["link"] = "Then runs row", ["event_type"] = "When", ["action_type"] = "Do", ["target_type"] = "To whom",
            ["event_chance"] = "Chance (%)", ["event_phase_mask"] = "Phases", ["event_flags"] = "Event switches",
            // models / equipment
            ["CreatureDisplayID"] = "Model", ["DisplayScale"] = "Size", ["Idx"] = "Look number", ["ItemID1"] = "Main hand", ["ItemID2"] = "Off hand", ["ItemID3"] = "Ranged",
        };

        static readonly Regex Numbered = new Regex(@"^(.*?)(\d+)$");
        static readonly Dictionary<string, string> NumberedColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["RequiredNpcOrGo"] = "Objective {0}: creature or object", ["RequiredNpcOrGoCount"] = "Objective {0}: how many", ["RequiredItemId"] = "Item to bring {0}", ["RequiredItemCount"] = "Item to bring {0}: how many",
            ["RewardItem"] = "Reward item {0}", ["RewardAmount"] = "Reward item {0}: how many", ["RewardChoiceItemID"] = "Choice of reward {0}", ["RewardChoiceItemQuantity"] = "Choice of reward {0}: how many",
            ["RewardFactionID"] = "Reputation {0}: faction", ["RewardFactionOverride"] = "Reputation {0}: amount (×100)", ["RewardFactionValue"] = "Reputation {0}: preset amount", ["ObjectiveText"] = "Objective {0}: text",
            ["event_param"] = "When: setting {0}", ["action_param"] = "Do: setting {0}", ["target_param"] = "To whom: setting {0}", ["modelid"] = "Model {0}",
        };

        /// <summary>The plain name of a table (the raw name when none is known).</summary>
        public static string Table(string table) => table != null && Tables.TryGetValue(table, out var n) ? n : table;

        /// <summary>The plain name of a column (the raw name, made readable, when none is known).</summary>
        public static string Column(string table, string column)
        {
            if (string.IsNullOrEmpty(column)) return column;
            if (Columns.TryGetValue(column, out var n)) return n;
            var m = Numbered.Match(column);
            if (m.Success && NumberedColumns.TryGetValue(m.Groups[1].Value.TrimEnd('_'), out var f)) return string.Format(f, m.Groups[2].Value);
            return Readable(column);
        }

        public static bool Known(string column) => !string.IsNullOrEmpty(column) && (Columns.ContainsKey(column) || (Numbered.Match(column) is var m && m.Success && NumberedColumns.ContainsKey(m.Groups[1].Value.TrimEnd('_'))));

        /// <summary>"RequiredSkillPoints" → "Required skill points", "spawn_time" → "Spawn time".</summary>
        public static string Readable(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            var s = raw.Replace('_', ' ');
            s = Regex.Replace(s, "([a-z])([A-Z])", "$1 $2");
            s = Regex.Replace(s, "([A-Za-z])([0-9])", "$1 $2");
            s = s.Trim();
            return s.Length == 0 ? raw : char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant().Replace(" id", " ID");
        }
    }
}
