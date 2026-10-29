using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using ConfigurationResolver.Exceptions;
using ConfigurationResolver.Interfaces;
using ConfigurationResolver.Tokens;

namespace ConfigurationResolver.Resolvers;

/// <summary>
/// Resolves text by replacing the configuration items delimited by a <see cref="ConfigurationItemTokenBase"/>
/// with values from an <see cref="IValueProvider"/>, for only the keys read by an <see cref="IConfigurationItemReader"/>.
/// </summary>
/// <remarks>
/// Keys are trimmed of surrounding whitespace, the same as <see cref="Readers.ConfigurationItemReader"/>.
/// Only keys returned by the reader's <see cref="IConfigurationItemReader.Keys"/> are looked up, so read
/// the keys from a trusted template first; any other key throws without the provider being asked for it.
/// A key that is not allowed or has no value throws a <see cref="ConfigurationKeyException"/>, whose
/// message does not contain the key.
/// The reader's keys are checked each time <c>Resolve</c> is called, so the most recent read applies.
/// Empty items such as <c>${}</c> and a start sequence with no matching end sequence are copied unchanged.
/// Values are written by the provider directly into the destination and are never read as strings. If
/// resolving fails, the destination is wiped with <see cref="CryptographicOperations.ZeroMemory"/> so no
/// partially resolved values are left behind.
/// </remarks>
public class ConfigurationItemResolver : IConfigurationResolver
{
    private readonly ConfigurationItemTokenBase _token;
    private readonly IConfigurationItemReader _reader;
    private readonly IValueProvider _valueProvider;
    private readonly byte[] _utf8StartSequence;
    private readonly byte[] _utf8EndSequence;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationItemResolver"/> class.
    /// </summary>
    /// <param name="token">The token that defines the start and end sequences of a configuration item.</param>
    /// <param name="reader">The reader whose keys are the only keys that may be resolved.</param>
    /// <param name="valueProvider">The provider used to resolve the value of each key.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="token"/>, <paramref name="reader"/>, or <paramref name="valueProvider"/> is <see langword="null"/>.
    /// </exception>
    public ConfigurationItemResolver(ConfigurationItemTokenBase token, IConfigurationItemReader reader, IValueProvider valueProvider)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(valueProvider);

