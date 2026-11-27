using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AppBlocks.Authentication;
using AppBlocks.Connections;
using Microsoft.Extensions.Options;
using AppBlocks.Databases;

namespace AppBlocks.Attachments;

/// <summary>The record a file belongs to: a form resource's alias and the record's key, as the form endpoints know them.</summary>
public sealed record AttachmentOwner(string Alias, string Key)
{
    /// <summary>An earlier owner alias (such as a SQL data source's alias before durable document references) still matched on reads, renames and removals.</summary>
    public string? LegacyAlias { get; init; }

    internal bool HasLegacyAlias => !string.IsNullOrWhiteSpace(LegacyAlias) && !string.Equals(LegacyAlias, Alias, StringComparison.Ordinal);

    public static string ResourceId(Forms.IFormResource resource) => resource is DataSources.SqlDataSource sql
        ? Blocks.AppBlockValues.Udi(sql.Definition.Key) : resource.Alias;

    public static AttachmentOwner ForResource(Forms.IFormResource resource, string key) => resource is DataSources.SqlDataSource sql
        ? new(ResourceId(resource), key) { LegacyAlias = sql.Alias } : new(resource.Alias, key);

    /// <summary>Prefix for files waiting for a new record to be saved.</summary>
    public const string PendingPrefix = "pending:";

    /// <summary>
    /// The owner of files uploaded for one submission of a form whose record is not saved yet: the resource's alias after
    /// <see cref="PendingPrefix"/>, and the submission key followed by the uploader's account key, so only the uploader
    /// reaches the files. An anonymous visitor has no account and is identified by the submission key alone.
    /// </summary>
    public static AttachmentOwner Pending(string resourceAlias, Guid submissionKey, ClaimsPrincipal uploader)
        => new(PendingPrefix + resourceAlias, PendingKey(submissionKey) + (uploader.Identities.Any(identity => identity.IsAuthenticated) ? ApplicationAccessPolicy.UserKey(uploader) : ""));

    /// <summary>What the key of every pending owner of one submission starts with, whoever uploaded.</summary>
    internal static string PendingKey(Guid submissionKey) => submissionKey.ToString("D") + ":";

    public bool IsPending => Alias.StartsWith(PendingPrefix, StringComparison.Ordinal);

    /// <summary>The form resource's alias, also for a pending owner.</summary>
    public string ResourceAlias => IsPending ? Alias[PendingPrefix.Length..] : Alias;
}

/// <summary>One of an owner's files. A file is stored once and never replaced: it can be renamed or removed, and a new version is a new file.</summary>
public sealed record AttachmentItem(long Id, string FileName, string ContentType, long Size, string Sha256, string? Purpose, string? Label, string? Notes,
    string UploadedBy, DateTime UploadedAt, string UpdatedBy, DateTime UpdatedAt);

public sealed record AttachmentContent(long Id, string FileName, string ContentType, long Size, string Sha256, byte[] Bytes);

public sealed record AttachmentUpload(string FileName, string ContentType, byte[] Bytes);

/// <summary>What a purge deleted: removed links, their files, and those files' bytes.</summary>
public sealed record AttachmentPurge(int Links, int Files, long Bytes);

/// <summary>
/// The two attachment tables, in whichever registered connection <see cref="AttachmentSettings.Connection"/> names.
/// Every method takes the owner and checks it in SQL: a file id that is not the file of one of that owner's live links is
/// simply not found, so a readable record can never be paired with another record's file. The store runs on SQLite, SQL Server, PostgreSQL and Oracle 12c or later, so the files can sit beside
/// the application's data; the PostgreSQL and Oracle scripts and SQL are written from each database's documentation and have not
/// been run against a live server (see <see cref="Sql"/>). Every way the database can refuse the store surfaces as an
/// <see cref="AttachmentStoreException"/>, and a problem that needs an administrator is logged once, with what to do, until it goes away.
/// </summary>
public sealed partial class AttachmentStore(IApplicationConnectionFactory connections, IOptions<AttachmentSettings> settings, ILogger<AttachmentStore> logger)
{
    public static readonly ApplicationSchema Schema = ApplicationSchema.FromReleaseAssembly(typeof(AttachmentStore).Assembly, "AppBlocks/Attachments/Schema", "AppBlocks:Attachments/Schema");

