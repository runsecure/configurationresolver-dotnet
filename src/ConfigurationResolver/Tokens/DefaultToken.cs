namespace ConfigurationResolver.Tokens;

/// <summary>
/// A token that uses the default <c>${</c> and <c>}</c> sequences, with <c>$</c> as its escape sequence, so
/// <c>$${Name}</c> is written as the literal text <c>${Name}</c>.
/// </summary>
public sealed class DefaultToken : ConfigurationItemTokenBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultToken"/> class.
    /// </summary>
    public DefaultToken()
        : base(escapeSequence: DefaultEscapeSequence)
    {
    }
}
