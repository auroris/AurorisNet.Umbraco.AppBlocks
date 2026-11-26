using System.Net;
using AppBlocks.Blocks;
using AppBlocks.DataSources;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Services;

namespace AppBlocks.Tests;

[Collection("Standalone Umbraco")]
public sealed class SearchSelectTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Published_single_column_list_supplies_and_validates_both_selection_modes(bool freeText)
    {
        await using var site = await BlockSite.StartAsync();
        using (var connection = site.OpenDatabase())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO ORDER_LINES (ORDER_ID, ITEM) VALUES (4, 'A,B'), (5, 'A,B')";
            command.ExecuteNonQuery();
        }
        Guid pageKey, formKey;
        using (var scope = site.CreateScope())
        {
            var contents = scope.ServiceProvider.GetRequiredService<IContentService>();
#pragma warning disable CS0618
            var root = contents.GetRootContent().Single(c => c.ContentType.Alias == DataSourceSchema.RootAlias);
            var list = contents.Create("Items", root.Id, PublishedDataCatalog.ListAlias);
            list.SetValue("alias", "items");
            list.SetValue("sql", "SELECT DISTINCT ITEM FROM ORDER_LINES");
            list.SetValue("valueColumn", "ITEM");
            list.SetValue("displayColumn", "ITEM");
            Assert.True(contents.Save(list).Success);
            Assert.True(contents.Publish(list, ["*"]).Success);

            var blocks = BlockPages.OrdersForm();
            var many = BlockPages.Blocks(blocks, "appText").Single();
            var one = BlockPages.Blocks(blocks, "appTextArea").Single();
            foreach (var block in new[] { many, one })
            {
                block["contentTypeKey"] = AppBlockSchema.Key(AppBlockSchema.SearchSelectAlias).ToString();
                BlockPages.SetValue(block, "listOfValues", "items");
                if (freeText) BlockPages.SetValue(block, AppBlockField.FreeTextAlias, "1");
            }
            BlockPages.SetValue(many, AppBlockField.MultipleAlias, "1");
            BlockPages.SetValue(many, AppBlockField.MaxOptionsAlias, "0");
            // Region gets its suggestions from the resource rather than the published list.
            var region = BlockPages.Blocks(blocks, "appSelect").Single(b => BlockPages.Value(b, "field")!.ToString() == "region");
            region["contentTypeKey"] = AppBlockSchema.Key(AppBlockSchema.SearchSelectAlias).ToString();
            if (freeText) BlockPages.SetValue(region, AppBlockField.FreeTextAlias, "1");
            formKey = Guid.Parse(BlockPages.Blocks(blocks, "appForm").Single()["key"]!.ToString());
            var page = contents.Create("Searchable choices", contents.GetById(BlockSite.FormPageKey)!.ParentId, BlockSite.PageAlias);
            page.SetValue(BlockSite.BodyAlias, blocks.ToJsonString());
            Assert.True(contents.Save(page).Success);
            Assert.True(contents.Publish(page, ["*"]).Success);
#pragma warning restore CS0618
            pageKey = page.Key;
        }
        await site.WaitForPublishedAsync(pageKey);
        using var client = await site.SignInMemberAsync(site.CreateMember("search-member", "search@example.test", "Search member"));
        var html = await client.GetStringAsync("/searchable-choices/");
        Assert.Equal(3, html.Split("data-app-search-select").Length - 1);
        Assert.Equal(3, html.Split($"data-allow-free-text=\"{freeText.ToString().ToLowerInvariant()}\"").Length - 1);
        Assert.Equal(2, html.Split("<option value=\"A,B\">A,B</option>").Length - 1);
        Assert.Contains("data-kind=\"csv\"", html);
        Assert.Contains("data-max-options=\"50\"", html);
        Assert.Contains("data-max-options=\"0\"", html);
        var form = new FormsClient(client, "", pageKey, formKey);
        Assert.True((await form.LoadAsync()).IsSuccess);
        Task<FormResponse> Create(string customer, string notes, string region = "WEST") => form.CreateAsync(new { customer, notes, region, status = "NEW", amount = 1, orderDate = "2026-01-01" });
        const string csv = "Oil,\"A,B\"";
        var created = await Create(csv, "A,B");
        Assert.True(created.IsSuccess, created.Body.ToString());
        Assert.Equal(csv, (await form.LoadAsync(created.RecordKey)).Values.GetProperty("customer").GetString());
        foreach (var invalid in new[] { "Oil,Oil", "Oil,", "", new string('a', 201) })
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Create(invalid, "A,B")).Response.StatusCode);
        const string mixed = "Oil,\"401 Sqn, West\"";
        foreach (var result in new[] { await Create(mixed, "A,B"), await Create(csv, "New note"), await Create(csv, "A,B", "New region") })
        {
            Assert.Equal(freeText ? HttpStatusCode.OK : HttpStatusCode.UnprocessableEntity, result.Response.StatusCode);
        }
        if (freeText)
        {
            var updated = await form.UpdateAsync(created.RecordKey!, created.RecordChecksum, new { customer = mixed });
            Assert.True(updated.IsSuccess, updated.Body.ToString());
            Assert.Equal(mixed, (await form.LoadAsync(created.RecordKey)).Values.GetProperty("customer").GetString());
        }
    }

    [Fact]
    public void Only_a_single_selection_can_bind_to_a_numeric_field()
    {
        Assert.True(AppBlocksValidator.Compatible(AppBlockSchema.SearchSelectAlias, new("id", "Id", "number"), true));
        Assert.False(AppBlocksValidator.Compatible(AppBlockSchema.SearchSelectAlias, new("ids", "Ids", "number"), true, true));
        Assert.False(AppBlocksValidator.Compatible(AppBlockSchema.SearchSelectAlias, new("ids", "Ids", "number"), true, true, true));
        Assert.True(AppBlocksValidator.Compatible(AppBlockSchema.SearchSelectAlias, new("unit", "Unit", "text"), false, false, true));
    }

    [Fact]
    public void Negative_max_options_is_rejected_by_validator()
    {
        var blocks = BlockPages.Empty();
        var field = BlockPages.Add(blocks, AppBlockSchema.SearchSelectAlias, ("field", "customer"), (AppBlockField.MaxOptionsAlias, "-1"));
        BlockPages.Layout(blocks).Add(BlockPages.FormBlock(blocks, "test.search", [field], []));
        var resource = TestStubs.Stub<AppBlocks.Forms.IFormResource>((m, _) => m.Name switch {
            "get_Alias" => "test.search",
            "get_Fields" => (IReadOnlyList<AppBlocks.Forms.FormField>)[new("customer", "Customer", "text", Choices: [new("a", "A")])],
            _ => null,
        });
        var validator = new AppBlocksValidator(new AppBlocks.Forms.FormResourceRegistry([resource], []));
        Assert.Contains(validator.Validate(blocks.ToJsonString()), e => e.Contains("maximum visible choices", StringComparison.OrdinalIgnoreCase));
        BlockPages.SetValue(blocks["contentData"]!.AsArray().Single(b => b!["key"]!.GetValue<string>() == field["contentKey"]!.GetValue<string>())!, AppBlockField.MaxOptionsAlias, "0");
        Assert.Empty(validator.Validate(blocks.ToJsonString()));
    }

    [Fact]
    public void Prepended_vendor_stylesheet_renders_ahead_of_runtime_css()
    {
        var assets = new AppPageAssets();
        assets.AddStylesheet("/App_Plugins/ApplicationBlocks/runtime.css");
        assets.PrependStylesheet("/App_Plugins/ApplicationBlocks/tom-select.css");
        assets.PrependStylesheet("/App_Plugins/ApplicationBlocks/tom-select.css");
        Assert.Equal(["/App_Plugins/ApplicationBlocks/tom-select.css", "/App_Plugins/ApplicationBlocks/runtime.css"], assets.Stylesheets);
    }
}
