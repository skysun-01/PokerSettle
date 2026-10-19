using System.Text;
using System.Text.Json;

namespace PokerSettle.Core.Logging;

/// <summary>
/// Rolling JSON-lines files in the logs folder: log.jsonl (newest), log.1.jsonl, log.2.jsonl.
/// When the current file would pass the size cap it rolls, so there are at most
/// <c>maxFiles</c> files of about <c>maxFileBytes</c> each.
/// </summary>
public sealed class LogFileStore
{
    public const long DefaultMaxFileBytes = 1024 * 1024;
    public const int DefaultMaxFiles = 3;

    private readonly long _maxFileBytes;
    private readonly int _maxFiles;
    private readonly object _gate = new();

    public LogFileStore(string directory, long maxFileBytes = DefaultMaxFileBytes, int maxFiles = DefaultMaxFiles)
    {
        if (maxFileBytes < 256) throw new ArgumentOutOfRangeException(nameof(maxFileBytes));
        if (maxFiles < 1) throw new ArgumentOutOfRangeException(nameof(maxFiles));
        DirectoryPath = directory;
        _maxFileBytes = maxFileBytes;
        _maxFiles = maxFiles;
    }

    public string DirectoryPath { get; }

    public string CurrentPath => PathFor(0);

    public string PathFor(int index) =>
        Path.Combine(DirectoryPath, index == 0 ? "log.jsonl" : $"log.{index}.jsonl");

    public void Append(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) return;
        lock (_gate)
        {
            Directory.CreateDirectory(DirectoryPath);
            var size = File.Exists(CurrentPath) ? new FileInfo(CurrentPath).Length : 0;
            var pending = new StringBuilder();
            long pendingBytes = 0;
            foreach (var line in lines)
            {
                var bytes = Encoding.UTF8.GetByteCount(line) + 1;
                if (size + pendingBytes + bytes > _maxFileBytes && size + pendingBytes > 0)
                {
                    Write(pending);
                    Roll();
                    size = 0;
                    pendingBytes = 0;
                    pending.Clear();
                }
                pending.Append(line).Append('\n');
                pendingBytes += bytes;
            }
            Write(pending);
        }
    }

    /// <summary>Existing log files, oldest first.</summary>
    public IReadOnlyList<string> Files()
    {
        var files = new List<string>();
        for (var i = _maxFiles - 1; i >= 0; i--)
        {
            var path = PathFor(i);
            if (File.Exists(path)) files.Add(path);
        }
        return files;
    }

    public long TotalBytes() => Files().Sum(f => new FileInfo(f).Length);

    /// <summary>All entries, oldest first. Lines that fail to parse are skipped.</summary>
    public IReadOnlyList<LogRecord> ReadAll()
    {
        var records = new List<LogRecord>();
        lock (_gate)
        {
            foreach (var file in Files())
            {
                foreach (var line in ReadLinesShared(file))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var record = JsonSerializer.Deserialize(line, LogJsonContext.Lines.LogRecord);
                        if (record is not null) records.Add(record);
                    }
                    catch (JsonException)
                    {
                        // A torn last line after a crash; ignore it.
                    }
                }
            }
        }
        return records;
    }

    /// <summary>Concatenates the files (oldest first) into one file for sharing.</summary>
    public string? ExportCombined(string targetPath)
    {
        lock (_gate)
        {
            var files = Files();
            if (files.Count == 0) return null;
            using var output = File.Create(targetPath);
            foreach (var file in files)
            {
                using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                input.CopyTo(output);
            }
            return targetPath;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            for (var i = 0; i < _maxFiles + 2; i++)
            {
                var path = PathFor(i);
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }

    private void Write(StringBuilder pending)
    {
        if (pending.Length == 0) return;
        using var stream = new FileStream(CurrentPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        var bytes = Encoding.UTF8.GetBytes(pending.ToString());
        stream.Write(bytes, 0, bytes.Length);
    }

    private void Roll()
    {
        var oldest = PathFor(_maxFiles - 1);
        if (File.Exists(oldest)) File.Delete(oldest);
        for (var i = _maxFiles - 1; i >= 1; i--)
        {
            var source = PathFor(i - 1);
            if (File.Exists(source)) File.Move(source, PathFor(i));
        }
    }

    private static IEnumerable<string> ReadLinesShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (reader.ReadLine() is { } line) yield return line;
    }
}