    /// <summary>The tables the store reads and writes, which an administrator can also create from the schema's script.</summary>
    public static readonly IReadOnlyList<string> Tables = ["APP_ATTACHMENT", "APP_ATTACHMENT_LINK"];

    /// <summary>Which problem has been logged per connection name, because a request's store does not outlive the request.</summary>
    private static readonly ConcurrentDictionary<string, AttachmentStoreProblem> Reported = new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<AttachmentItem>> ListAsync(AttachmentOwner owner, CancellationToken ct) => GuardAsync(write: false, async () =>
    {
        await using var connection = await OpenAsync(ct);
        return await ListAsync(connection, null, owner, null, ct);
    });

    /// <summary>The file of one of the owner's live links, with its bytes; null when it is not one.</summary>
    public Task<AttachmentContent?> ReadAsync(AttachmentOwner owner, long id, CancellationToken ct) => GuardAsync<AttachmentContent?>(write: false, async () =>
    {
        await using var connection = await OpenAsync(ct);
        var (whereAlias, aliasParams) = OwnerFilter("l.", owner);
        await using var command = Command(connection, null, $"""
            SELECT a.ATTACHMENT_ID, a.FILE_NAME, a.CONTENT_TYPE, a.SIZE, a.SHA256, a.CONTENT
            FROM APP_ATTACHMENT_LINK l JOIN APP_ATTACHMENT a ON a.ATTACHMENT_ID = l.ATTACHMENT_ID
            WHERE {whereAlias} AND l.OWNER_KEY = @key AND l.REMOVED_AT IS NULL AND l.ATTACHMENT_ID = @id
            """, [.. aliasParams, ("@key", owner.Key), ("@id", id)]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new AttachmentContent(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), reader.GetString(4), (byte[])reader.GetValue(5));
    });

    public Task<AttachmentItem> AddAsync(AttachmentOwner owner, AttachmentUpload file, string? purpose, string? label, string? notes, string by, CancellationToken ct) => GuardAsync(write: true, async () =>
    {
        await using var connection = await OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow;
        var id = await InsertFileAsync(connection, transaction, file, by, now, ct);
        await using var link = Command(connection, transaction, """
            INSERT INTO APP_ATTACHMENT_LINK (OWNER_ALIAS, OWNER_KEY, ATTACHMENT_ID, PURPOSE, LABEL, NOTES, CREATED_BY, CREATED_AT, UPDATED_BY, UPDATED_AT)
            VALUES (@alias, @key, @id, @purpose, @label, @notes, @by, @now, @by, @now)
            """, ("@alias", owner.Alias), ("@key", owner.Key), ("@id", id), ("@purpose", Trim(purpose)), ("@label", Trim(label)), ("@notes", Trim(notes)), ("@by", by), ("@now", now));
        await link.ExecuteNonQueryAsync(ct);
        var item = (await ListAsync(connection, transaction, owner, id, ct)).Single();
        await transaction.CommitAsync(ct);
        return item;
    });

    /// <summary>Renames one of an owner's files without changing its bytes or id. Null when the id is not the file of one of the owner's live links.</summary>
    public Task<AttachmentItem?> RenameAsync(AttachmentOwner owner, long id, string fileName, string by, CancellationToken ct) => GuardAsync<AttachmentItem?>(write: true, async () =>
    {
        await using var connection = await OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var (whereAlias, aliasParams) = OwnerFilter("", owner);
        await using var link = Command(connection, transaction, $"""
            UPDATE APP_ATTACHMENT_LINK SET OWNER_ALIAS = @alias, UPDATED_BY = @by, UPDATED_AT = @now
            WHERE {whereAlias} AND OWNER_KEY = @key AND ATTACHMENT_ID = @id AND REMOVED_AT IS NULL
            """, [("@by", by), ("@now", DateTime.UtcNow), .. aliasParams, ("@key", owner.Key), ("@id", id)]);
        if (await link.ExecuteNonQueryAsync(ct) != 1) return null;
        await using var rename = Command(connection, transaction,
            "UPDATE APP_ATTACHMENT SET FILE_NAME = @name WHERE ATTACHMENT_ID = @id", ("@name", fileName), ("@id", id));
        await rename.ExecuteNonQueryAsync(ct);
        var item = (await ListAsync(connection, transaction, owner, id, ct)).Single();
        await transaction.CommitAsync(ct);
        return item;
    });

