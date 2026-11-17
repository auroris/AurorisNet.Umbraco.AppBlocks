namespace AppBlocks.Tests;

/// <summary>
/// The resets reach Umbraco's private members by reflection. An Umbraco update that renames either fails here, in
/// the repository that ships them, before a host's first site start fails with the same message.
/// </summary>
[Collection("Standalone Umbraco")]
public sealed class UmbracoTestSupportTests
{
    [Fact]
    public void Resets_find_the_static_state_umbraco_caches()
    {
        UmbracoTestSupport.ResetBackOfficePath();
        UmbracoTestSupport.ResetPublishedContentServices();
    }
}
