using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using AppBlocks.AccountPickers;
using AppBlocks.Authentication;
using AppBlocks.DataSources;
using AppBlocks.Forms;
using AppBlocks.Publishing;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Services;
using Umbraco.Extensions;

namespace AppBlocks.Blocks;

/// <summary>Server-side conditions on a published block. Both renderers and block endpoints use the same published values.</summary>
public sealed class BlockConditions(IDataSourceUserResolver users, IMemberGroupService memberGroups, IEnumerable<IAppBlockPlugin> plugins, IHttpContextAccessor http, IContentTypeService? contentTypes = null)
{
    public const string Visibility = "blockVisibility", Security = "blockSecurity", ReadOnly = "blockReadOnly";
    private MemberGroupNames? groups;

    private static BlockListModel Conditions(IPublishedElement block, string alias)
        => block.GetProperty(alias)?.GetValue() as BlockListModel ?? BlockListModel.Empty;

    internal static string ConditionAlias(string property) => property switch {
        Visibility => "appVisibilityCondition", Security => "appSecurityCondition", ReadOnly => "appReadOnlyCondition",
        _ => throw new ArgumentOutOfRangeException(nameof(property)),
    };

    internal static bool? ConstantResult(string property, string? comparison) => (property, comparison) switch {
        (Visibility, "Always visible") or (Security, "Always rendered") or (ReadOnly, "Always read only") => true,
        (Visibility, "Never visible") or (Security, "Never rendered") or (ReadOnly, "Never read only") => false,
        _ => null,
    };

    private static bool? ConstantResult(string property, IPublishedElement condition) =>
        condition.ContentType.Alias == ConditionAlias(property) ? ConstantResult(property, condition.Value<string>("comparison")) : false;

    internal static bool VariesByRequest(BlockGridItem item) =>
        RequiresNoStore(item)
        || Conditions(item.Content, Visibility).Any(entry => ConstantResult(Visibility, entry.Content) is null);

    internal static bool RequiresNoStore(BlockGridItem item) =>
        Conditions(item.Content, Security).Any(entry => ConstantResult(Security, entry.Content) is null)
        || Conditions(item.Content, ReadOnly).Any(entry => ConstantResult(ReadOnly, entry.Content) is null);

    internal static bool HasSecurity(BlockGridItem item) =>
        Conditions(item.Content, Security).Any(entry => ConstantResult(Security, entry.Content) != true);

    public async Task<bool> MatchesAsync(BlockGridItem item, string alias, IPublishedContent page, ClaimsPrincipal? visitor,
        string? culture = null, FormDefinition? form = null, Func<Task<FormRecord?>>? load = null, CancellationToken ct = default)
    {
        var conditions = Conditions(item.Content, alias);
        if (alias == ReadOnly)
        {
            // Editable by default; any matching or indeterminate Read Only condition locks the block.
            foreach (var entry in conditions)
                if (await EvaluateAsync(entry.Content, alias, page, visitor, culture, form, load, ct) ?? true) return true;
            return false;
        }
        // Visible/rendered by default; every condition must pass, and an unavailable item or identity fails closed.
        foreach (var entry in conditions)
            if (await EvaluateAsync(entry.Content, alias, page, visitor, culture, form, load, ct) != true) return false;
        return true;
    }

    private async Task<bool?> EvaluateAsync(IPublishedElement condition, string alias, IPublishedContent page, ClaimsPrincipal? visitor,
        string? culture, FormDefinition? form, Func<Task<FormRecord?>>? load, CancellationToken ct)
    {
        if (condition.ContentType.Alias != ConditionAlias(alias)) return null;
        var comparison = condition.Value<string>("comparison");
        if (ConstantResult(alias, comparison) is { } passes) return passes;
        if (alias is Security or ReadOnly && http.HttpContext is { Response.HasStarted: false } context) context.KeepOutOfCaches();
        if (visitor is null) return null;
        if (!FieldCondition.IsComparison(comparison)) return await VisitorMatchesAsync(condition, comparison, visitor, ct);
        var name = condition.Value<string>("item")?.Trim();
        if (string.IsNullOrEmpty(name)) return null;
        var value = form is null || Guid.TryParse(name, out _)
            ? PageValue(page, name, culture) : await FieldValueAsync(form, name, load);
        return value is { } left ? FieldCondition.Compare(left.Kind, comparison!, left.Text, condition.Value<string>("literal") ?? "") : null;
    }

