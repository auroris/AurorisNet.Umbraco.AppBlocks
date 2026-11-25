using System.Text.RegularExpressions;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Core.Services;

namespace AppBlocks.Setup;

/// <summary>
/// One change to content an application installed, made once on each site: a block added to a page, a link retargeted, a page
/// moved. Its <paramref name="Name"/> is what the site records, and it sorts after the names of the steps before it, as in
/// <c>0002-v1.0.1-lead-time-notice</c>: the next number, the release that ships the step, and the change.
/// </summary>
/// <param name="Name">Letters, digits and <c>. _ -</c>, up to 200 characters, starting with a letter or a digit.</param>
/// <param name="Run">The change. It runs inside one Umbraco scope with the record that it ran, so a step that throws leaves both as they were.</param>
public sealed record ContentStep(string Name, Func<CancellationToken, Task> Run);

/// <summary>What <see cref="ContentStepRunner.RunAsync"/> did: installed the content on a new site, or ran these steps on an existing one.</summary>
public sealed record ContentStepsResult(bool Installed, IReadOnlyList<string> Ran);

/// <summary>A content step failed, a plan's steps are out of order, or the site has run a step this build does not have.</summary>
public sealed class ContentStepException(string message, Exception? inner = null) : InvalidOperationException(message, inner)
{
    /// <summary>
    /// True when the site has run a step that sorts after the last one this build has: another build of the application, usually a
    /// newer one, changed the site's content, and nothing was run against it.
    /// </summary>
    public bool SiteFromAnotherBuild { get; init; }
}

/// <summary>
/// Keeps the content an application installs from code in step with the application, release after release. A site the plan has
/// not touched gets the whole installation, which writes the content as the current build has it, and every step then counts as
/// run. A site installed by an earlier build runs the steps it has not run, in order, each once. The site's position is one entry
/// in Umbraco's key-value table, <c>AppBlocks:ContentSteps:&lt;plan&gt;</c>. Call it from the application's own startup code, in
/// a request or service scope, after the definitions its content uses are installed.
/// </summary>
/// <remarks>
/// A new site and an updated one reach their content by different routes, so an application tests that the two agree: it installs
/// the previous release, updates it, and compares the pages with a new installation's. Nodes that code owns outright, with no
/// editor's changes to keep, need no steps: <see cref="OwnedNodeInstaller"/> rewrites them when their definition changes.
/// </remarks>
public sealed partial class ContentStepRunner(IKeyValueService keyValues, ICoreScopeProvider scopes, ILogger<ContentStepRunner> logger)
{
    public const string KeyPrefix = "AppBlocks:ContentSteps:";
    private const string Installed = "installed";

    /// <summary>The key-value entry that holds a site's position in a plan.</summary>
    public static string StoreKey(string plan) => KeyPrefix + plan;

    /// <summary>
    /// The name of the last step the site has run under <paramref name="plan"/>; empty when the content is installed and no step has
    /// run since; null when the plan has not installed its content here.
    /// </summary>
    public string? Position(string plan)
    {
        var stored = keyValues.GetValue(StoreKey(Checked(plan, "plan")));
        if (stored is null) return null;
        if (stored == Installed) return "";
        return stored.StartsWith(Installed + ":", StringComparison.Ordinal) ? stored[(Installed.Length + 1)..]
            : throw new ContentStepException($"The entry {StoreKey(plan)} holds \"{stored}\", which is not a position this runner wrote.");
    }

    /// <summary>
    /// Records that the site's content stands at <paramref name="position"/>: the name of the last step it has run, or empty for
    /// installed content that has run none. For a site whose content was installed before its application had a plan; a site
    /// <see cref="RunAsync"/> installs or updates is recorded by it.
    /// </summary>
    public void Record(string plan, string position)
    {
        Checked(plan, "plan");
        if (position.Length > 0) Checked(position, "step");
        using var scope = scopes.CreateCoreScope();
        // Before any read, as OwnedNodeInstaller does, so a SQLite read transaction is never upgraded while startup writes elsewhere.
        scope.EagerWriteLock(Constants.Locks.KeyValues);
        keyValues.SetValue(StoreKey(plan), position.Length == 0 ? Installed : Installed + ":" + position);
        scope.Complete();
    }

