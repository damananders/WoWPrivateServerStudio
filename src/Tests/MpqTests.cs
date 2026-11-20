using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AndersCraftStudio.Core;

namespace AndersCraftStudio.Tests
{
    static class MpqTests
    {
        [Test] static void KnownHashValues()
        {
            // the well-known keys of the encrypted hash and block tables
            A.Eq(0xC3AF3770u, MpqCrypto.Hash("(hash table)", MpqCrypto.FileKey));
            A.Eq(0xEC83B3A3u, MpqCrypto.Hash("(block table)", MpqCrypto.FileKey));
            A.Eq(MpqCrypto.Hash("DBFilesClient\\Spell.dbc", 0), MpqCrypto.Hash("dbfilesclient/spell.DBC", 0), "case and slash insensitive");
        }

        [Test] static void EncryptDecryptRoundTrip()
        {
            var d = new uint[] { 1, 2, 3, 0xDEADBEEF, 0 };
            var c = (uint[])d.Clone();
            MpqCrypto.Encrypt(c, 0x12345678);
            A.False(c.SequenceEqual(d), "changed");
            MpqCrypto.Decrypt(c, 0x12345678);
            A.True(c.SequenceEqual(d), "restored");
        }

        static List<(string, byte[])> Sample()
        {
            var rnd = new Random(7);
            var big = new byte[4096 * 3 + 123]; rnd.NextBytes(big);              // incompressible, several sectors
            var text = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("AndersCraft ", 3000))); // compressible
            return new List<(string, byte[])>
            {
                ("DBFilesClient\\Spell.dbc", text), ("Interface\\Icons\\Test.blp", big), ("empty.txt", new byte[0]),
                ("Sound/one.wav", new byte[] { 1, 2, 3 }), ("Exactly\\4096.bin", Enumerable.Repeat((byte)9, 4096).ToArray()),
            };
        }

        [Test] static void WriteThenReadBack()
        {
            foreach (var compress in new[] { true, false })
            {
                var ms = new MemoryStream();
                var files = Sample();
                MpqWriter.Write(ms, files, compress);
                ms.Position = 0;
                using var r = new MpqReader(ms);
                A.Eq(files.Count + 1, r.Files.Count(), "files + listfile");
                foreach (var (p, d) in files) A.True(r.Read(p.Replace('/', '\\')).SequenceEqual(d), p + (compress ? " compressed" : " stored"));
                A.True(r.Files.All(f => f.Name != null), "every file named via (listfile)");
                A.True(r.Find("DBFILESCLIENT\\SPELL.DBC") != null, "lookup ignores case");
                A.Eq(null, r.Find("not\\there.txt"));
            }
        }

        [Test] static void CompressionShrinksText()
        {
            var ms = new MemoryStream();
            MpqWriter.Write(ms, new List<(string, byte[])> { ("a.txt", Encoding.ASCII.GetBytes(new string('x', 100000))) });
            A.True(ms.Length < 10000, "compressed archive is small: " + ms.Length);
        }

        [Test] static void WriterRejectsBadPaths()
        {
            A.Throws(() => MpqWriter.Write(new MemoryStream(), new List<(string, byte[])> { ("..\\x", new byte[1]) }), "parent path");
            A.Throws(() => MpqWriter.Write(new MemoryStream(), new List<(string, byte[])> { ("C:\\x", new byte[1]) }), "absolute path");
            A.Throws(() => MpqWriter.Write(new MemoryStream(), new List<(string, byte[])> { ("a\\b", new byte[1]), ("A/B", new byte[1]) }), "duplicate");
        }

        [Test] static void BuildVerifiedCreatesFile()
        {
            var dir = Path.Combine(Path.GetTempPath(), "acs_mpq_" + Guid.NewGuid().ToString("N"));
            try
            {
                var target = Path.Combine(dir, "patch-S.MPQ");
                var problems = MpqWriter.BuildVerified(target, Sample());
                A.Eq(0, problems.Count, "verified");
                A.True(File.Exists(target) && !File.Exists(target + ".building"), "moved into place");
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [Test] static void PatchNameRules()
        {
            A.True(PatchNames.IsLoadable("patch-Z.MPQ", out _), "letter");
            A.True(PatchNames.IsLoadable("patch-4.mpq", out _), "digit");
            A.True(PatchNames.IsLoadable("patch.MPQ", out _), "base");
            A.False(PatchNames.IsLoadable("patch-1.MPQ", out _), "1 not loaded");
            A.False(PatchNames.IsLoadable("mypatch.MPQ", out _), "other names");
            A.False(PatchNames.IsLoadable("patch-ZZ.MPQ", out _), "two letters");
            A.True(PatchNames.Rank("patch-Z.MPQ") > PatchNames.Rank("PATCH-A.MPQ") && PatchNames.Rank("patch-A.MPQ") > PatchNames.Rank("patch-9.MPQ"), "Z beats A beats 9");
        }

        [Test] static void PatchSetWinnerAndClashes()
        {
            var installed = new List<PatchInfo>
            {
                new PatchInfo { FileName = "patch-3.MPQ", Files = new HashSet<string>(new[] { "DBFilesClient\\Spell.dbc", "a.txt" }, StringComparer.OrdinalIgnoreCase) },
                new PatchInfo { FileName = "patch-Z.MPQ", Files = new HashSet<string>(new[] { "DBFilesClient\\Spell.dbc" }, StringComparer.OrdinalIgnoreCase) },
            };
            A.Eq("patch-Z.MPQ", PatchSet.Winner(installed, "dbfilesclient\\spell.dbc").FileName);
            A.Eq("patch-3.MPQ", PatchSet.Winner(installed, "a.txt").FileName);
            A.Eq(null, PatchSet.Winner(installed, "nope"));
            A.True(PatchSet.NameInUse(installed, "PATCH-z.mpq"), "clash detected");
            A.Eq("patch-Y.MPQ", PatchSet.SuggestFreeName(installed), "highest free letter");
            A.Eq("patch-W.MPQ", PatchSet.SuggestFreeName(installed.Concat(new[] { new PatchInfo { FileName = "patch-Y.MPQ" }, new PatchInfo { FileName = "PATCH-X.MPQ" } }).ToList()));
            var shadowed = PatchSet.Shadowed(installed, "patch-S.MPQ", new[] { "DBFilesClient\\Spell.dbc", "new.txt" });
            A.Eq(1, shadowed.Count, "Spell.dbc would be hidden by patch-Z");
            A.Eq("patch-Z.MPQ", shadowed["DBFilesClient\\Spell.dbc"]);
        }

        [Test] static void InstallBackupUninstallRestore()
        {
            var root = Path.Combine(Path.GetTempPath(), "acs_inst_" + Guid.NewGuid().ToString("N"));
            try
            {
                var client = Path.Combine(root, "FakeClient"); Directory.CreateDirectory(Path.Combine(client, "Data"));
                var oldPatch = Path.Combine(client, "Data", "patch-S.MPQ");
                using (var fs = File.Create(oldPatch)) MpqWriter.Write(fs, new List<(string, byte[])> { ("old.txt", new byte[] { 1 }) });
                var oldHash = PatchInstaller.Sha256Of(oldPatch);
                var built = Path.Combine(root, "built.MPQ");
                MpqWriter.BuildVerified(built, Sample());
                var gameOpen = new PatchInstaller(Path.Combine(root, "state.json"), Path.Combine(root, "backups"), _ => new List<string> { "Wow (pid 1)" });
                A.Throws(() => gameOpen.Install(built, client, "patch-S.MPQ"), "refuses while the game runs");
                A.Eq(oldHash, PatchInstaller.Sha256Of(oldPatch), "nothing touched when refused");
                var inst = new PatchInstaller(Path.Combine(root, "state.json"), Path.Combine(root, "backups"), _ => new List<string>());
                var rec = inst.Install(built, client, "patch-S.MPQ");
                A.True(rec.BackupOfPrevious != null && File.Exists(rec.BackupOfPrevious), "previous file backed up");
                A.Eq(PatchInstaller.Sha256Of(built), PatchInstaller.Sha256Of(oldPatch), "new patch in place");
                var msg = inst.Uninstall(client, "patch-S.MPQ");
                A.Contains(msg, "Restored");
                A.Eq(oldHash, PatchInstaller.Sha256Of(oldPatch), "original restored");
                A.Throws(() => inst.Uninstall(client, "patch-S.MPQ"), "not Studio's any more");
                A.Throws(() => inst.Install(built, client, "custom.MPQ"), "name the client won't load");
                var zip = Path.Combine(root, "players.zip");
                PatchInstaller.ExportZip(built, "patch-S.MPQ", zip);
                using var z = System.IO.Compression.ZipFile.OpenRead(zip);
                A.True(z.Entries.Any(e => e.FullName == "Data/patch-S.MPQ") && z.Entries.Any(e => e.Name == "HOW TO INSTALL.txt"), "zip layout");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Test] static void ExtractKeepsPaths()
        {
            var root = Path.Combine(Path.GetTempPath(), "acs_ext_" + Guid.NewGuid().ToString("N"));
            try
            {
                var mpq = Path.Combine(root, "a.MPQ");
                MpqWriter.BuildVerified(mpq, Sample());
                var (ok, failed) = PatchInstaller.Extract(mpq, new[] { "DBFilesClient\\Spell.dbc", "..\\evil.txt", "missing.txt" }, Path.Combine(root, "out"));
                A.Eq(1, ok); A.Eq(2, failed.Count, "unsafe + missing refused");
                A.True(File.Exists(Path.Combine(root, "out", "DBFilesClient", "Spell.dbc")), "path kept");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Test] static void RecoversNamesWithoutListfile()
        {
            var ms = new MemoryStream();
            var files = Sample();
            MpqWriter.Write(ms, files);
            ms.Position = 0;
            using var r = new MpqReader(ms);
            foreach (var e in r.Files) e.Name = null;                       // pretend there was no (listfile)
            A.True(r.Files.All(e => r.DisplayName(e).StartsWith("unknown #")), "listed as unknown");
            var res = NameRecovery.Recover(r, new[] { "DBFilesClient\\Spell.dbc", "Interface\\Icons\\Test", "empty.txt", "nothing\\here.blp" }, 0);
            A.True(r.Find("DBFilesClient\\Spell.dbc").Name != null, "exact candidate");
            A.True(r.Find("Interface\\Icons\\Test.blp").Name != null, "extension variant (.blp added)");
            A.Eq(5, res.Total); A.Eq(3, res.Recovered);
            var unknown = r.Files.First(e => e.Name == null);
            A.True(r.Read(r.FindDisplay(r.DisplayName(unknown))).Length == unknown.FileSize, "unknown entry still extractable");
        }

        [Test] static void RecoveryFindsPathsInsideFiles()
        {
            var model = Encoding.ASCII.GetBytes("MD20\0\0\0\0Creature\\Wolf\\WolfSkin.blp\0\0World\\Doodad\\Lamp.mdx\0");
            var ms = new MemoryStream();
            MpqWriter.Write(ms, new List<(string, byte[])> { ("Creature\\Wolf\\Wolf.m2", model), ("Creature\\Wolf\\WolfSkin.blp", Encoding.ASCII.GetBytes("BLP2xxxx")), ("World\\Doodad\\Lamp.m2", new byte[10]), ("World\\Doodad\\Lamp00.skin", Encoding.ASCII.GetBytes("SKINyyyy")) });
            ms.Position = 0;
            using var r = new MpqReader(ms);
            foreach (var e in r.Files) e.Name = null;
            var res = NameRecovery.Recover(r, new[] { "Creature\\Wolf\\Wolf.mdx" });
            A.Eq(4, res.Recovered, "model → texture and doodad (.mdx → .m2 → 00.skin)");
            A.Eq(".blp", NameRecovery.GuessExtension(Encoding.ASCII.GetBytes("BLP2....")));
            A.Eq(".m2", NameRecovery.GuessExtension(model));
        }

        [Test] static void ReadsFilesWithSmallerSectorsThanHeader()
        {
            var ms = new MemoryStream();
            var files = Sample();
            MpqWriter.Write(ms, files);                                      // written with 4096-byte sectors
            var bytes = ms.ToArray();
            bytes[14] = 5;                                                   // header now claims 16384-byte sectors (like backup-enUS.MPQ)
            using var r = new MpqReader(new MemoryStream(bytes));
            A.Eq(16384, r.SectorSize);
            foreach (var (p, d) in files) A.True(r.Read(p.Replace('/', '\\')).SequenceEqual(d), "still reads " + p);
        }

        [Test] static void ScansRealClientPatchSet()
        {
            var data = DevPaths.ClientData;
            if (!Directory.Exists(data)) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var set = PatchSet.Scan(data);
            Console.WriteLine($"        scanned {set.Count} patches in {sw.ElapsedMilliseconds} ms: " + string.Join(", ", set.Select(p => $"{p.FileName}={p.Files.Count}{(p.Error != null ? " ERR " + p.Error : "")}")));
            A.True(set.Count > 0, "found patches");
            A.True(set.All(p => p.Error == null), "all patch tables readable");
        }

        [Test] static void ReadsRealClientArchives()
        {
            var data = DevPaths.ClientData;
            if (!Directory.Exists(data)) return; // client not on this machine
            foreach (var name in new[] { "patch-Z.MPQ", "patch-3.MPQ" })
            {
                var p = Path.Combine(data, name);
                if (!File.Exists(p)) continue;
                using var r = new MpqReader(p);   // opened read-only, shared
                var named = r.Files.Where(f => f.Name != null).ToList();
                A.True(named.Count > 0, name + " has a readable (listfile)");
                var dbc = named.FirstOrDefault(f => f.Name.EndsWith(".dbc", StringComparison.OrdinalIgnoreCase) && f.FileSize < 5_000_000);
                if (dbc != null)
                {
                    var bytes = r.Read(dbc);
                    A.Eq("WDBC", Encoding.ASCII.GetString(bytes, 0, 4), name + " " + dbc.Name);
                    A.True(new DbcFile(bytes).Records >= 0, "parses as a DBC");
                }
            }
        }
    }
}
