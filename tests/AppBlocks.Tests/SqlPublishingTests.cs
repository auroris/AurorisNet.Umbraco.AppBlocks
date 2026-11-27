using AppBlocks.Authentication;
using AppBlocks.Connections;
using AppBlocks.DataSources;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using static AppBlocks.Tests.TestStubs;

namespace AppBlocks.Tests;

/// <summary>Who may publish SQL: a backoffice administrator, or, with nobody signed in, a node an administrator or the super user last wrote.</summary>
[Collection("Standalone Umbraco")]
public sealed class SqlPublishingTests
{
    private const string Refused = "Only administrators.";

    [Fact]
    public void A_publish_with_no_current_user_is_judged_by_the_nodes_last_writer()
    {
        const int superUserId = -1;
        var administrator = User(Constants.Security.AdminGroupAlias);
        var editor = User("editor");
        // The super user is judged by its key, whatever groups it is in.
        var superUser = User("editor", Constants.Security.SuperUserKey);
        var users = Stub<IUserService>((method, args) => method.Name == "GetUserById"
            ? (int)args![0]! switch { superUserId => superUser, 7 => administrator, 8 => editor, _ => null } : throw new NotImplementedException(method.Name));
        var access = new ApplicationAccessPolicy(users);
        string? Refusal(IUser? current, int writer) => access.Refusal(Security(current), Node(writer), Refused);
        // A signed-in User is judged as before, whoever last wrote the node.
        Assert.Null(Refusal(administrator, 8));
        Assert.Equal(Refused, Refusal(editor, superUserId));
        // Nobody signed in, as for a scheduled release or startup work: the writer decides.
        Assert.Null(Refusal(null, superUserId));
        Assert.Null(Refusal(null, 7));
        Assert.Contains("an AppBlocks administrator must publish it", Refusal(null, 8));
        Assert.Contains("an AppBlocks administrator must publish it", Refusal(null, 99));
        Assert.Contains("an AppBlocks administrator must publish it", access.Refusal(null, Node(8), Refused));
        // Without the policy, as in a unit test without a site, nothing is judged.
        Assert.Null(((ApplicationAccessPolicy?)null).Refusal(Security(editor), Node(8), Refused));
    }

    [Fact]
    public async Task Inline_list_SQL_and_data_sources_last_written_by_an_editor_are_not_published_without_an_administrator()
    {
        await using var site = await BlockSite.BootAsync();
        await site.PublishAsync();
        var editor = await site.CreateUserAsync("sql-editor", "sql-editor@example.invalid", "SQL editor");
        IUser administrator;
        using (var scope = site.CreateScope())
        {
            var created = await scope.ServiceProvider.GetRequiredService<IUserService>().CreateAsync(Constants.Security.SuperUserKey, new UserCreateModel {
                UserName = "sql-admin", Email = "sql-admin@example.invalid", Name = "SQL administrator", Kind = UserKind.Default,
                UserGroupKeys = new HashSet<Guid> { Constants.Security.AdminGroupKey },
            }, approveUser: true);
            Assert.True(created.Success, created.Status.ToString());
            administrator = created.Result.CreatedUser!;
        }
        using (var scope = site.CreateScope())
        {
            var contents = scope.ServiceProvider.GetRequiredService<IContentService>();
#pragma warning disable CS0618
            var sources = contents.GetRootContent().Single(content => content.ContentType.Alias == DataSourceSchema.RootAlias);
            IContent List(string alias, int writer, string? dataSource = null)
            {
                var list = contents.Create(alias, sources.Id, PublishedDataCatalog.ListAlias);
                list.SetValue("alias", alias);
                if (dataSource is null) { list.SetValue("connection", ConnectionCatalog.ApplicationConnection); list.SetValue("sql", "SELECT ORDER_ID, CUSTOMER FROM ORDERS"); list.SetValue("valueColumn", "ORDER_ID"); list.SetValue("displayColumn", "CUSTOMER"); }
                else { list.SetValue("dataSource", dataSource); list.SetValue("valueColumn", "LINE_ID"); list.SetValue("displayColumn", "ITEM"); }
                Assert.True(contents.Save(list, writer).Success);
                return list;
            }
            string Messages(PublishResult result) => string.Join("; ", result.EventMessages?.GetAll().Select(m => m.Message) ?? []);

            // Nobody is signed in here, as in a background publish.
            var editorsSql = contents.Publish(List("lists.editor", editor.Id), ["*"], editor.Id);
            Assert.False(editorsSql.Success);
            Assert.Contains("an AppBlocks administrator must publish it", Messages(editorsSql));
            var picked = contents.Publish(List("lists.picked", editor.Id, dataSource: "orders.lines"), ["*"], editor.Id);
            Assert.True(picked.Success, Messages(picked));
            var administratorsSql = contents.Publish(List("lists.administrator", administrator.Id), ["*"], administrator.Id);
            Assert.True(administratorsSql.Success, Messages(administratorsSql));
            var startup = List("lists.startup", Constants.Security.SuperUserId);
            Assert.True(contents.Publish(startup, ["*"]).Success);

            // A scheduled release commits as the node's last writer, so an editor cannot schedule SQL past the check.
            var scheduledList = List("lists.scheduled", editor.Id);
            var source = contents.Create("Scheduled source", sources.Id, DataSourceSchema.SqlAlias);
            source.SetValue("access", "Public"); source.SetValue("alias", "orders.scheduled");
            source.SetValue("connection", ConnectionCatalog.ApplicationConnection); source.SetValue("source", "ORDERS"); source.SetValue("keyColumns", "ORDER_ID");
            Assert.True(contents.Save(source, editor.Id).Success);
            foreach (var node in new[] { scheduledList, source })
                Assert.True(contents.Save(node, editor.Id, ContentScheduleCollection.CreateWithEntry(DateTime.Now.AddMinutes(-1), null)).Success);
            var released = contents.PerformScheduledPublish(DateTime.Now).ToArray();
            foreach (var node in new[] { scheduledList, source })
                Assert.Contains(released, result => result.Content?.Key == node.Key && !result.Success && Messages(result).Contains("an AppBlocks administrator must publish it"));
            Assert.False(contents.GetById(scheduledList.Key)!.Published);
            Assert.False(contents.GetById(source.Key)!.Published);
#pragma warning restore CS0618
        }
    }

    private static IUser User(string group, Guid? key = null)
    {
        IReadOnlyUserGroup[] groups = [Stub<IReadOnlyUserGroup>((method, _) => method.Name == "get_Alias" ? group : throw new NotImplementedException(method.Name))];
        var id = key ?? Guid.NewGuid();
        return Stub<IUser>((method, _) => method.Name switch {
            "get_Key" => id, "get_IsApproved" => true, "get_IsLockedOut" => false, "get_Groups" => groups, _ => throw new NotImplementedException(method.Name),
        });
    }

    private static IContent Node(int writer) => Stub<IContent>((method, _) => method.Name switch {
        "get_WriterId" => writer, "get_Name" => "Orders", _ => throw new NotImplementedException(method.Name),
    });

    private static IBackOfficeSecurityAccessor Security(IUser? current)
    {
        var security = Stub<IBackOfficeSecurity>((method, _) => method.Name == "get_CurrentUser" ? current : throw new NotImplementedException(method.Name));
        return Stub<IBackOfficeSecurityAccessor>((method, _) => method.Name == "get_BackOfficeSecurity" ? security : throw new NotImplementedException(method.Name));
    }
}
