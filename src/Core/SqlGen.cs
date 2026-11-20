using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AndersCraftStudio.Core
{
    /// <summary>Builds MySQL text. All values pass through <see cref="Literal"/>; all names through <see cref="Ident"/>.</summary>
    public static class SqlGen
    {
        public static string Ident(string name)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Identifier is empty.");
            return "`" + name.Replace("`", "``") + "`";
        }

        public static string QualifiedIdent(string db, string table) => string.IsNullOrEmpty(db) ? Ident(table) : Ident(db) + "." + Ident(table);

        public static string EscapeString(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '\'': sb.Append("\\'"); break;
                    case '\0': sb.Append("\\0"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\x1a': sb.Append("\\Z"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        /// <summary>Turns a value read from MySqlConnector (or a plain .NET value) into a MySQL literal.</summary>
        public static string Literal(object v)
        {
            switch (v)
            {
                case null: return "NULL";
                case DBNull _: return "NULL";
                case string s: return "'" + EscapeString(s) + "'";
                case bool b: return b ? "1" : "0";
                case sbyte _: case byte _: case short _: case ushort _: case int _: case uint _: case long _: case ulong _:
                    return Convert.ToString(v, CultureInfo.InvariantCulture);
                case float f:
                    if (float.IsNaN(f) || float.IsInfinity(f)) throw new ArgumentException("MySQL can't store NaN or infinity.");
                    return f.ToString("R", CultureInfo.InvariantCulture);
                case double d:
                    if (double.IsNaN(d) || double.IsInfinity(d)) throw new ArgumentException("MySQL can't store NaN or infinity.");
                    return d.ToString("R", CultureInfo.InvariantCulture);
                case decimal m: return m.ToString(CultureInfo.InvariantCulture);
                case byte[] bytes:
                    if (bytes.Length == 0) return "''";
                    return "X'" + Convert.ToHexString(bytes) + "'";
                case DateTime dt:
                    return "'" + dt.ToString(dt.Ticks % TimeSpan.TicksPerSecond == 0 ? "yyyy-MM-dd HH:mm:ss" : "yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture) + "'";
                case DateTimeOffset dto: return Literal(dto.UtcDateTime);
                case TimeSpan ts:
                    var sign = ts < TimeSpan.Zero ? "-" : "";
                    ts = ts.Duration();
                    return "'" + sign + ((int)ts.TotalHours).ToString(CultureInfo.InvariantCulture) + ts.ToString(@"\:mm\:ss", CultureInfo.InvariantCulture) + "'";
                case Guid g: return "'" + g.ToString() + "'";
                case MySqlConnector.MySqlDateTime mdt:
                    if (mdt.IsValidDateTime) return Literal(mdt.GetDateTime());
                    return "'" + mdt.Year.ToString("0000") + "-" + mdt.Month.ToString("00") + "-" + mdt.Day.ToString("00") + " " +
                           mdt.Hour.ToString("00") + ":" + mdt.Minute.ToString("00") + ":" + mdt.Second.ToString("00") + "'";
                default:
                    return "'" + EscapeString(Convert.ToString(v, CultureInfo.InvariantCulture)) + "'";
            }
        }

        /// <summary>Human-friendly display of a value (no quotes; NULL shown as NULL).</summary>
        public static string Display(object v)
        {
            switch (v)
            {
                case null: case DBNull _: return "NULL";
                case byte[] b: return b.Length <= 32 ? "0x" + Convert.ToHexString(b) : $"({b.Length} bytes)";
                case float f: return f.ToString("R", CultureInfo.InvariantCulture);
                case double d: return d.ToString("R", CultureInfo.InvariantCulture);
                case IFormattable fm: return fm.ToString(null, CultureInfo.InvariantCulture);
                default: return v.ToString();
            }
        }

        static readonly HashSet<Type> IntTypes = new HashSet<Type> { typeof(sbyte), typeof(byte), typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong) };

        /// <summary>
        /// Converts what a person typed into a grid cell into a literal for a column of the given .NET type.
        /// Typing NULL (upper case) means SQL NULL when the column allows it.
        /// </summary>
        public static bool TryTypedLiteral(string input, Type columnType, bool allowNull, out string literal, out string error)
        {
            literal = null; error = null;
            input ??= "";
            if (input == "NULL")
            {
                if (!allowNull) { error = "This column can't be NULL."; return false; }
                literal = "NULL"; return true;
            }
            if (columnType == null || columnType == typeof(string) || columnType == typeof(object)) { literal = Literal(input); return true; }
            var t = input.Trim();
            if (IntTypes.Contains(columnType))
            {
                if (columnType == typeof(ulong) || columnType == typeof(uint) || columnType == typeof(ushort) || columnType == typeof(byte))
                {
                    if (!ulong.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u)) { error = "Enter a whole number of 0 or more."; return false; }
                    if (!FitsUnsigned(columnType, u)) { error = "That number is too large for this column."; return false; }
                    literal = u.ToString(CultureInfo.InvariantCulture); return true;
                }
                if (!long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) { error = "Enter a whole number."; return false; }
                if (!FitsSigned(columnType, l)) { error = "That number is out of range for this column."; return false; }
                literal = l.ToString(CultureInfo.InvariantCulture); return true;
            }
            if (columnType == typeof(float) || columnType == typeof(double))
            {
                if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) || double.IsNaN(d) || double.IsInfinity(d)) { error = "Enter a number (use . as the decimal point)."; return false; }
                literal = columnType == typeof(float) ? ((float)d).ToString("R", CultureInfo.InvariantCulture) : d.ToString("R", CultureInfo.InvariantCulture); return true;
            }
            if (columnType == typeof(decimal))
            {
                if (!decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var m)) { error = "Enter a number (use . as the decimal point)."; return false; }
                literal = m.ToString(CultureInfo.InvariantCulture); return true;
            }
            if (columnType == typeof(bool))
            {
                if (t == "1" || t.Equals("true", StringComparison.OrdinalIgnoreCase)) { literal = "1"; return true; }
                if (t == "0" || t.Equals("false", StringComparison.OrdinalIgnoreCase)) { literal = "0"; return true; }
                error = "Enter 0 or 1."; return false;
            }
            if (columnType == typeof(byte[]))
            {
                var h = t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? t.Substring(2) : t;
                if (h.Length % 2 != 0 || !h.All(Uri.IsHexDigit)) { error = "Enter binary data as hex, e.g. 0x0A1B."; return false; }
                literal = h.Length == 0 ? "''" : "X'" + h.ToUpperInvariant() + "'"; return true;
            }
            literal = Literal(input); return true; // dates, times: let MySQL parse the text
        }

        static bool FitsUnsigned(Type t, ulong v) =>
            t == typeof(byte) ? v <= byte.MaxValue : t == typeof(ushort) ? v <= ushort.MaxValue : t == typeof(uint) ? v <= uint.MaxValue : true;
        static bool FitsSigned(Type t, long v) =>
            t == typeof(sbyte) ? v >= sbyte.MinValue && v <= sbyte.MaxValue : t == typeof(short) ? v >= short.MinValue && v <= short.MaxValue :
            t == typeof(int) ? v >= int.MinValue && v <= int.MaxValue : true;

        // ------------------------------------------------------------------ statements (values are literals)

        public static string WhereKey(IList<KeyPart> key)
        {
            if (key == null || key.Count == 0) throw new ArgumentException("A primary key is required.");
            return string.Join(" AND ", key.Select(k => k.Value == "NULL" ? Ident(k.Column) + " IS NULL" : Ident(k.Column) + " = " + k.Value));
        }

        public static string UpdateRow(string table, IList<KeyPart> key, IEnumerable<(string column, string literal)> set)
        {
            var s = set.ToList();
            if (s.Count == 0) throw new ArgumentException("Nothing to update.");
            return "UPDATE " + Ident(table) + " SET " + string.Join(", ", s.Select(p => Ident(p.column) + " = " + p.literal)) +
                   " WHERE " + WhereKey(key) + " LIMIT 1;";
        }

        public static string InsertRow(string table, IList<string> columns, IList<string> literals)
        {
            if (columns.Count == 0 || columns.Count != literals.Count) throw new ArgumentException("Columns and values don't match.");
            return "INSERT INTO " + Ident(table) + " (" + string.Join(", ", columns.Select(Ident)) + ") VALUES (" + string.Join(", ", literals) + ");";
        }

        public static string DeleteRow(string table, IList<KeyPart> key) => "DELETE FROM " + Ident(table) + " WHERE " + WhereKey(key) + " LIMIT 1;";

        public static string SelectRow(string table, IList<KeyPart> key, IEnumerable<string> columns, bool forUpdate)
        {
            var cols = columns?.ToList();
            var c = cols == null || cols.Count == 0 ? "*" : string.Join(", ", cols.Select(Ident));
            return "SELECT " + c + " FROM " + Ident(table) + " WHERE " + WhereKey(key) + " LIMIT 2" + (forUpdate ? " FOR UPDATE" : "") + ";";
        }
    }
}
