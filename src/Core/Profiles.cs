using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AndersCraftStudio.Core
{
    /// <summary>One server you work on. Saved as JSON in %APPDATA%\AndersCraftStudio\profiles. Passwords are encrypted per Windows user.</summary>
    public sealed class Profile
    {
        public string Name { get; set; } = "My server";
        public string ConfPath { get; set; }
        public string Core { get; set; }
        public string Host { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 3306;
        public string WorldDb { get; set; } = "acore_world";
        public string CharactersDb { get; set; } = "acore_characters";
        public string AuthDb { get; set; } = "acore_auth";
        public string SandboxDb { get; set; } = "acore_world_studio_sandbox";
        public bool SandboxExists { get; set; }
        public string RoUser { get; set; } = "studio_ro";
        public string RoPassEnc { get; set; } = "";
        public string RwUser { get; set; } = "studio_rw";
        public string RwPassEnc { get; set; } = "";
        public string AccountHost { get; set; } = "localhost";
        public bool AllowAuthView { get; set; } = true;
        public string ClientDir { get; set; } = "";
        public bool SetupComplete { get; set; }
        public DateTime? AccountsVerifiedUtc { get; set; }
    }

    public sealed class AppSettings
    {
        public string ActiveProfile { get; set; } = "My server";
        public string ChangesetsDir { get; set; } = Path.Combine(DefaultWorkspace, "changesets");
        public string BackupRoot { get; set; } = Path.Combine(DefaultWorkspace, "backups");
        public string MysqldumpPath { get; set; } = FindMysqldump() ?? "";
        /// <summary>Studio's own working folder (patch_work, patches). Empty = the folder that holds the changesets folder.</summary>
        public string WorkspaceDir { get; set; } = "";

        /// <summary>Documents\AndersCraft Studio: the default home for changesets, backups and patch work folders.</summary>
        public static string DefaultWorkspace => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AndersCraft Studio");

        public string Workspace()
        {
            if (!string.IsNullOrWhiteSpace(WorkspaceDir)) return WorkspaceDir;
            var d = Path.GetDirectoryName((ChangesetsDir ?? "").TrimEnd('\\', '/'));
            return string.IsNullOrEmpty(d) ? DefaultWorkspace : d;
        }

        /// <summary>mysqldump.exe from a standard MySQL or MariaDB install (newest first), or null.</summary>
        public static string FindMysqldump()
        {
            try
            {
                var roots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) }.Where(x => !string.IsNullOrEmpty(x) && Directory.Exists(x)).Distinct();
                foreach (var pf in roots)
                    foreach (var vendor in new[] { "MySQL", "MariaDB" })
                    {
                        var exes = new List<string>();
                        foreach (var h in Directory.GetDirectories(pf, vendor + "*"))
                        {
                            exes.Add(Path.Combine(h, "bin", "mysqldump.exe"));
                            foreach (var sub in Directory.GetDirectories(h)) exes.Add(Path.Combine(sub, "bin", "mysqldump.exe"));
                        }
                        var hit = exes.Where(File.Exists).OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                        if (hit != null) return hit;
                    }
            }
            catch { }
            return null;
        }
        /// <summary>Settings &gt; Safety. Off by default: Apply can only target the sandbox.</summary>
        public bool AllowLive { get; set; }
        public int PageSize { get; set; } = 200;
        /// <summary>"Duplicate as new" picks IDs at or above this, away from Blizzard's ranges.</summary>
        public long CustomIdStart { get; set; } = Duplicator.DefaultCustomStart;
    }

    public interface ISecretProtector
    {
        string Protect(string plain);
        string Unprotect(string enc);
    }

    public sealed class ProfileStore
    {
        public string Root { get; }
        static readonly JsonSerializerOptions JO = new JsonSerializerOptions { WriteIndented = true };
        public AppSettings Settings { get; private set; } = new AppSettings();

        public ProfileStore(string root)
        {
            Root = root;
            Directory.CreateDirectory(Path.Combine(root, "profiles"));
            try { if (File.Exists(SettingsPath)) Settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JO) ?? new AppSettings(); }
            catch { Settings = new AppSettings(); }
        }

        public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AndersCraftStudio");
        string SettingsPath => Path.Combine(Root, "settings.json");

        public void SaveSettings() => File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Settings, JO));

        public static string SafeFileName(string name)
        {
            var bad = Path.GetInvalidFileNameChars();
            var s = new string((name ?? "profile").Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim();
            return s.Length == 0 ? "profile" : s;
        }

        public string ProfilePath(string name) => Path.Combine(Root, "profiles", SafeFileName(name) + ".json");

        public Profile Load(string name)
        {
            try { var p = ProfilePath(name); return File.Exists(p) ? JsonSerializer.Deserialize<Profile>(File.ReadAllText(p), JO) : null; }
            catch { return null; }
        }

        public Profile Active() => Load(Settings.ActiveProfile);

        public void Save(Profile p)
        {
            File.WriteAllText(ProfilePath(p.Name), JsonSerializer.Serialize(p, JO));
            Settings.ActiveProfile = p.Name;
            SaveSettings();
        }

        public List<string> Names() => Directory.GetFiles(Path.Combine(Root, "profiles"), "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(x => x).ToList();
    }

    public static class Passwords
    {
        const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        /// <summary>Random password from letters and digits only (no quotes or symbols, so it pastes cleanly into any SQL client).</summary>
        public static string Generate(int length = 24)
        {
            var sb = new StringBuilder(length);
            for (int i = 0; i < length; i++) sb.Append(Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]);
            return sb.ToString();
        }
    }
}
