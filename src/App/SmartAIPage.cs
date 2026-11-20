using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AndersCraftStudio.Core;

namespace AndersCraftStudio
{
    /// <summary>SmartAI builder: a script's rows as "When … , do … on …" sentences, edited as Event → Action → Target.</summary>
    public sealed class SmartAIPage : IPage
    {
        readonly MainWindow w;
        Studio S => w.S;
        readonly Refs refs;
        readonly Grid view = new Grid();
        public UIElement View => view;

        int sourceType;
        long entry;
        List<RowValues> baseline = new List<RowValues>();
        List<SmartRow> rows = new List<SmartRow>();
        int sel = -1;
        readonly Dictionary<(SP, long), bool?> existsCache = new Dictionary<(SP, long), bool?>();

        readonly StackPanel sourceBar = new StackPanel { Orientation = Orientation.Horizontal };
        readonly TextBox idBox = U.Box("", true);
        readonly TextBlock entityName = U.T("", 13, "Amber", FontWeights.SemiBold, false, new Thickness(12, 0, 0, 0));
        readonly ListBox list = new ListBox();
        readonly ContentControl editorHost = new ContentControl { Focusable = false };
        readonly StackPanel issuesPanel = new StackPanel();
        readonly TextBox sqlBox = U.Code("", 220);
        readonly TextBlock sentence = U.T("", 14, "Text", FontWeights.SemiBold, true);
        Grid body;

