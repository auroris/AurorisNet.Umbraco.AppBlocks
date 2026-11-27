using System.Net.Http.Json;
using AppBlocks.Api;
using AppBlocks.Connections;
using AppBlocks.DataSources;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;

namespace AppBlocks.Tests;

/// <summary>Who publishes and who last wrote the node, for <see cref="PublishAuthorizationMatrixTests"/>.</summary>
public enum Publisher
{
    /// <summary>Through the Management API, signed in as a User in Umbraco's Administrators group.</summary>
    Administrator,
    /// <summary>Through the Management API, signed in as a User in Umbraco's Editors group only.</summary>
    Editor,
    /// <summary>Through the Management API, as an editor also in a group <c>AppBlocks:AdministratorGroups</c> names.</summary>
    Operator,
    /// <summary>With nobody signed in, as background work publishes, the node last saved by each kind of account.</summary>
    NoUserAdministratorWrote, NoUserEditorWrote, NoUserOperatorWrote, NoUserSuperUserWrote,
    /// <summary>Released by <see cref="IContentService.PerformScheduledPublish"/> after an editor saved it with a release date.</summary>
    ScheduledAfterEditorSaved,
}

/// <summary>
/// The publish rule for nodes that carry SQL, through Umbraco's real publish and the real validators, as a table: each kind of node,
/// each way of publishing it. A guarded kind publishes only for an AppBlocks administrator, or with nobody signed in when an
/// administrator or the super user last wrote it; a control kind publishes for everyone. A new kind is one row of <see cref="Kinds"/>.
/// Report definitions and report blocks have the same table in the Interactive Reports tests.
/// </summary>
[Collection("Standalone Umbraco")]
public sealed class PublishAuthorizationMatrixTests(PublishAuthorizationMatrixTests.Site fixture) : IClassFixture<PublishAuthorizationMatrixTests.Site>
{
    /// <param name="OnlyAdministrators">The refusal a signed-in editor gets; null for a control kind, which anyone may publish.</param>
    /// <param name="Draft">A new node of the kind, its values set and not yet saved, under the data sources root.</param>
    public sealed record Kind(string Name, string? OnlyAdministrators, Func<IContentService, IContent, string, IContent> Draft);

    public static readonly Kind[] Kinds = [
        new("sqlDataSource", "Only AppBlocks administrators may publish SQL data sources.", (contents, root, slug) => Node(contents, root, DataSourceSchema.SqlAlias, slug,
            ("alias", "matrix." + slug), ("access", "Public"), ("connection", ConnectionCatalog.ApplicationConnection), ("source", "ORDERS"), ("keyColumns", "ORDER_ID"))),
        new("apiEndpoint", "Only AppBlocks administrators may publish API endpoints.", (contents, root, slug) => Node(contents, root, ApiEndpointCatalog.Alias, slug,
            ("alias", "matrix." + slug), ("route", "matrix/" + slug), ("access", "Public"), ("sql", "SELECT ORDER_ID, CUSTOMER FROM ORDERS"))),
        new("listOfValues with its own SQL", "Only AppBlocks administrators may publish a list of values with its own SQL; select a published data source instead.",
            (contents, root, slug) => Node(contents, root, PublishedDataCatalog.ListAlias, slug, ("alias", "matrix." + slug), ("connection", ConnectionCatalog.ApplicationConnection),
                ("sql", "SELECT ORDER_ID, CUSTOMER FROM ORDERS"), ("valueColumn", "ORDER_ID"), ("displayColumn", "CUSTOMER"))),
        // Control: a list that only selects a published data source carries no SQL of its own.
        new("listOfValues selecting a data source", null, (contents, root, slug) => Node(contents, root, PublishedDataCatalog.ListAlias, slug,
            ("alias", "matrix." + slug), ("dataSource", "orders.lines"), ("valueColumn", "LINE_ID"), ("displayColumn", "ITEM"))),
    ];

