namespace ConfigurationResolver.Exceptions;

/// <summary>
/// Why a configuration item could not be resolved.
/// </summary>
public enum ConfigurationKeyError
{
    /// <summary>
    /// The key was not read by the configuration item reader, so the value provider was not asked for it.
    /// </summary>
    NotAllowed,

    /// <summary>
    /// The key was allowed, but the value provider has no value for it.
    /// </summary>
    NotFound,

    /// <summary>
    /// The value kept changing size while it was read, such as a secret file being rewritten.
    /// </summary>
    ValueUnstable,

    /// <summary>
    /// The key's value, resolved through the items in it, leads back to the same key.
    /// </summary>
    Cycle,
}
