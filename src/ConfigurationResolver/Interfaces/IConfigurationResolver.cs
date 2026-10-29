namespace ConfigurationResolver.Interfaces;

/// <summary>
/// Resolves text by replacing its configuration items, such as <c>${Name}</c>, with their values.
/// </summary>
public interface IConfigurationResolver
{
    /// <summary>
    /// Copies <paramref name="source"/> to <paramref name="destination"/>, replacing each configuration item
    /// with its value.
    /// </summary>
    /// <param name="source">The text to resolve.</param>
    /// <param name="destination">The buffer that receives the resolved text. It must not overlap <paramref name="source"/>.</param>
    /// <returns>The number of characters written to <paramref name="destination"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too small for the resolved text.</exception>
    /// <exception cref="Exceptions.ConfigurationKeyException">A configuration item's key is not allowed, has no value, or its value kept changing while it was read.</exception>
    int Resolve(ReadOnlySpan<char> source, Span<char> destination);

    /// <summary>
    /// Copies the UTF-8 <paramref name="source"/> to <paramref name="destination"/>, replacing each configuration
    /// item with its UTF-8 encoded value.
    /// </summary>
    /// <param name="source">The UTF-8 text to resolve.</param>
    /// <param name="destination">The buffer that receives the resolved UTF-8 text. It must not overlap <paramref name="source"/>.</param>
    /// <returns>The number of bytes written to <paramref name="destination"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too small for the resolved text.</exception>
    /// <exception cref="Exceptions.ConfigurationKeyException">A configuration item's key is not allowed, has no value, or its value kept changing while it was read.</exception>
    int Resolve(ReadOnlySpan<byte> source, Span<byte> destination);
}
