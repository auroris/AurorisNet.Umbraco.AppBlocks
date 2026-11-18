using System.Net;
using System.Text.Json;
using AppBlocks.InteractiveReports.Blocks;
using AppBlocks.InteractiveReports.Setup;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Services;

namespace AppBlocks.InteractiveReports.Tests;

/// <summary>
/// Who sees a report block's create button and open link, as its settings choose: on a real site, through the report's schema as
/// each kind of caller gets it.
/// </summary>
[Collection("Standalone Umbraco")]
public sealed class ReportLinkVisibilityTests
{
    [Theory]
    [InlineData(null, ReportLinkVisibility.FormPageDecides)]
    [InlineData("", ReportLinkVisibility.FormPageDecides)]
    [InlineData("Form page decides", ReportLinkVisibility.FormPageDecides)]
    [InlineData("Public", ReportLinkVisibility.Public)]
    [InlineData("Groups", ReportLinkVisibility.Groups)]
    [InlineData("Hidden", ReportLinkVisibility.Hidden)]
    [InlineData("Something else", ReportLinkVisibility.FormPageDecides)]
    public void A_stored_choice_reads_as_its_visibility(string? stored, ReportLinkVisibility expected)
        => Assert.Equal(expected, ReportLinkRule.Parse(stored));

    [Fact]
    public void A_rule_reads_its_three_properties()
    {
        var values = new Dictionary<string, string?> { ["openVisibility"] = "Groups", ["openUserGroups"] = " editor, admin,,Editor ", ["openMemberGroups"] = "7" };
        var open = ReportLinkRule.Read(ReportLinkRule.Open, values.GetValueOrDefault);
        Assert.Equal((ReportLinkVisibility.Groups, "7", true), (open.Visibility, open.MemberGroups, open.HasGroups));
        Assert.Equal(["editor", "admin"], open.UserGroups);
        var create = ReportLinkRule.Read(ReportLinkRule.Create, values.GetValueOrDefault);
        Assert.Equal((ReportLinkVisibility.FormPageDecides, 0, "", false), (create.Visibility, create.UserGroups.Count, create.MemberGroups, create.HasGroups));
    }

    [Fact]
    public async Task Each_link_is_shown_to_whom_its_setting_says()
    {
        await using var site = await PublicSite.StartAsync();
        var writer = await CreateMemberAsync(site, "writer", PublicSite.WritersGroup);
        var reviewer = await CreateMemberAsync(site, "reviewer", PublicSite.ReviewersGroup);
        var reader = await CreateMemberAsync(site, "reader");
        var editor = await CreateUserAsync(site, "reviewing-editor");
        using var writing = await site.SignedIn("member", writer.Id.ToString(), writer.Username);
        using var reviewing = await site.SignedIn("member", reviewer.Id.ToString(), reviewer.Username);
        using var reading = await site.SignedIn("member", reader.Id.ToString(), reader.Username);
        using var editing = await site.SignedIn("backoffice", editor.ToString(), "reviewing-editor");
        using var visitor = site.Anonymous();
        await CheckFormTargets(visitor, writing);
        var callers = new Dictionary<string, HttpClient> {
            ["writer"] = writing, ["reviewer"] = reviewing, ["member outside the groups"] = reading, ["backoffice editor"] = editing, ["anonymous"] = visitor,
        };
        // The data source lets only Writers add and change rows; Groups is the Reviewers member group and the backoffice Editors group.
        var shownTo = new Dictionary<string, string[]> {
            [""] = ["writer"], ["Form page decides"] = ["writer"], ["Public"] = [.. callers.Keys], ["Groups"] = ["reviewer", "backoffice editor"], ["Hidden"] = [],
        };
        foreach (var (link, property, other) in new[] { ("create", "createLink", "editLink"), ("open", "editLink", "createLink") })
            foreach (var choice in PublicSite.LinkChoices)
                foreach (var (who, client) in callers)
                {
                    var schema = await Schema(client, PublicSite.LinkReport(link, choice));
                    Assert.True(shownTo[choice].Contains(who) == Has(schema, property), $"{link} {choice}: {who}");
                    Assert.False(Has(schema, other), $"{link} {choice}: {who} sees the hidden link");
                    if (Has(schema, "createLink")) Assert.Equal("New row", schema.GetProperty("createLink").GetProperty("label").GetString());
                    if (Has(schema, "editLink")) Assert.Contains("?id={id}", schema.GetProperty("editLink").GetProperty("urlTemplate").GetString());
                }

        // The form agrees with Form page decides for each of them, refusing what the link left out.
        foreach (var (who, client) in callers)
        {
            var form = await Load(client, PublicSite.FormUrl(PublicSite.WritersPage, PublicSite.WritersForm));
            Assert.Equal(who == "writer", form.GetProperty("record").GetProperty("capabilities").GetProperty("create").GetBoolean());
        }

        // The answer belongs to each request: joining the group gives the links to someone who asked before.
        await site.WithServices(services => {
#pragma warning disable CS0618
            services.GetRequiredService<IMemberService>().AssignRole(reader.Id, PublicSite.WritersGroup);
#pragma warning restore CS0618
            return Task.CompletedTask;
        });
        Assert.True(Has(await Schema(reading, PublicSite.LinkReport("create", "")), "createLink"));
        Assert.True(Has(await Schema(reading, PublicSite.LinkReport("open", "")), "editLink"));

        // A form class decides through its Initialize: this one refuses anyone not signed in, so they get neither link. Everyone
        // else gets a new record that allows Create, so the button, and the open link, since the class answers them at all and
        // decides about each record as it loads, although that new record does not allow Update.
        var refusedSchema = await Schema(visitor, PublicSite.RefusingLinks);
        Assert.False(Has(refusedSchema, "createLink") || Has(refusedSchema, "editLink"));
        var signedInSchema = await Schema(reading, PublicSite.RefusingLinks);
        Assert.True(Has(signedInSchema, "createLink"));
        Assert.True(Has(signedInSchema, "editLink"));

        await A_block_published_without_its_key_column_omits_the_open_link(site, writing);
        await A_report_definition_set_to_specific_groups_admits_their_members(site, writing, reviewing);
    }

