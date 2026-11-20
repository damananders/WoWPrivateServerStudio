"""Dev-time generator: builds src/Core/DbcLayouts.txt from an AzerothCore checkout.

Sources (facts only, no code copied):
  data/sql/base/db_world/*_dbc.sql          full column lists (names + SQL types) of the DBCs the server loads
  src/server/shared/DataStores/DBCfmt.h     per-file column format strings (cross-check of column count / byte columns)
  (DBCStructure.h comments are NOT copied: Studio stays MIT; column notes are Studio's own, see DbcLayoutsExtra/Links)
  src/server/game/DataStores/DBCStores.cpp  store -> struct -> file name

Output format (one block per file):
  @File.dbc
  Name|type|hint            type: u uint, i int, f float, s string, L localized string (17 columns), b byte, B byte-or-int (decided by record size), x flags (uint shown hex)
Run:  python tools/gen_dbc_layouts.py <azerothcore-wotlk> src/Core/DbcLayouts.txt
"""
import os, re, sys

LOCALES = ["enUS", "enGB", "koKR", "frFR", "deDE", "enCN", "zhCN", "enTW", "zhTW", "esES", "esMX", "ruRU", "ptPT", "ptBR", "itIT", "Unk"]

def read(p):
    with open(p, encoding="utf-8", errors="replace") as f:
        return f.read()

def main(ac, out):
    shared = os.path.join(ac, "src/server/shared/DataStores")
    fmt_src = read(os.path.join(shared, "DBCfmt.h"))
    fmts = dict(re.findall(r'char constexpr (\w+)\[\]\s*=\s*"([^"]*)"', fmt_src))
    stores_src = read(os.path.join(ac, "src/server/game/DataStores/DBCStores.cpp"))
    store_struct = {}
    for struct, store, fmt in re.findall(r'DBCStorage\s*<\s*(\w+)\s*>\s*(\w+)\s*\(\s*(\w+)\s*\)', stores_src):
        store_struct[store] = (struct, fmt)
    file_store = {}
    for store, file in re.findall(r'LOAD_DBC\(\s*(\w+)\s*,\s*"([^"]+)"', stores_src):
        file_store[file] = store
    for store, file in re.findall(r'LOAD_GT\(\s*(\w+)\s*,\s*"([^"]+)"', stores_src):
        file_store[file] = store

    # struct hints: struct -> {col: hint}
    hints = {}
    cur = None
    for line in read(os.path.join(shared, "DBCStructure.h")).splitlines():
        m = re.match(r'\s*struct (\w+)\s*$', line)
        if m and line.startswith("struct"):
            cur = m.group(1); hints[cur] = {}; continue
        if cur is None: continue
        if line.startswith("};"): cur = None; continue
        m = re.search(r'//\s*(\d+)(?:\s*-\s*(\d+)(?=[\s,;]|$))?\s*(.*)$', line)
        if not m: continue
        a = int(m.group(1)); b = int(m.group(2) or a)
        if b < a: b = a
        note = re.sub(r'^[-:]\s+', '', m.group(3).strip())
        decl = line.split("//")[0].strip() or line.strip().lstrip("/").split("//")[0].strip()
        nm = re.search(r'(\w+)\s*(?:\[[^\]]*\])?\s*;', decl)
        member = nm.group(1) if nm else ""
        if re.fullmatch(r'm_\w+|\w+', note or ""): note = ""     # bare identifiers say nothing new
        text = note + (" (AzerothCore name: %s)" % member if note and member else "") if note else ("AzerothCore name: " + member if member else "")
        for c in range(a, b + 1):
            hints[cur].setdefault(c, text)

    sqldir = os.path.join(ac, "data/sql/base/db_world")
    blocks = []
    for file, store in sorted(file_store.items(), key=lambda kv: kv[0].lower()):
        struct, fmtname = store_struct.get(store, (None, None))
        fmt = fmts.get(fmtname, "")
        sqlname = os.path.splitext(file)[0].lower() + "_dbc.sql"
        p = os.path.join(sqldir, sqlname)
        cols = []
        if os.path.exists(p):
            body = read(p)
            m = re.search(r'CREATE TABLE `\w+` \((.*?)\n\)', body, re.S)
            for cm in re.finditer(r'^\s*`([^`]+)`\s+([a-z]+(?: unsigned)?)', m.group(1), re.M):
                cols.append((cm.group(1), cm.group(2)))
        else:
            # no SQL definition (e.g. gtOCTRegenMP): derive from the format string
            cols = [("ID" if i == 0 and ch in "nd" else "Field%d" % i, {"f": "float", "s": "varchar", "b": "tinyint unsigned", "X": "tinyint unsigned"}.get(ch, "int")) for i, ch in enumerate(fmt)]
        if fmt and len(fmt) != len(cols):
            print("warn: %s fmt %d cols vs sql %d" % (file, len(fmt), len(cols)), file=sys.stderr)
        h = hints.get(struct, {})
        lines = ["@" + file]
        i = 0
        while i < len(cols):
            name, t = cols[i]
            if name.endswith("_Lang_enUS") and i + 16 < len(cols) and cols[i + 16][0].endswith("_Lang_Mask"):
                base = name[:-len("_Lang_enUS")]
                lines.append("%s|L|" % base)
                i += 17; continue
            ch = fmt[i] if i < len(fmt) else "x"
            if t in ("varchar", "text", "tinytext"): ty = "s"
            elif t == "float": ty = "f"
            elif ch in "bX": ty = "b"
            elif t.startswith("tinyint"): ty = "B"   # byte or int: the server's table can't tell; Studio checks the record size
            elif t == "int unsigned" or t == "bigint unsigned": ty = "x" if re.search(r'flag|mask|^attributes', name, re.I) else "u"
            else: ty = "x" if re.search(r'flag|mask|^attributes', name, re.I) else "i"
            if ch == "f" and ty != "f": ty = "f"
            if ch == "s" and ty != "s": ty = "s"
            hint = ""   # facts only: column names and types. AzerothCore's comment text is not copied (licence).
            if hint.lower() == ("AzerothCore name: " + name).lower(): hint = ""
            lines.append("%s|%s|%s" % (name, ty, hint))
            i += 1
        blocks.append("\n".join(lines))
    with open(out, "w", encoding="utf-8", newline="\n") as f:
        f.write("# Generated by tools/gen_dbc_layouts.py from an AzerothCore checkout. Do not edit by hand.\n")
        f.write("\n\n".join(blocks) + "\n")
    print("files:", len(blocks))

if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
