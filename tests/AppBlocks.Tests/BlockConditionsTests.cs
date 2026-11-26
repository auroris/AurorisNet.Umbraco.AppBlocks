using System.Security.Claims;
using System.Text.Json;
using AppBlocks.Blocks;
using AppBlocks.DataSources;
using AppBlocks.Forms;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Services;

namespace AppBlocks.Tests;

public sealed class BlockConditionsTests
{
    static BlockConditionsTests()
    {
        StaticServiceProvider.Instance ??= new ServiceCollection()
            .AddSingleton<IPublishedValueFallback>(new NoopPublishedValueFallback())
            .BuildServiceProvider();
    }
    private static readonly Guid StaffGroupKey = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid MissingGroupKey = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static ClaimsPrincipal MemberPrincipal(string id = "10")
        => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id)], "Identity.Application"));

    private static BlockConditions CreateConditions(DataSourceUser? resolvedUser, params (Guid Key, string Name)[] groups)
    {
        var userResolver = TestStubs.Stub<IDataSourceUserResolver>((m, _) => m.Name == nameof(IDataSourceUserResolver.ResolveAsync)
            ? Task.FromResult(resolvedUser) : null);
        var memberGroups = TestStubs.Stub<IMemberGroupService>((m, _) => m.Name == nameof(IMemberGroupService.GetAllAsync)
            ? Task.FromResult<IEnumerable<IMemberGroup>>(groups.Select(g => new MemberGroup { Key = g.Key, Name = g.Name }).ToArray()) : null);
        var http = TestStubs.Stub<IHttpContextAccessor>((m, _) => m.Name == "get_HttpContext" ? new DefaultHttpContext() : null);
        return new BlockConditions(userResolver, memberGroups, [], http);
    }

    private static IPublishedElement Element(string alias, params (string Alias, object? Value)[] properties)
    {
        var map = properties.ToDictionary(p => p.Alias, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var type = TestStubs.Stub<IPublishedContentType>((m, _) => m.Name switch {
            "get_Alias" => alias,
            "get_PropertyTypes" => Array.Empty<IPublishedPropertyType>(),
            _ => null,
        });
        return TestStubs.Stub<IPublishedElement>((m, args) => m.Name switch {
            "get_ContentType" => type,
            "get_Key" => Guid.NewGuid(),
            nameof(IPublishedElement.GetProperty) => map.TryGetValue((string)args![0]!, out var value) ? Property((string)args[0]!, value) : null,
            _ => null,
        });
    }

    private static IPublishedProperty Property(string alias, object? value) => TestStubs.Stub<IPublishedProperty>((m, _) => m.Name switch {
        "get_Alias" => alias,
        nameof(IPublishedProperty.HasValue) => value is not null,
        nameof(IPublishedProperty.GetValue) => value,
        nameof(IPublishedProperty.GetSourceValue) => value,
        _ => null,
    });

    private static IPublishedContent Page(params (string Alias, object? Value)[] properties)
    {
        var map = properties.ToDictionary(p => p.Alias, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var type = TestStubs.Stub<IPublishedContentType>((m, _) => m.Name switch {
            "get_Alias" => "page",
            "get_PropertyTypes" => Array.Empty<IPublishedPropertyType>(),
            _ => null,
        });
        return TestStubs.Stub<IPublishedContent>((m, args) => m.Name switch {
            "get_ContentType" => type,
            "get_Key" => Guid.NewGuid(),
            "get_UpdateDate" => new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            "get_Cultures" => new Dictionary<string, PublishedCultureInfo>(),
            nameof(IPublishedContent.GetProperty) => map.TryGetValue((string)args![0]!, out var value) ? Property((string)args[0]!, value) : null,
            _ => null,
        });
    }

    private static BlockListModel ConditionList(string conditionAlias, params (string Alias, object? Value)[][] entries)
        => new(entries.Select(e => new BlockListItem(Guid.NewGuid(), Element(conditionAlias, e), null, null)).ToList());

    private static BlockGridItem GridItem(Guid key, IPublishedElement content, params BlockGridItem[] children)
    {
        var item = new BlockGridItem(key, content, null, null);
        if (children.Length > 0)
            item.Areas = [new BlockGridArea(children.ToList(), "fields", 1, 12)];
        return item;
    }

    private static IFormResource StubResource(params FormField[] fields) => TestStubs.Stub<IFormResource>((m, _) => m.Name switch {
        "get_Alias" => "test.resource",
        "get_ContractVersion" => "1",
        "get_Fields" => (IReadOnlyList<FormField>)fields,
        _ => null,
    });

    private static DataSourceUser MemberUser(params string[] memberGroups)
        => new("10", null, "member", "member@example.invalid", memberGroups, IsBackOfficeUser: false);

    [Fact]
    public async Task Read_only_uses_OR_semantics_and_fails_closed_when_any_condition_is_indeterminate()
    {
        var page = Page();
        var resource = StubResource(new FormField("status", "Status", "text"), new FormField("notes", "Notes", "text"));
        var notesBlock = GridItem(Guid.NewGuid(), Element("appText", ("field", "notes"),
            (BlockConditions.ReadOnly, ConditionList("appReadOnlyCondition",
                [("comparison", "Is"), ("item", "status"), ("literal", "Closed")],
                [("comparison", "User is outside member groups"), ("memberGroups", StaffGroupKey.ToString())]))));
        var formBlock = GridItem(Guid.NewGuid(), Element("appForm"), notesBlock);
        var form = new FormDefinition(page, formBlock, Element("appFormInterface"), resource, [notesBlock]);

        var openRecord = new FormRecord("1", null, new Dictionary<string, object?> { ["status"] = "Open", ["notes"] = "hi" }, new(Update: true));
        var closedRecord = new FormRecord("1", null, new Dictionary<string, object?> { ["status"] = "Closed", ["notes"] = "hi" }, new(Update: true));

        // Open + Staff: neither condition matches -> editable.
        var staffConditions = CreateConditions(MemberUser("Staff"), (StaffGroupKey, "Staff"));
        Assert.False(await staffConditions.MatchesAsync(notesBlock, BlockConditions.ReadOnly, page, MemberPrincipal(), form: form, load: () => Task.FromResult<FormRecord?>(openRecord)));
        // Closed + Staff: status condition matches -> locked.
        Assert.True(await staffConditions.MatchesAsync(notesBlock, BlockConditions.ReadOnly, page, MemberPrincipal(), form: form, load: () => Task.FromResult<FormRecord?>(closedRecord)));
        // Open + non-Staff: group condition matches -> locked.
        var regularConditions = CreateConditions(MemberUser("Members"), (StaffGroupKey, "Staff"));
        Assert.True(await regularConditions.MatchesAsync(notesBlock, BlockConditions.ReadOnly, page, MemberPrincipal(), form: form, load: () => Task.FromResult<FormRecord?>(openRecord)));

        // Open + broken/deleted group condition alongside a false condition -> MUST still fail closed (locked).
        var brokenBlock = GridItem(Guid.NewGuid(), Element("appText", ("field", "notes"),
            (BlockConditions.ReadOnly, ConditionList("appReadOnlyCondition",
                [("comparison", "Is"), ("item", "status"), ("literal", "Closed")],
                [("comparison", "User is outside member groups"), ("memberGroups", MissingGroupKey.ToString())]))));
        Assert.True(await staffConditions.MatchesAsync(brokenBlock, BlockConditions.ReadOnly, page, MemberPrincipal(), form: form, load: () => Task.FromResult<FormRecord?>(openRecord)));
    }

    [Fact]
    public async Task Security_denial_implies_read_only_and_strips_dependent_field_rules_without_leaking_excluded_fields()
    {
        var page = Page();
        var resource = StubResource(
            new FormField("publicField", "Public", "text"),
            new FormField("secretFlag", "Secret", "text"));
        var publicBlock = GridItem(Guid.NewGuid(), Element("appText", ("field", "publicField")));
        var secretBlock = GridItem(Guid.NewGuid(), Element("appText", ("field", "secretFlag"),
            (BlockConditions.Security, ConditionList("appSecurityCondition", [("comparison", "Never rendered")]))));
        var formBlock = GridItem(Guid.NewGuid(), Element("appForm"), publicBlock, secretBlock);
        var dependentRule = new FieldRule("publicField", "Require", "All", "", [new FieldCondition("secretFlag", "text", "Is", "VIP", "")]);
        var independentRule = new FieldRule("publicField", "Validate", "All", "Cannot be bad", [new FieldCondition("publicField", "text", "Is not", "bad", "")]);
        var form = new FormDefinition(page, formBlock, Element("appFormInterface"), resource, [publicBlock, secretBlock])
        {
            Rules = [dependentRule, independentRule],
        };

        var conditions = CreateConditions(MemberUser());
        var record = new FormRecord("1", null, new Dictionary<string, object?> { ["publicField"] = "ok", ["secretFlag"] = "VIP" }, new(Update: true));
        var scoped = await conditions.ForFormAsync(form, MemberPrincipal(), () => Task.FromResult<FormRecord?>(record), rendering: false, default);

        // Security implies Read Only on both block and field.
        Assert.False(scoped.Includes(secretBlock));
        Assert.True(scoped.IsReadOnly(secretBlock));
        Assert.True(scoped.IsFieldReadOnly("secretFlag"));
        Assert.False(scoped.CanWrite(secretBlock));
        Assert.True(scoped.CanWrite(publicBlock));

        // Dependent field rule is removed from server Rules as well as ClientRules.
        Assert.Single(scoped.Rules, independentRule);
        Assert.Single(scoped.ClientRules, independentRule);

        // Projection omits secretFlag from Values AND does not leak it in ReadOnlyFields (stays null when no visible field is locked).
        var projected = scoped.Project(record);
        Assert.False(projected.Values.ContainsKey("secretFlag"));
        Assert.Null(projected.ReadOnlyFields);

        // Submission attempting to write the excluded field is rejected.
        var errors = scoped.Validate(JsonSerializer.SerializeToElement(new { secretFlag = "Hacked" }), creating: false);
        Assert.Contains("secretFlag", errors);
    }

    [Fact]
    public async Task Reevaluating_ForFormAsync_after_state_transition_recomputes_from_full_published_fields()
    {
        var page = Page();
        var resource = StubResource(
            new FormField("status", "Status", "text"),
            new FormField("approvalNote", "Approval note", "text"));
        var statusBlock = GridItem(Guid.NewGuid(), Element("appText", ("field", "status")));
        var approvalBlock = GridItem(Guid.NewGuid(), Element("appText", ("field", "approvalNote"),
            (BlockConditions.Security, ConditionList("appSecurityCondition",
                [("comparison", "Is"), ("item", "status"), ("literal", "Approved")]))));
        var formBlock = GridItem(Guid.NewGuid(), Element("appForm"), statusBlock, approvalBlock);
        var form = new FormDefinition(page, formBlock, Element("appFormInterface"), resource, [statusBlock, approvalBlock]);

        var conditions = CreateConditions(MemberUser());
        var draft = new FormRecord("1", null, new Dictionary<string, object?> { ["status"] = "Draft", ["approvalNote"] = "Secret" }, new(Update: true));
        var approved = new FormRecord("1", null, new Dictionary<string, object?> { ["status"] = "Approved", ["approvalNote"] = "Visible now" }, new(Update: true));

        var beforeSave = await conditions.ForFormAsync(form, MemberPrincipal(), () => Task.FromResult<FormRecord?>(draft), rendering: false, default);
        Assert.False(beforeSave.Includes(approvalBlock));
        Assert.Single(beforeSave.Fields);

        // Passing the already-scoped FormDefinition into post-save ForFormAsync must still see approvalNote now that status is Approved.
        var afterSave = await conditions.ForFormAsync(beforeSave, MemberPrincipal(), () => Task.FromResult<FormRecord?>(approved), rendering: false, default);
        Assert.True(afterSave.Includes(approvalBlock));
        Assert.Equal(2, afterSave.Fields.Count);
        Assert.Equal("Visible now", afterSave.Project(approved).Values["approvalNote"]);
    }

    [Fact]
    public async Task Static_view_only_form_locks_nested_blocks_and_new_records_with_omitted_keys_compare_as_empty()
    {
        var page = Page();
        var resource = StubResource(new FormField("status", "Status", "text"));
        var tabularBlock = GridItem(Guid.NewGuid(), Element(AppBlockSchema.TabularAlias));
        var statusBlock = GridItem(Guid.NewGuid(), Element("appText", ("field", "status"),
            (BlockConditions.Security, ConditionList("appSecurityCondition", [("comparison", "Is empty"), ("item", "status")]))));
        var formBlock = GridItem(Guid.NewGuid(), Element("appForm"), statusBlock, tabularBlock);
        var viewOnlyForm = new FormDefinition(page, formBlock, Element("appFormInterface", ("viewOnly", true)), resource, [statusBlock]);

        var conditions = CreateConditions(MemberUser());
        var newRecordWithOmittedKey = new FormRecord(null, null, new Dictionary<string, object?>(), new(Create: true));
        var scoped = await conditions.ForFormAsync(viewOnlyForm, MemberPrincipal(), () => Task.FromResult<FormRecord?>(newRecordWithOmittedKey), rendering: false, default);

        // Omitted key on a new record is treated as empty for Security (not null/denied).
        Assert.True(scoped.Includes(statusBlock));
        // Static viewOnly propagates ReadOnly to nested blocks (e.g. Tabular/Attachments).
        Assert.True(scoped.ViewOnly);
        Assert.True(scoped.IsReadOnly(tabularBlock));
        Assert.True(scoped.IsReadOnly(statusBlock));
    }
}
