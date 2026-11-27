using System.Text.Json;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models.Editors;
using Umbraco.Cms.Core.PropertyEditors;

namespace AppBlocks.Blocks;

[DataEditor(AliasName, ValueType = ValueTypes.String)]
public sealed class DocumentReferenceEditor(IDataValueEditorFactory factory) : DataEditor(factory)
{
    public const string AliasName = "AppBlocks.DocumentReference";
}

[DataEditor(AliasName, ValueType = ValueTypes.Text)]
public sealed class ReportSourceEditor(IDataValueEditorFactory factory) : DataEditor(factory)
{
    public const string AliasName = "AppBlocks.ReportSourceValue";
}

public sealed class DocumentReferenceTracking : IDataValueReferenceFactory, IDataValueReference
{
    public bool IsForEditor(IDataEditor? editor) => editor?.Alias is DocumentReferenceEditor.AliasName or ReportSourceEditor.AliasName;
    public IDataValueReference GetDataValueReference() => this;

    public IEnumerable<UmbracoEntityReference> GetReferences(object? value)
    {
        var selection = value?.ToString()?.Trim();
        if (string.IsNullOrEmpty(selection)) yield break;
        if (selection.StartsWith('{'))
        {
            try
            {
                using var json = JsonDocument.Parse(selection);
                var root = json.RootElement;
                selection = root.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String
                    && kind.GetString() == "dataSource" && root.TryGetProperty("source", out var source)
                    && source.ValueKind == JsonValueKind.String ? source.GetString() : null;
            }
            catch (JsonException) { selection = null; }
        }
        if (selection is not null && AppBlockValues.DocumentKey(selection) is { } key)
            yield return new UmbracoEntityReference(Udi.Create(Constants.UdiEntityType.Document, key));
    }
}
