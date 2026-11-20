using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AndersCraftStudio.Core;

namespace AndersCraftStudio
{
    /// <summary>
    /// Asset library / import: icons (PNG/JPG/BMP/TGA/BLP → BLP), creature models (.m2 + .skin + textures), sounds and
    /// any other file, placed into a Client patches work folder and registered in the DBCs as staged rows.
    /// Nothing is written to the game client; the work folder is Studio's own.
    /// </summary>
    public sealed class AssetsPage : IPage
    {
        readonly MainWindow w;
        Studio S => w.S;
        readonly Grid view = new Grid();
        public UIElement View => view;
        readonly ListBox kinds = new ListBox();
        readonly ContentControl form = new ContentControl { Focusable = false };
        readonly ContentControl side = new ContentControl { Focusable = false };
        readonly TextBox workName = U.Box("dbc_edits", true);
        string kind = "icon";
        string source;          // chosen file
        BgraImage picture;      // decoded icon source
        MediaPlayer player;
        string lastMessage;

        public AssetsPage(MainWindow w)
        {
            this.w = w;
            view.Margin = new Thickness(28, 22, 28, 20);
            view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            view.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
            var strip = U.StepStrip(1); DockPanel.SetDock(strip, Dock.Right); strip.VerticalAlignment = VerticalAlignment.Center; head.Children.Add(strip);
            head.Children.Add(U.Stack(U.H1("Asset import"), U.T("Bring your own icons, creature and item models, sounds and other files into a patch. Studio converts them, puts them at the right path in a Client patches work folder, and stages the DBC rows that make the game use them.", 13.5, "Muted")));
            view.Children.Add(head);
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
            Grid.SetRow(g, 1); view.Children.Add(g);
            foreach (var (k, t, d) in new[]
            {
                ("icon", "Icon", "PNG, JPG, BMP, TGA or BLP → a 64×64 BLP in Interface\\Icons, plus a SpellIcon.dbc row (and optionally an item display)."),
                ("model", "Creature model", ".m2 with its .skin files and textures → Creature\\<Name>\\, plus CreatureModelData and CreatureDisplayInfo rows."),
                ("itemmodel", "Item model", ".m2 with its .skin files and textures → Item\\ObjectComponents\\<Slot>\\, plus an ItemDisplayInfo row the Items editor can pick."),
                ("armortex", "Body armour textures", "Pictures for chest, legs, arms, hands, feet → Item\\TextureComponents\\, plus an ItemDisplayInfo row, previewed on a character."),
                ("sound", "Sound", "WAV or MP3 → Sound\\Custom\\, plus a SoundEntries row other tables can point to."),
                ("file", "Any file", "Anything else, at the archive path you choose."),
            })
                kinds.Items.Add(new ListBoxItem { Tag = k, Content = U.Stack(U.T(t, 13.5, "Text", FontWeights.SemiBold, false), U.T(d, 12, "Muted", null, true)) });
            ScrollViewer.SetHorizontalScrollBarVisibility(kinds, ScrollBarVisibility.Disabled);
            kinds.SelectionChanged += (s, e) => { if (kinds.SelectedItem is ListBoxItem li && li.Tag is string k) { kind = k; source = null; picture = null; lastMessage = null; BuildForm(); } };
            workName.TextChanged += (s, e) => BuildSide();
            var wf = U.Field("Patch work folder", "Under Documents\\AndersCraft Studio\\patch_work (or your Studio folder). Use the same name when you send DBC edits out from Changes, then build the patch on Client patches.", workName);
            wf.Margin = new Thickness(0, 14, 0, 0);
            g.Children.Add(U.Card(U.Stack(U.Eyebrow("What to import"), kinds, wf), new Thickness(10, 12, 10, 8), new Thickness(0)));
            var fs = U.Scroll(form); Grid.SetColumn(fs, 2); g.Children.Add(fs);
            var ss = U.Scroll(side); Grid.SetColumn(ss, 4); g.Children.Add(ss);
            kinds.SelectedIndex = 0;
        }

        public void OnShow() => BuildSide();

        string WorkRoot => Path.Combine(w.IsScreens ? Path.Combine(Path.GetTempPath(), "AndersCraftStudio_screens_patches") : S.Profiles.Settings.Workspace(), "patch_work");
        string WorkFolder => Path.Combine(WorkRoot, AssetImport.SafeName(string.IsNullOrWhiteSpace(workName.Text) ? "dbc_edits" : workName.Text.Trim()));
        string ServerDbcDir { get { try { var d = ConfParser.ParseFile(S.Profile.ConfPath).DataDir; return string.IsNullOrEmpty(d) ? null : Path.Combine(d, "dbc"); } catch { return null; } } }
        uint CustomStart => (uint)Math.Min(uint.MaxValue, Math.Max(1, S.Profiles.Settings.CustomIdStart));

