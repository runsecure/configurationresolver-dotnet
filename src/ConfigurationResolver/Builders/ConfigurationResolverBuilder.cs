using ConfigurationResolver.Interfaces;
using ConfigurationResolver.Resolvers;
using ConfigurationResolver.Tokens;

namespace ConfigurationResolver.Builders;

/// <summary>
/// Builds a <see cref="ConfigurationItemResolver"/> from a token, a reader, and one or more value providers.
/// </summary>
/// <remarks>
/// The token defaults to <see cref="DefaultToken"/>. A reader and at least one value provider are required.
/// The reader is not created for you, because only you can read the keys of your trusted template into it;
/// create it with the same token. Each call to <see cref="Build"/> returns a new resolver that shares the
/// reader and the value providers.
/// <code>
/// var token = new DefaultToken();
/// var reader = new ConfigurationItemReader(token);
/// reader.ReadKeys(template);
///
/// var resolver = new ConfigurationResolverBuilder()
///     .WithToken(token)
///     .WithReader(reader)
///     .AddValueProvider(new FileSecretValueProvider("/run/secrets"))
///     .AddValueProvider(new EnvironmentVariableValueProvider())
///     .Build();
/// </code>
/// </remarks>
public class ConfigurationResolverBuilder : IConfigurationResolverBuilder
{
    private readonly List<IValueProvider> _valueProviders = [];
    private ConfigurationItemTokenBase _token = new DefaultToken();
    private IConfigurationItemReader? _reader;
    private bool _ignoreUnresolvablePlaceholders;
    private int _maxResolutionDepth = 1;

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="token"/> is <see langword="null"/>.</exception>
    public IConfigurationResolverBuilder WithToken(ConfigurationItemTokenBase token)
    {
        ArgumentNullException.ThrowIfNull(token);

        _token = token;
        return this;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
    public IConfigurationResolverBuilder WithReader(IConfigurationItemReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        _reader = reader;
        return this;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="valueProvider"/> is <see langword="null"/>.</exception>
    public IConfigurationResolverBuilder AddValueProvider(IValueProvider valueProvider)
    {
        ArgumentNullException.ThrowIfNull(valueProvider);

        _valueProviders.Add(valueProvider);
        return this;
    }

    /// <inheritdoc />
    public IConfigurationResolverBuilder IgnoreUnresolvablePlaceholders(bool ignore = true)
    {
        _ignoreUnresolvablePlaceholders = ignore;
        return this;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxResolutionDepth"/> is less than 1.</exception>
    public IConfigurationResolverBuilder WithMaxResolutionDepth(int maxResolutionDepth)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxResolutionDepth, 1);

        _maxResolutionDepth = maxResolutionDepth;
        return this;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">No reader has been set, or no value provider has been added.</exception>
    public IConfigurationResolver Build()
    {
        if (_reader is null)
        {
            throw new InvalidOperationException("A reader is required. Call WithReader before Build.");
        }

        if (_valueProviders.Count == 0)
        {
            throw new InvalidOperationException("At least one value provider is required. Call AddValueProvider before Build.");
        }

        return new ConfigurationItemResolver(_token, _reader, _valueProviders.ToArray())
        {
            IgnoreUnresolvablePlaceholders = _ignoreUnresolvablePlaceholders,
            MaxResolutionDepth = _maxResolutionDepth,
        };
    }
}
