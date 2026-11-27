using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AppBlocks.Blocks;
using AppBlocks.Connections;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Services;

namespace AppBlocks.InteractiveReports.Tests;

/// <summary>Who publishes and who last wrote the node, for <see cref="ReportPublishAuthorizationMatrixTests"/>.</summary>
public enum ReportPublisher
{
    /// <summary>Through the Management API, signed in as a User in Umbraco's Administrators group.</summary>
    Administrator,
    /// <summary>Through the Management API, signed in as a User in Umbraco's Editors group only.</summary>
    Editor,
    /// <summary>With nobody signed in, as background work publishes, the node last saved by each kind of account.</summary>
    NoUserAdministratorWrote, NoUserEditorWrote, NoUserSuperUserWrote,
    /// <summary>Released by <see cref="IContentService.PerformScheduledPublish"/> after an editor saved it with a release date.</summary>
    ScheduledAfterEditorSaved,
}

/// <summary>
/// The publish rule for what decides what a report runs, through Umbraco's real publish and the real validators, as a table: report
/// definitions, and pages whose Interactive Report source is added or changed, each way of publishing them. Pages whose report source
/// stays as published, and pages with no report, are the controls anyone may publish. A new kind is one row of <see cref="Kinds"/>.
/// The SQL data source, API endpoint and list of values rows are in AppBlocks.Tests' PublishAuthorizationMatrixTests.
/// </summary>
[Collection("Standalone Umbraco")]
public sealed class ReportPublishAuthorizationMatrixTests(ReportPublishAuthorizationMatrixTests.Site fixture) : IClassFixture<ReportPublishAuthorizationMatrixTests.Site>
{
    /// <param name="OnlyAdministrators">The refusal a signed-in editor gets; null for a control kind, which anyone may publish.</param>
    /// <param name="Draft">A node of the kind, its draft values set and not yet saved; a page's earlier version is already published by the super user.</param>
    public sealed record Kind(string Name, string? OnlyAdministrators, Func<IContentService, string, IContent> Draft);

    private const string ChangeReports = "Only AppBlocks administrators may add or change Interactive Report data sources.";
    private static readonly Guid Home = AppBlockSchema.Key("public-site:home");

    public static readonly Kind[] Kinds = [
        new("reportDefinition", "Only AppBlocks administrators may publish report definitions.", (contents, slug) => {
            var root = contents.GetRootContent().Single(c => c.ContentType.Alias == "dataSources");
            var node = contents.Create(slug, root.Id, DataSources.PublishedReportCatalog.ReportAlias);
            node.SetValue("alias", "matrix-" + slug); node.SetValue("connection", ConnectionCatalog.ApplicationConnection);
            node.SetValue("sql", "SELECT id, title FROM PUBLIC_ROWS");
            return node;
        }),
        new("page adding a report", ChangeReports, (contents, slug) => Page(contents, slug, published: null, draft: Report(slug, Table))),
        new("page changing its report source", ChangeReports, (contents, slug) => Page(contents, slug, published: Report(slug, Table), draft: Report(slug, Sql))),
        // Controls: a report whose source is as published, and a page with no report at all.
        new("page keeping its report source", null, (contents, slug) => Page(contents, slug, published: Report(slug, Table), draft: Report(slug, Table, heading: "Renamed rows"))),
        new("page without a report", null, (contents, slug) => Page(contents, slug, published: null, draft: Html(slug))),
    ];

