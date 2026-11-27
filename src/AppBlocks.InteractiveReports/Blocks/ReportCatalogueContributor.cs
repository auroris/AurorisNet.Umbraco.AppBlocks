using System.Security.Claims;
using AppBlocks.Blocks;
using AppBlocks.InteractiveReports.Reports;

namespace AppBlocks.InteractiveReports;

public sealed class ReportCatalogueContributor(IReportAccessPolicy access, DataSources.PublishedReportCatalog reports) : IAppBlocksCatalogueContributor
{
    public async Task PopulateAsync(IDictionary<string, object> catalogue, ClaimsPrincipal user)
    {
        await reports.LoadAsync();
        catalogue["scheduleReports"] = reports.ReportNodes.Values.Where(r => r.DataSource is not null)
            .Select(r => new { selection = AppBlockValues.Udi(r.Key), label = r.Definition.Title ?? r.Alias, alias = r.Alias }).ToArray();
        catalogue["canPreview"] = await access.IsAdministratorAsync(user);
    }
}
