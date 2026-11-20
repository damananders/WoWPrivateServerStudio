using System;
using System.Collections.Generic;

namespace AndersCraftStudio.Core
{
    /// <summary>Plain-English notes on common AzerothCore tables and fields. Written for Studio; unknown names simply have no note.</summary>
    public static class TableCatalog
    {
        static readonly Dictionary<string, string> Tables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // creatures
            ["creature_template"] = "What each kind of NPC or monster is: name, level range, faction, stats, flags and AI.",
            ["creature"] = "Every NPC placed in the world: which template, which map, and exactly where it stands.",
            ["creature_addon"] = "Extra looks for one placed NPC: mount, emote, auras, standing or sitting.",
            ["creature_template_addon"] = "Extra looks shared by every NPC of a template: mount, emote, auras.",
            ["creature_template_model"] = "Which 3D models a creature template can appear as, and how large.",
            ["creature_template_resistance"] = "Magic resistances (fire, frost, ...) of a creature template.",
            ["creature_template_spell"] = "Spells a creature can cast when a player controls it (vehicles, charmed pets).",
            ["creature_equip_template"] = "Weapons an NPC visibly holds in its hands.",
            ["creature_text"] = "Lines an NPC says, yells or emotes, used by scripts.",
            ["creature_formations"] = "Groups of NPCs that move together, following a leader.",
            ["creature_onkill_reputation"] = "Reputation you gain or lose for killing a creature.",
            ["creature_questender"] = "Which NPCs accept which quests when you hand them in.",
            ["creature_queststarter"] = "Which NPCs offer which quests.",
            ["creature_movement_override"] = "Per-spawn movement rules: can it swim, fly or walk.",
            ["npc_vendor"] = "Items that vendor NPCs sell, with stock limits.",
            ["npc_trainer"] = "Spells and skills that trainer NPCs teach, with cost and level.",
            ["npc_text"] = "Text shown in an NPC's chat window when you talk to it.",
            ["gossip_menu"] = "Links an NPC's chat window to the text it shows.",
            ["gossip_menu_option"] = "The clickable choices in an NPC's chat window.",
            ["waypoint_data"] = "Paths that patrolling NPCs walk along.",
            ["waypoints"] = "Simple waypoint paths used by scripts.",
            ["vehicle_template_accessory"] = "Passengers that automatically ride on a vehicle creature.",
            // items
            ["item_template"] = "What each item is: name, quality, stats, price, required level, spells it casts.",
            ["item_enchantment_template"] = "Random enchantment groups for items with random bonuses (\"of the Bear\").",
            ["item_set_names"] = "Names of item sets.",
            ["item_loot_template"] = "What you get when you open a container item, such as a bag or clam.",
            ["item_text"] = "Text written on readable items like letters and books.",
            // quests
            ["quest_template"] = "Every quest: objectives, rewards, level, required items and kills.",
            ["quest_template_addon"] = "Extra quest rules: chains, class/skill requirements, special flags.",
            ["quest_offer_reward"] = "Text the NPC says when you complete a quest.",
            ["quest_request_items"] = "Text the NPC says while you still need to bring items.",
            ["quest_details"] = "Emotes the NPC plays while offering a quest.",
            ["quest_poi"] = "Map markers that show where quest objectives are.",
            ["quest_poi_points"] = "The outline points of quest map markers.",
            ["gameobject_queststarter"] = "Objects (like wanted posters) that offer quests.",
            ["gameobject_questender"] = "Objects that accept finished quests.",
            // loot
            ["creature_loot_template"] = "What creatures drop when killed, and how often.",
            ["gameobject_loot_template"] = "What chests, herbs and ore nodes contain.",
            ["reference_loot_template"] = "Shared loot lists that other loot tables point to.",
            ["pickpocketing_loot_template"] = "What rogues can pickpocket from creatures.",
            ["skinning_loot_template"] = "What skinning (and herb/mining from creatures) gives.",
            ["disenchant_loot_template"] = "What disenchanting an item gives.",
            ["prospecting_loot_template"] = "What prospecting ore gives.",
            ["milling_loot_template"] = "What milling herbs gives.",
            ["fishing_loot_template"] = "What you can fish up in each zone.",
            ["mail_loot_template"] = "Items attached to mails the game sends you.",
            ["spell_loot_template"] = "Items created by spells that roll loot.",
            // objects & world
            ["gameobject_template"] = "What each kind of object is: chests, doors, chairs, herbs, portals.",
            ["gameobject"] = "Every object placed in the world and where it is.",
            ["gameobject_template_addon"] = "Extra rules for object templates: faction, flags, gold.",
            ["game_tele"] = "Named locations for the .tele GM command.",
            ["game_event"] = "World events and holidays, and when they run.",
            ["areatrigger_teleport"] = "Invisible zones that teleport you, such as instance entrances.",
            ["areatrigger_scripts"] = "Invisible zones that run a script when entered.",
            ["instance_template"] = "Settings for each dungeon or raid map.",
            ["playercreateinfo"] = "Where each race/class combination starts the game.",
            ["playercreateinfo_item"] = "Extra items new characters start with.",
            ["player_xp_for_level"] = "Experience needed to reach each level.",
            ["graveyard_zone"] = "Which graveyard a zone sends ghosts to.",
            ["game_graveyard"] = "Positions of graveyards.",
            ["points_of_interest"] = "Map pins NPC guards can show you.",
            ["broadcast_text"] = "Shared text that NPCs and gossip can display.",
            ["achievement_reward"] = "Titles, items and mail given for achievements.",
            ["achievement_criteria_data"] = "Extra conditions for achievement criteria.",
            ["conditions"] = "Shared rules (\"only if the player has quest X\") used by loot, gossip, spells and more.",
            ["pool_template"] = "Groups of spawns where only some appear at a time.",
            ["pool_creature"] = "Creature spawns belonging to a spawn pool.",
            ["pool_gameobject"] = "Object spawns (like ore veins) belonging to a spawn pool.",
            ["battleground_template"] = "Settings for each battleground: size, levels, start positions.",
            ["lfg_dungeon_rewards"] = "Rewards for Dungeon Finder runs.",
            ["fishing_loot"] = "Fishing loot (older table name).",
            // scripting
            ["smart_scripts"] = "SmartAI: event-driven behaviour scripts for NPCs, objects and area triggers.",
            ["spell_script_names"] = "Links spells to C++ spell scripts.",
            ["spell_proc"] = "When a spell's effect triggers (on hit, on crit, on taking damage...).",
            ["spell_bonus_data"] = "How much spell power or attack power boosts a spell.",
            ["spell_area"] = "Spells automatically applied in certain areas.",
            ["spell_linked_spell"] = "Spells that trigger or remove other spells.",
            ["spell_custom_attr"] = "Server-side tweaks to how specific spells behave.",
            ["spell_dbc"] = "Spell rows the server adds or overrides on top of the client's Spell.dbc.",
            ["spell_ranks"] = "Which spells are ranks 1, 2, 3... of the same spell.",
            ["spell_required"] = "Spells you must know before learning another.",
            ["spell_target_position"] = "Destinations of teleport spells.",
            ["command"] = "GM commands: who may use them and their help text.",
            ["trinity_string"] = "Text the server sends to players (system messages).",
            ["acore_string"] = "Text the server sends to players (system messages).",
            ["version"] = "Which database version this server is on.",
            ["updates"] = "Which database update files have already been applied.",
            ["updates_include"] = "Folders the server scans for database updates.",
            // characters DB
            ["characters"] = "Every player character: name, race, class, level, position, money.",
            ["character_inventory"] = "Which items each character is carrying, and in which bag slot.",
            ["item_instance"] = "Every individual item that exists in the game, owned by someone.",
            ["character_queststatus"] = "Quests each character currently has in their log.",
            ["character_queststatus_rewarded"] = "Quests each character has completed.",
            ["character_spell"] = "Spells each character has learned.",
            ["character_skills"] = "Skills (professions, weapons) and their levels per character.",
            ["character_reputation"] = "Reputation standing of each character with each faction.",
            ["character_achievement"] = "Achievements each character has earned.",
            ["guild"] = "Guilds and their settings.",
            ["guild_member"] = "Who is in which guild, and their rank.",
            ["mail"] = "In-game mail waiting in mailboxes.",
            ["auctionhouse"] = "Items currently up for auction.",
            ["character_homebind"] = "Each character's hearthstone location.",
            ["worldstates"] = "Saved world-wide state values.",
            // auth DB
            ["account"] = "Login accounts. Password data is stored hashed; Studio only ever shows it read-only.",
            ["account_access"] = "Which accounts are GMs, and at what level on which realm.",
            ["account_banned"] = "Account bans and their reasons.",
            ["ip_banned"] = "Banned IP addresses.",
            ["realmlist"] = "The realms this login server lists, with address and port.",
            ["realmcharacters"] = "How many characters each account has on each realm.",
            ["logs"] = "Server log messages saved to the database.",
        };

