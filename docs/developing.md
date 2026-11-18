# Developing AppBlocks

How to build and test the packages in this repository, run the demo site, and change the definitions the packages install.

## Build, test and pack

Building needs the .NET 10 SDK. The projects build against the Umbraco release in `UmbracoVersion` in `Directory.Build.props`.

```powershell
dotnet build AppBlocks.slnx
dotnet run --project tests/AppBlocks.Tests -- -noLogo
dotnet run --project tests/AppBlocks.InteractiveReports.Tests -- -noLogo
dotnet run --project tests/AppBlocks.Documents.Tests -- -noLogo
dotnet pack src/AppBlocks -c Release -o artifacts/packages
dotnet pack src/AppBlocks.InteractiveReports -c Release -o artifacts/packages
dotnet pack src/AppBlocks.Documents -c Release -o artifacts/packages
dotnet pack src/AppBlocks.Testing -c Release -o artifacts/packages
```

The three test projects and the [clean-install test](#clean-install-test) are the full test run. The workflows on GitHub run the three test projects on one Umbraco release. To build and test against another release, add `--property:UmbracoVersion=<release>` to each command, before the `--` of a `dotnet run`.

## Dependencies from sibling checkouts

The companions restore their dependencies from NuGet: `AurorisNet.InteractiveReports`, `AurorisNet.InteractiveReports.AspNetCore` and `AurorisNet.InteractiveReports.Client.FileDownload` for reports, and `AurorisNet.Pressroom.Client` for documents. To build the reports companion against Interactive Reports' source, pass `-p:UseInteractiveReportsSource=true`. It reads the checkout at `../AurorisNet.InteractiveReports`; the `InteractiveReportsRoot` property names another location. The Pressroom client always comes from its NuGet package.

The core `AppBlocks` project builds without either.

## Live database tests

The ordinary test run uses SQLite and SQL Server LocalDB and contacts no remote server. Two test classes run the same scenarios on a real server. They run only when a command asks for them with `-explicit only`, and each reads its connection string from an environment variable, which keeps credentials out of source control.

**SQL Server.** Supply an empty, disposable database and an account with `db_owner` on it. The tests cover form data sources, application schema installation and attachment storage.

```powershell
$env:APPBLOCKS_SQLSERVER_CONNECTION = "Server=HOST,1433;Database=TEST_DATABASE;User ID=TEST_USER;Password=TEST_PASSWORD;Encrypt=True;TrustServerCertificate=True;"
dotnet run --project tests/AppBlocks.Tests -- -noLogo -explicit only -class AppBlocks.Tests.SqlServerLiveTests
```

`TrustServerCertificate=True` suits a test server with a self-signed certificate. The tests refuse to run when one of their object names already exists, and they drop their own objects afterwards.

**Oracle.** Supply a test schema whose account has `CREATE SESSION`, `CREATE TABLE`, `CREATE VIEW`, `CREATE SEQUENCE` and a tablespace quota.

```powershell
$env:APPBLOCKS_ORACLE_CONNECTION = "User Id=TEST_USER;Password=TEST_PASSWORD;Data Source=HOST:1521/SERVICE_NAME;"
dotnet run --project tests/AppBlocks.Tests -- -noLogo -explicit only -class AppBlocks.Tests.OracleLiveTests
```

The tests run once in `oracle11g` mode and once in `oracle` mode, with the `Oracle.ManagedDataAccess.Core` driver the test project references. They create uniquely named tables, views and sequences and drop them afterwards.

## Browser tests

The tests in `tests/ui` drive the backoffice screens and the browser runtime with Playwright:

```powershell
node --test tests/ui/
```

They load Playwright from the checkout at `../AurorisNet.InteractiveReports`, after `npm ci` there. Set `INTERACTIVE_REPORTS_ROOT` for a checkout elsewhere. Set `BROWSER_PATH` to an installed Chromium browser when Playwright's own is missing.

## Clean-install test

`tests/clean-install/Invoke-CleanInstall.ps1` builds NuGet packages, installs them into new Umbraco sites and checks installation, editing, data operations and a restart in a browser. It needs PowerShell 7 and Node.js. The [test's README](../tests/clean-install/README.md) describes what it checks.

```powershell
pwsh -NoProfile -File tests/clean-install/Invoke-CleanInstall.ps1
```

The script packs the Interactive Reports projects from the checkout at `../AurorisNet.InteractiveReports`; `-InteractiveReportsRoot` names another location. The Pressroom client restores from nuget.org.

## The demo site

```powershell
dotnet run --project samples/AppBlocks.Demo --urls http://localhost:5079
```

The demo references the reports companion, which brings in AppBlocks. It also references `Umbraco.Cms.DevelopmentMode.Backoffice`, so **Settings → AppBlocks → Backing Classes** edits and compiles its `AppBlocks/Code` folder, which git ignores. Remove that package reference to see [Backing Classes](csharp-editor.md) read-only.

The demo can seed sample content: a task table, and an article page with a Block List and a Block Grid. The seed is website code in the demo, outside the packages. Turn it on with `AppBlocksDemo:SeedContent` in the demo's `appsettings.Development.local.json`, which git ignores and publishing leaves out. Use it on a local SQLite installation only.

`DemoHttpTests` in the reports test project checks the seeded demo over HTTP. It runs when `APPBLOCKS_DEMO_SETTINGS` names that settings file, and signs in with the unattended account in it:

```powershell
$env:APPBLOCKS_DEMO_SETTINGS = "$PWD/samples/AppBlocks.Demo/appsettings.Development.local.json"
dotnet run --project tests/AppBlocks.InteractiveReports.Tests -- -noLogo
```

## The Testing package

`src/AppBlocks.Testing` holds the HTTP clients a site's tests use to drive the forms, attachments, tabular, report and paper-report endpoints, with the helpers for running Umbraco in a test process. `AppBlocks.Tests` references the project and tests the clients against the endpoints.

- Change a client in the same commit as the endpoint protocol it drives.
- Keep the package free of any test framework reference, because sites test with different runners.

## Uppy assets

The attachment uploader is a bundle of Uppy's Core, Dashboard and XHR Upload. Its pinned dependencies and bundler are in `tools/uppy`. After updating them, run `npm ci` and `npm run build` there. Commit the lockfile and the generated `uppy.js`, `uppy.css` and `uppy-LICENSE.txt` under `src/AppBlocks/wwwroot/App_Plugins/ApplicationBlocks`.

.NET builds and sites use the committed files, so they need neither Node.js nor a CDN. The browser loads the bundle when someone opens **Upload**.

## Code organization

The core package and each companion own their code and assets. The Interactive Reports engine is a dependency, and its AppBlocks integration is in `src/AppBlocks.InteractiveReports`.

- C# is grouped by feature, controllers included. `Management` holds the settings controller that spans features. Form inventory and sample generation are in `Forms/Authoring`, and backing-class generation is in `Code/Generation`.
- `FormEndpoints` handles HTTP admission, antiforgery and serialization. `FormSubmissionService` validates and saves a record and its detail rows, and uses `TabularOperations` for row validation. `FormRequestValues` parses the bounded request values.
- Companions call public operations of the core. `DataSourceReadQuery` supplies read SQL and its bindings, and callers authorize through `DataSourceQueries.AuthorizeAsync`. The companions have no friend-assembly access to core internals.
- `runtime.js` mounts the browser features. Forms, attachments, tabular rows, anti-spam and field controls are separate modules, and `form-coordination.js` handles their registration and the exchange of submission keys. Keep the runtime's entry URL and the form events when changing these modules.
- Core addresses blocks through `AppBlockContent`. The reports companion resolves its report blocks through `ReportBlocks`.

### Backoffice screens

Each Settings tab is a `workspaceView` manifest conditioned on `AppBlocks.Workspace`, and companions register views of their own. Management requests go through `umbHttpClient`, so Umbraco handles authentication and the application's base URL. A link into a backoffice section starts from `backofficePath`, exported by `App_Plugins/AppBlocks/api.js`. A repository shares the picker catalogue for 30 seconds for each authentication context.

### Published content lookups

`AppBlockContent.PageAsync`, `ReportAsync` and `FormDefinition.ResolveAsync` await published-content lookups. Call `LoadAsync` on a catalogue, a resource registry, a schedule source collection or an `AppBlocksValidator` before using its synchronous members. Controllers, publishing notifications and rendering do this already. A provider of form resources may implement `IFormResourceProvider.GetResourcesAsync`.

Saved block values are read through Umbraco's Block Grid, Block List and Single Block converters. A form's Form Interface uses Umbraco's Single Block editor and resolves to one `BlockListItem`.

## Who owns the definitions

The `Schema/` folder of each package is the only source of its element types, data types and document types. Change a definition there. A new site installs every definition through Umbraco's package importer, which a package migration starts.

Installation imports only the definitions a site lacks. A change to a definition reaches an existing site in one of two ways.

### A migration step

Use a step for a change that a released version carries to every site. Each package has an Umbraco package migration plan: `AppBlocksMigrationPlan`, `InteractiveReportsMigrationPlan` and `DocumentsMigrationPlan`. At startup Umbraco runs the steps a site has not reached and records the last one in its key-value table under the plan's name. A new site runs every step.

- A step is a class deriving from `AsyncMigrationBase`. It takes the Umbraco services it needs in its constructor and changes the definition, or rewrites block values, through them. `AddDataSourceBackingClass` is the example to follow.
- Add the step after the last one in `DefinePlan`, as `To<TheStep>("yyyy-MM-dd-what-it-does")`. Once a site may have run a step, keep its name and its place in the plan.
- A step before `InstallPackageDefinitions` runs before the definitions exist on a new site, so it must accept that. Later steps run after the missing definitions are imported.
- Every schema step follows `Umbraco:CMS:PackageMigration:RunSchemaAndContentMigrations`. When the setting is `false`, the steps change nothing.
- A schema step takes Umbraco's key-value write lock before it reads. Each step writes its migration state when it completes, and taking the lock first avoids upgrading a SQLite read transaction while other startup work is writing.

Check a new step as the clean-install test does: install the previous package, start the new one, and compare each definition with a new install of the new package.

### Reinstall

**Settings → AppBlocks → Definitions → Reinstall** resets every definition to the installed package, by key, in one transaction: names, properties, allowed children, data type settings and folders. Use it for a change that no step carries, and during development after editing a schema file.

- Content keeps its values, because each property is matched by alias. Values of a property the package no longer has are removed.
- A type of the site's own that has taken one of the package's aliases is left as it is, and the screen says so.
- Reinstall works when automatic schema migrations are turned off, because an administrator starts it.
- A site that imports these definitions through a synchronization tool, and changes some of them on purpose, gets them reset by a Reinstall until its next import.

**Uninstall** on the same screen removes every definition and the folders it leaves empty. It refuses while documents, library elements, block configurations, compositions or saved block values still use a definition, or while a property of the site uses one of the data types, and names what to remove first. After an Uninstall, startup leaves the definitions out until someone chooses Reinstall.

### Definition keys

`AppBlockSchema.Key` derives each definition's key from its name and a fixed seed, so a definition keeps its key across versions. Changing the seed changes every key, and no migration step can carry that change to an existing site.

A site that imports definitions before Umbraco's package migrations run can call `SchemaInstaller.EnsureAsync` from a service scope first. The call is safe to repeat and follows the schema migration setting.
