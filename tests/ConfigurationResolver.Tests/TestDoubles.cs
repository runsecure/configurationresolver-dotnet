using System.Text;
using ConfigurationResolver.Interfaces;
using ConfigurationResolver.Readers;
using ConfigurationResolver.Resolvers;
using ConfigurationResolver.Tokens;

namespace ConfigurationResolver.Tests;

/// <summary>
/// A token with custom start, end, and escape sequences.
/// </summary>
internal sealed class CustomToken(string startSequence, string endSequence, string? escapeSequence = null)
    : ConfigurationItemTokenBase(startSequence, endSequence, escapeSequence)
{
}

/// <summary>
/// Reads the keys of each source before resolving it, so every key in the source is allowed.
/// </summary>
internal sealed class ReadingResolver : IConfigurationResolver
{
    private readonly ConfigurationItemTokenBase _token;
    private readonly IValueProvider[] _valueProviders;
    private readonly ConfigurationItemReader _reader;
    private ConfigurationItemResolver? _resolver;

    public ReadingResolver(ConfigurationItemTokenBase token, params IValueProvider[] valueProviders)
    {
        _token = token;
        _valueProviders = valueProviders;
        _reader = new ConfigurationItemReader(token);
    }

    public bool IgnoreUnresolvablePlaceholders { get; init; }

    public int MaxResolutionDepth { get; init; } = 1;

    /// <summary>
    /// Gets texts whose keys are allowed as well as the source's, such as the keys of items inside values.
    /// </summary>
    public string[] AlsoAllowed { get; init; } = [];

    public int Resolve(ReadOnlySpan<char> source, Span<char> destination)
    {
        _reader.ReadKeys([source.ToString(), .. AlsoAllowed]);
        return Resolver.Resolve(source, destination);
    }

    public int Resolve(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        _reader.ReadKeys([Encoding.UTF8.GetString(source), .. AlsoAllowed]);
        return Resolver.Resolve(source, destination);
    }

    private ConfigurationItemResolver Resolver => _resolver ??= new ConfigurationItemResolver(_token, _reader, _valueProviders)
    {
        IgnoreUnresolvablePlaceholders = IgnoreUnresolvablePlaceholders,
        MaxResolutionDepth = MaxResolutionDepth,
    };
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