    /// <summary>Removes the link whose file is <paramref name="id"/>: it leaves the lists, and its rows stay for the purge. False when it is not one of the owner's files.</summary>
    public Task<bool> RemoveAsync(AttachmentOwner owner, long id, string by, CancellationToken ct) => GuardAsync(write: true, async () =>
    {
        await using var connection = await OpenAsync(ct);
        var (whereAlias, aliasParams) = OwnerFilter("", owner);
        await using var command = Command(connection, null, $"""
            UPDATE APP_ATTACHMENT_LINK SET REMOVED_BY = @by, REMOVED_AT = @now
            WHERE {whereAlias} AND OWNER_KEY = @key AND ATTACHMENT_ID = @id AND REMOVED_AT IS NULL
            """, [("@by", by), ("@now", DateTime.UtcNow), .. aliasParams, ("@key", owner.Key), ("@id", id)]);
        return await command.ExecuteNonQueryAsync(ct) == 1;
    });

    /// <summary>Moves stored attachment links from an earlier owner alias (such as a SQL data source alias) to a durable owner identifier.</summary>
    public Task<int> MigrateOwnerAsync(string fromAlias, string toAlias, CancellationToken ct = default) => GuardAsync(write: true, async () =>
    {
        if (string.IsNullOrWhiteSpace(fromAlias) || string.IsNullOrWhiteSpace(toAlias) || string.Equals(fromAlias, toAlias, StringComparison.Ordinal)) return 0;
        await using var connection = await OpenAsync(ct);
        await using var command = Command(connection, null, """
            UPDATE APP_ATTACHMENT_LINK SET OWNER_ALIAS = @to
            WHERE OWNER_ALIAS = @from
            """, ("@to", toAlias), ("@from", fromAlias));
        return await command.ExecuteNonQueryAsync(ct);
    });

    /// <summary>Deletes the links removed before <paramref name="removedBefore"/> with their files.</summary>
    public Task<AttachmentPurge> PurgeAsync(DateTime removedBefore, CancellationToken ct) => GuardAsync(write: true, async () =>
    {
        await using var connection = await OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var purge = await DeleteLinksAsync(connection, transaction, "REMOVED_AT IS NOT NULL AND REMOVED_AT < @before", ct, ("@before", removedBefore));
        await transaction.CommitAsync(ct);
        if (purge.Links > 0) logger.LogInformation("Purged {Links} removed attachment link(s) and {Files} file(s), {Bytes} bytes.", purge.Links, purge.Files, purge.Bytes);
        return purge;
    });

    /// <summary>
    /// Deletes every attachment link belonging to (<paramref name="resource"/>, <paramref name="recordKey"/>) and any attachment rows
    /// that are no longer referenced by any link, opening the store's own connection and transaction.
    /// </summary>
    public Task<AttachmentPurge> PurgeOwnerAsync(string resource, string recordKey, CancellationToken ct = default) => GuardAsync(write: true, async () =>
    {
        await using var connection = await OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var purge = await PurgeOwnerAsync(connection, transaction, resource, recordKey, ct);
        await transaction.CommitAsync(ct);
        return purge;
    });

    /// <summary>
    /// Deletes every attachment link belonging to (<paramref name="resource"/>, <paramref name="recordKey"/>) and any attachment rows
    /// that are no longer referenced by any link, using the caller's open connection and transaction.
    /// Neither connection nor transaction is disposed or committed here, so record deletion and attachment cleanup commit together.
    /// </summary>
    public static Task<AttachmentPurge> PurgeOwnerAsync(DbConnection connection, DbTransaction? transaction,
        string resource, string recordKey, CancellationToken ct = default)
        => PurgeOwnerAsync(connection, transaction, [(resource, recordKey)], ct);

