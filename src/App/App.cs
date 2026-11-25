using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using AndersCraftStudio.Core;

namespace AndersCraftStudio
{
    public static class App
    {
        public static Application Current;
        public static BitmapFrame Icon;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SetCurrentProcessExplicitAppUserModelID(string id);

        /// <summary>Private → neutral text for screenshots that will be published (longest, most specific first).</summary>
        static List<(string, string)> PublicRedactions(Profile real, ProfileStore store)
        {
            var r = new List<(string, string)>();
            string ServerRoot(string conf) { try { var d = Path.GetDirectoryName(Path.GetDirectoryName(conf)); return d; } catch { return null; } }
            if (real != null)
            {
                var sr = ServerRoot(real.ConfPath);
                if (!string.IsNullOrEmpty(sr)) r.Add((sr, @"C:\AzerothCore"));
                if (!string.IsNullOrEmpty(real.ClientDir)) r.Add((real.ClientDir.TrimEnd('\\'), @"C:\Games\WoW 3.3.5a"));
                if (!string.IsNullOrEmpty(real.RoUser)) r.Add((real.RoUser, "studio_ro"));
                if (!string.IsNullOrEmpty(real.RwUser)) r.Add((real.RwUser, "studio_rw"));
                r.Add(("=" + real.Name, "My Realm"));
                r.Add(("\"" + real.Name + "\"", "\"My Realm\""));
                r.Add(($"{real.Host}:{real.Port}", "127.0.0.1:3306"));
            }
            var tmp = Path.GetTempPath().TrimEnd('\\');
            r.Add((tmp, @"C:\Users\you\AppData\Local\Temp"));
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            r.Add((home, @"C:\Users\you"));
            var drive = Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd('\\'));
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                if (d.Parent != null && d.Parent.Parent == null) { r.Add((d.FullName, @"C:\Server")); break; }   // top-level dev folder
            store.Settings.CustomIdStart = 5000000;   // neutral custom IDs in the pictures
            store.SaveSettings();
            return r;
        }