    private async Task<bool?> VisitorMatchesAsync(IPublishedElement condition, string? comparison, ClaimsPrincipal visitor, CancellationToken ct)
    {
        var user = await DataSourceQueries.ResolveReaderAsync(users, visitor, ct);
        if (user is null) return null;
        switch (comparison)
        {
            case "User is anonymous": return user.IsAnonymous;
            case "User is signed in": return !user.IsAnonymous;
            case "Backoffice user is in group": return user.IsInPickedUserGroup(condition.Value<string>("literal")?.Trim() ?? "");
            case "Member is in group":
            case "User is outside member groups":
                if (!user.IsMember) return comparison == "User is outside member groups";
                groups ??= new((await memberGroups.GetAllAsync()).ToArray());
                var picked = condition.GetProperty("memberGroups")?.GetValue()?.ToString() ?? "";
                if (groups.Resolve(picked) is not { Count: > 0 } names) return null;
                return names.Any(user.IsInMemberGroup) == (comparison == "Member is in group");
            default: return null;
        }
    }

    private (string Kind, string Text)? PageValue(IPublishedContent page, string name, string? culture)
    {
        if (Guid.TryParse(name, out var key))
        {
            var type = contentTypes?.Get(page.ContentType.Key)?.CompositionPropertyTypes.FirstOrDefault(p => p.Key == key);
            if (type is null) return null;
            name = type.Alias;
        }
        if (page.GetProperty(name) is not { } property) return null;
        var value = property.GetValue(culture);
        var kind = value switch {
            decimal or double or float or long or int or short or byte => "number",
            DateTime or DateTimeOffset => "datetime", DateOnly => "date", TimeOnly => "time", _ => "text",
        };
        return (kind, ItemText(value));
    }

    private static async Task<(string Kind, string Text)?> FieldValueAsync(FormDefinition form, string name, Func<Task<FormRecord?>>? load)
    {
        if (form.Resource.Fields.FirstOrDefault(f => f.Key == name) is not { } field || load is null
            || await load() is not { } record) return null;
        if (record.Values.TryGetValue(name, out var value)) return (field.Kind, ItemText(value));
        return record.Key is null ? (field.Kind, "") : null;
    }

    private static string ItemText(object? value) => value switch {
        DateTimeOffset moment => moment.ToString("O", CultureInfo.InvariantCulture),
        DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
        TimeOnly time => time.ToString("O", CultureInfo.InvariantCulture),
        JsonElement { ValueKind: JsonValueKind.True } => "true", JsonElement { ValueKind: JsonValueKind.False } => "false",
        _ => BindVariableProviders.Text(value) ?? "",
    };

    /// <summary>Checks the published path to a page-level block, including its enclosing layouts and Forms container.</summary>
    public async Task<bool> AllowsPathAsync(IPublishedContent page, Guid key, ClaimsPrincipal? visitor, string? culture, CancellationToken ct)
    {
        var path = AppBlockContent.PathTo(AppBlockContent.Blocks(page, culture), key, culture);
        return path is not null && await AllowsPathAsync(page, path, visitor, culture, ct);
    }

    internal async Task<bool> AllowsPathAsync(IPublishedContent page, IReadOnlyList<BlockGridItem> path, ClaimsPrincipal? visitor, string? culture, CancellationToken ct)
    {
        foreach (var item in path)
            if (!await MatchesAsync(item, Security, page, visitor, culture, ct: ct)) return false;
        return true;
    }

    internal async Task<bool> ReadOnlyOnPathAsync(IPublishedContent page, IEnumerable<BlockGridItem> path, ClaimsPrincipal? visitor, string? culture, CancellationToken ct)
    {
        foreach (var item in path)
            if (await MatchesAsync(item, ReadOnly, page, visitor, culture, ct: ct)) return true;
        return false;
    }

    /// <summary>Evaluates a form's View Only setting, its Read Only list against its record, and enclosing layouts against the published page.</summary>
    public async Task<bool> IsFormReadOnlyAsync(FormDefinition form, ClaimsPrincipal visitor, Func<Task<FormRecord?>> load, CancellationToken ct)
        => form.Interface.Value<bool>("viewOnly")
            || await ReadOnlyOnPathAsync(form.Page, form.Ancestors, visitor, form.Culture, ct)
            || await MatchesAsync(form.Block, ReadOnly, form.Page, visitor, form.Culture, form, load, ct);

