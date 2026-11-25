namespace AppBlocks.Setup;

public sealed partial class SchemaInstaller
{
    // Register the content patches implemented in transition folders in release order.
    private IReadOnlyList<ContentStep> ContentUpgrades => [];
}
