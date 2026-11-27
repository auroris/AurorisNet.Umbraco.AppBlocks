using AppBlocks.Blocks;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace AppBlocks.Publishing;

/// <summary>Applies the host's page cache setting, including when Public Access substitutes a login or denied page.</summary>
public sealed class PageCacheHeaders(IOptions<PageCacheOptions> settings, IHttpContextAccessor http, IEnumerable<IAppBlockPlugin> plugins)
    : INotificationAsyncHandler<RoutingRequestNotification>
{
    private static readonly object NoStoreRequest = new();

    public Task HandleAsync(RoutingRequestNotification notification, CancellationToken cancellationToken)
    {
        var mode = settings.Value.NoStorePages;
        if (http.HttpContext is not { } context) return Task.CompletedTask;
        // Keep the original route's decision across Public Access's login/denied-page rewrite.
        if (notification.RequestBuilder.PublishedContent is { } page)
        {
            // The address's culture counts only when the page has it, or has no cultures: one it lacks would read no blocks at all.
            var culture = AppBlockContent.RequestCulture(context) is { } asked && (page.Cultures.Count == 0 || page.Cultures.ContainsKey(asked)) ? asked
                : notification.RequestBuilder.Culture is { Length: > 0 } c && page.Cultures.ContainsKey(c) ? c : null;
            // A dynamic access condition already makes a page interactive, so in AppBlocksPages mode one walk of the blocks answers both.
            if (mode == NoStorePages.AllPages
                || (mode == NoStorePages.AppBlocksPages ? AppBlockContent.IsInteractive(page, plugins, culture) : AppBlockContent.HasDynamicAccessConditions(page, culture)))
                context.Items[NoStoreRequest] = true;
        }
        if (context.Items[NoStoreRequest] is not true) return Task.CompletedTask;

        context.KeepOutOfCaches();
        context.Response.Headers.CacheControl = "no-cache, no-store";
        notification.RequestBuilder.SetNoCacheHeader(true);
        notification.RequestBuilder.SetCacheExtensions(["no-store"]);
        return Task.CompletedTask;
    }
}
