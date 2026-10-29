using System.Text;
using ConfigurationResolver.Configuration;
using ConfigurationResolver.Interfaces;
using ConfigurationResolver.Providers;
using Microsoft.Extensions.Configuration;

namespace ConfigurationResolver.IntegrationTests;

/// <summary>
/// Exercises the dictionary, environment variable, and Microsoft.Extensions.Configuration providers, and
/// the shared behavior of <see cref="ValueProviderBase"/>.
/// </summary>
public sealed class ProviderScenarioTests : IDisposable
{
    private readonly string _variable = "CRIT_" + Guid.NewGuid().ToString("N");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable($"{_variable}__Section__Key", null);
        Environment.SetEnvironmentVariable(_variable, null);
    }

    [Fact]
    public void Dictionary_FoundAndMissing()
    {
        var provider = new DictionaryValueProvider(new Dictionary<string, string> { ["Key"] = "value" });

        Assert.Equal(ValueStatus.Found, provider.TryGetValue("Key", out var value));
        Assert.Equal("value", value);
        Assert.Equal(ValueStatus.NotFound, provider.TryGetValue("Missing", out var missing));
        Assert.Null(missing);
    }

    [Fact]
    public void Dictionary_KeyMatchingFollowsTheDictionaryComparer()
    {
        var ordinal = new DictionaryValueProvider(new Dictionary<string, string> { ["Key"] = "value" });
        var ignoreCase = new DictionaryValueProvider(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Key"] = "value" });

        Assert.Equal(ValueStatus.NotFound, ordinal.TryGetValue("KEY", out _));
        Assert.Equal(ValueStatus.Found, ignoreCase.TryGetValue("KEY", out _));
    }

    [Fact]
    public void Dictionary_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new DictionaryValueProvider(null!));
        Assert.Throws<ArgumentNullException>(
            () => new DictionaryValueProvider(new Dictionary<string, string>()).TryGetValue(null!, out _));
    }

    [Fact]
    public void Environment_TranslatesTheKeySeparator()
    {
        Environment.SetEnvironmentVariable($"{_variable}__Section__Key", "nested");
        Environment.SetEnvironmentVariable(_variable, "plain");
        var provider = new EnvironmentVariableValueProvider();

        Assert.Equal(ValueStatus.Found, provider.TryGetValue($"{_variable}:Section:Key", out var nested));
        Assert.Equal(ValueStatus.Found, provider.TryGetValue($"{_variable}__Section__Key", out var raw));
        Assert.Equal(ValueStatus.Found, provider.TryGetValue(_variable, out var plain));

        Assert.Equal("nested", nested);
        Assert.Equal("nested", raw);
        Assert.Equal("plain", plain);
    }

    [Fact]
    public void Environment_ReadsVariablesOnEachLookup()
    {
        var provider = new EnvironmentVariableValueProvider();
        Assert.Equal(ValueStatus.NotFound, provider.TryGetValue(_variable, out var before));

        Environment.SetEnvironmentVariable(_variable, "later");

        Assert.Null(before);
        Assert.Equal(ValueStatus.Found, provider.TryGetValue(_variable, out var after));
        Assert.Equal("later", after);
    }

    [Fact]
    public void Environment_NullKey_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new EnvironmentVariableValueProvider().TryGetValue(null!, out _));
    }

    [Fact]
    public void Configuration_ReadsRootsAndSections()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Logging:LogLevel:Default"] = "Warning" })
            .Build();
        var root = new ConfigurationValueProvider(configuration);
        var section = new ConfigurationValueProvider(configuration.GetSection("Logging"));

        Assert.Equal(ValueStatus.Found, root.TryGetValue("logging:loglevel:default", out var fromRoot));
        Assert.Equal(ValueStatus.Found, section.TryGetValue("LogLevel:Default", out var fromSection));
        Assert.Equal(ValueStatus.NotFound, root.TryGetValue("Logging", out var sectionWithoutValue));
        Assert.Equal(ValueStatus.NotFound, section.TryGetValue("Logging:LogLevel:Default", out _));

        Assert.Equal("Warning", fromRoot);
        Assert.Equal("Warning", fromSection);
        Assert.Null(sectionWithoutValue);
    }

    [Fact]
    public void Configuration_SeesReloadedValues()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Key"] = "first" })
            .Build();
        var provider = new ConfigurationValueProvider(configuration);

        configuration["Key"] = "second";

        Assert.Equal(ValueStatus.Found, provider.TryGetValue("Key", out var value));
        Assert.Equal("second", value);
    }

    [Fact]
    public void Configuration_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new ConfigurationValueProvider(null!));
        Assert.Throws<ArgumentNullException>(
            () => new ConfigurationValueProvider(new ConfigurationBuilder().Build()).TryGetValue(null!, out _));
    }

    [Fact]
    public void StringProvider_TryGetCharAndTryGetBytes_WriteIntoTheBuffer()
    {
        var provider = new DictionaryValueProvider(new Dictionary<string, string> { ["Key"] = "café" });
        Span<char> chars = stackalloc char[8];
        Span<byte> bytes = stackalloc byte[8];

        Assert.Equal(ValueStatus.Found, provider.TryGetChar("Key", chars, out var charsWritten));
        Assert.Equal(ValueStatus.Found, provider.TryGetBytes("Key", bytes, out var bytesWritten));

        Assert.Equal("café", chars[..charsWritten].ToString());
        Assert.Equal("café", Encoding.UTF8.GetString(bytes[..bytesWritten]));
    }

    [Fact]
    public void BufferTooSmallOrKeyMissing_ReportsTheStatus()
    {
        var provider = new DictionaryValueProvider(new Dictionary<string, string> { ["Key"] = "café" });

        Assert.Equal(ValueStatus.DestinationTooSmall, provider.TryGetChar("Key", stackalloc char[3], out var smallChars));
        Assert.Equal(ValueStatus.DestinationTooSmall, provider.TryGetBytes("Key", stackalloc byte[4], out var smallBytes));
        Assert.Equal(ValueStatus.NotFound, provider.TryGetChar("Missing", stackalloc char[8], out var missingChars));
        Assert.Equal(ValueStatus.NotFound, provider.TryGetBytes("Missing", stackalloc byte[8], out var missingBytes));

        Assert.Equal(0, smallChars + smallBytes + missingChars + missingBytes);
    }

    [Fact]
    public void Base_SizingMethods_ReportTheRequiredBufferSize()
    {
        var provider = new SizingDictionaryValueProvider(new Dictionary<string, string> { ["Key"] = "café" });

        Assert.True(provider.CharCount("Key", out var charCount));
        Assert.True(provider.ByteCount("Key", out var byteCount));
        Assert.False(provider.CharCount("Missing", out var missingChars));
        Assert.False(provider.ByteCount("Missing", out var missingBytes));

        Assert.Equal(4, charCount);
        Assert.Equal(5, byteCount);
        Assert.Equal(0, missingChars);
        Assert.Equal(0, missingBytes);
    }
}
