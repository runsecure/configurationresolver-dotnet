using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace ConfigurationResolver.Buffers;

/// <summary>
/// A fixed-size buffer for secret text that is pinned in memory and wiped when it is disposed.
/// </summary>
/// <remarks>
/// The memory is allocated on the pinned object heap, so the garbage collector never moves it and leaves
/// no copies behind. It is wiped with <see cref="CryptographicOperations.ZeroMemory"/> by
/// <see cref="Clear"/>, by <see cref="Dispose"/>, and by the finalizer if the buffer is never disposed.
/// The contents are only exposed as a span, are hidden from the debugger, and are never returned by
/// <see cref="ToString"/>. Fill it with
/// <see cref="SecretBufferExtensions.Resolve(Interfaces.IConfigurationResolver, ReadOnlySpan{char}, SecretBuffer{char})"/>
/// and use <see cref="Span"/> inside a <see langword="using"/> scope, without copying it to strings or arrays.
/// </remarks>
/// <typeparam name="T">The element type, normally <see cref="char"/> or <see cref="byte"/>.</typeparam>
[DebuggerDisplay("{ToString(),nq}")]
public sealed class SecretBuffer<T> : IDisposable
    where T : unmanaged
{
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly T[] _buffer;

    private int _length;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecretBuffer{T}"/> class.
    /// </summary>
    /// <param name="capacity">The number of elements the buffer can hold.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is negative.</exception>
    public SecretBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);

        _buffer = GC.AllocateArray<T>(capacity, pinned: true);
    }

    /// <summary>
    /// Wipes the buffer if it was never disposed.
    /// </summary>
    ~SecretBuffer()
    {
        Wipe();
    }

    /// <summary>
    /// Gets the number of elements the buffer can hold.
    /// </summary>
    public int Capacity => _buffer.Length;

    /// <summary>
    /// Gets the number of elements written to the buffer.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The buffer has been disposed.</exception>
    public int Length
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _length;
        }
    }

    /// <summary>
    /// Gets the elements written to the buffer.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The buffer has been disposed.</exception>
    public ReadOnlySpan<T> Span
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _buffer.AsSpan(0, _length);
        }
    }

    /// <summary>
    /// Wipes the buffer and sets <see cref="Length"/> to zero, so it can be filled again.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The buffer has been disposed.</exception>
    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Wipe();
    }

    /// <summary>
    /// Wipes the buffer. Disposing it again has no effect.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            Wipe();
            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Describes the buffer without revealing its contents.
    /// </summary>
    /// <returns>The element type, length, and capacity, or that the buffer is disposed.</returns>
    public override string ToString()
    {
        return _disposed
            ? $"SecretBuffer<{typeof(T).Name}>(disposed)"
            : $"SecretBuffer<{typeof(T).Name}>(Length = {_length}, Capacity = {Capacity})";
    }

    /// <summary>
    /// Wipes the buffer and returns its whole capacity to write into.
    /// </summary>
    internal Span<T> BeginWrite()
    {
        Clear();
        return _buffer;
    }

    /// <summary>
    /// Records how many elements were written after <see cref="BeginWrite"/>.
    /// </summary>
    internal void EndWrite(int length)
    {
        if ((uint)length > (uint)_buffer.Length)
        {
            Wipe();
            throw new InvalidOperationException("The resolver reported writing more than the buffer's capacity.");
        }

        _length = length;
    }

    private void Wipe()
    {
        CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(_buffer.AsSpan()));
        _length = 0;
    }
}