    public static TheoryData<string, Publisher> Cells()
    {
        var cells = new TheoryData<string, Publisher>();
        foreach (var kind in Kinds) foreach (var publisher in Enum.GetValues<Publisher>()) cells.Add(kind.Name, publisher);
        return cells;
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public async Task A_node_that_carries_SQL_publishes_only_for_an_AppBlocks_administrator(string kindName, Publisher publisher)
    {
        var kind = Kinds.Single(k => k.Name == kindName);
        var slug = (kindName.Split(' ')[0] + "-" + kindName.Length + "-" + publisher).ToLowerInvariant();
        var (published, messages, name) = await fixture.PublishAsync(kind, slug, publisher);
        var refusal = Expected(kind, publisher, name);
        Assert.True(published == (refusal is null), $"{kindName} by {publisher}: expected {(refusal is null ? "published" : "refused")}, got {(published ? "published" : "refused")}. {messages}");
        if (refusal is not null) Assert.Contains(refusal, messages);
    }

    /// <summary>The refusal a cell expects, or null when it publishes.</summary>
    private static string? Expected(Kind kind, Publisher publisher, string name) => kind.OnlyAdministrators is null ? null : publisher switch {
        Publisher.Editor => kind.OnlyAdministrators,
        Publisher.NoUserEditorWrote or Publisher.ScheduledAfterEditorSaved => name + " was last saved by someone who is not an AppBlocks administrator, so an AppBlocks administrator must publish it.",
        _ => null,
    };

    private static IContent Node(IContentService contents, IContent root, string type, string name, params (string Alias, string Value)[] values)
    {
        var node = contents.Create(name, root.Id, type);
        foreach (var (alias, value) in values) node.SetValue(alias, value);
        return node;
    }

    /// <summary>One site for the whole table, with an administrator, an editor, and an editor in a group <c>AppBlocks:AdministratorGroups</c> names.</summary>
    public sealed class Site : IAsyncLifetime
    {
        private const string Operators = "siteoperators";
        private const string Password = "Matrix-Password-2026!a";
        // Umbraco's super user; its id constant is obsolete in favour of its key, which a write still cannot take.
        private const int SuperUserId = -1;
        private BlockSite site = null!;
        private IUser administrator = null!, editor = null!, operatorUser = null!;
        private HttpClient administratorApi = null!, editorApi = null!, operatorApi = null!;

        public async ValueTask InitializeAsync()
        {
            site = await BlockSite.BootAsync(configure: builder => builder.Configuration["AppBlocks:AdministratorGroups:0"] = Operators);
            await site.PublishAsync();
            using (var scope = site.CreateScope())
            {
                var services = scope.ServiceProvider;
                var group = new UserGroup(services.GetRequiredService<IShortStringHelper>()) { Alias = Operators, Name = "Site operators" };
                Assert.True((await services.GetRequiredService<IUserGroupService>().CreateAsync(group, Constants.Security.SuperUserKey, [])).Success);
                var users = services.GetRequiredService<IUserService>();
                async Task<IUser> Create(string name, params Guid[] groups)
                {
                    var created = await users.CreateAsync(Constants.Security.SuperUserKey, new UserCreateModel {
                        UserName = name, Email = name + "@example.invalid", Name = name, Kind = UserKind.Default, UserGroupKeys = groups.ToHashSet(),
                    }, approveUser: true);
                    Assert.True(created.Success, created.Status.ToString());
                    return created.Result.CreatedUser!;
                }
                administrator = await Create("matrix-administrator", Constants.Security.AdminGroupKey);
                editor = await Create("matrix-editor", Constants.Security.EditorGroupKey);
                operatorUser = await Create("matrix-operator", Constants.Security.EditorGroupKey, group.Key);
            }
            foreach (var user in new[] { administrator, editor, operatorUser }) await site.SetPasswordAsync(user, Password);
            administratorApi = await site.SignInManagementApiAsync(administrator.Username, Password);
            editorApi = await site.SignInManagementApiAsync(editor.Username, Password);
            operatorApi = await site.SignInManagementApiAsync(operatorUser.Username, Password);
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var client in new[] { administratorApi, editorApi, operatorApi }) client?.Dispose();
            await site.DisposeAsync();
        }

        /// <summary>Saves a new node of the kind as the publisher's writer and publishes it their way: whether it ended up published, and every message the publish gave.</summary>
        public async Task<(bool Published, string Messages, string Name)> PublishAsync(Kind kind, string slug, Publisher publisher)
        {
            Guid key;
            string name;
            string messages;
            using (var scope = site.CreateScope())
            {
                var contents = scope.ServiceProvider.GetRequiredService<IContentService>();
#pragma warning disable CS0618
                var root = contents.GetRootContent().Single(c => c.ContentType.Alias == DataSourceSchema.RootAlias);
                var node = kind.Draft(contents, root, slug);
                key = node.Key; name = node.Name!;
                var writer = publisher switch {
                    Publisher.Administrator or Publisher.NoUserAdministratorWrote => administrator.Id,
                    Publisher.Operator or Publisher.NoUserOperatorWrote => operatorUser.Id,
                    Publisher.NoUserSuperUserWrote => SuperUserId,
                    _ => editor.Id,
                };
                if (publisher == Publisher.ScheduledAfterEditorSaved)
                {
                    Assert.True(contents.Save(node, writer, ContentScheduleCollection.CreateWithEntry(DateTime.Now.AddMinutes(-1), null)).Success);
                    var released = contents.PerformScheduledPublish(DateTime.Now).Where(result => result.Content?.Key == key).ToArray();
                    Assert.NotEmpty(released);
                    messages = string.Join("; ", released.SelectMany(result => result.EventMessages?.GetAll() ?? []).Select(m => m.Message));
                }
                else
                {
                    Assert.True(contents.Save(node, writer).Success);
                    if (publisher is Publisher.Administrator or Publisher.Editor or Publisher.Operator)
                        messages = await PublishThroughApiAsync(publisher switch { Publisher.Administrator => administratorApi, Publisher.Editor => editorApi, _ => operatorApi }, key);
                    else
                    {
                        // Nobody is signed in on this thread, as for background work.
                        var result = contents.Publish(node, ["*"], writer);
                        messages = string.Join("; ", result.EventMessages?.GetAll().Select(m => m.Message) ?? []);
                    }
                }
#pragma warning restore CS0618
            }
            using var check = site.CreateScope();
            return (check.ServiceProvider.GetRequiredService<IContentService>().GetById(key)!.Published, messages, name);
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
