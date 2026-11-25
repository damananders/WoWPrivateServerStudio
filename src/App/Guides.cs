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
        static GuideStep[] EditorSteps(string what, string example, (double x, double y) jump, string jumpText, (double x, double y) tabs, string tabText, (double x, double y) field) => new[]
        {
            S(0.338, 0.228, $"Find {what}", $"Type a name or an ID in the search box, for example {example}. The list updates as you type."),
            S(0.335, 0.36, "Open it", "Click a result. Its name, ID and a short summary show at the top."),
            S(jump.x, jump.y, "Jump to related screens", jumpText),
            S(tabs.x, tabs.y, "Pick a tab", tabText),
            S(field.x, field.y, "Change a field", "Type or pick a new value. Changed fields get an amber frame and a \"Was …\" tag with the old value. Nothing is saved yet."),
            S(0.566, 0.27, "Stage edits (Ctrl+S)", "Puts your edits in the staged list. Discard drops them. Duplicate as new copies the record under a free ID."),
            S(0.77, 0.21, "Read the Guide card", "Explains the field you're in, in game terms, with tips and the value now and before."),
            S(0.768, 0.592, "Check the SQL", "The exact statement that will be staged, and the undo saved with it."),
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
                    S(0.906, 0.915, "Next", "Step 4 points Studio at your game client (optional), step 5 makes the practice sandbox (recommended). Finish setup opens Home."))),

            G("creatures", "World", "Creatures", "Edit NPCs and monsters with plain fields: stats, looks, spawns, equipment, vendor, loot and AI.", "creatures", EditorTips,
                P(null, "creatures.jpg", null, EditorSteps("a creature", "Hogger or 448", (0.56, 0.341), "Loot ›, SmartAI › and Spawns › open the matching screen for this creature.",
                    (0.725, 0.41), "Basics, Stats & combat, Look & movement (with the 3D view), Spawns, Equipment, Vendor/trainer, Loot, SmartAI and Extras.", (0.71, 0.54)))),

            G("items", "World", "Items", "Edit weapons, armour and everything else in bags, with the in-game look and prices.", "items", EditorTips,
                P(null, "items.jpg", null, EditorSteps("an item", "Thunderfury or 19019", (0.505, 0.341), "Dropped by › and Sold by › list the creatures and vendors that hand it out.",
                    (0.72, 0.41), "Basics, Stats, Weapon/armor, Requirements, Spells, Prices, Display (with the 3D view) and Contains & enchants.", (0.535, 0.54)))),

            G("quests", "World", "Quests", "Edit quest text, objectives, rewards, who starts and ends it, and quest chains.", "quests", EditorTips,
                P(null, "quests.jpg", null, EditorSteps("a quest", "Kobold Camp Cleanup or 7", (0.475, 0.341), "Givers › and Chain › show who starts and ends the quest and where it sits in a chain.",
                    (0.72, 0.41), "Basics, Objectives, Rewards, Texts, Starters/enders and Chain.", (0.537, 0.50)))),

            G("loot", "World", "Loot", "Who drops what, how often, in which groups and shared lists, and what a kill really gives.", "loot",
                new[] { "Chance is a percent. In a group (GroupId above 0) at most one row drops per kill.", "The drop simulator uses your unstaged edits, so you can try numbers before staging them." },
                P("Rows", "loot.jpg", "Pick a loot table, then edit its rows. The overview explains each row in words.",
                    S(0.365, 0.245, "Pick the kind of loot", "Creature drops, chests & nodes, openable items, shared lists (references), pickpocketing, skinning and more."),
                    S(0.365, 0.90, "Find the entry", "Type the loot ID or the owner's name and press Enter. A creature's loot ID is creature_template.lootid."),
                    S(0.71, 0.33, "Read the overview", "Rows rolled on their own, and groups where at most one row drops, with the chances in words."),
                    S(0.6, 0.397, "Open shared lists", "Open › jumps into a reference list; Back returns."),
                    S(0.965, 0.34, "How loot works", "What Chance, GroupId, MinCount/MaxCount, Reference and QuestRequired mean."),
                    S(0.53, 0.714, "Edit the rows", "Change cells straight in the grid. Item names show next to the IDs.")),
                P("Stage and simulate", "loot_sim.jpg", "Below the rows: stage your edits and see what a kill really gives.",
                    S(0.385, 0.282, "Add, duplicate, remove rows", "Duplicate row is the quick way to add a similar drop."),
                    S(0.70, 0.25, "Open selected reference", "Jumps into the shared list the selected row points at."),
                    S(0.765, 0.25, "Stage", "Puts the edited rows in the staged list. Discard puts them back as loaded."),
                    S(0.55, 0.497, "Roll", "Pick how many kills to simulate and press Roll. Tick whether the player is on the quests."),
                    S(0.75, 0.551, "Results", "Per kill, % of kills and the average count when it drops, with the server's own rules."))),

            G("smartai", "Scripting", "SmartAI", "Build creature and object behaviour as sentences: when something happens, do something, to someone.", "smartai",
                new[] { "Action lists (the third source type) are reusable scripts that rows can start with 'run action list'." },
                P(null, "smartai.jpg", null,
                    S(0.38, 0.21, "Pick what the script belongs to", "Creature, Game object, or Action list."),
                    S(0.58, 0.21, "Load it", "Type the ID (or Pick… one) and press Load. The name shows next to it."),
                    S(0.41, 0.33, "Read the rows", "Each row is one sentence: when (event), do (action), to (target)."),
                    S(0.355, 0.28, "Add, duplicate or delete rows", "New rows start with sensible defaults; edit them on the right."),
                    S(0.74, 0.29, "Reads as", "The selected row in plain words, updated as you change it."),
                    S(0.74, 0.42, "Event, action and target", "Pick from lists; every parameter has a name and a hint. Times show in seconds too."),
                    S(0.77, 0.275, "Checks", "Problems such as missing links or impossible values are listed here before you stage."),
                    S(0.82, 0.905, "Stage script changes", "Puts the changed rows in the staged list. Discard puts the script back as loaded."))),

            G("spells", "Client data", "Spells", "Make a spell step by step or edit any spell as a form, see the in-game tooltip as you type, check it will show in the spellbook, and send it to the game in one go.", "spells",
                new[] { "A spell only shows in the spellbook when three things hold: it isn't marked \"Do not display\", it's linked to a spellbook tab (SkillLineAbility.dbc), and the game client has the same version as the server. The check card tests all three.", "Spell edits are client data (DBC), not database rows: they don't use Apply. Send to game puts them in the game and the server's copy together.", "After Send to game: restart worldserver if the server copy changed, delete WoW's Cache folder, start WoW, then learn the spell (trainer, quest or .learn)." },
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
                    S(0.29, 0.263, "Server or client copy", "Edit against the server's dbc folder (usual) or what the client loads."),
                    S(0.375, 0.322, "Find a spell", "Type a name or ID. Your custom spells are listed first."),
                    S(0.44, 0.31, "Create, duplicate, compare", "Create a spell… (guided), Duplicate as new (copy under a free ID), Compare with original."),
                    S(0.695, 0.313, "Stage (Ctrl+S)", "Puts the spell edits and its spellbook link in the staged list."),
                    S(0.705, 0.68, "Tabs", "Overview, Effects, Targeting & range, Cast time & cooldown, Cost, Duration, Interrupts & procs, Visuals (3D effects), Attributes, Server side."),
                    S(0.97, 0.23, "Will it show in the spellbook?", "Green when it will. Otherwise each reason in plain words, with a button that fixes it."),
                    S(0.905, 0.397, "Testing as", "Pick the class you'll test with, to check it's linked for that class."),
                    S(0.835, 0.446, "Send to game", "One patch with every spell and DBC edit you've made, built to win over your other patches, installed with a backup."),
                    S(0.97, 0.52, "Tooltip preview", "How the spell will read in game, with the numbers filled in.")),
                P("When it won't show", "spells_problems.jpg", "What the check says for a spell like one marked hidden, passive and unlinked.",
                    S(0.808, 0.321, "Hidden", "\"Do not display\" keeps it out of the book and off bars. Show it unticks that."),
                    S(0.85, 0.421, "Passive", "Passive spells show greyed and can't be clicked. Make it a button unticks Passive."),
                    S(0.885, 0.52, "No spellbook tab", "Add to a spellbook tab… picks the class and tab and adds the SkillLineAbility link."),
                    S(0.845, 0.648, "Client is older", "The game still has the old version (or none). Send to game fixes it."),
                    S(0.905, 0.697, "Testing as", "Checks the link covers the class you'll play.")),
                P("Send to game", "spells_send.jpg", "Puts everything in the game and the server's copy, once you've staged it.",
                    S(0.5, 0.189, "The patch", "Its name and folder. A patch in Data\enUS loads right after the base patch of the same letter, so patch-enUS-Z wins even over a patch-Z."),
                    S(0.615, 0.229, "Why that name", "The last patch holding these tables today, which yours loads after."),
                    S(0.62, 0.277, "What goes in", "Spell.dbc, SkillLineAbility.dbc and any other DBCs you changed, plus imported icons and models. Spells you edited get the server's exact row."),
                    S(0.685, 0.328, "Server copy", "Staged edits also go to the server's dbc folder (backed up first)."),
                    S(0.84, 0.436, "Type SEND", "Close WoW first; the game keeps its patches open."),
                    S(0.285, 0.539, "Build and install", "Builds and checks the patch, installs it with a backup, and moves the staged edits to History. Remove … from the game undoes it."))),

            G("dbc", "Client data", "DBC tables", "Read and edit any of the game client's 246 data tables. Originals are never overwritten.", "dbc",
                new[] { "Edits are staged as row patches. On Changes they leave as files for a client patch and an updated server copy.", "Ctrl+F jumps to the row filter; hover the filter box to see what you can type there." },
                P(null, "dbc.jpg", null,
                    S(0.295, 0.285, "Server or client copy", "Server reads the server's dbc folder; Client reads your game's archives and patches."),
                    S(0.378, 0.367, "Find a table", "Search by name or by what it holds (for example 'mount' or 'icon')."),
                    S(0.62, 0.26, "Tabs", "Each table opens in a tab, so you can switch without losing unstaged edits."),
                    S(0.64, 0.38, "Filter and go to an ID", "Filter rows with Ctrl+F, or type an ID to jump to it."),
                    S(0.392, 0.493, "Stage and tools", "Stage edits (Ctrl+S), add/duplicate/remove rows, export or import CSV, find & replace, bulk edit, compare."),
                    S(0.392, 0.77, "Edit cells", "Double-click a cell. Edited rows turn amber until you stage them."),
                    S(0.392, 0.88, "Follow links", "Values that point at other tables are listed under the grid; click one to open it."))),

            G("patches", "Client data", "Client patches", "Pack client-side files into a patch-X.MPQ, install it with a backup, and share it with players.", "patches",
                new[] { "A patch with a later letter wins: patch-Z beats patch-W. Studio warns when one of your files would be hidden.", "Close WoW before installing; Studio checks that the game isn't running." },
                P(null, "patches.jpg", null,
                    S(0.45, 0.185, "Build, installed, browse", "Build a patch, see and uninstall Installed patches, or Browse any MPQ."),
                    S(0.97, 0.25, "Game client folder", "Same as setup step 4. Studio only reads it, except when you press Install or Uninstall."),
                    S(0.965, 0.594, "Work folder", "Studio's own folder for this patch. Send out DBC edits and Asset import write here."),
                    S(0.535, 0.681, "Add more files", "Start from an installed patch, add a server DBC, or add any files at their archive path."),
                    S(0.68, 0.76, "Who wins", "For each file: which archive the client uses today, and which after you install."),
                    S(0.965, 0.905, "Warnings", "Files a later patch would hide, with what to do about it."),
                    S(0, 0, "Build, then install", "Scroll down: pick the patch letter, press Build, then Install. Installing backs up any patch it replaces; Uninstall puts it back. Export a zip for players."))),

            G("assets", "Client data", "Asset import", "Bring in your own icons, models, sounds and other files; Studio converts them and registers them in the DBCs.", "assets",
                new[] { "Imported files go to a patch work folder, never into the game. Build and install that patch on Client patches.", "The DBC rows are staged: send them out from Changes into the same work folder." },
                P(null, "assets.jpg", null,
                    S(0.345, 0.30, "What to import", "Icon, creature model, item model, body armour textures, sound, or any file."),
                    S(0.5, 0.215, "Choose a file", "PNG/JPG/TGA/BLP for icons, .m2 with its .skin files for models, WAV/MP3 for sounds."),
                    S(0.49, 0.35, "Preview", "The converted picture (or the 3D model) before anything is written."),
                    S(0.57, 0.488, "Name and options", "The file name in the game, the format, and which DBC rows to add (for example a SpellIcon row)."),
                    S(0.44, 0.816, "Import", "Writes the converted file to the work folder and stages the DBC rows."),
                    S(0.355, 0.908, "Patch work folder", "Which Client patches work folder the files go into."),
                    S(0.97, 0.62, "Where files go", "The game path for each kind of file."))),

            G("sql", "Database", "SQL browser", "Look at any table, filter it, export it, or run read-only queries. Edits are staged, never run.", "sql",
                new[] { "Write statements typed in the Query tab are staged too, with undo captured when they're applied.", "Right-click a row for more: duplicate, remove (staged), copy as INSERT." },
                P(null, "sql.jpg", null,
                    S(0.385, 0.21, "Pick a database", "World (read-only browsing), the sandbox, and view-only characters and auth."),
                    S(0.385, 0.467, "Filter tables", "Type part of a name. Each table shows its row count and what it holds."),
                    S(0.575, 0.334, "Data, Query, Structure", "Browse rows, run read-only SQL (F5), or see columns and indexes."),
                    S(0.665, 0.36, "Pages and tools", "Prev/Next page, find text, export or import CSV, export SQL, refresh."),
                    S(0.95, 0.455, "Edit a cell", "Double-click (or F2). The edit is staged (amber), not saved. Type NULL to clear a value."),
                    S(0.395, 0.966, "Hints", "The bottom line reminds you how editing works on this table."))),

            G("admin", "Database", "Admin console", "Everything you'd open HeidiSQL for: direct SQL, databases, users, imports and exports, server setup.", "admin",
                new[] { "Statements here run immediately. For game content use the editors: they stage, preview and back up every change.", "Deleting asks you to type the object's name; risky table changes offer a backup copy first." },
                P(null, "admin.jpg", null,
                    S(0.36, 0.376, "Server and port", "Usually 127.0.0.1 and 3306."),
                    S(0.6, 0.376, "Admin account", "An account that can manage MySQL, usually root."),
                    S(0.37, 0.51, "Password", "Typed each time, kept in memory only, never written to disk."),
                    S(0.27, 0.596, "Connect", "Opens the console: a tree of databases and tables on the left, tools on the right."),
                    S(0, 0, "Run SQL", "The Query tab runs any statement directly, with results in a grid."),
                    S(0, 0, "Manage", "Databases, tables, columns, indexes, views, users and permissions, connections and server status."),
                    S(0, 0, "Import, export, set up", "Import .sql files or whole folders, export databases, and Set up AzerothCore from scratch."))),

            G("changes", "Review", "Changes", "Everything you've staged: review it, apply it to the sandbox or Live, and roll back past applies.", "changes",
                new[] { "Apply backs up the affected tables first and runs everything in one transaction: all of it lands, or none of it.", "If a row changed since you staged an edit, Apply stops and offers: refresh the starting point, skip the change, or show it.", "Applied changes leave the staged list and appear under History." },
                P("Database changes", "changes.jpg", "Edits to database rows go through Apply.",
                    S(0.43, 0.318, "Tick what to apply", "Every staged change is listed. Untick any you want to keep for later."),
                    S(0.395, 0.27, "All, None, Discard, Export", "Discard removes a change (the database is never touched by it). Export SQL saves it as a .sql file with an undo file."),
                    S(0.607, 0.40, "Diff, SQL, Undo SQL", "Before and after for each field, the exact statement, and how it's undone."),
                    S(0.975, 0.38, "Sandbox or Live", "Sandbox is the practice copy. Live is your real world database, unlocked in Settings › Safety."),
                    S(0.715, 0.522, "Backup", "Always on: the affected tables are saved with mysqldump before anything runs."),
                    S(0.715, 0.693, "Type APPLY", "Type APPLY (or APPLY LIVE) so Apply is never one stray click away."),
                    S(0.715, 0.777, "Apply", "Runs the ticked changes in one transaction. When it's done they move to History and the list is ready for your next edits."),
                    S(0.3, 0.205, "History", "Every past apply with its backup and a Roll back button.")),
                P("Client data (DBC)", "changes_dbc.jpg", "Spell and DBC edits don't use Apply: they go out as files.",
                    S(0.375, 0.32, "DBC edits", "Marked Client data (DBC). Pills show where each one has been sent: server ✓, patch ✓."),
                    S(0.715, 0.94, "Write to patch work folder", "Patched copies for a client patch. Then build and install the patch on Client patches."),
                    S(0, 0, "Update server DBC copy", "Below: backs up the server's DBC files, then replaces them (type UPDATE DBC). Restart worldserver afterwards."),
                    S(0.965, 0.50, "Status", "Which of the two ways out each ticked edit has already gone."),
                    S(0.965, 0.6, "Next", "What's left to do: build and install the patch, restart worldserver."),
                    S(0.92, 0.666, "Done: move to History", "Takes sent edits out of the staged list. An edit sent both ways moves there by itself.")),
                P("History and roll back", "changes_history.jpg", "Undo an earlier apply.",
                    S(0.3, 0.205, "History", "Newest first, with where it went and how many changes."),
                    S(0.475, 0.305, "One apply", "What changed, and the folder with the backup taken before it."),
                    S(0.465, 0.462, "Roll back…", "Opens the roll-back panel for this apply."),
                    S(0.325, 0.605, "Type ROLL BACK", "A typed confirmation, like Apply."),
                    S(0.45, 0.615, "Roll back now", "Backs up again, then runs the undo SQL newest-first in one transaction. It stops if a row was changed again since."))),
        };

        /// <summary>The guide for a page key (the record editors without their own guide share the Creatures one).</summary>
        public static Guide For(string page)
        {
            var g = All.FirstOrDefault(x => x.Key == page);
            if (g != null) return g;
            return page switch
            {
                "gameobjects" or "npctext" or "pagetext" or "broadcast" or "tele" => All.First(x => x.Key == "creatures"),
                _ => null,
            };
        }
    }
}
