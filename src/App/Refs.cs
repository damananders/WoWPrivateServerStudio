using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using AndersCraftStudio.Core;

namespace AndersCraftStudio
{
    /// <summary>Turns foreign IDs into names (DB rows or DBC entries) and searches them for the picker. Read-only.</summary>
    public sealed class Refs
    {
        readonly Studio S;
        readonly Dictionary<string, string> cache = new Dictionary<string, string>();
        public Refs(Studio s) { S = s; }

        static readonly Dictionary<string, (string table, string key, string name, string extra)> DbKinds = new Dictionary<string, (string, string, string, string)>
        {
            ["creature"] = ("creature_template", "entry", "name", "subname"),
            ["item"] = ("item_template", "entry", "name", "CONCAT('item level ', ItemLevel)"),
            ["quest"] = ("quest_template", "ID", "LogTitle", "CONCAT('level ', QuestLevel)"),
            ["gameobject"] = ("gameobject_template", "entry", "name", "CONCAT('type ', type)"),
        };

        public static bool IsDb(string kind) => DbKinds.ContainsKey(kind);
        public static string Workspace(string kind) => kind switch { "creature" => "creatures", "item" => "items", "quest" => "quests", _ => null };

        public static string LikeLiteral(string q) => SqlGen.Literal("%" + q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%");

        public string Name(string kind, long id)
        {
            if (id == 0) return null;
            if (kind == "creature" && id < 0) { kind = "gameobject"; id = -id; }
            var ck = kind + ":" + id;
            if (cache.TryGetValue(ck, out var hit)) return hit;
            string n = null;
            try
            {
                if (IsDb(kind))
                {
                    if (!S.HasReadAccount) return null;
                    var k = DbKinds[kind];
                    using var db = S.OpenRead(S.Profile.WorldDb);
                    var t = db.Query($"SELECT {SqlGen.Ident(k.name)} FROM {SqlGen.Ident(k.table)} WHERE {SqlGen.Ident(k.key)} = {id.ToString(CultureInfo.InvariantCulture)} LIMIT 1", 1);
                    n = t.Rows.Count > 0 ? Convert.ToString(t.Rows[0][0]) : null;
                    if (n == null) n = "(not found)";
                }
                else n = S.Dbc.Name(kind, id);
            }
            catch { n = null; }
            cache[ck] = n;
            return n;
        }

        public void Forget() => cache.Clear();

        /// <summary>Name lookup by SmartAI / list parameter kind.</summary>
        public string ByKind(SP k, long id) => id == 0 ? null : k switch
        {
            SP.Spell => Name("spell", id), SP.Creature => Name("creature", id), SP.GameObject => Name("gameobject", id), SP.Quest => Name("quest", id),
            SP.Item => Name("item", id), SP.Faction => Name("factiontemplate", id), SP.Map => Name("map", id), _ => null,
        };

        public List<(long id, string name, string extra)> Search(string kind, string q)
        {
            q = (q ?? "").Trim();
            var res = new List<(long, string, string)>();
            if (IsDb(kind))
            {
                if (!S.HasReadAccount) return res;
                var k = DbKinds[kind];
                bool num = long.TryParse(q, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id);
                var where = q.Length == 0 ? "1=1" : num ? $"{SqlGen.Ident(k.key)} = {id} OR {SqlGen.Ident(k.name)} LIKE {LikeLiteral(q)}" : $"{SqlGen.Ident(k.name)} LIKE {LikeLiteral(q)}";
                var order = num ? $"({SqlGen.Ident(k.key)} = {id}) DESC, " : "";
                using var db = S.OpenRead(S.Profile.WorldDb);
                var t = db.Query($"SELECT {SqlGen.Ident(k.key)}, {SqlGen.Ident(k.name)}, {k.extra} FROM {SqlGen.Ident(k.table)} WHERE {where} ORDER BY {order}{SqlGen.Ident(k.key)} LIMIT 200", 200);
                foreach (var r in t.Rows) res.Add((Convert.ToInt64(r[0], CultureInfo.InvariantCulture), Convert.ToString(r[1]), Convert.ToString(r[2])));
            }
            else
                foreach (var (id, name) in S.Dbc.Search(kind, q)) res.Add((id, name, ""));
            return res;
        }
    }

