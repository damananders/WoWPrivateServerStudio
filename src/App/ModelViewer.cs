using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using AndersCraftStudio.Core;
using N = System.Numerics;

namespace AndersCraftStudio
{
    /// <summary>
    /// Finds and reads models for the viewer: client archives (the game's own load order) with Studio's patch work
    /// folders on top, so imported-but-not-installed models show too. Display rows come from the server's DBCs plus
    /// anything staged. Read-only.
    /// </summary>
    public sealed class ModelLibrary
    {
        readonly Studio S;
        ClientFiles client; string clientFor;
        readonly Dictionary<string, DbcTable> dbcs = new Dictionary<string, DbcTable>(StringComparer.OrdinalIgnoreCase);
        public ModelLibrary(Studio s) { S = s; S.ChangesChanged += () => dbcs.Clear(); }

        public string ClientDataDir => string.IsNullOrEmpty(S.Profile?.ClientDir) ? null : Path.Combine(S.Profile.ClientDir, "Data");
        public bool HasClient => ClientDataDir != null && Directory.Exists(ClientDataDir);

        /// <summary>Extra folders searched first (patch work folders, or the folder of a model being imported).</summary>
        public List<string> Overlays { get; } = new List<string>();

        IEnumerable<string> WorkFolders()
        {
            foreach (var o in Overlays) yield return o;
            string root = null;
            try { root = Path.Combine(S.Profiles.Settings.Workspace(), "patch_work"); } catch { }
            if (root != null && Directory.Exists(root)) foreach (var d in Directory.GetDirectories(root)) yield return d;
        }

        public byte[] Read(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            path = path.Replace('/', '\\');
            foreach (var d in WorkFolders())
            {
                var f = Path.Combine(d, path);
                if (File.Exists(f)) return File.ReadAllBytes(f);
            }
            if (!HasClient) return null;
            if (client == null || clientFor != ClientDataDir) { client?.Dispose(); client = new ClientFiles(ClientDataDir); clientFor = ClientDataDir; }
            return client.Read(path, out _);
        }

        DbcTable Dbc(string file)
        {
            if (dbcs.TryGetValue(file, out var t)) return t;
            t = null;
            try
            {
                var dd = ConfParser.ParseFile(S.Profile.ConfPath).DataDir;
                var p = Path.Combine(dd, "dbc", file);
                if (File.Exists(p))
                {
                    t = DbcTable.Load(p);
                    t = DbcPatcher.Apply(t, S.Changes.Pending.Changes, false, new List<string>()) ?? t;
                }
            }
            catch { t = null; }
            dbcs[file] = t;
            return t;
        }

        string Field(DbcTable t, DbcRow r, string name) { int f = t.Layout.FieldByName(name); return f < 0 ? "" : t.FieldText(r, f); }

        public sealed class Request
        {
            public string Path, Title, Note;
            public string[] CreatureSkins;
            public string ItemSkin, ItemFolder;
            public float Scale = 1;
            public Func<ushort, bool> Geoset;
        }

        public Request Creature(long displayId)
        {
            var cdi = Dbc("CreatureDisplayInfo.dbc"); var cmd = Dbc("CreatureModelData.dbc");
            var r = cdi?.Find((uint)displayId);
            if (r == null) return new Request { Note = $"Display {displayId} isn't in CreatureDisplayInfo.dbc." };
            uint.TryParse(Field(cdi, r, "ModelID"), out var modelId);
            var mr = cmd?.Find(modelId);
            if (mr == null) return new Request { Note = $"Model {modelId} isn't in CreatureModelData.dbc." };
            float.TryParse(Field(cdi, r, "CreatureModelScale"), NumberStyles.Float, CultureInfo.InvariantCulture, out var sc);
            var req = new Request
            {
                Path = M2Model.M2Path(Field(cmd, mr, "ModelName")), Title = $"Display {displayId}",
                CreatureSkins = new[] { Field(cdi, r, "TextureVariation_1"), Field(cdi, r, "TextureVariation_2"), Field(cdi, r, "TextureVariation_3") },
                Scale = sc > 0 ? sc : 1,
            };
            if (Field(cdi, r, "ExtendedDisplayInfoID") is string ext && ext != "0" && ext != "")
            {
                req.Geoset = M2Mesh.DefaultGeoset;
                req.Note = "A character-style NPC (player body with clothes): shown as the bare body, without its face, hair and clothing textures.";
            }
            return req;
        }

