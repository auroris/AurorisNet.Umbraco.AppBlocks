using AppBlocks.Blocks;
using AppBlocks.DataSources;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Extensions;

namespace AppBlocks.Forms;

/// <summary>The master form's record key supplies a detail's link field. The URL identifies the master;
/// loading it through its published form establishes the caller's access and its canonical key.</summary>
public sealed record FormRelationship(BlockGridItem? Parent, string LinkField, IPublishedContent? ParentPage)
{
    public string ParentParameter => Parent is null ? "" : FormDefinition.KeyParameterFor(Parent);

    public static FormRelationship? Read(IPublishedElement block, IPublishedContent page, string? culture = null)
    {
        var parent = block.Value<string>("parentForm") ?? "";
        var field = block.Value<string>("linkField") ?? "";
        var picked = !string.IsNullOrWhiteSpace(block.GetProperty("parentPage")?.GetSourceValue()?.ToString());
        if (parent.Length == 0 && field.Length == 0 && !picked) return null;
        var parentPage = picked ? block.Value<IPublishedContent>("parentPage") : page;
        var form = parentPage is not null && Guid.TryParse(parent, out var id)
            ? AppBlockContent.Flatten(AppBlockContent.Blocks(parentPage, culture), culture).FirstOrDefault(b => b.ContentKey == id && b.Content.ContentType.Alias == "appForm") : null;
        return new(form, field, parentPage);
    }

    public Task<(DataSourceScope? Scope, FormResult? Refusal)> ResolveAsync(FormDefinition detail, FormExecutionContext context,
        IServiceProvider services, CancellationToken ct)
        => ResolveAsync(detail, context, services, new HashSet<Guid> { detail.Block.ContentKey }, ct);

    private async Task<(DataSourceScope? Scope, FormResult? Refusal)> ResolveAsync(FormDefinition detail, FormExecutionContext context,
        IServiceProvider services, HashSet<Guid> visited, CancellationToken ct)
    {
        if (ParentPage is null || Parent is null || ParentParameter.Length == 0 || !detail.Resource.Fields.Any(f => f.Key == LinkField && !f.ReadOnly)
            || ParentParameter.Equals(detail.KeyParameter, StringComparison.OrdinalIgnoreCase))
            return (null, FormResult.Invalid("", "Check the parent form, its key parameter and the detail link field."));
        var caller = await services.GetRequiredService<AppPageAccess>().VisitorAsync(ParentPage);
        if (caller is null) return (null, FormResult.Denied());
        if (!visited.Add(Parent.ContentKey)) return (null, FormResult.Invalid("", "A form cannot be its own ancestor."));
        if (context.Parameters?.TryGetValue(ParentParameter, out var key) != true || string.IsNullOrWhiteSpace(key)) return (null, FormResult.Missing());
        var parent = await FormDefinition.ResolveAsync(ParentPage, Parent.ContentKey, services.GetRequiredService<FormResourceRegistry>(),
            services.GetRequiredService<PublishedDataCatalog>(), detail.Culture, services.GetServices<IAppBlockPlugin>());
        if (parent is null) return (null, FormResult.Invalid("", "The parent form cannot be loaded."));
        if (FormResourceAccess.Refusal(parent.Resource, caller) is { } status) return (null, new FormResult(status));
        var masterContext = new FormExecutionContext(caller, ParentPage.Key, parent.Block.ContentKey, parent.Revision, Parameters: context.Parameters);
        if (parent.Relationship is { } ancestor) {
            var resolved = await ancestor.ResolveAsync(parent, masterContext, services, visited, ct);
            if (resolved.Refusal is not null) return resolved;
            masterContext = masterContext with { ParentScope = resolved.Scope };
        }
        var loaded = await parent.LoadAsync(key, masterContext, ct);
        return loaded.Record?.Key is { Length: > 0 } canonical ? (new(LinkField, canonical), null)
            : (null, loaded.Status == 200 ? FormResult.Missing() : loaded);
    }
}
