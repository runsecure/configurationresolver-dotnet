using ConfigurationResolver.Tokens;

namespace ConfigurationResolver.Interfaces;

/// <summary>
/// Builds an <see cref="IConfigurationResolver"/> from a token, a reader, and one or more value providers.
/// </summary>
public interface IConfigurationResolverBuilder
{
    /// <summary>
    /// Sets the token that defines the start, end, and escape sequences of a configuration item.
    /// </summary>
    /// <param name="token">The token. Use the same token as the reader.</param>
    /// <returns>This builder.</returns>
    IConfigurationResolverBuilder WithToken(ConfigurationItemTokenBase token);

    /// <summary>
    /// Sets the reader whose keys are the only keys the resolver may resolve.
    /// </summary>
    /// <param name="reader">The reader.</param>
    /// <returns>This builder.</returns>
    IConfigurationResolverBuilder WithReader(IConfigurationItemReader reader);

    /// <summary>
    /// Adds a value provider, after any added already. Providers added earlier have higher priority.
    /// </summary>
    /// <param name="valueProvider">The value provider.</param>
    /// <returns>This builder.</returns>
    IConfigurationResolverBuilder AddValueProvider(IValueProvider valueProvider);

    /// <summary>
    /// Sets whether an allowed key that no value provider has is copied unchanged instead of throwing.
    /// </summary>
    /// <param name="ignore">Whether to copy unresolvable items unchanged.</param>
    /// <returns>This builder.</returns>
    IConfigurationResolverBuilder IgnoreUnresolvablePlaceholders(bool ignore = true);

    /// <summary>
    /// Sets the number of levels of configuration items that are resolved.
    /// </summary>
    /// <param name="maxResolutionDepth">The number of levels; 1 inserts values as they are.</param>
    /// <returns>This builder.</returns>
    IConfigurationResolverBuilder WithMaxResolutionDepth(int maxResolutionDepth);

    /// <summary>
    /// Creates a resolver from the builder's settings.
    /// </summary>
    /// <returns>A new resolver.</returns>
    IConfigurationResolver Build();
}
