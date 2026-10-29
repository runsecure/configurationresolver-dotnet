using System.Buffers;
using System.Text;
using ConfigurationResolver.Interfaces;
using ConfigurationResolver.Tokens;

namespace ConfigurationResolver.Readers;

/// <summary>
/// Reads text and extracts the keys of configuration items delimited by the start and end sequences
/// of a <see cref="ConfigurationItemTokenBase"/>.
/// </summary>
public class ConfigurationItemReader : IConfigurationItemReader
{
    private readonly ConfigurationItemTokenBase _token;
    private IReadOnlyList<string> _keys = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationItemReader"/> class.
    /// </summary>
    /// <param name="token">The token that defines the start and end sequences of a configuration item.</param>
    /// <exception cref="ArgumentNullException"><paramref name="token"/> is <see langword="null"/>.</exception>
    public ConfigurationItemReader(ConfigurationItemTokenBase token)
    {
        ArgumentNullException.ThrowIfNull(token);

        _token = token;
    }

    /// <summary>
    /// Gets the keys extracted by the most recent call to a <c>ReadKeys</c> overload.
    /// </summary>
    /// <returns>The keys from the most recent read, or an empty sequence if nothing has been read.</returns>
    public IEnumerable<string> Keys() => _keys;

    /// <summary>
    /// Reads the stream as text and extracts the keys of the configuration items it contains.
    /// </summary>
    /// <param name="stream">The stream to read. It is read from its current position and is left open.</param>
    /// <param name="encoding">
    /// The encoding of the stream. Defaults to UTF-8; a byte order mark in the stream takes precedence.
    /// </param>
    /// <returns>The trimmed keys, in the order they appear.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    public IReadOnlyList<string> ReadKeys(Stream stream, Encoding? encoding = null)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var reader = new StreamReader(stream, encoding ?? Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return ReadKeys(reader.ReadToEnd().AsSpan());
    }

    /// <summary>
    /// Decodes the UTF-8 bytes as text and extracts the keys of the configuration items they contain.
    /// </summary>
    /// <param name="utf8Text">The UTF-8 encoded text.</param>
    /// <returns>The trimmed keys, in the order they appear.</returns>
    public IReadOnlyList<string> ReadKeys(ReadOnlySpan<byte> utf8Text)
    {
        var buffer = ArrayPool<char>.Shared.Rent(Encoding.UTF8.GetMaxCharCount(utf8Text.Length));
        try
        {
            var charCount = Encoding.UTF8.GetChars(utf8Text, buffer);
            return ReadKeys(buffer.AsSpan(0, charCount));
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer, clearArray: true);
        }
    }

    /// <summary>
    /// Extracts the keys of the configuration items in the text.
    /// </summary>
    /// <remarks>
    /// Keys are trimmed of surrounding whitespace, and empty keys are skipped. A start sequence
    /// with no matching end sequence is ignored.
    /// </remarks>
    /// <param name="text">The text to search.</param>
    /// <returns>The trimmed keys, in the order they appear.</returns>
    public IReadOnlyList<string> ReadKeys(ReadOnlySpan<char> text)
    {
        var keys = new List<string>();
        var startSequence = _token.StartSequence.AsSpan();
        var endSequence = _token.EndSequence.AsSpan();

        while (true)
        {
            var startIndex = text.IndexOf(startSequence, StringComparison.Ordinal);
            if (startIndex < 0)
            {
                break;
            }

            text = text[(startIndex + startSequence.Length)..];

            var endIndex = text.IndexOf(endSequence, StringComparison.Ordinal);
            if (endIndex < 0)
            {
                break;
            }

            var key = text[..endIndex].Trim();
            if (!key.IsEmpty)
            {
                keys.Add(key.ToString());
            }

            text = text[(endIndex + endSequence.Length)..];
        }

        _keys = keys;
        return keys;
    }
}
