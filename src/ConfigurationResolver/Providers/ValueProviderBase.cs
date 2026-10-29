using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using ConfigurationResolver.Exceptions;
using ConfigurationResolver.Interfaces;

namespace ConfigurationResolver.Providers;

/// <summary>
/// Base class for value providers whose values are bytes, such as files or decrypted data.
/// </summary>
/// <remarks>
/// Derived classes implement <see cref="TryGetBytes"/> and <see cref="TryGetByteCount"/>. The character,
/// string, and sizing methods are built on them: the value's bytes are read into a temporary buffer that
/// the garbage collector cannot move, decoded, and the buffer is then wiped with
/// <see cref="CryptographicOperations.ZeroMemory"/>.
/// </remarks>
public abstract class ValueProviderBase : IValueProvider
{
    /// <summary>
    /// Values up to this many bytes are read into a stack buffer; larger values use a pinned array.
    /// </summary>
    private const int StackBufferLength = 512;

    /// <summary>
    /// How many times a value is re-read when it grows between being sized and being read.
    /// </summary>
    private const int MaxReadAttempts = 3;

    /// <inheritdoc />
    public abstract ValueStatus TryGetBytes(string key, Span<byte> destination, out int bytesWritten);

    /// <summary>
    /// Attempts to get the number of UTF-8 bytes needed to hold the value for the specified key.
    /// </summary>
    /// <param name="key">The configuration key.</param>
    /// <param name="byteCount">The required buffer size for <see cref="TryGetBytes"/>; 0 if the key was not found.</param>
    /// <returns><see langword="true"/> if the key was found; otherwise <see langword="false"/>.</returns>
    protected abstract bool TryGetByteCount(string key, out int byteCount);

    /// <inheritdoc />
    public virtual ValueStatus TryGetChar(string key, Span<char> destination, out int charsWritten)
    {
        charsWritten = 0;
        Span<byte> stackBuffer = stackalloc byte[StackBufferLength];
        byte[]? pinnedBuffer = null;

        try
        {
            var status = ReadBytes(key, stackBuffer, ref pinnedBuffer, out var length);
            if (status != ValueStatus.Found)
            {
                return status;
            }

            if (Encoding.UTF8.TryGetChars(Slice(stackBuffer, pinnedBuffer, length), destination, out charsWritten))
            {
                return ValueStatus.Found;
            }

            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(destination));
            return ValueStatus.DestinationTooSmall;
        }
        finally
        {
            Wipe(stackBuffer, pinnedBuffer);
        }
    }

    /// <inheritdoc />
    public virtual ValueStatus TryGetValue(string key, out string? value)
    {
        value = null;
        Span<byte> stackBuffer = stackalloc byte[StackBufferLength];
        byte[]? pinnedBuffer = null;

        try
        {
            var status = ReadBytes(key, stackBuffer, ref pinnedBuffer, out var length);
            if (status == ValueStatus.Found)
            {
                value = Encoding.UTF8.GetString(Slice(stackBuffer, pinnedBuffer, length));
            }

            return status;
        }
        finally
        {
            Wipe(stackBuffer, pinnedBuffer);
        }
    }

    /// <summary>
    /// Attempts to get the number of characters needed to hold the value for the specified key.
    /// </summary>
    /// <param name="key">The configuration key.</param>
    /// <param name="charCount">The required buffer size for <see cref="TryGetChar"/>; 0 if the key was not found.</param>
    /// <returns><see langword="true"/> if the key was found; otherwise <see langword="false"/>.</returns>
    protected virtual bool TryGetCharCount(string key, out int charCount)
    {
        charCount = 0;
        Span<byte> stackBuffer = stackalloc byte[StackBufferLength];
        byte[]? pinnedBuffer = null;

        try
        {
            if (ReadBytes(key, stackBuffer, ref pinnedBuffer, out var length) != ValueStatus.Found)
            {
                return false;
            }

            charCount = Encoding.UTF8.GetCharCount(Slice(stackBuffer, pinnedBuffer, length));
            return true;
        }
        finally
        {
            Wipe(stackBuffer, pinnedBuffer);
        }
    }

    /// <summary>
    /// Reads the value's bytes into <paramref name="stackBuffer"/>, or into a new pinned array when it does not fit.
    /// </summary>
    private ValueStatus ReadBytes(string key, Span<byte> stackBuffer, ref byte[]? pinnedBuffer, out int length)
    {
        for (var attempt = 0; attempt < MaxReadAttempts; attempt++)
        {
            if (!TryGetByteCount(key, out var byteCount))
            {
                length = 0;
                return ValueStatus.NotFound;
            }

            // A previous attempt's buffer is wiped and dropped; pinnedBuffer is only non-null while in use.
            Wipe([], pinnedBuffer);
            pinnedBuffer = byteCount > stackBuffer.Length ? GC.AllocateUninitializedArray<byte>(byteCount, pinned: true) : null;

            var buffer = Slice(stackBuffer, pinnedBuffer, byteCount);
            var status = TryGetBytes(key, buffer, out length);
            if (status != ValueStatus.DestinationTooSmall)
            {
                return status;
            }
        }

        throw new ConfigurationKeyException(key, ConfigurationKeyError.ValueUnstable);
    }

    private static Span<byte> Slice(Span<byte> stackBuffer, byte[]? pinnedBuffer, int length)
    {
        return pinnedBuffer is null ? stackBuffer[..length] : pinnedBuffer.AsSpan(0, length);
    }

    private static void Wipe(Span<byte> stackBuffer, byte[]? pinnedBuffer)
    {
        CryptographicOperations.ZeroMemory(stackBuffer);
        CryptographicOperations.ZeroMemory(pinnedBuffer);
    }
}
