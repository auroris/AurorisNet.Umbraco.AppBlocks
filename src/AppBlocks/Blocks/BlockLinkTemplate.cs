using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Models;
using Umbraco.Extensions;

namespace AppBlocks.Blocks;

internal static class BlockLinkTemplate
{
    public static string? Read(IPublishedElement block, string? culture = null)
    {
        var template = block.Value<string>("linkTemplate", culture: culture)?.Trim();
        if (string.IsNullOrWhiteSpace(block.GetProperty("linkPage")?.GetSourceValue(culture)?.ToString()))
            return string.IsNullOrEmpty(template) ? null : template;
        if (block.Value<IPublishedContent>("linkPage", culture: culture) is not { } page) return null;
        if (!string.IsNullOrEmpty(template) && template[0] is not ('?' or '#')) return null;
        var url = page.Url(culture: culture, mode: UrlMode.Absolute);
        return url == "#" ? null : url + template;
    }
}
