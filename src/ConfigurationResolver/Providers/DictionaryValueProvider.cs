namespace ConfigurationResolver.Providers;

/// <summary>
/// Provides configuration values from a dictionary.
/// </summary>
/// <remarks>
/// Key matching follows the comparer of the supplied dictionary.
/// </remarks>
public class DictionaryValueProvider : StringValueProviderBase
{
    private readonly IReadOnlyDictionary<string, string> _values;

    /// <summary>
    /// Initializes a new instance of the <see cref="DictionaryValueProvider"/> class.
    /// </summary>
    /// <param name="values">The dictionary used to resolve values.</param>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is <see langword="null"/>.</exception>
    public DictionaryValueProvider(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        _values = values;
    }

    /// <inheritdoc />
    protected override bool TryGetString(string key, out string? value)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (_values.TryGetValue(key, out var found))
        {
            value = found;
            return true;
        }

        value = null;
        return false;
    }
}
