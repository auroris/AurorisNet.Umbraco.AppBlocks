using Microsoft.AspNetCore.Http;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Extensions;

namespace AppBlocks.Blocks;

internal static class BlockLinkTemplate
{
    public static string? Read(IPublishedElement block, string? culture = null, PathString pathBase = default)
    {
        var template = block.Value<string>("linkTemplate", culture: culture)?.Trim();
        if (string.IsNullOrWhiteSpace(block.GetProperty("linkPage")?.GetSourceValue(culture)?.ToString()))
            return string.IsNullOrEmpty(template) ? null : template;
        if (block.Value<IPublishedContent>("linkPage", culture: culture) is not { } page) return null;
        if (!string.IsNullOrEmpty(template) && template[0] is not ('?' or '#')) return null;
        var url = page.Url(culture: culture);
        if (url == "#") return null;
        if (pathBase.HasValue && url.StartsWith(pathBase.Value, StringComparison.OrdinalIgnoreCase))
        {
            var rest = url[pathBase.Value.Length..];
            if (rest.Length == 0 || rest[0] == '/') url = rest.Length == 0 ? "/" : rest;
        }
        return url + template;
    }
}
