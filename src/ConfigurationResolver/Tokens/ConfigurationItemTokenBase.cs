namespace ConfigurationResolver.Tokens;

/// <summary>
/// Base class for tokens that identify configuration items delimited by a start and end sequence,
/// such as <c>${Name}</c>.
/// </summary>
public abstract class ConfigurationItemTokenBase
{
    /// <summary>
    /// The default sequence that marks the start of a configuration item.
    /// </summary>
    public const string DefaultStartSequence = "${";

    /// <summary>
    /// The default sequence that marks the end of a configuration item.
    /// </summary>
    public const string DefaultEndSequence = "}";

    /// <summary>
    /// The escape sequence used by <see cref="DefaultToken"/>, so that <c>$${Name}</c> is written
    /// as the literal text <c>${Name}</c>.
    /// </summary>
    public const string DefaultEscapeSequence = "$";

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationItemTokenBase"/> class.
    /// </summary>
    /// <param name="startSequence">The sequence that marks the start of a configuration item.</param>
    /// <param name="endSequence">The sequence that marks the end of a configuration item.</param>
    /// <param name="escapeSequence">
    /// The sequence that, directly before a start sequence, makes it literal text instead of the start of an
    /// item; or <see langword="null"/> for no escape sequence, the default.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="startSequence"/> or <paramref name="endSequence"/> is <see langword="null"/> or empty, or
    /// <paramref name="escapeSequence"/> is empty.
    /// </exception>
    protected ConfigurationItemTokenBase(
        string startSequence = DefaultStartSequence,
        string endSequence = DefaultEndSequence,
        string? escapeSequence = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(startSequence);
        ArgumentException.ThrowIfNullOrEmpty(endSequence);
        if (escapeSequence is not null)
        {
            ArgumentException.ThrowIfNullOrEmpty(escapeSequence);
        }

        StartSequence = startSequence;
        EndSequence = endSequence;
        EscapeSequence = escapeSequence;
    }

    /// <summary>
    /// Gets the sequence that marks the start of a configuration item.
    /// </summary>
    public string StartSequence { get; }

    /// <summary>
    /// Gets the sequence that marks the end of a configuration item.
    /// </summary>
    public string EndSequence { get; }

    /// <summary>
    /// Gets the sequence that, directly before a start sequence, makes it literal text, or
    /// <see langword="null"/> if the token has no escape sequence.
    /// </summary>
    /// <remarks>
    /// The escape sequence is removed and the start sequence is copied as text, so with the escape sequence
    /// <c>$</c>, <c>$${Name}</c> is written as <c>${Name}</c> and <c>Name</c> is not a key. Only the start
    /// sequence is escaped: the text after it is read as usual, so it can contain other items. An escape
    /// sequence anywhere else is ordinary text.
    /// </remarks>
    public string? EscapeSequence { get; }
}
