using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using AndersCraftStudio.Core;
using AndersCraftStudio.Core.Build;

namespace AndersCraftStudio
{
    /// <summary>
    /// A linear, step-by-step window: the steps down the left (numbered, clickable), the current step in the middle, a live
    /// preview on the right, and Back / Next at the bottom. The last step reviews and stages.
    /// </summary>
    public class StepWindow : Window
    {
        protected readonly MainWindow w;
        protected Studio S => w.S;
        protected readonly List<(string name, Func<UIElement> build)> Steps = new List<(string, Func<UIElement>)>();
        protected int step;
        readonly StackPanel rail = new StackPanel();
        readonly ContentControl body = new ContentControl { Focusable = false };
        protected readonly ContentControl preview = new ContentControl { Focusable = false };
        readonly Button back, next;
        readonly string eyebrow;
        protected string previewTitle = "Preview";
        /// <summary>Which "How to use this" guide belongs to this builder.</summary>
        protected string GuideKey = "help";

        public StepWindow(MainWindow owner, string title, string eyebrow)
        {
            w = owner; Owner = owner; Title = title; this.eyebrow = eyebrow;
            Width = Math.Max(1100, Math.Min(1500, owner.ActualWidth * 0.92)); Height = Math.Max(700, Math.Min(940, owner.ActualHeight * 0.92));
            WindowStartupLocation = WindowStartupLocation.CenterOwner; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResizeWithGrip; ShowInTaskbar = false;
            Background = U.R("Bg"); FontFamily = U.Font; Foreground = U.R("Text");
            WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 40, GlassFrameThickness = new Thickness(0), ResizeBorderThickness = new Thickness(6) });
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
            back = U.Btn("Back", () => Go(step - 1), null, "Previous step (Alt+Left)", 110);
            next = U.Btn("Next", () => Go(step + 1), "BtnPrimary", "Next step (Alt+Right)", 150);
            U.OnKey(this, Key.Right, ModifierKeys.Alt, () => { if (step < Steps.Count - 1) Go(step + 1); });
            U.OnKey(this, Key.Left, ModifierKeys.Alt, () => { if (step > 0) Go(step - 1); });

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400) });
            var left = new DockPanel { Background = U.R("Panel"), Margin = new Thickness(0) };
            var close = U.Btn("Close", Close, "BtnGhost"); WindowChrome.SetIsHitTestVisibleInChrome(close, true);
            var help = U.Btn("How to use this", () => GuideWindow.ShowFor(w, GuideKey), "BtnGhost", "A picture of this builder with numbered steps.");
            var leftFoot = U.Row(close, help); leftFoot.Margin = new Thickness(14, 10, 0, 10);
            DockPanel.SetDock(leftFoot, Dock.Bottom); left.Children.Add(leftFoot);
            var head = U.Stack(U.Eyebrow(eyebrow), U.T(title, 19, "Text", FontWeights.SemiBold, true, new Thickness(0, 0, 0, 18)));
            head.Margin = new Thickness(22, 24, 18, 0);
            DockPanel.SetDock(head, Dock.Top); left.Children.Add(head);
            rail.Margin = new Thickness(12, 0, 12, 0);
            left.Children.Add(U.Scroll(rail));
            g.Children.Add(left);

            var mid = new DockPanel { Margin = new Thickness(34, 26, 26, 20) };
            var foot = U.Row(back, new Border { Width = 10 }, next); foot.HorizontalAlignment = HorizontalAlignment.Right; foot.Margin = new Thickness(0, 14, 0, 0);
            DockPanel.SetDock(foot, Dock.Bottom); mid.Children.Add(foot);
            mid.Children.Add(U.Scroll(body));
            Grid.SetColumn(mid, 1); g.Children.Add(mid);
            var right = new Border { Background = U.R("Panel"), Padding = new Thickness(20, 24, 20, 20), Child = U.Scroll(preview) };
            Grid.SetColumn(right, 2); g.Children.Add(right);
            Content = g;
        }

        protected void Go(int s)
        {
            if (s < 0 || s >= Steps.Count) return;
            step = s;
            rail.Children.Clear();
            for (int i = 0; i < Steps.Count; i++)
            {
                int idx = i;
                bool cur = i == step, done = i < step;
                var num = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = U.R(cur ? "Amber" : done ? "OkBg" : "Panel2"), Margin = new Thickness(0, 0, 10, 0),
                    Child = new TextBlock { Text = done ? "✓" : (i + 1).ToString(), FontWeight = FontWeights.Bold, FontSize = 12, Foreground = U.R(cur ? "AmberInk" : done ? "Ok" : "Muted"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                var b = new Button { Style = U.S("BtnGhost"), HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(10, 8, 10, 8),
                    Content = U.Row(num, U.T(Steps[i].name, 13.5, cur ? "Text" : "Muted", cur ? FontWeights.SemiBold : (FontWeight?)null, false)) };
                ((FrameworkElement)((StackPanel)b.Content).Children[1]).VerticalAlignment = VerticalAlignment.Center;
                b.Click += (o, e) => Go(idx);
                rail.Children.Add(b);
            }
            back.IsEnabled = step > 0;
            next.Visibility = step < Steps.Count - 1 ? Visibility.Visible : Visibility.Collapsed;
            var sp = U.Stack(U.Eyebrow($"Step {step + 1} of {Steps.Count}"), U.H1(Steps[step].name));
            sp.Children.Add(Steps[step].build());
            body.Content = sp;
            Refresh();
        }

        /// <summary>Rebuilds the preview (call after any change).</summary>
        protected void Refresh()
        {
            try { preview.Content = U.Stack(U.Eyebrow(previewTitle), BuildPreview()); }
            catch (Exception ex) { preview.Content = U.T("Preview failed: " + Friendly.Text(ex), 12.5, "Bad"); }
        }
        protected virtual UIElement BuildPreview() => new Border();

        // ---------- form helpers (soft, labelled, plain)
        protected static StackPanel Field(string label, string hint, FrameworkElement c)
        {
            c.HorizontalAlignment = HorizontalAlignment.Left;
            var f = U.Field(label, hint, c); f.Margin = new Thickness(0, 0, 0, 14); f.HorizontalAlignment = HorizontalAlignment.Left;
            return f;
        }
        protected TextBox Text(string value, Action<string> set, double width = 460, bool multi = false)
        {
            var b = U.Box(value ?? ""); b.Width = width;
            if (multi) { b.AcceptsReturn = true; b.TextWrapping = TextWrapping.Wrap; b.Height = 96; b.VerticalContentAlignment = VerticalAlignment.Top; }
            b.TextChanged += (s, e) => { set(b.Text); Refresh(); };
            return b;
        }
        protected TextBox Number(double value, Action<double> set, double width = 120)
        {
            var b = U.Box(value.ToString(CultureInfo.InvariantCulture)); b.Width = width;
            b.TextChanged += (s, e) => { if (double.TryParse(b.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) { set(v); Refresh(); } };
            return b;
        }
        protected ComboBox Choice(IEnumerable<string> items, string current, Action<string> set, double width = 280)
        {
            var c = new ComboBox { Width = width };
            foreach (var i in items) c.Items.Add(i);
            c.SelectedItem = current; if (c.SelectedIndex < 0 && c.Items.Count > 0) c.SelectedIndex = 0;
            c.SelectionChanged += (s, e) => { set((string)c.SelectedItem); Refresh(); };
            return c;
        }

        /// <summary>A "pick one" control: the chosen name, a Pick… button and a Clear button.</summary>
        protected FrameworkElement Picker(string kind, long value, Action<long> set, string what, bool allowClear = true)
        {
            var refs = new Refs(S);
            var name = U.T(value == 0 ? "(none)" : $"{refs.Name(kind, value) ?? "?"}" + (U.Technical ? $"  #{value}" : ""), 13.5, value == 0 ? "Muted" : "Text", FontWeights.SemiBold, false, new Thickness(0, 0, 12, 0));
            name.VerticalAlignment = VerticalAlignment.Center; name.MinWidth = 220;
            var pick = U.Btn("Pick…", () =>
            {
                var dlg = new PickerWindow(this, "Pick " + what, "Type part of the name (or the number).", q => refs.Search(kind, q), "");
                if (dlg.ShowDialog() == true && dlg.Picked is long id) { set(id); name.Text = (refs.Name(kind, id) ?? "?") + (U.Technical ? $"  #{id}" : ""); name.Foreground = U.R("Text"); Refresh(); }
            }, null);
            var row = U.Row(name, pick);
            if (allowClear) row.Children.Add(U.Btn("Clear", () => { set(0); name.Text = "(none)"; name.Foreground = U.R("Muted"); Refresh(); }, "BtnGhost"));
            return row;
        }

        protected static Border Card(UIElement child, string title = null, string sub = null)
        {
            var sp = new StackPanel();
            if (title != null) sp.Children.Add(U.T(title, 15, "Text", FontWeights.SemiBold, false, new Thickness(0, 0, 0, sub == null ? 10 : 2)));
            if (sub != null) sp.Children.Add(U.T(sub, 12.5, "Muted", null, true, new Thickness(0, 0, 0, 10)));
            sp.Children.Add(child);
            return U.Soft(sp, new Thickness(20, 16, 20, 16), new Thickness(0, 0, 0, 14), "Panel");
        }

        /// <summary>Three big start choices (new / mine / the game's).</summary>
        protected UIElement StartChoices(string noun, Func<string> current, Action<string> choose, (string key, string title, string what)[] options)
        {
            var sp = new StackPanel();
            foreach (var o in options)
            {
                var oo = o;
                bool on = current() == o.key;
                var b = new Button { Style = U.S("BtnGhost"), HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 10),
                    Content = new Border { Background = U.R(on ? "MaroonDim" : "Panel"), CornerRadius = new CornerRadius(16), Padding = new Thickness(20, 16, 20, 16),
                        Child = U.Stack(U.T(o.title, 16, "Text", FontWeights.SemiBold, false), U.T(o.what, 13, "Muted", null, true, new Thickness(0, 4, 0, 0))) } };
                b.Click += (s, e) => choose(oo.key);
                sp.Children.Add(b);
            }
            return sp;
        }

        /// <summary>The changes a builder would stage, in plain words, grouped by what they touch.</summary>
        protected static UIElement ChangeList(IList<Change> changes)
        {
            var sp = new StackPanel();
            foreach (var g in changes.GroupBy(c => c.Table))
            {
                var adds = g.Count(c => c.Kind == ChangeKind.Insert); var edits = g.Count(c => c.Kind == ChangeKind.Update); var dels = g.Count(c => c.Kind == ChangeKind.Delete);
                var what = string.Join(", ", new[] { adds > 0 ? $"{adds} new" : null, edits > 0 ? $"{edits} changed" : null, dels > 0 ? $"{dels} removed" : null }.Where(x => x != null));
                var title = PlainNames.Table(g.Key) + (U.Technical ? $"  ({g.Key})" : "");
                sp.Children.Add(U.Row(U.Pill(what, "Panel2", "Text"), new Border { Width = 10 }, U.T(title, 13.5, "Text", null, false)));
                ((FrameworkElement)sp.Children[^1]).Margin = new Thickness(0, 0, 0, 6);
                foreach (var c in g.Where(c => c.Kind == ChangeKind.Update).Take(12))
                    sp.Children.Add(U.T("   " + string.Join(", ", c.Fields.Select(f => PlainNames.Column(c.Table, f.Name) + (U.Technical ? $" [{f.Name}]" : "") + ": " + Short(f.BeforeDisplay ?? f.Before) + " → " + Short(f.AfterDisplay ?? f.After))), 12, "Muted", null, true, new Thickness(0, 0, 0, 6)));
            }
            if (changes.Count == 0) sp.Children.Add(U.T("Nothing would change.", 13, "Muted"));
            return sp;
        }
        static string Short(string s) => s == null ? "(none)" : s.Length > 40 ? s.Substring(0, 40) + "…" : s;
    }

    // ============================================================================ Make a quest
    public sealed class QuestBuildWindow : StepWindow
    {
        string start = "new";      // new, mine, game
        StartMode mode = StartMode.New;
        long sourceId;
        QuestBuilder.QuestRows original;
        QuestDraft d = new QuestDraft();
        readonly Refs refs;
        public bool Staged { get; private set; }
        public long StagedId { get; private set; }

        public QuestBuildWindow(MainWindow owner, long openId = 0) : base(owner, "Make or change a quest", "Quest builder")
        {
            refs = new Refs(S);
            previewTitle = "What the player sees"; GuideKey = "questbuilder";
            d.Objectives.Add(new Objective { Kind = "kill", Count = 8 });
            Steps.Add(("Start", StepStart));
            Steps.Add(("The basics", StepBasics));
            Steps.Add(("Who gives it", StepWho));
            Steps.Add(("What to do", StepObjectives));
            Steps.Add(("Rewards", StepRewards));
            Steps.Add(("What they say", StepTexts));
            Steps.Add(("Who can take it", StepWhoCan));
            Steps.Add(("Review and stage", StepReview));
            if (openId > 0) { start = openId >= S.Profiles.Settings.CustomIdStart ? "mine" : "game"; sourceId = openId; mode = start == "mine" ? StartMode.Edit : StartMode.CopyAsNew; LoadSource(); }
            Go(openId > 0 ? 1 : 0);
        }

        UIElement StepStart()
        {
            var sp = new StackPanel();
            sp.Children.Add(U.T("Pick how to begin. You can change any part later; nothing reaches your server until you stage it and Apply it on the Changes screen.", 13.5, "Muted", null, true, new Thickness(0, 0, 0, 16)));
            sp.Children.Add(StartChoices("quest", () => start, k => { start = k; mode = k == "new" ? StartMode.New : k == "mine" ? StartMode.Edit : StartMode.CopyAsNew; if (k == "new") { original = null; d = new QuestDraft(); d.Objectives.Add(new Objective { Kind = "kill", Count = 8 }); sourceId = 0; } Go(0); }, new[]
            {
                ("new", "A brand-new quest", "Start from an empty quest with sensible defaults."),
                ("mine", "Change a quest I made", "Load one of your own quests and change it."),
                ("game", "Start from a game quest", "Load one of the game's own quests: change it, or copy it as a new quest (recommended, the original stays as it is)."),
            }));
            if (start != "new")
            {
                sp.Children.Add(Card(Picker("quest", sourceId, id => { sourceId = id; LoadSource(); Go(0); }, "a quest", false), "Which quest", start == "mine" ? "Your quests have numbers from " + S.Profiles.Settings.CustomIdStart + " up." : "Search the game's quests by name."));
                if (start == "game")
                    sp.Children.Add(Card(Choice(new[] { "Copy it as a new quest (the original stays)", "Change the game's quest itself" }, mode == StartMode.Edit ? "Change the game's quest itself" : "Copy it as a new quest (the original stays)",
                        v => mode = v.StartsWith("Change") ? StartMode.Edit : StartMode.CopyAsNew, 420), "How"));
            }
            return sp;
        }

        void LoadSource()
        {
            if (sourceId <= 0) return;
            try
            {
                using var db = S.OpenRead(S.Profile.WorldDb);
                original = QuestBuilder.Load(db, sourceId);
                if (original == null) { w.Toast($"Quest {sourceId} wasn't found.", "warn"); return; }
                d = QuestBuilder.ToDraft(original);
            }
            catch (Exception ex) { w.Toast("Couldn't read the quest: " + Friendly.Text(ex), "bad"); }
        }

        UIElement StepBasics()
        {
            var sp = new StackPanel();
            sp.Children.Add(Field("Name", "As it shows in the quest log.", Text(d.Title, v => d.Title = v)));
            sp.Children.Add(U.Row(Field("Quest level", "Its level in the quest log (colour).", Number(d.Level, v => d.Level = (int)v)), new Border { Width = 16 },
                                  Field("Lowest level to take it", null, Number(d.MinLevel, v => d.MinLevel = (int)v))));
            sp.Children.Add(U.Row(Field("Kind", "Group, dungeon and raid quests are marked in the log.", Choice(QuestBuilder.Types, d.Type, v => d.Type = v, 200)), new Border { Width = 16 },
                                  Field("Group size", "For group quests.", Number(d.GroupSize, v => d.GroupSize = (int)v))));
            sp.Children.Add(Field("How often", "Daily and weekly quests reset at the server's reset time.", Choice(QuestBuilder.Repeats, d.Repeat, v => d.Repeat = v, 200)));
            sp.Children.Add(Field("Listed under zone", "Where the quest log groups it (optional).", Picker("area", d.Zone, v => d.Zone = v, "a zone")));
            return sp;
        }

        UIElement StepWho()
        {
            var sp = new StackPanel();
            bool giverObj = d.Giver < 0, enderObj = d.Ender < 0;
            sp.Children.Add(Card(U.Stack(
                Choice(new[] { "A creature (NPC)", "An object (a sign, a book, a chest)" }, giverObj ? "An object (a sign, a book, a chest)" : "A creature (NPC)", v => { var obj = v.StartsWith("An object"); if (obj != d.Giver < 0) d.Giver = -d.Giver; Go(step); }, 320),
                new Border { Height = 10 },
                Picker(giverObj ? "gameobject" : "creature", Math.Abs(d.Giver), v => d.Giver = giverObj ? -v : v, "who gives the quest", false)),
                "Who gives the quest", "Studio adds the link, and the \"!\" over a creature's head if it didn't have one."));
            var same = new CheckBox { IsChecked = d.Ender == 0 || d.Ender == d.Giver, Content = U.T("The same one takes it back", 13.5, "Text", null, false), Margin = new Thickness(0, 0, 0, 10) };
            same.Checked += (s, e) => { d.Ender = 0; Go(step); }; same.Unchecked += (s, e) => { d.Ender = d.Giver; Go(step); };
            var endSp = U.Stack(same);
            if (same.IsChecked != true)
            {
                endSp.Children.Add(Choice(new[] { "A creature (NPC)", "An object" }, enderObj ? "An object" : "A creature (NPC)", v => { var obj = v == "An object"; if (obj != d.Ender < 0) d.Ender = -d.Ender; Go(step); }, 320));
                endSp.Children.Add(new Border { Height = 10 });
                endSp.Children.Add(Picker(enderObj ? "gameobject" : "creature", Math.Abs(d.Ender), v => d.Ender = enderObj ? -v : v, "who takes it back", false));
            }
            sp.Children.Add(Card(endSp, "Who the player returns to"));
            return sp;
        }

        static readonly (string key, string label, string kind, string what)[] ObjKinds =
        {
            ("kill", "Kill", "creature", "Kill a number of a creature."), ("collect", "Bring items", "item", "Collect items (Studio can make them drop)."),
            ("talk", "Talk to", "creature", "Speak with an NPC (Studio adds a small script for the credit)."), ("use", "Use an object", "gameobject", "Click an object (a lever, a banner, a crate)."),
            ("explore", "Go somewhere", "", "Reach a place (an area trigger)."),
        };

        UIElement StepObjectives()
        {
            var sp = new StackPanel();
            sp.Children.Add(U.T("Add what the player has to do. Up to 4 kill / talk / use goals and 6 kinds of items.", 13.5, "Muted", null, true, new Thickness(0, 0, 0, 12)));
            foreach (var o in d.Objectives.ToList())
            {
                var oo = o;
                var k = ObjKinds.First(x => x.key == o.Kind);
                var inner = new StackPanel();
                var kind = Choice(ObjKinds.Select(x => x.label), k.label, v => { oo.Kind = ObjKinds.First(x => x.label == v).key; oo.Target = 0; Go(step); }, 200);
                var rm = U.Btn("Remove", () => { d.Objectives.Remove(oo); Go(step); }, "BtnGhost");
                inner.Children.Add(U.Row(kind, new Border { Width = 10 }, rm));
                inner.Children.Add(new Border { Height = 10 });
                if (o.Kind == "explore")
                    inner.Children.Add(Field("Place (area trigger number)", "World tables › Area triggers lists them; or ask a GM to stand there and use .gps.", Number(o.Target, v => oo.Target = (long)v)));
                else
                {
                    if (o.Kind == "collect")
                    {
                        var mk = new CheckBox { IsChecked = !string.IsNullOrEmpty(o.NewItemName), Content = U.T("Make a new quest item for it", 13, "Text", null, false), Margin = new Thickness(0, 0, 0, 10) };
                        mk.Checked += (s, e) => { oo.NewItemName = "Mysterious Trinket"; oo.Target = 0; Go(step); }; mk.Unchecked += (s, e) => { oo.NewItemName = null; Go(step); };
                        inner.Children.Add(mk);
                        if (!string.IsNullOrEmpty(o.NewItemName)) inner.Children.Add(Field("Its name", null, Text(o.NewItemName, v => oo.NewItemName = v, 320)));
                        else inner.Children.Add(Field("Item", null, Picker("item", o.Target, v => oo.Target = v, "the item")));
                    }
                    else inner.Children.Add(Field(o.Kind == "use" ? "Object" : "Creature", null, Picker(k.kind, o.Target, v => oo.Target = v, o.Kind == "use" ? "the object" : "the creature")));
                    inner.Children.Add(Field(o.Kind == "talk" ? "How many times" : "How many", null, Number(o.Count, v => oo.Count = (int)v)));
                    if (o.Kind == "collect")
                    {
                        inner.Children.Add(Field("Dropped by", "Optional: Studio adds it to this creature's drops, only for players on the quest.", Picker("creature", o.DropFrom, v => oo.DropFrom = v, "who drops it")));
                        inner.Children.Add(Field("Drop chance (%)", null, Number(o.DropChance, v => oo.DropChance = v)));
                    }
                    if (o.Kind is "kill" or "use" or "talk") inner.Children.Add(Field("Quest log text (optional)", "Leave empty to use the creature's or object's name.", Text(o.Text, v => oo.Text = v, 320)));
                }
                sp.Children.Add(Card(inner, k.label, k.what));
            }
            var add = new WrapPanel();
            foreach (var k in ObjKinds) { var kk = k; var b = U.Btn("+ " + k.label, () => { d.Objectives.Add(new Objective { Kind = kk.key, Count = kk.key is "talk" or "explore" ? 1 : 8 }); Go(step); }, null, k.what); b.Margin = new Thickness(0, 0, 8, 8); add.Children.Add(b); }
            sp.Children.Add(add);
            return sp;
        }

        UIElement StepRewards()
        {
            var sp = new StackPanel();
            sp.Children.Add(Field("Experience", "Scaled to the quest's level by the game.", Choice(QuestBuilder.XpNames, QuestBuilder.XpNames[Math.Max(0, Math.Min(8, d.XpSize))], v => d.XpSize = Array.IndexOf(QuestBuilder.XpNames, v), 260)));
            long g = d.Money / 10000, s = d.Money / 100 % 100, c = d.Money % 100;
            void Money(long gg, long ss, long cc) { d.Money = gg * 10000 + ss * 100 + cc; }
            sp.Children.Add(U.Row(Field("Gold", null, Number(g, v => { g = (long)v; Money(g, s, c); }, 90)), new Border { Width = 10 }, Field("Silver", null, Number(s, v => { s = (long)v; Money(g, s, c); }, 90)), new Border { Width = 10 }, Field("Copper", null, Number(c, v => { c = (long)v; Money(g, s, c); }, 90))));
            sp.Children.Add(ItemList("Items they always get", "Up to 4.", d.Items, 4));
            sp.Children.Add(ItemList("Items they choose one of", "Up to 6. The player picks one.", d.Choices, 6));
            var rep = new StackPanel();
            for (int i = 0; i < d.Reputation.Count; i++)
            {
                int ii = i;
                rep.Children.Add(U.Row(Picker("faction", d.Reputation[i].faction, v => d.Reputation[ii] = (v, d.Reputation[ii].amount), "a faction", false), new Border { Width = 10 },
                    Number(d.Reputation[i].amount, v => d.Reputation[ii] = (d.Reputation[ii].faction, (int)v), 90), U.Btn("Remove", () => { d.Reputation.RemoveAt(ii); Go(step); }, "BtnGhost")));
                ((FrameworkElement)rep.Children[^1]).Margin = new Thickness(0, 0, 0, 8);
            }
            if (d.Reputation.Count < 5) rep.Children.Add(U.Btn("+ Reputation", () => { d.Reputation.Add((0, 250)); Go(step); }, null));
            sp.Children.Add(Card(rep, "Reputation", "Points with a faction (250 is a typical quest)."));
            sp.Children.Add(Field("Also casts a spell on them (optional)", "A buff, a title effect, a teleport…", Picker("spell", d.RewardSpell, v => d.RewardSpell = v, "a spell")));
            return sp;
        }

        Border ItemList(string title, string sub, List<(long item, int count)> list, int max)
        {
            var sp = new StackPanel();
            for (int i = 0; i < list.Count; i++)
            {
                int ii = i;
                sp.Children.Add(U.Row(Picker("item", list[i].item, v => list[ii] = (v, list[ii].count), "an item", false), new Border { Width = 10 }, U.T("×", 13, "Muted", null, false, new Thickness(0, 8, 6, 0)),
                    Number(list[i].count, v => list[ii] = (list[ii].item, (int)v), 70), U.Btn("Remove", () => { list.RemoveAt(ii); Go(step); }, "BtnGhost")));
                ((FrameworkElement)sp.Children[^1]).Margin = new Thickness(0, 0, 0, 8);
            }
            if (list.Count < max) sp.Children.Add(U.Btn("+ Item", () => { list.Add((0, 1)); Go(step); }, null));
            return Card(sp, title, sub);
        }

        UIElement StepTexts()
        {
            var sp = new StackPanel();
            sp.Children.Add(U.T("$N is the player's name, $C their class, $R their race; $B starts a new line.", 13, "Muted", null, true, new Thickness(0, 0, 0, 14)));
            sp.Children.Add(Field("What the giver says", "The quest text when they're offered it.", Text(d.Description, v => d.Description = v, 640, true)));
            sp.Children.Add(Field("Quest log summary", "Leave empty and Studio writes it from the objectives.", Text(d.Summary, v => d.Summary = v, 640, true)));
            sp.Children.Add(Field("When they come back before it's done", null, Text(d.Progress, v => d.Progress = v, 640, true)));
            sp.Children.Add(Field("When they hand it in", null, Text(d.Completion, v => d.Completion = v, 640, true)));
            sp.Children.Add(Field("Quest log text once it's done (optional)", "e.g. \"Return to Marshal Dughan.\"", Text(d.Done, v => d.Done = v, 640)));
            return sp;
        }

        UIElement StepWhoCan()
        {
            var sp = new StackPanel();
            var sides = new[] { "Everyone", "Alliance only", "Horde only" };
            sp.Children.Add(Field("Which side", null, Choice(sides, d.Races == QuestBuilder.AllianceRaces ? sides[1] : d.Races == QuestBuilder.HordeRaces ? sides[2] : sides[0],
                v => d.Races = v == sides[1] ? QuestBuilder.AllianceRaces : v == sides[2] ? QuestBuilder.HordeRaces : 0, 220)));
            var cls = new WrapPanel();
            foreach (var (mask, name) in SpellBook.Classes)
            {
                var m = mask;
                var cb = new CheckBox { IsChecked = (d.Classes & m) != 0, Content = U.T(name, 13, "Text", null, false), Margin = new Thickness(0, 0, 16, 8), Width = 140 };
                cb.Checked += (s, e) => { d.Classes |= m; Refresh(); }; cb.Unchecked += (s, e) => { d.Classes &= ~m; Refresh(); };
                cls.Children.Add(cb);
            }
            sp.Children.Add(Card(cls, "Which classes", "Leave all unticked for every class."));
            sp.Children.Add(Card(Picker("quest", d.Previous, v => d.Previous = v, "the quest that comes first"), "Comes after", "Players must have finished this quest first (a chain)."));
            sp.Children.Add(Card(Picker("quest", d.Next, v => d.Next = v, "the next quest"), "Offers next", "As soon as they hand this one in, the next quest is offered."));
            return sp;
        }

        List<string> notes = new List<string>();
        List<Change> Dry()
        {
            notes = new List<string>();
            using var db = S.OpenRead(S.Profile.WorldDb);
            var (orig, rows) = Prepare(db, S.Changes.Pending);
            var scratch = new Changeset();
            return QuestBuilder.Stage(mode, orig, rows, scratch);
        }

        (QuestBuilder.QuestRows orig, QuestBuilder.QuestRows rows) Prepare(ISqlReader db, Changeset cs)
        {
            var baseRows = original ?? QuestBuilder.Blank(db);
            if (mode != StartMode.Edit || original == null)
            {
                d.Id = Rows.FreeId(db, cs, "quest_template", "ID", S.Profiles.Settings.CustomIdStart);
                if (original != null) { baseRows = original.Clone(); baseRows.Starters.Clear(); baseRows.Enders.Clear(); baseRows.Triggers.Clear(); }
            }
            return (original, QuestBuilder.Apply(d, baseRows, db, cs, S.Profiles.Settings.CustomIdStart, notes));
        }

        UIElement StepReview()
        {
            var sp = new StackPanel();
            var problems = QuestBuilder.Check(d);
            if (problems.Count > 0) { sp.Children.Add(U.Callout(string.Join("\n", problems.Select(p => "• " + p)), "warn", "Before you stage")); ((FrameworkElement)sp.Children[^1]).Margin = new Thickness(0, 0, 0, 14); }
            try
            {
                var ch = Dry();
                sp.Children.Add(Card(ChangeList(ch), mode == StartMode.Edit ? $"Changes to quest {d.Title}" : $"New quest \"{d.Title}\"", mode == StartMode.CopyAsNew ? "A copy: the game's quest stays as it is." : null));
                if (notes.Count > 0) sp.Children.Add(U.Callout(string.Join("\n", notes.Distinct().Select(n => "• " + n)), "info", "Also"));
            }
            catch (Exception ex) { sp.Children.Add(U.Callout(Friendly.Text(ex), "bad", "Couldn't prepare it")); }
            var stage = U.Btn("Stage it", Stage, "BtnPrimary", "Puts these changes in the staged list. Review and Apply them on the Changes screen.");
            stage.IsEnabled = problems.Count == 0; stage.HorizontalAlignment = HorizontalAlignment.Left; stage.Margin = new Thickness(0, 10, 0, 0);
            sp.Children.Add(stage);
            return sp;
        }

        void Stage()
        {
            try
            {
                notes = new List<string>();
                using var db = S.OpenRead(S.Profile.WorldDb);
                var (orig, rows) = Prepare(db, S.Changes.Pending);
                var ch = QuestBuilder.Stage(mode, orig, rows, S.Changes.Pending);
                foreach (var c in ch.Where(c => c.Table == "quest_template" && c.Kind == ChangeKind.Insert)) c.Summary = $"New quest #{d.Id} \"{d.Title}\"";
                S.SaveChanges();
                Staged = true; StagedId = d.Id;
                w.Toast($"Staged {ch.Count} change(s) for \"{d.Title}\". Review and Apply them on the Changes screen.", "ok");
                Close();
            }
            catch (Exception ex) { w.Toast("Not staged: " + Friendly.Text(ex), "bad"); }
        }

        protected override UIElement BuildPreview()
        {
            string Fill(string s) => (s ?? "").Replace("$N", "Aldren").Replace("$n", "Aldren").Replace("$C", "Warrior").Replace("$c", "warrior").Replace("$R", "Human").Replace("$r", "human").Replace("$B", "\n").Replace("$b", "\n");
            var parch = new Border { Background = U.R("Parchment"), CornerRadius = new CornerRadius(12), Padding = new Thickness(18, 16, 18, 16) };
            var ink = U.R("Parchment.Ink"); var head = U.R("Parchment.Head");
            TextBlock P(string t, double size = 13, bool bold = false, Brush b = null) => new TextBlock { Text = t, FontSize = size, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, Foreground = b ?? ink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), FontFamily = new FontFamily("Georgia") };
            var sp = new StackPanel();
            sp.Children.Add(P(string.IsNullOrWhiteSpace(d.Title) ? "(no name)" : d.Title, 17, true, head));
            sp.Children.Add(P(Fill(d.Description)));
            sp.Children.Add(P("Quest objectives", 14, true, head));
            sp.Children.Add(P(Fill(string.IsNullOrWhiteSpace(d.Summary) ? Summary() : d.Summary)));
            foreach (var o in d.Objectives)
            {
                var name = o.Kind == "collect" ? (o.Target > 0 ? refs.Name("item", o.Target) : o.NewItemName) : o.Kind == "use" ? refs.Name("gameobject", o.Target) : o.Kind == "explore" ? "Place explored" : refs.Name("creature", o.Target);
                sp.Children.Add(P($"• {(string.IsNullOrWhiteSpace(o.Text) ? (o.Kind == "talk" ? "Speak with " + name : o.Kind == "kill" ? name + " slain" : name ?? "?") : o.Text)}: 0/{o.Count}", 12.5));
            }
            sp.Children.Add(P("Rewards", 14, true, head));
            if (d.Items.Count > 0) sp.Children.Add(P("You will receive: " + string.Join(", ", d.Items.Select(i => (i.count > 1 ? i.count + " × " : "") + (refs.Name("item", i.item) ?? "?"))), 12.5));
            if (d.Choices.Count > 0) sp.Children.Add(P("You will be able to choose one of these: " + string.Join(", ", d.Choices.Select(i => refs.Name("item", i.item) ?? "?")), 12.5));
            if (d.Money > 0) sp.Children.Add(P("Money: " + MoneyText(d.Money), 12.5));
            if (d.XpSize > 0) sp.Children.Add(P("Experience: " + QuestBuilder.XpNames[Math.Min(8, d.XpSize)].ToLowerInvariant(), 12.5));
            foreach (var (f, a) in d.Reputation) sp.Children.Add(P($"+{a} reputation with {refs.Name("faction", f) ?? "?"}", 12.5));
            parch.Child = sp;
            var who = U.T((d.Giver == 0 ? "No giver yet" : $"From {refs.Name(d.Giver < 0 ? "gameobject" : "creature", Math.Abs(d.Giver))}") + $" · level {d.Level} (from {d.MinLevel})" + (d.Repeat != "Once" ? " · " + d.Repeat.ToLowerInvariant() : ""), 12.5, "Muted", null, true, new Thickness(0, 10, 0, 0));
            return U.Stack(parch, who);
        }
        string Summary() { try { using var db = S.OpenRead(S.Profile.WorldDb); return QuestBuilder.AutoSummary(d, db); } catch { return ""; } }
        public static string MoneyText(long c) => string.Join(" ", new[] { c / 10000 > 0 ? $"{c / 10000} gold" : null, c / 100 % 100 > 0 ? $"{c / 100 % 100} silver" : null, c % 100 > 0 ? $"{c % 100} copper" : null }.Where(x => x != null));

        public void ScreensGo(int s) => Go(s);
        public void ScreensStage() => Stage();
        public void ScreensSample()
        {
            d.Title = "The Kobold Menace"; d.Level = 7; d.MinLevel = 5; d.Description = "$N, kobolds have overrun the old mine. Thin their numbers and bring back their candles."; d.Money = 3500; d.XpSize = 5;
            d.Objectives.Clear(); d.Objectives.Add(new Objective { Kind = "kill", Target = 6, Count = 10 }); d.Objectives.Add(new Objective { Kind = "collect", NewItemName = "Kobold Candle", Count = 6, DropFrom = 6, DropChance = 45 });
            d.Giver = 197; d.Completion = "The mine is safer thanks to you, $N."; d.Choices.Add((2385, 1)); d.Choices.Add((2386, 1));
        }
    }

    // ============================================================================ Make an NPC
    public sealed class NpcBuildWindow : StepWindow
    {
        string start = "new";
        StartMode mode = StartMode.New;
        long sourceId;
        NpcBuilder.NpcRows original;
        NpcDraft d = new NpcDraft { Display = 0, Services = new HashSet<string> { "Talk" } };
        ModelViewer viewer;
        public NpcBuildWindow(MainWindow owner, long openId = 0) : base(owner, "Make or change an NPC", "NPC builder")
        {
            previewTitle = "How it looks"; GuideKey = "npcbuilder";
            Steps.Add(("Start", StepStart));
            Steps.Add(("What it is", StepKind));
            Steps.Add(("Name and look", StepLook));
            Steps.Add(("Level and strength", StepStrength));
            Steps.Add(("What it offers", StepServices));
            Steps.Add(("Where it stands", StepWhere));
            Steps.Add(("Review and stage", StepReview));
            if (openId > 0) { start = openId >= S.Profiles.Settings.CustomIdStart ? "mine" : "game"; sourceId = openId; mode = start == "mine" ? StartMode.Edit : StartMode.CopyAsNew; LoadSource(); }
            Go(openId > 0 ? 1 : 0);
        }

        UIElement StepStart()
        {
            var sp = new StackPanel();
            sp.Children.Add(U.T("Pick how to begin. Nothing reaches your server until you stage it and Apply it on the Changes screen.", 13.5, "Muted", null, true, new Thickness(0, 0, 0, 16)));
            sp.Children.Add(StartChoices("npc", () => start, k => { start = k; mode = k == "new" ? StartMode.New : k == "mine" ? StartMode.Edit : StartMode.CopyAsNew; if (k == "new") { original = null; sourceId = 0; d = new NpcDraft { Services = new HashSet<string> { "Talk" } }; } Go(0); }, new[]
            {
                ("new", "A brand-new NPC", "Start from nothing: you pick what it is, how it looks and where it stands."),
                ("mine", "Change an NPC I made", "Load one of your own creatures and change it."),
                ("game", "Start from a game NPC", "Load one of the game's creatures: copy it as a new NPC (recommended), or change the original."),
            }));
            if (start != "new")
            {
                sp.Children.Add(Card(Picker("creature", sourceId, id => { sourceId = id; LoadSource(); Go(0); }, "a creature", false), "Which creature"));
                if (start == "game")
                    sp.Children.Add(Card(Choice(new[] { "Copy it as a new NPC (the original stays)", "Change the game's NPC itself" }, mode == StartMode.Edit ? "Change the game's NPC itself" : "Copy it as a new NPC (the original stays)",
                        v => mode = v.StartsWith("Change") ? StartMode.Edit : StartMode.CopyAsNew, 420), "How"));
            }
            return sp;
        }

        void LoadSource()
        {
            if (sourceId <= 0) return;
            try
            {
                using var db = S.OpenRead(S.Profile.WorldDb);
                original = NpcBuilder.Load(db, sourceId);
                if (original == null) { w.Toast($"Creature {sourceId} wasn't found.", "warn"); return; }
                d = NpcBuilder.ToDraft(original);
            }
            catch (Exception ex) { w.Toast("Couldn't read the creature: " + Friendly.Text(ex), "bad"); }
        }

        UIElement StepKind()
        {
            return StartChoices("kind", () => d.Kind, k =>
            {
                d.Kind = k;
                d.Services = new HashSet<string>(NpcBuilder.DefaultServices(k));
                if (k == "Enemy" && d.Side is "Everyone" or "Alliance" or "Horde") d.Side = "Hostile";
                if (k != "Enemy" && k != "Critter" && d.Side == "Hostile") d.Side = "Everyone";
                Go(step);
            }, NpcBuilder.Kinds.Select(k => (k.key, k.key, k.what)).ToArray());
        }

        UIElement StepLook()
        {
            var sp = new StackPanel();
            sp.Children.Add(Field("Name", null, Text(d.Name, v => d.Name = v)));
            sp.Children.Add(Field("Title under the name (optional)", "e.g. \"Weapon Merchant\" shows as <Weapon Merchant>.", Text(d.Title, v => d.Title = v)));
            sp.Children.Add(Field("Look (model)", "Search models by their file name (e.g. human, orc, wolf, guard).", Picker("creaturedisplay", d.Display, v => { d.Display = v; Refresh(); }, "a look", false)));
            sp.Children.Add(Field("Size", "1 = normal.", Number(d.Scale, v => d.Scale = v)));
            sp.Children.Add(U.Row(Field("Main hand weapon", null, Picker("item", d.Weapon1, v => d.Weapon1 = v, "a weapon")), new Border { Width = 10 }));
            sp.Children.Add(Field("Off hand (shield or weapon)", null, Picker("item", d.Weapon2, v => d.Weapon2 = v, "an off-hand")));
            return sp;
        }

        UIElement StepStrength()
        {
            var sp = new StackPanel();
            sp.Children.Add(U.Row(Field("Lowest level", null, Number(d.MinLevel, v => d.MinLevel = (int)v)), new Border { Width = 16 }, Field("Highest level", "Each spawn picks a level in between.", Number(d.MaxLevel, v => d.MaxLevel = (int)v))));
            sp.Children.Add(Field("Who it's friendly to", null, Choice(NpcBuilder.Sides.Select(s => s.key + " — " + s.what), NpcBuilder.Sides.Where(s => s.key == d.Side).Select(s => s.key + " — " + s.what).FirstOrDefault(), v => d.Side = v.Split(" — ")[0], 520)));
            sp.Children.Add(Field("How tough", "Health and damage come from its level; this multiplies them.", Choice(NpcBuilder.Strengths.Select(s => s.key + " — " + s.what), NpcBuilder.Strengths.Where(s => s.key == d.Strength).Select(s => s.key + " — " + s.what).FirstOrDefault(), v => d.Strength = v.Split(" — ")[0], 520)));
            return sp;
        }

        UIElement StepServices()
        {
            var sp = new StackPanel();
            var wrap = new WrapPanel();
            foreach (var (key, bit, what) in NpcBuilder.Services)
            {
                var k = key;
                var cb = new CheckBox { IsChecked = d.Services.Contains(key), Content = U.Stack(U.T(key, 13.5, "Text", FontWeights.SemiBold, false), U.T(what, 11.5, "Muted", null, true)), Width = 250, Margin = new Thickness(0, 0, 16, 12), ToolTip = U.Technical ? $"npcflag bit {bit}" : null };
                cb.Checked += (s, e) => { d.Services.Add(k); Go(step); }; cb.Unchecked += (s, e) => { d.Services.Remove(k); Go(step); };
                wrap.Children.Add(cb);
            }
            sp.Children.Add(Card(wrap, "Services", "What players see when they talk to it."));
            sp.Children.Add(Card(Text(d.Greeting, v => d.Greeting = v, 620, true), "What it says when you talk to it", "Optional. Shown in its chat window."));
            if (d.Services.Contains("Vendor"))
            {
                var list = new StackPanel();
                var refs = new Refs(S);
                for (int i = 0; i < d.Sells.Count; i++) { int ii = i; list.Children.Add(U.Row(U.T(refs.Name("item", d.Sells[i]) ?? "?", 13, "Text", null, false, new Thickness(0, 6, 12, 0)), U.Btn("Remove", () => { d.Sells.RemoveAt(ii); Go(step); }, "BtnGhost"))); }
                list.Children.Add(Picker("item", 0, v => { if (v > 0) { d.Sells.Add(v); Go(step); } }, "an item to sell", false));
                sp.Children.Add(Card(list, "What it sells", "Prices come from each item."));
            }
            if (d.Services.Contains("Trainer"))
                sp.Children.Add(Card(Picker("creature", d.TeachesLike, v => d.TeachesLike = v, "a trainer to copy"), "What it teaches", "Pick an existing trainer: this NPC teaches the same things."));
            return sp;
        }

        UIElement StepWhere()
        {
            var sp = new StackPanel();
            sp.Children.Add(U.T("Stand where you want it in the game, type .gps, and paste what it says here (or type \"map x y z\").", 13.5, "Muted", null, true, new Thickness(0, 0, 0, 12)));
            var paste = U.Box(""); paste.Width = 620; paste.Height = 80; paste.AcceptsReturn = true; paste.TextWrapping = TextWrapping.Wrap;
            var add = U.Btn("Add this place", () =>
            {
                var s = Spawn.Parse(paste.Text);
                if (s == null) { w.Toast("No coordinates found. Paste the whole .gps reply, or type map x y z.", "warn"); return; }
                d.Spawns.Add(s); Go(step);
            }, "BtnPrimary");
            sp.Children.Add(Card(U.Stack(paste, new Border { Height = 10 }, add), "Add a place"));
            var list = new StackPanel();
            var maps = new Refs(S);
            for (int i = 0; i < d.Spawns.Count; i++) { int ii = i; var s = d.Spawns[i]; list.Children.Add(U.Row(U.T($"{maps.Name("map", s.Map) ?? "Map " + s.Map} · {s.X:0.#}, {s.Y:0.#}, {s.Z:0.#}", 13, "Text", null, false, new Thickness(0, 6, 12, 0)), U.Btn("Remove", () => { d.Spawns.RemoveAt(ii); Go(step); }, "BtnGhost"))); }
            if (d.Spawns.Count == 0) list.Children.Add(U.T("Nowhere yet: it exists but isn't placed in the world.", 13, "Muted"));
            sp.Children.Add(Card(list, "Where it stands"));
            sp.Children.Add(U.Row(Field("Wanders around (yards)", "0 = stands still.", Number(d.Wander, v => d.Wander = v)), new Border { Width = 16 }, Field("Comes back after (seconds)", "After it's killed.", Number(d.RespawnSeconds, v => d.RespawnSeconds = (int)v))));
            return sp;
        }

        List<string> notes = new List<string>();
        (NpcBuilder.NpcRows orig, NpcBuilder.NpcRows rows) Prepare(ISqlReader db, Changeset cs)
        {
            BRow Blank(string t) { try { var keys = t switch { "creature_template" => new[] { "entry" }, "creature" => new[] { "guid" }, "creature_template_model" => new[] { "CreatureID", "Idx" }, "creature_equip_template" => new[] { "CreatureID", "ID" }, "npc_text" => new[] { "ID" }, _ => new[] { "ID" } }; var r = Rows.Blank(db, t, keys); return r.Cols.Count == 0 ? null : r; } catch { return null; } }
            NpcBuilder.NpcRows baseRows;
            if (mode == StartMode.Edit && original != null) baseRows = original;
            else
            {
                d.Entry = Rows.FreeId(db, cs, "creature_template", "entry", S.Profiles.Settings.CustomIdStart);
                baseRows = original != null ? NpcBuilder.AsCopy(original, d.Entry) : new NpcBuilder.NpcRows { Template = Blank("creature_template") ?? throw new InvalidOperationException("creature_template couldn't be read.") };
            }
            return (original, NpcBuilder.Apply(d, baseRows, db, cs, notes, Blank));
        }

        UIElement StepReview()
        {
            var sp = new StackPanel();
            var problems = NpcBuilder.Check(d);
            if (problems.Count > 0) { sp.Children.Add(U.Callout(string.Join("\n", problems.Select(p => "• " + p)), "warn", "Before you stage")); ((FrameworkElement)sp.Children[^1]).Margin = new Thickness(0, 0, 0, 14); }
            try
            {
                notes = new List<string>();
                using var db = S.OpenRead(S.Profile.WorldDb);
                var (o, r) = Prepare(db, S.Changes.Pending);
                var ch = NpcBuilder.Stage(mode, o, r, new Changeset());
                sp.Children.Add(Card(ChangeList(ch), mode == StartMode.Edit ? $"Changes to {d.Name}" : $"New NPC \"{d.Name}\"", mode == StartMode.CopyAsNew ? "A copy: the game's creature stays as it is." : null));
                if (notes.Count > 0) sp.Children.Add(U.Callout(string.Join("\n", notes.Distinct().Select(n => "• " + n)), "info", "Also"));
            }
            catch (Exception ex) { sp.Children.Add(U.Callout(Friendly.Text(ex), "bad", "Couldn't prepare it")); }
            var stage = U.Btn("Stage it", Stage, "BtnPrimary"); stage.IsEnabled = problems.Count == 0 || problems.All(p => p.Contains("sells nothing")); stage.HorizontalAlignment = HorizontalAlignment.Left; stage.Margin = new Thickness(0, 10, 0, 0);
            sp.Children.Add(stage);
            return sp;
        }

        void Stage()
        {
            try
            {
                notes = new List<string>();
                using var db = S.OpenRead(S.Profile.WorldDb);
                var (o, r) = Prepare(db, S.Changes.Pending);
                var ch = NpcBuilder.Stage(mode, o, r, S.Changes.Pending);
                foreach (var c in ch.Where(c => c.Table == "creature_template" && c.Kind == ChangeKind.Insert)) c.Summary = $"New NPC #{d.Entry} \"{d.Name}\"";
                S.SaveChanges();
                w.Toast($"Staged {ch.Count} change(s) for \"{d.Name}\". Review and Apply them on the Changes screen.", "ok");
                Close();
            }
            catch (Exception ex) { w.Toast("Not staged: " + Friendly.Text(ex), "bad"); }
        }

        long shownDisplay = -1;
        protected override UIElement BuildPreview()
        {
            viewer ??= new ModelViewer(S.Models, 380);
            if (d.Display != shownDisplay)
            {
                shownDisplay = d.Display;
                try { if (d.Display > 0) viewer.Load(S.Models.Creature(d.Display)); else viewer.ShowMessage("Pick a look on the Name and look step."); } catch (Exception ex) { viewer.ShowMessage(Friendly.Text(ex)); }
            }
            if (viewer.View is FrameworkElement fe && fe.Parent is Panel old) old.Children.Remove(fe);
            var name = U.T(string.IsNullOrWhiteSpace(d.Name) ? "(no name)" : d.Name, 17, "Ok", FontWeights.SemiBold, false);
            var sp = U.Stack(name);
            if (!string.IsNullOrWhiteSpace(d.Title)) sp.Children.Add(U.T("<" + d.Title + ">", 13, "Muted", null, false));
            sp.Children.Add(U.T($"Level {d.MinLevel}{(d.MaxLevel != d.MinLevel ? "–" + d.MaxLevel : "")} · {d.Strength} · friendly to {d.Side.ToLowerInvariant()}", 12.5, "Muted", null, true, new Thickness(0, 4, 0, 10)));
            sp.Children.Add(viewer.View);
            if (d.Services.Count > 0) sp.Children.Add(U.T("Offers: " + string.Join(", ", d.Services), 12.5, "Text", null, true, new Thickness(0, 10, 0, 0)));
            if (!string.IsNullOrWhiteSpace(d.Greeting)) sp.Children.Add(U.T("\"" + d.Greeting + "\"", 12.5, "Amber", null, true, new Thickness(0, 6, 0, 0)));
            sp.Children.Add(U.T(d.Spawns.Count == 0 ? "Not placed in the world yet." : $"Stands in {d.Spawns.Count} place(s).", 12.5, "Muted", null, true, new Thickness(0, 6, 0, 0)));
            return sp;
        }

        public void ScreensGo(int s) => Go(s);
        public void ScreensStage() => Stage();
        public void ScreensSample() { d.Name = "Brannock Ironsong"; d.Title = "Weapon Merchant"; d.Kind = "Vendor"; d.Services = new HashSet<string> { "Talk", "Vendor", "Repairs" }; d.Display = 3258; d.MinLevel = d.MaxLevel = 30; d.Greeting = "Steel for the brave, $N."; d.Sells.Add(2488); d.Spawns.Add(new Spawn { Map = 0, X = -8823.5, Y = 640.2, Z = 94.3 }); }
    }

    // ============================================================================ Make it do something (behaviours)
    public sealed class BehaviourWindow : StepWindow
    {
        long creature;
        string key = Behaviours.All[0].Key;
        readonly Dictionary<string, string> values = new Dictionary<string, string>();

        public BehaviourWindow(MainWindow owner, long creatureId = 0) : base(owner, "Make it do something", "Behaviour builder")
        {
            creature = creatureId;
            previewTitle = "In plain words"; GuideKey = "behaviourbuilder";
            Steps.Add(("Who", StepWho));
            Steps.Add(("What it does", StepWhat));
            Steps.Add(("The details", StepDetails));
            Steps.Add(("Review and stage", StepReview));
            Defaults();
            Go(creature > 0 ? 1 : 0);
        }

        void Defaults() { values.Clear(); foreach (var i in Behaviours.All.First(b => b.Key == key).Inputs) values[i.key] = i.def ?? ""; }

        UIElement StepWho() => U.Stack(U.T("Which creature should do it? It can be one of yours or one of the game's; its other behaviours stay.", 13.5, "Muted", null, true, new Thickness(0, 0, 0, 14)),
            Card(Picker("creature", creature, v => creature = v, "a creature", false), "Creature"));

        UIElement StepWhat()
        {
            return StartChoices("behaviour", () => key, k => { key = k; Defaults(); Go(step); }, Behaviours.All.Select(b => (b.Key, b.Name, b.What)).ToArray());
        }

        UIElement StepDetails()
        {
            var b = Behaviours.All.First(x => x.Key == key);
            var sp = new StackPanel();
            if (b.Inputs.Count == 0) sp.Children.Add(U.T("Nothing to fill in for this one.", 13.5, "Muted"));
            foreach (var i in b.Inputs)
            {
                var ii = i;
                FrameworkElement c = i.kind switch
                {
                    "choice" => Choice(i.choices, values.TryGetValue(i.key, out var cv) ? cv : i.def, v => values[ii.key] = v),
                    "spell" => Picker("spell", long.TryParse(values.GetValueOrDefault(i.key), out var sv) ? sv : 0, v => values[ii.key] = v.ToString(CultureInfo.InvariantCulture), "a spell", false),
                    "creature" => Picker("creature", long.TryParse(values.GetValueOrDefault(i.key), out var cr) ? cr : 0, v => values[ii.key] = v.ToString(CultureInfo.InvariantCulture), "a creature", false),
                    "emote" => Choice(Behaviours.Emotes.Select(e => e.name), Behaviours.Emotes.FirstOrDefault(e => e.id.ToString() == values.GetValueOrDefault(i.key)).name ?? "Talk", v => values[ii.key] = Behaviours.Emotes.First(e => e.name == v).id.ToString()),
                    "text" => Text(values.GetValueOrDefault(i.key), v => values[ii.key] = v, 560),
                    _ => Text(values.GetValueOrDefault(i.key), v => values[ii.key] = v, 120),
                };
                sp.Children.Add(Field(i.label, i.hint, c));
            }
            return sp;
        }

        Behaviours.Result Make(ISqlReader db, Changeset cs, out string name, out BRow before, out BRow after)
        {
            var c = Rows.One(db, "creature_template", new[] { "entry" }, "entry = " + creature) ?? throw new InvalidOperationException("Pick a creature first.");
            name = c.S("name");
            var existing = Rows.Read(db, "smart_scripts", SmartRow.KeyColumns, $"entryorguid = {creature} AND source_type = 0", 500);
            int staged = cs.Changes.Count(x => x.Kind == ChangeKind.Insert && x.Table == "smart_scripts" && x.Key.Any(k => k.Column == "entryorguid" && k.Value == creature.ToString(CultureInfo.InvariantCulture)));
            int next = existing.Select(x => (int)x.L("id")).DefaultIfEmpty(-1).Max() + 1 + staged;
            var texts = Rows.Read(db, "creature_text", new[] { "CreatureID", "GroupID", "ID" }, "CreatureID = " + creature, 500);
            int stagedTexts = cs.Changes.Count(x => x.Kind == ChangeKind.Insert && x.Table == "creature_text" && x.Key.Any(k => k.Column == "CreatureID" && k.Value == creature.ToString(CultureInfo.InvariantCulture)));
            int group = texts.Select(x => (int)x.L("GroupID")).DefaultIfEmpty(-1).Max() + 1 + stagedTexts;
            var res = Behaviours.Make(key, values, creature, name, next, group);
            before = c; after = c.Clone();
            if (after.S("AIName") != "SmartAI") after["AIName"] = "SmartAI";
            if (key == "talk_credit" && (after.L("npcflag") & 1) == 0) after["npcflag"] = after.L("npcflag") | 1;
            return res;
        }

        UIElement StepReview()
        {
            var sp = new StackPanel();
            var problems = creature <= 0 ? new List<string> { "Pick a creature first." } : Behaviours.Check(key, values);
            if (problems.Count > 0) { sp.Children.Add(U.Callout(string.Join("\n", problems.Select(p => "• " + p)), "warn", "Before you stage")); ((FrameworkElement)sp.Children[^1]).Margin = new Thickness(0, 0, 0, 14); }
            else
            {
                try
                {
                    using var db = S.OpenRead(S.Profile.WorldDb);
                    var res = Make(db, S.Changes.Pending, out var name, out var before, out var after);
                    if (before.S("ScriptName").Length > 0) sp.Children.Add(U.Callout($"{name} has its own program script, which runs instead of behaviours like this one. Clear its program script (Creatures › Extras) for this to work.", "warn", "Heads up"));
                    var ch = Rows.Stage(new[] { before }, res.Scripts.Select(Smart.ToRow).Concat(res.Texts).Append(after), new Changeset(), "SmartAI");
                    sp.Children.Add(Card(ChangeList(ch), $"For {name}"));
                }
                catch (Exception ex) { sp.Children.Add(U.Callout(Friendly.Text(ex), "bad", "Couldn't prepare it")); }
            }
            var stage = U.Btn("Stage it", Stage, "BtnPrimary"); stage.IsEnabled = problems.Count == 0; stage.HorizontalAlignment = HorizontalAlignment.Left; stage.Margin = new Thickness(0, 10, 0, 0);
            sp.Children.Add(stage);
            return sp;
        }

        void Stage()
        {
            try
            {
                using var db = S.OpenRead(S.Profile.WorldDb);
                var res = Make(db, S.Changes.Pending, out var name, out var before, out var after);
                var ch = Rows.Stage(new[] { before }, res.Scripts.Select(Smart.ToRow).Concat(res.Texts).Append(after), S.Changes.Pending, "SmartAI");
                foreach (var c in ch.Where(c => c.Table == "smart_scripts")) c.Summary = $"{name}: {Behaviours.All.First(b => b.Key == key).Name.ToLowerInvariant()}";
                S.SaveChanges();
                w.Toast($"Staged \"{Behaviours.All.First(b => b.Key == key).Name}\" for {name}. Review and Apply it on the Changes screen.", "ok");
                Close();
            }
            catch (Exception ex) { w.Toast("Not staged: " + Friendly.Text(ex), "bad"); }
        }

        protected override UIElement BuildPreview()
        {
            var b = Behaviours.All.First(x => x.Key == key);
            var refs = new Refs(S);
            var who = creature > 0 ? refs.Name("creature", creature) ?? "the creature" : "The creature";
            Behaviours.Result res;
            try { res = Behaviours.Make(key, values, creature, who, 0, 0); } catch { return U.T(b.What, 13.5); }
            var sp = new StackPanel();
            sp.Children.Add(U.T(b.Name, 16, "Text", FontWeights.SemiBold, true, new Thickness(0, 0, 0, 8)));
            foreach (var r in res.Scripts)
            {
                var sentence = SmartAI.Sentence(r, (k, id) => refs.ByKind(k, id)).TrimEnd('.');
                var line = res.Texts.FirstOrDefault()?.S("Text");
                if (line != null) sentence = System.Text.RegularExpressions.Regex.Replace(sentence, @"say line group \d+", (res.Texts[0].L("Type") == 14 ? "yell" : "say") + " \"" + line + "\"");
                if (!sentence.StartsWith("When", StringComparison.OrdinalIgnoreCase)) sentence = "When " + sentence;
                sp.Children.Add(U.Soft(U.T(sentence + ".", 13.5, "Text", null, true), new Thickness(14, 10, 14, 10), new Thickness(0, 0, 0, 8), "Panel2"));
            }
            foreach (var t in res.Texts) sp.Children.Add(U.T($"{(t.L("Type") == 14 ? "Yells" : "Says")}: \"{t.S("Text")}\"", 13, "Amber", null, true, new Thickness(0, 4, 0, 0)));
            return sp;
        }

        public void ScreensSet(long c, string k) { creature = c; key = k; Defaults(); Go(2); }
        public void ScreensGo(int s) => Go(s);
        public void ScreensStage() => Stage();
    }
}
