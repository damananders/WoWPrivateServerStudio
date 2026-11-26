# Release notes

## 0.9.0 — 3D model viewer

**New**
* **3D model viewer** in Creatures (Basics), Game objects (Basics), Items (Display) and Asset import: the model textured
  and lit, straight from your client's archives (and your patch work folders), with turn / move / zoom, wireframe and a
  model picker for creatures with several looks.
* **Animations**: every sequence by name (Stand, Walk, Attack, Spell, Death…), play / pause / speed, including the ones
  kept in separate .anim files.
* Reflective metal and glow layers are approximated, so weapons look like metal.
* **Script tables without a key** (event_scripts, spell_scripts) are now editable: rows are matched on every column.
* **DBC tables open in tabs**: switch between several tables without losing unstaged edits.

* **Dressed characters**: humanoid NPCs show with their hair, beard, face, clothes and armour (the NPC's baked texture or the
  look composed from skin, face and armour pieces), and their helmet, shoulders, weapons and shield.
* **Spell effects**: particles and ribbons (sparks, smoke, glows, trails), approximated; Spell editor › Visuals shows the
  spell's cast, missile and impact effect models.
* **WMO buildings**: big game objects (houses, towers…) with all their groups and textures.
* **Body armour textures** in Asset import: one picture per body region → Item\TextureComponents + an ItemDisplayInfo row,
  previewed on a character.

**New: "How to use this" guides.** Every main screen has a picture of itself with numbered markers and short steps:
Creatures, Items, Quests, Loot, SmartAI, Spells, DBC tables, Client patches, Asset import, SQL browser, Admin console,
Changes (database changes, client data, history and roll back) and the setup wizard. Open the one for the screen you're on
with *How to use this* in the top bar or Shift+F1 (in setup: the button under the steps); Help › How-to guides lists them
all. It opens in its own window, so the screen you're on stays as it is.

**New: step-by-step builders for quests, NPCs and behaviours**
Linear, guided ways to make or change things, next to the full editors (which stay for everything else). Each starts from
nothing, from one of your own, or from one of the game's (copy it as new, recommended, or change the original), shows a
live preview, explains problems in plain words, lists what it will add or change, and stages it: Apply on Changes as usual.
* **Make a quest** (8 steps): name, levels, kind (group, dungeon, raid, PvP) and how often (once, daily, weekly,
  repeatable); who gives it and who takes it back (creature or object; the "!" is added when needed); what to do (kill,
  bring items, talk to, use an object, go somewhere). Bring-items can make a new quest item and add it to a creature's
  drops (only for players on the quest); talk-to adds a small behaviour that gives the credit. Then rewards (experience,
  money, items, a choice of items, reputation, a spell), the texts with a parchment preview of what the player reads, and
  who can take it (side, classes, the quest before it, the quest offered next).
* **Make an NPC** (7 steps): what it is (friendly, vendor, trainer, quest giver, innkeeper, enemy, critter); name, title,
  model with a 3D preview and weapons; level, side and strength (health and damage follow its level); services, what it
  says in its chat window, what it sells and teaches (copied from another trainer); where it stands (paste the in-game
  .gps reply), wandering and respawn time.
* **Make it do something**: a behaviour for any creature in plain words: cast a spell in a fight or at low health, say or
  yell a line when a fight starts, at low health or when it dies, call for reinforcements, run for help, greet players who
  walk by, play an animation now and then, count for a quest when talked to, cast on itself when it appears. Studio writes
  the SmartAI rows and the lines (creature_text) and shows them as sentences.
* Verified against the real tables (read-only): every column the builders write exists, and a quest that only reads a
  creature never changes or removes it.

