using AppBlocks.Blocks;
using AppBlocks.Forms;
using Microsoft.AspNetCore.WebUtilities;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Extensions;

namespace AppBlocks.InteractiveReports.Blocks;

internal sealed record ReportFormTarget(IPublishedContent Page, BlockGridItem? Block, string Parameter, bool SamePage, string? Culture = null)
{
    private static ReportFormTarget? For(IPublishedContent? page, IPublishedContent reportPage, string? culture, string? parameter, bool targetForm = false, bool createOnly = false, string? selection = null)
    {
        if (page is null) return null;
        var local = page.Key == reportPage.Key;
        var forms = AppBlockContent.Flatten(AppBlockContent.Blocks(page, culture), culture).Where(b => b.Content.ContentType.Alias == "appForm").ToArray();
        if (!string.IsNullOrWhiteSpace(selection))
        {
            var picked = Guid.TryParse(selection, out var key) ? forms.FirstOrDefault(form => form.ContentKey == key) : null;
            return picked is null ? null : new(page, picked, KeyOf(picked), local, culture);
        }
        var panels = forms.Where(form => form.Content.Value<bool>("sidePanel")).ToArray();
        var single = forms.Length == 1 ? forms[0] : null;
        static string KeyOf(BlockGridItem form) => FormDefinition.KeyParameterFor(form);
        // Existing page links can also lead to ordinary pages or pages of inline forms.
        if (!targetForm && panels.Length == 0 && !forms.Any(f => !string.IsNullOrWhiteSpace(f.Content.Value<string>("parentForm"))))
            return new(page, null, parameter ?? (single is null ? "id" : KeyOf(single)), false, culture);
        // A master form and one detail panel need only the report's key column.
        if (parameter is null && panels.Length == 1) return new(page, panels[0], KeyOf(panels[0]), local, culture);
        if (single is not null && (parameter is null || createOnly)) return new(page, single, KeyOf(single), local, culture);
        var matches = forms.Where(form => string.Equals(KeyOf(form), parameter ?? "id", StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length == 1 ? new(page, matches[0], KeyOf(matches[0]), local, culture) : null;
    }

    public static (ReportFormTarget? Open, ReportFormTarget? Create) Resolve(IPublishedElement block, IPublishedContent page, string? culture, bool localOnly = false)
    {
        var useThisPage = block.Value<bool>("formOnThisPage");
        var openPage = useThisPage ? page : block.Value<IPublishedContent>("formPage");
        // A picked Create page that has since been unpublished has no public destination.
        var createPick = block.GetProperty("createFormPage");
        var createPage = string.IsNullOrWhiteSpace(createPick?.GetSourceValue()?.ToString()) ? openPage : block.Value<IPublishedContent>("createFormPage");
        var parameter = block.Value<string>("keyParameter")?.Trim();
        if (string.IsNullOrEmpty(parameter)) parameter = null;
        if (localOnly)
        {
            if (openPage?.Key != page.Key) openPage = null;
            if (createPage?.Key != page.Key) createPage = null;
        }
        var open = For(openPage, page, culture, parameter, targetForm: useThisPage, selection: block.Value<string>("openForm"));
        var createForm = block.Value<string>("createForm");
        if (createPage?.Key == openPage?.Key && string.IsNullOrWhiteSpace(createForm)) return (open, open);
        return (open, For(createPage, page, culture, parameter, createOnly: true, selection: createForm));
    }

    public FormRelationship? Relationship => Block is null ? null : FormRelationship.Read(Block.Content, Page, Culture);
    public string? PanelAnchor => SamePage && Block?.Content.Value<bool>("sidePanel") == true ? FormDefinition.Anchor(Block.ContentKey) : null;

    public string? LinkMode => PanelAnchor is null ? null : "event";

    // The master page's parameters, with the target form's previous detail key cleared.
    public IQueryCollection PageParameters(HttpContext? context)
    {
        if (context?.Request.Query["appPageQuery"] is not { Count: 1 } pageQuery) return QueryCollection.Empty;
        var parameters = QueryHelpers.ParseQuery(pageQuery.ToString());
        if (!SamePage) {
            var ancestors = new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(StringComparer.OrdinalIgnoreCase);
            var visited = new HashSet<Guid>();
            for (var relation = Relationship; relation?.Parent is { } master && relation.ParentPage is { } page && visited.Add(master.ContentKey);
                relation = FormRelationship.Read(master.Content, page, Culture)) {
                var parameter = relation.ParentParameter;
                if (parameters.TryGetValue(parameter, out var key) && key.Count == 1) ancestors[parameter] = key;
            }
            return new QueryCollection(ancestors);
        }
        parameters.Remove(Parameter);
        return new QueryCollection(parameters);
    }

    public string Url(string? culture, HttpContext? context, string? column = null)
    {
        var provider = context?.RequestServices.GetService<Umbraco.Cms.Core.Routing.IPublishedUrlProvider>();
        var url = provider is null ? Page.Url(culture: culture) : Page.Url(provider, culture);
        // Only this page inherits its own parameters. Create clears the detail key and Edit replaces it.
        if (SamePage || Relationship is not null) url = QueryHelpers.AddQueryString(url, PageParameters(context));
        if (column is not null)
            url += (url.Contains('?') ? "&" : "?") + Uri.EscapeDataString(Parameter) + "={" + column + "}";
        return Block is null ? url : url + "#" + FormDefinition.Anchor(Block.ContentKey);
    }
}
