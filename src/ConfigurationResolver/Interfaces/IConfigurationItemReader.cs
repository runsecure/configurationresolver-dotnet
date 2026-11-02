using System.Text;

namespace ConfigurationResolver.Interfaces;

/// <summary>
/// Reads text and extracts the keys of the configuration items it contains.
/// </summary>
public interface IConfigurationItemReader
{
    /// <summary>
    /// Gets the parsed keys for this configuration item
    /// </summary>
    IEnumerable<string> Keys();

    /// <summary>
    /// Reads the stream as text and extracts the keys of the configuration items it contains.
    /// </summary>
    /// <param name="stream">The stream to read. It is read from its current position and is left open.</param>
    /// <param name="encoding">
    /// The encoding of the stream. Defaults to UTF-8; a byte order mark in the stream takes precedence.
    /// </param>
    /// <returns>The trimmed keys, in the order they appear.</returns>
    IReadOnlyList<string> ReadKeys(Stream stream, Encoding? encoding = null);

    /// <summary>
    /// Decodes the UTF-8 bytes as text and extracts the keys of the configuration items they contain.
    /// </summary>
    /// <param name="utf8Text">The UTF-8 encoded text.</param>
    /// <returns>The trimmed keys, in the order they appear.</returns>
    IReadOnlyList<string> ReadKeys(ReadOnlySpan<byte> utf8Text);

    /// <summary>
    /// Extracts the keys of the configuration items in the text.
    /// </summary>
    /// <param name="text">The text to search.</param>
    /// <returns>The trimmed keys, in the order they appear.</returns>
    IReadOnlyList<string> ReadKeys(ReadOnlySpan<char> text);

    /// <summary>
    /// Extracts the keys of the configuration items in each of several texts, and keeps the keys from all of them.
    /// </summary>
    /// <param name="texts">The texts to search. Each is read on its own.</param>
    /// <returns>The trimmed keys, in the order they appear, text by text.</returns>
    IReadOnlyList<string> ReadKeys(IEnumerable<string> texts);
}
