# AppBlocks

Forms, schedules, API endpoints, and database-backed application blocks for Umbraco 18.

![MIT license](https://raw.githubusercontent.com/auroris/AurorisNet.Build/main/badges/license-mit.svg)
![Umbraco 18](https://raw.githubusercontent.com/auroris/AurorisNet.Build/main/badges/umbraco-18.svg)
![.NET 10](https://raw.githubusercontent.com/auroris/AurorisNet.Build/main/badges/dotnet-10.svg)
![Status: alpha](https://raw.githubusercontent.com/auroris/AurorisNet.Build/main/badges/status-alpha.svg)
![SQLite, SQL Server, PostgreSQL and Oracle](https://raw.githubusercontent.com/auroris/AurorisNet.Build/main/badges/databases.svg)

![Interactive report of sample orders on an Umbraco page](https://raw.githubusercontent.com/auroris/AurorisNet.Umbraco.AppBlocks/main/docs/images/hero.png)

Add application blocks to your site's existing Block Grid or Block List without changing your templates, layout, or authentication:

- **Forms** bound to a registered `IFormResource` C# class or a published SQL data source (with field blocks, attachments, editable child tables, and field rules).
- **Schedules** plotting one data source against time with bars from another.
- **Anti-spam** (Turnstile, reCAPTCHA, hCaptcha, or honeypot) verified server-side.
- **JSON API endpoints** over SQL data sources at editor-defined routes.
- **Interactive reports** via `AurorisNet.Umbraco.AppBlocks.InteractiveReports`.
- **HTML/PDF documents** from Razor templates via `AurorisNet.Umbraco.AppBlocks.Documents`.

Blocks inherit page Public Access, with optional data source groups, row filters, and C# policies. Application data defaults to a separate SQLite file; SQL Server, PostgreSQL (`Npgsql`), and Oracle (`Oracle.ManagedDataAccess.Core`) connections are configured under **Settings → AppBlocks → Connections**.

![Order form with responsive fields, attachments, and editable order lines](https://raw.githubusercontent.com/auroris/AurorisNet.Umbraco.AppBlocks/main/docs/images/form.png)

## Packages and install

| Package | What it adds |
|---|---|
| `AurorisNet.Umbraco.AppBlocks` | Core forms, schedules, anti-spam, SQL data sources, API endpoints, and connections. |
| `AurorisNet.Umbraco.AppBlocks.InteractiveReports` | Interactive Report block and saved views (depends on `AppBlocks`). |
| `AurorisNet.Umbraco.AppBlocks.Documents` | Razor HTML/PDF document generation and Pressroom printing (depends on `AppBlocks`). |
| `AurorisNet.Umbraco.AppBlocks.Testing` | HTTP test clients, Umbraco test helpers, and schema/content snapshot checks. |

Currently alpha (`0.1.0-alpha.1`, not yet on nuget.org). Pack locally or reference the projects:

```powershell
dotnet pack src/AppBlocks -c Release -o artifacts/packages
dotnet nuget add source <path-to-artifacts/packages> --name appblocks-local
dotnet add package AurorisNet.Umbraco.AppBlocks --version 0.1.0-alpha.1
```

Each package auto-registers via an Umbraco composer and imports its element/data types on startup.

## Quick start

1. Allow **Forms container** (and **Interactive Report** if installed) on your Block Grid or Block List data type.
2. Create tables in `AppBlocksDb` or add a connection under **Settings → AppBlocks → Connections**.
3. Register `services.AddScoped<IFormResource, YourFormClass>()` or publish a **SQL data source** node.
4. Add a **Forms container** → **Form** block to a page, pick the interface, and add field blocks.

> **Security:** Form classes on pages without Public Access are publicly reachable unless the class enforces its own authorization—see [Business rules](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/docs/business-rules.md).

## Supported versions

- Umbraco 18: <!-- umbraco-releases 18 -->18.3.1 to 18.3.2<!-- /umbraco-releases --> (.NET 10)
- Databases: SQLite & SQL Server (included), PostgreSQL (`Npgsql`), Oracle 11.2+ (`Oracle.ManagedDataAccess.Core`)

## Documentation

| Guide | Covers |
|---|---|
| [Site integration](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/docs/site-integration.md) | Rendering, styling, caching, all `AppBlocks`/`Attachments` settings, extension points |
| [Business rules](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/docs/business-rules.md) | Access control, backing classes, validation, concurrency |
| [Connections](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/docs/connections.md) & [Database setup](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/docs/database-setup.md) | Default DB, extra connections, app DB schema migrations, table creation |
| [API endpoints](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/docs/api-endpoints.md) & [Backing classes](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/docs/csharp-editor.md) | JSON GET routes and in-backoffice C# classes |
| [Fields and blocks](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/docs/schedule.md) | [Schedule](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/docs/schedule.md), [Anti-spam](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/docs/anti-spam.md), [Display value](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/docs/display-value.md), [Shuttle](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/docs/shuttle.md), [Color picker](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/docs/color-picker.md), [Account picker](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/docs/account-picker.md), [Localisation](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/docs/localisation.md) |
| [Worked example](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/docs/activity-schedule-example.md) | Multi-tenant activity schedule walkthrough |
| Companion packages | [Interactive Reports](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/docs/interactive-reports.md), [Documents](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/docs/documents.md), [Testing](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/src/AppBlocks.Testing/README.md) |
| [Developing](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/docs/developing.md) & [Migrations](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/Migrations/README.md) | Building, tests, demo site, upgrade snapshots, and releases |

## License

[MIT](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/LICENSE)
