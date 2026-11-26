using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AndersCraftStudio.Core
{
    /// <summary>
    /// Why Windows refused to write a file, in plain words: which programs have it open (Restart Manager), whether it's
    /// read-only, and whether Windows Security's "Controlled folder access" is on (it blocks writes even for administrators).
    /// </summary>
    public static class FileLocks
    {
        [StructLayout(LayoutKind.Sequential)] struct RM_UNIQUE_PROCESS { public int dwProcessId; public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strAppName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string strServiceShortName;
            public int ApplicationType; public uint AppStatus; public uint TSSessionId; [MarshalAs(UnmanagedType.Bool)] public bool bRestartable;
        }
        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);
        [DllImport("rstrtmgr.dll")] static extern int RmEndSession(uint pSessionHandle);
        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] static extern int RmRegisterResources(uint pSessionHandle, uint nFiles, string[] rgsFilenames, uint nApplications, IntPtr rgApplications, uint nServices, string[] rgsServiceNames);
        [DllImport("rstrtmgr.dll")] static extern int RmGetList(uint dwSessionHandle, out uint pnProcInfoNeeded, ref uint pnProcInfo, [In, Out] RM_PROCESS_INFO[] rgAffectedApps, ref uint lpdwRebootReasons);

        /// <summary>Programs that have <paramref name="path"/> open right now ("Name (pid N)"). Empty when none or unknown.</summary>
        public static List<string> Holders(string path)
        {
            var res = new List<string>();
            if (!OperatingSystem.IsWindows() || !File.Exists(path)) return res;
            try
            {
                if (RmStartSession(out var h, 0, Guid.NewGuid().ToString()) != 0) return res;
                try
                {
                    if (RmRegisterResources(h, 1, new[] { path }, 0, IntPtr.Zero, 0, null) != 0) return res;
                    uint needed = 0, count = 0, reasons = 0;
                    int r = RmGetList(h, out needed, ref count, null, ref reasons);
                    if (r == 234 && needed > 0)   // ERROR_MORE_DATA
                    {
                        var info = new RM_PROCESS_INFO[needed]; count = needed;
                        if (RmGetList(h, out needed, ref count, info, ref reasons) == 0)
                            res.AddRange(info.Take((int)count).Select(i => $"{i.strAppName} (pid {i.Process.dwProcessId})"));
                    }
                }
                finally { RmEndSession(h); }
            }
            catch { }
            return res;
        }

        /// <summary>True when Windows Security's Controlled folder access (ransomware protection) is switched on.</summary>
        public static bool ControlledFolderAccessOn()
        {
            if (!OperatingSystem.IsWindows()) return false;
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows Defender\Windows Defender Exploit Guard\Controlled Folder Access");
                return k?.GetValue("EnableControlledFolderAccess") is int v && v == 1;
            }
            catch { return false; }
        }

        /// <summary>A plain explanation of why writing <paramref name="path"/> failed, with what to do.</summary>
        public static string Explain(string path, Exception ex)
        {
            var parts = new List<string>();
            var who = Holders(path);
            if (who.Count > 0) parts.Add($"{Path.GetFileName(path)} is open in: {string.Join(", ", who)}. Close those (another copy of Studio counts) and try again.");
            try { if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0) parts.Add($"{Path.GetFileName(path)} is marked read-only (right-click › Properties, untick Read-only)."); } catch { }
            if (ControlledFolderAccessOn()) parts.Add("Windows Security's Controlled folder access is on: it blocks programs it doesn't know from writing into protected folders, even as administrator. Allow AndersCraft Studio under Windows Security › Virus & threat protection › Ransomware protection › Allow an app.");
            if (parts.Count == 0) parts.Add(Friendly.Text(ex));
            return string.Join(" ", parts);
        }
    }
}
