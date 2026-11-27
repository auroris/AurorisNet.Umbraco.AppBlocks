using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Services.Navigation;

namespace AppBlocks.DataSources;

public static class PublishedDefinitionNodes
{
    public static async IAsyncEnumerable<IPublishedContent> OfType(IPublishedContentCache cache,
        IDocumentNavigationQueryService navigation, string type)
    {
        if (!navigation.TryGetRootKeys(out var roots)) yield break;
        foreach (var root in roots)
        {
            if (await cache.GetByIdAsync(root, false) is not { } rootNode) continue;
            if (rootNode.ContentType.Alias == type) yield return rootNode;
            if (!navigation.TryGetDescendantsKeysOfType(root, type, out var keys)) continue;
            foreach (var key in keys)
            {
                if (!navigation.TryGetAncestorsKeys(key, out var ancestors)) continue;
                var published = true;
                foreach (var ancestor in ancestors)
                    if (await cache.GetByIdAsync(ancestor, false) is null) { published = false; break; }
                if (published && await cache.GetByIdAsync(key, false) is { } node) yield return node;
            }
        }
    }
}
