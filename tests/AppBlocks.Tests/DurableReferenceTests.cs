using System.Text.Json.Nodes;
using AppBlocks.Attachments;
using AppBlocks.Blocks;
using AppBlocks.DataSources;
using AppBlocks.Forms;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace AppBlocks.Tests;

[Collection("Standalone Umbraco")]
public sealed class DurableReferenceTests
{
    [Fact]
    public async Task Picked_sources_survive_moves_and_alias_reuse_and_the_document_relation_follows_the_pick()
    {
        await using var site = await BlockSite.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        Guid sourceKey;
        using (var setup = site.CreateScope())
        {
            var contents = setup.ServiceProvider.GetRequiredService<IContentService>();
            var types = setup.ServiceProvider.GetRequiredService<IContentTypeService>();
            var source = contents.GetPagedOfType(types.Get(DataSourceSchema.SqlAlias)!.Id, 0, 100, out _, null!).Single();
            sourceKey = source.Key;
            var page = contents.GetById(BlockSite.FormPageKey)!;
            var grid = JsonNode.Parse(page.GetValue<string>(BlockSite.BodyAlias)!)!;
            var table = BlockPages.Blocks(grid, "appTabular").Single();
            BlockPages.SetValue(table, "dataSource", AppBlockValues.Udi(source.Key));
            page.SetValue(BlockSite.BodyAlias, grid.ToJsonString());
            Publish(contents, page);
            Assert.True(References(setup.ServiceProvider, page.Id, source.Id), "The saved Block Grid must track its picked SQL document.");
            var store = setup.ServiceProvider.GetRequiredService<AttachmentStore>();
            var original = Assert.IsType<SqlDataSource>(await setup.ServiceProvider.GetRequiredService<FormResourceRegistry>().FindAsync(AppBlockValues.Udi(sourceKey)));
            await store.AddAsync(new(AttachmentOwner.ResourceId(original), "1"), new("before.txt", "text/plain", [1]), null, null, null, "tester", ct);
        }

        Guid replacementKey;
        using (var edit = site.CreateScope())
        {
            var contents = edit.ServiceProvider.GetRequiredService<IContentService>();
            var source = contents.GetById(sourceKey)!;
            var oldParent = source.ParentId;
            source.Name = "Renamed lines";
            source.SetValue("alias", "lines.renamed");
            Publish(contents, source);
            var home = contents.GetById(AppBlockSchema.Key("block-site:home"))!;
#pragma warning disable CS0618
            Assert.True(contents.Move(source, home.Id).Success);
#pragma warning restore CS0618
            var replacement = contents.Create("Different lines", oldParent, DataSourceSchema.SqlAlias);
            replacement.CopyFrom(source, "*");
            replacement.SetValue("alias", "orders.lines");
            replacement.SetValue("access", "Not public");
            replacement.SetValue("source", "ORDER_LINES");
            replacement.SetValue("keyColumns", "LINE_ID");
            Publish(contents, replacement);
            replacementKey = replacement.Key;
        }

        using var client = await site.SignInMemberAsync(site.CreateMember("picked", "picked@example.invalid", "Reader"));
        var rows = await site.Tabular(client, BlockSite.FormPageKey, BlockPages.LinesBlock).ListAsync("id=4");
        Assert.True(rows.IsSuccess, rows.Problem);
        Assert.Equal(2, rows.Rows.Count());
        using var verify = site.CreateScope();
        var services = verify.ServiceProvider;
        var registry = services.GetRequiredService<FormResourceRegistry>();
        var selected = Assert.IsType<SqlDataSource>(await registry.FindAsync(AppBlockValues.Udi(sourceKey)));
        Assert.Equal("lines.renamed", selected.Alias);
        Assert.Same(selected, registry.Find(sourceKey.ToString()));
        var other = Assert.IsType<SqlDataSource>(registry.Find("orders.lines"));
        Assert.Equal(replacementKey, other.Definition.Key);
        var files = services.GetRequiredService<AttachmentStore>();
        Assert.Equal("before.txt", Assert.Single(await files.ListAsync(AttachmentOwner.ForResource(selected, "1"), ct)).FileName);
        Assert.Empty(await files.ListAsync(AttachmentOwner.ForResource(other, "1"), ct));
        // Existing SQL data source attachments stored under a pre-upgrade alias remain accessible and can be migrated to the UDI.
        var legacyFile = await files.AddAsync(new("orders.lines", "2"), new("legacy.txt", "text/plain", [2]), null, null, null, "tester", ct);
        Assert.Equal("legacy.txt", Assert.Single(await files.ListAsync(AttachmentOwner.ForResource(other, "2"), ct)).FileName);
        Assert.Equal(1, await files.MigrateOwnerAsync("orders.lines", AttachmentOwner.ResourceId(other), ct));
        Assert.Equal("legacy.txt", Assert.Single(await files.ListAsync(new(AttachmentOwner.ResourceId(other), "2"), ct)).FileName);
        Assert.NotNull(await files.ReadAsync(new(AttachmentOwner.ResourceId(other), "2"), legacyFile.Id, ct));
        var schedules = services.GetRequiredService<AppBlocks.Schedules.ScheduleSources>();
        await schedules.LoadAsync();
        Assert.Equal("lines.renamed", schedules.Resolve(AppBlockValues.Udi(sourceKey))!.Alias);
        var controller = ActivatorUtilities.CreateInstance<AppBlocksController>(services);
        var missingType = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(controller.PageProperties(Guid.NewGuid(), services.GetRequiredService<IContentTypeService>()));
        Assert.Empty(Assert.IsAssignableFrom<System.Collections.IEnumerable>(missingType.Value));
        var data = services.GetRequiredService<PublishedDataCatalog>();
        await data.LoadAsync();
        Assert.Equal(sourceKey, data.ResolveSource(AppBlockValues.Udi(sourceKey))!.Key);
        var documents = services.GetRequiredService<IContentService>();
        var owner = documents.GetById(BlockSite.FormPageKey)!;
        var first = documents.GetById(sourceKey)!.Id;
        var second = documents.GetById(replacementKey)!.Id;
        Assert.True(References(services, owner.Id, first));
        Assert.False(References(services, owner.Id, second));
        // The published version still uses the original while the draft uses its replacement.
        var body = JsonNode.Parse(owner.GetValue<string>(BlockSite.BodyAlias)!)!;
        BlockPages.SetValue(BlockPages.Blocks(body, "appTabular").Single(), "dataSource", AppBlockValues.Udi(replacementKey));
        owner.SetValue(BlockSite.BodyAlias, body.ToJsonString());
#pragma warning disable CS0618
        Assert.True(documents.Save(owner).Success);
#pragma warning restore CS0618
        Assert.True(References(services, owner.Id, first));
        Assert.True(References(services, owner.Id, second));
        Publish(documents, owner);
        Assert.False(References(services, owner.Id, first));
        Assert.True(References(services, owner.Id, second));
        owner.SetValue(BlockSite.BodyAlias, BlockPages.Empty().ToJsonString());
        Publish(documents, owner);
        Assert.False(References(services, owner.Id, second));
    }

