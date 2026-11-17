# AppBlocks demo site

A plain Umbraco site that references AppBlocks as a project, for trying the packages during development.

```powershell
dotnet run --project samples/AppBlocks.Demo --urls http://localhost:5079
```

Complete Umbraco's installer in the browser, sign in and open **Settings → AppBlocks**.

## What the site contains

- A project reference to `AppBlocks.InteractiveReports`, which brings in AppBlocks. Both register through their composers.
- `DemoTasks`, a C# form class registered in `Program.cs` as an `IFormResource`, with the alias `demo.tasks`.
- `DemoSeed`, an optional seed that creates the `AppBlocksDemoTasks` table in the AppBlocks database and sample pages with a form and a report. The report blocks select `AppBlocksDb.AppBlocksDemoTasks`.
- A reference to `Umbraco.Cms.DevelopmentMode.Backoffice`, which turns on editing under **Settings → AppBlocks → Backing Classes**.

Application data goes in `AppBlocks.sqlite.db` beside Umbraco's SQLite database. Forms, saved report views and uploads share that database.

## Seeding sample content

Set `AppBlocksDemo:SeedContent` to `true` in `appsettings.Development.local.json` in the demo's folder. Git ignores that file, and it may also hold unattended installation settings for automated checks. Keep it out of commits and published output, because it holds credentials.

[Developing AppBlocks](../../docs/developing.md#the-demo-site) describes the HTTP test that checks the seeded demo.
