using System.Security.Claims;
using AppBlocks.Authentication;
using AppBlocks.Blocks;

namespace AppBlocks.InteractiveReports;

public sealed class ReportCatalogueContributor(ApplicationAccessPolicy access, DataSources.PublishedReportCatalog reports) : IAppBlocksCatalogueContributor
{
    public async Task PopulateAsync(IDictionary<string, object> catalogue, ClaimsPrincipal user)
    {
        await reports.LoadAsync();
        catalogue["scheduleReports"] = reports.ReportNodes.Values.Where(r => r.DataSource is not null)
            .Select(r => new { selection = AppBlockValues.Udi(r.Key), label = r.Definition.Title ?? r.Alias, alias = r.Alias }).ToArray();
        // Previewing a block's source needs an AppBlocks administrator, as publishing it does; a report administrator is not enough.
        catalogue["canPreview"] = await access.IsBackOfficeAdministratorAsync(user);
    }
}
