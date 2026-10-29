using ConfigurationResolver.Providers;
using Microsoft.Extensions.Configuration;

namespace ConfigurationResolver.Configuration;

/// <summary>
/// Provides configuration values from a Microsoft.Extensions.Configuration <see cref="IConfiguration"/>, such as an
/// <see cref="IConfigurationRoot"/> or a section returned by <see cref="IConfiguration.GetSection(string)"/>.
/// </summary>
/// <remarks>
/// Keys use the configuration library's <c>:</c> separator, such as <c>Logging:LogLevel:Default</c>, and
/// are matched case-insensitively. When given a section, keys are relative to it, so for
/// <c>config.GetSection("Logging")</c> the key <c>LogLevel:Default</c> reads <c>Logging:LogLevel:Default</c>.
/// Values are read on each lookup, so reloaded configuration is visible.
/// A key that names a section with children but no value of its own is not found. Configuration keeps its
/// values as strings for the life of the process, so it is not a secret store.
/// </remarks>
public class ConfigurationValueProvider : StringValueProviderBase
{
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationValueProvider"/> class.
    /// </summary>
    /// <param name="configuration">The configuration root or section used to resolve values.</param>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
    public ConfigurationValueProvider(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _configuration = configuration;
    }

    /// <inheritdoc />
    protected override bool TryGetString(string key, out string? value)
    {
        ArgumentNullException.ThrowIfNull(key);

        value = _configuration[key];
        return value is not null;
    }
}
