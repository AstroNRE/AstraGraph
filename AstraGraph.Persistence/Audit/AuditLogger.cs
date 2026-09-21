using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace AstraGraph.Persistence.Audit;

/// <summary>
/// Thread-safe append-only audit logger writing structured JSONL entries to the Audit directory.
/// </summary>
public sealed class AuditLogger
{
    private readonly StorageLayout _layout;
    private readonly object _lock = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public AuditLogger(StorageLayout layout)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _layout.EnsureDirectories();
    }

    /// <summary>
    /// Appends an audit record to the daily audit log file.
    /// </summary>
    public void Append(AuditRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var logPath = _layout.GetAuditLogPath(record.Timestamp.UtcDateTime);
        var json = JsonSerializer.Serialize(record, JsonOptions);
        var lineBytes = Encoding.UTF8.GetBytes(json + "\n");

        lock (_lock)
        {
            var dir = Path.GetDirectoryName(logPath)!;
            Directory.CreateDirectory(dir);

            using var stream = new FileStream(
                logPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.WriteThrough);

            stream.Write(lineBytes, 0, lineBytes.Length);
            stream.Flush(flushToDisk: true);
        }
    }

    /// <summary>
    /// Reads all audit records in a given UTC date/time range.
    /// </summary>
    public IReadOnlyList<AuditRecord> ReadRecords(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        var records = new List<AuditRecord>();
        var currentDate = fromUtc.UtcDateTime.Date;
        var endDate = toUtc.UtcDateTime.Date;

        while (currentDate <= endDate)
        {
            var logPath = _layout.GetAuditLogPath(currentDate);
            if (File.Exists(logPath))
            {
                lock (_lock)
                {
                    using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        try
                        {
                            var rec = JsonSerializer.Deserialize<AuditRecord>(line, JsonOptions);
                            if (rec != null && rec.Timestamp >= fromUtc && rec.Timestamp <= toUtc)
                            {
                                records.Add(rec);
                            }
                        }
                        catch (JsonException)
                        {
                            // Skip corrupted line
                        }
                    }
                }
            }

            currentDate = currentDate.AddDays(1);
        }

        return records;
    }
}
