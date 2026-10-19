using System.Text.Json;
using System.Threading.Channels;

namespace PokerSettle.Core.Logging;

/// <summary>
/// Background writer: log calls only enqueue into a channel; a single loop writes batches to disk
/// every couple of seconds, and <see cref="Flush"/> writes immediately (app backgrounded, crash).
/// Nothing touches the disk until the first entry arrives.
/// </summary>
public sealed class JsonLogWriter : IDisposable
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(2);

    private readonly Channel<LogRecord> _channel = Channel.CreateUnbounded<LogRecord>();
    private readonly SemaphoreSlim _wake = new(0);
    private readonly CancellationTokenSource _stop = new();
    private readonly object _drainGate = new();
    private readonly TimeSpan _interval;
    private int _started;

    public JsonLogWriter(LogFileStore store, TimeSpan? interval = null)
    {
        Store = store;
        _interval = interval ?? DefaultInterval;
    }

    public LogFileStore Store { get; }

    public void Enqueue(LogRecord record)
    {
        if (!_channel.Writer.TryWrite(record)) return;
        if (Interlocked.Exchange(ref _started, 1) == 0)
        {
            _ = Task.Run(RunAsync);
        }
    }

    /// <summary>Writes everything queued so far, synchronously. Safe to call from crash handlers.</summary>
    public void Flush()
    {
        try
        {
            Drain();
        }
        catch
        {
            // Logging must never take the app down.
        }
    }

    public Task FlushAsync() => Task.Run(Flush);

    /// <summary>Drops queued entries and deletes the files.</summary>
    public void Clear()
    {
        lock (_drainGate)
        {
            while (_channel.Reader.TryRead(out _))
            {
            }
            Store.Clear();
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        Flush();
    }

    private async Task RunAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await _wake.WaitAsync(_interval, _stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            Flush();
        }
    }

    private void Drain()
    {
        lock (_drainGate)
        {
            List<string>? lines = null;
            while (_channel.Reader.TryRead(out var record))
            {
                lines ??= [];
                lines.Add(JsonSerializer.Serialize(record, LogJsonContext.Lines.LogRecord));
            }
            if (lines is not null) Store.Append(lines);
        }
    }
}