        public Request GameObject(long displayId)
        {
            var t = Dbc("GameObjectDisplayInfo.dbc");
            var r = t?.Find((uint)displayId);
            if (r == null) return new Request { Note = $"Display {displayId} isn't in GameObjectDisplayInfo.dbc." };
            var path = Field(t, r, "ModelName");
            if (path.EndsWith(".wmo", StringComparison.OrdinalIgnoreCase)) return new Request { Note = $"{path} is a WMO (a building), not an M2 model. The viewer shows M2 models only." };
            return new Request { Path = M2Model.M2Path(path), Title = $"Display {displayId}" };
        }

        public Request Item(long displayId, long inventoryType)
        {
            var t = Dbc("ItemDisplayInfo.dbc");
            var r = t?.Find((uint)displayId);
            if (r == null) return new Request { Note = $"Display {displayId} isn't in ItemDisplayInfo.dbc." };
            var model = Field(t, r, "ModelName_1");
            if (string.IsNullOrEmpty(model)) return new Request { Note = "This item has no model of its own (body armour is drawn on the character; see Level 3 in the README)." };
            foreach (var p in ItemModels.ModelPaths(model, inventoryType))
                if (Read(p) != null)
                    return new Request { Path = p, Title = $"Display {displayId}", ItemSkin = Field(t, r, "ModelTexture_1"), ItemFolder = Path.GetDirectoryName(p) };
            return new Request { Note = $"{model} wasn't found in the client's item folders." };
        }

        public string AnimationName(int id)
        {
            var t = Dbc("AnimationData.dbc");
            var r = t?.Find((uint)id);
            if (r != null && r.S.Length > 1 && !string.IsNullOrEmpty(r.S[1])) return r.S[1];
            return id switch { 0 => "Stand", 1 => "Death", 2 => "Spell", 3 => "Stop", 4 => "Walk", 5 => "Run", 6 => "Dead", 7 => "Rise", 8 => "StandWound", 9 => "CombatWound", 10 => "CombatCritical", 11 => "ShuffleLeft", 12 => "ShuffleRight", 13 => "Walkbackwards", 14 => "Stun", 15 => "HandsClosed", 16 => "AttackUnarmed", 17 => "Attack1H", 18 => "Attack2H", 19 => "Attack2HL", 20 => "ParryUnarmed", 26 => "ReadyUnarmed", 27 => "Ready1H", 32 => "SpellCastDirected", 33 => "SpellCastOmni", 37 => "JumpStart", 38 => "Jump", 39 => "JumpEnd", 40 => "Fall", 41 => "SwimIdle", 42 => "Swim", 60 => "EmoteTalk", 69 => "EmoteDance", _ => "Animation " + id };
        }
    }

