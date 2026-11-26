using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;
using AndersCraftStudio.Core;

namespace AndersCraftStudio
{
    /// <summary>
    /// CSV → database table: map the CSV's columns, preview what would change, then stage it (INSERTs for new rows, UPDATEs
    /// for changed cells, each with undo). The existing rows are read with the read-only account; nothing is written.
    /// </summary>
    public sealed class CsvImportWindow : Window
    {
        readonly MainWindow w;
        Studio S => w.S;
        readonly string dbName, table, file;
        readonly IList<string> pk;
        readonly List<CsvTableImport.Column> columns;
        readonly List<string> header;
        readonly List<List<string>> rows;
        readonly List<ComboBox> mapBoxes = new List<ComboBox>();
        readonly ComboBox mode = new ComboBox { Width = 300 };
        readonly ContentControl result = new ContentControl { Focusable = false };
        Button stageBtn;
        public int Staged { get; private set; }

        public CsvImportWindow(MainWindow owner, string dbName, string table, IList<string> pk, DataTable schema, string file)
        {
            w = owner; this.dbName = dbName; this.table = table; this.pk = pk; this.file = file;
            Owner = owner; Title = "Import CSV"; Width = 860; Height = 720; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResizeWithGrip; ShowInTaskbar = false; Background = U.R("Panel"); FontFamily = U.Font; Foreground = U.R("Text");
            WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 40, GlassFrameThickness = new Thickness(0), ResizeBorderThickness = new Thickness(6), CornerRadius = new CornerRadius(0) });
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };

            columns = schema.Columns.Cast<DataColumn>().Select(c => new CsvTableImport.Column(c.ColumnName, c.DataType, c.ExtendedProperties["nullable"] is bool b ? b : true)).ToList();
            var all = DbcCsv.Parse(File.ReadAllText(file));
            header = all.Count > 0 ? all[0] : new List<string>();
            rows = all.Skip(1).ToList();

            var dp = new DockPanel { Margin = new Thickness(22, 16, 22, 18) };
            var head = U.Stack(U.Eyebrow("Import CSV"), U.H2($"Into {table}"),
                U.T($"{Path.GetFileName(file)} · {rows.Count:N0} row(s) · {header.Count} column(s). Rows are matched to the table by its key ({(pk.Count == 0 ? "none" : string.Join(", ", pk))}). " +
                    "Empty cells are left as they are (type NULL to set NULL). Nothing is written: the result is staged in your changeset with its undo.", 12.5, "Muted", null, true, new Thickness(0, 0, 0, 12)));
            DockPanel.SetDock(head, Dock.Top); dp.Children.Add(head);

            var btns = U.Row(U.Btn("Preview", Preview, null, "Compare the CSV with the table (read-only) and show what would be staged."),
                             stageBtn = U.Btn("Stage it", StageIt, "BtnPrimary", "Add the changes to your changeset. Review and apply them on Changes."),
                             U.Btn("Cancel  (Esc)", Close, "BtnGhost"));
            stageBtn.IsEnabled = false;
            btns.Margin = new Thickness(0, 12, 0, 0); btns.HorizontalAlignment = HorizontalAlignment.Right;
            DockPanel.SetDock(btns, Dock.Bottom); dp.Children.Add(btns);

            var body = new StackPanel();
            var auto = CsvTableImport.AutoMap(header, columns);
            var mapGrid = new Grid();
            mapGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            mapGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            mapGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            void Cell(UIElement e, int r, int c) { Grid.SetRow(e, r); Grid.SetColumn(e, c); mapGrid.Children.Add(e); }
            mapGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Cell(U.T("CSV column", 12, "Faint", FontWeights.SemiBold, false), 0, 0); Cell(U.T("Goes into table column", 12, "Faint", FontWeights.SemiBold, false), 0, 1); Cell(U.T("First value", 12, "Faint", FontWeights.SemiBold, false), 0, 2);
            for (int i = 0; i < header.Count; i++)
            {
                mapGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var cb = new ComboBox { Width = 230, Margin = new Thickness(0, 3, 0, 3) };
                cb.Items.Add(new ComboBoxItem { Content = "(skip this column)", Tag = null });
                foreach (var c in columns) cb.Items.Add(new ComboBoxItem { Content = c.Name + (pk.Contains(c.Name, StringComparer.OrdinalIgnoreCase) ? "  (key)" : ""), Tag = c.Name });
                cb.SelectedIndex = auto.TryGetValue(i, out var m) ? columns.FindIndex(c => c.Name == m) + 1 : 0;
                cb.SelectionChanged += (s, e) => { stageBtn.IsEnabled = false; result.Content = null; };
                mapBoxes.Add(cb);
                var name = U.T(header[i], 13, "Text", null, false); name.VerticalAlignment = VerticalAlignment.Center; name.FontFamily = U.Mono;
                var sample = U.T(rows.Count > 0 && i < rows[0].Count ? Refs.OneLine(rows[0][i], 50) : "", 12.5, "Muted", null, false); sample.VerticalAlignment = VerticalAlignment.Center; sample.Margin = new Thickness(12, 0, 0, 0);
                Cell(name, i + 1, 0); Cell(cb, i + 1, 1); Cell(sample, i + 1, 2);
            }
            body.Children.Add(U.Card(U.Stack(U.H2("Columns"), U.T($"{auto.Count} of {header.Count} matched by name. Pick a table column for the others, or skip them.", 12.5, "Muted", null, true, new Thickness(0, 0, 0, 8)), mapGrid), new Thickness(16, 12, 16, 12), new Thickness(0, 0, 0, 12)));
            foreach (var (label, m) in new[] { ("Add new rows and update existing ones", CsvTableImport.Mode.AddAndUpdate), ("Only add new rows (leave existing rows alone)", CsvTableImport.Mode.AddOnly), ("Only update existing rows (ignore new keys)", CsvTableImport.Mode.UpdateOnly) })
                mode.Items.Add(new ComboBoxItem { Content = label, Tag = m });
            mode.SelectedIndex = 0;
            mode.SelectionChanged += (s, e) => { stageBtn.IsEnabled = false; result.Content = null; };
            var modeField = U.Field("What to do", "Rows are matched by the table's key.", mode, null, 320); modeField.HorizontalAlignment = HorizontalAlignment.Left;
            body.Children.Add(modeField);
            body.Children.Add(result);
            dp.Children.Add(U.Scroll(body));
            Content = U.Frame(new Border { Child = dp });
            if (pk.Count == 0) result.Content = U.Callout($"{table} has no primary key, so CSV rows can't be matched to its rows. Use Stage .sql script for this table instead.", "bad");
        }

        Dictionary<int, string> Map()
        {
            var m = new Dictionary<int, string>();
            for (int i = 0; i < mapBoxes.Count; i++) if (mapBoxes[i].SelectedItem is ComboBoxItem ci && ci.Tag is string col) m[i] = col;
            return m;
        }

        CsvTableImport.Mode Mode => mode.SelectedItem is ComboBoxItem ci && ci.Tag is CsvTableImport.Mode m ? m : CsvTableImport.Mode.AddAndUpdate;

        /// <summary>Reads the table rows the CSV keys point at (read-only account, in chunks).</summary>
        Dictionary<string, IDictionary<string, object>> Existing(Dictionary<int, string> map)
        {
            var res = new Dictionary<string, IDictionary<string, object>>();
            var keys = CsvTableImport.Keys(columns, pk, rows, map);
            using var db = S.OpenRead(dbName);
            int chunk = pk.Count == 1 ? 500 : 150;
            for (int i = 0; i < keys.Count; i += chunk)
            {
                var sql = CsvTableImport.LookupSql(table, pk, keys.Skip(i).Take(chunk).ToList());
                if (sql == null) continue;
                var dt = db.QueryTable(sql, chunk * 2, out _);
                foreach (DataRow r in dt.Rows)
                {
                    var vals = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    foreach (DataColumn c in dt.Columns) vals[c.ColumnName] = r[c];
                    res[CsvTableImport.KeyText(pk.Select(k => SqlGen.Literal(r[k])))] = vals;
                }
            }
            return res;
        }

        CsvTableImport.Result Run(Changeset cs)
        {
            var map = Map();
            Mouse.OverrideCursor = Cursors.Wait;
            try { return CsvTableImport.Stage(cs, table, columns, pk, rows, map, Existing(map), Mode); }
            finally { Mouse.OverrideCursor = null; }
        }

        void Preview()
        {
            CsvTableImport.Result r; var temp = new Changeset();
            try { r = Run(temp); }
            catch (Exception ex) { result.Content = U.Callout("Couldn't read the table: " + Friendly.Text(ex), "bad"); return; }
            var sp = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            if (!r.Ok)
            {
                sp.Children.Add(U.Callout("Nothing can be staged until these are fixed:\n" + string.Join("\n", r.Errors.Take(15)) + (r.Errors.Count > 15 ? $"\n… and {r.Errors.Count - 15} more" : ""), "bad", "Problems"));
                result.Content = sp; stageBtn.IsEnabled = false; return;
            }
            sp.Children.Add(U.Callout(CsvTableImport.Describe(r) + "." + (r.Notes.Count > 0 ? "\n" + string.Join("\n", r.Notes.Take(5)) : ""), temp.Changes.Count > 0 ? "ok" : "info", "Preview"));
            if (temp.Changes.Count > 0)
            {
                var dt = new DataTable();
                dt.Columns.Add("What"); dt.Columns.Add("Key"); dt.Columns.Add("Changes");
                foreach (var c in temp.Changes.Take(200))
                    dt.Rows.Add(c.Kind == ChangeKind.Insert ? "new row" : "update", c.KeyText,
                        string.Join(", ", c.Fields.Where(f => c.Kind != ChangeKind.Insert || !pk.Contains(f.Name, StringComparer.OrdinalIgnoreCase)).Take(8).Select(f => c.Kind == ChangeKind.Insert ? $"{f.Name}={f.AfterDisplay}" : $"{f.Name}: {f.BeforeDisplay} → {f.AfterDisplay}")));
                var g = new DataGrid { IsReadOnly = true, AutoGenerateColumns = true, ItemsSource = dt.DefaultView, MaxHeight = 300, Margin = new Thickness(0, 10, 0, 0) };
                sp.Children.Add(g);
                if (temp.Changes.Count > 200) sp.Children.Add(U.T($"Showing the first 200 of {temp.Changes.Count} row changes.", 12, "Faint"));
            }
            result.Content = sp;
            stageBtn.IsEnabled = temp.Changes.Count > 0;
        }

        void StageIt()
        {
            CsvTableImport.Result r;
            try { r = Run(S.Changes.Pending); }
            catch (Exception ex) { w.Toast("Not staged: " + Friendly.Text(ex), "bad"); return; }
            if (!r.Ok) { Preview(); return; }
            S.SaveChanges();
            Staged = r.Added + r.Updated;
            w.Toast($"CSV staged into {table}: {CsvTableImport.Describe(r)}. Nothing is written until you Apply on Changes.", "ok");
            DialogResult = true;
        }

        // screenshots
        public void ScreensPreview() => Preview();
    }
}
