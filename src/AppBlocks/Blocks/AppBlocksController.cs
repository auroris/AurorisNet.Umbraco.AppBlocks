using Asp.Versioning;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;
using AppBlocks.DataSources;
using AppBlocks.Forms;

namespace AppBlocks.Blocks;

/// <summary>
/// The catalogue the back-office pickers read: lists, form resources, data source backing classes and optional companion
/// choices. Data sources are listed beside the C# resources, with the column behind each field and why one is not available, if it is not.
/// </summary>
[ApiVersion("1.0")]
[VersionedApiBackOfficeRoute("application-blocks")]
[ApiExplorerSettings(GroupName = "Application blocks")]
public sealed class AppBlocksController(FormResourceRegistry resources, DataSourceHandlers handlers,
    PublishedDataCatalog definitions, IEnumerable<IAppBlocksCatalogueContributor> contributors) : ManagementApiControllerBase
{
    [HttpPost("validate-form")]
    public async Task<IActionResult> ValidateForm([FromBody] JsonElement grid, [FromServices] AppBlocksValidator validator)
    {
        Response.Headers.CacheControl = "no-store";
        await validator.LoadAsync();
        try { return Ok(validator.EditorProblems(grid.GetRawText())); }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException)
        { return BadRequest("Invalid form block data."); }
    }

    [HttpGet("page-properties/{type:guid}")]
    public IActionResult PageProperties(Guid type, [FromServices] Umbraco.Cms.Core.Services.IContentTypeService types)
        => Ok(types.Get(type)?.CompositionPropertyTypes.Select(p => new { key = p.Key, alias = p.Alias, name = p.Name }));

    [HttpGet("catalogue")]
    public async Task<IActionResult> Catalogue()
    {
        Response.Headers.CacheControl = "no-store";
        await resources.LoadAsync();
        await definitions.LoadAsync();
        var catalogue = new Dictionary<string, object> {
            // What a Form Interface may bind: the C# classes and the providers' resources, such as published data sources.
            ["formClasses"] = resources.Resources.Values.Select(r => new {
                alias = r.Alias, selection = Selection(r), fields = r.Fields, kind = resources.RegisteredResources.ContainsKey(r.Alias) ? "class" : "dataSource",
            }).OrderBy(r => r.alias),
            ["dataSourceHandlers"] = handlers.All.Keys.Order(StringComparer.Ordinal).Select(alias => new { alias }),
            ["lists"] = definitions.Lists.Values.Select(list => new { alias = list.Alias, key = list.Key, selection = AppBlockValues.Udi(list.Key), label = list.Name }).OrderBy(list => list.label),
            ["resources"] = resources.Resources.Values.Select(r => new {
                alias = r.Alias, selection = Selection(r), version = r.ContractVersion, fields = r.Fields,
                dataSource = r is SqlDataSource source ? source.Describe() : null,
            }).OrderBy(r => r.alias),
        };
        foreach (var contributor in contributors) await contributor.PopulateAsync(catalogue, User);
        return Ok(catalogue);

        static string Selection(IFormResource resource) => resource is SqlDataSource source
            ? AppBlockValues.Udi(source.Definition.Key) : resource.Alias;
    }
}