    /// <summary>
    /// The 3D viewer: a textured, lit model you can turn (left drag), move (right drag) and zoom (wheel), with its
    /// animations. Uses WPF's own 3D; the model is skinned on the CPU each frame while an animation plays.
    /// </summary>
    public sealed class ModelViewer
    {
        readonly ModelLibrary lib;
        readonly Border root = new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true };
        readonly Grid stage = new Grid { MinHeight = 320 };
        readonly Viewport3D view = new Viewport3D();
        readonly PerspectiveCamera cam = new PerspectiveCamera { FieldOfView = 40, NearPlaneDistance = 0.01, FarPlaneDistance = 5000 };
        readonly ModelVisual3D modelVisual = new ModelVisual3D();
        readonly ComboBox anims = new ComboBox { Width = 210 };
        readonly ComboBox speed = new ComboBox { Width = 80 };
        readonly CheckBox wire = new CheckBox { Content = U.T("Wireframe", 12.5, "Text", null, false), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 6, 0) };
        readonly Button play;
        readonly TextBlock status = U.T("", 12, "Muted", null, true, new Thickness(0, 6, 0, 0));
        readonly TextBlock overlay = U.T("", 13, "Muted", null, true);
        public UIElement View => root;

        M2Model model; List<MeshPart> parts; List<GeometryModel3D> geos = new List<GeometryModel3D>(); GeometryModel3D wireGeo;
        string path;
        int seq = -1; bool playing; double t; DateTime last;
        double yaw = 30, pitch = 15, dist = 5; Vector3D pan; Point3D center; double radius = 1;
        Point dragFrom; bool rotating, panning;

        public ModelViewer(ModelLibrary lib, double height = 380)
        {
            this.lib = lib;
            stage.Height = height;
            stage.Background = new LinearGradientBrush(Color.FromRgb(0x2a, 0x24, 0x24), Color.FromRgb(0x14, 0x11, 0x12), 90);
            view.Camera = cam;
            var lights = new Model3DGroup();
            lights.Children.Add(new AmbientLight(Color.FromRgb(0x8c, 0x8c, 0x8c)));
            lights.Children.Add(new DirectionalLight(Color.FromRgb(0xc8, 0xc4, 0xbc), new Vector3D(-0.6, -0.8, -0.5)));
            lights.Children.Add(new DirectionalLight(Color.FromRgb(0x50, 0x50, 0x58), new Vector3D(0.7, 0.3, 0.6)));
            view.Children.Add(new ModelVisual3D { Content = lights });
            view.Children.Add(modelVisual);
            stage.Children.Add(view);
            overlay.HorizontalAlignment = HorizontalAlignment.Center; overlay.VerticalAlignment = VerticalAlignment.Center; overlay.TextAlignment = TextAlignment.Center; overlay.MaxWidth = 420;
            stage.Children.Add(overlay);
            var hint = U.T("drag: turn · right-drag: move · wheel: zoom · double-click: reset", 11, "Faint", null, false, new Thickness(10, 0, 0, 8));
            hint.VerticalAlignment = VerticalAlignment.Bottom; hint.IsHitTestVisible = false;
            stage.Children.Add(hint);
            stage.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) { ResetView(); return; } rotating = true; dragFrom = e.GetPosition(stage); stage.CaptureMouse(); };
            stage.MouseRightButtonDown += (s, e) => { panning = true; dragFrom = e.GetPosition(stage); stage.CaptureMouse(); };
            stage.MouseUp += (s, e) => { rotating = panning = false; stage.ReleaseMouseCapture(); };
            stage.MouseMove += (s, e) =>
            {
                if (!rotating && !panning) return;
                var p = e.GetPosition(stage); var d = p - dragFrom; dragFrom = p;
                if (rotating) { yaw -= d.X * 0.5; pitch = Math.Clamp(pitch + d.Y * 0.4, -85, 85); }
                else { var right = Vector3D.CrossProduct(cam.LookDirection, cam.UpDirection); right.Normalize(); pan += right * (-d.X * dist / 600) + cam.UpDirection * (d.Y * dist / 600); }
                UpdateCamera();
            };
            stage.MouseWheel += (s, e) => { dist = Math.Clamp(dist * (e.Delta > 0 ? 0.88 : 1.14), radius * 0.2, radius * 30); UpdateCamera(); };
            foreach (var x in new[] { "0.25×", "0.5×", "1×", "2×" }) speed.Items.Add(x);
            speed.SelectedIndex = 2;
            play = U.Btn("▶ Play", TogglePlay, null, "Play or pause the animation.");
            play.Padding = new Thickness(10, 5, 10, 5);
            var reset = U.Btn("Reset view", ResetView, "BtnGhost"); reset.Padding = new Thickness(8, 5, 8, 5);
            anims.SelectionChanged += (s, e) => { if (anims.SelectedItem is ComboBoxItem ci && ci.Tag is int i) { SetSequence(i); if (i >= 0 && !playing) TogglePlay(); } };
            wire.Checked += (s, e) => Redraw(); wire.Unchecked += (s, e) => Redraw();
            var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            foreach (var x in new FrameworkElement[] { anims, play, speed, wire, reset }) { x.Margin = new Thickness(0, 0, 8, 4); x.VerticalAlignment = VerticalAlignment.Center; bar.Children.Add(x); }
            root.Child = U.Stack(bar, new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = stage, BorderBrush = U.R("Line"), BorderThickness = new Thickness(1) }, status);
            root.Unloaded += (s, e) => Stop();
            ShowMessage("No model loaded.");
        }

        public void ShowMessage(string text)
        {
            Stop(); model = null; parts = null; modelVisual.Content = null; overlay.Text = text; status.Text = ""; anims.Items.Clear();
        }

        /// <summary>Loads and shows a model. Returns false (and shows why) when it can't.</summary>
        public bool Load(ModelLibrary.Request r)
        {
            if (r == null || string.IsNullOrEmpty(r.Path)) { ShowMessage(r?.Note ?? "No model."); return false; }
            if (!lib.HasClient && lib.Read(r.Path) == null) { ShowMessage("Set your game client folder (setup step 4) to see models."); return false; }
            try
            {
                var bytes = lib.Read(r.Path) ?? throw new FileNotFoundException($"{r.Path} isn't in the client or your patch work folders.");
                var m = M2Model.Read(bytes);
                var skinBytes = lib.Read(M2Model.SkinPath(r.Path)) ?? throw new FileNotFoundException($"{Path.GetFileName(M2Model.SkinPath(r.Path))} (its .skin file) wasn't found.");
                var s = M2Skin.Read(skinBytes);
                model = m; path = r.Path;
                parts = M2Mesh.Build(m, s, r.Geoset);
                BuildScene(r);
                FillAnimations();
                FitCamera(r.Scale);
                overlay.Text = "";
                int tris = parts.Sum(p => p.Triangles.Length) / 3;
                status.Text = $"{r.Path} · {m.Vertices.Length:N0} vertices · {tris:N0} triangles · {m.Bones.Count} bones · {m.Sequences.Count} animations" +
                              (missingTextures > 0 ? $" · {missingTextures} texture(s) not found (grey)" : "") +
                              (m.ParticleEmitters > 0 ? $" · {m.ParticleEmitters} particle effect(s) not shown" : "") + (string.IsNullOrEmpty(r.Note) ? "" : "\n" + r.Note);
                return true;
            }
            catch (Exception ex) { ShowMessage("Can't show this model: " + ex.Message); return false; }
        }

        /// <summary>A model file from disk (e.g. one being imported): its .skin and textures are looked for next to it first.</summary>
        public bool LoadFile(string file)
        {
            lib.Overlays.Clear();
            var dir = Path.GetDirectoryName(file);
            var tmp = Path.Combine(Path.GetTempPath(), "studio_model_preview");
            try
            {
                if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
                var name = Path.GetFileName(file);
                Directory.CreateDirectory(Path.Combine(tmp, "Preview"));
                File.Copy(file, Path.Combine(tmp, "Preview", name));
                foreach (var f in Directory.GetFiles(dir).Where(f => f.EndsWith(".skin", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".blp", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".anim", StringComparison.OrdinalIgnoreCase)))
                    File.Copy(f, Path.Combine(tmp, "Preview", Path.GetFileName(f)), true);
                lib.Overlays.Add(tmp);
                // textures named with a full client path are also looked for by file name next to the model
                previewDir = Path.Combine(tmp, "Preview");
                // a model on disk has no display row: its creature skins are the textures next to it
                var skins = Directory.GetFiles(Path.Combine(tmp, "Preview"), "*.blp").Select(Path.GetFileNameWithoutExtension).OrderBy(x => x).ToArray();
                return Load(new ModelLibrary.Request { Path = Path.Combine("Preview", name), Title = name, CreatureSkins = skins, ItemSkin = skins.FirstOrDefault() });
            }
            catch (Exception ex) { ShowMessage("Can't show this model: " + ex.Message); return false; }
        }

        string previewDir;
        int missingTextures;

        Material MaterialFor(MeshPart p, ModelLibrary.Request r)
        {
            BitmapSource bmp = null;
            if (p.TextureIndex >= 0 && p.TextureIndex < model.Textures.Count)
            {
                var tex = model.Textures[p.TextureIndex];
                var file = M2Textures.Resolve(tex, r.Path, r.CreatureSkins, r.ItemSkin, r.ItemFolder);
                if (file != null)
                {
                    var bytes = lib.Read(file);
                    if (bytes == null && previewDir != null) { var local = Path.Combine(previewDir, Path.GetFileName(file)); if (File.Exists(local)) bytes = File.ReadAllBytes(local); }
                    if (bytes != null)
                        try
                        {
                            var img = Blp.Decode(bytes);
                            var px = img.Pixels;
                            if (p.Blend >= 3 || p.EnvMap)
                            {
                                // additive / modulating layers (shine, glow): brightness becomes coverage so dark parts let the layer below show
                                px = (byte[])px.Clone();
                                for (int i = 0; i < px.Length; i += 4) { var lum = Math.Max(px[i], Math.Max(px[i + 1], px[i + 2])); px[i + 3] = (byte)(lum * (p.Blend >= 5 ? 0.35 : 0.8)); }
                            }
                            else if (p.Blend == 0) { px = (byte[])px.Clone(); for (int i = 3; i < px.Length; i += 4) px[i] = 255; }   // opaque: ignore stray alpha
                            bmp = BitmapSource.Create(img.Width, img.Height, 96, 96, PixelFormats.Bgra32, null, px, img.Width * 4); bmp.Freeze();
                        }
                        catch { }
                }
                if (bmp == null) missingTextures++;
            }
            if (bmp == null) return new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x9a, 0x92, 0x88)));
            var brush = new ImageBrush(bmp) { ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1), TileMode = TileMode.Tile, Stretch = Stretch.Fill };
            brush.Freeze();
            if (p.Blend >= 3 || p.EnvMap) return new EmissiveMaterial(brush);   // glows add light rather than paint over
            return new DiffuseMaterial(brush);
        }

        void BuildScene(ModelLibrary.Request r)
        {
            missingTextures = 0;
            geos = new List<GeometryModel3D>();
            var group = new Model3DGroup();
            foreach (var p in parts)
            {
                var g = new GeometryModel3D { Geometry = MeshFor(p, null) };
                var mat = MaterialFor(p, r);
                g.Material = mat; if (p.TwoSided || p.Transparent) g.BackMaterial = mat;
                geos.Add(g); group.Children.Add(g);
            }
            wireGeo = new GeometryModel3D { Material = new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(0xf0, 0xb0, 0x50))) };
            group.Children.Add(wireGeo);
            // M2 is Z-up; WPF is Y-up
            group.Transform = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), -90));
            modelVisual.Content = group;
            Redraw();
        }

        MeshGeometry3D MeshFor(MeshPart p, N.Matrix4x4[] pose)
        {
            var mesh = new MeshGeometry3D();
            var pos = new Point3DCollection(p.Vertices.Length); var nrm = new Vector3DCollection(p.Vertices.Length); var uv = new PointCollection(p.Vertices.Length);
            foreach (var vi in p.Vertices)
            {
                var v = model.Vertices[vi];
                var (pp, nn) = pose == null ? (v.Pos, v.Normal) : M2Model.Skin(v, pose);
                pos.Add(new Point3D(pp.X, pp.Y, pp.Z)); nrm.Add(new Vector3D(nn.X, nn.Y, nn.Z));
                if (p.EnvMap) { var su = M2Mesh.SphereUv(nn); uv.Add(new Point(su.X, su.Y)); } else uv.Add(new Point(v.Uv.X, v.Uv.Y));
            }
            mesh.Positions = pos; mesh.Normals = nrm; mesh.TextureCoordinates = uv;
            mesh.TriangleIndices = new Int32Collection(p.Triangles);
            return mesh;
        }

        N.Matrix4x4[] currentPose;

        void Redraw()
        {
            if (model == null) return;
            for (int i = 0; i < parts.Count; i++) geos[i].Geometry = MeshFor(parts[i], currentPose);
            wireGeo.Geometry = wire.IsChecked == true ? WireMesh() : null;
        }

        /// <summary>Triangle edges as thin flat strips (WPF 3D has no line drawing).</summary>
        MeshGeometry3D WireMesh()
        {
            var m = new MeshGeometry3D(); var pos = new Point3DCollection(); var idx = new Int32Collection();
            double w = radius * 0.003;
            var seen = new HashSet<long>();
            foreach (var p in parts)
            {
                var pts = p.Vertices.Select(vi => currentPose == null ? model.Vertices[vi].Pos : M2Model.Skin(model.Vertices[vi], currentPose).pos).ToArray();
                for (int k = 0; k + 2 < p.Triangles.Length; k += 3)
                    for (int e = 0; e < 3; e++)
                    {
                        int a = p.Triangles[k + e], b = p.Triangles[k + (e + 1) % 3];
                        long key = ((long)Math.Min(a, b) << 32 | (uint)Math.Max(a, b)) ^ ((long)p.GetHashCode() << 16);
                        if (!seen.Add(key) || pos.Count > 400000) continue;
                        var A = pts[a]; var B = pts[b];
                        var d = B - A; var n = N.Vector3.Cross(d, N.Vector3.UnitZ); if (n.LengthSquared() < 1e-8) n = N.Vector3.Cross(d, N.Vector3.UnitX);
                        n = N.Vector3.Normalize(n) * (float)w;
                        int s = pos.Count;
                        pos.Add(new Point3D(A.X - n.X, A.Y - n.Y, A.Z - n.Z)); pos.Add(new Point3D(A.X + n.X, A.Y + n.Y, A.Z + n.Z));
                        pos.Add(new Point3D(B.X + n.X, B.Y + n.Y, B.Z + n.Z)); pos.Add(new Point3D(B.X - n.X, B.Y - n.Y, B.Z - n.Z));
                        idx.Add(s); idx.Add(s + 1); idx.Add(s + 2); idx.Add(s); idx.Add(s + 2); idx.Add(s + 3);
                        idx.Add(s); idx.Add(s + 2); idx.Add(s + 1); idx.Add(s); idx.Add(s + 3); idx.Add(s + 2);
                    }
            }
            m.Positions = pos; m.TriangleIndices = idx;
            return m;
        }

        void FillAnimations()
        {
            anims.Items.Clear();
            anims.Items.Add(new ComboBoxItem { Content = "Rest pose (no animation)", Tag = -1 });
            for (int i = 0; i < model.Sequences.Count; i++)
            {
                var q = model.Sequences[i];
                if ((q.Flags & 0x40) != 0) continue;   // alias entries point at another sequence
                anims.Items.Add(new ComboBoxItem { Content = lib.AnimationName(q.Id) + (q.Variation > 0 ? $" ({q.Variation + 1})" : "") + $"  · {q.Duration / 1000.0:0.0}s", Tag = i });
            }
            int stand = model.Sequences.FindIndex(q => q.Id == 0);
            playing = false; play.Content = "▶ Play";
            anims.SelectedItem = anims.Items.Cast<ComboBoxItem>().FirstOrDefault(c => (int)c.Tag == stand) ?? anims.Items[0];
        }

        public IEnumerable<string> AnimationNames => anims.Items.Cast<ComboBoxItem>().Select(c => Convert.ToString(c.Content));

        void SetSequence(int i)
        {
            seq = i; t = 0;
            if (model != null && i >= 0 && model.NeedsAnimFile(i)) model.LoadAnimFile(i, lib.Read(M2Model.AnimPath(path, model.Sequences[i])));
            currentPose = i < 0 ? null : model.Pose(i, 0);
            Redraw();
        }

        public void SelectAnimation(int animId)
        {
            var i = model?.Sequences.FindIndex(q => q.Id == animId) ?? -1;
            var item = anims.Items.Cast<ComboBoxItem>().FirstOrDefault(c => (int)c.Tag == i);
            if (item != null) anims.SelectedItem = item;
        }

        public void ScreensWire(bool on) => wire.IsChecked = on;

        /// <summary>Shows the pose at a time without playing (screenshots).</summary>
        public void ShowFrame(double ms) { if (model == null || seq < 0) return; Stop(); t = ms; currentPose = model.Pose(seq, (uint)ms, (uint)ms); Redraw(); }

        void TogglePlay()
        {
            if (model == null) return;
            if (playing) { Stop(); return; }
            if (seq < 0) return;
            playing = true; play.Content = "❚❚ Pause"; last = DateTime.Now;
            CompositionTarget.Rendering += Tick;
        }

        void Stop() { if (playing) CompositionTarget.Rendering -= Tick; playing = false; play.Content = "▶ Play"; }

        double globalT;
        void Tick(object s, EventArgs e)
        {
            if (model == null || seq < 0 || !root.IsVisible) { Stop(); return; }
            var now = DateTime.Now; var dt = (now - last).TotalMilliseconds; if (dt < 30) return; last = now;
            double sp = speed.SelectedIndex switch { 0 => 0.25, 1 => 0.5, 3 => 2, _ => 1 };
            t += dt * sp; globalT += dt * sp;
            var d = Math.Max(1, model.Sequences[seq].Duration);
            if (t >= d) t %= d;
            currentPose = model.Pose(seq, (uint)t, (uint)globalT);
            Redraw();
        }

        void FitCamera(float scale)
        {
            var (min, max) = M2Mesh.VertexBounds(model, parts);
            if (currentPose != null)
            {
                N.Vector3 a = new N.Vector3(float.MaxValue), b = new N.Vector3(float.MinValue);
                foreach (var p in parts) foreach (var vi in p.Vertices) { var q = M2Model.Skin(model.Vertices[vi], currentPose).pos; a = N.Vector3.Min(a, q); b = N.Vector3.Max(b, q); }
                if (a.X != float.MaxValue) { min = N.Vector3.Min(min, a); max = N.Vector3.Max(max, b); }
            }
            var c = (min + max) / 2;
            center = new Point3D(c.X, c.Z, -c.Y);    // after the Z-up → Y-up turn
            radius = Math.Max(0.05, (max - min).Length() / 2);
            ResetView();
        }

        void ResetView() { yaw = 30; pitch = 12; pan = new Vector3D(); dist = radius / Math.Tan(cam.FieldOfView * Math.PI / 360) * 1.05; UpdateCamera(); }

        void UpdateCamera()
        {
            double y = yaw * Math.PI / 180, p = pitch * Math.PI / 180;
            var dir = new Vector3D(Math.Cos(p) * Math.Cos(y), Math.Sin(p), Math.Cos(p) * Math.Sin(y));
            var target = center + pan;
            cam.Position = target + dir * dist;
            cam.LookDirection = -dir;
            cam.UpDirection = new Vector3D(0, 1, 0);
        }
    }
}
