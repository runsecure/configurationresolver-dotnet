using ConfigurationResolver.Interfaces;

namespace ConfigurationResolver.Buffers;

/// <summary>
/// Resolves text into a <see cref="SecretBuffer{T}"/>.
/// </summary>
public static class SecretBufferExtensions
{
    /// <summary>
    /// Wipes <paramref name="destination"/>, then resolves <paramref name="source"/> into it.
    /// </summary>
    /// <param name="resolver">The resolver to use.</param>
    /// <param name="source">The text to resolve.</param>
    /// <param name="destination">The buffer that receives the resolved text. It is wiped if resolving fails.</param>
    /// <returns>The number of characters written, which is also <paramref name="destination"/>'s new length.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resolver"/> or <paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="destination"/> has been disposed.</exception>
    public static int Resolve(this IConfigurationResolver resolver, ReadOnlySpan<char> source, SecretBuffer<char> destination)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(destination);

        var span = destination.BeginWrite();
        try
        {
            var written = resolver.Resolve(source, span);
            destination.EndWrite(written);
            return written;
        }
        catch
        {
            // Another resolver implementation may not wipe the destination itself.
            destination.Clear();
            throw;
        }
    }

    /// <summary>
    /// Wipes <paramref name="destination"/>, then resolves the UTF-8 <paramref name="source"/> into it.
    /// </summary>
    /// <param name="resolver">The resolver to use.</param>
    /// <param name="source">The UTF-8 text to resolve.</param>
    /// <param name="destination">The buffer that receives the resolved UTF-8 text. It is wiped if resolving fails.</param>
    /// <returns>The number of bytes written, which is also <paramref name="destination"/>'s new length.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resolver"/> or <paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="destination"/> has been disposed.</exception>
    public static int Resolve(this IConfigurationResolver resolver, ReadOnlySpan<byte> source, SecretBuffer<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(destination);

        var span = destination.BeginWrite();
        try
        {
            var written = resolver.Resolve(source, span);
            destination.EndWrite(written);
            return written;
        }
        catch
        {
            // Another resolver implementation may not wipe the destination itself.
            destination.Clear();
            throw;
        }
    }
}
