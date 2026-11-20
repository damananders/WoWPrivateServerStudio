# Feature-parity audit

What the tools AndersCraft Studio replaces can do, and where Studio stands. Studio keeps its safety model everywhere:
**read-only by default, stage → review → apply with a backup, never overwrite originals**. Features that would need a
live-write shortcut are listed as *out of scope by design*.

Legend: ✅ has it · ➕ added in 0.6 · 🆕 added in 0.7 · 🟡 partly / different way · ❌ missing · ⛔ out of scope by design

## HeidiSQL (database admin)

| Feature | Studio | Notes |
|---|---|---|
| Several saved connections / sessions | ✅ ➕ | Server profiles from the setup wizard; 0.6 adds *Switch server* in Settings. |
| Browse databases, tables, views | ✅ | World, sandbox, characters (and auth if allowed) with table descriptions. |
| Data grid editing | ✅ | Cell edits are staged with undo, not written. |
| Query tab | ✅ | Reads run read-only; writes are staged. |
| Query history | ➕ | Last 100 queries per Windows user, searchable. |
| Multiple result sets | ➕ | Each SELECT in the editor gets its own result tab (up to 10). |
| Export grid / table to CSV (Excel) | ✅ | Whole filtered table or the result grid. |
| SQL dump export | ➕ | *Export SQL…*: CREATE TABLE + INSERTs for the filtered table, read with the read-only account. 🟡 One table at a time (no whole-database dump; Apply's mysqldump backups cover that). |
| Run a .sql file | ➕ | *Open .sql…* (into the editor) and *Stage .sql script…* (the whole file becomes staged changes; SET/LOCK/transaction lines skipped; GRANT/CALL etc. refuse the file). |
| Import data | 🆕 | *Import CSV…* on any world/sandbox table: map CSV columns to table columns, pick add / update / both, preview every change, then stage INSERTs and changed cells with undo (matched by the primary key, read with the read-only account). SQL via *Stage .sql script*. |
| Table structure (columns, indexes, foreign keys) | ➕ | *Structure* tab with CREATE TABLE. Read-only. |
| Find text across a table | ➕ | *Find text…* builds a filter over every column. |
| User / session info | ➕ | Home shows the account Studio browses as and the MySQL version. |
| Create / alter / drop tables, edit users, kill processes | ⛔ | Structure statements can be staged and exported, never applied; account management stays with your MySQL tools. |

## Keira3 (AzerothCore content editor)

| Feature | Studio | Notes |
|---|---|---|
| Creatures (template, spawns, equipment, addon) | ✅ | Form editor; spawns/equipment editable lists. |
| Items | ✅ | |
| Quests (incl. starters/enders, chains) | ✅ | |
| Vendors, trainers | ✅ | On the creature editor. |
| Loot (all *_loot_template) | ✅ | Plus a drop simulator. |
| SmartAI | ✅ | Sentences, checks, all event/action/target types. |
| creature_text | ✅ | From the SmartAI *Say a line* rows. |
| Gossip menus | ➕ | World tables › Gossip menus (gossip_menu + gossip_menu_option, npc_text preview, who uses it). |
| Conditions | ➕ | World tables › Conditions, every row read as a sentence, all source and condition types described. |
| Waypoints / paths | ➕ | World tables › Waypoints (waypoint_data by path, who walks it). |
| Game events | ➕ | World tables › Game events (event row + creature/object spawns). |
| Area triggers | ➕ | World tables › Area triggers (teleport, scripts, quest exploration, tavern; position from AreaTrigger.dbc). |
| spell_dbc | ✅ | From the Spell editor's Server side tab. |
| SQL preview / export of changes | ✅ | Changes › SQL / Undo SQL tabs and *Export SQL…*. |
| Game objects (gameobject_template + addon) | 🆕 | Form editor where Data0-Data23 are labelled for the object's type (all 36 types), with spawns (editable list), loot (chests and fishing pools, opens the Loot editor), quests it starts/accepts, faction/flags/money and a SmartAI link. |
| npc_text | 🆕 | Form editor: 8 variants with male/female text, chance, language, broadcast text and 3 emotes each, a *How it reads* preview, and who uses it (gossip menus, creatures, objects). Gossip menus link to it. |
| page_text | 🆕 | Form editor with next-page chaining, the whole book read in order (loops and missing pages flagged), and who uses it (items, readable objects, earlier pages). Items link to their page. |

## WDBXEditor (DBC / DB2)

| Feature | Studio | Notes |
|---|---|---|
| Open and edit DBC | ✅ | All 246 3.3.5a DBCs; edits are staged row patches. |
| Column definitions | ✅ ➕ | 152 verified layouts (+ raw mode); 0.6 adds the *Columns* view. |
| CSV export / import | ✅ | |
| SQL export | ➕ | CREATE TABLE + INSERTs for the filtered rows. |
| SQL import | 🟡 | Use CSV import (same columns). |
| Find / replace | ➕ | In one text column or all, on the filtered rows. |
| Mass edit | ➕ | *Bulk edit*: set / add / multiply a column on the filtered rows. |
| Compare two files | ➕ | *Compare…*: server ↔ client side by side, field by field. |
| Create a new DBC file | 🟡 | *Save copy as…* writes any table (with new rows) to a new file; the 3.3.5a client can't load new DBC types anyway. |
| Several files open at once | 🟡 | One grid at a time with instant switching; edits per file stay staged. |
| DB2 / newer formats | ⛔ | Not used by 3.3.5a. |

## MPQ tools

| Feature | Studio | Notes |
|---|---|---|
| Build an archive | ✅ | patch-X.MPQ, verified after building. |
| List / browse / view | ✅ | Any MPQ, with name recovery for archives without a listfile. 🟡 Viewing = extract; DBCs open in DBC tables. |
| Extract | ✅ | |
| Add / remove files in an existing archive | ✅ | *Start from an installed patch* copies its files to a work folder; add/remove there and rebuild (never modified in place). |
| Patch-chain simulation | ✅ | Load order, *which patch wins?* per file, *after installing* preview with shadowing warnings, client view in DBC tables. |
| All compression types | ✅ | zlib, bzip2, PKWARE implode, Huffman, ADPCM; v3/v4 ⛔ (Cataclysm+). |

## Spell editors (e.g. stoneharry's)

| Feature | Studio | Notes |
|---|---|---|
| Copy a spell | ✅ | *Duplicate as new* in the custom ID range. |
| Edit all fields | ✅ | 10-tab form for the meaningful fields; every column in DBC tables. |
| spell_dbc (server side) | ✅ | |
| Icon | ✅ ➕ | Icon picker from the client's BLPs; 0.6 adds importing your own icons. |
| Visuals | 🟡 | SpellVisual rows by ID and kit numbers; no 3D preview. |
| Mass / batch edit | ➕ | *Batch edit…* → DBC tables bulk edit / find & replace on filtered spells. |
| Export / import a spell to a file | ➕ | `.studio-spell.json` with the related range/cast/duration/radius/icon/visual rows. |
| Live tooltip | ✅ | Studio extra. |

## Asset import (new in 0.6, item models in 0.7)

| Asset | What Studio does |
|---|---|
| Icons (PNG/JPG/BMP/TGA/BLP) | Resizes to 64×64, encodes BLP (DXT5 / DXT1 / palette, with mips), places it in `Interface\Icons`, stages a SpellIcon.dbc row, optionally an ItemDisplayInfo row. Shown in the Spell editor's icon picker straight away. |
| Creature models (.m2 + .skin + textures) | Shows M2 info, copies to `Creature\<Name>\` (skins renamed, PNG/TGA textures converted), stages CreatureModelData + CreatureDisplayInfo rows. |
| Item models (.m2 + .skin + textures) | 🆕 Copies to `Item\ObjectComponents\<Slot>\` (weapon, shield, head, shoulder, quiver, ammo, pouch), converts textures, stages an ItemDisplayInfo row copied from a base display. It shows up in the Items editor's Display picker as *icon · model*; optionally stages it on an item too. |
| Sounds (WAV/MP3) | Preview playback, copies to `Sound\Custom\`, stages a SoundEntries row. |
| Any file | Copied to the archive path you choose, with a table of where each kind of file goes. |
| Manifest | `studio_manifest.json` in each work folder lists every imported file and its DBC rows. |

**Closed in 0.7:** game object, npc_text and page_text form editors; CSV import into database tables; item model import
with ItemDisplayInfo wiring.

**Known limits (by choice):** no 3D model or spell visual preview (Studio shows the IDs and file names); body armour
textures (chest, legs…) aren't covered by item model import; one DBC grid at a time; no DB2 formats (not used by 3.3.5a).
Account management and table structure changes stay with your MySQL tools (⛔ above).
