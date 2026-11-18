# Documents package

`AurorisNet.Umbraco.AppBlocks.Documents` produces documents from AppBlocks data sources and Razor templates, as self-contained HTML or as PDF through a [Pressroom](https://github.com/auroris/AurorisNet.Pressroom) server. It also gives other packages one service for sending HTML to a PDF or printer destination. This page covers the settings screen, authoring a document, the settings and the output service.

Install the package beside `AurorisNet.Umbraco.AppBlocks`, which it depends on. It works with or without the Interactive Reports package. Its composer registers the document engine, the routes, the settings screen and the schema.

## Pressroom configurations

Open **Settings → AppBlocks → Documents** as an administrator to add, edit or remove named Pressroom configurations. Each has a URL, an API key, an output and a timeout. A change applies at once, and several configurations can use one server with different outputs or keys.

- **Output.** **PDF** is always offered. **Load printers** asks the server's `GET /v1/printers` and adds the printers it returns.
- **API key.** Printer discovery and printer output require a key. PDF can omit it when the Pressroom server permits anonymous rendering.
- Saving a printer configuration checks that the server still offers that printer.
- A saved key is never sent to the browser. A blank key field keeps the saved key, and **Remove the saved API key** clears it.
- Changing the URL requires a new key or an explicit removal, so a saved key is never sent to another server.

### Where the configurations are stored

The configurations are kept in `App_Data/AppBlocks/documents.json` under `AppBlocks:DataRoot`, or under the site's content root when that is not set. API keys are protected with the site's Data Protection keys. Back up those keys with the file. As with AppBlocks' connections, the file suits a single application instance.

Until the first save on the screen, the configuration section `AppBlocks:Documents:Pressroom` supplies one **Pressroom PDF** configuration:

| Setting | Default |
|---|---|
| `AppBlocks:Documents:Pressroom:Url` | none |
| `AppBlocks:Documents:Pressroom:ApiKey` | none |
| `AppBlocks:Documents:Pressroom:TimeoutSeconds` | `65` |

The first edit on the screen saves that configuration with the rest of the collection. Deleting every configuration saves an empty collection, and the configured one stays away.

## Authoring a document

1. Create a **Document** (`paperReport`) content node. It can sit at the content root. To nest it under one of the site's pages, allow Document beneath that page's document type.
2. Choose a published **SQL data source**.
3. Select a Razor template whose model is `AppBlocks.Documents.PaperReports.PaperReportModel`.

The site's own templates supply the content and layout. The model gives a template the rows and columns, the bound parameters, the signed-in person, the culture, the generation time and the presentation theme.

| Property | What it does |
|---|---|
| **SQL data source** | The published data source that supplies the rows |
| **Parameters** | A JSON map of query parameter names to literals or bind variables, such as `{"id":":PARAM.id"}`. Empty forwards the request's query parameters |
| **Page options** | Pressroom's render options as JSON. See below |
| **File name** | The output file name, with `{id}`, other parameter names, `{date}` or `{date:yyyy-MM-dd}` |
| **Culture** | A culture name such as `en-GB` or `fr-FR`, or a bind variable such as `:PARAM.culture`. Empty uses the culture the site serves the download request in |
| **Maximum rows** | From 1 to 50000. Empty means 5000. A query that returns more rows fails the download |

**Page options** defaults: with no `pageSize` the renderer's default paper applies, with no `timeZone` the web server's time zone, and with no `footerTemplate` the footer shows the page number and page count.

### Templates the Document type offers

The Document type offers every installed template. At startup, and whenever a template or the type is saved, the package adds the templates the type lacks. It removes none. A site that chooses the type's templates itself sets `AppBlocks:Documents:AllowAllTemplates` to `false`.

## Downloading a document

A document is served at `/api/paper-reports/{key}`, where the key is the node's key.

- With a PDF configuration, the link answers with a PDF from the first PDF configuration. With none, or with `?format=html`, it answers with self-contained HTML.
- A GET request never submits to a printer.
- A PDF failure falls back to HTML and is logged.

**A document always needs a signed-in Member or backoffice User**, on a page without Public Access too. Access is checked against Umbraco's Public Access, which only the site's `IProtectedPageAdmission` can relax, and against the data source's read permissions. Knowing a document's URL grants no access.

## Styling and assets

The package places its print stylesheet, `/App_Plugins/AppBlocks.Documents/print.css`, first in every document.

- `AppBlocks:Documents:PrintStylesheet` names another site path, or none when it is empty.
- A template that styles itself leaves the stylesheet out with `<meta name="appblocks-print-stylesheet" content="none">` in its head.
- Register an `IDocumentPresentationProvider` to supply a theme name and a print stylesheet from code.

To make a document self-contained, the package fetches the images, stylesheets and fonts a template refers to and embeds them. It sends no cookies and follows no redirects.

**The package never fetches from loopback, link-local or private-network addresses**: 10/8, 172.16/12, 192.168/16, 100.64/10, `fc00::/7` and `fec0::/10`. A template may write a data value into an asset's address, and the server's own services live on those networks. Such a reference is kept as written and logged.

A template that embeds images from a server on the site's own network names it in `AppBlocks:Documents:AllowedAssetHosts`, as host names, addresses or CIDR ranges, such as `["reports.intranet", "10.20.0.0/16"]`. A host name listed there is fetched whatever address it resolves to.

## Settings

| Setting | Default | What it does |
|---|---|---|
| `AppBlocks:Documents:Pressroom:Url`, `ApiKey`, `TimeoutSeconds` | none, none, `65` | One PDF configuration, used until configurations are saved in the backoffice |
| `AppBlocks:Documents:AllowAllTemplates` | `true` | Whether the Document type is given every installed template |
| `AppBlocks:Documents:PrintStylesheet` | `/App_Plugins/AppBlocks.Documents/print.css` | The stylesheet placed first in every document. Empty places none |
| `AppBlocks:Documents:AllowedAssetHosts` | none | Hosts on private networks that asset embedding may fetch from |

## Linking to a document from another package

`IAppDocumentLinks` is in the base AppBlocks assembly, in the `AppBlocks.DocumentOutput` namespace, so a package can link to documents with no reference to this package.

1. Resolve it with `GetService<IAppDocumentLinks>()`. When it is absent, leave the document actions out.
2. Call `Url(documentKey, request.PathBase)` for the endpoint and append the record's parameters.
3. Use `target="_blank" rel="noopener"` on a link, or `window.open(url, "_blank", "noopener")` inside a user action.

The endpoint checks authorization on every request.

## The output service

`IDocumentOutputService` is also in the base AppBlocks assembly, namespace `AppBlocks.DocumentOutput`. A consumer needs no reference to Pressroom or to this package. Resolve it with `GetService<IDocumentOutputService>()`, and leave the output or Print controls out when it is absent or `GetDestinations()` returns nothing.

| Member | What it does |
|---|---|
| `GetDestinations()` | Returns each `DocumentDestination(Id, Name, Output, Printer)`. `Output` is a `DocumentOutputKind`. The descriptors hold no service URLs or keys |
| `SubmitAsync(configurationId, new DocumentRequest(html, fileName, options), cancellationToken)` | Sends complete, self-contained HTML to the chosen configuration and returns a `DocumentOutputResult` |

**The service performs no authorization.** The consumer must:

- Authorize the source data and the chosen destination.
- Render trusted HTML. Never pass on HTML or rows from a client without the application's own validation.
- Print only from an explicit user action, through a protected POST. Never print from a GET, a preview or a background retry.

### Render options

`DocumentRenderOptions`, with its page size, margins and readiness records, belongs to AppBlocks. Its defaults:

- No paper size, so the renderer's default applies.
- A footer of page numbers.
- `Locale` null, which is the culture of the request that submits the document.
- `TimeZone` null, which is the web server's time zone, `DocumentRenderOptions.ServerTimeZone`.

A caller that submits later than the request it formats for, as a print preview does, sets both `Locale` and `TimeZone`.

### Results

| Result | Meaning |
|---|---|
| `DocumentFileResult` | Holds `Content` (a stream), `ContentType` and `FileName`. `IsHtmlFallback` is true when the PDF service failed and the result is HTML |
| `DocumentPrintResult` | Holds `JobId`, `Printer` and `PageCount`. It confirms **submission** only. It is not a Windows spooler job id and does not confirm physical printing |
| `DocumentPrintException` | Carries `MayHavePrinted`. A timeout, a network failure or an unclear response may come after submission |
| `KeyNotFoundException` | The configuration id was removed. The service never switches to another configuration |

- Keep a result alive until copying or streaming finishes, then dispose it. `DocumentOutputResult` implements `IDisposable` and `IAsyncDisposable`. An ASP.NET Core handler can call `http.Response.RegisterForDispose(result)` and return `Results.Stream(file.Content, file.ContentType)`.
- **Never retry a print request automatically.** When `MayHavePrinted` is true, tell the user to check the printer's queue before offering a new submission. Cancellation by the caller can also leave the outcome unknown.

```csharp
var outputs = services.GetService<IDocumentOutputService>();
var destinations = outputs?.GetDestinations() ?? [];
// After authorization and the user's explicit selection and submission:
await using var result = await outputs!.SubmitAsync(selectedId,
    new DocumentRequest(trustedHtml, "report.pdf", new() {
        PageSize = new DocumentPageSize("A4"), Landscape = true,
    }), cancellationToken);
switch (result)
{
    case DocumentFileResult file:
        await file.Content.CopyToAsync(destinationStream, cancellationToken);
        break;
    case DocumentPrintResult receipt:
        // Show a submission receipt. Physical printing is not confirmed.
        break;
}
```

## Management API

The administrator-only management API is under `/umbraco/management/api/v1/appblocks/documents`:

| Request | Purpose |
|---|---|
| `GET settings` | The configurations, without their keys |
| `POST configurations` | Adds or changes a configuration |
| `DELETE configurations/{id}` | Removes a configuration |
| `POST printers` | Lists the printers of a server. It changes nothing |

The request types are in `PressroomConfigurations.cs`. A configuration's stored output is `pdf` or `printer:<identifier>`. The package installs no public endpoint that accepts HTML or prints.

## Building and testing

```powershell
dotnet build src/AppBlocks.Documents
dotnet run --project tests/AppBlocks.Documents.Tests -- -noLogo
dotnet pack src/AppBlocks.Documents -c Release -o artifacts/packages
```

The build restores `AurorisNet.Pressroom.Client` from NuGet. Its C# namespaces are `Pressroom.Client` and `Pressroom.Contracts`. [Developing AppBlocks](developing.md#dependencies-from-sibling-checkouts) describes building against a Pressroom checkout. Rendering runs on the Pressroom server, so the Umbraco process needs no browser and no PDF engine.
