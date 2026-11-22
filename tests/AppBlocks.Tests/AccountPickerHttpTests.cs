using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AppBlocks.AccountPickers;
using AppBlocks.Blocks;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Services;

namespace AppBlocks.Tests;

[Collection("Standalone Umbraco")]
public sealed class AccountPickerHttpTests
{
    [Fact]
    public async Task Published_scope_controls_lookup_and_save_and_the_form_reopens_with_canonical_values()
    {
        await using var site = await BlockSite.StartAsync();
        var member = site.CreateMember("picker.member", "member@example.invalid", "Picker Example");
        var retiring = site.CreateMember("picker.retiring", "retiring@example.invalid", "Retiring member");
        var user = await site.CreateUserAsync("picker.user", "user@example.invalid", "Picker Example");
        var memberKey = member.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var userKey = "backoffice:" + user.Key.ToString("D");
        var content = BlockPages.Empty();
        (Guid Form, Guid Picker) Add(string scope, bool bindName = true)
        {
            var picker = BlockPages.Add(content, UmbracoAccountPickerBlock.AliasName,
                ("identityField", "notes"), ("nameField", bindName ? "item" : ""), ("accountType", scope));
            var form = BlockPages.FormBlock(content, "orders.lines", [picker,
                BlockPages.Add(content, "appText", ("field", "notes")), BlockPages.Add(content, "appText", ("field", "item"))], []);
            BlockPages.Layout(content).Add(form);
            return (Guid.Parse(form["contentKey"]!.ToString()), Guid.Parse(picker["contentKey"]!.ToString()));
        }
        var members = Add("Members");
        var users = Add("Backoffice Users");
        var both = Add("Both", bindName: false);
        using var scope = site.CreateScope();
        var contents = scope.ServiceProvider.GetRequiredService<IContentService>();
#pragma warning disable CS0618
        var page = contents.GetById(BlockSite.FormPageKey)!;
        page.SetValue(BlockSite.BodyAlias, content.ToJsonString());
        Assert.True(contents.Save(page).Success);
        var published = contents.Publish(page, ["*"]);
        Assert.True(published.Success, string.Join(" ", published.EventMessages?.GetAll().Select(m => m.Message) ?? []));
#pragma warning restore CS0618
        string Url(Guid picker) => $"/api/account-picker/{page.Key}/{picker}";
        using var anonymous = site.CreateClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.GetAsync(Url(both.Picker) + "?s=Picker")).StatusCode);
        using var client = await site.SignInMemberAsync(member);
        var html = await client.GetStringAsync("/orders-form/");
        Assert.Contains("data-app-account-picker", html);
        Assert.Contains(Url(both.Picker), html);

        async Task<string[]> Keys(Guid picker, string query)
        {
            using var response = await client.GetAsync(Url(picker) + "?s=" + Uri.EscapeDataString(query));
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            Assert.True(response.Headers.CacheControl?.NoStore);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            return body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("key").GetString()!).ToArray();
        }
        Assert.Equal([memberKey], await Keys(members.Picker, "picker.member"));
        Assert.Equal([userKey], await Keys(users.Picker, "picker.user"));
        Assert.Equal(2, (await Keys(both.Picker, "Picker Example")).Distinct().Count());
        Assert.Single(await Keys(members.Picker, "picker.retiring"));
        retiring.IsApproved = false;
#pragma warning disable CS0618
        scope.ServiceProvider.GetRequiredService<IMemberService>().Save(retiring);
#pragma warning restore CS0618
        Assert.Empty(await Keys(members.Picker, "picker.retiring"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.GetAsync(Url(members.Picker) + "?key=" + userKey)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(Url(both.Picker) + "?s=Picker&key=" + memberKey)).StatusCode);

        var form = new FormsClient(client, "", page.Key, members.Form);
        var row = await form.LoadAsync("1");
        Assert.True(row.IsSuccess, row.Body.ToString());
        var wrongType = await form.UpdateAsync("1", row.RecordChecksum, new { notes = userKey, item = "Posted name" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrongType.Response.StatusCode);
        var retired = await form.UpdateAsync("1", row.RecordChecksum, new { notes = retiring.Id.ToString(), item = "Posted name" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, retired.Response.StatusCode);
        var linked = await form.UpdateAsync("1", row.RecordChecksum, new { notes = memberKey, item = "Posted name" });
        Assert.True(linked.IsSuccess, linked.Body.ToString());
        var reopened = await form.LoadAsync("1");
        Assert.Equal(memberKey, reopened.Values.GetProperty("notes").GetString());
        Assert.Equal(member.Name, reopened.Values.GetProperty("item").GetString());
        var unchanged = await form.UpdateAsync("1", reopened.RecordChecksum, new { item = "Changed in the browser" });
        Assert.Equal(member.Name, unchanged.Values.GetProperty("item").GetString());
        var missingName = await form.UpdateAsync("1", unchanged.RecordChecksum, new { notes = (string?)null, item = "Posted name" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, missingName.Response.StatusCode);
        var keyOnly = new FormsClient(client, "", page.Key, both.Form);
        row = await keyOnly.LoadAsync("1");
        var cleared = await keyOnly.UpdateAsync("1", row.RecordChecksum, new { notes = (string?)null });
        Assert.True(cleared.IsSuccess, cleared.Body.ToString());
        Assert.Equal(JsonValueKind.Null, cleared.Values.GetProperty("notes").ValueKind);
        Assert.Equal(member.Name, cleared.Values.GetProperty("item").GetString());

        var conflicting = BlockPages.Empty();
        var duplicateBindings = BlockPages.FormBlock(conflicting, "orders.lines", [
            BlockPages.Add(conflicting, "appText", ("field", "notes")),
            BlockPages.Add(conflicting, UmbracoAccountPickerBlock.AliasName, ("identityField", "notes"), ("accountType", "Members")),
            BlockPages.Add(conflicting, UmbracoAccountPickerBlock.AliasName, ("identityField", "notes"), ("accountType", "Backoffice Users"))], []);
        BlockPages.Layout(conflicting).Add(duplicateBindings);
        var validator = scope.ServiceProvider.GetRequiredService<AppBlocksValidator>();
        await validator.LoadAsync();
        Assert.Contains(validator.Validate(conflicting.ToJsonString()), e => e.Contains("its own writable text field for 'notes'"));
    }
}
