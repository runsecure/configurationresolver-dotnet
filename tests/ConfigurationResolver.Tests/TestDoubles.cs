using System.Text;
using ConfigurationResolver.Interfaces;
using ConfigurationResolver.Readers;
using ConfigurationResolver.Resolvers;
using ConfigurationResolver.Tokens;

namespace ConfigurationResolver.Tests;

/// <summary>
/// The default <c>${</c> and <c>}</c> token.
/// </summary>
internal sealed class DefaultToken : ConfigurationItemTokenBase
{
}

/// <summary>
/// A token with custom start and end sequences.
/// </summary>
internal sealed class CustomToken(string startSequence, string endSequence)
    : ConfigurationItemTokenBase(startSequence, endSequence)
{
}

/// <summary>
/// Reads the keys of each source before resolving it, so every key in the source is allowed.
/// </summary>
internal sealed class ReadingResolver : IConfigurationResolver
{
    private readonly ConfigurationItemReader _reader;
    private readonly ConfigurationItemResolver _resolver;

    public ReadingResolver(ConfigurationItemTokenBase token, IValueProvider valueProvider)
    {
        _reader = new ConfigurationItemReader(token);
        _resolver = new ConfigurationItemResolver(token, _reader, valueProvider);
    }

    public int Resolve(ReadOnlySpan<char> source, Span<char> destination)
    {
        _reader.ReadKeys(source);
        return _resolver.Resolve(source, destination);
    }

    public int Resolve(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        _reader.ReadKeys(source);
        return _resolver.Resolve(source, destination);
    }
}

/// <summary>
/// A value provider that records how the resolver calls it.
/// </summary>
internal sealed class SpyValueProvider(IReadOnlyDictionary<string, string> values) : IValueProvider
{
    public List<string> TryGetValueCalls { get; } = [];

    public List<string> TryGetCharCalls { get; } = [];

    public List<string> TryGetBytesCalls { get; } = [];

    public List<int> BufferLengths { get; } = [];

    public Exception? ThrowOnLookup { get; init; }

    public ValueStatus TryGetValue(string key, out string? value)
    {
        TryGetValueCalls.Add(key);
        return values.TryGetValue(key, out value) ? ValueStatus.Found : ValueStatus.NotFound;
    }

    public ValueStatus TryGetChar(string key, Span<char> buffer, out int charsWritten)
    {
        TryGetCharCalls.Add(key);
        BufferLengths.Add(buffer.Length);
        ThrowIfConfigured();

        charsWritten = 0;
        if (!values.TryGetValue(key, out var value))
        {
            return ValueStatus.NotFound;
        }

        if (!value.AsSpan().TryCopyTo(buffer))
        {
            return ValueStatus.DestinationTooSmall;
        }

        charsWritten = value.Length;
        return ValueStatus.Found;
    }

    public ValueStatus TryGetBytes(string key, Span<byte> buffer, out int bytesWritten)
    {
        TryGetBytesCalls.Add(key);
        BufferLengths.Add(buffer.Length);
        ThrowIfConfigured();

        bytesWritten = 0;
        if (!values.TryGetValue(key, out var value))
        {
            return ValueStatus.NotFound;
        }

        return Encoding.UTF8.TryGetBytes(value, buffer, out bytesWritten) ? ValueStatus.Found : ValueStatus.DestinationTooSmall;
    }

    private void ThrowIfConfigured()
    {
        if (ThrowOnLookup is not null)
        {
            throw ThrowOnLookup;
        }
    }
}