        public static IEnumerable<string> KnownTables => Tables.Keys;

        public static string Describe(string table) => table != null && Tables.TryGetValue(table, out var d) ? d : null;

        public static string CategoryOf(string table)
        {
            var t = (table ?? "").ToLowerInvariant();
            if (t.EndsWith("_loot_template")) return "Loot";
            if (t == "smart_scripts" || t.StartsWith("waypoint")) return "SmartAI";
            if (t.StartsWith("quest_") || t.EndsWith("_queststarter") || t.EndsWith("_questender")) return "Quests";
            if (t.StartsWith("creature") || t.StartsWith("npc_") || t.StartsWith("gossip") || t.StartsWith("vehicle")) return "Creatures";
            if (t.StartsWith("item_")) return "Items";
            if (t.StartsWith("spell_")) return "Spells";
            if (t.StartsWith("gameobject")) return "Objects";
            return "Database";
        }

        static readonly Dictionary<string, string> Fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["creature_template.entry"] = "Template ID. Placed spawns (creature.id1) point to it.",
            ["creature_template.name"] = "Name shown above the creature.",
            ["creature_template.subname"] = "Second line under the name, e.g. <Innkeeper>.",
            ["creature_template.minlevel"] = "Lowest level it can spawn at.",
            ["creature_template.maxlevel"] = "Highest level it can spawn at.",
            ["creature_template.faction"] = "Faction template: decides who it is friendly or hostile to.",
            ["creature_template.npcflag"] = "What talking to it does (vendor, trainer, quest giver...). Bit flags.",
            ["creature_template.rank"] = "0 normal, 1 elite, 2 rare elite, 3 boss, 4 rare.",
            ["creature_template.HealthModifier"] = "Multiplies base health. 1 = normal, 2 = double.",
            ["creature_template.ManaModifier"] = "Multiplies base mana.",
            ["creature_template.ArmorModifier"] = "Multiplies base armor.",
            ["creature_template.DamageModifier"] = "Multiplies the damage it deals.",
            ["creature_template.ExperienceModifier"] = "Multiplies experience given when killed.",
            ["creature_template.speed_walk"] = "Walking speed; 1 = normal.",
            ["creature_template.speed_run"] = "Running speed; about 1.14 is normal.",
            ["creature_template.scale"] = "Size; 1 = normal.",
            ["creature_template.BaseAttackTime"] = "Milliseconds between melee swings.",
            ["creature_template.unit_flags"] = "Behaviour flags (not attackable, pacified...). Bit flags.",
            ["creature_template.type"] = "Creature type: beast, humanoid, undead, ...",
            ["creature_template.AIName"] = "Which AI to use. SmartAI means it is driven by smart_scripts.",
            ["creature_template.ScriptName"] = "C++ script attached to it, if any.",
            ["creature_template.lootid"] = "Which creature_loot_template entry it drops from.",
            ["creature_template.mingold"] = "Least copper it drops.",
            ["creature_template.maxgold"] = "Most copper it drops.",
            ["creature_template.MovementType"] = "0 stays put, 1 wanders randomly, 2 follows waypoints.",
            ["creature.guid"] = "Unique ID of this one spawn.",
            ["creature.id1"] = "Which creature_template this spawn is.",
            ["creature.map"] = "Map ID (0 Eastern Kingdoms, 1 Kalimdor, 530 Outland, 571 Northrend).",
            ["creature.position_x"] = "Position on the map (X).",
            ["creature.position_y"] = "Position on the map (Y).",
            ["creature.position_z"] = "Height (Z).",
            ["creature.orientation"] = "Facing direction in radians.",
            ["creature.spawntimesecs"] = "Seconds until it respawns after death.",
            ["creature.wander_distance"] = "How far it wanders from its spawn point.",
            ["item_template.entry"] = "Item ID.",
            ["item_template.name"] = "Item name.",
            ["item_template.Quality"] = "0 poor (grey), 1 common, 2 uncommon (green), 3 rare (blue), 4 epic, 5 legendary.",
            ["item_template.ItemLevel"] = "Item level.",
            ["item_template.RequiredLevel"] = "Level needed to use it.",
            ["item_template.BuyPrice"] = "Price at vendors, in copper.",
            ["item_template.SellPrice"] = "What vendors pay you for it, in copper.",
            ["item_template.stackable"] = "How many fit in one stack.",
            ["item_template.bonding"] = "0 none, 1 bind on pickup, 2 bind on equip, 3 bind on use, 4 quest item.",
            ["item_template.InventoryType"] = "Where it is worn (head, chest, one-hand...).",
            ["item_template.armor"] = "Armor value.",
            ["item_template.dmg_min1"] = "Minimum weapon damage.",
            ["item_template.dmg_max1"] = "Maximum weapon damage.",
            ["item_template.delay"] = "Weapon speed in milliseconds.",
            ["item_template.description"] = "Yellow flavour text on the tooltip.",
            ["quest_template.ID"] = "Quest ID.",
            ["quest_template.LogTitle"] = "Quest name in the quest log.",
            ["quest_template.QuestLevel"] = "Quest level (-1 = scales with the player).",
            ["quest_template.MinLevel"] = "Level needed to get the quest.",
            ["quest_template.RewardMoney"] = "Copper given on completion.",
            ["quest_template.RewardXPDifficulty"] = "Experience reward tier.",
            ["quest_template.LogDescription"] = "Objective summary in the quest log.",
            ["quest_template.QuestDescription"] = "Full quest text.",
            ["creature_loot_template.Entry"] = "Loot ID; creature_template.lootid points here.",
            ["creature_loot_template.Item"] = "Item ID (or reference ID if Reference is set).",
            ["creature_loot_template.Chance"] = "Drop chance in percent.",
            ["creature_text.CreatureID"] = "The creature (creature_template.entry) whose lines these are.",
            ["creature_text.GroupID"] = "Text group. SmartAI's \"Say a line\" picks one line from a group.",
            ["creature_text.ID"] = "Line number inside the group (0, 1, 2 …).",
            ["creature_text.Text"] = "What is said. $n = the target's name, $r race, $c class.",
            ["creature_text.Type"] = "12 say, 14 yell, 16 emote, 41 boss emote, 15 whisper, 42 boss whisper.",
            ["creature_text.Language"] = "0 = universal (everyone understands), 7 Common, 1 Orcish …",
            ["creature_text.Probability"] = "Chance weight of this line among the lines of its group (0 counts as equal).",
            ["creature_text.Emote"] = "Animation played with the line (Emotes.dbc ID, 0 = none).",
            ["creature_text.Duration"] = "How long the line counts as \"being said\" in ms; \"Finished talking\" fires after it.",
            ["creature_text.Sound"] = "Sound played with the line (SoundEntries.dbc ID, 0 = none).",
            ["creature_text.BroadcastTextId"] = "Blizzard's translated text ID. Leave 0 for your own lines (Text is used).",
            ["creature_text.TextRange"] = "Who hears it: 0 normal range, 1 area, 2 zone, 3 map, 4 world.",
            ["creature_text.comment"] = "Your note; not shown in game.",
            ["creature_loot_template.QuestRequired"] = "1 = only drops while you have a quest that needs it.",
            ["creature_loot_template.GroupId"] = "Items in the same group (non-zero) drop at most one at a time.",
            ["creature_loot_template.MinCount"] = "Fewest copies dropped.",
            ["creature_loot_template.MaxCount"] = "Most copies dropped.",
            ["smart_scripts.entryorguid"] = "Which creature (template entry, or -guid for one spawn) the script belongs to.",
            ["smart_scripts.source_type"] = "0 creature, 1 game object, 2 area trigger, 9 timed list.",
            ["smart_scripts.event_type"] = "When it fires (on aggro, on timer, on death...).",
            ["smart_scripts.action_type"] = "What it does (cast, talk, summon...).",
            ["smart_scripts.target_type"] = "Who the action targets.",
            ["smart_scripts.comment"] = "Free text note for humans.",
        };

        static readonly Dictionary<string, string> AnyTable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["VerifiedBuild"] = "Client build the row was checked against. Doesn't affect gameplay.",
            ["comment"] = "Free text note for humans.",
            ["Comment"] = "Free text note for humans.",
            ["ScriptName"] = "C++ script attached to it, if any.",
        };

        public static string FieldMeaning(string table, string field)
        {
            if (field == null) return "";
            if (table != null && Fields.TryGetValue(table + "." + field, out var d)) return d;
            if (table != null && table.EndsWith("_loot_template", StringComparison.OrdinalIgnoreCase) && Fields.TryGetValue("creature_loot_template." + field, out d))
                return d.Replace("creature_template.lootid points here.", "the owner of this loot points here.");
            return AnyTable.TryGetValue(field, out d) ? d : "";
        }
    }
}