    /// <summary>
    /// Brings a site's content to what this build installs. On a site with no position in <paramref name="plan"/>,
    /// <paramref name="install"/> runs and the site is recorded at the last step. On any other site, each step after the site's
    /// position runs in order, inside one scope with its record.
    /// </summary>
    /// <param name="plan">The application's name for its content, such as its assembly name; it stays the same across releases.</param>
    /// <param name="install">
    /// Writes the content as this build has it. It runs outside any scope the runner opens, so what it publishes is in the published
    /// cache for what it publishes next. When it throws, nothing is recorded and the next start runs it again, so it finds what it
    /// already created.
    /// </param>
    /// <param name="steps">
    /// Every step, in the order they were written, names ascending. A step that throws leaves the site at the step before it, and the
    /// next start runs it again. Content a step publishes reaches the published cache when the step ends. What a step writes
    /// outside Umbraco's database, such as rows in the application's own, is the step's to make repeatable.
    /// </param>
    /// <exception cref="ContentStepException">
    /// A step failed; or the names are out of order; or the site has run a step later than the last one given
    /// (<see cref="ContentStepException.SiteFromAnotherBuild"/>).
    /// </exception>
    public async Task<ContentStepsResult> RunAsync(string plan, Func<CancellationToken, Task> install, IReadOnlyList<ContentStep> steps, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(install);
        ArgumentNullException.ThrowIfNull(steps);
        for (var i = 0; i < steps.Count; i++)
        {
            Checked(steps[i].Name, "step");
            if (i > 0 && string.CompareOrdinal(steps[i].Name, steps[i - 1].Name) <= 0)
                throw new ContentStepException($"The content step {steps[i].Name} of {plan} does not sort after {steps[i - 1].Name}. Steps run in the order of their names, so give each the next number.");
        }
        var position = Position(plan);
        if (position is null)
        {
            await install(ct);
            Record(plan, steps.Count > 0 ? steps[^1].Name : "");
            logger.LogInformation("Installed the content of {Plan}. Its {Count} content steps count as run on this site.", plan, steps.Count);
            return new(true, []);
        }
        // A site with no steps to compare with has only retired ones behind it, as a schema with no run-once scripts has.
        if (steps.Count > 0 && string.CompareOrdinal(position, steps[^1].Name) > 0)
            throw new ContentStepException($"This site has run the content step {position} of {plan}, which this build does not have and which sorts after its last step, {steps[^1].Name}. "
                + "Another build of the application, usually a newer one, changed this site's content, so nothing was run against it. "
                + "Install that build, or restore the backup of Umbraco's database taken before it was installed.") { SiteFromAnotherBuild = true };
        var ran = new List<string>();
        foreach (var step in steps.Where(step => string.CompareOrdinal(step.Name, position) > 0))
        {
            ct.ThrowIfCancellationRequested();
            // The step's own cache of what it reads and saves: Umbraco's shared one takes the step's changes when the step is
            // recorded, and keeps what it had when the step fails.
            using var scope = scopes.CreateCoreScope(repositoryCacheMode: RepositoryCacheMode.Scoped);
            if (scope.Depth > 0)
                throw new InvalidOperationException($"The content steps of {plan} were run inside an Umbraco scope that was already open. "
                    + "Each step is one transaction of its own, so call RunAsync where no scope is open, as the application's startup code is.");
            scope.EagerWriteLock(Constants.Locks.KeyValues);
            try { await step.Run(ct); }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                throw new ContentStepException($"The content step {step.Name} of {plan} failed, and the site stays where it was: {error.Message}", error);
            }
            keyValues.SetValue(StoreKey(plan), Installed + ":" + step.Name);
            scope.Complete();
            ran.Add(step.Name);
            logger.LogInformation("Ran the content step {Step} of {Plan}.", step.Name, plan);
        }
        return new(false, ran);
    }

    private static string Checked(string name, string what)
        => !string.IsNullOrEmpty(name) && ValidName().IsMatch(name) ? name
            : throw new ArgumentException($"A content {what}'s name is 1 to 200 letters, digits, dots, underscores or hyphens, starting with a letter or a digit; \"{name}\" is not.");

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,199}$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidName();
}

