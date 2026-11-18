using System.Text.Json;

namespace AppBlocks.Forms;

/// <summary>
/// A companion's server-side validation and canonical values for blocks in a published form.
/// Runs after admission, antiforgery, field validation and loading the existing record, before
/// persistence (including a master/detail transaction). A processor must only change fields its
/// own blocks bind. It must not use browser configuration as an authority.
/// </summary>
public interface IFormSubmissionProcessor
{
    Task<FormSubmissionValues> ProcessAsync(FormDefinition form, JsonElement values, FormRecord? stored,
        FormExecutionContext context, CancellationToken ct);
}

public sealed record FormSubmissionValues(JsonElement Values, FormResult? Refusal = null);

/// <summary>Adds a server-side validation to every AppBlocks form using the named resource.</summary>
public static class FormValidationExtensions
{
    /// <summary>Return a field error to stop the save, or null to continue. Values include stored fields on an update.</summary>
    public static IServiceCollection AddFormValidation(this IServiceCollection services, string resource,
        Func<JsonElement, FormExecutionContext, FormResult?> validate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        ArgumentNullException.ThrowIfNull(validate);
        return services.AddSingleton<IFormSubmissionProcessor>(new Validation(resource, validate));
    }

    private sealed class Validation(string resource, Func<JsonElement, FormExecutionContext, FormResult?> validate) : IFormSubmissionProcessor
    {
        public Task<FormSubmissionValues> ProcessAsync(FormDefinition form, JsonElement values, FormRecord? stored, FormExecutionContext context, CancellationToken ct)
            => Task.FromResult(new FormSubmissionValues(values, string.Equals(form.Resource.Alias, resource, StringComparison.OrdinalIgnoreCase)
                ? validate(values, context) : null));
    }
}
