using System.Text.Json;
using AppBlocks.Setup;
using Umbraco.Cms.Core.Manifest;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Manifest;

namespace AppBlocks.Blocks;

/// <summary>
/// Publishes element-type key-to-alias mappings for backoffice nested block previews, so plugins and host
/// packages can register nested previews by element alias instead of hardcoding element-type GUIDs in JavaScript.
/// </summary>
internal sealed class BlockTypeAliasesManifest(IEnumerable<AppBlocksSchema> schemas, IContentTypeService contentTypes)
    : IPackageManifestReader
{
    public Task<IEnumerable<PackageManifest>> ReadPackageManifestsAsync()
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in schemas.DistinctBy(s => s.Assembly).SelectMany(s => s.Definitions("ContentTypes")))
        {
            if ((bool?)definition.Element("Info")?.Element("IsElement") != true) continue;
            var key = definition.Attribute("Key")?.Value;
            var alias = definition.Attribute("Alias")?.Value;
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(alias))
                aliases[key] = alias;
        }
        foreach (var type in contentTypes.GetAll())
        {
            if (type.IsElement && !string.IsNullOrWhiteSpace(type.Alias))
                aliases[type.Key.ToString()] = type.Alias;
        }

        return Task.FromResult<IEnumerable<PackageManifest>>([new PackageManifest
        {
            Id = "AppBlocks.BlockTypeAliases",
            Name = "AppBlocks element type aliases",
            Version = "1.0.0",
            Extensions = [
                JsonSerializer.SerializeToElement(new
                {
                    type = "appEntryPoint",
                    alias = "AppBlocks.BlockTypeAliases",
                    name = "AppBlocks nested block type aliases",
                    js = "/App_Plugins/ApplicationBlocks/block-aliases.js",
                    meta = new { aliases },
                }),
            ],
        }]);
    }
}