    private static async Task CheckFormTargets(HttpClient visitor, HttpClient writing)
    {
        var anchor = "#" + Forms.FormDefinition.Anchor(PublicSite.LinePanel);
        Task<JsonElement> LineLinks(Guid key) => Load(visitor, "/api/reports/" + PublicSite.ReportName(PublicSite.LinesPage, key) + "/schema?appPageQuery=%3ForderId%3D7%26lineId%3Dold");
        var local = await LineLinks(PublicSite.LocalLinks);
        Assert.Equal("/lines/?orderId=7" + anchor, local.GetProperty("createLink").GetProperty("url").GetString());
        Assert.Equal("/lines/?orderId=7&lineId={id}" + anchor, local.GetProperty("editLink").GetProperty("urlTemplate").GetString());
        Assert.Equal("event", local.GetProperty("createLink").GetProperty("mode").GetString());
        Assert.Equal("event", local.GetProperty("editLink").GetProperty("mode").GetString());
        var split = await LineLinks(PublicSite.SplitLinks);
        Assert.Equal("/contact/", split.GetProperty("createLink").GetProperty("url").GetString());
        Assert.Equal(local.GetProperty("editLink").GetProperty("urlTemplate").GetString(), split.GetProperty("editLink").GetProperty("urlTemplate").GetString());
        var picked = await Load(visitor, "/api/reports/" + PublicSite.ReportName(PublicSite.PickedPage, PublicSite.PickedLinks) + "/schema?appPageQuery=%3Fprivate%3D7");
        Assert.Equal("/lines/" + anchor, picked.GetProperty("createLink").GetProperty("url").GetString());
        Assert.Equal("/lines/?lineId={id}" + anchor, picked.GetProperty("editLink").GetProperty("urlTemplate").GetString());
        var rendered = await writing.GetStringAsync("/lines/");
        Assert.Contains("data-app-form-panel", rendered);
        Assert.Contains("data-form-panels=\"" + anchor[1..] + "\"", rendered);
    }

