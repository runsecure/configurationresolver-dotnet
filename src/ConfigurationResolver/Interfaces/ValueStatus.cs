namespace ConfigurationResolver.Interfaces;

/// <summary>
/// The result of asking an <see cref="IValueProvider"/> for a value.
/// </summary>
public enum ValueStatus
{
    /// <summary>
    /// The key was found and its value was written.
    /// </summary>
    Found,

    /// <summary>
    /// The key was not found. Nothing was written.
    /// </summary>
    NotFound,

    /// <summary>
    /// The key was found, but its value does not fit in the destination. Nothing usable was written.
    /// </summary>
    DestinationTooSmall,
}
