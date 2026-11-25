AndersCraft Studio 0.9 (3D model viewer)
========================================
End-user README: README.md. Licence: LICENSE (MIT). Changes: RELEASE_NOTES.md. Build: CONTRIBUTING.md.
Feature parity with HeidiSQL / Keira3 / WDBX / MPQ tools / spell editors: PARITY_AUDIT.md.
Distributable: dist\AndersCraftStudio-0.9.zip (self-contained) and dist\AndersCraftStudio-0.9-framework-dependent.zip.

VERSION 0.9
  - Core\M2.cs: clean-room 3.3.5a M2 + .skin reader, meshes, bones, keyframe tracks, .anim files, skinning, sphere-mapped layers.
  - App\ModelViewer.cs: WPF Viewport3D viewer + ModelLibrary (client archives + patch work folders, display DBCs).
  - Keyless tables: RowListEditor.MatchAllColumns, SqlGen.WhereKey matches fractions with ABS(...) < 0.0005.
  - DBC tabs: Core\OpenTabs.cs, DbcPage keeps per-table state.
  - Screens: --screens <dir> [--public] --m9 renders just the 0.9 screens.
  - Apply flow fixes: ChangesetStore.MarkDbcSent / MoveDbcToHistory / InferDbcSent (sent DBC edits leave the staged list),
    NewHistoryId (no same-second overwrite), ApplyEngine conflicts name the change (ConflictKind) + RefreshBaseline,
    Studio.DataVersion makes editors reload after Apply / Roll back. Tests: Tests\ApplyFlowTests.cs.
  - "How to use this" guides: App\Guides.cs (steps + marker positions), App\GuideWindow.cs, pictures in App\Guides\*.jpg
    made by tools\make_guides.py from a --public screenshot run (paints neutral text over private text boxes).
  - Screens: --m10 renders the Changes fixes and every guide.

VERSION 0.8
  - Admin console (AdminPage.cs, Core\Admin.cs): admin login per session, never saved; direct SQL, databases/tables/indexes/views,
    import .sql files/folders (SqlBatch splitter, DELIMITER-aware), export DB/tables (.sql/CSV), users/grants (masked), processlist/kill,
    status/variables, Set up AzerothCore (ServerSetup). Wizard: Run it now as admin (AdminRunWindow).
  - Keira3: side-table rows created on first edit (EditSession.StageAsNewRow, ColumnDefaults); creature addon/movement/resist/spells/
    kill rep/models/spawn addon/formations/spellclick/text/quest items; quest texts and givers; item enchants and loot; GO quest items
    and addon; broadcast_text and game_tele editors; World tables pools, spell_area, dungeon access, script tables.
  - Screens: --screens <dir> [--public] --m8 renders just the 0.8 screens.

VERSION 0.7
  - Game objects editor (type-aware Data0-23 for all 36 types, spawns, loot, quests, addon), NPC text (8 variants), Page text (chains).
  - SQL browser: Import CSV into a table (map, preview, stage INSERT/UPDATE with undo).
  - Asset import: item models -> Item\ObjectComponents\<Slot>\ + ItemDisplayInfo row (Display picker shows icon · model).
  - Screens: AndersCraftStudio.exe --screens <dir> [--public] --m7 renders just the 0.7 screens.

MILESTONE 6
  - Home dashboard, Help / Getting started (F1), About box, "not set up yet" pages, Settings › Switch server.
  - Templates: clone a creature stronger, scale a creature, vendor, quest from a template, combat spell (SmartAI).
  - World tables: conditions (sentences), waypoints, gossip menus, game events, area triggers.
  - Asset import: icons (BLP encoder: DXT1/DXT5/palette + mips; TGA reader), creature models, sounds, any file; manifest.
  - SQL browser: Structure tab, multiple result sets, query history, Open / Stage .sql script, Export SQL, Find text.
  - DBC tables: Find & replace, Bulk edit, Columns, Compare server ↔ client, Export SQL, Save copy as.
  - Spells: Export / Import a spell (.studio-spell.json with related rows), Batch edit via DBC tables.
  - Portable: default folders under Documents\AndersCraft Studio, mysqldump auto-detected, --fresh screenshots of a
    brand-new user. No developer paths in the app.

