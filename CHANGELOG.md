# Changelog

Full notes for each version are in [RELEASE_NOTES.md](RELEASE_NOTES.md).

## 0.9.0
3D viewer for creatures (dressed humanoid NPCs included), game objects and WMO buildings, items and spell effects with
particles; animations; body-armour texture import; editable event/spell scripts; DBC tabs; friendlier error messages, a
log for problem reports, recovery of a damaged staged-changes file; MariaDB backup fix. Fixed: sent DBC edits stayed
staged forever with no way out, and editors kept pre-Apply values (so a second edit of the same row stopped Apply);
Apply is always shown, mismatches are explained with choices, two applies in one second no longer share a History file.
New "How to use this" visual guides for every main screen (Shift+F1).
Spells: guided Create a spell, a "Will it show in the spellbook?" check with fixes (SkillLineAbility link, hidden/passive
flags, client out of date), and Send to game (one patch that wins over the client's other patches, installed with a
backup). Fixed: each send to the patch work folder started over from the client's copy and dropped earlier edits.
Server control: the AndersCraft Control Panel folded in as a second, live tab, beefed up (CPU / memory, crash watchdog,
maintenance mode, accounts, bulk player actions, quick commands, live coloured logs, scheduled messages / restarts / backups,
backup restore). Settings › Show technical names (off by default).
Step-by-step builders for quests, NPCs and creature behaviours (new, yours, or the game's: copy or change), with live
previews, all staged; plain names across the editors, lists, Changes and SmartAI (NAMING_AUDIT.md).
New look across every screen (the studio suite's design, AndersCraft colours). Fixed: Send to game now follows the
client's real patch order (Data\patch-X beats every Data\enUS patch) and updates the patch that loads last when nothing
can outrank it; spells can be learned automatically at login; the 3D viewer's wheel no longer scrolls the page; a
maximized window stays above the taskbar. Six colour schemes (Settings › Look): Andersian, Frostforge, Verdant, Arcane,
Emberforge, Obsidian.

## 0.8.0
Admin console (everything HeidiSQL does, including setting up AzerothCore's databases) and the rest of Keira3's editors.
Setup wizard can run its scripts itself.

## 0.7.0
Game objects, NPC text and page text editors; CSV import into database tables; item model import.

## 0.6.0
First public release.
