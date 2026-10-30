using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using ConfigurationResolver.Exceptions;
using ConfigurationResolver.Interfaces;
using ConfigurationResolver.Providers;
using ConfigurationResolver.Tokens;

namespace ConfigurationResolver.Resolvers;

/// <summary>
/// Resolves text by replacing the configuration items delimited by a <see cref="ConfigurationItemTokenBase"/>
/// with values from one or more <see cref="IValueProvider"/>s, for only the keys read by an
/// <see cref="IConfigurationItemReader"/>.
/// </summary>
/// <remarks>
/// Keys are trimmed of surrounding whitespace, the same as <see cref="Readers.ConfigurationItemReader"/>.
/// Only keys returned by the reader's <see cref="IConfigurationItemReader.Keys"/> are looked up, so read
/// the keys from a trusted template first; any other key throws without the provider being asked for it.
/// With several value providers, each key is looked up in the order the providers were given, and the
/// first provider that has the key supplies the value, as with <see cref="CompositeValueProvider"/>.
/// A key that is not allowed throws a <see cref="ConfigurationKeyException"/>, whose message does not
/// contain the key. A key that no provider has throws the same way, unless
/// <see cref="IgnoreUnresolvablePlaceholders"/> is set.
/// The reader's keys are checked each time <c>Resolve</c> is called, so the most recent read applies.
/// Empty items such as <c>${}</c> and a start sequence with no matching end sequence are copied unchanged,
/// and a start sequence preceded by the token's <see cref="ConfigurationItemTokenBase.EscapeSequence"/> is
/// copied without the escape sequence.
/// By default values are inserted as they are; set <see cref="MaxResolutionDepth"/> to also resolve the
/// items inside values.
/// Values are written by the provider directly into the destination and are never read as strings. If
/// resolving fails, the destination is wiped with <see cref="CryptographicOperations.ZeroMemory"/> so no
/// partially resolved values are left behind.
/// </remarks>
public class ConfigurationItemResolver : IConfigurationResolver
{
    private readonly IConfigurationItemReader _reader;
    private readonly IValueProvider _valueProvider;
    private readonly Delimiters<char> _charDelimiters;
    private readonly Delimiters<byte> _utf8Delimiters;
    private readonly int _maxResolutionDepth = 1;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationItemResolver"/> class.
    /// </summary>
    /// <param name="token">The token that defines the start, end, and escape sequences of a configuration item.</param>
    /// <param name="reader">The reader whose keys are the only keys that may be resolved.</param>
    /// <param name="valueProviders">The providers used to resolve the value of each key, highest priority first.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="token"/>, <paramref name="reader"/>, or <paramref name="valueProviders"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="valueProviders"/> is empty or contains <see langword="null"/>.</exception>
    public ConfigurationItemResolver(ConfigurationItemTokenBase token, IConfigurationItemReader reader, params IEnumerable<IValueProvider> valueProviders)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(valueProviders);

        var composite = new CompositeValueProvider(valueProviders);

