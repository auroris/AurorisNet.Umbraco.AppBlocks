using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Security;

namespace AppBlocks.Authentication;

/// <summary>
/// Who may publish a node that carries SQL or decides what a report runs: a SQL data source, an API endpoint, a report definition,
/// a list of values with its own SQL, or a page whose Interactive Report source changed. Every publish validator asks this one rule.
/// </summary>
public static class SqlPublishing
{
    /// <summary>
    /// Why this publish may not carry <paramref name="node"/>'s SQL, or null when it may. A backoffice user publishing must be an
    /// AppBlocks administrator, and is refused with <paramref name="onlyAdministrators"/>. A publish with no current user, such as a
    /// scheduled release, which Umbraco commits as the node's last writer, or startup work, which writes as the super user, goes
    /// ahead only when that writer is the super user or an AppBlocks administrator now; otherwise a non-administrator could save
    /// SQL and schedule it past the check. The super user is known by its key, as its integer id is obsolete. Without <paramref name="access"/>, as in a unit test without a site, nothing is judged.
    /// </summary>
    public static string? Refusal(this ApplicationAccessPolicy? access, IBackOfficeSecurityAccessor? security, IContent node, string onlyAdministrators)
    {
        if (access is null) return null;
        if (security?.BackOfficeSecurity?.CurrentUser is { } current) return access.IsBackOfficeAdministrator(current) ? null : onlyAdministrators;
        return access.UserById(node.WriterId) is { } writer && (writer.Key == Constants.Security.SuperUserKey || access.IsBackOfficeAdministrator(writer)) ? null
            : $"{node.Name} was last saved by someone who is not an AppBlocks administrator, so an AppBlocks administrator must publish it.";
    }
}
