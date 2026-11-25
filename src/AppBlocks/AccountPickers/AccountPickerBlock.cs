using System.Text.Json;
using AppBlocks.Blocks;
using AppBlocks.DataSources;
using AppBlocks.Forms;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace AppBlocks.AccountPickers;

/// <summary>A form companion. Register its provider as a scoped, keyed IAccountPickerProvider under this block's alias.</summary>
public abstract class AccountPickerBlock : IAppBlockPlugin
{
    public abstract string Alias { get; }
    public virtual string ViewPath => "~/Views/Partials/ApplicationBlocks/AccountPicker.cshtml";
    public bool RequiresUser => false; // Form companions authorize lookup and submission themselves.
    public bool VariesByData => true;
    public abstract AccountPickerSettings ReadSettings(Func<string, string> value);

    public AccountPickerSettings ReadSettings(IPublishedElement element) =>
        ReadSettings(alias => element.GetProperty(alias)?.GetSourceValue()?.ToString()?.Trim() ?? "");

    public AccountPickerSettings ReadSettings(JsonElement block)
    {
        var values = block.TryGetProperty("values", out var properties) && properties.ValueKind == JsonValueKind.Array
            ? properties.EnumerateArray().ToDictionary(p => p.GetProperty("alias").GetString()!, p => p.GetProperty("value").ToString().Trim())
            : new Dictionary<string, string>();
        return ReadSettings(alias => values.GetValueOrDefault(alias, ""));
    }

    public void Validate(JsonElement block, List<string> errors)
    {
        try { ReadSettings(block); }
        catch (AccountPickerException error) { errors.Add(error.Message); }
    }
}

public record AccountPickerSettings
{
    public string IdentityField { get; init; } = "";
    public string NameField { get; init; } = "";
    public string Label { get; init; } = "";
    public Dictionary<string, string> FieldMappings { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> PrefillMappings { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> FallbackFields { get; init; } = new(StringComparer.Ordinal);
    public string[] CompactUpperFields { get; init; } = [];
    public bool AllowManualEntry { get; init; }
    public virtual bool AllowsEmpty => true;
    public virtual bool Permits(DataSourceUser? user) => user is { IsAnonymous: false };

    public string? Normalize(string field, string? value) => CompactUpperFields.Contains(field)
        ? string.Concat((value ?? "").Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant() : value;

    internal IEnumerable<string> WritableFields()
    {
        yield return IdentityField;
        if (NameField.Length > 0) yield return NameField;
        foreach (var field in FieldMappings.Values) yield return field;
        foreach (var field in PrefillMappings.Values) yield return field;
    }

    internal void CheckBindings(HashSet<string> owned, Func<string, bool> writable, Func<string, bool> mirror)
    {
        foreach (var field in WritableFields())
            if (!owned.Add(field) || !writable(field))
                throw new AccountPickerException($"The picker needs its own writable text field for '{field}' on this form.");
        foreach (var field in FallbackFields.Values)
            if (!owned.Add(field) || !mirror(field))
                throw new AccountPickerException($"The picker needs a separate read-only text field for '{field}' on this form.");
    }
}

public sealed record AccountPickerDefinition(string Alias, Guid PageKey, Guid BlockKey, AccountPickerSettings Settings);
public sealed record AccountPickerItem(string Key, string DisplayName, string? Detail = null, IReadOnlyDictionary<string, string?>? Attributes = null);
public sealed record AccountPickerSearch(IReadOnlyList<AccountPickerItem> Items, bool Cached = false);
public sealed record AccountPickerSelection(AccountPickerItem Person, bool Cached = false);
public sealed class AccountPickerException(string message, bool unavailable = false) : Exception(message)
{
    public bool Unavailable { get; } = unavailable;
}

public interface IAccountPickerProvider
{
    Task<AccountPickerSearch> SearchAsync(AccountPickerDefinition picker, string? query, CancellationToken ct);
    /// <summary>Resolve a key within the published scope. Submission is supplied only for a changed selection during save.</summary>
    Task<AccountPickerSelection?> ResolveAsync(AccountPickerDefinition picker, string key, FormExecutionContext? submission, CancellationToken ct);
}
