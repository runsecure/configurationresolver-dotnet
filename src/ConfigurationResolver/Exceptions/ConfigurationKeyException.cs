namespace ConfigurationResolver.Exceptions;

/// <summary>
/// The exception thrown when the value for a configuration key could not be resolved.
/// </summary>
/// <remarks>
/// The message contains only the item's position and the <see cref="Error"/>, so it is safe to log. The key
/// is available from <see cref="Key"/>; it may come from the source text, which may be untrusted, so treat
/// it as untrusted input before logging or displaying it.
/// </remarks>
public sealed class ConfigurationKeyException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationKeyException"/> class for a key whose
    /// position in a source is not known, such as when thrown by a value provider.
    /// </summary>
    /// <param name="key">The configuration key.</param>
    /// <param name="error">Why the value could not be resolved.</param>
    public ConfigurationKeyException(string key, ConfigurationKeyError error)
        : base($"The value for a configuration key could not be resolved ({error}).")
    {
        Key = key;
        Error = error;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationKeyException"/> class for a configuration
    /// item in a source.
    /// </summary>
    /// <param name="key">The trimmed key of the configuration item.</param>
    /// <param name="error">Why the item could not be resolved.</param>
    /// <param name="sourceIndex">The index in the source where the item's start sequence begins.</param>
    /// <param name="innerException">The exception that caused this one, if any.</param>
    public ConfigurationKeyException(string key, ConfigurationKeyError error, int sourceIndex, Exception? innerException = null)
        : base($"The configuration item at index {sourceIndex} could not be resolved ({error}).", innerException)
    {
        Key = key;
        Error = error;
        SourceIndex = sourceIndex;
    }

    /// <summary>
    /// Gets the configuration key. When thrown by a resolver it comes from the source text and may be untrusted.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets why the value could not be resolved.
    /// </summary>
    public ConfigurationKeyError Error { get; }

    /// <summary>
    /// Gets the index in the source where the item's start sequence begins: a character index when
    /// resolving characters, or a byte index when resolving UTF-8 bytes. It is <see langword="null"/> when
    /// the exception was thrown outside a resolver, such as by a value provider called directly.
    /// </summary>
    public int? SourceIndex { get; }
}