    public static TheoryData<string, ReportPublisher> Cells()
    {
        var cells = new TheoryData<string, ReportPublisher>();
        foreach (var kind in Kinds) foreach (var publisher in Enum.GetValues<ReportPublisher>()) cells.Add(kind.Name, publisher);
        return cells;
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public async Task What_decides_what_a_report_runs_publishes_only_for_an_AppBlocks_administrator(string kindName, ReportPublisher publisher)
    {
        var kind = Kinds.Single(k => k.Name == kindName);
        var slug = (string.Join("-", kindName.Split(' ')) + "-" + publisher).ToLowerInvariant();
        var (published, messages, name) = await fixture.PublishAsync(kind, slug, publisher);
        var refusal = kind.OnlyAdministrators is null ? null : publisher switch {
            ReportPublisher.Editor => kind.OnlyAdministrators,
            ReportPublisher.NoUserEditorWrote or ReportPublisher.ScheduledAfterEditorSaved => name + " was last saved by someone who is not an AppBlocks administrator, so an AppBlocks administrator must publish it.",
            _ => null,
        };
        Assert.True(published == (refusal is null), $"{kindName} by {publisher}: expected {(refusal is null ? "published" : "refused")}, got {(published ? "published" : "refused")}. {messages}");
        if (refusal is not null) Assert.Contains(refusal, messages);
    }

    private static readonly string Table = new JsonObject { ["kind"] = "table", ["connection"] = ConnectionCatalog.ApplicationConnection, ["source"] = "PUBLIC_ROWS" }.ToJsonString();
    private static readonly string Sql = new JsonObject { ["kind"] = "sql", ["connection"] = ConnectionCatalog.ApplicationConnection, ["source"] = "SELECT id, title FROM PUBLIC_ROWS" }.ToJsonString();

    /// <summary>A page under the site's home: <paramref name="published"/> published first by the super user, when there is one, then <paramref name="draft"/> set.</summary>
    private static IContent Page(IContentService contents, string slug, JsonObject? published, JsonObject draft)
    {
#pragma warning disable CS0618
        var page = contents.Create(slug, contents.GetById(Home)!.Id, "publicPage");
        if (published is not null) page.SetValue("content", published.ToJsonString());
        Assert.True(contents.Save(page).Success);
        var result = contents.Publish(page, ["*"]);
        Assert.True(result.Success, string.Join("; ", result.EventMessages?.GetAll().Select(m => m.Message) ?? []));
#pragma warning restore CS0618
        page.SetValue("content", draft.ToJsonString());
        return page;
    }

    /// <summary>One report block, keyed by the page so its draft and published versions are the same block.</summary>
    private static JsonObject Report(string slug, string source, string heading = "Rows")
        => Blocks(AppBlockSchema.Key("matrix-report:" + slug), "appReport", ("heading", heading), ("dataSource", source));

    private static JsonObject Html(string slug) => Blocks(AppBlockSchema.Key("matrix-html:" + slug), AppBlockSchema.HtmlAlias, ("html", "<p>No report here</p>"));

    private static JsonObject Blocks(Guid key, string alias, params (string Alias, string Value)[] values) => new() {
        ["layout"] = new JsonObject { ["Umbraco.BlockList"] = new JsonArray(new JsonObject { ["contentKey"] = key.ToString(), ["settingsKey"] = null }) },
        ["contentData"] = new JsonArray(new JsonObject {
            ["key"] = key.ToString(), ["contentTypeKey"] = AppBlockSchema.Key(alias).ToString(),
            ["values"] = new JsonArray(values.Select(v => (JsonNode)new JsonObject { ["alias"] = v.Alias, ["value"] = v.Value, ["culture"] = null, ["segment"] = null }).ToArray()),
        }),
        ["settingsData"] = new JsonArray(),
        ["expose"] = new JsonArray(new JsonObject { ["contentKey"] = key.ToString(), ["culture"] = null, ["segment"] = null }),
    };

    /// <summary>One site for the whole table, with an administrator and an editor signed in to the Management API.</summary>
    public sealed class Site : IAsyncLifetime
    {
        // Umbraco's super user; its id constant is obsolete in favour of its key, which a write still cannot take.
        private const int SuperUserId = -1;
        private PublicSite site = null!;
        private IUser administrator = null!, editor = null!;
        private HttpClient administratorApi = null!, editorApi = null!;

        public async ValueTask InitializeAsync()
        {
            site = await PublicSite.StartAsync();
            await site.WithServices(async services => {
                var users = services.GetRequiredService<IUserService>();
                async Task<IUser> Create(string email, Guid group)
                {
                    var created = await users.CreateAsync(Constants.Security.SuperUserKey, new UserCreateModel {
                        UserName = email, Email = email, Name = email, Kind = UserKind.Default, UserGroupKeys = new HashSet<Guid> { group },
                    }, approveUser: true);
                    Assert.True(created.Success, created.Status.ToString());
                    return created.Result.CreatedUser!;
                }
                administrator = await Create("matrix.administrator@example.test", Constants.Security.AdminGroupKey);
                editor = await Create("matrix.editor@example.test", Constants.Security.EditorGroupKey);
            });
            await site.SetUserPassword(administrator.Key);
            await site.SetUserPassword(editor.Key);
            administratorApi = await site.Administrator(administrator.Email);
            editorApi = await site.Administrator(editor.Email);
        }

        public async ValueTask DisposeAsync()
        {
            administratorApi?.Dispose(); editorApi?.Dispose();
            await site.DisposeAsync();
        }

        /// <summary>Saves the kind's draft as the publisher's writer and publishes it their way: whether it ended up published, and every message the publish gave.</summary>
        public async Task<(bool Published, string Messages, string Name)> PublishAsync(Kind kind, string slug, ReportPublisher publisher)
        {
            Guid key = Guid.Empty;
            string name = "", messages = "";
            await site.WithServices(async services => {
                var contents = services.GetRequiredService<IContentService>();
#pragma warning disable CS0618
                var node = kind.Draft(contents, slug);
                key = node.Key; name = node.Name!;
                var writer = publisher switch {
                    ReportPublisher.Administrator or ReportPublisher.NoUserAdministratorWrote => administrator.Id,
                    ReportPublisher.NoUserSuperUserWrote => SuperUserId,
                    _ => editor.Id,
                };
                if (publisher == ReportPublisher.ScheduledAfterEditorSaved)
                {
                    Assert.True(contents.Save(node, writer, ContentScheduleCollection.CreateWithEntry(DateTime.Now.AddMinutes(-1), null)).Success);
                    var released = contents.PerformScheduledPublish(DateTime.Now).Where(result => result.Content?.Key == key).ToArray();
                    Assert.NotEmpty(released);
                    messages = string.Join("; ", released.SelectMany(result => result.EventMessages?.GetAll() ?? []).Select(m => m.Message));
                }
                else
                {
                    Assert.True(contents.Save(node, writer).Success);
                    if (publisher is ReportPublisher.Administrator or ReportPublisher.Editor)
                        messages = await PublishThroughApiAsync(publisher == ReportPublisher.Administrator ? administratorApi : editorApi, key);
                    else
                    {
                        // Nobody is signed in on this thread, as for background work.
                        var result = contents.Publish(node, ["*"], writer);
                        messages = string.Join("; ", result.EventMessages?.GetAll().Select(m => m.Message) ?? []);
                    }
                }
#pragma warning restore CS0618
            });
            var published = false;
            // A page's earlier version was published, so what counts is whether the draft became the published version.
            await site.WithServices(services => {
                var node = services.GetRequiredService<IContentService>().GetById(key)!;
                published = node.Published && !node.Edited;
                return Task.CompletedTask;
            });
            return (published, messages, name);
        }

        /// <summary>Publishes as the backoffice does, so the signed-in User is the current user; the answer's body and notification headers hold the messages.</summary>
        private static async Task<string> PublishThroughApiAsync(HttpClient client, Guid key)
        {
            using var response = await client.PutAsJsonAsync($"/umbraco/management/api/v1/document/{key}/publish", new { publishSchedules = new[] { new { culture = (string?)null } } });
            var headers = response.Headers.Concat(response.Content.Headers).SelectMany(header => header.Value.Select(value => header.Key + ": " + value));
            return $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()} {string.Join(" ", headers)}";
        }
    }
}
