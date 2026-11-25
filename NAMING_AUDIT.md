# Naming audit (0.9)

Rule: everything a user reads has a plain-English name and a short description. Engine names (tables, columns, DBC
fields, flag bits, command text, raw numbers) only show when **Settings › Show technical names** is on (off by default).

## Changed in this pass

| Where | Before | Now |
|---|---|---|
| Creature / item / quest / object editors | `Column: creature_template.minlevel` under every field, table name pill, column in tooltips | Hidden; shown only with technical names |
| Editor dropdowns (rank, quality, quest type…) | `3 · Rare (blue)` | `Rare (blue)` (number only with technical names) |
| Editor flag tick boxes | Tooltip `Bit 0x80 (128)` | No bit tooltip unless technical names are on |
| Editor header | `#197` + `creature_template` | `#197` ("its number") + "from the game" marker for Blizzard's records |
| Lists (loot, spawns, vendor, creature text, scripts, world tables…) | Column headers `QuestRequired`, `spawntimesecs`, `event_param2` | "Only for players on the quest", "Respawn time (seconds)", "When: setting 2" (raw name in brackets with technical names) |
| Changes screen | Field column `minlevel`, `creature_template · entry=69` | "Lowest level", "Creature · entry=69" |
| SmartAI | Event / action / target lists `22 · Receives an emote`; parameter tooltips `(param 1)`; sentences `Fireball (133)` | `Receives an emote`; no parameter numbers; `Fireball` |
| Spell editor | Dropdowns `6 · Apply aura`; flag help `Bit 0x80 of Attributes`; guide card `Column: …` | Plain label; raw bits / columns only with technical names |
| World tables (conditions) | `14 · Gossip menu` | `Gossip menu` |
| Top bar | `acore_world · read-only` | `World database · read-only` |
| Templates | `Every (min ms)`, `Money reward (copper)` | `Every, at the soonest (thousandths of a second)`, `Money reward, in copper` with hints |
| Server control (new) | — | Plain from the start: "command port" for SOAP, "staff rank" for GM level, quick commands by name, raw command text only with technical names |
| Builders (new) | — | Plain throughout: kinds, sides, strengths, services, objectives, rewards, behaviours |

The plain names come from `Core/PlainNames.cs` (tables and columns) and the existing catalogues (`EntityDefs`,
`SpellCatalog`, the SmartAI catalogue). Unknown columns are made readable ("RequiredSkillPoints" → "Required skill points").

## Technical by design

These screens exist to show the raw data, so their names stay as the database or client has them (with descriptions
next to them where Studio knows one):

* **SQL browser** and **Admin console**: raw tables, columns and SQL.
* **DBC tables**: the client's raw tables (the column list shows a short description for each field).
* **Client patches**: archive paths inside MPQ files.

## Still to do (next passes)

* The visual restyle pass will also re-check wording on the older screens (Home, Settings, Client patches, Asset import).
* Guide pictures of the record editors were taken before the "Column:" lines were hidden; they're refreshed in the restyle pass.
