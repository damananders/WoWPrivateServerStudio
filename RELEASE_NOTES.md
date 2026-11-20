# Release notes

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