        [STAThread]
        public static int Main(string[] args)
        {
            try { SetCurrentProcessExplicitAppUserModelID("AndersCraft.Studio"); } catch { }
            string screens = Arg(args, "--screens");
            bool demoConf = args.Contains("--demo-conf");   // screenshots only: browse with the worldserver.conf login, read-only
            bool fresh = args.Contains("--fresh");
            bool pub = args.Contains("--public");            // screenshots only: neutral paths and names for publishing           // screenshots only: a brand-new user (no profile, nothing to auto-detect)
            string makeIcon = Arg(args, "--make-icon");

            Current = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/AndersCraftStudio;component/Theme.xaml") });
            if (makeIcon != null) { IconMaker.Write(makeIcon); return 0; }
            try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/AndersCraftStudio;component/Studio.ico")); } catch { }

            Studio state;
            if (screens != null)
            {
                // screenshot mode never touches your real settings or changesets
                var tmp = Path.Combine(Path.GetTempPath(), "AndersCraftStudio_screens_" + DateTime.Now.ToString("yyyyMMddHHmmss"));
                var store = new ProfileStore(Path.Combine(tmp, "appdata"));
                // If you've finished setup, screenshots browse with your own verified read-only account (a copy of the
                // profile goes into the throw-away folder; your real settings are only read).
                Profile real = null;
                try { var rp = new ProfileStore(ProfileStore.DefaultRoot).Active(); if (rp != null && rp.AccountsVerifiedUtc != null) real = rp; } catch { }
                if (fresh) { real = null; var empty = Path.Combine(tmp, "empty_drive"); Directory.CreateDirectory(empty); ConfLocator.RootsOverride = () => new[] { empty }; }
                if (real != null) { real.SetupComplete = false; store.Save(real); }
                if (pub) MainWindow.Redactions = PublicRedactions(real, store);
                state = new Studio(store, Path.Combine(tmp, "changesets")) { DemoConf = demoConf && real == null, ScreensRealAccount = real != null };
            }
            else
            {
                var ps = new ProfileStore(ProfileStore.DefaultRoot);
                state = new Studio(ps, ps.Settings.ChangesetsDir);
            }
            Current.DispatcherUnhandledException += (s, e) =>
            {
                e.Handled = true;
                Log.Error("Unhandled", e.Exception);
                try { MainWindow.Instance?.Toast(Friendly.Text(e.Exception) + "  (Details are in Help › Open logs.)", "bad"); } catch { }
            };
            MainWindow.Fresh = fresh;
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Error("Crash", e.ExceptionObject as Exception);
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) => { Log.Error("Background task", e.Exception); e.SetObserved(); };
            Log.Info($"Studio {AppInfo.Version} started" + (screens != null ? " (screenshots)" : ""));
            MainWindow.OnlyM7 = args.Contains("--m7");
            MainWindow.OnlyM8 = args.Contains("--m8");
            MainWindow.OnlyM9 = args.Contains("--m9");
            MainWindow.OnlyM10 = args.Contains("--m10");    // screenshots only: the Changes fixes (DBC status, a stopped Apply)      // screenshots only: just the 0.7 screens
            var w = new MainWindow(state, screens);
            var code = Current.Run(w);
            if (screens != null)
                try { Directory.Delete(Path.GetDirectoryName(state.Profiles.Root), true); Directory.Delete(Path.Combine(Path.GetTempPath(), "AndersCraftStudio_screens_patches"), true); } catch { }
            return code;
        }

        static string Arg(string[] a, string name) { for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1]; return null; }
    }

    /// <summary>Windows DPAPI: passwords can only be decrypted by the same Windows user on the same PC.</summary>
    public sealed class Dpapi : ISecretProtector
    {
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AndersCraft.Studio.v1");
        public string Protect(string plain) => string.IsNullOrEmpty(plain) ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser));
        public string Unprotect(string enc)
        {
            if (string.IsNullOrEmpty(enc)) return "";
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(enc), Entropy, DataProtectionScope.CurrentUser)); }
            catch { return ""; }
        }
    }

    /// <summary>Everything the screens share: the active profile, the changeset, and how to open connections.</summary>
    public sealed class Studio
    {
        public ProfileStore Profiles { get; }
        public Profile Profile { get; set; }
        public ChangesetStore Changes { get; private set; }
        public ISecretProtector Secrets { get; } = new Dpapi();
        public bool DemoConf { get; set; }
        public bool ScreensRealAccount { get; set; }
        public event Action ChangesChanged;
        public event Action ProfileChanged;

        public Studio(ProfileStore ps, string changesetsDir)
        {
            Profiles = ps;
            Profile = ps.Active();
            Changes = new ChangesetStore(changesetsDir);
        }

        public void ReloadChangesFolder(string dir) { Changes = new ChangesetStore(dir); RaiseChanges(); }
        public void RaiseChanges() { dbc = null; ChangesChanged?.Invoke(); }
        public void RaiseProfile() => ProfileChanged?.Invoke();
        public void SaveChanges() { Changes.SavePending(); RaiseChanges(); }

        /// <summary>Browsing needs a saved password AND a passed account check (logged in, confirmed read-only).</summary>
        public bool HasReadAccount => Profile != null && (DemoConf || (!string.IsNullOrEmpty(Profile.RoUser) && !string.IsNullOrEmpty(Profile.RoPassEnc) && Profile.AccountsVerifiedUtc != null));
        public bool HasWriteAccount => Profile != null && !string.IsNullOrEmpty(Profile.RwUser) && !string.IsNullOrEmpty(Profile.RwPassEnc) && Profile.AccountsVerifiedUtc != null;
        public int StagedCount => Changes.Pending.Changes.Count;
        /// <summary>Goes up after every successful Apply / Roll back, so editors know the rows they loaded are out of date and reload them.</summary>
        public int DataVersion { get; private set; }
        public void DatabaseWritten() => DataVersion++;

        /// <summary>Opens a read-only connection to one of the profile's databases using studio_ro.</summary>
        ModelLibrary models;
        /// <summary>Models and textures for the 3D viewer (client archives + patch work folders, read-only).</summary>
        public ModelLibrary Models => models ??= new ModelLibrary(this);

        public ReadOnlyDb OpenRead(string database)
        {
            if (Profile == null) throw new InvalidOperationException("No server profile yet. Run setup first.");
            if (DemoConf)
            {
                // screenshot/demo mode: the server's own login from worldserver.conf, forced READ ONLY by ReadOnlyDb
                var c = ConfParser.ParseFile(Profile.ConfPath).World;
                return new ReadOnlyDb(c.Host, c.Port, c.User, c.Password, database);
            }
            if (!HasReadAccount) throw new InvalidOperationException("The read-only account isn't set up yet. Finish setup step 3.");
            return new ReadOnlyDb(Profile.Host, Profile.Port, Profile.RoUser, Secrets.Unprotect(Profile.RoPassEnc), database);
        }

        public WriteDb OpenWrite(string database)
        {
            if (!HasWriteAccount) throw new InvalidOperationException("The apply account isn't set up yet. Finish setup step 3.");
            return new WriteDb(Profile.Host, Profile.Port, Profile.RwUser, Secrets.Unprotect(Profile.RwPassEnc), database);
        }

        DbcNames dbc; string dbcFor;
        /// <summary>Names from the server's extracted DBC files (DataDir\dbc in worldserver.conf). Read-only.</summary>
        public DbcNames Dbc
        {
            get
            {
                var conf = Profile?.ConfPath;
                if (dbc == null || dbcFor != conf)
                {
                    string dir = null;
                    try { var d = conf != null && File.Exists(conf) ? ConfParser.ParseFile(conf).DataDir : null; if (!string.IsNullOrEmpty(d)) dir = Path.Combine(d, "dbc"); } catch { }
                    dbc = new DbcNames(dir) { Pending = () => Changes.Pending.Changes }; dbcFor = conf;
                }
                return dbc;
            }
        }

        public string[] AllDatabases => Profile == null ? new string[0] : new[] { Profile.WorldDb, Profile.SandboxDb, Profile.CharactersDb, Profile.AuthDb };
    }
}
