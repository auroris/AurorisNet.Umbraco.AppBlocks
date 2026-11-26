using System.Xml.Linq;
using AppBlocks.Blocks;
using AppBlocks.Setup;

namespace AppBlocks.Tests;

/// <summary>
/// The Visibility, Security and Read Only lists as the package's definitions declare them. A block type that has the conditions
/// group carries the lists its caption names, so no type ships with the group and nothing in it.
/// </summary>
public sealed class BlockConditionSchemaTests
{
    private const string Group = "blockConditions";

    [Fact]
    public void Every_conditions_group_holds_the_lists_its_caption_names()
    {
        var schema = new AppBlocksSchema(typeof(AppBlocksExtensions).Assembly);
        string List(string alias) => schema.Definitions("DataTypes").Single(type => type.Attribute("Alias")!.Value == alias).Attribute("Key")!.Value;
        var lists = new Dictionary<string, string> {
            [BlockConditions.Visibility] = List("Application: Visibility conditions"), [BlockConditions.Security] = List("Application: Security conditions"),
            [BlockConditions.ReadOnly] = List("Application: Read only conditions"),
        };
        var types = schema.Definitions("ContentTypes").ToDictionary(type => type.Attribute("Alias")!.Value);
        foreach (var (alias, type) in types)
        {
            if (Tab(type) is not { } tab) continue;
            var caption = tab.Element("Caption")!.Value;
            string[] expected = caption == "Visibility, Security and Read Only" ? [BlockConditions.Visibility, BlockConditions.Security, BlockConditions.ReadOnly]
                : [BlockConditions.Visibility, BlockConditions.Security];
            if (expected.Length == 2) Assert.Equal("Visibility and Security", caption);
            Assert.Equal(AppBlockSchema.Key($"{alias}:group:{Group}").ToString(), tab.Element("Key")!.Value);
            var properties = type.Element("GenericProperties")!.Elements("GenericProperty").Where(property => (string?)property.Element("Tab")!.Attribute("Alias") == Group)
                .OrderBy(property => (int)property.Element("SortOrder")!).ToArray();
            var declared = properties.Select(property => property.Element("Alias")!.Value).ToArray();
            Assert.True(expected.SequenceEqual(declared), $"{alias} has '{caption}' holding [{string.Join(", ", declared)}].");
            foreach (var property in properties)
            {
                var name = property.Element("Alias")!.Value;
                Assert.Equal(AppBlockSchema.Key($"{alias}:{name}").ToString(), property.Element("Key")!.Value);
                Assert.Equal(lists[name], property.Element("Definition")!.Value);
                Assert.Equal("Umbraco.BlockList", property.Element("Type")!.Value);
                Assert.Equal(caption, property.Element("Tab")!.Value);
            }
        }
        // Every block an editor places has the group, and every field a visitor can change can be locked.
        string[] placed = ["appForm", "appFieldset", "appActions", AppBlockSchema.FormsContainerAlias, AppBlockSchema.AttachmentsAlias, AppBlockSchema.ScheduleAlias,
            AppBlockSchema.TabularAlias, AppBlockSchema.AntiSpamAlias, ..AppBlockSchema.LayoutAliases, ..AppBlockSchema.ContentAliases, ..AppBlockSchema.FieldAliases];
        foreach (var alias in placed) Assert.True(Tab(types[alias]) is not null, $"{alias} has no conditions group.");
        foreach (var alias in AppBlockSchema.FieldAliases.Where(alias => alias != "appDisplay"))
            Assert.True(types[alias].Element("GenericProperties")!.Elements("GenericProperty").Any(property => property.Element("Alias")!.Value == BlockConditions.ReadOnly),
                $"{alias} has no Read Only list.");
    }

    private static XElement? Tab(XElement type) => type.Element("Tabs")?.Elements("Tab").SingleOrDefault(tab => tab.Element("Alias")?.Value == Group);
}