    [Fact]
    public async Task A_page_condition_follows_its_property_after_an_alias_change()
    {
        await using var site = await BlockSite.StartAsync();
        using var client = await site.SignInMemberAsync(site.CreateMember("property", "property@example.invalid", "Reader"));
        using var scope = site.CreateScope();
        var services = scope.ServiceProvider;
        var types = services.GetRequiredService<IContentTypeService>();
        var type = types.Get(BlockSite.PageAlias)!;
        var heading = types.Get("appForm")!.PropertyTypes.Single(p => p.Alias == "heading");
        var dataType = await services.GetRequiredService<IDataTypeService>().GetAsync(heading.DataTypeKey);
        var property = new PropertyType(services.GetRequiredService<Umbraco.Cms.Core.Strings.IShortStringHelper>(), dataType!) {
            Key = Guid.NewGuid(), Name = "Audience", Alias = "audience",
        };
        type.AddPropertyType(property);
        Assert.True((await types.UpdateAsync(type, Umbraco.Cms.Core.Constants.Security.SuperUserKey)).Success);
        var conditions = AppBlockValues.BlockList(b => b.Place(b.Add("appSecurityCondition",
            new { comparison = "Is", item = property.Key, literal = "members" })));
        var grid = BlockPages.OrdersForm();
        var form = BlockPages.Blocks(grid, "appForm").Single();
        BlockPages.SetValue(form, "heading", "Audience marker");
        form["values"]!.AsArray().Add(new JsonObject {
            ["alias"] = BlockConditions.Security, ["value"] = JsonNode.Parse(conditions),
        });
        var contents = services.GetRequiredService<IContentService>();
        var page = contents.GetById(BlockSite.WebsitePageKey)!;
        page.SetValue(BlockSite.BodyAlias, grid.ToJsonString());
        page.SetValue("audience", "members");
        Publish(contents, page);
        Assert.Equal(property.Key, types.Get(type.Key)!.CompositionPropertyTypes.Single(p => p.Alias == "audience").Key);
        Assert.Contains("Audience marker", await client.GetStringAsync("/website/"));
        type = types.Get(type.Key)!;
        type.PropertyTypes.Single(p => p.Key == property.Key).Alias = "renamedAudience";
        Assert.True((await types.UpdateAsync(type, Umbraco.Cms.Core.Constants.Security.SuperUserKey)).Success);
        page = contents.GetById(page.Key)!;
        Assert.Equal("members", page.GetValue<string>("renamedAudience"));
        Publish(contents, page);
        Assert.Contains("Audience marker", await client.GetStringAsync("/website/"));
        page.SetValue("renamedAudience", "guests");
        Publish(contents, page);
        Assert.DoesNotContain("Audience marker", await client.GetStringAsync("/website/"));
    }

    private static bool References(IServiceProvider services, int parent, int target)
        => services.GetRequiredService<IRelationService>().GetByParentId(parent)?.Any(r => r.ChildId == target) == true;

    private static void Publish(IContentService contents, IContent node)
    {
#pragma warning disable CS0618
        Assert.True(contents.Save(node).Success);
        var result = contents.Publish(node, ["*"]);
        Assert.True(result.Success, string.Join(" ", result.EventMessages?.GetAll().Select(m => m.Message) ?? []));
#pragma warning restore CS0618
    }
}