/// <summary>
/// Changes a property value that a site's editors may have changed too, such as the blocks of a page an application installed. The
/// published version takes the change and is published again. A draft an editor saved since stays a draft, with the same change
/// made to it, so the editor's unpublished work is neither published nor lost.
/// </summary>
public sealed class ContentPatcher(IContentService contents)
{
    /// <summary>Applies <paramref name="change"/> to the published and the draft value of one property, and gives the node a new name when asked.</summary>
    /// <param name="content">The node, as <see cref="IContentService.GetById(Guid)"/> returns it.</param>
    /// <param name="property">The property's alias.</param>
    /// <param name="change">
    /// Takes a version's stored value, null when it has none, and returns the value to store, or null to leave that version as it is.
    /// It is called once with the draft's value and once with the published one, and returns null for a value that already has the change.
    /// </param>
    /// <param name="culture">The culture, for a node whose type varies by culture; null for one that does not.</param>
    /// <param name="name">The node's new name, or null to keep the name it has.</param>
    /// <returns>True when a version changed; false when both already were as the change leaves them.</returns>
    /// <exception cref="ContentStepException">Umbraco refused to publish the changed version.</exception>
    public bool Patch(IContent content, string property, Func<string?, string?> change, string? culture = null, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(change);
        var draft = content.GetValue<string>(property, culture);
        var draftChange = change(draft);
        var liveChange = change(content.GetValue<string>(property, culture, published: true));
        var published = culture is null ? content.Published : content.IsCulturePublished(culture);
        var renameLive = name is not null && (culture is null ? content.PublishName : content.GetPublishName(culture)) != name;
        var renameDraft = name is not null && (culture is null ? content.Name : content.GetCultureName(culture)) != name;
        if (draftChange is null && liveChange is null && !renameLive && !renameDraft) return false;

        void Rename() { if (name is null) return; if (culture is null) content.Name = name; else content.SetCultureName(name, culture); }
#pragma warning disable CS0618 // The content service's synchronous Save and Publish, as the package's own installers use them.
        if (published && (liveChange is not null || renameLive))
        {
            // The change is made to the published version and published. The draft an editor saved since is put back below.
            var savedDraft = content.Edited ? contents.GetVersion(content.VersionId) : null;
            if (savedDraft is not null)
            {
                var live = contents.GetVersion(content.PublishedVersionId)
                    ?? throw new ContentStepException($"The published version of {content.Name} ({content.Key}) could not be read.");
                content.CopyFrom(live, "*");
                content.TemplateId = live.TemplateId;
            }
            if (liveChange is not null) content.SetValue(property, liveChange, culture);
            Rename();
            contents.Save(content);
            var result = contents.Publish(content, [culture ?? "*"]);
            if (!result.Success)
                throw new ContentStepException($"{content.Name} ({content.Key}) could not be published with its change: {result.Result}. "
                    + string.Join(" ", result.EventMessages?.GetAll().Select(message => message.Message) ?? []));
            if (savedDraft is null) return true;
            content.CopyFrom(savedDraft, "*");
            content.TemplateId = savedDraft.TemplateId;
        }
        content.SetValue(property, draftChange ?? draft, culture);
        Rename();
        contents.Save(content);
#pragma warning restore CS0618
        return true;
    }
}
