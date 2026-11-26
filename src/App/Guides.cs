using System.Collections.Generic;
using System.Linq;

namespace AndersCraftStudio
{
    public sealed class GuideStep
    {
        public string Head, Text;
        /// <summary>Where the numbered marker sits on the picture, as fractions of its width and height (0,0 = no marker).</summary>
        public double X, Y;
    }

    public sealed class GuidePart
    {
        public string Name, Image, Intro;
        public List<GuideStep> Steps = new List<GuideStep>();
    }

    public sealed class Guide
    {
        public string Key, Area, Title, Intro, Go;
        public List<GuidePart> Parts = new List<GuidePart>();
        public List<string> Tips = new List<string>();
    }

    /// <summary>The "How to use this" guides: one per workspace, each a picture with numbered markers and short steps.</summary>
    public static class Guides
    {
        static GuideStep S(double x, double y, string head, string text) => new GuideStep { X = x, Y = y, Head = head, Text = text };

        static Guide G(string key, string area, string title, string intro, string go, string[] tips, params GuidePart[] parts) =>
            new Guide { Key = key, Area = area, Title = title, Intro = intro, Go = go, Tips = tips.ToList(), Parts = parts.ToList() };

        static GuidePart P(string name, string image, string intro, params GuideStep[] steps) => new GuidePart { Name = name, Image = image, Intro = intro, Steps = steps.ToList() };

        // the three record editors share a layout, so they share most steps
        static GuideStep[] EditorSteps(string what, string example, (double x, double y) jump, string jumpText, (double x, double y) tabs, string tabText, (double x, double y) field, double dy = 0) => new[]
        {
            S(0.354, 0.331 + dy, $"Find {what}", $"Type a name or an ID in the search box, for example {example}. The list updates as you type."),
            S(0.358, 0.431 + dy, "Open it", "Click a result. Its name, ID and a short summary show at the top."),
            S(jump.x, jump.y, "Jump to related screens", jumpText),
            S(tabs.x, tabs.y, "Pick a tab", tabText),
            S(field.x, field.y, "Change a field", "Type or pick a new value. Changed fields turn amber and get a \"Was …\" tag with the old value. Nothing is saved yet."),
            S(0.521, 0.365 + dy, "Stage edits (Ctrl+S)", "Puts your edits in the staged list. Discard drops them. Duplicate as new copies the record under a free ID."),
            S(0.775, 0.309 + dy, "Read the Guide card", "Explains the field you're in, in game terms, with tips and the value now and before."),
            S(0.775, 0.673 + dy, "Check the SQL", "The exact statement that will be staged, and the undo saved with it."),
            S(0.895, 0.06, "Review and apply", "The amber button opens Changes. Apply to the Sandbox to try it, or to Live when you're happy."),
        };

        static readonly string[] EditorTips =
        {
            "After an Apply, the editor reloads the record, so your next edit starts from what's in the database now.",
            "The game server reads most tables at start-up: use a .reload command or restart worldserver to see the change in game.",
        };

