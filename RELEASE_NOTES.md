# Release notes

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
