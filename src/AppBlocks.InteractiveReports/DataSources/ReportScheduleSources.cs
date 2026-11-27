using AppBlocks.InteractiveReports.DataSources;
using AppBlocks.Schedules;

namespace AppBlocks.InteractiveReports;

public sealed class ReportScheduleSources(PublishedReportCatalog catalog) : IScheduleSourceProvider
{
    public Task LoadAsync() => catalog.LoadAsync();
    public bool TryResolve(string alias, out ScheduleSource? source)
    {
        source = null;
        var node = AppBlocks.Blocks.AppBlockValues.DocumentKey(alias) is { } key
            ? catalog.FindReportByKey(key) : catalog.ReportNodes.GetValueOrDefault(alias);
        if (node is null) return catalog.IsUnavailableReport(alias);
        if (node.DataSource is { } definition) source = new(node.Alias, definition, FromReport: true);
        return true;
    }
    public IReadOnlyList<string>? Columns(ScheduleSource source) => source.FromReport ? catalog.SourceColumns(source.Definition) : null;
}
