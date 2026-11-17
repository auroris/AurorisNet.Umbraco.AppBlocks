namespace AppBlocks.Testing;

/// <summary>
/// Downloads paper reports through <c>/api/paper-reports</c>, as a report block's Print button does. The endpoint
/// belongs to AppBlocks.Documents and answers only for a Paper report node.
/// </summary>
public sealed class PaperReportsClient(HttpClient client, string pathBase)
{
    /// <summary>Downloads the paper report <paramref name="nodeKey"/>; <paramref name="query"/> carries its parameters, such as <c>?id=1</c>.</summary>
    public Task<HttpResponseMessage> DownloadAsync(Guid nodeKey, string? query = null)
        => client.GetAsync(Http.WithQuery($"{pathBase}/api/paper-reports/{nodeKey}", query));
}
