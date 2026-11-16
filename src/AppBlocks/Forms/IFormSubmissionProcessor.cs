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
