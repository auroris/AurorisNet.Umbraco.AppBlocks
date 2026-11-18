# Clean-install test

This test builds the AppBlocks packages, installs them into new Umbraco sites from the official template, and checks installation, editing, data operations and a restart in a real browser.

## Running it

Run it from the repository root on Windows, with PowerShell 7, the .NET 10 SDK and Node.js installed:

```powershell
pwsh -NoProfile -File tests/clean-install/Invoke-CleanInstall.ps1
```

| Parameter | Default | What it does |
|---|---|---|
| `-InteractiveReportsRoot` | `../AurorisNet.InteractiveReports` | The Interactive Reports checkout whose projects the script packs, and whose `node_modules` supply Playwright. Run `npm ci` there first |
| `-Port` | `5189` | The port the test sites listen on |

The browser is Playwright's Chromium, or Microsoft Edge. The test sites run only while the checks run.

## What it sets up

The script packs the projects into a local feed and creates two sites with the Umbraco template matching the version in `Directory.Build.props`:

1. **core**: AppBlocks alone.
2. **companions**: AppBlocks with the Interactive Reports and Documents packages.

Each run has new SQLite databases and its own NuGet cache, in an `artifacts/clean-install-*` folder that git ignores. The sites use package references and the template's unchanged startup code, with no other CMS packages.

The core site starts with no connection string and no unattended installation. A browser completes the account, telemetry and SQLite steps of Umbraco's installer. The companions site installs unattended. The script fails on a fatal startup error in either site's log.

## What it checks

**On both sites, after installation:**

- Every embedded schema definition and its AppBlocks folder in the backoffice trees.
- The packaged JavaScript, CSS and manifests.
- Backoffice sign-in, the Connections screen, database metadata and connection testing.
- The Business rules help page and its section navigation.
- Rejection of anonymous access to the management API.
- That AppBlocks adds no content files and no PostgreSQL or Oracle driver to the site.
- That saving or testing a connection without its driver returns a validation error that names the package to install.

**On the companions site, with sample content.** The script adds website-owned sample content only after installation has passed. It then checks:

- Forms in a Block List and a Block Grid, required-field validation, the configured concurrency mode and antiforgery protection.
- Report queries and saved views.
- Attachment upload, download and removal.
- Document HTML output.
- Browser edits that survive a reload.
- In a new browser session, that the page sets its antiforgery cookie before several blocks load in parallel.

The script runs these checks again after restarting the site.

**Database drivers.** The companions site installs `Npgsql` and `Oracle.ManagedDataAccess.Core`, in the versions `tests/AppBlocks.Tests/AppBlocks.Tests.csproj` references, and adds the [Oracle assembly-scan exclusion](../../docs/connections.md#database-drivers). The seeded site and its restart verify that both drivers load and attempt connections, in both Oracle modes. The publish output must hold each driver only when the site installs it.

## What it leaves out

The test uses SQLite and HTML document output. It contacts no SQL Server, PostgreSQL or Oracle server and no Pressroom service. [Developing AppBlocks](../../docs/developing.md#live-database-tests) describes the live database tests.

## Results

Logs, JSON results and screenshots stay in the run's folder. The administrator account the script generates is for the test only and stays inside that folder.

## Related checks in the test projects

- `SchemaFolderChecks` verifies folder placement on a new site, and repeated installation with no duplicate folders.
- `WizardSiteTests` covers the runtime before installation, with no database.
- `SchemaInstallationChecks` verifies installation outside `RuntimeLevel.Run`, and saving and publishing table names and 20,000-character SQL sources on a new site.
- `tests/ui/table-picker.browser.test.mjs` checks the table dropdown and SQL editing as a plain string.
