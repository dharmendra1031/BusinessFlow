using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using Microsoft.Reporting.WinForms;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 2 || !System.Text.RegularExpressions.Regex.IsMatch(args[0], "^[a-z-]+$"))
            return 2;
        try
        {
            var directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Reports");
            var serializer = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 };
            var definitions = serializer.Deserialize<Dictionary<string, ReportDefinition>>(
                File.ReadAllText(Path.Combine(directory, "definitions.json")));
            if (!definitions.TryGetValue(args[0], out var definition)) return 2;
            var rows = serializer.Deserialize<List<Dictionary<string, object>>>(Console.In.ReadToEnd());
            var table = new DataTable("Items");
            foreach (var column in definition.Columns)
                table.Columns.Add(column.Key, ColumnType(column.Value));
            foreach (var row in rows)
            {
                var values = definition.Columns.Select(column =>
                    row.TryGetValue(column.Key, out var value) && value != null
                        ? ConvertValue(value, ColumnType(column.Value)) : DBNull.Value).ToArray();
                table.Rows.Add(values);
            }
            using (var report = new LocalReport())
            {
                report.ReportPath = Path.Combine(directory, args[0] + ".rdlc");
                report.DataSources.Add(new ReportDataSource("Items", table));
                report.SetParameters(new ReportParameter("Title", definition.Title));
                File.WriteAllBytes(args[1], report.Render("PDF"));
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static Type ColumnType(string value) => value switch
    {
        "long" => typeof(long), "decimal" => typeof(decimal),
        "date" => typeof(DateTime), _ => typeof(string)
    };

    private static object ConvertValue(object value, Type type)
    {
        if (type == typeof(DateTime))
            return DateTime.Parse(Convert.ToString(value, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);
        return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
    }

    private sealed class ReportDefinition
    {
        public string Title { get; set; }
        public Dictionary<string, string> Columns { get; set; }
    }
}