        _token = token;
        _reader = reader;
        _valueProvider = valueProvider;
        _utf8StartSequence = Encoding.UTF8.GetBytes(token.StartSequence);
        _utf8EndSequence = Encoding.UTF8.GetBytes(token.EndSequence);
    }

    /// <inheritdoc />
    public int Resolve(ReadOnlySpan<char> source, Span<char> destination)
    {
        var startSequence = _token.StartSequence.AsSpan();
        var endSequence = _token.EndSequence.AsSpan();
        var allowedKeys = AllowedKeys();
        var offset = 0;
        var written = 0;

        try
        {
            while (true)
            {
                var startIndex = source.IndexOf(startSequence, StringComparison.Ordinal);
                var keyStart = startIndex + startSequence.Length;
                var endIndex = startIndex < 0 ? -1 : source[keyStart..].IndexOf(endSequence, StringComparison.Ordinal);
                if (endIndex < 0)
                {
                    Copy(source, destination, ref written);
                    return written;
                }

                var itemEnd = keyStart + endIndex + endSequence.Length;
                var key = source.Slice(keyStart, endIndex).Trim();
                if (key.IsEmpty)
                {
                    Copy(source[..itemEnd], destination, ref written);
                }
                else
                {
                    Copy(source[..startIndex], destination, ref written);
                    WriteValue(key.ToString(), offset + startIndex, allowedKeys, destination, ref written);
                }

                source = source[itemEnd..];
                offset += itemEnd;
            }
        }
        catch
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(destination));
            throw;
        }
    }

    /// <inheritdoc />
    public int Resolve(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        ReadOnlySpan<byte> startSequence = _utf8StartSequence;
        ReadOnlySpan<byte> endSequence = _utf8EndSequence;
        var allowedKeys = AllowedKeys();
        var offset = 0;
        var written = 0;

        try
        {
            while (true)
            {
                var startIndex = source.IndexOf(startSequence);
                var keyStart = startIndex + startSequence.Length;
                var endIndex = startIndex < 0 ? -1 : source[keyStart..].IndexOf(endSequence);
                if (endIndex < 0)
                {
                    Copy(source, destination, ref written);
                    return written;
                }

                var itemEnd = keyStart + endIndex + endSequence.Length;
                var key = Encoding.UTF8.GetString(source.Slice(keyStart, endIndex)).Trim();
                if (key.Length == 0)
                {
                    Copy(source[..itemEnd], destination, ref written);
                }
                else
                {
                    Copy(source[..startIndex], destination, ref written);
                    WriteValue(key, offset + startIndex, allowedKeys, destination, ref written);
                }

                source = source[itemEnd..];
                offset += itemEnd;
            }
        }
        catch
        {
            CryptographicOperations.ZeroMemory(destination);
            throw;
        }
    }

    private HashSet<string> AllowedKeys() => new(_reader.Keys(), StringComparer.Ordinal);

    private void WriteValue(string key, int sourceIndex, HashSet<string> allowedKeys, Span<char> destination, ref int written)
    {
        ThrowIfNotAllowed(key, sourceIndex, allowedKeys);

        ValueStatus status;
        int charsWritten;
        try
        {
            status = _valueProvider.TryGetChar(key, destination[written..], out charsWritten);
        }
        catch (ConfigurationKeyException ex) when (ex.SourceIndex is null)
        {
            throw WithSourceIndex(ex, sourceIndex);
        }

        if (status != ValueStatus.Found)
        {
            throw ValueNotWritten(key, sourceIndex, status);
        }

        written += charsWritten;
    }

    private void WriteValue(string key, int sourceIndex, HashSet<string> allowedKeys, Span<byte> destination, ref int written)
    {
        ThrowIfNotAllowed(key, sourceIndex, allowedKeys);

        ValueStatus status;
        int bytesWritten;
        try
        {
            status = _valueProvider.TryGetBytes(key, destination[written..], out bytesWritten);
        }
        catch (ConfigurationKeyException ex) when (ex.SourceIndex is null)
        {
            throw WithSourceIndex(ex, sourceIndex);
        }

        if (status != ValueStatus.Found)
        {
            throw ValueNotWritten(key, sourceIndex, status);
        }

        written += bytesWritten;
    }

    private static void ThrowIfNotAllowed(string key, int sourceIndex, HashSet<string> allowedKeys)
    {
        if (!allowedKeys.Contains(key))
        {
            throw new ConfigurationKeyException(key, ConfigurationKeyError.NotAllowed, sourceIndex);
        }
    }

    private static void Copy<T>(ReadOnlySpan<T> text, Span<T> destination, ref int written)
    {
        if (!text.TryCopyTo(destination[written..]))
        {
            throw DestinationTooSmall();
        }

        written += text.Length;
    }

    private static Exception ValueNotWritten(string key, int sourceIndex, ValueStatus status)
    {
        return status == ValueStatus.DestinationTooSmall
            ? DestinationTooSmall()
            : new ConfigurationKeyException(key, ConfigurationKeyError.NotFound, sourceIndex);
    }

    /// <summary>
    /// Adds the item's position to a <see cref="ConfigurationKeyException"/> thrown by the value provider.
    /// </summary>
    private static ConfigurationKeyException WithSourceIndex(ConfigurationKeyException exception, int sourceIndex)
    {
        return new ConfigurationKeyException(exception.Key, exception.Error, sourceIndex, exception);
    }

    private static ArgumentException DestinationTooSmall()
    {
        return new ArgumentException("The destination is too small for the resolved text.", "destination");
    }
}
