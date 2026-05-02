using System.Threading.Channels;

namespace SqlServerMcpServer;

/// <summary>An MCP SSE session with bidirectional channels bridging HTTP and the MCP server.</summary>
internal sealed class SseSession
{
    public string SessionId { get; }
    public Channel<byte[]> ClientToServer { get; } = Channel.CreateUnbounded<byte[]>();
    public Channel<byte[]> ServerToClient { get; } = Channel.CreateUnbounded<byte[]>();

    public SseSession(string sessionId) => SessionId = sessionId;
}

/// <summary>Wraps a <see cref="ChannelReader{T}"/> of byte arrays as a readable Stream.</summary>
internal sealed class ChannelInputStream : Stream
{
    private readonly ChannelReader<byte[]> _reader;
    private byte[]? _current;
    private int _offset;

    public ChannelInputStream(ChannelReader<byte[]> reader) => _reader = reader;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        while (_current == null || _offset >= _current.Length)
        {
            _current = await _reader.ReadAsync(ct).ConfigureAwait(false);
            _offset = 0;
        }

        var count = Math.Min(buffer.Length, _current.Length - _offset);
        _current.AsSpan(_offset, count).CopyTo(buffer.Span);
        _offset += count;

        if (_offset >= _current.Length)
        {
            _current = null;
            _offset = 0;
        }

        return count;
    }

    public override int Read(byte[] buffer, int offset, int count)
        => ReadAsync(new Memory<byte>(buffer, offset, count)).AsTask().GetAwaiter().GetResult();

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>Wraps a <see cref="ChannelWriter{T}"/> of byte arrays as a writable Stream.</summary>
internal sealed class ChannelOutputStream : Stream
{
    private readonly ChannelWriter<byte[]> _writer;

    public ChannelOutputStream(ChannelWriter<byte[]> writer) => _writer = writer;

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        var chunk = buffer.ToArray();
        await _writer.WriteAsync(chunk, ct).ConfigureAwait(false);
    }

    public override async Task FlushAsync(CancellationToken ct = default)
    {
        await Task.CompletedTask;
    }

    public override void Write(byte[] buffer, int offset, int count)
        => _writer.TryWrite(buffer.AsSpan(offset, count).ToArray());

    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
