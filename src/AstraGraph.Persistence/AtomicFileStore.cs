using System;
using System.IO;
using System.Text;

namespace AstraGraph.Persistence;

/// <summary>
/// Provides atomic, crash-proof file operations ensuring that disk writes are resilient
/// to crashes and power failures.
/// </summary>
public static class AtomicFileStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>
    /// Atomically writes a byte array to the target file path.
    /// Writes to a temporary file, flushes to disk (fsync), validates via optional callback,
    /// optionally backs up existing file, and renames atomically to the target path.
    /// </summary>
    public static void WriteAllBytesAtomic(
        string targetPath,
        byte[] data,
        string? backupDirectory = null,
        Action<string>? validator = null)
    {
        ArgumentNullException.ThrowIfNull(targetPath);
        ArgumentNullException.ThrowIfNull(data);

        var fullPath = Path.GetFullPath(targetPath);
        var dir = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(dir);

        var tempPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            using (var stream = new FileStream(
                       tempPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(data, 0, data.Length);
                stream.Flush(flushToDisk: true);
            }

            // Run validation callback if provided
            validator?.Invoke(tempPath);

            // Create backup if target exists and backup directory is configured
            if (File.Exists(fullPath) && !string.IsNullOrEmpty(backupDirectory))
            {
                Directory.CreateDirectory(backupDirectory);
                var fileName = Path.GetFileName(fullPath);
                var backupPath = Path.Combine(
                    backupDirectory,
                    $"{fileName}.{DateTime.UtcNow:yyyyMMddHHmmssfff}.bak");
                File.Copy(fullPath, backupPath, overwrite: true);
            }

            // Atomic move/replace
            File.Move(tempPath, fullPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* best effort cleanup */ }
            }
            throw;
        }
    }

    /// <summary>
    /// Atomically writes text to the target file using UTF-8 encoding without BOM.
    /// </summary>
    public static void WriteAllTextAtomic(
        string targetPath,
        string content,
        string? backupDirectory = null,
        Action<string>? validator = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        var bytes = Utf8NoBom.GetBytes(content);
        WriteAllBytesAtomic(targetPath, bytes, backupDirectory, validator);
    }

    /// <summary>
    /// Reads all text safely from the specified path.
    /// </summary>
    public static string ReadAllText(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Reads all bytes safely from the specified path.
    /// </summary>
    public static byte[] ReadAllBytes(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bytes = new byte[stream.Length];
        var read = 0;
        while (read < bytes.Length)
        {
            var chunk = stream.Read(bytes, read, bytes.Length - read);
            if (chunk == 0) break;
            read += chunk;
        }
        return bytes;
    }
}
