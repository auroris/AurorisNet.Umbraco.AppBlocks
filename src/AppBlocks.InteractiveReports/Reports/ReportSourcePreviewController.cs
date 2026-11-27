using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;
using AppBlocks.Authentication;
using AppBlocks.InteractiveReports.DataSources;

namespace AppBlocks.InteractiveReports.Reports;

public sealed record ReportSourcePreviewRequest(string DataSource);

/// <summary>
/// Turns a block's unpublished source, which may be a table or SQL against a registered connection, into a short-lived report to
/// configure its starting view. Publishing that source needs an AppBlocks administrator, so previewing it does too; a report
/// administrator who is not one may read reports but not choose what they run.
/// </summary>
[ApiVersion("1.0")]
[VersionedApiBackOfficeRoute("appblocks")]
[ApiExplorerSettings(GroupName = "AppBlocks")]
public sealed class ReportSourcePreviewController(PublishedReportCatalog definitions, ReportSourcePreviews previews) : ManagementApiControllerBase
{
    [HttpPost("report-preview")]
    [Authorize(Policy = AppBlocksAuthorization.AdministratorPolicy)]
    public async Task<IActionResult> Create(ReportSourcePreviewRequest request)
    {
        Response.Headers.CacheControl = "no-store";
        var errors = new List<string>();
        var report = await definitions.ReadBlockReportAsync(Guid.NewGuid(), property => property == "dataSource" ? request.DataSource : null, errors);
        return report is null ? Problem(string.Join(" ", errors), statusCode: 422) : Ok(new { name = previews.Add(report, User) });
    }
}
