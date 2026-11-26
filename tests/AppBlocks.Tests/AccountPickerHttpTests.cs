using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    [Fact]
    public async Task A_picker_that_Security_omits_leaves_its_fields_read_only()
    {
        await using var site = await BlockSite.StartAsync();
        var member = site.CreateMember("picker.omitted", "omitted@example.invalid", "Omitted Example");
        var memberKey = member.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var content = BlockPages.Empty();
        // The block's Security list, with the one condition that fails for every visitor.
        JsonNode Omitted(JsonNode layout)
        {
            var condition = Guid.NewGuid().ToString();
            var block = content["contentData"]!.AsArray().Single(item => item!["key"]!.GetValue<string>() == layout["contentKey"]!.GetValue<string>())!;
            block["values"]!.AsArray().Add(new JsonObject {
                ["alias"] = BlockConditions.Security, ["culture"] = null, ["segment"] = null,
                ["value"] = new JsonObject {
                    ["contentData"] = new JsonArray(new JsonObject {
                        ["key"] = condition, ["contentTypeKey"] = AppBlockSchema.Key("appSecurityCondition").ToString(),
                        ["values"] = new JsonArray(new JsonObject { ["alias"] = "comparison", ["culture"] = null, ["segment"] = null, ["value"] = "Never rendered" }),
                    }),
                    ["settingsData"] = new JsonArray(),
                    ["expose"] = new JsonArray(new JsonObject { ["contentKey"] = condition, ["culture"] = null, ["segment"] = null }),
                    ["layout"] = new JsonObject { ["Umbraco.BlockList"] = new JsonArray(new JsonObject { ["contentKey"] = condition, ["settingsKey"] = null }) },
                },
            });
            return layout;
        }
        Guid Add(bool omitPicker)
        {
            var picker = BlockPages.Add(content, UmbracoAccountPickerBlock.AliasName, ("identityField", "notes"), ("nameField", "item"), ("accountType", "Members"));
            var name = BlockPages.Add(content, "appText", ("field", "item"));
            var form = BlockPages.FormBlock(content, "orders.lines", [omitPicker ? Omitted(picker) : picker,
                BlockPages.Add(content, "appText", ("field", "notes")), omitPicker ? name : Omitted(name), BlockPages.Add(content, "appNumber", ("field", "quantity"))], []);
            BlockPages.Layout(content).Add(form);
            return Guid.Parse(form["contentKey"]!.ToString());
        }
        // The picker's own Security omits it, or the Security of one field it writes does.
        var pickerOmitted = Add(omitPicker: true);
        var nameOmitted = Add(omitPicker: false);
        using var scope = site.CreateScope();
        var contents = scope.ServiceProvider.GetRequiredService<IContentService>();
#pragma warning disable CS0618
        var page = contents.GetById(BlockSite.FormPageKey)!;
        page.SetValue(BlockSite.BodyAlias, content.ToJsonString());
        Assert.True(contents.Save(page).Success);
        var published = contents.Publish(page, ["*"]);
        Assert.True(published.Success, string.Join(" ", published.EventMessages?.GetAll().Select(m => m.Message) ?? []));
#pragma warning restore CS0618
        using var client = await site.SignInMemberAsync(member);

        // Only the picker checks a key against its accounts, so without it the form takes none.
        var form = new FormsClient(client, "", page.Key, pickerOmitted);
        var row = await form.LoadAsync("1");
        Assert.True(row.IsSuccess, row.Body.ToString());
        var posted = await form.UpdateAsync("1", row.RecordChecksum, new { notes = memberKey, item = "Posted name" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, posted.Response.StatusCode);
        Assert.True(posted.Errors.TryGetProperty("notes", out _) && posted.Errors.TryGetProperty("item", out _), posted.Body.ToString());
        Assert.Equal(["item", "notes"], row.Body.GetProperty("record").GetProperty("readOnlyFields").EnumerateArray().Select(field => field.GetString()!).Order().ToArray());
        var other = await form.UpdateAsync("1", row.RecordChecksum, new { quantity = 3 });
        Assert.True(other.IsSuccess, other.Body.ToString());
        var reopened = await form.LoadAsync("1");
        Assert.Equal(JsonValueKind.Null, reopened.Values.GetProperty("notes").ValueKind);
        Assert.Equal("Bearing", reopened.Values.GetProperty("item").GetString());
        Assert.Equal(3m, decimal.Parse(reopened.Values.GetProperty("quantity").ToString(), System.Globalization.CultureInfo.InvariantCulture));

        form = new FormsClient(client, "", page.Key, nameOmitted);
        row = await form.LoadAsync("1");
        Assert.True(row.IsSuccess, row.Body.ToString());
        Assert.False(row.Values.TryGetProperty("item", out _));
        posted = await form.UpdateAsync("1", row.RecordChecksum, new { notes = memberKey });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, posted.Response.StatusCode);
        Assert.Equal("notes", Assert.Single(row.Body.GetProperty("record").GetProperty("readOnlyFields").EnumerateArray()).GetString());

        // The page shows the fields without the picker, and locked.
        var html = await client.GetStringAsync("/orders-form/?id=1");
        Assert.DoesNotContain("data-app-account-picker", html);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Count(html, "(?s)<input [^>]*name=\"notes\"[^>]* disabled"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "(?s)<input [^>]*name=\"item\"[^>]* disabled"));
    }
}
