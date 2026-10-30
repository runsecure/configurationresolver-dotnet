using ConfigurationResolver.Interfaces;

namespace ConfigurationResolver.Providers;

/// <summary>
/// Provides configuration values from several value providers, in priority order.
/// </summary>
/// <remarks>
/// Each lookup asks the providers in the order given, and the first provider that has the key supplies the
/// value; the providers after it are not asked. A provider that has the key but whose value does not fit in
/// the destination ends the lookup with <see cref="ValueStatus.DestinationTooSmall"/>, rather than falling
/// through to a lower-priority value. An exception thrown by a provider, such as a
/// <see cref="Exceptions.ConfigurationKeyException"/>, is not caught. Put the provider you trust most first.
/// </remarks>
public sealed class CompositeValueProvider : IValueProvider
{
    private readonly IValueProvider[] _providers;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositeValueProvider"/> class.
    /// </summary>
    /// <param name="providers">The providers to ask, highest priority first.</param>
    /// <exception cref="ArgumentNullException"><paramref name="providers"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="providers"/> is empty or contains <see langword="null"/>.</exception>
    public CompositeValueProvider(params IEnumerable<IValueProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        _providers = providers.ToArray();
        if (_providers.Length == 0)
        {
            throw new ArgumentException("At least one value provider is required.", nameof(providers));
        }

        if (Array.IndexOf(_providers, null) >= 0)
        {
            throw new ArgumentException("The value providers cannot contain null.", nameof(providers));
        }
    }

    /// <summary>
    /// Gets the providers, highest priority first.
    /// </summary>
    public IReadOnlyList<IValueProvider> Providers => _providers;

    /// <inheritdoc />
    public ValueStatus TryGetValue(string key, out string? value)
    {
        foreach (var provider in _providers)
        {
            if (provider.TryGetValue(key, out value) == ValueStatus.Found)
            {
                return ValueStatus.Found;
            }
        }

        value = null;
        return ValueStatus.NotFound;
    }

    /// <inheritdoc />
    public ValueStatus TryGetChar(string key, Span<char> destination, out int charsWritten)
    {
        foreach (var provider in _providers)
        {
            var status = provider.TryGetChar(key, destination, out charsWritten);
            if (status != ValueStatus.NotFound)
            {
                return status;
            }
        }

        charsWritten = 0;
        return ValueStatus.NotFound;
    }

    /// <inheritdoc />
    public ValueStatus TryGetBytes(string key, Span<byte> destination, out int bytesWritten)
    {
        foreach (var provider in _providers)
        {
            var status = provider.TryGetBytes(key, destination, out bytesWritten);
            if (status != ValueStatus.NotFound)
            {
                return status;
            }
        }

        bytesWritten = 0;
        return ValueStatus.NotFound;
    }
}
