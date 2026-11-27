using AppBlocks.Blocks;
using AppBlocks.Publishing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Routing;

namespace AppBlocks.Tests;

/// <summary>
/// The page cache decision at routing, apart from a site: which culture's blocks it reads, and that a page whose blocks vary by
/// request is kept out of caches whatever the address's <c>appCulture</c> says.
/// </summary>
public sealed class PageCacheHeadersTests
{
    static PageCacheHeadersTests()
    {
        StaticServiceProvider.Instance ??= new ServiceCollection()
            .AddSingleton<IPublishedValueFallback>(new NoopPublishedValueFallback())
            .BuildServiceProvider();
    }

    [Theory]
    // A culture the page does not have is as if the address named none: the routed culture's blocks decide.
    [InlineData("xx-XX")]
    [InlineData("en-US")]
    [InlineData(null)]
    public async Task An_interactive_variant_page_is_kept_out_of_caches_whatever_culture_the_address_names(string? asked)
    {
        Assert.True(await NoStoreAsync(NoStorePages.AppBlocksPages, Page(["en-US"], Block("appForm")), "en-US", asked));
        // Security that depends on the visitor keeps the page out of caches in every mode.
        Assert.True(await NoStoreAsync(NoStorePages.Off, Page(["en-US"], Block("appText", Dynamic(BlockConditions.Security))), "en-US", asked));
    }

    [Theory]
    [InlineData("xx-XX")]
    [InlineData(null)]
    public async Task An_invariant_page_reads_its_blocks_as_before(string? asked)
    {
        Assert.True(await NoStoreAsync(NoStorePages.AppBlocksPages, Page([], Block("appForm")), null, asked));
        Assert.True(await NoStoreAsync(NoStorePages.Off, Page([], Block("appText", Dynamic(BlockConditions.ReadOnly))), null, asked));
    }

    [Fact]
    public async Task Content_blocks_alone_leave_the_page_to_the_host_and_a_form_only_marks_it_when_the_mode_says()
    {
        Assert.False(await NoStoreAsync(NoStorePages.AppBlocksPages, Page(["en-US"], Block("appHtml")), "en-US", null));
        Assert.False(await NoStoreAsync(NoStorePages.Off, Page(["en-US"], Block("appForm")), "en-US", null));
        Assert.True(await NoStoreAsync(NoStorePages.AllPages, Page(["en-US"], Block("appHtml")), "en-US", null));
    }

    private static async Task<bool> NoStoreAsync(NoStorePages mode, IPublishedContent page, string? routed, string? asked)
    {
        var context = new DefaultHttpContext();
        if (asked is not null) context.Request.QueryString = new QueryString("?" + AppBlockContent.CultureParameter + "=" + asked);
        var http = TestStubs.Stub<IHttpContextAccessor>((m, _) => m.Name == "get_HttpContext" ? context : null);
        IPublishedRequestBuilder? builder = null;
        builder = TestStubs.Stub<IPublishedRequestBuilder>((m, _) => m.Name switch {
            "get_PublishedContent" => page,
            "get_Culture" => routed,
            nameof(IPublishedRequestBuilder.SetNoCacheHeader) or nameof(IPublishedRequestBuilder.SetCacheExtensions) => builder,
            _ => null,
        });
        await new PageCacheHeaders(Options.Create(new PageCacheOptions { NoStorePages = mode }), http, [])
            .HandleAsync(new RoutingRequestNotification(builder), default);
        return context.Response.Headers.CacheControl.ToString() == "no-cache, no-store";
    }

    /// <summary>A page with one Block Grid property, whose value is the same blocks in whatever culture it is asked.</summary>
    private static IPublishedContent Page(string[] cultures, BlockGridItem block)
    {
        var grid = new BlockGridModel([block], 12);
        var propertyType = TestStubs.Stub<IPublishedPropertyType>((m, _) => m.Name switch {
            "get_Alias" => "blocks",
            "get_EditorAlias" => "Umbraco.BlockGrid",
            _ => null,
        });
        var type = TestStubs.Stub<IPublishedContentType>((m, _) => m.Name switch {
            "get_Alias" => "page",
            "get_PropertyTypes" => new[] { propertyType },
            _ => null,
        });
        var property = Property("blocks", grid);
        return TestStubs.Stub<IPublishedContent>((m, args) => m.Name switch {
            "get_ContentType" => type,
            "get_Key" => Guid.Empty,
            "get_Cultures" => cultures.ToDictionary(c => c, _ => (PublishedCultureInfo)null!),
            nameof(IPublishedContent.GetProperty) => (string)args![0]! == "blocks" ? property : null,
            _ => null,
        });
    }

    private static BlockGridItem Block(string alias, params (string Alias, object Value)[] properties)
        => new(Guid.NewGuid(), Element(alias, properties), null, null);

    /// <summary>A Security or Read Only list whose one condition compares an item, so its answer depends on the request.</summary>
    private static (string, object) Dynamic(string property) => (property, new BlockListModel([
        new BlockListItem(Guid.NewGuid(), Element(BlockConditions.ConditionAlias(property), ("comparison", "Is"), ("item", "status"), ("literal", "Closed")), null, null)]));

    private static IPublishedElement Element(string alias, params (string Alias, object Value)[] properties)
    {
        var map = properties.ToDictionary(p => p.Alias, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var type = TestStubs.Stub<IPublishedContentType>((m, _) => m.Name switch {
            "get_Alias" => alias,
            "get_PropertyTypes" => Array.Empty<IPublishedPropertyType>(),
            _ => null,
        });
        return TestStubs.Stub<IPublishedElement>((m, args) => m.Name switch {
            "get_ContentType" => type,
            "get_Key" => Guid.NewGuid(),
            nameof(IPublishedElement.GetProperty) => map.TryGetValue((string)args![0]!, out var value) ? Property((string)args[0]!, value) : null,
            _ => null,
        });
    }

    private static IPublishedProperty Property(string alias, object value) => TestStubs.Stub<IPublishedProperty>((m, _) => m.Name switch {
        "get_Alias" => alias,
        nameof(IPublishedProperty.HasValue) => true,
        nameof(IPublishedProperty.GetValue) => value,
        nameof(IPublishedProperty.GetSourceValue) => value,
        _ => null,
    });
}