**Plain names everywhere** (see NAMING_AUDIT.md): column names under editor fields, numbers in dropdowns, flag bits, raw
list headers, field names on Changes, numbers in SmartAI sentences and parameter numbers are gone unless Settings › Show
technical names is on. List headers and the Changes screen use plain names ("Only for players on the quest", "Respawn time
(seconds)").

**New: Server control (the Control Panel, folded in and beefed up)**
A second tab at the top, next to Studio, for running the server. It is kept clearly apart from editing: a red "Live server"
marker on every page and in the sidebar, and a reminder that it acts on the running server right away (nothing is staged,
Apply isn't involved). Every action goes to an audit log.
* **Dashboard**: login server, each realm (ready, uptime, CPU, memory, players and bots online, last log line), the sign-up
  website and tunnel, the daily backup script; start / stop / restart each, or everything. Stops save everyone and shut
  down gracefully over the command port, offer a backup first, and only force a realm after a second confirmation.
* **Crash watchdog** (per realm, opt-in): starts a realm again when it stops on its own, at most a few times an hour.
* **Maintenance mode** per realm: only staff can log in.
* **Players**: every character on every realm with search and filters (realm, online, real players / bots); one
  character's window (kick, revive, mute, bans for a time, teleport, home, level, rename / looks / race / faction at next
  login, reset talents / spells, delete with the name typed, mail with gold or items); **several at once** (kick, revive,
  send home, mail items or gold).
* **Accounts**: create, new password, staff rank, expansion, ban and lift bans (passwords never reach the audit log).
* **Console** with **quick commands**: common commands with plain names (status, who's online, save everyone, messages,
  maintenance, shutdown countdown, reloads, message of the day, ban lists).
* **Logs**: each realm's logs followed live, problems in colour, "problems only", and the audit log (Studio's and the old
  Control Panel's).
* **Messages & schedule**: message everyone (chat and screen), a shutdown countdown, and scheduled repeating messages,
  daily restarts (with warnings at 15 / 10 / 5 / 1 minutes) and daily backups, run while Studio is open.
* **Backups**: every backup (Server control's, Studio's and the old Control Panel's), back up chosen databases now, open
  one, or put a database back (typed confirmation, a fresh safety backup first).
* **Settings**: GM login (encrypted per Windows user), folders, ports and realms; **Import from the old Control Panel**
  (its settings file is only read; the old program keeps working).
* The login and character databases are read with Studio's read-only account, falling back to the server's own login
  from worldserver.conf in a read-only session for a realm the read-only account can't see.

**Plain names first**: Settings › *Show technical names* (off by default). The "Column: …" lines under editor fields and
the raw command text in Server control only show when it's on.

**Spells: made to show up in the spellbook**
* **Why a learned custom spell never appeared in the spellbook.** Three things have to hold, and none did for a spell made
  in earlier versions:
  1. The game client must have the same Spell.dbc row as the server. The client decides what the book shows from its own
     copy, and the patch with the edits was never in the game. Also, each *Write to patch work folder* started again
     from the client's copy and kept only the edits still staged, so edits sent earlier (like un-hiding the spell) were
     dropped from the patch folder.
  2. The spell must not be marked "Do not display" (Attributes 0x80). The client's copy still had that flag, plus Passive.
  3. The spell needs a SkillLineAbility.dbc row linking it to a spellbook tab the character has. Studio never added one.
* **Create a spell…** (guided): damage, heal, buff or passive from a few plain fields, with the tooltip live as you type.
  Studio starts from a real spell of the same kind (Fireball, Lesser Heal, Power Word: Fortitude), clears what you didn't
  ask for, sets the numbers, text, icon, cost, cast time, cooldown, range and duration, and links it to the class and
  spellbook tab you pick (or every class).
* **Will it show in the spellbook?** A card on every spell lists each reason it won't, in plain words, each with a fix:
  * *Show it* (unticks "Do not display"), *Make it a button* (unticks Passive), *Clear it* (a spell that triggers itself).
  * *Add to a spellbook tab…* (picks the class and tab and adds the SkillLineAbility row).
  * *Send to game…* when the client's copy is older or missing.
  It also checks the class you'll test with, the icon and the visual. When everything is right, it turns green and names
  the tab.
* **Send to game**: one client patch with every DBC edit in History plus the staged ones, and the files from your patch
  work folder (icons, models). Spells you edited get the server's exact row, with their spellbook links and icon.
  * The patch goes where the game really reads it. The 3.3.5a client sorts Data\patch-X.MPQ and Data\enUS\patch-enUS-X.MPQ
    by their path and reads the highest first, so every Data\patch-X beats every enUS patch (read from WoW.exe itself).
    Studio picks a Data\patch-X name that loads after every patch holding those tables; when one already loads last (a
    patch-Z of your own), Studio puts its tables into that patch instead, backed up first, leaving the rest untouched.
  * Fixed: earlier sends went to Data\enUS\patch-enUS-Z.MPQ, which a Data\patch-Z.MPQ outranks, so the game kept an older
    copy of the spell (known on the server, but hidden in the spellbook and not castable by name). Send it again.
  * Spellbook links can make every character of the class learn the spell by themselves at their next login (the server
    does it: SkillLineAbility AcquireMethod 1). On by default in Create a spell and Add to a spellbook tab; the check warns
    when nobody would learn it and fixes it in one click.
  * Staged edits also go to the server's copy (backed up). The patch is checked and installed with a backup, and
    *Remove … from the game* undoes it.
* *Write to patch work folder* now adds to what that folder already holds instead of starting over.
* The "Server side" note on custom spells now says plainly that the server's own Spell.dbc (sent from Studio) is enough;
  a spell_dbc row is optional.

**Fixed: stage → review → apply works every time**
* Spell and other DBC edits never left the staged list after being sent out, so Changes kept showing them with no Apply
  button (DBC edits don't go through Apply). Now each one shows where it has been sent (server copy, patch folder); once
  it has gone both ways it moves to History by itself, and *Done: move to History* clears one you've sent just one way.
  Older staged files learn where their DBC edits went from the send log.
* The database Apply card is always shown. When nothing ticked is for the database, it says why in plain words.
* After an Apply (or Roll back) the editors, Loot, SmartAI, World tables and the SQL browser reload what's in the database.
  Before, a second edit of the same row recorded the old value as its starting point and the next Apply stopped.
* When a row changed since you staged an edit, Apply stops (nothing changes) and explains it in plain words, with choices:
  *Refresh starting point* (re-reads the row and keeps your new values), *Refresh and apply now*, *Skip it (untick)*,
  *Discard it* (for a row that's gone or already there) and *Show it*. The change is marked *needs a look* in the list.
* Two applies within the same second no longer share (and overwrite) one History file.
* History shows sent DBC edits with their server backup and a *Restore server DBC backup…* button.

**New look**: the whole app uses the studio suite's design (its token file, with AndersCraft's dark maroon and amber):
glass cards with a warm sheen and halo, glowing page titles with their underline, selections and hovers that glow and fade
instead of boxes, no outlines anywhere, pill buttons with a light sweep, a double keyboard focus ring, four drifting
glows and rising motes behind everything (still with Windows' reduced-motion setting, paused when minimized). Raw data
tables stay plain and readable. The design system lives in src/DesignSystem (a copy, so this repository builds on its own)
and DesignRulesTests keep screens from drawing outlines or their own colours.

**Fixed**: the 3D viewer's mouse wheel zooms without scrolling the page; a maximized window fills the screen above the
taskbar (any monitor, any taskbar side); the top bar keeps the window buttons on screen at narrower widths.

**Tests**: 192 automated tests (new: the client's real patch order; Send to game picking a name or updating the patch that
loads last; replacing files inside an archive keeps the rest byte for byte; a hidden copy in Data\patch-Z beating a visible
one in Data\enUS until Studio updates patch-Z; learned-automatically links; the design rules and tokens). Before that, 183 (new: a built quest with every kind of objective, read back; editing a quest stages only what
changed, copying adds a fresh one; a quest never removes a creature it only reads; a built NPC with look, services, place,
weapons, wares and greeting, read back, then a one-field edit; every behaviour makes valid rows that read as sentences;
plain names). Before that, 176 (new for Server control: SOAP messages, GM command building and cleaning, passwords
kept out of the audit log, the schedule, the crash watchdog, importing the old Control Panel's settings, read-only queries
with safe names, logs, audit and backups). Before that, 169 (new: a learnable spell gets a SkillLineAbility row that survives a write; the spellbook check
names each reason and goes green when wired; class tabs come from SkillLine + SkillRaceClassInfo; the guided recipe makes a
visible, linked spell of every kind, read back from bytes; Send to game picks a name that wins and replays sent edits
safely). Before that, 163 (new: M2 / .skin reading on real client models, mesh building, animation, particles and
ribbons on the client's spell effects, dressed NPC looks, WMO groups, script-row matching, DBC tabs; three applies in a row,
a restart, a fourth apply and a roll back on an in-memory table; a stale edit stopped and refreshed; sent DBC edits
leaving the staged list across restarts; an older staged file learning where its DBC edits went).

## 0.8.0 — no other tools needed

Studio now covers everything Keira3 and HeidiSQL do: a new user can set up a server's databases and edit all of its
content without any other program.

**New: Admin console** (sidebar › Database). Needs a MySQL admin login typed each time; Studio never saves it.
* SQL console: run any SQL directly, several statements and result sets, errors per statement, transactions with Commit /
  Roll back, session history (passwords masked).
* Databases & tables: create/drop databases; create tables from a column list; add/change/drop columns; add/drop indexes;
  rename, truncate, drop; views. A copy of the table is offered before risky changes.
* Import & export: run .sql files or whole folders (name order, DELIMITER-aware, progress, log, stop on error); export
  whole databases or chosen tables to .sql (restorable into an empty server) or CSV.
* Users & permissions (passwords masked), Connections (end a connection), Server (status and variables).
* **Set up AzerothCore**: creates the three databases and the server's account, loads the base tables, and shows the conf lines.
* Risky statements show the exact SQL and ask you to type the object's name; permanent ones (accounts, permissions,
  connections) are marked as such.
* The setup wizard can now run its account and sandbox scripts for you (*Run it now as admin…*).

**New: the rest of Keira3**
* Creatures: *Look & movement* (template addon + movement), resistances, controlled-spell list, kill reputation, editable
  models, per-spawn addon, formations, spell click, creature_text and quest items lists.
* Quests: *Texts* (request-items and reward texts with emotes); quest givers/enders are editable lists.
* Items: *Contains & enchants* (random enchants; item, disenchant, prospecting and milling loot).
* Game objects: quest items and per-spawn addon. New **Broadcast text** and **Teleport locations** editors.
* World tables: pools, spell areas (phasing), dungeon entry requirements, event/spell/waypoint scripts.
* Side tables a row doesn't have yet (e.g. creature_template_addon) are created on your first edit, as one INSERT with undo.

**Fixed**: some symbols (· › —) showed as garbled characters in the 0.7 README.

**Tests**: 145 automated tests.

## 0.7.0 — the last parity gaps

**New**
* **Game objects** editor: chests, doors, herbs, signs, portals and every other object type. Data0–Data23 are labelled
  for the object's type (all 36 types, with switches and name pickers where the value is a spell, quest, creature or page),
  plus spawns (editable list), loot (chests and fishing pools, opens the Loot editor), the quests it starts and accepts,
  faction / flags / money, and a SmartAI link.
* **NPC text** editor: what an NPC says when its gossip window opens: 8 variants with male/female text, chance, language,
  broadcast text and 3 emotes each, a *How it reads* preview, and who uses it. Gossip menus in World tables link to it.
* **Page text** editor: books, letters and plaques, with next-page chaining, the whole book read in order (loops and
  missing pages flagged), and the items and objects that show it. Items link to their page.
* **CSV import into database tables** (SQL browser › *Import CSV…*): map the CSV's columns, choose add / update / both,
  preview every change, then stage it — INSERTs for new rows and only the cells that changed, each with undo.
* **Item model import** (Asset import): .m2 + skins + textures into `Item\ObjectComponents\<Slot>\` and a staged
  ItemDisplayInfo row. It appears in the Items editor's Display picker as *icon · model*; Studio can stage it on an item.
* Ctrl+K finds game objects, NPC texts and pages by name or ID.

**Tests**: 137 automated tests (new: type-aware game object data, the new editors' fields against AzerothCore's schema,
CSV import mapping / staging / errors, item model rows).

## 0.6.0 — first public release

**New**
* **Home** dashboard: your server at a glance (connection, sandbox, Live lock, folders), staged changes by area, recent
  applies, quick links, and a *New to this? Start here* card.
* **Templates**: clone a creature as a stronger version · scale a creature to a level · make a vendor that sells items ·
  new quest from a template · make a creature cast a spell in combat. Each previews exactly what it will stage.
* **World tables**: conditions (rows read as sentences), waypoints/paths (with who walks them), gossip menus (with the
  text players read), game events (with their spawns), area triggers (teleport, scripts, quest exploration, taverns).
* **Asset import**: icons (PNG/JPG/BMP/TGA/BLP → 64×64 BLP with Studio's own BLP encoder: DXT1, DXT5 or palette), creature
  models (.m2 + .skin + textures → CreatureModelData + CreatureDisplayInfo rows), sounds (WAV/MP3 → SoundEntries) and any
  file, with a manifest per work folder. New icons appear in the Spell editor's picker immediately.
* **SQL browser**: Structure tab (columns, indexes, foreign keys, CREATE TABLE), multiple result sets, query history,
  open a .sql file, *Stage .sql script* (whole files become staged changes; session lines skipped), SQL export (CREATE +
  INSERTs), find text in every column.
* **DBC tables**: find & replace, bulk edit (set / add / multiply on the filtered rows), column definitions, compare server ↔
  client side by side, SQL export, save a copy as a new .dbc.
* **Spells**: export a spell (with its range, cast time, duration, radius, icon and visual rows) to a `.studio-spell.json`
  file and import it on another server; batch edit through DBC tables.
* **Help / Getting started** page (F1) with the safety model and a glossary; **About** box with licence and credits.
* Several server profiles: switch between them in Settings.

**Good to know**
* **Windows SmartScreen** may say *Windows protected your PC* the first time, because the exe isn't code-signed.
  Click **More info** › **Run anyway**. The source is in this repository if you'd rather build it yourself.

**Changed**
* No developer paths anywhere: changesets, backups and patch work folders default to `Documents\AndersCraft Studio`;
  mysqldump is found automatically in standard MySQL/MariaDB installs. Existing settings are kept.
* Every workspace that needs the database shows a friendly *not set up yet* page instead of an error.
* Self-contained Windows build (no .NET install needed) alongside the small framework-dependent one.

**Tests**: 128 automated tests (templates' SQL, BLP encoder round-trip, TGA, asset DBC rows, script staging, SQL dump,
DBC bulk tools, spell packages, and everything from earlier milestones). DBC round-trip: 245/245 of the server's DBCs
read and written back byte-identical.

## Earlier milestones (development)
* 0.5 — Spell editor with live tooltip, icon decoding, spell_dbc staging.
* 0.4 — DBC tables workspace for all 246 3.3.5a DBCs, DBC row patches, paged lists, creature_text editing.
* 0.3 — Loot workspace and simulator, SmartAI builder, editable spawns/equipment/vendor/trainer.
* 0.2 — Creature, item and quest editors; MPQ patching (build, install, browse, all codecs).
* 0.1 — Setup wizard, read-only SQL browser, changesets, backup-first Apply and roll back.
