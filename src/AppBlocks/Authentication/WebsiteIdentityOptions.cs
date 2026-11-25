namespace AppBlocks.Authentication;

/// <summary>Which Umbraco sessions can act on the published website.</summary>
public sealed class WebsiteIdentityOptions
{
    /// <summary>Opt in only for sites that grant application access directly to backoffice accounts.</summary>
    public bool IncludeBackOfficeUsers { get; set; }
}