        // ------------------------------------------------------------------ side: guidance + manifest
        void BuildSide()
        {
            var sp = new StackPanel();
            var guideText = kind switch
            {
                "icon" => "Icons are 64×64 BLP textures in Interface\\Icons. Studio resizes your picture, converts it (DXT5 keeps soft transparency, DXT1 is smaller, Palette is closest to Blizzard's style) and adds a SpellIcon.dbc row so the Spell editor's icon picker shows it. For items, it can also copy an ItemDisplayInfo row with the new icon; use that display ID on item_template.displayid.",
                "model" => "A creature model is an .m2 file, its .skin files (Name00.skin … Name03.skin) and its textures. Studio copies them to Creature\\<Name>\\, renames the skins to match, converts PNG/TGA textures to BLP, and stages a CreatureModelData row (copied from a base model, for collision and size) and a CreatureDisplayInfo row. Put the new display ID in creature_template_model.CreatureDisplayID (or modelid on older databases).",
                "itemmodel" => "An item model (weapon, shield, helm, shoulders…) is an .m2 file, its .skin files and its textures, placed in Item\\ObjectComponents\\<Slot>\\. Studio copies them there, converts PNG/TGA textures to BLP, and stages an ItemDisplayInfo row copied from a base display (for sounds, geosets and the icon) that points at the new model and texture. The new display shows up in the Items editor's Display tab picker as \"icon · model name\"; Studio can also stage it on an item for you. Body armour (chest, legs…) has no model of its own: it uses textures on the character, which this import doesn't cover.",
                "armortex" => "Body armour (chest, legs, gloves, boots, bracers, belt, robes) has no model: the game paints it onto the character from up to eight pictures, one per body region (upper arm, lower arm, hand, upper torso, lower torso, upper leg, lower leg, foot). Studio converts your pictures to BLP at Item\\TextureComponents\\<Region>Texture\\Name_U.blp and stages an ItemDisplayInfo row (copied from a base display for sounds and geosets) that uses them. The preview paints them on a human; regions you leave empty keep the base display's textures unless you tick the box.",
                "sound" => "Sounds are WAV or MP3 files (the formats Blizzard's own 3.3.5a sounds use; Studio doesn't import OGG). Studio copies the file to Sound\\Custom\\ and stages a SoundEntries row (volume and distances copied from a base sound). Use its ID wherever a SoundID is asked for: SmartAI's Play sound, creature_text.Sound, DBC columns.",
                _ => "Any file can go into a patch at any path. Check the table below for where the client looks for each kind of file. The file is copied into the work folder; nothing is registered in a DBC.",
            };
            sp.Children.Add(GuideCard.Make("How it fits together", guideText, "Imported files go into Studio's work folder, never into the game client. DBC rows are staged: send them out from Changes into the same work folder, then build and install the patch on Client patches."));
            var g = new StackPanel();
            g.Children.Add(U.H2("Where files go"));
            foreach (var (k, folder, what) in AssetImport.Guidance) g.Children.Add(U.CheckRow(U.Check.Info, k, folder + "  —  " + what, null, 105));
            sp.Children.Add(U.Card(g, new Thickness(16, 14, 16, 10), new Thickness(0, 12, 0, 12)));
            var man = AssetImport.ReadManifest(WorkFolder);
            var m = new StackPanel();
            m.Children.Add(U.H2($"In this work folder ({man.Count})"));
            if (man.Count == 0) m.Children.Add(U.T("Nothing imported yet.", 12.5, "Muted"));
            foreach (var r in man.OrderByDescending(x => x.Utc).Take(30))
                m.Children.Add(U.CheckRow(U.Check.Ok, r.Kind, r.ArchivePath + (r.DbcRows.Count > 0 ? "  ·  " + string.Join(", ", r.DbcRows) : ""), r.Source, 70));
            var open = U.Btn("Open work folder", () => { Directory.CreateDirectory(WorkFolder); Process.Start(new ProcessStartInfo("explorer.exe", "\"" + WorkFolder + "\"") { UseShellExecute = true }); }, "BtnGhost");
            var build = U.Btn("Build the patch…", () => w.Navigate("patches"), null, "Client patches › Build: pick this work folder.");
            var row = U.Row(open, build); row.Margin = new Thickness(0, 10, 0, 0);
            m.Children.Add(row);
            sp.Children.Add(U.Card(m, new Thickness(16, 14, 16, 12), new Thickness(0)));
            side.Content = sp;
        }

        // ------------------------------------------------------------------ forms
        readonly Dictionary<string, Control> f = new Dictionary<string, Control>();
        TextBox Box(string key, string text, bool mono = true) { var b = U.Box(text, mono); f[key] = b; return b; }
        string Val(string key) => f.TryGetValue(key, out var c) ? (c as TextBox)?.Text?.Trim() ?? "" : "";
        bool Ticked(string key) => f.TryGetValue(key, out var c) && c is CheckBox cb && cb.IsChecked == true;

        static FrameworkElement Fld(string label, string hint, FrameworkElement c, double w = 260) { var x = U.Field(label, hint, c, null, w); x.Margin = new Thickness(0, 0, 16, 12); return x; }

