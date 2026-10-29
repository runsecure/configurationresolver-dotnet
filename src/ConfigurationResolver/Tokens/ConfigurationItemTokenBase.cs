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
    /// Initializes a new instance of the <see cref="ConfigurationItemTokenBase"/> class.
    /// </summary>
    /// <param name="startSequence">The sequence that marks the start of a configuration item.</param>
    /// <param name="endSequence">The sequence that marks the end of a configuration item.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="startSequence"/> or <paramref name="endSequence"/> is <see langword="null"/> or empty.
    /// </exception>
    protected ConfigurationItemTokenBase(
        string startSequence = DefaultStartSequence,
        string endSequence = DefaultEndSequence)
    {
        ArgumentException.ThrowIfNullOrEmpty(startSequence);
        ArgumentException.ThrowIfNullOrEmpty(endSequence);

        StartSequence = startSequence;
        EndSequence = endSequence;
    }

    /// <summary>
    /// Gets the sequence that marks the start of a configuration item.
    /// </summary>
    public string StartSequence { get; }

    /// <summary>
    /// Gets the sequence that marks the end of a configuration item.
    /// </summary>
    public string EndSequence { get; }
}
