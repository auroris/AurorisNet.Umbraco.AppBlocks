namespace AppBlocks;

/// <summary>
/// A settings file under App_Data, such as connections.json, parsed once per version of the file. The parsed value is kept with the
/// file's last write time and length, and the file is read again when either changes, so a hand-edited file is picked up without a
/// restart. <see cref="Invalidate"/> drops the value once the application has written the file itself, and <see cref="WriteAsync"/>
/// writes atomically via a temporary file in the same directory and invalidates the cache.
/// </summary>
/// <param name="path">Where the file is, asked on every read, since configuration may move it.</param>
/// <param name="parse">Turns the file's text into the value; null text means there is no file.</param>
public sealed class SettingsFile<T>(Func<string> path, Func<string?, T> parse)
{
    private sealed record Version(string Path, bool Exists, DateTime Written, long Length, T Value);
    private volatile Version? current;

    public T Read()
    {
        var file = new FileInfo(path());
        var known = current;
        if (known is not null && known.Path == file.FullName && known.Exists == file.Exists
            && (!file.Exists || known.Written == file.LastWriteTimeUtc && known.Length == file.Length))
            return known.Value;
        // Stamped before the read: a write in between leaves a stamp older than the file, so the next read parses it again.
        var (exists, written, length) = file.Exists ? (true, file.LastWriteTimeUtc, file.Length) : (false, default, 0L);
        string? text = null;
        if (exists)
        {
            try { text = File.ReadAllText(file.FullName); }
            catch (FileNotFoundException) { exists = false; written = default; length = 0L; }
        }
        var value = parse(text);
        current = new(file.FullName, exists, written, length, value);
        return value;
    }

    /// <summary>Writes <paramref name="content"/> atomically via a sibling temporary file, creating parent directories if needed, and invalidates the cached value.</summary>
    public async Task WriteAsync(string content, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var target = path();
        if (Path.GetDirectoryName(target) is { Length: > 0 } directory) Directory.CreateDirectory(directory);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, content, ct);
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            Invalidate();
        }
    }

    public void Invalidate() => current = null;
}