    /// <summary>
    /// Deletes every attachment link belonging to any of <paramref name="resources"/> for <paramref name="recordKey"/> and any
    /// now-unreferenced attachment rows, using the caller's open connection and transaction.
    /// </summary>
    public static Task<AttachmentPurge> PurgeOwnerAsync(DbConnection connection, DbTransaction? transaction,
        IEnumerable<string> resources, string recordKey, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resources);
        return PurgeOwnerAsync(connection, transaction, resources.Select(resource => (resource, recordKey)), ct);
    }

    /// <summary>
    /// Deletes every attachment link belonging to any of <paramref name="owners"/> and any attachment rows that are no longer
    /// referenced by any link, using the caller's open connection and transaction.
    /// </summary>
    public static async Task<AttachmentPurge> PurgeOwnerAsync(DbConnection connection, DbTransaction? transaction,
        IEnumerable<(string Resource, string RecordKey)> owners, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(owners);
        var deletedLinks = 0;
        foreach (var (resource, recordKey) in owners)
        {
            if (string.IsNullOrWhiteSpace(resource) || string.IsNullOrWhiteSpace(recordKey)) continue;
            await using var deleteLinks = Command(connection, transaction, """
                DELETE FROM APP_ATTACHMENT_LINK
                WHERE OWNER_ALIAS = @alias AND OWNER_KEY = @key
                """, ("@alias", resource), ("@key", recordKey));
            deletedLinks += await deleteLinks.ExecuteNonQueryAsync(ct);
        }
        if (deletedLinks == 0) return new AttachmentPurge(0, 0, 0);
        long bytes;
        await using (var sum = Command(connection, transaction, """
            SELECT COALESCE(SUM(SIZE), 0) FROM APP_ATTACHMENT a
            WHERE NOT EXISTS (SELECT 1 FROM APP_ATTACHMENT_LINK l WHERE l.ATTACHMENT_ID = a.ATTACHMENT_ID)
            """))
            bytes = Convert.ToInt64(await sum.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        await using var deleteFiles = Command(connection, transaction, """
            DELETE FROM APP_ATTACHMENT
            WHERE NOT EXISTS (SELECT 1 FROM APP_ATTACHMENT_LINK l WHERE l.ATTACHMENT_ID = APP_ATTACHMENT.ATTACHMENT_ID)
            """);
        var deletedFiles = await deleteFiles.ExecuteNonQueryAsync(ct);
        return new AttachmentPurge(deletedLinks, deletedFiles, bytes);
    }

    /// <summary>
    /// Gives the pending owner's live files to <paramref name="owner"/>, the record its form has saved, and returns how many moved.
    /// The files keep their ids, so a list the runtime holds stays valid.
    /// </summary>
    public Task<int> AdoptAsync(AttachmentOwner pending, AttachmentOwner owner, string by, CancellationToken ct) => GuardAsync(write: true, async () =>
    {
        await using var connection = await OpenAsync(ct);
        return await AdoptAsync(connection, null, pending, owner, by, ct);
    });

    /// <summary>
    /// Opens the attachments database, its tables ensured as every store operation ensures them, for a resource that adopts
    /// pending files in its own create transaction with <see cref="AdoptAsync(DbConnection, DbTransaction?, AttachmentOwner, AttachmentOwner, string, CancellationToken)"/>.
    /// The record's tables must be in this database, so the record and the adoption commit together. The caller begins and
    /// commits the transaction and disposes the connection. A database that cannot be used throws an <see cref="AttachmentStoreException"/>.
    /// </summary>
    public Task<DbConnection> OpenConnectionAsync(CancellationToken ct) => GuardAsync(write: false, () => OpenAsync(ct));

    /// <summary>
    /// Adopts files on the caller's open attachment database connection and transaction, opened with
    /// <see cref="OpenConnectionAsync"/> so the attachment tables are there before the transaction begins. The caller authorizes
    /// both owners. Neither connection nor transaction is disposed or committed here, so record creation and adoption can commit together.
    /// </summary>
    public static async Task<int> AdoptAsync(DbConnection connection, DbTransaction? transaction,
        AttachmentOwner pending, AttachmentOwner owner, string by, CancellationToken ct)
    {
        if (!pending.IsPending || owner.IsPending || pending.ResourceAlias != owner.Alias)
            throw new ArgumentException("Adopt pending files into a saved record of the same resource.");
        await using var command = Command(connection, transaction, """
            UPDATE APP_ATTACHMENT_LINK SET OWNER_ALIAS = @alias, OWNER_KEY = @key, UPDATED_BY = @by, UPDATED_AT = @now
            WHERE OWNER_ALIAS = @pendingAlias AND OWNER_KEY = @pendingKey AND REMOVED_AT IS NULL
            """, ("@alias", owner.Alias), ("@key", owner.Key), ("@by", by), ("@now", DateTime.UtcNow), ("@pendingAlias", pending.Alias), ("@pendingKey", pending.Key));
        return await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Whether another uploader than <paramref name="pending"/>'s has live files pending under the same submission key.</summary>
    public Task<bool> PendingForOthersAsync(AttachmentOwner pending, Guid submissionKey, CancellationToken ct) => GuardAsync(write: false, async () =>
    {
        await using var connection = await OpenAsync(ct);
        // The submission key is a GUID and the colon after it is no LIKE wildcard, so the prefix matches literally in every database.
        await using var command = Command(connection, null, """
            SELECT COUNT(*) FROM APP_ATTACHMENT_LINK
            WHERE OWNER_ALIAS = @alias AND OWNER_KEY LIKE @submission AND OWNER_KEY <> @key AND REMOVED_AT IS NULL
            """, ("@alias", pending.Alias), ("@submission", AttachmentOwner.PendingKey(submissionKey) + "%"), ("@key", pending.Key));
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) > 0;
    });

    /// <summary>
    /// Deletes the pending files uploaded more than <see cref="AttachmentSettings.PendingLifetimeHours"/> ago, whose form was never
    /// saved, with their rows. The hourly cleanup job and the pending endpoints call this.
    /// </summary>
    public Task<int> ExpirePendingAsync(CancellationToken ct) => GuardAsync(write: true, async () =>
    {
        await using var connection = await OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        // A file adopted after it was selected no longer matches when its link is deleted, so it keeps its record.
        var expired = await DeleteLinksAsync(connection, transaction, "OWNER_ALIAS LIKE @pending AND CREATED_AT < @before", ct,
            ("@pending", AttachmentOwner.PendingPrefix + "%"),
            ("@before", DateTime.UtcNow.AddHours(-Math.Max(0, settings.Value.PendingLifetimeHours))));
        await transaction.CommitAsync(ct);
        return expired.Links;
    });

    /// <summary>
    /// Deletes the links that <paramref name="where"/>, a condition on <c>APP_ATTACHMENT_LINK</c>, selects, each with its file. Each
    /// link is deleted only while it still meets the condition, so one another request changed after it was selected, such as a
    /// pending file adopted meanwhile, keeps its file. The database's own write locking decides that race, at any isolation level.
    /// </summary>
    private static async Task<AttachmentPurge> DeleteLinksAsync(DbConnection connection, DbTransaction transaction, string where, CancellationToken ct, params (string Name, object? Value)[] values)
    {
        var links = new List<(long Link, long Attachment, long Size)>();
        await using (var selected = Command(connection, transaction, $"""
            SELECT l.LINK_ID, l.ATTACHMENT_ID, a.SIZE FROM APP_ATTACHMENT_LINK l JOIN APP_ATTACHMENT a ON a.ATTACHMENT_ID = l.ATTACHMENT_ID
            WHERE l.LINK_ID IN (SELECT LINK_ID FROM APP_ATTACHMENT_LINK WHERE {where})
            """, values))
        await using (var reader = await selected.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) links.Add((reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2)));
        var deleted = 0; var files = 0; var bytes = 0L;
        foreach (var (link, attachment, size) in links)
        {
            // The link references its file, so the link goes first.
            await using (var delete = Command(connection, transaction, "DELETE FROM APP_ATTACHMENT_LINK WHERE LINK_ID = @link AND " + where, [("@link", link), .. values]))
                if (await delete.ExecuteNonQueryAsync(ct) == 0) continue;
            deleted++;
            await using var file = Command(connection, transaction, "DELETE FROM APP_ATTACHMENT WHERE ATTACHMENT_ID = @id", ("@id", attachment));
            files += await file.ExecuteNonQueryAsync(ct);
            bytes += size;
        }
        return new AttachmentPurge(deleted, files, bytes);
    }

    /// <summary>The owner's items (one when <paramref name="id"/> names one of its files), newest first.</summary>
    private static async Task<IReadOnlyList<AttachmentItem>> ListAsync(DbConnection connection, DbTransaction? transaction, AttachmentOwner owner, long? id, CancellationToken ct)
    {
        var (whereAlias, aliasParams) = OwnerFilter("l.", owner);
        (string, object?)[] selection = id is null ? [.. aliasParams, ("@key", owner.Key)] : [.. aliasParams, ("@key", owner.Key), ("@id", id)];
        await using var command = Command(connection, transaction, $"""
            SELECT a.ATTACHMENT_ID, a.FILE_NAME, a.CONTENT_TYPE, a.SIZE, a.SHA256, l.PURPOSE, l.LABEL, l.NOTES, a.UPLOADED_BY, a.UPLOADED_AT, l.UPDATED_BY, l.UPDATED_AT
            FROM APP_ATTACHMENT_LINK l JOIN APP_ATTACHMENT a ON a.ATTACHMENT_ID = l.ATTACHMENT_ID
            WHERE {whereAlias} AND l.OWNER_KEY = @key AND l.REMOVED_AT IS NULL
            """ + (id is null ? "" : " AND l.ATTACHMENT_ID = @id") + " ORDER BY l.LINK_ID DESC", selection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var items = new List<AttachmentItem>();
        while (await reader.ReadAsync(ct))
            items.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), reader.GetString(4), Null(reader, 5), Null(reader, 6), Null(reader, 7),
                reader.GetString(8), reader.GetDateTime(9), reader.GetString(10), reader.GetDateTime(11)));
        return items;
    }

    private static (string Clause, (string Name, object? Value)[] Parameters) OwnerFilter(string prefix, AttachmentOwner owner) => owner.HasLegacyAlias
        ? ($"({prefix}OWNER_ALIAS = @alias OR {prefix}OWNER_ALIAS = @legacy)", [("@alias", owner.Alias), ("@legacy", owner.LegacyAlias)])
        : ($"{prefix}OWNER_ALIAS = @alias", [("@alias", owner.Alias)]);

    private static async Task<long> InsertFileAsync(DbConnection connection, DbTransaction transaction, AttachmentUpload file, string by, DateTime now, CancellationToken ct)
    {
        var kind = DatabaseKinds.Of(connection);
        var isOracle = kind is DatabaseDialect.Oracle or DatabaseDialect.Oracle11g;
        var sql = "INSERT INTO APP_ATTACHMENT (FILE_NAME, CONTENT_TYPE, SIZE, SHA256, CONTENT, UPLOADED_BY, UPLOADED_AT)"
            + (kind == DatabaseDialect.SqlServer ? " OUTPUT INSERTED.ATTACHMENT_ID" : "")
            + " VALUES (@name, @type, @size, @hash, @content, @by, @now)"
            + (kind == DatabaseDialect.SqlServer ? "" : isOracle ? " RETURNING ATTACHMENT_ID INTO @id" : " RETURNING ATTACHMENT_ID");
        await using var command = Command(connection, transaction, sql, ("@name", file.FileName), ("@type", file.ContentType), ("@size", (long)file.Bytes.Length),
            ("@hash", Convert.ToHexString(SHA256.HashData(file.Bytes)).ToLowerInvariant()), ("@content", file.Bytes), ("@by", by), ("@now", now));
        if (!isOracle) return Convert.ToInt64(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        // Oracle hands a RETURNING value back through an output parameter, whose value may be the driver's own number type, so it is read as text.
        var id = command.CreateParameter();
        id.ParameterName = "id";
        id.DbType = DbType.Int64;
        id.Direction = ParameterDirection.Output;
        command.Parameters.Add(id);
        await command.ExecuteNonQueryAsync(ct);
        return long.Parse(Convert.ToString(id.Value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
    }

    private async Task<DbConnection> OpenAsync(CancellationToken ct)
    {
        var name = settings.Value.ConnectionName;
        DbConnection connection;
        try { connection = connections.CreateConnection(name); }
        catch (InvalidOperationException error)
        {
            throw new AttachmentStoreException($"The attachments connection {name} is not registered. Register it through AddAppBlocks, or leave Attachments:Connection empty to use AppBlocksDb.", error, AttachmentStoreProblem.NotRegistered);
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // A connection string the provider refuses to parse, or a folder for its file that cannot be made.
            throw new AttachmentStoreException($"The attachments connection {name} cannot be used: {error.Message}", error, AttachmentStoreProblem.Unavailable);
        }
        try
        {
            await connection.OpenAsync(ct);
            // The two tables are created in whichever database the connection opens, once per process, or found there already made by hand.
            await ApplicationDatabase.EnsureOrAcceptAsync(connection, Schema, Tables, ct);
            return connection;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            await connection.DisposeAsync();
            if (error is AttachmentStoreException) throw;
            throw new AttachmentStoreException($"The attachments database through connection {name} is not available: {error.Message}", error,
                DatabaseInspector.Refusal(error) == DatabaseRefusal.None ? AttachmentStoreProblem.Unavailable : AttachmentStoreProblem.NotSetUp);
        }
    }

    /// <summary>Runs one store operation, so a refusal in its SQL reads like one on opening, and keeps the log to one line per problem.</summary>
    private async Task<T> GuardAsync<T>(bool write, Func<Task<T>> work)
    {
        var name = settings.Value.ConnectionName;
        try
        {
            var result = await work();
            // A read says nothing about a refused write, so only a write clears that one.
            if (Reported.TryGetValue(name, out var known) && (write || known != AttachmentStoreProblem.WritesRefused) && Reported.TryRemove(name, out _))
                logger.LogInformation("File uploads work again on connection {Connection}.", name);
            return result;
        }
        catch (AttachmentStoreException error) { throw Logged(error, name); }
        catch (DbException error)
        {
            var refusal = DatabaseInspector.Refusal(error);
            throw Logged(new AttachmentStoreException($"The attachments database through connection {name} refused the store: {error.Message}", error,
                refusal == DatabaseRefusal.None ? AttachmentStoreProblem.Failed : write && refusal == DatabaseRefusal.Denied ? AttachmentStoreProblem.WritesRefused : AttachmentStoreProblem.NotSetUp), name);
        }
    }

    private AttachmentStoreException Logged(AttachmentStoreException error, string name)
    {
        if (error.Problem == AttachmentStoreProblem.Failed) logger.LogWarning(error, "A file upload operation failed: {Problem}", error.Message);
        else if (!Reported.TryGetValue(name, out var known) || known != error.Problem)
        {
            Reported[name] = error.Problem;
            logger.LogWarning(error, "File uploads are off: {Problem} {Remedy} Forms and everything else keep working, and uploads return without a restart once this is done.", error.Message, error.Problem switch {
                AttachmentStoreProblem.Unsupported => "Set Attachments:Connection to AttachmentsDb, or to another SQLite, SQL Server, PostgreSQL or Oracle 12c connection.",
                AttachmentStoreProblem.NotRegistered => "Register that connection, or leave Attachments:Connection empty.",
                AttachmentStoreProblem.Unavailable => "Check the connection in Settings > AppBlocks > Connections.",
                _ => "Grant the permission or run the script shown in Settings > AppBlocks > Database setup.",
            });
        }
        return error;
    }

    private static DbCommand Command(DbConnection connection, DbTransaction? transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        var kind = DatabaseKinds.Of(connection);
        var isOracle = kind is DatabaseDialect.Oracle or DatabaseDialect.Oracle11g;
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = Sql(kind, sql);
        // Oracle binds by position unless told otherwise, and the store's SQL uses some parameters twice.
        if (isOracle) AppBlocks.DataSources.OracleDialect.Instance.ConfigureCommand(command);
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = isOracle ? name.TrimStart('@') : name;
            parameter.Value = value ?? DBNull.Value;
            // SQL Server needs to be told a blob is varbinary(max) and a time is datetime2, and Oracle that a blob is a BLOB rather than a RAW,
            // which holds 2000 bytes. SQLite and PostgreSQL bind by the value's type: a blob, and for PostgreSQL a UTC time as timestamptz.
            if (kind == DatabaseDialect.SqlServer && value is byte[]) { parameter.DbType = DbType.Binary; parameter.Size = -1; }
            else if (kind == DatabaseDialect.SqlServer && value is DateTime) parameter.DbType = DbType.DateTime2;
            else if (isOracle && value is byte[] && parameter.GetType().GetProperty("OracleDbType") is { CanWrite: true } type && type.PropertyType.IsEnum)
                type.SetValue(parameter, Enum.Parse(type.PropertyType, "Blob"));
            else if (isOracle && value is DateTime) parameter.DbType = DbType.DateTime;
            command.Parameters.Add(parameter);
        }
        return command;
    }

    /// <summary>
    /// The store's SQL, written once with <c>@name</c> parameters and unquoted names, as <paramref name="kind"/> runs it. PostgreSQL gets
    /// the two table names quoted, because its script creates them in capitals as the setup screen and its grants name them. Oracle gets
    /// <c>:name</c> parameters, and <c>SIZE</c>, a reserved word there, quoted as its script creates it. Neither database has been run
    /// against this store; the SQL follows each one's documentation.
    /// </summary>
    internal static string Sql(DatabaseDialect kind, string sql) => kind switch {
        DatabaseDialect.Postgres => TableNames().Replace(sql, "\"$0\""),
        DatabaseDialect.Oracle or DatabaseDialect.Oracle11g => SizeColumn().Replace(Parameters().Replace(sql, ":$1"), "\"SIZE\""),
        _ => sql,
    };

    [GeneratedRegex(@"\bAPP_ATTACHMENT(_LINK)?\b")]
    private static partial Regex TableNames();

    [GeneratedRegex(@"@(\w+)")]
    private static partial Regex Parameters();

    [GeneratedRegex(@"\bSIZE\b")]
    private static partial Regex SizeColumn();

    private static string? Null(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>The attachments database cannot be used: the connection is missing or its database refuses the store.</summary>
public sealed class AttachmentStoreException(string message, Exception? inner = null, AttachmentStoreProblem problem = AttachmentStoreProblem.Failed) : Exception(message, inner)
{
    public AttachmentStoreProblem Problem { get; } = problem;

    /// <summary>Whether this needs an administrator, rather than being a failure that can pass.</summary>
    public bool NeedsSetup => Problem is AttachmentStoreProblem.NotSetUp or AttachmentStoreProblem.WritesRefused or AttachmentStoreProblem.Unsupported or AttachmentStoreProblem.NotRegistered;
}

/// <summary>Why the attachments database cannot be used.</summary>
public enum AttachmentStoreProblem
{
    /// <summary>One operation failed for a reason that says nothing about the setup, such as a deadlock.</summary>
    Failed,
    /// <summary>The connection does not open.</summary>
    Unavailable,
    /// <summary>The connection names nothing registered.</summary>
    NotRegistered,
    /// <summary>The tables are missing and cannot be created, or cannot be read.</summary>
    NotSetUp,
    /// <summary>The tables can be read but this account may not write them.</summary>
    WritesRefused,
    /// <summary>The connection is a database type uploads do not run on.</summary>
    Unsupported,
}