MILESTONE 5
  - Spells (sidebar > Client data > Spells): Spell.dbc (build 12340) as a tabbed form on top of the M4 DBC engine, from
    the server's Data\dbc or the client's merged view. Search by name or #ID (with icons), header with icon, name, #ID
    and rank; Duplicate as new (first free ID at or above the custom ID range, skipping Spell.dbc, staged rows and
    spell_dbc), Stage (Ctrl+S), Discard, Compare with original (a copy is compared with the spell it came from).
  - Tabs: Overview (name, rank, tooltip and buff text, school, dispel, mechanic, damage class, levels, stacks, missile
    speed) · Effects (3 cards: effect type and aura type dropdowns with descriptions, base points / die sides with the
    rolled range, per level, bonus coefficient, per combo point, targets A/B, radius, chain, tick interval, trigger
    spell with name and Open, item, misc values, class masks) · Targeting & range · Cast time & cooldown (cooldown,
    category cooldown, GCD) · Cost · Duration · Interrupts & procs (cast/aura/channel interrupt and proc tick boxes) ·
    Visuals (icon picker, SpellVisual) · Attributes (all 256 Attributes…AttributesEx7 bits as described tick boxes)
    · Server side.
  - Live tooltip preview: name, rank, cost (flat, % of base mana, rage/runic ×10), range, cast time / instant /
    channeled / passive, cooldown and the description with the client's tokens filled in ($s1 $m1 $M1 $o1 $t1 $d $a1
    $h $n $x1 $u $e1 $b1 $i $r, $12345s1, ${…} sums, $<variables> from SpellDescriptionVariables.dbc, $l/$g forms,
    $?…[…][…] → the "else" text). "What it does" lists each effect as a sentence ("Deals 14 to 22 Fire damage
    (selected enemy)"). Checked against real spells: Fireball, Renew, Frostbolt, Arcane Intellect, Backstab.
  - Related DBCs: range, cast time, duration, radius, icon, visual, category, rune cost and spell focus are shown by
    name ("35 · Medium-Long Range (0–30 yd)", "1.5 sec", "used by 4,211 spells"), with Pick… (search), New row… (a
    copy with your values, next free ID in the custom range, staged with the spell) and Open (DBC tables).
  - Icons: SpellIcon.dbc + the client's BLP textures through the MPQ reader (BLP2: palette with 0/1/4/8-bit alpha,
    DXT1/3/5, raw BGRA; Studio's own decoder). Decoded icons are cached in memory and on disk
    (%APPDATA%\AndersCraftStudio\icon_cache\<client fingerprint>\). All 3,214 icons the client has decode
    (22 SpellIcon.dbc entries point at textures the client doesn't ship). Paged, searchable icon picker.
  - Server side: a custom or new spell ID offers to stage INSERT INTO spell_dbc (AzerothCore loads that table as extra
    Spell.dbc rows), only with the columns the live table has, undo = DELETE; restaged automatically when you stage the
    spell again. The tab shows whether the spell is in Spell.dbc / spell_dbc and counts its rows in spell_custom_attr,
    spell_proc, spell_script_names, spell_bonus_data, spell_ranks and spell_linked_spell, with a Guide card.
  - All edits are DBC row patches (Spell.dbc + related DBCs) and a world-DB change (spell_dbc), shown on Changes and
    sent out with the same two outputs / the normal Apply.
  - Spell names and IDs are also found with Ctrl+K.

MILESTONE 4
  - DBC tables (sidebar > Client data > DBC tables): every one of the 246 3.3.5a (build 12340) DBCs, each with a
    one-line description, searchable. 152 have named, typed columns (int / uint / float / text / localized text / byte /
    flags) with hints; the other 93 open in raw mode with the types guessed from the data (text, localized text,
    decimals and negative numbers are recognized). Every layout is checked against the real file before it is used.
  - Paged grid (Settings page size), filter (text, numbers, Column:part, Column=value, Column>n / <n, several terms),
    find by ID (Ctrl+G), edit cells (bad values refused with the reason), add / duplicate (next free ID at or above the
    custom ID range) / remove rows, CSV export (filtered rows) and import (update or add by ID; all-or-nothing).
  - Localized text shows English; "All locales" adds the other 15 languages and the language mask.
  - Foreign keys: ~90 link rules (Spell → SpellIcon / SpellRange / SpellCastTimes …, AreaTable → Map, items and
    creatures → the world database). A "→ name" column next to each link, and buttons under the grid that open the
    linked DBC row or the Creature / Item editor. Tables without text are summed up by their first values.
  - Sources: Server (worldserver.conf DataDir\dbc) or Client ("what the game sees": each DBC from the archive that wins
    in the client's load order, with the archives it wins over listed). "Only rows that differ" compares the two.
  - Staging: edits become DBC row patches in the changeset (field-level before/after, undo, diff on Changes with what
    each field means). They never go to the database and never overwrite an original.
  - Changes > Client data card, two user-triggered outputs, each checks every row against the staged "before" values
    and writes nothing on any mismatch: (1) patched DBCs into a Client patches work folder (DBFilesClient\…), starting
    from the client's current copy; (2) the server's Data\dbc copy updated after a backup to
    <BackupRoot>\dbc_<date_time> (typed UPDATE DBC), with Restore buttons (typed RESTORE).
  - String blocks: unchanged files are written back byte for byte; edited text is appended (raw mode) or the block is
    rebuilt compactly (verified layouts).
  - Round-trip on the server's Data\dbc (2026-11-19, temp copies): 245/245 DBCs read and written back byte-identical;
    with a forced string-block rebuild every value of all 245 stays identical (239 even byte-identical). The 246th,
    CharVariations.dbc, is a 0-byte file. Run it yourself: AndersCraftStudio.Tests.exe --dbc-roundtrip [<dbc folder>]
  - M3 fixes: editable lists are paged (500 rows a page, unstaged edits staged or dropped when changing page, new keys
    checked against the whole table), so there is no row cap any more. SmartAI "Say a line" / "Make target say" /
    "Finished talking" rows show the creature_text lines of that group, editable and staged with undo (spawn scripts use
    the spawn's template); the "unknown text group" check counts staged lines.

MILESTONE 3
  - Loot (sidebar > World > Loot): all 12 *_loot_template tables. Search by entry or owner name (creature, object,
    item), "Used by" line, rows grouped into "rolled on their own" and Group N cards with a plain-English rule
    ("at most one drops; 70% of kills get nothing"), shared lists (references) with Open › drill-in and a back trail,
    an explanation card (Chance / GroupId / Min-MaxCount / Reference / QuestRequired), an editable row grid with item
    names, and a drop simulator (N kills, quest toggle) that follows the server's rules and uses unstaged edits.
  - SmartAI (sidebar > Scripting > SmartAI): creature, game object or action-list scripts. Each row reads as a
    sentence ("When in combat, cast Fireball (133) on its victim, every 8-12s"). Event / Action / Target dropdowns
    with a description per type and labelled parameters (names looked up for spells, creatures, quests, items),
    chance, 12 phase tick boxes, event-flag tick boxes, x/y/z/o, link and comment. Checks panel: missing link targets,
    "Linked" rows nobody links to, unknown spells/creatures/quests/items/action lists/text groups, 0% chance,
    min > max timers, unknown or unsupported types. Every event, action and target in AzerothCore's
    SmartScriptMgr.h is described (a test enforces this). Wording is Studio's own.
  - Creature editor lists are now editable and staged with undo: Spawns (map/zone/area names, Duplicate row makes a
    new spawn with the next free guid), Equipment, Vendor items (with ExtendedCost) and Trainer spells.
  - All list editing goes through one engine (RowDiff): removed rows → DELETE, new rows → INSERT, changed cells →
    UPDATE, key changes → DELETE + INSERT, layered on earlier staged changes (re-editing a staged row updates it).
Safe, staged editing for AzerothCore 3.3.5a world databases, plus MPQ patching for the game client.
WPF, .NET 10, framework-dependent single exe.

Exe:         publish\AndersCraftStudio.exe   (framework-dependent: needs the .NET 10 Desktop Runtime; publish-sc\ = self-contained)
Source:      src
               Core\   engine: conf parser, SQL lexer/classifier, SQL + undo generation, changesets, apply engine,
                       mysqldump backup, entity field definitions, DBC reader/writer/layouts/patches, MPQ reader/
                       writer/decompressors
               Core\DbcLayouts.txt  generated by tools\gen_dbc_layouts.py from an AzerothCore checkout (don't edit)
               Core\DbcLayoutsExtra.txt, DbcDescriptions.txt  Studio's own layouts and descriptions
               Core\SpellCatalog.txt  generated by tools\gen_spell_catalog.py (spell enum values and names)
               Core\SpellTexts.txt    Studio's own spell wording (labels, descriptions, summary sentences)
               App\    WPF UI (code-built, Theme.xaml for styles)
               Tests\  dependency-free unit tests:  dotnet run --project src\Tests -c Release
                       client survey (read-only):    AndersCraftStudio.Tests.exe --scan-client <Data> [--all] [--extract]
                       name recovery (read-only):    AndersCraftStudio.Tests.exe --recover-names [<cache dir>]
                       DBC round-trip (temp copies): AndersCraftStudio.Tests.exe --dbc-roundtrip [<dbc folder>]
                       spell tooltips (read-only):   AndersCraftStudio.Tests.exe --spell-tip <id> [<id> …]
                       client icons (read-only):     AndersCraftStudio.Tests.exe --icons [<client Data folder>]
Build:       dotnet publish src\App\AndersCraftStudio.csproj -c Release -o publish
Dev paths:   tests that read a real server/client use dev.local.json at the repo root (git-ignored) or the
             STUDIO_SERVER_DBC / STUDIO_CLIENT_DATA / STUDIO_AZEROTHCORE environment variables; they skip when unset.
Settings:    %APPDATA%\AndersCraftStudio\settings.json, profiles\<name>.json  (passwords DPAPI, current Windows user)
             %APPDATA%\AndersCraftStudio\name_cache\   recovered file names for patches without a (listfile)
             %APPDATA%\AndersCraftStudio\installed_patches.json   patches Studio installed (for uninstall)
Changesets:  Documents\AndersCraft Studio\changesets  (pending.json + applied_<id>.json history; folder configurable)
Backups:     Documents\AndersCraft Studio\backups\<yyyy-MM-dd_HHmmss>\  (mysqldump before every Apply / Roll back)
             Documents\AndersCraft Studio\backups\dbc_<date_time>\   (server DBC files replaced by "Update server DBC copy")
             Documents\AndersCraft Studio\backups\client_<date_time>\   (client patch files replaced or removed)
Patches:     Documents\AndersCraft Studio\patch_work\<name>\  (work folders)   ...\patches\  (built patches)
Screens:     docs\screens\*.png (public set);  AndersCraftStudio.exe --screens <dir> [--public] [--fresh] renders them
Dependency:  MySqlConnector 2.6.2 (MIT). Everything else is written from scratch.
Credits:     The PKWARE DCL code tables, the Storm Huffman byte-weight tables and the ADPCM step tables are format
             constants, taken as documented by StormLib (MIT licence, (c) Ladislav Zezula). Studio's decompressors
             themselves are its own code.
             DBC column names and types for the 114 tables the server loads are generated from
             AzerothCore's DBC definitions (DBCfmt.h, DBCStructure.h, data/sql/base/db_world/*_dbc.sql; GPL-2,
             AzerothCore project). Descriptions, extra layouts, link rules and all code are Studio's own.
             Spell enum values/names (effects, auras, targets, attributes, flags) and the attribute TITLE notes are
             generated from AzerothCore's SharedDefines.h, SpellAuraDefines.h, SpellDefines.h and SpellMgr.h.

FIRST RUN (what you do)
  1. Start the exe. The setup wizard opens.
  2. Step 2: Auto-detect or Browse to worldserver.conf.
  3. Step 3: copy the generated script and run it ONCE yourself as MySQL root (HeidiSQL / Workbench / mysql CLI).
     It creates the read-only browse account and the apply-only account. Then press "I ran it - Test the accounts".
     Browsing unlocks only after that test passes. Passwords are masked on screen (Show button); Copy/Save use them.
  4. Step 5 (recommended): "Create sandbox..." builds a copy-the-world-DB script; run it yourself as root.
     Apply targets the sandbox by default. Live stays locked until Settings > Safety > Allow applying to Live.

SAFETY MODEL
  - Browsing/queries: read-only account only, every session SET SESSION TRANSACTION READ ONLY, and every statement is
    re-checked by the classifier. Write statements and cell edits are staged into the changeset, never run.
  - Blocked outright: GRANT/SET/CALL/LOAD/USE/transactions, SELECT ... INTO, EXPLAIN ANALYZE of writes,
    /*! executable comments */, multiple statements in one filter.
  - Structure statements (CREATE/ALTER/DROP/TRUNCATE/RENAME) are recorded but Studio refuses to apply them.
  - Apply: backup first (always), typed confirm word (APPLY / APPLY LIVE), one transaction on the apply account, each row
    re-read under lock and compared with the staged "before" value; any mismatch rolls everything back.
  - Typed UPDATE/DELETE get undo captured from the live rows (again inside the Apply transaction); INSERT with
    literal keys gets DELETE-by-key undo; anything else is marked "no auto-undo" (the backup is the way back).
  - Roll back: fresh backup, typed ROLL BACK, undo runs newest-first, stops if a row changed after Apply.
  - Client patches: building writes only into Studio's own folders. Install / Uninstall are the only actions that touch
    the game folder: typed INSTALL, backup of any replaced file, refused while the game client is running.

MILESTONE 2 (Creatures / Items / Quests editors)
  - Form editors with search list, header (name + #ID), Duplicate as new, Stage edits (Ctrl+S), Discard, cross-links.
    Creatures: Basics, Stats & combat, Spawns, Equipment, Vendor/trainer, Loot (read-only list), SmartAI (read-only list).
    Items: Basics, Stats, Weapon/armor, Requirements, Spells, Prices, Display.  Quests: Basics, Objectives, Rewards,
    Starters/enders, Chain.
  - Every field: plain label, "Column: x" hint, Guide card (game-terms explanation + tip). Help text: src\Core\EntityDefs.cs.
  - Bitmasks = tick boxes (unknown bits preserved), enums = dropdowns, foreign IDs = name pickers (DB rows or the server's
    Data\dbc files, read-only). Edited fields get an amber outline with "Was X"; the SQL preview card shows UPDATE + undo.
  - Duplicate as new: first free ID >= Settings > Custom ID range (default 900000), skipping IDs already staged; staged
    INSERT with DELETE undo (quest_template_addon row copied too).
  - Ctrl+K finds creatures, items and quests by name or #ID.
  - Older databases where creature.id is not yet id1 are detected automatically.

CLIENT PATCHES (sidebar > Client data > Client patches)
  - Build a patch: work folder -> patch-X.MPQ (zlib, (listfile)), every file read back and compared before it's kept.
    "Start from an installed patch" copies an existing patch's files out first, so it is changed rather than rebuilt.
    "Add server DBC" copies DBCs from the server's Data\dbc. Letter check (suggests the latest free letter, warns on
    clashes), per-file "today the client uses / after installing" with a warning when a later patch still wins.
  - Install with backup, one-click Uninstall (restores the previous file), Export zip for players.
  - Installed patches: load order, "which patch wins?" lookup, Recover names for patches without a (listfile).
  - Browse an MPQ: any archive, read-only, filter, extract. Files without a known name show as "unknown #index.ext".
  - Decompression: zlib, bzip2, PKWARE DCL implode (binary + ASCII), Storm Huffman, ADPCM mono/stereo, files with a
    smaller sector size than the archive header. Verified: all 29 archives of a full 3.3.5a client, 324,011 files,
    0 failures (2026-11-20).
  - Name recovery on the client (2026-11-20): patch-X 29/29 (100%), patch-T 1,732/1,809 (95.7%), patch-L 406/479 (84.8%).

KNOWN ISSUES (kept current)
  1. Storm Huffman and ADPCM decoding are verified by round-trip tests only: no file in this client uses them
     (its audio is zlib-compressed). bzip2 and the sector-size case are verified on real client files.
  2. Name recovery can't find every name: 73 files in patch-L and 77 in patch-T (mostly .blp textures) stay
     "unknown #...". They can still be extracted. Recovery on patch-T takes about 1.5 minutes (cached afterwards).
  3. MPQ format v3/v4 (Cataclysm and later: HET/BET tables, LZMA) isn't supported; the 3.3.5a client doesn't use it.
  4. Building patches > 4 GB isn't supported (format v1).
  5. Apply, Roll back, Install, Uninstall and the two DBC outputs (patch work folder, server DBC copy + Restore) have
     never been executed against the real database / client / server folders during development (by design); they're
     covered by unit tests against fakes and temp folders.
  6. Item icons and models aren't rendered yet (BLP/M2 viewers are planned).
  7. 93 of the 246 DBCs have no published layout and open in raw mode: columns are named Field<n> / Text<n> and their
     types are guessed from the values (a column that is all 0 shows as uint; a number column whose values happen to
     look like text offsets could be shown as text). Raw-mode text edits are appended to the string block so nothing
     else moves.
  8. The client "what the game sees" view uses Studio's load-order model: base archives, then locale archives, then
     patch, patch-<locale>, patch-2, patch-<locale>-2 … patch-9, then patch-A … patch-Z (later wins). The order between
     a Data patch and its locale twin follows the usual 3.3.5a convention; it hasn't been checked inside Wow.exe.
  9. DBC edits are staged against the copy shown when you stage (server or client). If the other copy differs, staged
     rows that don't fit it are listed and left out of the grid; outputs refuse rows whose "before" doesn't match.
 10. DBC tables without an ID column (the gt*.dbc game tables) can only get rows added or removed at the end.
     The DBC grid can't sort by column (use the filter).
 11. The drop simulator uses normal drop rates (Rate.Drop.* = 1) and ignores the conditions table; quest-only drops
     are on/off with the "player is on the quests" toggle rather than per quest.
 12. SmartAI: per-spawn scripts are opened by typing -guid; cast flags and NPC/unit flags in action parameters are
     entered as numbers (the cast flags are named below the box). Action lists can't show creature_text lines (they
     don't belong to one creature).
 13. Trainer spells are shared by every creature linked to the same trainer list; Studio says so but doesn't list them.
 14. Spell tooltip preview: tokens that depend on the player ($AP, $SP, talents and auras in $?…) can't be known, so
     ${…} sums using them show "…" and $?conditions take the "else" text. $l picks the plural form. A few rare
     tokens are left as written. The "What it does" sentences have Studio wording for ~60 effects and ~80 auras; the
     rest use the effect/aura name.
 15. Spell editor fields that most users never touch (other locales, totems, reagents, equipped-item requirements,
     shapeshift masks, required areas) are edited on DBC tables (Open in DBC tables) rather than in the form.
 16. Icons from the client need the game client folder (icons you import show without it); JPEG-compressed BLPs (not
     used by 3.3.5a icons) aren't decoded.
 17. A spell_dbc row that already exists is shown but not edited in the Spell editor (use the SQL browser), and the
     Spell editor's staged spell_dbc INSERT uses the columns of the world database at staging time.
 18. Asset import: models must already be 3.3.5a .m2 files (version 264); Studio doesn't convert models. Item models,
     spell visual kits and map tiles can be imported as files but aren't wired into DBCs automatically. Re-importing an
     icon under the same name may show the old picture in the Spell editor until Studio restarts (icon cache).
 19. SQL browser: Export SQL works one table at a time; there's no CSV import into database tables (stage a .sql script).
 20. Templates copy a creature's side rows that exist on your server (model, addon, equipment, resistance, spells,
     SmartAI); C++ ScriptNames are copied too and may need clearing (the preview says so).
 21. Gameobject templates, npc_text and page_text have no dedicated forms yet (SQL browser; previews in Gossip menus).

KEYS  Spells: Ctrl+S stage · Ctrl+D duplicate as new · Ctrl+PgUp/PgDn tabs
      DBC tables: Ctrl+F filter · Ctrl+G find ID · Alt+PgUp/PgDn page · Ctrl+S stage · Ctrl+D duplicate row
      Ctrl+K search · Alt+1..9 sidebar · F5 / Ctrl+Enter run query · F2 edit cell · Ctrl+S stage form edits ·
      Ctrl+D duplicate as new · Ctrl+PgUp/PgDn editor tabs · Ctrl+Shift+R review · Ctrl+, settings

DEV FLAGS (not for users)
  --screens <dir> [--demo-conf]   render every screen off-screen to PNG using a throw-away settings/changesets folder,
                                  using your verified read-only account if setup is done (otherwise --demo-conf reads
                                  through the worldserver.conf login, forced READ ONLY). Never applies or installs.
  --make-icon <file.ico>          regenerate the crest icon.