    /// <summary>A report definition node admits any signed-in account unless its Access is Specific groups, which admits the Members of its groups.</summary>
    private static async Task A_report_definition_set_to_specific_groups_admits_their_members(PublicSite site, HttpClient writer, HttpClient reviewer)
    {
        await site.WithServices(async services => {
            var writers = (await services.GetRequiredService<IMemberGroupService>().GetAllAsync()).Single(g => g.Name == PublicSite.WritersGroup);
            var contents = services.GetRequiredService<IContentService>();
            var root = contents.GetRootContent().Single(c => c.ContentType.Alias == "dataSources");
            foreach (var (alias, access, groups) in new[] { ("signed-in-rows", "", ""), ("writers-rows", DataSources.PublishedReportCatalog.SpecificGroups, writers.Key.ToString()) })
            {
                var node = contents.Create(alias, root.Id, DataSources.PublishedReportCatalog.ReportAlias);
                node.SetValue("alias", alias); node.SetValue("connection", AppBlocks.Connections.ConnectionCatalog.ApplicationConnection);
                node.SetValue("sql", "SELECT id, title FROM PUBLIC_ROWS");
                if (access.Length > 0) { node.SetValue("access", access); node.SetValue("accessGroups", groups); }
                Assert.True(contents.Save(node).Success);
#pragma warning disable CS0618
                var published = contents.Publish(node, ["*"]);
#pragma warning restore CS0618
                Assert.True(published.Success, alias + ": " + string.Join("; ", published.EventMessages?.GetAll().Select(m => m.Message) ?? []));
            }
        });
        foreach (var (client, report, allowed) in new[] { (writer, "signed-in-rows", true), (reviewer, "signed-in-rows", true), (writer, "writers-rows", true), (reviewer, "writers-rows", false) })
        {
            using var response = await client.GetAsync("/api/reports/" + report + "/schema");
            Assert.True(allowed == response.IsSuccessStatusCode, $"{report}: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }
    }

    /// <summary>
    /// Publishing refuses an Open link without a key column, but a block published before the setting existed can hold one: its
    /// report then answers without the Open link instead of failing.
    /// </summary>
    private static async Task A_block_published_without_its_key_column_omits_the_open_link(PublicSite site, HttpClient writer)
    {
        await site.WithServices(services => {
            var contents = services.GetRequiredService<IContentService>();
            var page = contents.GetById(PublicSite.LinksPage)!;
            var value = System.Text.Json.Nodes.JsonNode.Parse((string)page.GetValue("content")!)!;
            var values = value["contentData"]!.AsArray().Single(b => Guid.Parse((string)b!["key"]!) == PublicSite.LinkReport("create", PublicSite.LinkChoices[0]))!["values"]!.AsArray();
            values.Remove(values.Single(v => (string)v!["alias"]! == "keyColumn"));
            page.SetValue("content", value.ToJsonString());
            // Past the publish check, as content saved before it was: without notifications, then the cache refreshed by hand.
            using (var scope = services.GetRequiredService<Umbraco.Cms.Core.Scoping.ICoreScopeProvider>().CreateCoreScope())
            {
                using (scope.Notifications.Suppress())
                {
                    Assert.True(contents.Save(page).Success);
#pragma warning disable CS0618
                    Assert.True(contents.Publish(page, ["*"]).Success);
#pragma warning restore CS0618
                }
                scope.Complete();
            }
            Umbraco.Extensions.DistributedCacheExtensions.RefreshContentCache(services.GetRequiredService<Umbraco.Cms.Core.Cache.DistributedCache>(),
                [new Umbraco.Cms.Core.Services.Changes.TreeChange<IContent>(contents.GetById(PublicSite.LinksPage)!, Umbraco.Cms.Core.Services.Changes.TreeChangeTypes.RefreshNode)]);
            return Task.CompletedTask;
        });
        var schema = await Schema(writer, PublicSite.LinkReport("create", PublicSite.LinkChoices[0]));
        Assert.False(Has(schema, "editLink"));
        Assert.True(Has(schema, "createLink"));
    }

    private static bool Has(JsonElement schema, string link) => schema.TryGetProperty(link, out var value) && value.ValueKind != JsonValueKind.Null;

    private static Task<JsonElement> Schema(HttpClient client, Guid report)
        => Load(client, "/api/reports/" + PublicSite.ReportName(PublicSite.LinksPage, report) + "/schema");

    private static async Task<JsonElement> Load(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url}: {response.StatusCode} {body}");
        return JsonSerializer.Deserialize<JsonElement>(body);
    }

    private static async Task<IMember> CreateMemberAsync(PublicSite site, string name, string? group = null)
    {
        IMember? created = null;
        await site.WithServices(services => {
            var members = services.GetRequiredService<IMemberService>();
            var username = name + "-" + Guid.NewGuid().ToString("N")[..8];
            created = members.CreateMemberWithIdentity(username, username + "@example.invalid", username, "Member", isApproved: true);
            members.Save(created);
#pragma warning disable CS0618
            if (group is not null) members.AssignRole(created.Id, group);
#pragma warning restore CS0618
            return Task.CompletedTask;
        });
        return created!;
    }

    /// <summary>A backoffice User in Umbraco's Editors group, whose alias is the one Groups picks.</summary>
    private static async Task<Guid> CreateUserAsync(PublicSite site, string name)
    {
        var key = Guid.Empty;
        await site.WithServices(async services => {
            var username = name + "-" + Guid.NewGuid().ToString("N")[..8] + "@example.invalid";
            var result = await services.GetRequiredService<IUserService>().CreateAsync(Umbraco.Cms.Core.Constants.Security.SuperUserKey, new UserCreateModel {
                UserName = username, Email = username, Name = name, Kind = UserKind.Default, UserGroupKeys = new HashSet<Guid> { Umbraco.Cms.Core.Constants.Security.EditorGroupKey },
            }, approveUser: true);
            Assert.True(result.Success, result.Status.ToString());
            key = result.Result.CreatedUser!.Key;
        });
        return key;
    }
}
