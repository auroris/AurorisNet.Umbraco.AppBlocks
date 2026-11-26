using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;

namespace AppBlocks.Blocks;

public sealed class AppBlockContent(IUmbracoContextFactory contexts, IHttpContextAccessor http)
{
    public const string CultureParameter = "appCulture";

    public static string? RequestCulture(HttpContext? context) => context?.Request.Query[CultureParameter] is { Count: 1 } value && !string.IsNullOrWhiteSpace(value[0]) ? value[0] : null;

    public async Task<IPublishedContent?> PageAsync(Guid key)
    {
        using var reference = contexts.EnsureUmbracoContext();
        var page = reference.UmbracoContext.Content is { } cache ? await cache.GetByIdAsync(key, false) : null;
        return page is not null && AppBlockSchema.HasBlocks(page.ContentType) ? page : null;
    }

    public static BlockGridModel Blocks(IPublishedContent page, string? culture = null) =>
        culture is not null && page.Cultures.Count > 0 && !page.Cultures.ContainsKey(culture) ? BlockGridModel.Empty
            : new(NestedBlocks(page, culture).ToList(), 12);

    /// <summary>
    /// Whether the page has a block whose output depends on who asks or on data that changes without a publish: a form,
    /// attachments, a schedule, a tabular form, or a plugin block that requires a user or varies by data
    /// (<see cref="IAppBlockPlugin.VariesByData"/>). Such a page is rendered for this request and kept out of caches; a page of
    /// content blocks alone is cached as Umbraco and the host decide.
    /// </summary>
    public static bool IsInteractive(IPublishedContent page, IEnumerable<IAppBlockPlugin> plugins, string? culture = null)
        => AppBlockSchema.HasBlocks(page.ContentType) && IsInteractive(Blocks(page, culture), plugins, culture);

    /// <summary>The same question for some blocks of a page and everything inside them, such as the one block a native Block Grid partial renders.</summary>
    public static bool IsInteractive(IEnumerable<BlockGridItem> blocks, IEnumerable<IAppBlockPlugin> plugins, string? culture = null)
        => Flatten(blocks, culture).Any(block =>
            block.Content.ContentType.Alias is "appForm" or AppBlockSchema.AttachmentsAlias or AppBlockSchema.ScheduleAlias or AppBlockSchema.TabularAlias
            || BlockConditions.VariesByRequest(block)
            || plugins.Any(plugin => Varies(plugin) && plugin.Alias == block.Content.ContentType.Alias));

    /// <summary>Whether any block on <paramref name="page"/> carries a visitor- or item-based Security or Read Only condition.</summary>
    public static bool HasDynamicAccessConditions(IPublishedContent page, string? culture = null)
        => AppBlockSchema.HasBlocks(page.ContentType) && Flatten(Blocks(page, culture), culture).Any(BlockConditions.RequiresNoStore);

    /// <summary>Whether a plugin's block keeps its page out of caches: it requires the user, or its output varies by data.</summary>
    internal static bool Varies(IAppBlockPlugin plugin) => plugin.RequiresUser || plugin.VariesByData;

    public static BlockGridItem AsGridItem(BlockListItem item) => new(item.ContentKey, item.Content, item.SettingsKey, item.Settings)
        { ColumnSpan = 12, RowSpan = 1, Areas = [] };

    public static IEnumerable<BlockGridItem> NestedBlocks(IPublishedElement element, string? culture = null)
    {
        foreach (var property in element.ContentType.PropertyTypes)
        {
            if (property.Alias is BlockConditions.Security or BlockConditions.Visibility or BlockConditions.ReadOnly) continue;
            if (property.EditorAlias == "Umbraco.BlockGrid" && element.Value<BlockGridModel>(property.Alias, culture: culture) is { } grid)
                foreach (var item in grid) yield return item;
            else if (property.EditorAlias == "Umbraco.BlockList" && element.Value<BlockListModel>(property.Alias, culture: culture) is { } list)
                foreach (var item in list) yield return AsGridItem(item);
            else if (property.EditorAlias == "Umbraco.SingleBlock" && element.Value<BlockListItem>(property.Alias, culture: culture) is { } single)
                yield return AsGridItem(single);
        }
    }

    internal static IEnumerable<BlockGridItem> Children(BlockGridItem item, string? culture)
        => item.Areas.SelectMany(area => area).Concat(NestedBlocks(item.Content, culture));

    public static IEnumerable<BlockGridItem> Flatten(IEnumerable<BlockGridItem> items, string? culture = null)
    {
        foreach (var item in items) {
            yield return item;
            foreach (var child in Flatten(Children(item, culture), culture)) yield return child;
        }
    }
    internal static IReadOnlyList<BlockGridItem>? PathTo(IEnumerable<BlockGridItem> items, Guid key, string? culture)
    {
        foreach (var item in items)
        {
            if (item.ContentKey == key) return [item];
            if (PathTo(Children(item, culture), key, culture) is { } path)
                return [item, ..path];
        }
        return null;
    }

    public static string BlockName(Guid page, Guid block) => $"page-{page:N}-{block:N}";
    public static bool ParseBlockName(string name, out Guid page, out Guid block)
    {
        page = block = Guid.Empty;
        var parts = name.Split('-');
        return parts.Length == 3 && parts[0] == "page" && Guid.TryParseExact(parts[1], "N", out page) && Guid.TryParseExact(parts[2], "N", out block);
    }
    public async Task<(IPublishedContent Page, BlockGridItem Block, string? Culture)?> BlockAsync(string name, string alias)
    {
        if (!ParseBlockName(name, out var pageKey, out var blockKey) || await PageAsync(pageKey) is not { } page) return null;
        var culture = RequestCulture(http.HttpContext);
        var path = PathTo(Blocks(page, culture), blockKey, culture);
        if (path is null || path[^1].Content.ContentType.Alias != alias) return null;
        if (path.Any(BlockConditions.HasSecurity))
        {
            if (http.HttpContext is not { } request) return null;
            var services = request.RequestServices;
            var caller = await services.GetRequiredService<AppPageAccess>().VisitorAsync(page);
            if (caller is null) return null;
            var conditions = services.GetRequiredService<BlockConditions>();
            if (!await conditions.AllowsPathAsync(page, path, caller, culture, request.RequestAborted)) return null;
        }
        return (page, path[^1], culture);
    }
}
