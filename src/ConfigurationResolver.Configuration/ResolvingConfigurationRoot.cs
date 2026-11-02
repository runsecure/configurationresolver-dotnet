using System.Buffers;
using ConfigurationResolver.Exceptions;
using ConfigurationResolver.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace ConfigurationResolver.Configuration;

/// <summary>
/// An <see cref="IConfigurationRoot"/> that resolves the configuration items in the values of another,
/// already built configuration root, so the value <c>Server=${Database:Host}</c> is read as
/// <c>Server=db.internal</c>.
/// </summary>
/// <remarks>
/// <para>
/// Values are resolved each time they are read, so changes and reloads are seen. Sections and children are
/// views of this root, so their values are resolved too. Setting a value sets it, unresolved, in the inner
/// configuration. Keys, children, providers, and reload tokens are those of the inner configuration.
/// </para>
/// <para>
/// The keys that may be resolved are the keys of the items in the inner configuration's values: the root
/// reads them into the reader when it is created, and again when a value refers to a key that isn't in the
/// reader, such as after a reload. A value from a value provider can therefore only refer to keys that some
/// configuration value refers to. Pass the reader that the resolver uses.
/// </para>
/// <para>
/// Give the resolver value providers that read the inner configuration, such as a
/// <c>ConfigurationValueProvider</c> for the inner root, not this root. To resolve items whose values contain
/// further items, set the resolver's maximum resolution depth above 1.
/// </para>
/// <para>
/// Resolved values are returned as strings, which cannot be wiped from memory, so this root is not suited to
/// secrets that must stay out of strings.
/// </para>
/// </remarks>
public sealed class ResolvingConfigurationRoot : IConfigurationRoot
{
    private const int MinimumBufferLength = 256;

    private readonly IConfigurationRoot _configuration;
    private readonly IConfigurationItemReader _reader;
    private readonly IConfigurationResolver _resolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResolvingConfigurationRoot"/> class.
    /// </summary>
    /// <param name="configuration">The built configuration whose values are resolved.</param>
    /// <param name="reader">The reader used by <paramref name="resolver"/>. The root reads the keys of the configuration's values into it.</param>
    /// <param name="resolver">The resolver, with its value providers, used to resolve each value.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="configuration"/>, <paramref name="reader"/>, or <paramref name="resolver"/> is <see langword="null"/>.
    /// </exception>
    public ResolvingConfigurationRoot(IConfigurationRoot configuration, IConfigurationItemReader reader, IConfigurationResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(resolver);

        _configuration = configuration;
        _reader = reader;
        _resolver = resolver;

        ReadKeys();
    }

    /// <summary>
    /// Gets or sets a configuration value. Getting it resolves its configuration items; setting it stores it
    /// unresolved in the inner configuration.
    /// </summary>
    /// <param name="key">The configuration key.</param>
    /// <returns>The resolved value, or <see langword="null"/> if the key has no value.</returns>
    /// <exception cref="ConfigurationKeyException">An item in the value is not allowed, has no value, or leads to a cycle.</exception>
    public string? this[string key]
    {
        get
        {
            var value = _configuration[key];
            if (value is null)
            {
                return null;
            }

            try
            {
                return Resolve(value);
            }
            catch (ConfigurationKeyException ex) when (ex.Error == ConfigurationKeyError.NotAllowed)
            {
                // The configuration may have changed since its keys were read.
                ReadKeys();
                return Resolve(value);
            }
        }

        set => _configuration[key] = value;
    }

    /// <inheritdoc />
    public IEnumerable<IConfigurationProvider> Providers => _configuration.Providers;

    /// <inheritdoc />
    public IConfigurationSection GetSection(string key) => new ConfigurationSection(this, key);

    /// <inheritdoc />
    public IEnumerable<IConfigurationSection> GetChildren() => _configuration.GetChildren().Select(child => GetSection(child.Key));

    /// <inheritdoc />
    public IChangeToken GetReloadToken() => _configuration.GetReloadToken();

    /// <inheritdoc />
    public void Reload() => _configuration.Reload();

    private void ReadKeys()
    {
        _reader.ReadKeys(_configuration.AsEnumerable().Select(pair => pair.Value).OfType<string>());
    }

    private string Resolve(string value)
    {
        for (var length = Math.Max(value.Length * 2, MinimumBufferLength); ; length *= 2)
        {
            var buffer = ArrayPool<char>.Shared.Rent(length);
            try
            {
                var written = _resolver.Resolve(value, buffer);
                return new string(buffer, 0, written);
            }
            catch (ArgumentException ex) when (ex.ParamName == "destination" && length <= Array.MaxLength / 2)
            {
                // Too small; try again with twice the space.
            }
            finally
            {
                ArrayPool<char>.Shared.Return(buffer, clearArray: true);
            }
        }
    }
}
