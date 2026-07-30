using System.Threading;

namespace Matgate.Services;

internal sealed class FileTransferLimitExceededException(string message) : IOException(message);

internal static class FileTransferBudget
{
    private static readonly AsyncLocal<State?> CurrentState = new();

    public static IDisposable Begin(long maxBytes, int maxEntries)
    {
        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        }

        if (maxEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEntries));
        }

        var previous = CurrentState.Value;
        CurrentState.Value = new State(maxBytes, maxEntries);
        return new Scope(previous);
    }

    public static void ConsumeBytes(long count)
    {
        if (count <= 0 || CurrentState.Value is not { } state)
        {
            return;
        }

        var total = Interlocked.Add(ref state.Bytes, count);
        if (total > state.MaxBytes)
        {
            throw new FileTransferLimitExceededException(
                $"Archive expansion exceeds the configured {state.MaxBytes} byte limit.");
        }
    }

    public static void ConsumeEntry()
    {
        if (CurrentState.Value is not { } state)
        {
            return;
        }

        var total = Interlocked.Increment(ref state.Entries);
        if (total > state.MaxEntries)
        {
            throw new FileTransferLimitExceededException(
                $"Archive expansion exceeds the configured {state.MaxEntries} entry limit.");
        }
    }

    private sealed class State(long maxBytes, int maxEntries)
    {
        public long MaxBytes { get; } = maxBytes;

        public int MaxEntries { get; } = maxEntries;

        public long Bytes;

        public int Entries;
    }

    private sealed class Scope(State? previous) : IDisposable
    {
        private State? _previous = previous;
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CurrentState.Value = _previous;
            _previous = null;
        }
    }
}

internal sealed class BoundedReadStream(
    Stream inner,
    long maxBytes,
    bool countAgainstArchiveBudget,
    bool leaveOpen = true) : Stream
{
    private long _read;

    public override bool CanRead => inner.CanRead;

    public override bool CanSeek => inner.CanSeek;

    public override bool CanWrite => false;

    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => inner.Position = value;
    }

    public override void Flush() => inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = inner.Read(buffer, offset, count);
        Account(read);
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        var read = inner.Read(buffer);
        Account(read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer, cancellationToken);
        Account(read);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return ReadLegacyAsync(buffer, offset, count, cancellationToken);
    }

    private async Task<int> ReadLegacyAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var read = await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
        Account(read);
        return read;
    }

    private void Account(int read)
    {
        if (read <= 0)
        {
            return;
        }

        _read += read;
        if (_read > maxBytes)
        {
            throw new FileTransferLimitExceededException(
                $"Transfer exceeds the configured {maxBytes} byte limit.");
        }

        if (countAgainstArchiveBudget)
        {
            FileTransferBudget.ConsumeBytes(read);
        }
    }

    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !leaveOpen)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!leaveOpen)
        {
            await inner.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }
}
