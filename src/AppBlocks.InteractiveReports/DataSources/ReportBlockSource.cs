using System.Text.Json;
using AppBlocks.DataSources;

namespace AppBlocks.InteractiveReports.DataSources;

/// <summary>A report's own row source: a table/view, an inline SQL query, or a published SQL data source alias.</summary>
internal static class ReportBlockSource
{
    public static bool IsPublishedDataSource(string? selection)
    {
        if (string.IsNullOrWhiteSpace(selection)) return false;
        try
        {
            using var document = JsonDocument.Parse(selection);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("kind", out var kind)
                && kind.ValueKind == JsonValueKind.String
                && string.Equals(kind.GetString()?.Trim(), "dataSource", StringComparison.Ordinal);
        }
        catch (JsonException) { return false; }
    }

    public static DataSourceDefinition? Read(Guid key, string selection, Func<string, bool> isConnection, ICollection<string> errors,
        Func<string, DataSourceDefinition?>? resolveDataSource = null)
    {
        var start = errors.Count;
        try
        {
            using var document = JsonDocument.Parse(selection);
            var root = document.RootElement;
            string Text(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()!.Trim() : "";
            var kind = Text("kind");
            var connection = Text("connection");
            var source = Text("source");
            if (root.EnumerateObject().Any(p => p.Name is not ("kind" or "connection" or "source"))
                || root.EnumerateObject().GroupBy(p => p.Name).Any(g => g.Count() > 1))
                errors.Add("The report source contains unsupported or repeated settings. Select its table, SQL query or data source again.");
            if (kind == "dataSource")
            {
                if (!string.IsNullOrEmpty(connection))
                    errors.Add("A published data source provides its own connection.");
                if (source.Length == 0)
                    errors.Add("Select a published SQL data source.");
                else if (resolveDataSource?.Invoke(source) is { } resolved)
                {
                    if (!isConnection(resolved.Connection))
                        errors.Add("Select a data source configured in Settings → AppBlocks → Connections.");
                    return errors.Count == start ? resolved : null;
                }
                else
                    errors.Add($"Published SQL data source '{source}' was not found.");
                return null;
            }
            if (!isConnection(connection))
                errors.Add("Select a data source configured in Settings → AppBlocks → Connections.");
            if (source.Length is 0 or > DataSourceDefinition.MaxSourceLength)
                errors.Add($"Enter a table or SQL query of at most {DataSourceDefinition.MaxSourceLength} characters.");
            else if (kind == "table")
            {
                if (TableName.Parse(source) is null) errors.Add("Select a table or view from the data source.");
            }
            else if (kind == "sql")
            {
                DataSourceDefinition.ValidateQuery(source, "The report SQL query", errors);
            }
            else errors.Add("Choose Table, SQL query or Published data source for the report source.");
            return errors.Count == start ? new DataSourceDefinition {
                Key = key, Name = "Report " + key.ToString("N"), Alias = "appblocks-block-" + key.ToString("N"),
                // Block-owned sources explicitly use Public to follow the page's access.
                Connection = connection, Source = source, Access = DataSourceAccess.Public,
            } : null;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            errors.Add("The report source is invalid. Select its table or SQL query again.");
            return null;
        }
    }
}
