namespace AppBlocks.Forms;

/// <summary>The form admission check used while rendering a page.</summary>
public static class FormAccess
{
    /// <summary>
    /// Whether a form's class refuses an anonymous visitor on the load the runtime is about to make, so the page can show the
    /// sign-in notice in place of a form that would only ever fail. A class that throws, or answers anything else, gets the form:
    /// the runtime's own load then reports what happened. The class is asked once more by that load, so its Initialize and Load must be safe to repeat.
    /// </summary>
    public static async Task<bool> RefusesAnonymousAsync(FormDefinition form, Guid pageKey, HttpContext http)
    {
        if (FormResourceAccess.Refusal(form.Resource, http.User) is not null) return true;
        var query = http.Request.Query[form.KeyParameter];
        if (query.Count > 1 || query.Count == 1 && string.IsNullOrWhiteSpace(query[0])) return false;
        var context = new FormExecutionContext(http.User, pageKey, form.Block.ContentKey, form.Revision, Parameters: FormRequestValues.Parameters(http.Request.Query));
        try {
            if (form.Relationship is { } relation) {
                var resolved = await relation.ResolveAsync(form, context, http.RequestServices, http.RequestAborted);
                if (resolved.Refusal is { } refusal) return refusal.Status is 401 or 403;
                context = context with { ParentScope = resolved.Scope };
            }
            return (await form.LoadAsync(query.Count == 0 ? null : query[0], context, http.RequestAborted)).Status == 403;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ApplicationForms").LogWarning(error, "Form resource {Resource} failed while the page rendered for an anonymous visitor.", form.Resource.Alias);
            return false;
        }
    }
}