        public SmartAIPage(MainWindow w)
        {
            this.w = w; refs = new Refs(w.S);
            view.Margin = new Thickness(28, 22, 28, 20);
            view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            view.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
            var strip = U.StepStrip(1); DockPanel.SetDock(strip, Dock.Right); strip.VerticalAlignment = VerticalAlignment.Center; head.Children.Add(strip);
            head.Children.Add(U.Stack(U.H1("SmartAI"), U.T("Build behaviour as plain sentences: when something happens, do something, to someone. Changes are staged with undo.", 13.5, "Muted")));
            view.Children.Add(head);

            // ---- which script
            foreach (var kv in new[] { (0, "Creature"), (1, "Game object"), (9, "Action list") })
            {
                var rb = new RadioButton { Style = U.S("SegBtn"), Content = kv.Item2, GroupName = "smartsrc", IsChecked = kv.Item1 == 0, Tag = kv.Item1,
                    ToolTip = kv.Item1 == 0 ? "Scripts on a creature template (entry) or one spawn (minus its guid)." : kv.Item1 == 1 ? "Scripts on a game object." : "Timed action lists, started with the action \"Run action list\"." };
                rb.Checked += (s, e) => sourceType = (int)((RadioButton)s).Tag;
                sourceBar.Children.Add(rb);
            }
            idBox.Width = 120;
            idBox.ToolTip = "Creature entry (or -guid for one spawn), game object entry, or action list ID. Enter loads it.";
            idBox.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter && long.TryParse(idBox.Text, out var v)) Open(sourceType, v); };
            var pick = U.Btn("Pick…", () =>
            {
                var kind = sourceType == 1 ? "gameobject" : "creature";
                var dlg = new PickerWindow(w, "Pick a " + kind, "Choose whose script to open.", q => refs.Search(kind, q), "");
                if (dlg.ShowDialog() == true && dlg.Picked != null) Open(sourceType, dlg.Picked.Value);
            }, null, "Search by name.");
            var load = U.Btn("Load", () => { if (long.TryParse(idBox.Text, out var v)) Open(sourceType, v); }, "BtnPrimary");
            var bar = U.Row(new Border { Background = U.R("Panel"), BorderBrush = U.R("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Child = sourceBar, Margin = new Thickness(0, 0, 12, 0) }, idBox, pick, load, entityName);
            pick.Margin = new Thickness(8, 0, 8, 0); entityName.VerticalAlignment = VerticalAlignment.Center;
            bar.Margin = new Thickness(0, 0, 0, 14);
            Grid.SetRow(bar, 1); view.Children.Add(bar);

            body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
            Grid.SetRow(body, 2); view.Children.Add(body);

            var left = new DockPanel();
            var lb = U.Row(U.Btn("Add row", AddRow, "BtnGhost", "A new row with the next free id."), U.Btn("Duplicate", DuplicateRow, "BtnGhost"), U.Btn("Delete", DeleteRow, "BtnGhost"));
            lb.Margin = new Thickness(0, 0, 0, 8); DockPanel.SetDock(lb, Dock.Top); left.Children.Add(lb);
            list.SelectionChanged += (s, e) => { if (list.SelectedIndex >= 0 && list.SelectedIndex != sel) { sel = list.SelectedIndex; BuildEditor(); } };
            left.Children.Add(list);
            body.Children.Add(U.Card(left, new Thickness(10, 10, 6, 8), new Thickness(0)));
            Grid.SetColumn(editorHost, 2); body.Children.Add(editorHost);

            var right = new DockPanel();
            var stage = U.Btn("Stage script changes", Stage, "BtnPrimary", "Stage the differences (added, changed and removed rows) with undo.");
            var discard = U.Btn("Discard", () => Open(sourceType, entry, true), "BtnGhost");
            var rb2 = U.Row(stage, discard); rb2.Margin = new Thickness(0, 10, 0, 0);
            DockPanel.SetDock(rb2, Dock.Bottom); right.Children.Add(rb2);
            sqlBox.TextWrapping = TextWrapping.Wrap; sqlBox.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            var sq = U.Stack(U.Eyebrow("SQL preview"), sqlBox); DockPanel.SetDock(sq, Dock.Bottom); right.Children.Add(sq);
            right.Children.Add(U.Scroll(U.Stack(U.Eyebrow("Checks"), issuesPanel)));
            var rc = U.Card(right, new Thickness(16, 14, 16, 14), new Thickness(0));
            Grid.SetColumn(rc, 4); body.Children.Add(rc);
            editorHost.Content = U.Card(U.Stack(U.H2("Open a script"), U.T("Pick a creature, game object or action list above. Each row reads as one sentence; click a row to edit it.", 13.5, "Muted")), new Thickness(28), new Thickness(0));
        }

        public void OnShow() { }

        // ================================================================== creature_text
        readonly Dictionary<(long, long), RowListEditor> textEditors = new Dictionary<(long, long), RowListEditor>();
        long? templateFor; long templateEntry;

        /// <summary>The creature template whose lines a Talk uses: the entry itself, or the template of a spawn (-guid).</summary>
        long TemplateEntry()
        {
            if (sourceType != 0) return 0;
            if (entry > 0) return entry;
            if (templateFor == entry) return templateEntry;
            templateFor = entry; templateEntry = 0;
            try
            {
                using var db = S.OpenRead(S.Profile.WorldDb);
                var t = db.Query($"SELECT * FROM creature WHERE guid = {-entry} LIMIT 1", 1);
                int c = t.ColumnIndex("id1"); if (c < 0) c = t.ColumnIndex("id");
                if (t.Rows.Count > 0 && c >= 0) templateEntry = Convert.ToInt64(t.Rows[0][c], CultureInfo.InvariantCulture);
            }
            catch { }
            return templateEntry;
        }

        UIElement TextLines(long group)
        {
            var ce = TemplateEntry();
            if (ce <= 0)
                return U.Callout(sourceType == 9 ? "An action list doesn't belong to one creature: the lines come from whichever creature runs the list. Open that creature's script to edit its lines." : "Lines belong to creatures (creature_text). Game objects can't talk with this action.", "info");
            if (!textEditors.TryGetValue((ce, group), out var ed))
            {
                var cid = ce.ToString(CultureInfo.InvariantCulture); var gid = group.ToString(CultureInfo.InvariantCulture);
                ed = new RowListEditor(w, $"creature_text for {refs.Name("creature", ce)} ({ce}), group {group}",
                    "One line is picked at random each time (weighted by Probability). Type: 12 say, 14 yell, 16 emote, 41 boss emote, 15 whisper. Duration feeds \"Finished talking\". Leave BroadcastTextId 0 for your own text.",
                    "creature_text", $"CreatureID = {cid} AND GroupID = {gid}", new Dictionary<string, string> { ["CreatureID"] = cid, ["GroupID"] = gid }, "ID");
                var edRef = ed;
                ed.NewRow = nr =>
                {
                    long next = 0;
                    foreach (System.Data.DataRow dr in edRef.Data.Rows)
                        if (dr.RowState != System.Data.DataRowState.Deleted && long.TryParse(Convert.ToString(dr["ID"]), out var v)) next = Math.Max(next, v + 1);
                    nr.Set("ID", next.ToString(CultureInfo.InvariantCulture));
                    if (nr.Cells.ContainsKey("Type")) nr.Set("Type", "12");
                    if (nr.Cells.ContainsKey("Probability")) nr.Set("Probability", "100");
                    if (nr.Cells.ContainsKey("Text")) nr.Set("Text", "'New line'", "New line");
                    if (nr.Cells.ContainsKey("comment")) nr.Set("comment", "'Studio'", "Studio");
                    return nr;
                };
                ed.Changed = _ => { existsCache.Remove((SP.TextGroup, group)); RefreshChecks(); };
                ed.Load();
                textEditors[(ce, group)] = ed;
            }
            if (ed.View is FrameworkElement fe && fe.Parent is Panel p) p.Children.Remove(fe);
            if (ed.View is FrameworkElement fe2 && fe2.Parent is Decorator d) d.Child = null;
            return ed.View;
        }

        string Resolve(SP k, long id) => k == SP.Script ? null : refs.ByKind(k, id);

        bool? Exists(SP k, long id)
        {
            if (existsCache.TryGetValue((k, id), out var v)) return v;
            try
            {
                switch (k)
                {
                    case SP.Spell: v = S.Dbc.Available ? S.Dbc.Name("spell", id) != null : (bool?)null; break;
                    case SP.Creature: v = id < 0 ? (bool?)null : refs.Name("creature", id) != "(not found)"; break;
                    case SP.GameObject: v = refs.Name("gameobject", id) != "(not found)"; break;
                    case SP.Quest: v = refs.Name("quest", id) != "(not found)"; break;
                    case SP.Item: v = refs.Name("item", id) != "(not found)"; break;
                    case SP.Script:
                    {
                        using var db = S.OpenRead(S.Profile.WorldDb);
                        v = db.Scalar($"SELECT COUNT(*) FROM smart_scripts WHERE source_type = 9 AND entryorguid = {id}") != "0" || rows.Any(r => r.SourceType == 9 && r.EntryOrGuid == id);
                        break;
                    }
                    case SP.TextGroup:
                    {
                        var ce = TemplateEntry();
                        if (ce <= 0) { v = null; break; }
                        var cid = ce.ToString(CultureInfo.InvariantCulture); var gid = id.ToString(CultureInfo.InvariantCulture);
                        // staged lines count too; staged removals don't
                        var staged = S.Changes.Pending.Changes.Where(c => string.Equals(c.Table, "creature_text", StringComparison.OrdinalIgnoreCase)
                            && c.Key.Any(kp => kp.Column == "CreatureID" && kp.Value == cid) && c.Key.Any(kp => kp.Column == "GroupID" && kp.Value == gid)).ToList();
                        using var db = S.OpenRead(S.Profile.WorldDb);
                        var n = long.Parse(db.Scalar($"SELECT COUNT(*) FROM creature_text WHERE CreatureID = {cid} AND GroupID = {gid}") ?? "0", CultureInfo.InvariantCulture);
                        n += staged.Count(c => c.Kind == ChangeKind.Insert) - staged.Count(c => c.Kind == ChangeKind.Delete);
                        v = n > 0;
                        break;
                    }
                    default: v = null; break;
                }
            }
            catch { v = null; }
            existsCache[(k, id)] = v;
            return v;
        }

        public void Open(int src, long id, bool force = false)
        {
            if (!S.HasReadAccount) { w.Toast("Finish setup step 3 first.", "warn"); return; }
            if (!force && !w.IsScreens && IsDirty() && ChoiceWindow.Ask(w, "Unstaged script edits", "This script has edits you haven't staged. Stage them first?", "Stage them", "Discard them", "Stay") is int r && r != 1) { if (r == 0) Stage(); else return; }
            sourceType = src; entry = id; textEditors.Clear();
            foreach (RadioButton rb in sourceBar.Children) rb.IsChecked = (int)rb.Tag == src;
            idBox.Text = id.ToString(CultureInfo.InvariantCulture);
            entityName.Text = src == 0 ? (id < 0 ? $"spawn {-id}" : refs.Name("creature", id)) : src == 1 ? refs.Name("gameobject", id) : $"action list {id}";
            try
            {
                using var db = S.OpenRead(S.Profile.WorldDb);
                var t = db.QueryTable($"SELECT * FROM smart_scripts WHERE entryorguid = {id} AND source_type = {src} ORDER BY id, link", 1000, out _);
                var cols = t.Columns.Cast<System.Data.DataColumn>().Select(c => c.ColumnName).ToList();
                var dbRows = t.Rows.Cast<System.Data.DataRow>().Select(r => RowValues.FromObjects(cols, r.ItemArray)).ToList();
                baseline = RowDiff.Overlay(S.Changes.Pending, "smart_scripts", SmartRow.KeyColumns, dbRows, r => r.Lit("entryorguid") == id.ToString(CultureInfo.InvariantCulture) && r.Lit("source_type") == src.ToString(CultureInfo.InvariantCulture));
                rows = baseline.Select(r => SmartRow.FromValues(c => r.Disp(c))).OrderBy(r => r.Id).ToList();
            }
            catch (Exception ex) { w.Toast("Couldn't load the script: " + Friendly.Text(ex), "bad"); baseline = new List<RowValues>(); rows = new List<SmartRow>(); }
            existsCache.Clear();
            sel = rows.Count > 0 ? 0 : -1;
            RefreshList(); BuildEditor(); RefreshChecks();
        }

        bool IsDirty()
        {
            if (rows == null) return false;
            var now = rows.Select(r => r.ToValues()).ToList();
            if (now.Count != baseline.Count) return true;
            string K(RowValues r) => string.Join("|", SmartRow.KeyColumns.Select(r.Lit));
            var b = baseline.ToDictionary(K);
            return now.Any(r => !b.TryGetValue(K(r), out var o) || SmartRow.Columns.Any(c => r.Lit(c) != o.Lit(c)));
        }

        void RefreshList()
        {
            list.Items.Clear();
            var issues = SmartAI.Validate(rows, Exists);
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                var top = U.Row(U.Pill("#" + r.Id, "AmberDim", "Amber"));
                if (r.Link != 0) top.Children.Add(U.Pill("→ #" + r.Link, "Panel2", "Muted", "Runs row " + r.Link + " right after this one."));
                var mine = issues.Where(x => x.RowIndex == i).ToList();
                if (mine.Count > 0) top.Children.Add(U.Pill(mine.Any(x => x.Level == "Error") ? "error" : "warning", mine.Any(x => x.Level == "Error") ? "BadBg" : "AmberDim", mine.Any(x => x.Level == "Error") ? "Bad" : "Amber", string.Join("\n", mine.Select(x => x.Text))));
                var text = U.T(SmartAI.Sentence(r, Resolve), 12.5, "Text", null, true, new Thickness(0, 4, 0, 0));
                var item = new ListBoxItem { Content = U.Stack(top, text, string.IsNullOrWhiteSpace(r.Comment) ? null : U.T(r.Comment, 11.5, "Faint", null, false)), Tag = i };
                list.Items.Add(item);
            }
            if (sel >= 0 && sel < list.Items.Count) list.SelectedIndex = sel;
        }

        void RefreshChecks()
        {
            issuesPanel.Children.Clear();
            var issues = SmartAI.Validate(rows, Exists);
            if (issues.Count == 0) issuesPanel.Children.Add(U.CheckRow(U.Check.Ok, "All rows", "no problems found", null, 70));
            foreach (var i in issues) issuesPanel.Children.Add(U.Callout(i.Text, i.Level == "Error" ? "bad" : "warn"));
            // SQL preview against a copy of the changeset
            try
            {
                var copy = JsonSerializer.Deserialize<Changeset>(JsonSerializer.Serialize(S.Changes.Pending));
                int before = copy.Changes.Count;
                var ids = new HashSet<string>(copy.Changes.Select(c => c.Id));
                RowDiff.Stage(copy, "smart_scripts", SmartRow.KeyColumns, SmartRow.Columns, baseline, rows.Select(r => r.ToValues()).ToList());
                var touched = copy.Changes.Where(c => c.Table == "smart_scripts" && (!ids.Contains(c.Id) || c.ForwardSql != S.Changes.Pending.Changes.FirstOrDefault(p => p.Id == c.Id)?.ForwardSql)).ToList();
                sqlBox.Text = touched.Count == 0 ? "-- No unstaged edits." : string.Join("\n", touched.Select(c => c.ForwardSql));
            }
            catch (Exception ex) { sqlBox.Text = "-- " + Friendly.Text(ex); }
        }

        void Changed() { RefreshList(); RefreshChecks(); if (sel >= 0 && sel < rows.Count) sentence.Text = SmartAI.Sentence(rows[sel], Resolve); }

        // ================================================================== row editor
        void BuildEditor()
        {
            if (sel < 0 || sel >= rows.Count) { editorHost.Content = U.Card(U.Stack(U.H2(rows.Count == 0 ? "No rows yet" : "Pick a row"), U.T("Add a row to start this script.", 13.5, "Muted")), new Thickness(28), new Thickness(0)); return; }
            var r = rows[sel];
            if (sentence.Parent is Panel old) old.Children.Remove(sentence);
            var sp = new StackPanel();
            sentence.Text = SmartAI.Sentence(r, Resolve);
            sp.Children.Add(U.Card(U.Stack(U.Eyebrow("Reads as"), sentence), new Thickness(18, 12, 18, 12), new Thickness(0, 0, 0, 12), "Panel2"));

            // event
            var ev = new StackPanel();
            ev.Children.Add(TypePicker("When (event)", SmartAI.Events, r.EventType, v => { r.EventType = v; Array.Clear(r.EventParams); BuildEditor(); Changed(); }));
            var et = SmartAI.Event(r.EventType);
            if (et != null) ev.Children.Add(U.T(et.Desc, 12.5, "Muted", null, true, new Thickness(0, 4, 0, 10)));
            ev.Children.Add(Params(et?.P, r.EventParams));
            var chance = U.Box(r.EventChance.ToString(), true); chance.Width = 80;
            chance.TextChanged += (s, e) => { if (int.TryParse(chance.Text, out var v)) { r.EventChance = v; Changed(); } };
            var cf = U.Field("Chance %", "100 = always. Lower values make it fire only sometimes.", chance, null, 140); cf.Margin = new Thickness(0, 6, 16, 8);
            ev.Children.Add(U.Wrap(cf));
            ev.Children.Add(U.T("Only in phases (none ticked = all phases)", 13, "Text", FontWeights.SemiBold, false, new Thickness(0, 4, 0, 6)));
            var phases = new WrapPanel();
            for (int i = 0; i < 12; i++)
            {
                int bit = 1 << i;
                var cb = new CheckBox { Content = U.T((i + 1).ToString(), 12.5, "Text", null, false), IsChecked = (r.EventPhaseMask & bit) != 0, Margin = new Thickness(0, 0, 14, 6), ToolTip = $"Phase {i + 1}: set with the actions Set phase / Change phase by." };
                cb.Checked += (s, e) => { r.EventPhaseMask |= bit; Changed(); }; cb.Unchecked += (s, e) => { r.EventPhaseMask &= ~bit; Changed(); };
                phases.Children.Add(cb);
            }
            ev.Children.Add(phases);
            ev.Children.Add(U.T("Event flags", 13, "Text", FontWeights.SemiBold, false, new Thickness(0, 8, 0, 6)));
            var flags = new WrapPanel();
            foreach (var kv in SmartAI.EventFlagBits)
            {
                int bit = (int)kv.Key;
                var cb = new CheckBox { Content = U.T(kv.Value, 12.5, "Text", null, false), IsChecked = (r.EventFlags & bit) != 0, Margin = new Thickness(0, 0, 16, 6), Width = 230 };
                cb.Checked += (s, e) => { r.EventFlags |= bit; Changed(); }; cb.Unchecked += (s, e) => { r.EventFlags &= ~bit; Changed(); };
                flags.Children.Add(cb);
            }
            ev.Children.Add(flags);
            sp.Children.Add(Section("1 · Event", ev));

            // action
            var ac = new StackPanel();
            ac.Children.Add(TypePicker("Do (action)", SmartAI.Actions, r.ActionType, v => { r.ActionType = v; Array.Clear(r.ActionParams); BuildEditor(); Changed(); }));
            var at = SmartAI.Action(r.ActionType);
            if (at != null) ac.Children.Add(U.T(at.Desc, 12.5, "Muted", null, true, new Thickness(0, 4, 0, 10)));
            ac.Children.Add(Params(at?.P, r.ActionParams));
            sp.Children.Add(Section("2 · Action", ac));

            // the lines behind "Say a line" (creature_text), editable right here
            long? group = (r.ActionType == 1 || r.ActionType == 84) ? r.ActionParams[0] : r.EventType == 52 ? r.EventParams[0] : (long?)null;
            if (group != null) sp.Children.Add(Section($"Lines of text group {group}", TextLines(group.Value)));

            // target
            var tg = new StackPanel();
            tg.Children.Add(TypePicker("On (target)", SmartAI.Targets, r.TargetType, v => { r.TargetType = v; Array.Clear(r.TargetParams); BuildEditor(); Changed(); }));
            var tt = SmartAI.Target(r.TargetType);
            if (tt != null) tg.Children.Add(U.T(tt.Desc, 12.5, "Muted", null, true, new Thickness(0, 4, 0, 10)));
            tg.Children.Add(Params(tt?.P, r.TargetParams));
            var pos = new WrapPanel();
            foreach (var (label, get, set) in new (string, Func<float>, Action<float>)[] { ("x", () => r.X, v => r.X = v), ("y", () => r.Y, v => r.Y = v), ("z", () => r.Z, v => r.Z = v), ("o (facing)", () => r.O, v => r.O = v) })
            {
                var b = U.Box(get().ToString("R", CultureInfo.InvariantCulture), true); b.Width = 120;
                b.TextChanged += (s, e) => { if (float.TryParse(b.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) { set(v); Changed(); } };
                var f = U.Field(label, null, b, "Position used by target type Position, movement and summon actions.", 140); f.Margin = new Thickness(0, 0, 12, 8);
                pos.Children.Add(f);
            }
            tg.Children.Add(pos);
            sp.Children.Add(Section("3 · Target", tg));

            // row
            var row = new WrapPanel();
            var idb = U.Box(r.Id.ToString(), true); idb.Width = 90;
            idb.TextChanged += (s, e) => { if (int.TryParse(idb.Text, out var v)) { r.Id = v; Changed(); } };
            var lk = U.Box(r.Link.ToString(), true); lk.Width = 90;
            lk.TextChanged += (s, e) => { if (int.TryParse(lk.Text, out var v)) { r.Link = v; Changed(); } };
            var cm = U.Box(r.Comment); cm.Width = 420;
            cm.TextChanged += (s, e) => { r.Comment = cm.Text; Changed(); };
            foreach (var f in new[] { U.Field("Row id", "Unique within this script.", idb, null, 120), U.Field("Then run row (link)", "0 = none. The linked row must use the event \"Linked\".", lk, null, 200), U.Field("Comment", "A note for humans; the server ignores it.", cm, null, 440) })
            { f.Margin = new Thickness(0, 0, 14, 8); row.Children.Add(f); }
            sp.Children.Add(Section("Row", row));
            editorHost.Content = U.Scroll(sp);
        }

        UIElement Section(string title, UIElement content) => U.Card(U.Stack(U.Eyebrow(title), content), new Thickness(18, 12, 18, 12), new Thickness(0, 0, 0, 12));

        UIElement TypePicker(string label, IReadOnlyList<SType> types, int current, Action<int> set)
        {
            var cb = new ComboBox { MaxDropDownHeight = 420, Width = 420, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var t in types.OrderBy(t => t.Obsolete).ThenBy(t => t.Id))
                cb.Items.Add(new ComboBoxItem { Content = $"{t.Id} · {t.Name}", Tag = t.Id, ToolTip = t.Desc, Foreground = t.Obsolete ? U.R("Faint") : U.R("Text") });
            if (!types.Any(t => t.Id == current)) cb.Items.Insert(0, new ComboBoxItem { Content = $"{current} · (unknown type)", Tag = current });
            cb.SelectedItem = cb.Items.Cast<ComboBoxItem>().First(i => (int)i.Tag == current);
            cb.SelectionChanged += (s, e) => { if (cb.SelectedItem is ComboBoxItem ci && (int)ci.Tag != current) set((int)ci.Tag); };
            var f = U.Field(label, null, cb, "Pick the type; its description and parameters appear below.");
            f.Margin = new Thickness(0, 0, 0, 0);
            return f;
        }

        UIElement Params(SParam[] defs, long[] values)
        {
            var wrap = new WrapPanel();
            if (defs == null || defs.Length == 0) { wrap.Children.Add(U.T("No settings.", 12.5, "Faint", null, false, new Thickness(0, 0, 0, 6))); return wrap; }
            for (int i = 0; i < defs.Length && i < values.Length; i++)
            {
                var d = defs[i]; int k = i;
                var box = U.Box(values[i].ToString(CultureInfo.InvariantCulture), true); box.Width = 110; box.HorizontalAlignment = HorizontalAlignment.Left;
                var name = U.T("", 12, "Amber", null, false, new Thickness(2, 4, 0, 0));
                void Upd()
                {
                    var v = values[k];
                    name.Text = d.Kind switch
                    {
                        SP.Ms => v == 0 ? "" : SmartAI.MsRange(v, v),
                        SP.PhaseMask => v == 0 ? "" : "phases " + string.Join(", ", SmartAI.Phases((int)v)),
                        SP.Flags when d.Label.StartsWith("Cast") => FlagMask.Describe(unchecked((ulong)v), SmartAI.CastFlagBits),
                        _ => Resolve(d.Kind, v) ?? (v != 0 && (d.Kind == SP.Spell || d.Kind == SP.Creature || d.Kind == SP.GameObject || d.Kind == SP.Quest || d.Kind == SP.Item) ? "not found" : ""),
                    };
                }
                box.TextChanged += (s, e) =>
                {
                    if (long.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) { values[k] = v; box.BorderBrush = U.R("Line"); Upd(); Changed(); }
                    else box.BorderBrush = U.R("Bad");
                };
                Upd();
                FrameworkElement ctl = box;
                var refKind = d.Kind switch { SP.Spell => "spell", SP.Creature => "creature", SP.GameObject => "gameobject", SP.Quest => "quest", SP.Item => "item", SP.Faction => "factiontemplate", _ => null };
                if (refKind != null)
                {
                    var pick = U.Btn("…", () =>
                    {
                        var dlg = new PickerWindow(w, "Pick " + d.Label.ToLowerInvariant(), d.Hint ?? d.Label, q => refs.Search(refKind, q), "");
                        if (dlg.ShowDialog() == true && dlg.Picked != null) box.Text = dlg.Picked.Value.ToString(CultureInfo.InvariantCulture);
                    }, null, "Pick by name");
                    pick.Padding = new Thickness(10, 6, 10, 6); pick.Margin = new Thickness(6, 0, 0, 0);
                    ctl = U.Row(box, pick);
                }
                var f = U.Field(d.Label, d.Hint ?? $"Parameter {i + 1}", U.Stack(ctl, name), (d.Hint ?? d.Label) + $"\n(param {i + 1})", 220);
                f.Margin = new Thickness(0, 0, 14, 8);
                wrap.Children.Add(f);
            }
            return wrap;
        }

        // ================================================================== rows
        void AddRow()
        {
            if (entry == 0 && rows.Count == 0 && string.IsNullOrEmpty(idBox.Text)) { w.Toast("Open a script first.", "warn"); return; }
            int next = rows.Count == 0 ? 0 : rows.Max(r => r.Id) + 1;
            var r = new SmartRow { EntryOrGuid = entry, SourceType = sourceType, Id = next, EventType = sourceType == 9 ? 0 : 4, ActionType = 1, TargetType = 1, EventChance = 100 };
            rows.Add(r); sel = rows.Count - 1;
            Changed(); BuildEditor();
        }

        void DuplicateRow()
        {
            if (sel < 0) return;
            var r = rows[sel].Clone(); r.Id = rows.Max(x => x.Id) + 1; r.Link = 0;
            rows.Add(r); sel = rows.Count - 1;
            Changed(); BuildEditor();
        }

        void DeleteRow()
        {
            if (sel < 0) return;
            rows.RemoveAt(sel); sel = Math.Min(sel, rows.Count - 1);
            Changed(); BuildEditor();
        }

        void Stage()
        {
            try
            {
                var errors = SmartAI.Validate(rows, Exists).Where(i => i.Level == "Error").ToList();
                if (errors.Count > 0 && ChoiceWindow.Ask(w, "The script has errors", $"{errors.Count} error(s), e.g. {errors[0].Text} Stage anyway?", "Stage anyway", "Go back", "Go back") != 0) return;
                int n = RowDiff.Stage(S.Changes.Pending, "smart_scripts", SmartRow.KeyColumns, SmartRow.Columns, baseline, rows.Select(r => r.ToValues()).ToList());
                S.SaveChanges();
                w.Toast(n == 0 ? "No changes to stage." : $"Staged {n} change(s) to this script. Review them on Changes.", n == 0 ? "info" : "ok");
                Open(sourceType, entry, true);
            }
            catch (Exception ex) { w.Toast("Not staged: " + Friendly.Text(ex), "bad"); }
        }

        // for screenshots
        public void ScreensSelect(int i) { sel = i; RefreshList(); BuildEditor(); }
        public void ScreensScrollEditorToEnd()
        {
            if (!(editorHost.Content is ScrollViewer sv)) return;
            sv.UpdateLayout();
            var ed = textEditors.Values.FirstOrDefault();
            if (ed?.View is FrameworkElement fe) { var pt = fe.TransformToAncestor(sv).Transform(new Point(0, 0)); sv.ScrollToVerticalOffset(sv.VerticalOffset + pt.Y - 120); }
            else sv.ScrollToEnd();
        }
        public List<SmartRow> Rows => rows;
        public void ScreensChanged() { Changed(); BuildEditor(); }
    }
}
