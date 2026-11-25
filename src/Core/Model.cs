using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace AndersCraftStudio.Core
{
    public sealed class KeyPart
    {
        public string Column { get; set; }
        /// <summary>SQL literal, e.g. 1234 or 'abc'.</summary>
        public string Value { get; set; }
        public string Display { get; set; }
        public KeyPart() { }
        public KeyPart(string column, string literal, string display = null) { Column = column; Value = literal; Display = display ?? literal; }
    }

    public sealed class FieldChange
    {
        public string Name { get; set; }
        /// <summary>SQL literal before the change (null when there was no row).</summary>
        public string Before { get; set; }
        /// <summary>SQL literal after the change (null when the row is removed). For raw statements this can be an expression.</summary>
        public string After { get; set; }
        public string BeforeDisplay { get; set; }
        public string AfterDisplay { get; set; }
    }

    public enum ChangeKind { Update, Insert, Delete, Raw }

    public sealed class Change
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 12);
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ChangeKind Kind { get; set; }
        /// <summary>Which database family the change belongs to. M1 only stages "world" changes (applied to Sandbox or Live).</summary>
        public string Schema { get; set; } = "world";
        public string Table { get; set; }
        public string Category { get; set; }
        public string Summary { get; set; }
        public List<KeyPart> Key { get; set; } = new List<KeyPart>();
        public List<FieldChange> Fields { get; set; } = new List<FieldChange>();
        public string ForwardSql { get; set; }
        public string UndoSql { get; set; }
        public bool UndoAvailable { get; set; } = true;
        public string UndoNote { get; set; }
        /// <summary>Structure change (DDL). Kept for the record; Studio refuses to apply it.</summary>
        public bool Structure { get; set; }
        /// <summary>Included in the next Apply (the checkbox on the Changes screen).</summary>
        public bool Selected { get; set; } = true;
        /// <summary>For raw statements: the parsed shape, used to re-capture undo data at Apply time.</summary>
        public RawPlan Raw { get; set; }
        /// <summary>For client-data changes (Schema "dbc"): the DBC row patch. These never go to the database.</summary>
        public DbcPatch Dbc { get; set; }

        [JsonIgnore] public string KeyText => Key == null || Key.Count == 0 ? "" : string.Join(", ", Key.Select(k => k.Column + "=" + (k.Display ?? k.Value)));
        public bool SameRow(string table, IList<KeyPart> key) =>
            string.Equals(Table, table, StringComparison.OrdinalIgnoreCase) && Key != null && key != null && Key.Count == key.Count &&
            Key.Zip(key, (a, b) => string.Equals(a.Column, b.Column, StringComparison.OrdinalIgnoreCase) && a.Value == b.Value).All(x => x);
    }

    public enum ChangesetState { Pending, Applied, RolledBack }

    public sealed class ApplyRecord
    {
        public DateTime Utc { get; set; }
        public string Target { get; set; }        // "Sandbox" or "Live"
        public string Database { get; set; }
        public string BackupDir { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
        public string Kind { get; set; } = "Apply"; // "Apply", "Rollback", or "DBC" (client data sent out and moved to History)
    }

    public sealed class Changeset
    {
        public string Id { get; set; } = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        public string Name { get; set; }
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ChangesetState State { get; set; } = ChangesetState.Pending;
        public string AppliedTarget { get; set; }
        public string AppliedDatabase { get; set; }
        public List<Change> Changes { get; set; } = new List<Change>();
        public List<ApplyRecord> Log { get; set; } = new List<ApplyRecord>();
        [JsonIgnore] public string FilePath { get; set; }
    }

    /// <summary>A grid of values returned by a read query.</summary>
    public sealed class ResultTable
    {
        public List<string> Columns { get; } = new List<string>();
        public List<object[]> Rows { get; } = new List<object[]>();
        public bool Truncated { get; set; }
        public int ColumnIndex(string name) => Columns.FindIndex(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Read access used to capture "before" data. Implementations must never write.</summary>
    public interface ISqlReader
    {
        ResultTable Query(string sql, int maxRows);
        IList<string> PrimaryKey(string table);
    }

    /// <summary>Write access, only ever used by the Apply / Roll back engine.</summary>
    public interface ISqlExecutor : ISqlReader
    {
        void Begin();
        void Commit();
        void Rollback();
        int Execute(string sql);
    }
}
