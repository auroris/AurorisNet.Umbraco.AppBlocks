using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace AppBlocks.InteractiveReports.Tests;

/// <summary>A saved view shows its report's rows, so reading, changing or deleting one asks whether the caller may read the report now.</summary>
[Collection("Standalone Umbraco")]
public sealed class SavedViewAccessSiteTests
{
    private const string Report = "writers-only-rows";

    [Fact]
    public async Task Saved_views_of_a_specific_groups_report_follow_who_may_read_the_report()
    {
        await using var site = await PublicSite.StartAsync();
        await site.WithServices(async services => {
            var writers = (await services.GetRequiredService<IMemberGroupService>().GetAllAsync()).Single(g => g.Name == PublicSite.WritersGroup);
            var contents = services.GetRequiredService<IContentService>();
            var root = contents.GetRootContent().Single(c => c.ContentType.Alias == "dataSources");
            var node = contents.Create(Report, root.Id, DataSources.PublishedReportCatalog.ReportAlias);
            node.SetValue("alias", Report); node.SetValue("connection", AppBlocks.Connections.ConnectionCatalog.ApplicationConnection);
            node.SetValue("sql", "SELECT id, title FROM PUBLIC_ROWS");
            node.SetValue("access", DataSources.PublishedReportCatalog.SpecificGroups); node.SetValue("accessGroups", writers.Key.ToString());
            Assert.True(contents.Save(node).Success);
#pragma warning disable CS0618
            var published = contents.Publish(node, ["*"]);
#pragma warning restore CS0618
            Assert.True(published.Success, string.Join("; ", published.EventMessages?.GetAll().Select(m => m.Message) ?? []));
        });
        var writer = await Member(site, "writer", PublicSite.WritersGroup);
        var reviewer = await Member(site, "reviewer", PublicSite.ReviewersGroup);
        using var writing = await site.SignedIn("member", writer.Id.ToString(), writer.Username);
        using var reviewing = await site.SignedIn("member", reviewer.Id.ToString(), reviewer.Username);
        using var admin = await site.Administrator();

        var own = await Save(writing, "/api/reports", "Writer's view", isGlobal: false);
        var shared = await Save(admin, "/umbraco/reports", "Everyone's view", isGlobal: true);
        foreach (var id in new[] { own, shared })
            Assert.Equal(HttpStatusCode.OK, (await writing.GetAsync($"/api/reports/{Report}/{id}")).StatusCode);
        // Someone outside the report's groups reads none of its views, the shared one included.
        Assert.False((await reviewing.GetAsync($"/api/reports/{Report}/{shared}")).IsSuccessStatusCode);

        // A writer removed from the group keeps no way back in through the views they saved.
        await site.WithServices(services => {
#pragma warning disable CS0618
            services.GetRequiredService<IMemberService>().DissociateRole(writer.Id, PublicSite.WritersGroup);
#pragma warning restore CS0618
            return Task.CompletedTask;
        });
        Assert.False((await writing.GetAsync($"/api/reports/{Report}/schema")).IsSuccessStatusCode);
        foreach (var id in new[] { own, shared })
            Assert.False((await writing.GetAsync($"/api/reports/{Report}/{id}")).IsSuccessStatusCode);
        Assert.False((await writing.DeleteAsync($"/api/reports/{own}")).IsSuccessStatusCode);
        // An administrator still reads and removes them.
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/umbraco/reports/{Report}/{own}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/umbraco/reports/{own}")).StatusCode);
    }

    private static async Task<IMember> Member(PublicSite site, string name, string group)
    {
        IMember? created = null;
        await site.WithServices(services => {
            var members = services.GetRequiredService<IMemberService>();
            var username = name + "-" + Guid.NewGuid().ToString("N")[..8];
            created = members.CreateMemberWithIdentity(username, username + "@example.invalid", username, "Member", isApproved: true);
            members.Save(created);
#pragma warning disable CS0618
            members.AssignRole(created.Id, group);
#pragma warning restore CS0618
            return Task.CompletedTask;
        });
        return created!;
    }

    private static async Task<long> Save(HttpClient client, string route, string title, bool isGlobal)
    {
        using var response = await client.PostAsJsonAsync($"{route}/{Report}/saved", new { title, isGlobal, state = new { page = new { index = 1, size = 10 } } });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"{route}: {response.StatusCode} {body}");
        return JsonSerializer.Deserialize<JsonElement>(body).GetProperty("id").GetInt64();
    }
}
