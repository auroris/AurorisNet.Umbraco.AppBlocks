using System.Text.Json;
using AppBlocks.Blocks;
using AppBlocks.DataSources;
using AppBlocks.Forms;

namespace AppBlocks.AccountPickers;

public sealed class AccountPickerForm(IEnumerable<AccountPickerBlock> blocks, IDataSourceUserResolver users,
    Func<string, IAccountPickerProvider> providerFor) : IFormSubmissionProcessor
{
    internal static AccountPickerForm FromServices(IServiceProvider services) =>
        new(services.GetServices<IAppBlockPlugin>().OfType<AccountPickerBlock>(), services.GetRequiredService<IDataSourceUserResolver>(),
            services.GetRequiredKeyedService<IAccountPickerProvider>);

    public static IReadOnlyList<AccountPickerDefinition> Definitions(FormDefinition form, IEnumerable<AccountPickerBlock> blocks)
    {
        var types = blocks.ToDictionary(b => b.Alias);
        var owned = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<AccountPickerDefinition>();
        foreach (var block in AppBlockContent.Flatten(form.Block.Areas.SelectMany(a => a), form.Culture))
        {
            if (!form.Includes(block) || !types.TryGetValue(block.Content.ContentType.Alias, out var type)) continue;
            var settings = type.ReadSettings(block.Content);
            settings.CheckBindings(owned,
                field => form.Fields.Any(f => f.Content.Value<string>("field") == field && f.Content.ContentType.Alias == "appText" && !f.Content.Value<bool>("readOnly"))
                    && form.Resource.Fields.Any(f => f.Key == field && f.Kind == "text" && !f.ReadOnly),
                field => form.Fields.Any(f => f.Content.Value<string>("field") == field)
                    && form.Resource.Fields.Any(f => f.Key == field && f.Kind == "text" && f.ReadOnly));
            result.Add(new(type.Alias, form.Page.Key, block.ContentKey, settings));
        }
        return result;
    }

    public async Task<FormSubmissionValues> ProcessAsync(FormDefinition form, JsonElement values, FormRecord? stored, FormExecutionContext context, CancellationToken ct)
    {
        IReadOnlyList<AccountPickerDefinition> pickers;
        try { pickers = Definitions(form, blocks); }
        catch (AccountPickerException error) { return new(values, FormResult.Invalid("", error.Message)); }
        if (pickers.Count == 0) return new(values);
        var user = await users.ResolveAsync(context, ct);
        if (pickers.Any(picker => !picker.Settings.Permits(user))) return new(values, FormResult.Denied());

        var result = values.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone(), StringComparer.Ordinal);
        foreach (var picker in pickers)
        {
            var provider = providerFor(picker.Alias);
            if (await ApplyAsync(picker, provider, result, stored, context, ct) is { } refusal) return new(values, refusal);
        }
        // The provider's canonical name or attributes can also exceed a form field's length or clear a required value.
        var fields = pickers.SelectMany(p => p.Settings.WritableFields()).Where(result.ContainsKey);
        var errors = form.Validate(JsonSerializer.SerializeToElement(fields.ToDictionary(f => f, f => result[f])), creating: false);
        return errors.Count > 0 ? new(values, new FormResult(422, Errors: errors)) : new(JsonSerializer.SerializeToElement(result));
    }

    private static async Task<FormResult?> ApplyAsync(AccountPickerDefinition picker, IAccountPickerProvider provider,
        Dictionary<string, object?> result, FormRecord? stored, FormExecutionContext context, CancellationToken ct)
    {
        var settings = picker.Settings;
        var key = Text(result.GetValueOrDefault(settings.IdentityField))?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            if (!settings.AllowsEmpty) return FormResult.Invalid(settings.IdentityField, "Choose an account.");
            result[settings.IdentityField] = null;
            if (!settings.AllowManualEntry)
            {
                if (settings.NameField.Length > 0) result[settings.NameField] = null;
                foreach (var field in settings.FieldMappings.Values) result[field] = null;
            }
        }
        else if (stored is not null && key == Text(stored.Values.GetValueOrDefault(settings.IdentityField))?.Trim())
        {
            // Preserve the saved identity and its server-owned fields even if the directory is offline.
            result[settings.IdentityField] = stored.Values.GetValueOrDefault(settings.IdentityField);
            if (settings.NameField.Length > 0) result[settings.NameField] = stored.Values.GetValueOrDefault(settings.NameField);
            foreach (var field in settings.FieldMappings.Values)
                if (!settings.FallbackFields.TryGetValue(field, out var mirror) || !string.IsNullOrEmpty(Text(stored.Values.GetValueOrDefault(mirror))))
                    result[field] = stored.Values.GetValueOrDefault(field);
        }
        else
        {
            AccountPickerSelection? selected;
            try { selected = await provider.ResolveAsync(picker, key, context, ct); }
            catch (AccountPickerException error) { return FormResult.Invalid(settings.IdentityField, error.Message); }
            if (selected is null || selected.Person.Key != key)
                return FormResult.Invalid(settings.IdentityField, "This selection is no longer available in this picker.");
            result[settings.IdentityField] = selected.Person.Key;
            if (settings.NameField.Length > 0) result[settings.NameField] = selected.Person.DisplayName;
            foreach (var (attribute, field) in settings.FieldMappings)
            {
                var value = selected.Person.Attributes?.GetValueOrDefault(attribute);
                if (!string.IsNullOrWhiteSpace(value) || !settings.FallbackFields.ContainsKey(field)) result[field] = value;
            }
        }
        foreach (var field in settings.CompactUpperFields) result[field] = settings.Normalize(field, Text(result.GetValueOrDefault(field)));
        return null;
    }

    private static string? Text(object? value) => value is JsonElement { ValueKind: JsonValueKind.String } json ? json.GetString() : value as string;
}
