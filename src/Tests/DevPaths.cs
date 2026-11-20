using System;
using System.IO;
using System.Text.Json;

namespace AndersCraftStudio.Tests
{
    /// <summary>
    /// Where a developer's own server and client live, for the tests that read real files (they skip when unset).
    /// Set them in dev.local.json at the repository root (git-ignored), e.g.
    ///   { "serverDbc": "C:\\AzerothCore\\Data\\dbc", "clientData": "C:\\WoW\\Data", "azerothcore": "C:\\src\\azerothcore-wotlk" }
    /// or with the environment variables STUDIO_SERVER_DBC, STUDIO_CLIENT_DATA and STUDIO_AZEROTHCORE.
    /// </summary>
    static class DevPaths
    {
        static JsonElement? file;
        static bool loaded;

        static string Get(string key, string env)
        {
            var e = Environment.GetEnvironmentVariable(env);
            if (!string.IsNullOrWhiteSpace(e)) return e;
            if (!loaded)
            {
                loaded = true;
                for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                {
                    var p = Path.Combine(d.FullName, "dev.local.json");
                    if (File.Exists(p)) { try { file = JsonDocument.Parse(File.ReadAllText(p)).RootElement; } catch { } break; }
                }
            }
            return file != null && file.Value.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";
        }

        /// <summary>The server's Data\dbc folder.</summary>
        public static string ServerDbc => Get("serverDbc", "STUDIO_SERVER_DBC");
        /// <summary>The 3.3.5a client's Data folder.</summary>
        public static string ClientData => Get("clientData", "STUDIO_CLIENT_DATA");
        /// <summary>An azerothcore-wotlk source checkout.</summary>
        public static string AzerothCore => Get("azerothcore", "STUDIO_AZEROTHCORE");
    }
}
