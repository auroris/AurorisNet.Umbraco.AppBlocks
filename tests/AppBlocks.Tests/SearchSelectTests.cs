using System.Net;
using AppBlocks.Blocks;
using AppBlocks.DataSources;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Services;

namespace AppBlocks.Tests;

[Collection("Standalone Umbraco")]
public sealed class SearchSelectTests
{
    [Fact]
    public async Task Published_single_column_list_supplies_and_validates_both_selection_modes()
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
            }
            BlockPages.SetValue(many, AppBlockField.MultipleAlias, "1");
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
        Assert.Equal(2, html.Split("data-app-search-select").Length - 1);
        Assert.Equal(2, html.Split("<option value=\"A,B\">A,B</option>").Length - 1);
        Assert.Contains("data-kind=\"csv\"", html);
        var form = new FormsClient(client, "", pageKey, formKey);
        Assert.True((await form.LoadAsync()).IsSuccess);
        Task<FormResponse> Create(string customer, string notes) => form.CreateAsync(new { customer, notes, region = "WEST", status = "NEW", amount = 1, orderDate = "2026-01-01" });
        const string csv = "Oil,\"A,B\"";
        var created = await Create(csv, "A,B");
        Assert.True(created.IsSuccess, created.Body.ToString());
        Assert.Equal(csv, (await form.LoadAsync(created.RecordKey)).Values.GetProperty("customer").GetString());
        foreach (var invalid in new[] { "Oil,Oil", "Oil,Missing", "Oil,", "" })
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Create(invalid, "A,B")).Response.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Create(csv, "Oil,Bearing")).Response.StatusCode);
    }

    [Fact]
    public void Only_a_single_selection_can_bind_to_a_numeric_field()
    {
        Assert.True(AppBlocksValidator.Compatible(AppBlockSchema.SearchSelectAlias, new("id", "Id", "number"), true));
        Assert.False(AppBlocksValidator.Compatible(AppBlockSchema.SearchSelectAlias, new("ids", "Ids", "number"), true, true));
    }
}
