namespace ConfigurationResolver.Providers;

/// <summary>
/// Provides configuration values from the process's environment variables.
/// </summary>
/// <remarks>
/// Environment variables are read on each lookup, so changes made after the provider is created are
/// visible. The <c>:</c> separator in keys is translated to <c>__</c>, so the key
/// <c>Logging:LogLevel</c> is resolved from the variable <c>Logging__LogLevel</c>. Whether keys are
/// case-sensitive depends on the operating system. Environment variables are inherited by child processes and
/// can be read by other tools on the machine, so they are not a secret store.
/// </remarks>
public class EnvironmentVariableValueProvider : StringValueProviderBase
{
    /// <summary>
    /// The separator used in environment variable names.
    /// </summary>
    public const string EnvironmentSeparator = "__";

    /// <summary>
    /// The separator used in configuration keys.
    /// </summary>
    public const string KeySeparator = ":";

    /// <inheritdoc />
    protected override bool TryGetString(string key, out string? value)
    {
        ArgumentNullException.ThrowIfNull(key);

        value = Environment.GetEnvironmentVariable(key.Replace(KeySeparator, EnvironmentSeparator, StringComparison.Ordinal));
        return value is not null;
    }
}
