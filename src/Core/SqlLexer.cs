using System;
using System.Collections.Generic;
using System.Text;

namespace AndersCraftStudio.Core
{
    public enum TokKind { Word, QuotedIdent, String, Number, Symbol, Semicolon, Variable }

    public sealed class Tok
    {
        public TokKind Kind;
        public string Text;    // raw text as written
        public string Value;   // unescaped value for strings / identifiers; upper-case for words
        public int Start, End; // [Start, End) offsets in the source
        public int Depth;      // parenthesis depth at this token
        public bool Is(string word) => Kind == TokKind.Word && Value == word;
        public bool IsSym(string s) => Kind == TokKind.Symbol && Text == s;
        public override string ToString() => Text;
    }

    /// <summary>
    /// A small MySQL tokenizer. It understands the parts that matter for deciding whether text is safe to run:
    /// quoted strings, `identifiers`, -- / # / block comments, and MySQL "executable comments" (/*! ... */),
    /// whose contents the server DOES run and therefore are tokenized as normal SQL here.
    /// </summary>
    public static class SqlLexer
    {
        public static List<Tok> Lex(string sql)
        {
            var toks = new List<Tok>();
            if (string.IsNullOrEmpty(sql)) return toks;
            int i = 0, n = sql.Length, depth = 0, execComment = 0;
            while (i < n)
            {
                char c = sql[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }

                // comments
                if (c == '#') { i = SkipLine(sql, i); continue; }
                if (c == '-' && i + 1 < n && sql[i + 1] == '-' && (i + 2 >= n || char.IsWhiteSpace(sql[i + 2]) || char.IsControl(sql[i + 2]))) { i = SkipLine(sql, i); continue; }
                if (c == '/' && i + 1 < n && sql[i + 1] == '*')
                {
                    if (i + 2 < n && sql[i + 2] == '!')
                    {
                        // executable comment: skip the marker and optional version number, keep lexing inside.
                        // A marker token is emitted so the classifier can refuse such statements outright.
                        toks.Add(new Tok { Kind = TokKind.Symbol, Text = "/*!", Value = "/*!", Start = i, End = i + 3, Depth = depth });
                        i += 3;
                        while (i < n && char.IsDigit(sql[i])) i++;
                        execComment++;
                        continue;
                    }
                    int close = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = close < 0 ? n : close + 2;
                    continue;
                }
                if (c == '*' && execComment > 0 && i + 1 < n && sql[i + 1] == '/') { execComment--; i += 2; continue; }

                int start = i;
                if (c == '\'' || c == '"')
                {
                    var sb = new StringBuilder();
                    i = ReadQuoted(sql, i, c, sb, true);
                    toks.Add(new Tok { Kind = TokKind.String, Text = sql.Substring(start, i - start), Value = sb.ToString(), Start = start, End = i, Depth = depth });
                    continue;
                }
                if (c == '`')
                {
                    var sb = new StringBuilder();
                    i = ReadQuoted(sql, i, '`', sb, false);
                    toks.Add(new Tok { Kind = TokKind.QuotedIdent, Text = sql.Substring(start, i - start), Value = sb.ToString(), Start = start, End = i, Depth = depth });
                    continue;
                }
                if (c == ';') { toks.Add(new Tok { Kind = TokKind.Semicolon, Text = ";", Value = ";", Start = i, End = i + 1, Depth = depth }); i++; continue; }
                if (c == '@')
                {
                    i++;
                    if (i < n && sql[i] == '@') i++;
                    while (i < n && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_' || sql[i] == '.' || sql[i] == '$')) i++;
                    toks.Add(new Tok { Kind = TokKind.Variable, Text = sql.Substring(start, i - start), Value = sql.Substring(start, i - start), Start = start, End = i, Depth = depth });
                    continue;
                }
                if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(sql[i + 1])))
                {
                    // hex 0x.., numbers incl. exponent
                    if (c == '0' && i + 1 < n && (sql[i + 1] == 'x' || sql[i + 1] == 'X' || sql[i + 1] == 'b' || sql[i + 1] == 'B')) { i += 2; while (i < n && char.IsLetterOrDigit(sql[i])) i++; }
                    else
                    {
                        while (i < n && (char.IsDigit(sql[i]) || sql[i] == '.')) i++;
                        if (i < n && (sql[i] == 'e' || sql[i] == 'E') && i + 1 < n && (char.IsDigit(sql[i + 1]) || ((sql[i + 1] == '+' || sql[i + 1] == '-') && i + 2 < n && char.IsDigit(sql[i + 2]))))
                        { i += 2; while (i < n && char.IsDigit(sql[i])) i++; }
                    }
                    // identifiers may start with digits in MySQL (e.g. 1abc) – treat a trailing letter run as a word
                    if (i < n && (char.IsLetter(sql[i]) || sql[i] == '_'))
                    {
                        while (i < n && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_' || sql[i] == '$')) i++;
                        var w = sql.Substring(start, i - start);
                        toks.Add(new Tok { Kind = TokKind.Word, Text = w, Value = w.ToUpperInvariant(), Start = start, End = i, Depth = depth });
                        continue;
                    }
                    toks.Add(new Tok { Kind = TokKind.Number, Text = sql.Substring(start, i - start), Value = sql.Substring(start, i - start), Start = start, End = i, Depth = depth });
                    continue;
                }
                if (char.IsLetter(c) || c == '_' || c == '$' || c > 127)
                {
                    // X'..' / B'..' / N'..' / _charset'..' literals
                    if ((c == 'x' || c == 'X' || c == 'b' || c == 'B' || c == 'n' || c == 'N') && i + 1 < n && sql[i + 1] == '\'')
                    {
                        var sb = new StringBuilder();
                        i = ReadQuoted(sql, i + 1, '\'', sb, true);
                        toks.Add(new Tok { Kind = TokKind.String, Text = sql.Substring(start, i - start), Value = sb.ToString(), Start = start, End = i, Depth = depth });
                        continue;
                    }
                    while (i < n && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_' || sql[i] == '$' || sql[i] > 127)) i++;
                    var w = sql.Substring(start, i - start);
                    toks.Add(new Tok { Kind = TokKind.Word, Text = w, Value = w.ToUpperInvariant(), Start = start, End = i, Depth = depth });
                    continue;
                }
                // symbols (multi-char operators)
                string sym = c.ToString();
                if (i + 2 < n && sql.Substring(i, 3) == "<=>") sym = "<=>";
                else if (i + 1 < n)
                {
                    var two = sql.Substring(i, 2);
                    if (two == "<=" || two == ">=" || two == "<>" || two == "!=" || two == ":=" || two == "||" || two == "&&" || two == "<<" || two == ">>" || two == "->") sym = two;
                }
                if (sym == "(") { toks.Add(new Tok { Kind = TokKind.Symbol, Text = sym, Value = sym, Start = i, End = i + 1, Depth = depth }); depth++; i++; continue; }
                if (sym == ")") { depth = Math.Max(0, depth - 1); toks.Add(new Tok { Kind = TokKind.Symbol, Text = sym, Value = sym, Start = i, End = i + 1, Depth = depth }); i++; continue; }
                toks.Add(new Tok { Kind = TokKind.Symbol, Text = sym, Value = sym, Start = i, End = i + sym.Length, Depth = depth });
                i += sym.Length;
            }
            return toks;
        }

        static int SkipLine(string s, int i)
        {
            int nl = s.IndexOf('\n', i);
            return nl < 0 ? s.Length : nl + 1;
        }

        static int ReadQuoted(string s, int i, char q, StringBuilder sb, bool backslashEscapes)
        {
            int n = s.Length;
            i++; // opening quote
            while (i < n)
            {
                char c = s[i];
                if (backslashEscapes && c == '\\' && i + 1 < n)
                {
                    char e = s[i + 1];
                    switch (e)
                    {
                        case '0': sb.Append('\0'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'Z': sb.Append('\x1a'); break;
                        default: sb.Append(e); break;
                    }
                    i += 2; continue;
                }
                if (c == q)
                {
                    if (i + 1 < n && s[i + 1] == q) { sb.Append(q); i += 2; continue; }
                    return i + 1;
                }
                sb.Append(c); i++;
            }
            return n; // unterminated: swallow to end
        }

        /// <summary>Splits text into statements (by top-level ';'), returning each statement's tokens and raw text.</summary>
        public static List<(string text, List<Tok> toks)> Split(string sql)
        {
            var all = Lex(sql);
            var res = new List<(string, List<Tok>)>();
            var cur = new List<Tok>();
            foreach (var t in all)
            {
                if (t.Kind == TokKind.Semicolon)
                {
                    if (cur.Count > 0) res.Add((sql.Substring(cur[0].Start, cur[cur.Count - 1].End - cur[0].Start), cur));
                    cur = new List<Tok>();
                }
                else cur.Add(t);
            }
            if (cur.Count > 0) res.Add((sql.Substring(cur[0].Start, cur[cur.Count - 1].End - cur[0].Start), cur));
            return res;
        }
    }
}