        public static readonly List<Guide> All = new List<Guide>
        {
            G("wizard", "Setup", "Setup wizard", "Connects Studio to your server in five steps. Nothing in setup changes your game data.", null,
                new[] { "You can run setup again any time: Settings › Run setup again.", "No server yet? Admin console › Set up AzerothCore creates the databases first." },
                P(null, "wizard.jpg", null,
                    S(0.195, 0.355, "Follow the steps on the left", "Welcome, Find your server, Safe database accounts, Game client folder, Practice sandbox. A tick means a step is done; you can go back to any of them."),
                    S(0.57, 0.254, "Browse account (read-only)", "Studio looks at everything with this account. It can't change anything."),
                    S(0.93, 0.254, "Apply account", "Used only when you press Apply on Changes, after a backup and a typed confirmation."),
                    S(0.57, 0.472, "Passwords", "Generate makes a strong one; Show reveals it. They're saved encrypted for your Windows user only."),
                    S(0.565, 0.652, "Connect from", "Leave localhost when Studio and MySQL are on the same PC."),
                    S(0.55, 0.81, "Make the accounts", "Scroll down and press Run it now as admin… (type your MySQL root password once; it isn't saved), or copy the script into any MySQL tool. Then press Test the accounts."),
                    S(0.906, 0.95, "Next", "Step 4 points Studio at your game client (optional), step 5 makes the practice sandbox (recommended). Finish setup opens Home."))),

            G("creatures", "World", "Creatures", "Edit NPCs and monsters with plain fields: stats, looks, spawns, equipment, vendor, loot and AI.", "creatures", EditorTips,
                P(null, "creatures.jpg", null, EditorSteps("a creature", "Hogger or 448", (0.72, 0.446), "Loot ›, SmartAI › and Spawns › open the matching screen for this creature.",
                    (0.717, 0.576), "Basics, Stats & combat, Look & movement (with the 3D view), Spawns, Equipment, Vendor/trainer, Loot, SmartAI and Extras.", (0.708, 0.749)))),

            G("items", "World", "Items", "Edit weapons, armour and everything else in bags, with the in-game look and prices.", "items", EditorTips,
                P(null, "items.jpg", null, EditorSteps("an item", "Thunderfury or 19019", (0.517, 0.403), "Dropped by › and Sold by › list the creatures and vendors that hand it out.",
                    (0.73, 0.476), "Basics, Stats, Weapon/armor, Requirements, Spells, Prices, Display (with the 3D view) and Contains & enchants.", (0.38, 0.743), -0.04))),

            G("quests", "World", "Quests", "Edit quest text, objectives, rewards, who starts and ends it, and quest chains.", "quests", EditorTips,
                P(null, "quests.jpg", null, EditorSteps("a quest", "Kobold Camp Cleanup or 7", (0.596, 0.443), "Givers › and Chain › show who starts and ends the quest and where it sits in a chain.",
                    (0.72, 0.512), "Basics, Objectives, Rewards, Texts, Starters/enders and Chain.", (0.542, 0.636)))),

            G("loot", "World", "Loot", "Who drops what, how often, in which groups and shared lists, and what a kill really gives.", "loot",
                new[] { "Chance is a percent. In a group (GroupId above 0) at most one row drops per kill.", "The drop simulator uses your unstaged edits, so you can try numbers before staging them." },
                P("Rows", "loot.jpg", "Pick a loot table, then edit its rows. The overview explains each row in words.",
                    S(0.367, 0.306, "Pick the kind of loot", "Creature drops, chests & nodes, openable items, shared lists (references), pickpocketing, skinning and more."),
                    S(0.367, 0.955, "Find the entry", "Type the loot ID or the owner's name and press Enter. A creature's loot ID is creature_template.lootid."),
                    S(0.646, 0.283, "Read the overview", "Rows rolled on their own, and groups where at most one row drops, with the chances in words."),
                    S(0.6, 0.456, "Open shared lists", "Open › jumps into a reference list; Back returns."),
                    S(0.727, 0.4, "How loot works", "What Chance, GroupId, MinCount/MaxCount, Reference and QuestRequired mean."),
                    S(0.53, 0.771, "Edit the rows", "Change cells straight in the grid. Item names show next to the IDs.")),
                P("Stage and simulate", "loot_sim.jpg", "Below the rows: stage your edits and see what a kill really gives.",
                    S(0.385, 0.298, "Add, duplicate, remove rows", "Duplicate row is the quick way to add a similar drop."),
                    S(0.70, 0.266, "Open selected reference", "Jumps into the shared list the selected row points at."),
                    S(0.765, 0.266, "Stage", "Puts the edited rows in the staged list. Discard puts them back as loaded."),
                    S(0.6575, 0.48, "Roll", "Pick how many kills to simulate and press Roll. Tick whether the player is on the quests."),
                    S(0.75, 0.567, "Results", "Per kill, % of kills and the average count when it drops, with the server's own rules."))),

            G("smartai", "Scripting", "SmartAI", "Build creature and object behaviour as sentences: when something happens, do something, to someone.", "smartai",
                new[] { "Action lists (the third source type) are reusable scripts that rows can start with 'run action list'." },
                P(null, "smartai.jpg", null,
                    S(0.38, 0.241, "Pick what the script belongs to", "Creature, Game object, or Action list."),
                    S(0.548, 0.239, "Load it", "Type the ID (or Pick… one) and press Load. The name shows next to it."),
                    S(0.415, 0.456, "Read the rows", "Each row is one sentence: when (event), do (action), to (target)."),
                    S(0.367, 0.344, "Add, duplicate or delete rows", "New rows start with sensible defaults; edit them on the right."),
                    S(0.725, 0.346, "Reads as", "The selected row in plain words, updated as you change it."),
                    S(0.74, 0.436, "Event, action and target", "Pick from lists; every parameter has a name and a hint. Times show in seconds too."),
                    S(0.842, 0.34, "Checks", "Problems such as missing links or impossible values are listed here before you stage."),
                    S(0.82, 0.921, "Stage script changes", "Puts the changed rows in the staged list. Discard puts the script back as loaded."))),

            G("spells", "Client data", "Spells", "Make a spell step by step or edit any spell as a form, see the in-game tooltip as you type, check it will show in the spellbook, and send it to the game in one go.", "spells",
                new[] { "A spell only shows in the spellbook when three things hold: it isn't marked \"Do not display\", it's linked to a spellbook tab (SkillLineAbility.dbc), and the game client has the same version as the server. The check card tests all three.", "Spell edits are client data (DBC), not database rows: they don't use Apply. Send to game puts them in the game and the server's copy together.", "After Send to game: restart worldserver if the server copy changed, delete WoW's Cache folder and start WoW. A spell set to \"learns it by themselves\" is in every character's book of that class after login; otherwise teach it with .learn, a trainer or a quest." },
                P("Create a spell", "spells_create.jpg", "Create a spell… (on the Spells screen) builds a whole spell from a few plain choices.",
                    S(0.342, 0.18, "Pick a kind", "Damage, Heal, Buff (raises something for a while) or Passive (always on once learned)."),
                    S(0.43, 0.297, "Name it", "As it shows in the spellbook and the tooltip."),
                    S(0.59, 0.313, "Choose an icon", "Any icon in SpellIcon.dbc, including ones you imported."),
                    S(0.594, 0.44, "Tooltip text", "Leave it empty and Studio writes it ($s1 is the amount, $d the duration)."),
                    S(0.415, 0.578, "How much", "The amount, and the school (Fire, Frost…) or what a buff raises (Stamina, run speed, experience…)."),
                    S(0.53, 0.69, "Cast time, cooldown, cost, range", "Picked from what the game supports; Studio uses the closest when there's no exact match."),
                    S(0.594, 0.918, "Spellbook tab", "The class and tab it shows under (or every class). Studio adds the SkillLineAbility link for you."),
                    S(0.88, 0.06, "Live tooltip", "How it will read in game, updated as you type."),
                    S(0, 0, "Create the spell", "Makes it under the next free custom ID and opens it in the editor. Nothing is staged yet.")),
                P("Edit and check", "spells.jpg", "Every spell opens as a form, with the spellbook check at the top right.",
                    S(0.3, 0.329, "Server or client copy", "Edit against the server's dbc folder (usual) or what the client loads."),
                    S(0.391, 0.375, "Find a spell", "Type a name or ID. Your custom spells are listed first."),
                    S(0.4375, 0.376, "Create, duplicate, compare", "Create a spell… (guided), Duplicate as new (copy under a free ID), Compare with original."),
                    S(0.7, 0.376, "Stage (Ctrl+S)", "Puts the spell edits and its spellbook link in the staged list."),
                    S(0.712, 0.811, "Tabs", "Overview, Effects, Targeting & range, Cast time & cooldown, Cost, Duration, Interrupts & procs, Visuals (3D effects), Attributes, Server side."),
                    S(0.975, 0.293, "Will it show in the spellbook?", "Green when it will. Otherwise each reason in plain words, with a button that fixes it."),
                    S(0.904, 0.432, "Testing as", "Pick the class you'll test with, to check it's linked for that class."),
                    S(0.837, 0.48, "Send to game", "One patch with every spell and DBC edit you've made, built to win over your other patches, installed with a backup."),
                    S(0.975, 0.553, "Tooltip preview", "How the spell will read in game, with the numbers filled in.")),
                P("When it won't show", "spells_problems.jpg", "What the check says for a spell like one marked hidden, passive and unlinked.",
                    S(0.8125, 0.384, "Hidden", "\"Do not display\" keeps it out of the book and off bars. Show it unticks that."),
                    S(0.854, 0.484, "Passive", "Passive spells show greyed and can't be clicked. Make it a button unticks Passive."),
                    S(0.8875, 0.583, "No spellbook tab", "Add to a spellbook tab… picks the class and tab and adds the SkillLineAbility link."),
                    S(0.8625, 0.683, "Client is older", "The game still has the old version (or none). Send to game fixes it."),
                    S(0.904, 0.731, "Testing as", "Checks the link covers the class you'll play.")),
                P("Send to game", "spells_send.jpg", "Puts everything in the game and the server's copy, once you've staged it.",
                    S(0.5, 0.189, "The patch", "Its name and folder. A patch in Data\enUS loads right after the base patch of the same letter, so patch-enUS-Z wins even over a patch-Z."),
                    S(0.615, 0.229, "Why that name", "The last patch holding these tables today, which yours loads after."),
                    S(0.81, 0.277, "What goes in", "Spell.dbc, SkillLineAbility.dbc and any other DBCs you changed, plus imported icons and models. Spells you edited get the server's exact row."),
                    S(0.685, 0.328, "Server copy", "Staged edits also go to the server's dbc folder (backed up first)."),
                    S(0.84, 0.436, "Type SEND", "Close WoW first; the game keeps its patches open."),
                    S(0.158, 0.555, "Build and install", "Builds and checks the patch, installs it with a backup, and moves the staged edits to History. Remove … from the game undoes it."))),

            G("dbc", "Client data", "DBC tables", "Read and edit any of the game client's 246 data tables. Originals are never overwritten.", "dbc",
                new[] { "Edits are staged as row patches. On Changes they leave as files for a client patch and an updated server copy.", "Ctrl+F jumps to the row filter; hover the filter box to see what you can type there." },
                P(null, "dbc.jpg", null,
                    S(0.2917, 0.349, "Server or client copy", "Server reads the server's dbc folder; Client reads your game's archives and patches."),
                    S(0.379, 0.433, "Find a table", "Search by name or by what it holds (for example 'mount' or 'icon')."),
                    S(0.6208, 0.323, "Tabs", "Each table opens in a tab, so you can switch without losing unstaged edits."),
                    S(0.9167, 0.466, "Filter and go to an ID", "Filter rows with Ctrl+F, or type an ID to jump to it."),
                    S(0.4, 0.556, "Stage and tools", "Stage edits (Ctrl+S), add/duplicate/remove rows, export or import CSV, find & replace, bulk edit, compare."),
                    S(0.4, 0.771, "Edit cells", "Double-click a cell. Edited rows turn amber until you stage them."),
                    S(0.4, 0.896, "Follow links", "Values that point at other tables are listed under the grid; click one to open it."))),

            G("patches", "Client data", "Client patches", "Pack client-side files into a patch-X.MPQ, install it with a backup, and share it with players.", "patches",
                new[] { "A patch with a later letter wins: patch-Z beats patch-W. Studio warns when one of your files would be hidden.", "Close WoW before installing; Studio checks that the game isn't running." },
                P(null, "patches.jpg", null,
                    S(0.454, 0.268, "Build, installed, browse", "Build a patch, see and uninstall Installed patches, or Browse any MPQ."),
                    S(0.967, 0.376, "Game client folder", "Same as setup step 4. Studio only reads it, except when you press Install or Uninstall."),
                    S(0.967, 0.675, "Work folder", "Studio's own folder for this patch. Send out DBC edits and Asset import write here."),
                    S(0.542, 0.763, "Add more files", "Start from an installed patch, add a server DBC, or add any files at their archive path."),
                    S(0.667, 0.841, "Who wins", "For each file: which archive the client uses today, and which after you install."),
                    S(0.983, 0.967, "Warnings", "Files a later patch would hide, with what to do about it."),
                    S(0, 0, "Build, then install", "Scroll down: pick the patch letter, press Build, then Install. Installing backs up any patch it replaces; Uninstall puts it back. Export a zip for players."))),

            G("assets", "Client data", "Asset import", "Bring in your own icons, models, sounds and other files; Studio converts them and registers them in the DBCs.", "assets",
                new[] { "Imported files go to a patch work folder, never into the game. Build and install that patch on Client patches.", "The DBC rows are staged: send them out from Changes into the same work folder." },
                P(null, "assets.jpg", null,
                    S(0.33, 0.326, "What to import", "Icon, creature model, item model, body armour textures, sound, or any file."),
                    S(0.496, 0.326, "Choose a file", "PNG/JPG/TGA/BLP for icons, .m2 with its .skin files for models, WAV/MP3 for sounds."),
                    S(0.5375, 0.409, "Preview", "The converted picture (or the 3D model) before anything is written."),
                    S(0.6417, 0.571, "Name and options", "The file name in the game, the format, and which DBC rows to add (for example a SpellIcon row)."),
                    S(0.5083, 0.9, "Import", "Writes the converted file to the work folder and stages the DBC rows."),
                    S(0.325, 0.951, "Patch work folder", "Which Client patches work folder the files go into."),
                    S(0.9, 0.703, "Where files go", "The game path for each kind of file."))),

            G("sql", "Database", "SQL browser", "Look at any table, filter it, export it, or run read-only queries. Edits are staged, never run.", "sql",
                new[] { "Write statements typed in the Query tab are staged too, with undo captured when they're applied.", "Right-click a row for more: duplicate, remove (staged), copy as INSERT." },
                P(null, "sql.jpg", null,
                    S(0.392, 0.312, "Pick a database", "World (read-only browsing), the sandbox, and view-only characters and auth."),
                    S(0.392, 0.546, "Filter tables", "Type part of a name. Each table shows its row count and what it holds."),
                    S(0.583, 0.397, "Data, Query, Structure", "Browse rows, run read-only SQL (F5), or see columns and indexes."),
                    S(0.667, 0.421, "Pages and tools", "Prev/Next page, find text, export or import CSV, export SQL, refresh."),
                    S(0.95, 0.546, "Edit a cell", "Double-click (or F2). The edit is staged (amber), not saved. Type NULL to clear a value."),
                    S(0.395, 0.982, "Hints", "The bottom line reminds you how editing works on this table."))),

            G("admin", "Database", "Admin console", "Everything you'd open HeidiSQL for: direct SQL, databases, users, imports and exports, server setup.", "admin",
                new[] { "Statements here run immediately. For game content use the editors: they stage, preview and back up every change.", "Deleting asks you to type the object's name; risky table changes offer a backup copy first." },
                P(null, "admin.jpg", null,
                    S(0.36, 0.392, "Server and port", "Usually 127.0.0.1 and 3306."),
                    S(0.6, 0.392, "Admin account", "An account that can manage MySQL, usually root."),
                    S(0.371, 0.573, "Password", "Typed each time, kept in memory only, never written to disk."),
                    S(0.275, 0.66, "Connect", "Opens the console: a tree of databases and tables on the left, tools on the right."),
                    S(0, 0, "Run SQL", "The Query tab runs any statement directly, with results in a grid."),
                    S(0, 0, "Manage", "Databases, tables, columns, indexes, views, users and permissions, connections and server status."),
                    S(0, 0, "Import, export, set up", "Import .sql files or whole folders, export databases, and Set up AzerothCore from scratch."))),

            G("server", "Server control", "Server control", "The live side of Studio: start and stop the server, players, accounts, messages, schedules, logs and backups. Everything here acts on your running server right away.", "sc_dash",
                new[] { "Server control is separate from editing on purpose: editing is staged and applied after review; Server control acts at once, and writes every action to the audit log.", "Commands need a GM login (Server control › Settings) and a realm with its command port (SOAP) on. Buttons stay greyed until both are there.", "Scheduled tasks and the crash watchdog run while Studio is open." },
                P("Dashboard", "server_dash.jpg", "What's running and who's online, refreshed every few seconds.",
                    S(0.303, 0.072, "Studio or Server control", "Switch between editing (staged, safe) and live server control. The red dot marks the live side."),
                    S(0.157, 0.1, "Live reminder", "Everything on this side acts on your server right away."),
                    S(0.66, 0.256, "The whole server", "Start everything, stop everything (gracefully), restart the realms, back up now, and the server's own status report."),
                    S(0.958, 0.379, "What's missing", "Notices like a missing GM login or a database that can't be read."),
                    S(0.667, 0.453, "Each realm", "Ready or not, uptime, CPU and memory, players and bots online, and the last log line."),
                    S(0.567, 0.568, "Restart if it crashes", "The watchdog starts the realm again when it stops on its own (a few times an hour at most)."),
                    S(0.633, 0.595, "Maintenance", "Close the realm to players (staff can still log in), or open it again."),
                    S(0.6125, 0.64, "Start, stop, restart", "Stop and restart save everyone first and offer a backup; a realm that won't close is only forced after you say so."),
                    S(0.958, 0.984, "Activity", "What Server control just did.")),
                P("Players", "server_players.jpg", "Every character on every realm.",
                    S(0.362, 0.226, "Search", "By character or account name."),
                    S(0.705, 0.229, "Filters", "Realm, online or offline, real players or bots."),
                    S(0.807, 0.236, "Open", "Opens a character's actions (or double-click it)."),
                    S(0.975, 0.332, "The list", "Click a column to sort. Ctrl+click or Shift+click to select several."),
                    S(0.965, 0.789, "Several at once", "Kick, revive, send home, mail items or gold to every selected character."),
                    S(0.91665, 0.9, "What the server said", "The answer for each one.")),
                P("One player", "server_player.jpg", "A character's actions, each confirmed first.",
                    S(0.39, 0.043, "Live", "These run on the server as soon as you confirm."),
                    S(0.71, 0.137, "At a glance", "Account, where they are, gold, time played, last seen."),
                    S(0.24, 0.231, "Bans and mutes", "Anything active on the character or its account."),
                    S(0.33, 0.455, "Session", "Kick, revive, details, mute and unmute, with a reason."),
                    S(0.69, 0.455, "Bans", "Ban the character or the whole account for a time (30m, 7d…, -1 forever), or lift a ban."),
                    S(0.31, 0.819, "Move", "Teleport to a named place or home; offline characters move at their next login."),
                    S(0.785, 0.944, "Character", "Level; new name, looks, race or faction at next login; reset talents or spells; delete (type the name)."),
                    S(0, 0, "Mail, gold and items", "Further down: mail with gold or up to 12 items.")),
                P("Console", "server_console.jpg", "Any server command, or a quick one picked from the list.",
                    S(0.97, 0.273, "Quick commands", "Common commands with plain names. Click one to put it in the box; check it, then Run."),
                    S(0.33, 0.916, "Realm", "Which realm the command goes to."),
                    S(0.72, 0.949, "Run", "Type a command (no dot needed) and press Enter or Run. The answer appears above.")),
                P("Messages & schedule", "server_schedule.jpg", "Talk to everyone, count down to a shutdown, and automate.",
                    S(0.658, 0.316, "The message", "What players read, in chat (and on screen if ticked)."),
                    S(0.492, 0.399, "Send it", "To one realm or every realm."),
                    S(0.667, 0.513, "Shutdown countdown", "Players see the countdown; Cancel it stops it."),
                    S(0.975, 0.606, "Scheduled", "Repeating messages, daily restarts (with warnings) and daily backups; tick to switch one off."),
                    S(0.575, 0.795, "Add", "A repeating message, a daily restart or a daily backup."),
                    S(0.958, 0.868, "What the server said", "Answers to what you sent."))),

            G("questbuilder", "Quests", "Quest builder", "Make a quest step by step, change one of yours, or start from one of the game's quests.", "quests",
                new[] { "Copying a game quest leaves the original alone; changing it changes it for everyone.", "Collect objectives can make a new quest item and add it to a creature's drops (only for players on the quest).", "\"Talk to\" objectives add a small behaviour script to the NPC that gives the credit.", "Everything is staged: review and Apply it on the Changes screen." },
                P("Start", "qb_start.jpg", "Pick how to begin. Quests page › Make a quest step by step, or Step by step… on any quest.",
                    S(0.19, 0.132, "The steps", "Eight steps, always in this order; click one to jump back."),
                    S(0.68, 0.212, "A brand-new quest", "Starts empty with sensible defaults."),
                    S(0.68, 0.425, "From a game quest", "Load one of the game's quests (or one of yours) to change or copy."),
                    S(0.465, 0.592, "Which quest", "Search by name."),
                    S(0.615, 0.727, "Copy or change", "Copy it as a new quest (recommended), or change the game's quest itself."),
                    S(0.98, 0.06, "What the player sees", "The quest window as the player will read it, updated as you go."),
                    S(0.68, 0.953, "Next", "Or Alt+→. Back is Alt+←.")),
                P("What to do", "qb_objectives.jpg", "The objectives: kill, bring items, talk to, use an object, go somewhere.",
                    S(0.675, 0.165, "One objective", "Each is a card; Remove takes it out."),
                    S(0.39, 0.239, "Kind", "Change what the player has to do."),
                    S(0.5, 0.322, "Target", "Pick the creature, item or object."),
                    S(0.325, 0.418, "How many", "From 1 to 255."),
                    S(0.385, 0.775, "A new quest item", "Studio makes one for you (and can make a creature drop it)."),
                    S(0.98, 0.06, "Preview", "The objectives as the quest log lists them.")),
                P("Review and stage", "qb_review.jpg", "Everything it will add or change, in plain words.",
                    S(0.163, 0.52, "All steps done", "A tick on each step you've been through."),
                    S(0.68, 0.12, "What changes", "Grouped by what they touch: the quest, its giver, its texts, drops, items."),
                    S(0.68, 0.49, "Also", "Things Studio did for you (a new item, a quest-giver mark, a drop)."),
                    S(0.285, 0.58, "Stage it", "Puts it all in the staged list. Apply it on the Changes screen."))),

            G("npcbuilder", "Creatures", "NPC builder", "Make an NPC step by step (or change or copy an existing one): what it is, its look, its strength, what it offers and where it stands.", "creatures",
                new[] { "Health and damage come from its level; Strength only multiplies them.", "Stand where you want it in the game and type .gps, then paste the reply in Where it stands.", "Copying a game NPC leaves the original alone." },
                P("Name and look", "nb_look.jpg", "Name, title and model, with a 3D preview.",
                    S(0.57, 0.16, "Name", "As players see it over its head."),
                    S(0.45, 0.386, "Look", "Search models by their file name (human, orc, guard, wolf…)."),
                    S(0.49, 0.632, "Weapons", "What it holds."),
                    S(0.985, 0.3, "3D preview", "Turn it with the mouse; pick an animation."),
                    S(0.84, 0.774, "Summary", "What it offers and where it stands.")),
                P("Where it stands", "nb_where.jpg", "Places in the world, from the in-game .gps command.",
                    S(0.675, 0.24, "Paste .gps", "The whole reply, or just \"map x y z\"."),
                    S(0.665, 0.325, "Add this place", "One spawn per place."),
                    S(0.415, 0.457, "Its places", "Remove any you don't want."),
                    S(0.435, 0.567, "Wander and respawn", "How far it walks around, and how soon it comes back after it's killed."))),

            G("behaviourbuilder", "Scripting", "Behaviour builder", "Give a creature a behaviour in plain words; Studio writes the SmartAI rows (and the lines it says).", "smartai",
                new[] { "A creature with its own program script ignores behaviours like these: the builder warns you.", "Its existing behaviours stay; new rows get the next free numbers.", "The SmartAI screen shows every row as a sentence for fine-tuning." },
                P("Pick a behaviour", "bb_pick.jpg", "Creatures › Make it do something…, SmartAI › Make it do something…, or Home.",
                    S(0.19, 0.19, "The steps", "Who, what, the details, review."),
                    S(0.675, 0.148, "Behaviours", "Casting in a fight, at low health, lines when a fight starts or it dies, calling for help, greeting players, animations, quest credit…"),
                    S(0.985, 0.125, "In plain words", "Exactly what it will do, as a sentence.")),
                P("The details", "bb_details.jpg", "Only the few things that behaviour needs.",
                    S(0.645, 0.16, "What it says", "$N is the player's name."),
                    S(0.435, 0.283, "How", "Say (nearby players) or yell (the whole area)."),
                    S(0.985, 0.125, "Preview", "Updated as you type."),
                    S(0.68, 0.953, "Next", "Then review and stage."))),

            G("changes", "Review", "Changes", "Everything you've staged: review it, apply it to the sandbox or Live, and roll back past applies.", "changes",
                new[] { "Apply backs up the affected tables first and runs everything in one transaction: all of it lands, or none of it.", "If a row changed since you staged an edit, Apply stops and offers: refresh the starting point, skip the change, or show it.", "Applied changes leave the staged list and appear under History." },
                P("Database changes", "changes.jpg", "Edits to database rows go through Apply.",
                    S(0.429, 0.385, "Tick what to apply", "Every staged change is listed. Untick any you want to keep for later."),
                    S(0.392, 0.339, "All, None, Discard, Export", "Discard removes a change (the database is never touched by it). Export SQL saves it as a .sql file with an undo file."),
                    S(0.608, 0.471, "Diff, SQL, Undo SQL", "Before and after for each field, the exact statement, and how it's undone."),
                    S(0.967, 0.44, "Sandbox or Live", "Sandbox is the practice copy. Live is your real world database, unlocked in Settings › Safety."),
                    S(0.717, 0.6, "Backup", "Always on: the affected tables are saved with mysqldump before anything runs."),
                    S(0.717, 0.771, "Type APPLY", "Type APPLY (or APPLY LIVE) so Apply is never one stray click away."),
                    S(0.717, 0.856, "Apply", "Runs the ticked changes in one transaction. When it's done they move to History and the list is ready for your next edits."),
                    S(0.321, 0.273, "History", "Every past apply with its backup and a Roll back button.")),
                P("Client data (DBC)", "changes_dbc.jpg", "Spell and DBC edits don't use Apply: they go out as files.",
                    S(0.41633, 0.4, "DBC edits", "Marked Client data (DBC). Pills show where each one has been sent: server ✓, patch ✓."),
                    S(0.715, 0.956, "Write to patch work folder", "Patched copies for a client patch. Then build and install the patch on Client patches."),
                    S(0, 0, "Update server DBC copy", "Below: backs up the server's DBC files, then replaces them (type UPDATE DBC). Restart worldserver afterwards."),
                    S(0.965, 0.516, "Status", "Which of the two ways out each ticked edit has already gone."),
                    S(0.965, 0.643, "Next", "What's left to do: build and install the patch, restart worldserver."),
                    S(0.917, 0.737, "Done: move to History", "Takes sent edits out of the staged list. An edit sent both ways moves there by itself.")),
                P("History and roll back", "changes_history.jpg", "Undo an earlier apply.",
                    S(0.321, 0.273, "History", "Newest first, with where it went and how many changes."),
                    S(0.479, 0.373, "One apply", "What changed, and the folder with the backup taken before it."),
                    S(0.467, 0.531, "Roll back…", "Opens the roll-back panel for this apply."),
                    S(0.329, 0.681, "Type ROLL BACK", "A typed confirmation, like Apply."),
                    S(0.455, 0.689, "Roll back now", "Backs up again, then runs the undo SQL newest-first in one transaction. It stops if a row was changed again since."))),
        };

        /// <summary>The guide for a page key (the record editors without their own guide share the Creatures one).</summary>
        public static Guide For(string page)
        {
            var g = All.FirstOrDefault(x => x.Key == page);
            if (g != null) return g;
            return page switch
            {
                "gameobjects" or "npctext" or "pagetext" or "broadcast" or "tele" => All.First(x => x.Key == "creatures"),
                _ when page != null && page.StartsWith("sc_") => All.FirstOrDefault(x => x.Key == "server"),
                _ => null,
            };
        }
    }
}
