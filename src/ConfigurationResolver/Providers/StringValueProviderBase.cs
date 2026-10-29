using System.Security.Cryptography;
using System.Text;
using ConfigurationResolver.Interfaces;

namespace ConfigurationResolver.Providers;

/// <summary>
/// Base class for value providers whose values are already strings, such as dictionaries, environment
/// variables, or Microsoft.Extensions.Configuration.
/// </summary>
/// <remarks>
/// Derived classes implement <see cref="TryGetString"/>; every other method is built on it. Because the
/// source already holds its values as strings, which cannot be wiped from memory, these providers are not
/// suited to secrets. Use a <see cref="ValueProviderBase"/> that reads bytes, such as
/// <see cref="FileSecretValueProvider"/>, instead.
/// </remarks>
public abstract class StringValueProviderBase : ValueProviderBase
{
    /// <summary>
    /// Attempts to get the value for the specified key from the source.
    /// </summary>
    /// <param name="key">The configuration key.</param>
    /// <param name="value">The value if found; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the key was found; otherwise <see langword="false"/>.</returns>
    protected abstract bool TryGetString(string key, out string? value);

    /// <inheritdoc />
    public override ValueStatus TryGetValue(string key, out string? value)
    {
        return TryGetString(key, out value) ? ValueStatus.Found : ValueStatus.NotFound;
    }

    /// <inheritdoc />
    public override ValueStatus TryGetChar(string key, Span<char> destination, out int charsWritten)
    {
        charsWritten = 0;
        if (!TryGetString(key, out var value))
        {
            return ValueStatus.NotFound;
        }

        if (!value.AsSpan().TryCopyTo(destination))
        {
            return ValueStatus.DestinationTooSmall;
        }

        charsWritten = value!.Length;
        return ValueStatus.Found;
    }

    /// <inheritdoc />
    public override ValueStatus TryGetBytes(string key, Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0;
        if (!TryGetString(key, out var value))
        {
            return ValueStatus.NotFound;
        }

        if (Encoding.UTF8.TryGetBytes(value, destination, out bytesWritten))
        {
            return ValueStatus.Found;
        }

        CryptographicOperations.ZeroMemory(destination);
        return ValueStatus.DestinationTooSmall;
    }

    /// <inheritdoc />
    protected override bool TryGetByteCount(string key, out int byteCount)
    {
        var found = TryGetString(key, out var value);
        byteCount = found ? Encoding.UTF8.GetByteCount(value!) : 0;
        return found;
    }

    /// <inheritdoc />
    protected override bool TryGetCharCount(string key, out int charCount)
    {
        var found = TryGetString(key, out var value);
        charCount = found ? value!.Length : 0;
        return found;
    }
}