        _reader = reader;
        _valueProvider = composite.Providers.Count == 1 ? composite.Providers[0] : composite;
        _charDelimiters = new Delimiters<char>(
            token.StartSequence.ToCharArray(),
            token.EndSequence.ToCharArray(),
            token.EscapeSequence?.ToCharArray() ?? []);
        _utf8Delimiters = new Delimiters<byte>(
            Encoding.UTF8.GetBytes(token.StartSequence),
            Encoding.UTF8.GetBytes(token.EndSequence),
            token.EscapeSequence is null ? [] : Encoding.UTF8.GetBytes(token.EscapeSequence));
    }

    /// <summary>
    /// Gets a value indicating whether an allowed key that no value provider has is copied to the destination
    /// unchanged, start and end sequences included, instead of throwing a <see cref="ConfigurationKeyException"/>
    /// with <see cref="ConfigurationKeyError.NotFound"/>. Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// A key that is not allowed still throws with <see cref="ConfigurationKeyError.NotAllowed"/>, and an error
    /// reported by a value provider, such as <see cref="ConfigurationKeyError.ValueUnstable"/>, still propagates.
    /// </remarks>
    public bool IgnoreUnresolvablePlaceholders { get; init; }

    /// <summary>
    /// Gets the number of levels of configuration items that are resolved. Defaults to 1.
    /// </summary>
    /// <remarks>
    /// At 1, values are inserted exactly as they are, even if they contain items. At 2, the items in each
    /// value are resolved too, and their values are inserted as they are; each further level resolves one
    /// more level of values. Items in values follow the same rules as items in the source: their keys must
    /// be allowed by the reader, and escape sequences in values are applied. Values at the last level are
    /// inserted unchanged, escape sequences included. A key whose value leads back to itself throws a
    /// <see cref="ConfigurationKeyException"/> with <see cref="ConfigurationKeyError.Cycle"/>. An error in a
    /// value reports the position of the item in the source that led to it.
    /// A value that is resolved is held in a pinned buffer that is wiped afterwards, and is never read as a
    /// string.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int MaxResolutionDepth
    {
        get => _maxResolutionDepth;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _maxResolutionDepth = value;
        }
    }

    /// <inheritdoc />
    public int Resolve(ReadOnlySpan<char> source, Span<char> destination)
    {
        return Resolve<char, CharText>(source, destination, _charDelimiters);
    }

    /// <inheritdoc />
    public int Resolve(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        return Resolve<byte, Utf8Text>(source, destination, _utf8Delimiters);
    }

    private int Resolve<T, TText>(ReadOnlySpan<T> source, Span<T> destination, Delimiters<T> delimiters)
        where T : unmanaged, IEquatable<T>
        where TText : IText<T>
    {
        var allowedKeys = new HashSet<string>(_reader.Keys(), StringComparer.Ordinal);
        var written = 0;

        try
        {
            Write<T, TText>(source, destination, ref written, delimiters, allowedKeys, resolving: [], depth: 1, itemIndex: null);
            return written;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(destination));
            throw;
        }
    }

    /// <summary>
    /// Copies text to the destination, replacing each configuration item with its value.
    /// </summary>
    /// <remarks>
    /// <c>resolving</c> holds the keys whose values are being resolved, outermost first. <c>depth</c> is 1 for
    /// the source, 2 for a value in it, and so on. <c>itemIndex</c> is the index of the item in the source
    /// that led to this text, or <see langword="null"/> for the source itself.
    /// </remarks>
    private void Write<T, TText>(
        ReadOnlySpan<T> text,
        Span<T> destination,
        ref int written,
        Delimiters<T> delimiters,
        HashSet<string> allowedKeys,
        List<string> resolving,
        int depth,
        int? itemIndex)
        where T : unmanaged, IEquatable<T>
        where TText : IText<T>
    {
        ReadOnlySpan<T> startSequence = delimiters.Start;
        ReadOnlySpan<T> endSequence = delimiters.End;
        ReadOnlySpan<T> escapeSequence = delimiters.Escape;
        var offset = 0;

        while (true)
        {
            var startIndex = text.IndexOf(startSequence);
            if (startIndex < 0)
            {
                Copy(text, destination, ref written);
                return;
            }

            var keyStart = startIndex + startSequence.Length;
            int consumed;
            if (!escapeSequence.IsEmpty && text[..startIndex].EndsWith(escapeSequence))
            {
                Copy(text[..(startIndex - escapeSequence.Length)], destination, ref written);
                Copy(startSequence, destination, ref written);
                consumed = keyStart;
            }
            else
            {
                var endIndex = text[keyStart..].IndexOf(endSequence);
                if (endIndex < 0)
                {
                    Copy(text, destination, ref written);
                    return;
                }

                consumed = keyStart + endIndex + endSequence.Length;
                var key = TText.GetKey(text.Slice(keyStart, endIndex));
                if (key.Length == 0)
                {
                    Copy(text[..consumed], destination, ref written);
                }
                else
                {
                    Copy(text[..startIndex], destination, ref written);
                    var sourceIndex = itemIndex ?? offset + startIndex;
                    if (!TryWriteValue<T, TText>(key, sourceIndex, destination, ref written, delimiters, allowedKeys, resolving, depth))
                    {
                        Copy(text[startIndex..consumed], destination, ref written);
                    }
                }
            }

            text = text[consumed..];
            offset += consumed;
        }
    }

    /// <summary>
    /// Writes the value of a key to the destination, resolving the items in it if the depth allows.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the value was written; <see langword="false"/> if no provider has the key and
    /// <see cref="IgnoreUnresolvablePlaceholders"/> is set, so the item is to be copied unchanged.
    /// </returns>
    private bool TryWriteValue<T, TText>(
        string key,
        int sourceIndex,
        Span<T> destination,
        ref int written,
        Delimiters<T> delimiters,
        HashSet<string> allowedKeys,
        List<string> resolving,
        int depth)
        where T : unmanaged, IEquatable<T>
        where TText : IText<T>
    {
        if (!allowedKeys.Contains(key))
        {
            throw new ConfigurationKeyException(key, ConfigurationKeyError.NotAllowed, sourceIndex);
        }

        if (resolving.Contains(key))
        {
            throw new ConfigurationKeyException(key, ConfigurationKeyError.Cycle, sourceIndex);
        }

        ValueStatus status;
        int valueLength;
        try
        {
            status = TText.TryGetValue(_valueProvider, key, destination[written..], out valueLength);
        }
        catch (ConfigurationKeyException ex) when (ex.SourceIndex is null)
        {
            throw WithSourceIndex(ex, sourceIndex);
        }

        if (status == ValueStatus.NotFound && IgnoreUnresolvablePlaceholders)
        {
            return false;
        }

        if (status != ValueStatus.Found)
        {
            throw ValueNotWritten(key, sourceIndex, status);
        }

        var value = destination.Slice(written, valueLength);
        if (depth >= _maxResolutionDepth || value.IndexOf(delimiters.Start) < 0)
        {
            written += valueLength;
            return true;
        }

        // Move the value to a pinned copy, wipe it from the destination, and write it back resolved.
        var copy = GC.AllocateUninitializedArray<T>(valueLength, pinned: true);
        try
        {
            value.CopyTo(copy);
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(value));

            resolving.Add(key);
            Write<T, TText>(copy, destination, ref written, delimiters, allowedKeys, resolving, depth + 1, sourceIndex);
            resolving.RemoveAt(resolving.Count - 1);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(copy.AsSpan()));
        }

        return true;
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

    /// <summary>
    /// The token's sequences, encoded as characters or as UTF-8 bytes.
    /// </summary>
    private sealed record Delimiters<T>(T[] Start, T[] End, T[] Escape);

    /// <summary>
    /// The operations that differ between resolving characters and resolving UTF-8 bytes.
    /// </summary>
    private interface IText<T>
        where T : unmanaged
    {
        static abstract string GetKey(ReadOnlySpan<T> item);

        static abstract ValueStatus TryGetValue(IValueProvider provider, string key, Span<T> destination, out int written);
    }

    private readonly struct CharText : IText<char>
    {
        public static string GetKey(ReadOnlySpan<char> item) => item.Trim().ToString();

        public static ValueStatus TryGetValue(IValueProvider provider, string key, Span<char> destination, out int written)
        {
            return provider.TryGetChar(key, destination, out written);
        }
    }

    private readonly struct Utf8Text : IText<byte>
    {
        public static string GetKey(ReadOnlySpan<byte> item) => Encoding.UTF8.GetString(item).Trim();

        public static ValueStatus TryGetValue(IValueProvider provider, string key, Span<byte> destination, out int written)
        {
            return provider.TryGetBytes(key, destination, out written);
        }
    }
}
