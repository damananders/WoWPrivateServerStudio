# Contributing / how to build

## Build

Requirements: Windows, the .NET 10 SDK.

```powershell
# run the tests (dependency-free runner; tests that need a server/client folder skip themselves)
dotnet run --project src\Tests -c Release

# framework-dependent single exe (needs the .NET 10 Desktop Runtime on the target PC)
dotnet publish src\App\AndersCraftStudio.csproj -c Release -o publish

# self-contained single exe (no runtime needed)
dotnet publish src\App\AndersCraftStudio.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish-sc
```

## Layout

* `src/Core` — everything that isn't UI: config parsing, SQL lexer/classifier, changesets, apply/rollback engine, DBC
  reader/writer/layouts, MPQ reader/writer/codecs, BLP decoder/encoder, spell tooltip, templates, asset import.
  Pure .NET, unit-testable.
* `src/App` — the WPF app (code-built UI, styles in `Theme.xaml`).
* `src/Tests` — the test runner (`[Test]` methods) plus read-only dev commands:
  `--dbc-roundtrip`, `--scan-client`, `--recover-names`, `--spell-tip <id>`, `--icons`.
* `tools/` — generators that turn an AzerothCore checkout into `DbcLayouts.txt` and `SpellCatalog.txt`:
  `python tools/gen_dbc_layouts.py <azerothcore-wotlk> src/Core/DbcLayouts.txt` and
  `python tools/gen_spell_catalog.py <azerothcore-wotlk> src/Core/SpellCatalog.txt`.

## Rules every change must keep

1. **Read-only by default.** Browsing uses the read-only account with `SET SESSION TRANSACTION READ ONLY`; new queries go
   through `SqlClassifier`. Never add a code path that writes to a database outside `ApplyEngine`.
2. **Stage, then apply.** Edits become `Change`s in the changeset with before/after and undo. No live-write shortcuts.
3. **Never overwrite originals.** Client and server game files are only changed by the user-triggered, backed-up Install /
   *Update server DBC copy* actions, never as a side effect.
4. **Write it yourself.** Don't copy code or help text from other editors (Keira3, HeidiSQL, WDBX, Spell Editor). Format
   constants and AzerothCore-generated layouts are credited in LICENSE.
5. **Explain it.** Every field and screen gets plain-English help; keep the Guide-card style.
6. Add tests for new Core logic; keep the suite green.

## Screenshots

`AndersCraftStudio.exe --screens <dir>` renders every screen off-screen with a throw-away settings folder (never applies or
installs); add `--fresh` to see what a brand-new user sees.
