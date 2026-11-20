using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AndersCraftStudio.Core;
using Microsoft.Win32;

namespace AndersCraftStudio
{
    /// <summary>Build, check, install, uninstall and share MPQ patches for the 3.3.5a client; browse and extract any MPQ.</summary>
    public sealed class ClientPatchesPage : IPage
    {
        readonly MainWindow w;
        Studio S => w.S;
        readonly Grid view = new Grid();
        public UIElement View => view;
        readonly ContentControl body = new ContentControl { Focusable = false };
        RadioButton tBuild, tInstalled, tBrowse;

        public string StudioRoot { get; set; }
        string WorkRoot => Path.Combine(StudioRoot, "patch_work");
        string OutRoot => Path.Combine(StudioRoot, "patches");
        PatchInstaller Installer => new PatchInstaller(Path.Combine(S.Profiles.Root, "installed_patches.json"), S.Profiles.Settings.BackupRoot);

        // state
        string workDir;
        string patchName;
        bool compress = true;
        string confirm = "";
        List<PatchInfo> installed;
        string lastBuild;
        List<string> lastProblems;
        string browsePath;
        string browseFilter = "";
        string whoWins = "";

        public ClientPatchesPage(MainWindow w)
        {
            this.w = w;
            StudioRoot = w.IsScreens ? Path.Combine(Path.GetTempPath(), "AndersCraftStudio_screens_patches") : w.S.Profiles.Settings.Workspace();
            view.Margin = new Thickness(28, 22, 28, 20);
            view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            view.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            view.Children.Add(U.Stack(U.H1("Client patches"), U.T("Pack client-side changes (DBC tables, icons, interface files) into a patch-X.MPQ the 3.3.5a client loads, check it, install it with a backup, and share it with players.", 13.5, "Muted", null, true, new Thickness(0, 0, 0, 14))));
            tBuild = Seg("Build a patch", "Collect files and build a patch MPQ.");
            tInstalled = Seg("Installed patches", "What's in your client's Data folder, which patch wins for each file, and uninstall.");
            tBrowse = Seg("Browse an MPQ", "Look inside any MPQ and extract files (read-only).");
            tBuild.IsChecked = true;
            var tabs = new Border { Background = U.R("Panel"), BorderBrush = U.R("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 14), Child = U.Row(tBuild, tInstalled, tBrowse) };
            Grid.SetRow(tabs, 1); view.Children.Add(tabs);
            Grid.SetRow(body, 2); view.Children.Add(body);
        }

        RadioButton Seg(string text, string tip)
        {
            var r = new RadioButton { Style = U.S("SegBtn"), Content = text, GroupName = "patchtab", ToolTip = tip };
            r.Checked += (s, e) => Build();
            return r;
        }

        public void OnShow() { installed = null; Build(); }

        string ClientDir => S.Profile?.ClientDir;
        string DataDir => string.IsNullOrEmpty(ClientDir) ? null : Path.Combine(ClientDir, "Data");
        List<PatchInfo> Installed() => installed ??= DataDir != null ? PatchSet.Scan(DataDir, NameCache) : new List<PatchInfo>();
        /// <summary>Recovered file names for archives without a listfile (Studio's own data folder, shared by all profiles).</summary>
        static string NameCache => Path.Combine(ProfileStore.DefaultRoot, "name_cache");
        string ServerDbcDir { get { try { var d = ConfParser.ParseFile(S.Profile.ConfPath).DataDir; return string.IsNullOrEmpty(d) ? null : Path.Combine(d, "dbc"); } catch { return null; } } }
        bool recovering;

        void RecoverNames(PatchInfo p)
        {
            if (recovering) return;
            recovering = true;
            w.Toast($"Recovering file names in {p.FileName}… this reads other archives and can take a minute or two.", "info");
            var data = DataDir; var dbc = ServerDbcDir; var path = p.FullPath;
            Task.Run(() =>
            {
                var cands = NameRecovery.ClientCandidates(data, dbc, path);
                using var r = new MpqReader(path);
                var res = NameRecovery.Recover(r, cands);
                NameRecovery.SaveCache(NameCache, path, res);
                return res;
            }).ContinueWith(t => w.Dispatcher.Invoke(() =>
            {
                recovering = false; installed = null;
                if (t.Exception != null) w.Toast("Name recovery failed: " + t.Exception.GetBaseException().Message, "bad");
                else w.Toast($"{p.FileName}: {t.Result.Recovered:N0} of {t.Result.Total:N0} file names known ({t.Result.Percent:0.0}%). The rest stay listed as \"unknown #…\" and can still be extracted.", "ok");
                Build();
            }));
        }

        void Build()
        {
            if (tBuild == null) return;
            UIElement content;
            if (tInstalled.IsChecked == true) content = InstalledView();
            else if (tBrowse.IsChecked == true) content = BrowseView();
            else content = BuildView();
            body.Content = U.Scroll(content);
        }

        UIElement ClientFolderCard()
        {
            var box = U.Box(ClientDir ?? "", true);
            var browse = U.Btn("Browse…", () =>
            {
                var d = new OpenFolderDialog { Title = "Pick your WoW 3.3.5a folder (with Wow.exe and Data)" };
                if (d.ShowDialog(w) == true) SetClient(d.FolderName);
            }, null, "The folder that contains the Data folder.");
            box.LostFocus += (s, e) => { if (box.Text.Trim() != (ClientDir ?? "")) SetClient(box.Text.Trim()); };
            var row = new DockPanel(); browse.Margin = new Thickness(10, 0, 0, 0); DockPanel.SetDock(browse, Dock.Right); row.Children.Add(browse); row.Children.Add(box);
            var ok = DataDir != null && Directory.Exists(DataDir);
            var running = ok ? PatchInstaller.RunningClients(ClientDir) : new List<string>();
            var sp = U.Stack(U.Field("Game client folder", "Same as setup step 4. Studio only reads it, except when you press Install or Uninstall.", row));
            sp.Children.Add(U.CheckRow(ok ? U.Check.Ok : U.Check.Warn, "Data folder", ok ? DataDir : "not found: pick the folder that contains Data"));
            if (ok) sp.Children.Add(U.CheckRow(running.Count == 0 ? U.Check.Ok : U.Check.Bad, "Game running?", running.Count == 0 ? "no" : "yes: " + string.Join(", ", running) + ". Install and uninstall wait until it's closed."));
            return U.Card(sp, new Thickness(20, 16, 20, 12), new Thickness(0, 0, 0, 12));
        }

        void SetClient(string dir)
        {
            if (S.Profile == null) return;
            S.Profile.ClientDir = dir;
            if (!w.IsScreens) S.Profiles.Save(S.Profile);
            installed = null;
            Build();
        }

        // ================================================================== build
        List<(string path, long size)> WorkFiles()
        {
            if (workDir == null || !Directory.Exists(workDir)) return new List<(string, long)>();
            var root = Path.GetFullPath(workDir).TrimEnd('\\') + "\\";
            return Directory.GetFiles(root, "*", SearchOption.AllDirectories).Select(f => (f.Substring(root.Length), new FileInfo(f).Length)).OrderBy(x => x.Item1, StringComparer.OrdinalIgnoreCase).ToList();
        }

        UIElement BuildView()
        {
            var inst = Installed();
            patchName ??= PatchSet.SuggestFreeName(inst) ?? "patch-S.MPQ";
            workDir ??= Path.Combine(WorkRoot, Path.GetFileNameWithoutExtension(patchName));
            var sp = new StackPanel { MaxWidth = 1180, HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(ClientFolderCard());

            // ---- 1 contents
            var c1 = new StackPanel();
            c1.Children.Add(U.Row(U.Eyebrow("1 · Contents"), new Border { Width = 8 }));
            c1.Children.Add(U.T("Everything in the work folder goes into the patch, at the same path. Example: work\\DBFilesClient\\Spell.dbc becomes DBFilesClient\\Spell.dbc inside the patch.", 13, "Muted", null, true, new Thickness(0, 0, 0, 12)));
            var wbox = U.Box(workDir, true);
            wbox.LostFocus += (s, e) => { if (wbox.Text.Trim().Length > 0 && wbox.Text.Trim() != workDir) { workDir = wbox.Text.Trim(); Build(); } };
            var wrow = new DockPanel();
            var wb = U.Row(U.Btn("Open folder", () => { Directory.CreateDirectory(workDir); Process.Start(new ProcessStartInfo("explorer.exe", "\"" + workDir + "\"") { UseShellExecute = true }); }, null, "Arrange files in Explorer; press Refresh afterwards."),
                           U.Btn("Refresh", Build, "BtnGhost"));
            wb.Margin = new Thickness(10, 0, 0, 0); DockPanel.SetDock(wb, Dock.Right); wrow.Children.Add(wb); wrow.Children.Add(wbox);
            c1.Children.Add(U.Field("Work folder", "Studio's own folder for this patch. Nothing here touches the game until you install.", wrow));
            var adds = new WrapPanel();
            foreach (var b in new UIElement[]
            {
                U.Btn("Start from an installed patch…", StartFromInstalled, "BtnPrimary", "Copy every file out of an existing patch (e.g. your patch-Z) into the work folder, so you change it instead of rebuilding it from scratch and losing files."),
                U.Btn("Add server DBC…", AddServerDbc, null, "Copy DBC files from your server's Data\\dbc folder into DBFilesClient\\. Use this after changing DBCs on the server so the client matches."),
                U.Btn("Add files…", AddFiles, null, "Copy any files (icons .blp, interface files, sounds...) into the work folder. You'll be asked which folder inside the patch they belong in."),
            }) { b.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 8)); adds.Children.Add(b); }
            c1.Children.Add(adds);

            var files = WorkFiles();
            var shadow = PatchSet.Shadowed(inst, patchName, files.Select(f => f.path));
            if (files.Count == 0) c1.Children.Add(U.Callout("The work folder is empty. Start from an installed patch, or add DBCs and files.", "info"));
            else
            {
                var dt = new DataTable();
                dt.Columns.Add("File in patch"); dt.Columns.Add("Size", typeof(string)); dt.Columns.Add("Today the client uses"); dt.Columns.Add("After installing");
                foreach (var (p, size) in files)
                {
                    var now = PatchSet.Winner(inst, p);
                    var after = shadow.TryGetValue(p, out var sh) ? $"still {sh} (it loads later and wins)" : $"{patchName} (yours)";
                    dt.Rows.Add(p, Size(size), now == null ? "base game files" : now.FileName, after);
                }
                var g = new DataGrid { IsReadOnly = true, ItemsSource = dt.DefaultView, MaxHeight = 320, AutoGenerateColumns = true };
                g.AutoGeneratingColumn += (s, e) => e.Column.Header = new TextBlock { Text = e.PropertyName };
                c1.Children.Add(U.T($"{files.Count} file(s), {Size(files.Sum(f => f.size))}", 12.5, "Muted", null, false, new Thickness(0, 4, 0, 8)));
                c1.Children.Add(g);
                if (shadow.Count > 0) { var cb = U.Callout($"{shadow.Count} file(s) would stay hidden, because a patch that loads after {patchName} also contains them ({string.Join(", ", shadow.Values.Distinct())}). Pick a later letter, or rebuild that patch instead.", "warn", "Some files won't take effect"); cb.Margin = new Thickness(0, 10, 0, 0); c1.Children.Add(cb); }
            }
            sp.Children.Add(U.Card(c1, new Thickness(20, 16, 20, 16), new Thickness(0, 0, 0, 12)));

            // ---- 2 build
            var c2 = new StackPanel();
            c2.Children.Add(U.Eyebrow("2 · Build"));
            var nbox = U.Box(patchName, true); nbox.Width = 200;
            nbox.LostFocus += (s, e) => { if (nbox.Text.Trim() != patchName) { patchName = nbox.Text.Trim(); Build(); } };
            var suggest = PatchSet.SuggestFreeName(inst);
            var nameRow = U.Row(nbox, U.Btn("Use " + suggest, () => { patchName = suggest; Build(); }, "BtnGhost", "The latest free letter, so your patch wins over the most others."));
            ((FrameworkElement)nameRow.Children[1]).Margin = new Thickness(10, 0, 0, 0);
            c2.Children.Add(U.Field("Patch file name", "The client loads patch.MPQ, then patch-2 … patch-9, then patch-A … patch-Z. Later ones win when two contain the same file.", nameRow));
            bool loadable = PatchNames.IsLoadable(patchName, out var why);
            var clash = inst.FirstOrDefault(p => string.Equals(p.FileName, patchName, StringComparison.OrdinalIgnoreCase));
            if (!loadable) c2.Children.Add(U.CheckRow(U.Check.Bad, "Name", why));
            else if (clash != null) c2.Children.Add(U.CheckRow(U.Check.Warn, "Name", $"{clash.FileName} is already installed ({clash.Files.Count:N0} named files). Installing replaces it (after a backup). If you didn't start from it, its other files will be gone: use \"Start from an installed patch\" first."));
            else c2.Children.Add(U.CheckRow(U.Check.Ok, "Name", $"{patchName} is free. It loads after: {string.Join(", ", inst.Where(p => p.Rank < PatchNames.Rank(patchName)).Select(p => p.FileName).DefaultIfEmpty("nothing"))}."));
            var comp = new CheckBox { IsChecked = compress, Content = U.T("Compress files (zlib)", 13.5, "Text", null, false), Margin = new Thickness(0, 12, 0, 12), ToolTip = "Smaller patch, same result in game. Untick only for troubleshooting." };
            comp.Checked += (s, e) => compress = true; comp.Unchecked += (s, e) => compress = false;
            c2.Children.Add(comp);
            var buildBtn = U.Btn("Build " + patchName, DoBuild, "BtnPrimary", "Writes the patch to Studio's patches folder and reads every file back to check it. The game folder isn't touched.");
            buildBtn.IsEnabled = loadable && files.Count > 0;
            var zipBtn = U.Btn("Export zip for players…", DoZip, null, "A zip players unpack into their WoW folder, with install instructions.");
            zipBtn.IsEnabled = lastBuild != null && File.Exists(lastBuild);
            c2.Children.Add(U.Row(buildBtn, zipBtn, U.Btn("Open patches folder", () => { Directory.CreateDirectory(OutRoot); Process.Start(new ProcessStartInfo("explorer.exe", "\"" + OutRoot + "\"") { UseShellExecute = true }); }, "BtnGhost")));
            if (lastProblems != null)
                c2.Children.Add(lastProblems.Count == 0
                    ? U.Callout($"Built {Path.GetFileName(lastBuild)} ({Size(new FileInfo(lastBuild).Length)}) and read every file back: all identical.", "ok", "Built and checked")
                    : U.Callout(string.Join("\n", lastProblems.Take(10)), "bad", "Build failed the read-back check (nothing was kept)"));
            if (lastProblems != null) ((FrameworkElement)c2.Children[c2.Children.Count - 1]).Margin = new Thickness(0, 12, 0, 0);
            sp.Children.Add(U.Card(c2, new Thickness(20, 16, 20, 16), new Thickness(0, 0, 0, 12)));

            // ---- 3 install
            var c3 = new StackPanel();
            c3.Children.Add(U.Eyebrow("3 · Install into your client"));
            c3.Children.Add(U.T("Copies the built patch into Data\\. If a file with that name is there, it's backed up first to " + Path.Combine(S.Profiles.Settings.BackupRoot, "client_<date_time>") + ". You can undo it from Installed patches. Delete the client's Cache folder afterwards.", 13, "Muted", null, true, new Thickness(0, 0, 0, 12)));
            var cbox = U.Box(confirm, true); cbox.Width = 200;
            var instBtn = U.Btn("Install " + patchName, DoInstall, "BtnPrimary", "Only enabled after a successful build, with the game closed, and after typing INSTALL.");
            void Upd() => instBtn.IsEnabled = lastBuild != null && lastProblems?.Count == 0 && Path.GetFileName(lastBuild).Equals(patchName, StringComparison.OrdinalIgnoreCase) && DataDir != null && Directory.Exists(DataDir) && cbox.Text.Trim() == "INSTALL";
            cbox.TextChanged += (s, e) => { confirm = cbox.Text; Upd(); };
            Upd();
            var f = U.Field("Type INSTALL to confirm", null, cbox, "A typed word so the game folder is never changed by a stray click.", 220);
            f.Margin = new Thickness(0, 0, 12, 0);
            instBtn.VerticalAlignment = VerticalAlignment.Bottom;
            c3.Children.Add(U.Row(f, instBtn));
            sp.Children.Add(U.Card(c3, new Thickness(20, 16, 20, 16), new Thickness(0)));
            return sp;
        }

        static string Size(long b) => b >= 1 << 20 ? (b / 1048576.0).ToString("0.0") + " MB" : b >= 1024 ? (b / 1024.0).ToString("0.0") + " KB" : b + " B";

        void StartFromInstalled()
        {
            var inst = Installed().Where(p => p.Files.Count > 0).ToList();
            if (inst.Count == 0) { w.Toast("No installed patch with a readable file list was found. Set the client folder first.", "warn"); return; }
            var dlg = new PickerWindow(w, "Start from an installed patch", "Every named file in it is copied into the work folder. The patch itself isn't changed.",
                q => inst.Where(p => p.FileName.Contains(q ?? "", StringComparison.OrdinalIgnoreCase)).Select(p => ((long)inst.IndexOf(p), p.FileName, $"{p.Files.Count:N0} files · {Size(p.Size)}")).ToList(), "");
            if (dlg.ShowDialog() != true || dlg.Picked == null) return;
            var src = inst[(int)dlg.Picked.Value];
            if (src.Size > 600L * 1024 * 1024 && ChoiceWindow.Ask(w, "Large patch", $"{src.FileName} is {Size(src.Size)}. Copying it out can take a while and uses that much disk space.", "Copy it", "Cancel", "Cancel") != 0) return;
            patchName = src.FileName; workDir = Path.Combine(WorkRoot, Path.GetFileNameWithoutExtension(src.FileName));
            w.Toast($"Extracting {src.Files.Count:N0} files from {src.FileName}…", "info");
            var wd = workDir;
            Task.Run(() => PatchInstaller.Extract(src.FullPath, src.Files, wd)).ContinueWith(t => w.Dispatcher.Invoke(() =>
            {
                if (t.Exception != null) w.Toast("Extract failed: " + t.Exception.GetBaseException().Message, "bad");
                else w.Toast($"Copied {t.Result.ok:N0} file(s) into the work folder" + (t.Result.failed.Count > 0 ? $"; {t.Result.failed.Count} couldn't be read ({t.Result.failed[0]})" : "") + ". Edit or add files, then build.", t.Result.failed.Count > 0 ? "warn" : "ok");
                Build();
            }));
        }

        void AddServerDbc()
        {
            string dir = null;
            try { var d = ConfParser.ParseFile(S.Profile.ConfPath).DataDir; if (!string.IsNullOrEmpty(d)) dir = Path.Combine(d, "dbc"); } catch { }
            var dlg = new OpenFileDialog { Title = "Pick server DBC files", Filter = "DBC tables|*.dbc", Multiselect = true };
            if (dir != null && Directory.Exists(dir)) dlg.InitialDirectory = dir;
            if (dlg.ShowDialog(w) != true) return;
            var dest = Path.Combine(workDir, "DBFilesClient");
            Directory.CreateDirectory(dest);
            foreach (var f in dlg.FileNames) File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), true);
            w.Toast($"Copied {dlg.FileNames.Length} DBC file(s) into DBFilesClient\\.", "ok");
            Build();
        }

