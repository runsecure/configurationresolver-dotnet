namespace ConfigurationResolver.Interfaces;

/// <summary>
/// Provides configuration values by key.
/// </summary>
public interface IValueProvider
{
    /// <summary>
    /// Attempts to get the value for the specified key as a string.
    /// </summary>
    /// <remarks>
    /// A string cannot be cleared from memory once created. Prefer <see cref="TryGetBytes"/> or
    /// <see cref="TryGetChar"/> for secrets.
    /// </remarks>
    /// <param name="key">The configuration key.</param>
    /// <param name="value">The value if found; otherwise <see langword="null"/>.</param>
    /// <returns><see cref="ValueStatus.Found"/> or <see cref="ValueStatus.NotFound"/>.</returns>
    ValueStatus TryGetValue(string key, out string? value);

    /// <summary>
    /// Attempts to write the value for the specified key into <paramref name="destination"/>.
    /// </summary>
    /// <param name="key">The configuration key.</param>
    /// <param name="destination">The buffer that receives the value's characters.</param>
    /// <param name="charsWritten">The number of characters written to <paramref name="destination"/>; 0 unless found.</param>
    /// <returns>Whether the value was found and whether it fit in <paramref name="destination"/>.</returns>
    ValueStatus TryGetChar(string key, Span<char> destination, out int charsWritten);

    /// <summary>
    /// Attempts to write the value for the specified key as UTF-8 into <paramref name="destination"/>.
    /// </summary>
    /// <param name="key">The configuration key.</param>
    /// <param name="destination">The buffer that receives the value's UTF-8 bytes.</param>
    /// <param name="bytesWritten">The number of bytes written to <paramref name="destination"/>; 0 unless found.</param>
    /// <returns>Whether the value was found and whether it fit in <paramref name="destination"/>.</returns>
    ValueStatus TryGetBytes(string key, Span<byte> destination, out int bytesWritten);
}