    /// <summary>Scopes field binding, projection, validation and child blocks to this caller and record.
    /// Recomputes from the form's full published blocks so post-save state transitions stay accurate, and
    /// enforces the invariant that Security denial also implies Read Only. Only item comparisons need the record,
    /// which is loaded at most once.</summary>
    public async Task<FormDefinition> ForFormAsync(FormDefinition form, ClaimsPrincipal visitor, Func<Task<FormRecord?>> load,
        bool rendering, CancellationToken ct)
    {
        var denied = new HashSet<Guid>();
        var hidden = new HashSet<Guid>();
        var locked = new HashSet<Guid>();
        Task<FormRecord?>? record = null;
        Task<FormRecord?> Record() => record ??= load();
        var wholeForm = await IsFormReadOnlyAsync(form, visitor, Record, ct);
        if (wholeForm) locked.Add(form.Block.ContentKey);
        async Task Visit(BlockGridItem item, bool parentLocked)
        {
            if (!await MatchesAsync(item, Security, form.Page, visitor, form.Culture, form, Record, ct))
            {
                foreach (var excluded in AppBlockContent.Flatten([item], form.Culture))
                {
                    denied.Add(excluded.ContentKey);
                    // Security also implies read only, so every write path sees one unified invariant.
                    locked.Add(excluded.ContentKey);
                }
                return;
            }
            if (rendering && !await MatchesAsync(item, Visibility, form.Page, visitor, form.Culture, form, Record, ct)) hidden.Add(item.ContentKey);
            var itemLocked = parentLocked || await MatchesAsync(item, ReadOnly, form.Page, visitor, form.Culture, form, Record, ct);
            if (itemLocked) locked.Add(item.ContentKey);
            foreach (var child in AppBlockContent.Children(item, form.Culture)) await Visit(child, itemLocked);
        }
        foreach (var child in form.Block.Areas.SelectMany(a => a)) await Visit(child, wholeForm);
        var allFields = form.AllFields;
        var allRules = form.AllRules;
        var allAntiSpam = form.AllAntiSpam;
        var excludedFields = allFields.Where(f => denied.Contains(f.ContentKey)).Select(f => f.Content.Value<string>("field")!).ToHashSet(StringComparer.Ordinal);
        var lockedFields = allFields.Where(f => locked.Contains(f.ContentKey)).Select(f => f.Content.Value<string>("field")!).ToHashSet(StringComparer.Ordinal);
        lockedFields.UnionWith(excludedFields);
        if (denied.Count > 0 || locked.Count > 0)
        {
            var pickers = plugins.OfType<AccountPickerBlock>().ToDictionary(p => p.Alias);
            foreach (var block in AppBlockContent.Flatten(form.Block.Areas.SelectMany(a => a), form.Culture))
            {
                if (!pickers.TryGetValue(block.Content.ContentType.Alias, out var picker)) continue;
                AccountPickerSettings settings;
                try { settings = picker.ReadSettings(block.Content); }
                catch (AccountPickerException) { continue; }
                var writable = settings.WritableFields().ToArray();
                // A picker writes several fields. Omit the companion as a whole when one of its bindings is secured.
                if (writable.Concat(settings.FallbackFields.Values).Any(excludedFields.Contains))
                {
                    denied.Add(block.ContentKey);
                    locked.Add(block.ContentKey);
                }
                if (!locked.Contains(block.ContentKey) && !writable.Any(lockedFields.Contains)) continue;
                // Only the picker checks an account against its provider. Its fields are locked whenever the picker is locked or omitted.
                locked.Add(block.ContentKey);
                lockedFields.UnionWith(writable);
            }
        }
        if (denied.Count == 0 && hidden.Count == 0 && locked.Count == 0
            && form.DeniedBlocks.Count == 0 && form.InvisibleBlocks.Count == 0 && form.ReadOnlyBlocks.Count == 0)
            return form;
        return form with {
            AllFields = allFields, AllRules = allRules, AllAntiSpam = allAntiSpam,
            DeniedBlocks = denied, InvisibleBlocks = hidden, ExcludedFields = excludedFields,
            ReadOnlyBlocks = locked, ReadOnlyFields = lockedFields,
            Fields = allFields.Where(f => !denied.Contains(f.ContentKey)).ToArray(),
            Rules = allRules.Where(r => !excludedFields.Contains(r.Field)
                && r.Conditions.All(c => !excludedFields.Contains(c.Field) && !excludedFields.Contains(c.OtherField))).ToArray(),
            AntiSpam = allAntiSpam.Where(b => !denied.Contains(b.ContentKey)).ToArray(),
        };
    }
}