        void AddFiles()
        {
            var dlg = new OpenFileDialog { Title = "Pick files to add to the patch", Multiselect = true };
            if (dlg.ShowDialog(w) != true) return;
            var folder = TextAsk.Ask(w, "Folder inside the patch", "Where do these files go inside the patch? For example Interface\\Icons for icons, or DBFilesClient for DBC tables. Leave empty for the top level.", "Interface\\Icons");
            if (folder == null) return;
            folder = folder.Trim().Trim('\\', '/');
            if (folder.Contains("..") || folder.Contains(':')) { w.Toast("Use a relative folder like Interface\\Icons.", "bad"); return; }
            var dest = Path.Combine(workDir, folder);
            Directory.CreateDirectory(dest);
            foreach (var f in dlg.FileNames) File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), true);
            w.Toast($"Added {dlg.FileNames.Length} file(s) under {(folder.Length == 0 ? "the top level" : folder)}.", "ok");
            Build();
        }

        void DoBuild()
        {
            var target = Path.Combine(OutRoot, patchName);
            var wd = workDir;
            w.Toast("Building " + patchName + "…", "info");
            Task.Run(() =>
            {
                var files = MpqWriter.CollectFolder(wd);
                return MpqWriter.BuildVerified(target, files, compress);
            }).ContinueWith(t => w.Dispatcher.Invoke(() =>
            {
                lastBuild = target;
                lastProblems = t.Exception != null ? new List<string> { t.Exception.GetBaseException().Message } : t.Result;
                w.Toast(lastProblems.Count == 0 ? $"Built and checked {patchName}." : "The build didn't pass its check.", lastProblems.Count == 0 ? "ok" : "bad");
                Build();
            }));
        }

        void DoZip()
        {
            var d = new SaveFileDialog { FileName = Path.GetFileNameWithoutExtension(patchName) + "_for_players.zip", Filter = "Zip|*.zip" };
            if (d.ShowDialog(w) != true) return;
            PatchInstaller.ExportZip(lastBuild, patchName, d.FileName);
            w.Toast("Saved " + d.FileName, "ok");
        }

        void DoInstall()
        {
            try
            {
                var rec = Installer.Install(lastBuild, ClientDir, patchName);
                confirm = ""; installed = null;
                w.Toast($"Installed {rec.Name}." + (rec.BackupOfPrevious != null ? " The previous file is backed up at " + rec.BackupOfPrevious : "") + " Delete the client's Cache folder, then start the game.", "ok");
            }
            catch (Exception ex) { w.Toast("Not installed: " + ex.Message, "bad"); }
            Build();
        }

        // ================================================================== installed
        UIElement InstalledView()
        {
            var sp = new StackPanel { MaxWidth = 1180, HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(ClientFolderCard());
            var inst = Installed();
            var mine = Installer.Records();
            var c = new StackPanel();
            c.Children.Add(U.Row(U.H2("Load order"), new Border { Width = 8 }, U.Pill("last one wins", "Panel2", "Muted")));
            c.Children.Add(U.T("The client opens these in this order. When two patches contain the same file, the lower one in this list wins.", 13, "Muted", null, true, new Thickness(0, 0, 0, 10)));
            if (inst.Count == 0) c.Children.Add(U.T("No patches found (set the client folder above).", 13, "Faint"));
            foreach (var p in inst)
            {
                var rec = mine.FirstOrDefault(r => string.Equals(r.Name, p.FileName, StringComparison.OrdinalIgnoreCase));
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                if (rec != null)
                {
                    var un = U.Btn("Uninstall", () =>
                    {
                        if (ChoiceWindow.Ask(w, "Uninstall " + p.FileName, $"Remove {p.FileName} from Data\\" + (rec.BackupOfPrevious != null ? " and put back the version that was there before" : "") + "? A copy of the removed file is kept in the backup folder.", "Uninstall", "Cancel", "Cancel") != 0) return;
                        try { w.Toast(Installer.Uninstall(ClientDir, p.FileName), "ok"); } catch (Exception ex) { w.Toast("Not uninstalled: " + ex.Message, "bad"); }
                        installed = null; Build();
                    }, null, "Remove this patch (only patches Studio installed).");
                    un.Padding = new Thickness(12, 5, 12, 5);
                    DockPanel.SetDock(un, Dock.Right); row.Children.Add(un);
                }
                if (p.Unnamed > 0)
                {
                    var rn = U.Btn(recovering ? "Recovering…" : "Recover names", () => RecoverNames(p), "BtnGhost", "Find this archive's file names by hashing candidate paths from your other archives, the server's DBCs and the files inside it. Read-only; results are saved for next time.");
                    rn.IsEnabled = !recovering; rn.Padding = new Thickness(12, 5, 12, 5);
                    DockPanel.SetDock(rn, Dock.Right); row.Children.Add(rn);
                }
                var info = U.Row(U.T(p.FileName, 13.5, "Text", FontWeights.SemiBold, false), new Border { Width = 10 },
                    U.Pill(Size(p.Size), "Panel2", "Muted"),
                    U.Pill(p.Error != null ? "unreadable" : $"{p.FileCount:N0} files", p.Error != null ? "BadBg" : "Panel2", p.Error != null ? "Bad" : "Muted", p.Error),
                    p.Unnamed > 0 ? U.Pill($"{p.Unnamed:N0} without names ({100.0 * (p.FileCount - p.Unnamed) / Math.Max(1, p.FileCount):0.#}% named)", "AmberDim", "Amber", "This patch has no (listfile). Studio can recover most names by matching known paths against the archive's hash table; unknown files can still be extracted.") : null,
                    rec != null ? U.Pill("installed by Studio " + rec.InstalledUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), "AmberDim", "Amber") : null);
                row.Children.Add(info);
                c.Children.Add(new Border { Child = row, Padding = new Thickness(10, 6, 10, 6), Background = U.R("Panel2"), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 0, 4) });
            }
            sp.Children.Add(U.Card(c, new Thickness(20, 16, 20, 16), new Thickness(0, 0, 0, 12)));

            // which patch wins
            var c2 = new StackPanel();
            c2.Children.Add(U.H2("Which patch wins?"));
            var q = U.Box(whoWins, true);
            var go = U.Btn("Look up", null, "BtnPrimary");
            void Lookup() { whoWins = q.Text.Trim(); Build(); }
            go.Click += (s, e) => Lookup();
            q.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) Lookup(); };
            var row2 = new DockPanel(); go.Margin = new Thickness(10, 0, 0, 0); DockPanel.SetDock(go, Dock.Right); row2.Children.Add(go); row2.Children.Add(U.WithPlaceholder(q, "Part of a file path, e.g. Spell.dbc or Icons\\INV_Sword"));
            c2.Children.Add(U.Field("File", "Shows every patch that contains a matching file, and which one the game actually uses.", row2));
            if (whoWins.Length >= 3)
            {
                var all = inst.SelectMany(p => p.Files.Where(f => f.Contains(whoWins, StringComparison.OrdinalIgnoreCase))).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).Take(200).ToList();
                var dt = new DataTable(); dt.Columns.Add("File"); dt.Columns.Add("Game uses"); dt.Columns.Add("Also in");
                foreach (var f in all)
                {
                    var win = PatchSet.Winner(inst, f);
                    dt.Rows.Add(f, win.FileName, string.Join(", ", inst.Where(p => p != win && p.Files.Contains(f)).Select(p => p.FileName)));
                }
                var g = new DataGrid { IsReadOnly = true, ItemsSource = dt.DefaultView, MaxHeight = 360 };
                g.AutoGeneratingColumn += (s, e) => e.Column.Header = new TextBlock { Text = e.PropertyName };
                c2.Children.Add(U.T(all.Count == 0 ? "No patch contains a matching file (base game files are used)." : $"{all.Count} matching file(s){(all.Count >= 200 ? " (first 200)" : "")}.", 12.5, "Muted", null, false, new Thickness(0, 0, 0, 8)));
                if (all.Count > 0) c2.Children.Add(g);
            }
            sp.Children.Add(U.Card(c2, new Thickness(20, 16, 20, 16), new Thickness(0)));
            return sp;
        }

        // ================================================================== browse
        UIElement BrowseView()
        {
            var sp = new StackPanel { MaxWidth = 1180, HorizontalAlignment = HorizontalAlignment.Left };
            var pbox = U.Box(browsePath ?? "", true);
            var pick = U.Btn("Open MPQ…", () =>
            {
                var d = new OpenFileDialog { Title = "Open an MPQ archive (read-only)", Filter = "MPQ archives|*.mpq|All files|*.*" };
                if (DataDir != null && Directory.Exists(DataDir)) d.InitialDirectory = DataDir;
                if (d.ShowDialog(w) == true) { browsePath = d.FileName; Build(); }
            }, "BtnPrimary", "Opens the archive read-only. Nothing in it is changed.");
            var row = new DockPanel(); pick.Margin = new Thickness(10, 0, 0, 0); DockPanel.SetDock(pick, Dock.Right); row.Children.Add(pick); row.Children.Add(pbox);
            var c = U.Stack(U.Field("Archive", "Any MPQ: the game's own archives, a patch, or one you built.", row));
            if (browsePath != null)
            {
                try
                {
                    using var r = new MpqReader(browsePath);
                    NameRecovery.ApplyCache(NameCache, browsePath, r);
                    var files = r.Files.ToList();
                    int namedCount = files.Count(f => f.Name != null);
                    // files without a name are listed as "unknown #index.ext" and can still be extracted
                    var rows = files.Select(f => (name: r.DisplayName(f), f)).ToList();
                    c.Children.Add(U.Row(U.Pill($"format v{r.FormatVersion + 1}", "Panel2", "Muted"), U.Pill($"{files.Count:N0} files", "Panel2", "Muted"),
                        U.Pill($"{namedCount:N0} named ({100.0 * namedCount / Math.Max(1, files.Count):0.#}%)", namedCount < files.Count ? "AmberDim" : "Panel2", namedCount < files.Count ? "Amber" : "Muted"), U.Pill("read-only", "OkBg", "Ok")));
                    var fbox = U.Box(browseFilter, true);
                    fbox.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) { browseFilter = fbox.Text.Trim(); Build(); } };
                    var ff = U.Field("Filter (Enter)", "Part of a path, e.g. DBFilesClient or .blp, or \"unknown\" for files without a name", fbox, null, 420); ff.Margin = new Thickness(0, 12, 0, 8);
                    c.Children.Add(ff);
                    var shown = rows.Where(x => browseFilter.Length == 0 || x.name.Contains(browseFilter, StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.name, StringComparer.OrdinalIgnoreCase).Take(2000).Select(x => x.f).ToList();
                    var dt = new DataTable(); dt.Columns.Add("Path"); dt.Columns.Add("Size"); dt.Columns.Add("Stored"); dt.Columns.Add("Storage");
                    foreach (var f in shown) dt.Rows.Add(r.DisplayName(f), Size(f.FileSize), Size(f.CompressedSize), (f.Compressed ? "compressed" : "stored") + ((f.Flags & MpqFileFlags.Encrypted) != 0 ? ", encrypted" : "") + ((f.Flags & MpqFileFlags.SingleUnit) != 0 ? ", single unit" : ""));
                    var named = files.Where(f => f.Name != null).ToList();
                    var g = new DataGrid { IsReadOnly = true, ItemsSource = dt.DefaultView, MaxHeight = 440, SelectionUnit = DataGridSelectionUnit.FullRow, SelectionMode = DataGridSelectionMode.Extended };
                    g.AutoGeneratingColumn += (s, e) => e.Column.Header = new TextBlock { Text = e.PropertyName };
                    c.Children.Add(U.T(shown.Count >= 2000 ? "Showing the first 2,000 matches." : $"{shown.Count:N0} shown.", 12.5, "Muted", null, false, new Thickness(0, 0, 0, 6)));
                    c.Children.Add(g);
                    if (files.Count > named.Count) c.Children.Add(U.T($"{files.Count - named.Count:N0} file(s) have no known name. They're listed as \"unknown #…\" (type guessed from their contents) and extract into an \"unknown\" folder. Installed patches › Recover names can find more of them.", 12, "Faint", null, true, new Thickness(0, 6, 0, 0)));
                    var ex = U.Btn("Extract selected…", () => ExtractRows(g.SelectedItems.Cast<DataRowView>().Select(v => (string)v.Row[0]).ToList()), null, "Copy the selected files out, keeping their folders.");
                    var exAll = U.Btn("Extract all shown…", () => ExtractRows(shown.Select(s2 => r.DisplayName(s2)).ToList()), "BtnGhost");
                    var exRow = U.Row(ex, exAll); exRow.Margin = new Thickness(0, 12, 0, 0);
                    c.Children.Add(exRow);
                }
                catch (Exception ex) { c.Children.Add(U.Callout(ex.Message, "bad", "Couldn't open it")); }
            }
            sp.Children.Add(U.Card(c, new Thickness(20, 16, 20, 16), new Thickness(0)));
            return sp;
        }

        void ExtractRows(List<string> paths)
        {
            if (paths.Count == 0) { w.Toast("Select some files first.", "warn"); return; }
            var d = new OpenFolderDialog { Title = "Extract to which folder?" };
            if (d.ShowDialog(w) != true) return;
            var (ok, failed) = PatchInstaller.Extract(browsePath, paths, d.FolderName, NameCache);
            w.Toast($"Extracted {ok} file(s)" + (failed.Count > 0 ? $"; {failed.Count} failed ({failed[0]})" : "") + ".", failed.Count > 0 ? "warn" : "ok");
        }

        // ================================================================== screenshots
        public void ScreensPrepare(string serverDbcDir, string clientDir)
        {
            if (S.Profile != null) S.Profile.ClientDir = clientDir;
            installed = null;
            patchName = PatchSet.SuggestFreeName(Installed());
            workDir = Path.Combine(WorkRoot, Path.GetFileNameWithoutExtension(patchName));
            var dest = Path.Combine(workDir, "DBFilesClient");
            Directory.CreateDirectory(dest);
            foreach (var n in new[] { "Faction.dbc", "CharTitles.dbc", "ItemSet.dbc" })
                if (File.Exists(Path.Combine(serverDbcDir, n))) File.Copy(Path.Combine(serverDbcDir, n), Path.Combine(dest, n), true);
            tBuild.IsChecked = true; Build();
        }
        public void ScreensBuild()
        {
            var target = Path.Combine(OutRoot, patchName);
            lastBuild = target;
            lastProblems = MpqWriter.BuildVerified(target, MpqWriter.CollectFolder(workDir), compress);
            confirm = "INSTALL";
            Build();
        }
        public void ScreensInstalled(string lookup) { whoWins = lookup; tInstalled.IsChecked = true; Build(); }
        public void ScreensBrowse(string mpq, string filter) { browsePath = mpq; browseFilter = filter; tBrowse.IsChecked = true; Build(); }
    }

    /// <summary>Tiny themed text prompt.</summary>
    public sealed class TextAsk : Window
    {
        string result;
        public static string Ask(Window owner, string title, string text, string initial)
        {
            var d = new TextAsk(owner, title, text, initial);
            d.ShowDialog();
            return d.result;
        }
        TextAsk(Window owner, string title, string text, string initial)
        {
            Owner = owner; Width = 480; SizeToContent = SizeToContent.Height; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Background = U.R("Panel"); FontFamily = U.Font;
            System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome { CaptionHeight = 30, GlassFrameThickness = new Thickness(0), ResizeBorderThickness = new Thickness(0) });
            var box = U.Box(initial, true); box.Margin = new Thickness(0, 12, 0, 0);
            var ok = U.Btn("OK", () => { result = box.Text; Close(); }, "BtnPrimary"); ok.IsDefault = true;
            var cancel = U.Btn("Cancel", () => Close(), "BtnGhost"); cancel.IsCancel = true;
            var row = U.Row(ok, cancel); row.Margin = new Thickness(0, 16, 0, 0);
            Content = new Border { BorderBrush = U.R("LineHi"), BorderThickness = new Thickness(1), Padding = new Thickness(22, 18, 22, 18), Child = U.Stack(U.H2(title), U.T(text, 13, "Muted"), box, row) };
            Loaded += (s, e) => { box.Focus(); box.SelectAll(); };
        }
    }
}