        void BuildForm()
        {
            f.Clear();
            var sp = new StackPanel();
            var pick = U.Btn(source == null ? "Choose a file…" : "Choose another file…", ChooseFile, "BtnPrimary");
            if (kind != "armortex")
            {
                sp.Children.Add(U.Row(pick, U.T(source == null ? "No file chosen." : source, 12.5, source == null ? "Muted" : "Text", null, true, new Thickness(10, 0, 0, 0))));
                ((FrameworkElement)((StackPanel)sp.Children[0]).Children[1]).VerticalAlignment = VerticalAlignment.Center;
                sp.Children.Add(new Border { Height = 14 });
            }
            string stem = source == null ? "" : AssetImport.SafeName(Path.GetFileNameWithoutExtension(source));
            switch (kind)
            {
                case "icon":
                {
                    if (picture != null)
                    {
                        var small = BlpEncoder.Resize(picture, 64, 64);
                        var img = new Image { Width = 64, Height = 64, Source = Bmp(small), Margin = new Thickness(0, 0, 16, 0) };
                        var big = new Image { Width = 128, Height = 128, Source = Bmp(small), Margin = new Thickness(0, 0, 16, 0) };
                        RenderOptions.SetBitmapScalingMode(big, BitmapScalingMode.NearestNeighbor);
                        sp.Children.Add(U.Row(big, img, U.T($"Source {picture.Width}×{picture.Height} → 64×64" + (picture.Pixels.Where((x, i) => i % 4 == 3).Any(a => a < 250) ? " · has transparency" : ""), 12.5, "Muted", null, true)));
                        sp.Children.Add(new Border { Height = 14 });
                    }
                    var fmt = new ComboBox { Width = 240 }; foreach (var x in new[] { "DXT5 (soft transparency)", "DXT1 (smallest, no transparency)", "Palette (256 colours + alpha)" }) fmt.Items.Add(x); fmt.SelectedIndex = 0; f["fmt"] = fmt;
                    sp.Children.Add(U.Wrap(Fld("Icon name", "File name in Interface\\Icons (letters, digits, _ and - only).", Box("name", stem.Length > 0 ? "INV_Custom_" + stem : "INV_Custom_Icon")), Fld("Format", "How the BLP is compressed.", fmt)));
                    var spell = new CheckBox { IsChecked = true, Content = U.T("Add a SpellIcon.dbc row (pickable in the Spell editor)", 13, "Text", null, false), Margin = new Thickness(0, 0, 0, 8) }; f["spellicon"] = spell;
                    var item = new CheckBox { IsChecked = false, Content = U.T("Also add an item display with this icon, copied from this ItemDisplayInfo ID:", 13, "Text", null, false), Margin = new Thickness(0, 0, 10, 8), VerticalAlignment = VerticalAlignment.Center }; f["itemdisplay"] = item;
                    var baseBox = Box("itembase", "1542"); baseBox.Width = 110;
                    sp.Children.Add(spell);
                    sp.Children.Add(item);
                    baseBox.Margin = new Thickness(26, 0, 0, 12); baseBox.HorizontalAlignment = HorizontalAlignment.Left;
                    sp.Children.Add(baseBox);
                    break;
                }
                case "model":
                {
                    if (source != null)
                    {
                        try
                        {
                            var info = M2Info.Read(File.ReadAllBytes(source));
                            sp.Children.Add(U.Callout($"Model \"{info.Name}\" · M2 version {info.Version}{(info.Version == 264 ? " (3.3.5a)" : " — the 3.3.5a client expects 264")} · {info.Vertices:N0} vertices · {info.Bones} bones · {info.Sequences} animations · {info.Views} skin view(s)" + (info.Textures.Count > 0 ? "\nTextures: " + string.Join(", ", info.Textures) : ""), info.Version == 264 ? "ok" : "warn", "Model info"));
                        }
                        catch (Exception ex) { sp.Children.Add(U.Callout(ex.Message, "bad")); }
                        var dir = Path.GetDirectoryName(source);
                        var skins = Directory.GetFiles(dir, Path.GetFileNameWithoutExtension(source) + "0?.skin").OrderBy(x => x).ToList();
                        var tex = Directory.GetFiles(dir).Where(x => new[] { ".blp", ".png", ".tga" }.Contains(Path.GetExtension(x).ToLowerInvariant())).OrderBy(x => x).ToList();
                        sp.Children.Add(U.T($"Found next to it: {skins.Count} skin file(s){(skins.Count == 0 ? " — the client needs at least Name00.skin" : "")}, {tex.Count} texture(s) {(tex.Count > 0 ? "(" + string.Join(", ", tex.Select(Path.GetFileName).Take(6)) + ")" : "")}.", 12.5, skins.Count == 0 ? "Bad" : "Muted", null, true, new Thickness(0, 0, 0, 12)));
                        sp.Children.Add(PreviewOf(source));
                    }
                    sp.Children.Add(U.Wrap(
                        Fld("Model name", "Folder and file name under Creature\\.", Box("name", stem.Length > 0 ? stem : "CustomModel")),
                        Fld("Base CreatureModelData ID", "Copied for size, collision and footsteps (e.g. a creature of similar size).", Box("basemodel", "49"), 220),
                        Fld("Base CreatureDisplayInfo ID", "Copied for sounds and blood (e.g. a display of a similar creature).", Box("basedisplay", "49"), 220),
                        Fld("Scale", "CreatureModelScale of the new display.", Box("scale", "1"), 120)));
                    break;
                }
                case "itemmodel":
                {
                    if (source != null)
                    {
                        try
                        {
                            var info = M2Info.Read(File.ReadAllBytes(source));
                            sp.Children.Add(U.Callout($"Model \"{info.Name}\" · M2 version {info.Version}{(info.Version == 264 ? " (3.3.5a)" : " — the 3.3.5a client expects 264")} · {info.Vertices:N0} vertices · {info.Views} skin view(s)" + (info.Textures.Count > 0 ? "\nTextures: " + string.Join(", ", info.Textures) : ""), info.Version == 264 ? "ok" : "warn", "Model info"));
                        }
                        catch (Exception ex) { sp.Children.Add(U.Callout(ex.Message, "bad")); }
                        var dir = Path.GetDirectoryName(source);
                        var skins = Directory.GetFiles(dir, Path.GetFileNameWithoutExtension(source) + "0?.skin").OrderBy(x => x).ToList();
                        var tex = Directory.GetFiles(dir).Where(x => new[] { ".blp", ".png", ".tga" }.Contains(Path.GetExtension(x).ToLowerInvariant())).OrderBy(x => x).ToList();
                        sp.Children.Add(U.T($"Found next to it: {skins.Count} skin file(s){(skins.Count == 0 ? " — the client needs at least Name00.skin" : "")}, {tex.Count} texture(s) {(tex.Count > 0 ? "(" + string.Join(", ", tex.Select(Path.GetFileName).Take(6)) + ")" : "")}. The first texture becomes the model's texture.", 12.5, skins.Count == 0 ? "Bad" : "Muted", null, true, new Thickness(0, 0, 0, 12)));
                        sp.Children.Add(PreviewOf(source));
                    }
                    var slot = new ComboBox { Width = 200 }; foreach (var x in AssetImport.ItemModelSlots) slot.Items.Add(x); slot.SelectedIndex = 0; f["slot"] = slot;
                    sp.Children.Add(U.Wrap(
                        Fld("Model name", "File name in Item\\ObjectComponents\\<Slot>.", Box("name", stem.Length > 0 ? stem : "CustomItem")),
                        Fld("What it is", "Decides the folder the client looks in. Shoulders get the model on both sides.", slot, 220),
                        Fld("Base ItemDisplayInfo ID", "Copied for sounds, geosets, visual effects and the icon (pick an item of the same kind).", Box("basedisplay", "1542"), 220),
                        Fld("Icon (optional)", "An icon file name in Interface\\Icons, e.g. INV_Sword_04. Empty = keep the base display's icon.", Box("icon", "", true), 260),
                        Fld("Use it on item (optional)", "An item_template entry: Studio stages its displayid change too. Empty = just add the display.", Box("useon", ""), 200)));
                    break;
                }
                case "armortex":
                {
                    var names = new[] { "Upper arm", "Lower arm", "Hand", "Upper torso", "Lower torso", "Upper leg", "Lower leg", "Foot" };
                    var grid = new WrapPanel();
                    for (int i = 0; i < 8; i++)
                    {
                        int slot = i;
                        var (tw, th) = AssetImport.ArmorTextureSize(slot);
                        var label = U.T(armorFiles.TryGetValue(slot, out var cur) ? Path.GetFileName(cur) : "none", 12, armorFiles.ContainsKey(slot) ? "Text" : "Faint", null, false);
                        var pickB = U.Btn("Choose…", () =>
                        {
                            var d = new Microsoft.Win32.OpenFileDialog { Filter = "Pictures (*.png;*.jpg;*.jpeg;*.bmp;*.tga;*.blp)|*.png;*.jpg;*.jpeg;*.bmp;*.tga;*.blp" };
                            if (d.ShowDialog(w) == true) { armorFiles[slot] = d.FileName; BuildForm(); }
                        }, null, $"Picture for the {names[slot].ToLowerInvariant()} ({tw}×{th} on a 256×256 skin; any size is resized).");
                        pickB.Padding = new Thickness(8, 4, 8, 4);
                        var clear = U.Btn("×", () => { armorFiles.Remove(slot); BuildForm(); }, "BtnGhost"); clear.Padding = new Thickness(6, 4, 6, 4);
                        grid.Children.Add(Fld(names[slot], $"{tw}×{th}", U.Stack(U.Row(pickB, clear), label), 200));
                    }
                    sp.Children.Add(grid);
                    var only = new CheckBox { Content = U.T("Remove the base display's textures in the regions I leave empty", 13, "Text", null, false), IsChecked = false, Margin = new Thickness(0, 0, 0, 10) }; f["clearothers"] = only;
                    sp.Children.Add(only);
                    sp.Children.Add(U.Wrap(
                        Fld("Name", "File names: Name_U.blp in each region's folder.", Box("name", "CustomArmor")),
                        Fld("Base ItemDisplayInfo ID", "Copied for sounds and geosets (pick a chest or legs display of the same kind).", Box("basedisplay", "9671"), 220),
                        Fld("Use it on item (optional)", "An item_template entry: Studio stages its displayid change too.", Box("useon", ""), 200)));
                    if (armorFiles.Count > 0) sp.Children.Add(ArmorPreview());
                    break;
                }
                case "sound":
                {
                    if (source != null)
                    {
                        var ext = Path.GetExtension(source).ToLowerInvariant();
                        if (ext == ".ogg") sp.Children.Add(U.Callout("Studio imports WAV and MP3, the formats the 3.3.5a client's own sounds use. Convert OGG to MP3 or WAV first (e.g. with Audacity).", "warn"));
                        else
                        {
                            var play = U.Btn("▶ Play", () => { try { player?.Stop(); player = new MediaPlayer(); player.Open(new Uri(source)); player.Play(); } catch (Exception ex) { w.Toast("Can't play it: " + ex.Message, "warn"); } });
                            var stop = U.Btn("■ Stop", () => player?.Stop(), "BtnGhost");
                            sp.Children.Add(U.Row(play, stop, U.T($"{new FileInfo(source).Length / 1024.0:N0} KB", 12.5, "Muted", null, false)));
                            sp.Children.Add(new Border { Height = 12 });
                        }
                    }
                    sp.Children.Add(U.Wrap(
                        Fld("Sound name", "File name in Sound\\Custom and the SoundEntries Name.", Box("name", stem.Length > 0 ? stem : "CustomSound")),
                        Fld("Base SoundEntries ID", "Copied for volume, distances and sound type (e.g. 3 for a short effect).", Box("basesound", "3"), 220)));
                    break;
                }
                default:
                {
                    sp.Children.Add(Fld("Path inside the patch", "E.g. Interface\\Glues\\LoadingScreens\\MyScreen.blp. See \"Where files go\".", Box("path", source == null ? "" : @"Interface\" + Path.GetFileName(source)), 560));
                    break;
                }
            }
            var go = U.Btn(kind == "file" ? "Copy into the work folder" : "Import", Import, "BtnPrimary", "Converts and copies into the work folder, and stages the DBC rows. The game client isn't touched.");
            go.IsEnabled = kind == "armortex" ? armorFiles.Count > 0 : source != null && !(kind == "sound" && Path.GetExtension(source).ToLowerInvariant() == ".ogg");
            go.HorizontalAlignment = HorizontalAlignment.Left;
            sp.Children.Add(go);
            if (lastMessage != null) { var c = U.Callout(lastMessage, "ok", "Imported"); c.Margin = new Thickness(0, 14, 0, 0); sp.Children.Add(c); }
            form.Content = U.Card(sp, new Thickness(20, 16, 20, 16), new Thickness(0));
            BuildSide();
        }

        static BitmapSource Bmp(BgraImage img) { var b = BitmapSource.Create(img.Width, img.Height, 96, 96, PixelFormats.Bgra32, null, img.Pixels, img.Width * 4); b.Freeze(); return b; }

        /// <summary>Any picture → BGRA: PNG/JPG/BMP/GIF through WPF, TGA and BLP with Studio's readers.</summary>
        public static BgraImage LoadPicture(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".blp") return Blp.Decode(bytes);
            if (ext == ".tga") return Tga.Decode(bytes);
            using var ms = new MemoryStream(bytes);
            var frame = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            var conv = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var px = new byte[conv.PixelWidth * conv.PixelHeight * 4];
            conv.CopyPixels(px, conv.PixelWidth * 4, 0);
            return new BgraImage { Width = conv.PixelWidth, Height = conv.PixelHeight, Pixels = px };
        }

        void ChooseFile()
        {
            var filter = kind switch
            {
                "icon" => "Pictures (*.png;*.jpg;*.jpeg;*.bmp;*.tga;*.blp)|*.png;*.jpg;*.jpeg;*.bmp;*.tga;*.blp",
                "model" => "M2 models (*.m2)|*.m2",
                "itemmodel" => "M2 models (*.m2)|*.m2",
                "sound" => "Sounds (*.wav;*.mp3;*.ogg)|*.wav;*.mp3;*.ogg",
                _ => "All files|*.*",
            };
            var d = new Microsoft.Win32.OpenFileDialog { Filter = filter };
            if (d.ShowDialog(w) != true) return;
            UseFile(d.FileName);
        }

        public void UseFile(string path)
        {
            source = path; picture = null; lastMessage = null;
            if (kind == "icon") { try { picture = LoadPicture(path); } catch (Exception ex) { w.Toast("Can't read that picture: " + ex.Message, "bad"); source = null; } }
            BuildForm();
        }

        // ------------------------------------------------------------------ import
        (DbcTable baseT, DbcTable work)? Dbc(string file)
        {
            var dir = ServerDbcDir;
            var p = dir == null ? null : Path.Combine(dir, file);
            if (p == null || !File.Exists(p)) return null;
            var b = DbcTable.Load(p);
            var probs = new List<string>();
            var wk = DbcPatcher.Apply(b, S.Changes.Pending.Changes, false, probs) ?? b.Clone();
            return (b, wk);
        }

        void StageRows(string file, Func<DbcTable, DbcRow> make, List<string> added)
        {
            var t = Dbc(file) ?? throw new InvalidOperationException($"The server's {file} wasn't found (worldserver.conf DataDir\\dbc).");
            var r = make(t.work);
            t.work.Rows.Add(r); t.work.InvalidateIndex();
            DbcStager.Restage(S.Changes.Pending, t.baseT, t.work, "server");
            added.Add($"{file} {r.V[0]}");
        }

        public void Import()
        {
            if (source == null && !(kind == "armortex" && armorFiles.Count > 0)) return;
            var work = WorkFolder;
            var added = new List<string>();
            try
            {
                switch (kind)
                {
                    case "icon":
                    {
                        var name = AssetImport.SafeName(Val("name"));
                        var fmt = ((ComboBox)f["fmt"]).SelectedIndex switch { 1 => BlpFormat.Dxt1, 2 => BlpFormat.Palette, _ => BlpFormat.Dxt5 };
                        var blp = BlpEncoder.Encode(BlpEncoder.Resize(picture, 64, 64), fmt);
                        var ap = AssetImport.IconPath(name);
                        AssetImport.Place(work, ap, blp);
                        if (Ticked("spellicon")) StageRows("SpellIcon.dbc", t => AssetImport.IconRow(t, name, CustomStart), added);
                        if (Ticked("itemdisplay"))
                        {
                            if (!uint.TryParse(Val("itembase"), out var bid)) throw new FormatException("ItemDisplayInfo ID: enter a number.");
                            StageRows("ItemDisplayInfo.dbc", t => AssetImport.ItemDisplayRow(t, bid, name, CustomStart), added);
                        }
                        AssetImport.AddToManifest(work, new AssetRecord { Kind = "icon", Source = source, ArchivePath = ap, DbcRows = added, Note = fmt.ToString() });
                        lastMessage = $"{ap} written ({fmt}, {blp.Length / 1024.0:N1} KB)" + (added.Count > 0 ? "; staged " + string.Join(", ", added) : "") + ".";
                        break;
                    }
                    case "model":
                    {
                        var name = AssetImport.SafeName(Val("name"));
                        if (!uint.TryParse(Val("basemodel"), out var bm) || !uint.TryParse(Val("basedisplay"), out var bd)) throw new FormatException("Base IDs: enter numbers.");
                        if (!float.TryParse(Val("scale"), NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) || scale <= 0) throw new FormatException("Scale: enter a number like 1 or 1.5.");
                        var folder = AssetImport.ModelFolder(name);
                        AssetImport.Place(work, folder + name + ".m2", File.ReadAllBytes(source));
                        var dir = Path.GetDirectoryName(source);
                        var skins = Directory.GetFiles(dir, Path.GetFileNameWithoutExtension(source) + "0?.skin").OrderBy(x => x).ToList();
                        foreach (var sk in skins) AssetImport.Place(work, folder + name + Path.GetFileNameWithoutExtension(sk).Substring(Path.GetFileNameWithoutExtension(source).Length) + ".skin", File.ReadAllBytes(sk));
                        var texNames = new List<string>();
                        foreach (var tf in Directory.GetFiles(dir).Where(x => new[] { ".blp", ".png", ".tga" }.Contains(Path.GetExtension(x).ToLowerInvariant())).OrderBy(x => x))
                        {
                            var tn = AssetImport.SafeName(Path.GetFileNameWithoutExtension(tf));
                            byte[] data = Path.GetExtension(tf).ToLowerInvariant() == ".blp" ? File.ReadAllBytes(tf) : null;
                            if (data == null)
                            {
                                var pic = LoadPicture(tf);
                                if (!BlpEncoder.IsPowerOfTwo(pic.Width) || !BlpEncoder.IsPowerOfTwo(pic.Height)) pic = BlpEncoder.Resize(pic, NextPow2(pic.Width), NextPow2(pic.Height));
                                data = BlpEncoder.Encode(pic, BlpFormat.Dxt5);
                            }
                            AssetImport.Place(work, folder + tn + ".blp", data);
                            texNames.Add(tn);
                        }
                        uint modelId = 0, displayId = 0;
                        var cmd = Dbc("CreatureModelData.dbc") ?? throw new InvalidOperationException("The server's CreatureModelData.dbc wasn't found.");
                        var cdi = Dbc("CreatureDisplayInfo.dbc") ?? throw new InvalidOperationException("The server's CreatureDisplayInfo.dbc wasn't found.");
                        var (mrow, drow) = AssetImport.CreatureRows(cmd.work, cdi.work, bm, bd, name, texNames, CustomStart, scale);
                        cmd.work.Rows.Add(mrow); cmd.work.InvalidateIndex(); modelId = mrow.V[0];
                        cdi.work.Rows.Add(drow); cdi.work.InvalidateIndex(); displayId = drow.V[0];
                        DbcStager.Restage(S.Changes.Pending, cmd.baseT, cmd.work, "server");
                        DbcStager.Restage(S.Changes.Pending, cdi.baseT, cdi.work, "server");
                        added.Add($"CreatureModelData.dbc {modelId}"); added.Add($"CreatureDisplayInfo.dbc {displayId}");
                        AssetImport.AddToManifest(work, new AssetRecord { Kind = "model", Source = source, ArchivePath = folder + name + ".m2", DbcRows = added, Note = $"{skins.Count} skin(s), {texNames.Count} texture(s)" });
                        lastMessage = $"{folder}{name}.m2 with {skins.Count} skin(s) and {texNames.Count} texture(s); staged {string.Join(", ", added)}. Use display ID {displayId} on a creature (creature_template_model.CreatureDisplayID).";
                        break;
                    }
                    case "itemmodel":
                    {
                        var name = AssetImport.SafeName(Val("name"));
                        var slot = (string)((ComboBox)f["slot"]).SelectedItem ?? "Weapon";
                        if (!uint.TryParse(Val("basedisplay"), out var bd)) throw new FormatException("Base ItemDisplayInfo ID: enter a number.");
                        long useOn = 0;
                        if (Val("useon").Length > 0 && !long.TryParse(Val("useon"), out useOn)) throw new FormatException("Use it on item: enter an item entry number, or leave it empty.");
                        var folder = AssetImport.ItemModelFolder(slot);
                        AssetImport.Place(work, folder + name + ".m2", File.ReadAllBytes(source));
                        var dir = Path.GetDirectoryName(source);
                        var skins = Directory.GetFiles(dir, Path.GetFileNameWithoutExtension(source) + "0?.skin").OrderBy(x => x).ToList();
                        foreach (var sk in skins) AssetImport.Place(work, folder + name + Path.GetFileNameWithoutExtension(sk).Substring(Path.GetFileNameWithoutExtension(source).Length) + ".skin", File.ReadAllBytes(sk));
                        var texNames = new List<string>();
                        foreach (var tf in Directory.GetFiles(dir).Where(x => new[] { ".blp", ".png", ".tga" }.Contains(Path.GetExtension(x).ToLowerInvariant())).OrderBy(x => x))
                        {
                            var tn = AssetImport.SafeName(Path.GetFileNameWithoutExtension(tf));
                            byte[] data = Path.GetExtension(tf).ToLowerInvariant() == ".blp" ? File.ReadAllBytes(tf) : null;
                            if (data == null)
                            {
                                var pic = LoadPicture(tf);
                                if (!BlpEncoder.IsPowerOfTwo(pic.Width) || !BlpEncoder.IsPowerOfTwo(pic.Height)) pic = BlpEncoder.Resize(pic, NextPow2(pic.Width), NextPow2(pic.Height));
                                data = BlpEncoder.Encode(pic, BlpFormat.Dxt5);
                            }
                            AssetImport.Place(work, folder + tn + ".blp", data);
                            texNames.Add(tn);
                        }
                        uint displayId = 0;
                        StageRows("ItemDisplayInfo.dbc", t => { var r = AssetImport.ItemModelRow(t, bd, name, texNames.FirstOrDefault(), slot, Val("icon"), CustomStart); displayId = r.V[0]; return r; }, added);
                        string itemNote = "";
                        if (useOn > 0)
                        {
                            if (!S.HasReadAccount) throw new InvalidOperationException("Using it on an item needs the read-only account (setup step 3). The display was not staged on an item.");
                            using var db = S.OpenRead(S.Profile.WorldDb);
                            var cur = db.Query($"SELECT displayid FROM item_template WHERE entry = {useOn.ToString(CultureInfo.InvariantCulture)}", 1);
                            if (cur.Rows.Count == 0) throw new InvalidOperationException($"There's no item {useOn}; the display row was staged, but not used on an item.");
                            var before = cur.Rows[0][0];
                            ChangeFactory.StageCellEdit(S.Changes.Pending, "item_template", new List<KeyPart> { new KeyPart("entry", useOn.ToString(CultureInfo.InvariantCulture), "#" + useOn) }, "displayid",
                                SqlGen.Literal(before), SqlGen.Display(before), displayId.ToString(CultureInfo.InvariantCulture), displayId.ToString(CultureInfo.InvariantCulture));
                            added.Add($"item_template {useOn}.displayid");
                            itemNote = $" Item {useOn} now uses it (staged).";
                        }
                        AssetImport.AddToManifest(work, new AssetRecord { Kind = "itemmodel", Source = source, ArchivePath = folder + name + ".m2", DbcRows = added, Note = $"{slot}, {skins.Count} skin(s), {texNames.Count} texture(s)" });
                        lastMessage = $"{folder}{name}.m2 with {skins.Count} skin(s) and {texNames.Count} texture(s); staged {string.Join(", ", added)}. Pick display {displayId} on an item's Display tab (it's listed as \"icon · {name}\").{itemNote}";
                        break;
                    }
                    case "armortex":
                    {
                        var name = AssetImport.SafeName(Val("name"));
                        if (!uint.TryParse(Val("basedisplay"), out var bd)) throw new FormatException("Base ItemDisplayInfo ID: enter a number.");
                        var tex = new Dictionary<int, string>();
                        foreach (var (slot, file) in armorFiles)
                        {
                            var (tw, th) = AssetImport.ArmorTextureSize(slot);
                            var pic = BlpEncoder.Resize(LoadPicture(file), tw, th);
                            var ap = AssetImport.ArmorTexturePath(slot, name);
                            AssetImport.Place(work, ap, BlpEncoder.Encode(pic, BlpFormat.Dxt5));
                            tex[slot] = name;
                            AssetImport.AddToManifest(work, new AssetRecord { Kind = "armortex", Source = file, ArchivePath = ap });
                        }
                        uint displayId = 0;
                        StageRows("ItemDisplayInfo.dbc", t => { var r = AssetImport.ArmorDisplayRow(t, bd, tex, Ticked("clearothers"), CustomStart); displayId = r.V[0]; return r; }, added);
                        if (long.TryParse(Val("useon"), out var useOn) && useOn > 0 && S.HasReadAccount)
                        {
                            using var db = S.OpenRead(S.Profile.WorldDb);
                            var cur = db.Query($"SELECT displayid FROM item_template WHERE entry = {useOn.ToString(CultureInfo.InvariantCulture)}", 1);
                            if (cur.Rows.Count == 0) throw new InvalidOperationException($"There's no item {useOn}; the display row was staged, but not used on an item.");
                            ChangeFactory.StageCellEdit(S.Changes.Pending, "item_template", new List<KeyPart> { new KeyPart("entry", useOn.ToString(CultureInfo.InvariantCulture), "#" + useOn) }, "displayid",
                                SqlGen.Literal(cur.Rows[0][0]), SqlGen.Display(cur.Rows[0][0]), displayId.ToString(CultureInfo.InvariantCulture), displayId.ToString(CultureInfo.InvariantCulture));
                            added.Add($"item_template {useOn}.displayid");
                        }
                        lastMessage = $"{tex.Count} armour texture(s) written under Item\\TextureComponents; staged {string.Join(", ", added)}. Display {displayId} wears them.";
                        break;
                    }
                    case "sound":
                    {
                        var name = AssetImport.SafeName(Val("name"));
                        if (!uint.TryParse(Val("basesound"), out var bs)) throw new FormatException("Base SoundEntries ID: enter a number.");
                        var ext = Path.GetExtension(source);
                        var ap = AssetImport.SoundPath(name, ext);
                        AssetImport.Place(work, ap, File.ReadAllBytes(source));
                        StageRows("SoundEntries.dbc", t => AssetImport.SoundRow(t, bs, name, name + ext.ToLowerInvariant(), @"Sound\Custom", CustomStart), added);
                        AssetImport.AddToManifest(work, new AssetRecord { Kind = "sound", Source = source, ArchivePath = ap, DbcRows = added });
                        lastMessage = $"{ap} written; staged {string.Join(", ", added)}. Use that ID as a SoundID.";
                        break;
                    }
                    default:
                    {
                        var ap = Val("path").Trim('\\', '/');
                        if (ap.Length == 0 || ap.Contains("..")) throw new FormatException("Path inside the patch: e.g. Interface\\Glues\\LoadingScreens\\MyScreen.blp.");
                        AssetImport.Place(work, ap, File.ReadAllBytes(source));
                        AssetImport.AddToManifest(work, new AssetRecord { Kind = "file", Source = source, ArchivePath = ap });
                        lastMessage = $"{ap} copied into the work folder.";
                        break;
                    }
                }
                S.SaveChanges();
                w.Toast("Imported into " + work + (added.Count > 0 ? $"; {added.Count} DBC row(s) staged." : "."), "ok");
            }
            catch (Exception ex) { w.Toast("Not imported: " + ex.Message, "bad"); return; }
            BuildForm();
        }

        readonly Dictionary<int, string> armorFiles = new Dictionary<int, string>();

        /// <summary>The chosen armour pictures painted on a human, in the 3D viewer (nothing staged).</summary>
        UIElement ArmorPreview()
        {
            var v = new ModelViewer(S.Models, 340);
            var look = new CharacterLook { Race = 1, Sex = 0, Skin = 0, Face = 0, HairStyle = 0, HairColor = 0, FacialHair = 0 };
            foreach (var (slot, file) in armorFiles) { try { look.Paint.Add((CharacterBuilder.ItemTextureSlots[slot].region, LoadPicture(file))); } catch { } }
            v.Load(S.Models.Character(look, "Preview"));
            LastArmorPreview = new Border { Margin = new Thickness(0, 6, 0, 12), Child = v.View };
            return LastArmorPreview;
        }

        public Border LastArmorPreview;
        public void ScreensArmor(int slot, string file) { armorFiles[slot] = file; BuildForm(); }

        /// <summary>The chosen .m2 in the 3D viewer, with its .skin and textures from the same folder.</summary>
        UIElement PreviewOf(string m2)
        {
            var v = new ModelViewer(S.Models, 300);
            v.LoadFile(m2);
            var b = new Border { Margin = new Thickness(0, 0, 0, 12), Child = v.View };
            return b;
        }

        static int NextPow2(int n) { int p = 1; while (p < n && p < 2048) p <<= 1; return p; }

        // screenshots
        public void ScreensKind(string k) => kinds.SelectedIndex = new[] { "icon", "model", "itemmodel", "armortex", "sound", "file" }.ToList().IndexOf(k);
        public void ScreensWorkName(string n) => workName.Text = n;
    }
}
