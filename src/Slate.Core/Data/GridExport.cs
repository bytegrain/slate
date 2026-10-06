namespace Slate.Data;

/// <summary>CSV (RFC 4180) and TSV (clipboard) export using each column's display text.</summary>
public static class GridExport
{
    /// <summary>
    /// CSV with CRLF line breaks and no trailing newline. Fields are quoted when they contain a comma, quote, CR/LF or
    /// leading/trailing whitespace; quotes are doubled.
    /// </summary>
    public static string ToCsv<T>(IReadOnlyList<GridColumn<T>> columns, IEnumerable<T> rows, bool includeHeader = true,
        Func<T, GridColumn<T>, object?>? valueOf = null)
    {
        var lines = new List<string>();
        if (includeHeader) lines.Add(string.Join(',', columns.Select(c => Csv(c.DisplayTitle))));
        foreach (var row in rows)
            lines.Add(string.Join(',', columns.Select(c => Csv(c.DisplayText(valueOf is null ? c.GetValue(row) : valueOf(row, c))))));
        return string.Join("\r\n", lines);
    }

    /// <summary>Tab-separated values with LF line breaks (tabs and newlines inside cells become spaces).</summary>
    public static string ToTsv<T>(IReadOnlyList<GridColumn<T>> columns, IEnumerable<T> rows, bool includeHeader = false,
        Func<T, GridColumn<T>, object?>? valueOf = null)
    {
        var lines = new List<string>();
        if (includeHeader) lines.Add(string.Join('\t', columns.Select(c => Tsv(c.DisplayTitle))));
        foreach (var row in rows)
            lines.Add(string.Join('\t', columns.Select(c => Tsv(c.DisplayText(valueOf is null ? c.GetValue(row) : valueOf(row, c))))));
        return string.Join('\n', lines);
    }

    public static string Csv(string field)
    {
        var needs = field.IndexOfAny([',', '"', '\r', '\n']) >= 0 || (field.Length > 0 && (char.IsWhiteSpace(field[0]) || char.IsWhiteSpace(field[^1])));
        return needs ? "\"" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : field;
    }

    public static string Tsv(string field) => field.Replace("\r\n", " ").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