    /// <summary>A dark, keyboard-first dialog for picking a foreign ID by name.</summary>
    public sealed class PickerWindow : Window
    {
        public long? Picked { get; private set; }

        public PickerWindow(Window owner, string title, string help, Func<string, List<(long id, string name, string extra)>> search, string initial)
        {
            Owner = owner; Title = title; Width = 560; Height = 560; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Background = U.R("Panel"); FontFamily = U.Font; Foreground = U.R("Text");
            WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 40, GlassFrameThickness = new Thickness(0), ResizeBorderThickness = new Thickness(0), CornerRadius = new CornerRadius(0) });
            var box = U.Box(initial ?? "");
            var list = new ListBox();
            var status = U.T("", 12, "Muted", null, false, new Thickness(2, 8, 0, 0));
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
            void Run()
            {
                timer.Stop(); list.Items.Clear();
                List<(long id, string name, string extra)> hits;
                try { hits = search(box.Text); } catch (Exception ex) { status.Text = "Search failed: " + ex.Message; return; }
                foreach (var h in hits)
                {
                    var row = new DockPanel();
                    var id = U.T("#" + h.id, 12.5, "Amber", FontWeights.SemiBold, false); id.Width = 86; id.FontFamily = U.Mono;
                    DockPanel.SetDock(id, Dock.Left); row.Children.Add(id);
                    row.Children.Add(U.Stack(U.T(h.name, 13.5, "Text", FontWeights.SemiBold, false), string.IsNullOrEmpty(h.extra) ? null : U.T(h.extra, 12, "Muted", null, false)));
                    list.Items.Add(new ListBoxItem { Content = row, Tag = h.id });
                }
                status.Text = hits.Count == 0 ? "Nothing found. Try part of the name, or the exact ID." : hits.Count >= 200 ? "Showing the first 200 matches." : $"{hits.Count} match(es).";
                if (list.Items.Count > 0) list.SelectedIndex = 0;
            }
            timer.Tick += (s, e) => Run();
            box.TextChanged += (s, e) => { timer.Stop(); timer.Start(); };
            void Pick() { if (list.SelectedItem is ListBoxItem li) { Picked = (long)li.Tag; DialogResult = true; } }
            box.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Down && list.Items.Count > 0) { list.SelectedIndex = Math.Min(list.Items.Count - 1, list.SelectedIndex + 1); list.ScrollIntoView(list.SelectedItem); e.Handled = true; }
                else if (e.Key == Key.Up && list.Items.Count > 0) { list.SelectedIndex = Math.Max(0, list.SelectedIndex - 1); list.ScrollIntoView(list.SelectedItem); e.Handled = true; }
                else if (e.Key == Key.Enter) { Pick(); e.Handled = true; }
            };
            list.MouseDoubleClick += (s, e) => Pick();
            list.KeyDown += (s, e) => { if (e.Key == Key.Enter) Pick(); };
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; } };

            var dp = new DockPanel { Margin = new Thickness(20, 16, 20, 18) };
            var head = U.Stack(U.Eyebrow("Pick"), U.H2(title), U.T(help, 12.5, "Muted", null, true, new Thickness(0, 0, 0, 12)), U.WithPlaceholder(box, "Type a name or an ID…"), status);
            DockPanel.SetDock(head, Dock.Top); dp.Children.Add(head);
            var btns = U.Row(U.Btn("Use selected  (Enter)", Pick, "BtnPrimary"), U.Btn("Cancel  (Esc)", () => DialogResult = false, "BtnGhost"));
            btns.Margin = new Thickness(0, 12, 0, 0); btns.HorizontalAlignment = HorizontalAlignment.Right;
            DockPanel.SetDock(btns, Dock.Bottom); dp.Children.Add(btns);
            list.Margin = new Thickness(0, 10, 0, 0);
            dp.Children.Add(list);
            Content = new Border { BorderBrush = U.R("LineHi"), BorderThickness = new Thickness(1), Child = dp };
            Loaded += (s, e) => { box.Focus(); box.SelectAll(); Run(); };
        }
    }
}
