# Release migrations

The first release has two complete installers. At 1.0.0, `1.0.0-Base` and `1.0.0-Current` must contain identical files. Freeze Base when 1.0.0 ships and tag the release so its dependency versions and application code are reproducible.

For each release after 1.0.0, follow these steps.

1. Rename the single `*-Current` folder to the new version and update its clean installer.
2. Add a `<from>-<to>` folder for the change to an installed site. Use the same component and dialect paths for its SQL. For a release with no installation changes, add a short README to the transition folder so Git retains it. Keep previously shipped transition folders unchanged.
3. Install Current on an empty site. Separately, start the previous release, add representative user data, upgrade it and restart it. Compare the release-owned schema, starting data and definitions reached by both routes. Verify that existing records and editor drafts survive. Check the frozen folders against the previous release tag.

Each AppBlocks package has its own directory within the snapshot. The assemblies expose the definitions under `Schema/` for downstream callers. The attachment store reads its versioned SQL under `AppBlocks/Attachments/Schema` and keeps its existing schema name in `APP_SCHEMA`. Shared installer code lives in `Runtime/`.

`ApplicationSchema.FromReleaseAssembly` reads the embedded SQL. `FromReleaseDirectory` uses the same folders on disk. Fresh databases run the selected snapshot and record its version. Existing databases run the later transitions in numeric version order. SQL within one component transition runs in filename order with its completion record in the same transaction on SQLite, SQL Server and PostgreSQL. Oracle commits DDL implicitly, so its scripts must support retry after partial DDL.

When a DBA manages the attachment tables by hand, apply the attachment SQL from each pending transition during deployment. The database setup screen continues to supply the clean-install script and grants for this account.

Run the AppBlocks database, definition-installation and content-step tests against both snapshots at 1.0.0. Set the MSBuild property `InstallSnapshot=1.0.0-Base` to build the baseline. Ordinary builds select Current.

For an upgrade to stored CMS content, implement the patch in the transition folder and register it in Current’s `SchemaInstaller.ContentUpgrades`. Startup records the initial installation and runs pending patches once, in order.
