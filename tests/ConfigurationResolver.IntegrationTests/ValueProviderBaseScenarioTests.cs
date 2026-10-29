using System.Text;
using ConfigurationResolver.Exceptions;
using ConfigurationResolver.Interfaces;
using ConfigurationResolver.Providers;

namespace ConfigurationResolver.IntegrationTests;

/// <summary>
/// Exercises how <see cref="ValueProviderBase"/> builds characters, strings, and sizes from a byte source,
/// including values that change between being sized and being read.
/// </summary>
public sealed class ValueProviderBaseScenarioTests
{
    private static readonly string Large = new('x', 600);

    [Theory]
    [InlineData("café")]
    [InlineData("")]
    [MemberData(nameof(LargeValue))]
    public void BytesSource_IsReadAsStringCharsAndCount(string value)
    {
        var provider = ScriptedBytesProvider.Fixed(value);
        var chars = new char[value.Length];

        Assert.Equal(ValueStatus.Found, provider.TryGetValue("Key", out var text));
        Assert.Equal(ValueStatus.Found, provider.TryGetChar("Key", chars, out var charsWritten));
        Assert.True(provider.CharCount("Key", out var charCount));

        Assert.Equal(value, text);
        Assert.Equal(value, new string(chars, 0, charsWritten));
        Assert.Equal(value.Length, charCount);
    }

    public static TheoryData<string> LargeValue => new() { Large };

    [Fact]
    public void BytesSource_CharDestinationTooSmall_ReportsItAndWipesTheDestination()
    {
        var provider = ScriptedBytesProvider.Fixed("café");
        var chars = new char[3];
        Array.Fill(chars, '#');

        Assert.Equal(ValueStatus.DestinationTooSmall, provider.TryGetChar("Key", chars, out var charsWritten));

        Assert.Equal(0, charsWritten);
        Assert.All(chars, c => Assert.Equal('\0', c));
    }

    [Fact]
    public void BytesSource_MissingKey_IsNotFoundEverywhere()
    {
        var provider = ScriptedBytesProvider.Missing();

        Assert.Equal(ValueStatus.NotFound, provider.TryGetValue("Key", out var value));
        Assert.Equal(ValueStatus.NotFound, provider.TryGetChar("Key", new char[8], out _));
        Assert.False(provider.CharCount("Key", out var charCount));

        Assert.Null(value);
        Assert.Equal(0, charCount);
    }

    [Fact]
    public void BytesSource_RemovedBetweenSizingAndReading_IsNotFound()
    {
        var provider = new ScriptedBytesProvider(counts: [4], reads: [null]);

        Assert.Equal(ValueStatus.NotFound, provider.TryGetValue("Key", out var value));
        Assert.Null(value);
    }

    [Theory]
    [InlineData(3, "abcdef")]
    [InlineData(3, 600)]
    [InlineData(600, 700)]
    [InlineData(600, "abcdef")]
    public void BytesSource_ThatChangesSize_IsReadAgain(int firstCount, object secondValue)
    {
        var second = secondValue as string ?? new string('y', (int)secondValue);
        var tooLarge = new string('z', firstCount + 1);
        var provider = new ScriptedBytesProvider(
            counts: [firstCount, Encoding.UTF8.GetByteCount(second)],
            reads: [tooLarge, second]);

        Assert.Equal(ValueStatus.Found, provider.TryGetValue("Key", out var value));

        Assert.Equal(second, value);
        Assert.Equal(2, provider.ReadCalls);
    }

    [Fact]
    public void BytesSource_ThatKeepsGrowing_Throws()
    {
        var provider = new ScriptedBytesProvider(counts: [1, 1, 1], reads: ["ab", "ab", "ab"]);

        var exception = Assert.Throws<ConfigurationKeyException>(() => provider.TryGetValue("Key", out _));

        Assert.Equal("Key", exception.Key);
        Assert.Equal(ConfigurationKeyError.ValueUnstable, exception.Error);
        Assert.Null(exception.SourceIndex);
        Assert.Equal("The value for a configuration key could not be resolved (ValueUnstable).", exception.Message);
        Assert.Equal(3, provider.ReadCalls);
    }

    [Fact]
    public void BytesSource_ThatKeepsGrowing_IsReportedAtItsPositionInTheTemplate()
    {
        // Resolving characters goes through the base class's retry loop.
        var provider = new ScriptedBytesProvider(counts: [1], reads: ["ab", "ab", "ab"]);
        var resolver = TemplateFile.CreateResolver(new DefaultToken(), provider, "${Key}");

        var exception = Assert.Throws<ConfigurationKeyException>(() => resolver.Resolve("é ${Key}", new char[16]));

        AssertReportedAt(exception, 2);
    }

    [Fact]
    public void ProviderKeyException_IsReportedAtItsPositionInTheTemplate()
    {
        var resolver = TemplateFile.CreateResolver(new DefaultToken(), new ThrowingProvider(sourceIndex: null), "${Key}");

        AssertReportedAt(Assert.Throws<ConfigurationKeyException>(() => resolver.Resolve("é ${Key}", new char[16])), 2);
        AssertReportedAt(Assert.Throws<ConfigurationKeyException>(() => resolver.Resolve("é ${Key}"u8, new byte[16])), 3);
    }

    [Fact]
    public void ProviderKeyExceptionWithAPosition_IsNotWrapped()
    {
        var provider = new ThrowingProvider(sourceIndex: 7);
        var resolver = TemplateFile.CreateResolver(new DefaultToken(), provider, "${Key}");

        Assert.Same(provider.Exception, Assert.Throws<ConfigurationKeyException>(() => resolver.Resolve("${Key}", new char[16])));
        Assert.Same(provider.Exception, Assert.Throws<ConfigurationKeyException>(() => resolver.Resolve("${Key}"u8, new byte[16])));
    }

    private static void AssertReportedAt(ConfigurationKeyException exception, int sourceIndex)
    {
        Assert.Equal("Key", exception.Key);
        Assert.Equal(ConfigurationKeyError.ValueUnstable, exception.Error);
        Assert.Equal(sourceIndex, exception.SourceIndex);
        Assert.DoesNotContain("Key", exception.Message);
        var inner = Assert.IsType<ConfigurationKeyException>(exception.InnerException);
        Assert.Null(inner.SourceIndex);
    }

    /// <summary>
    /// A provider that throws the same <see cref="ConfigurationKeyException"/> for every lookup.
    /// </summary>
    private sealed class ThrowingProvider(int? sourceIndex) : IValueProvider
    {
        public ConfigurationKeyException Exception { get; } = sourceIndex is { } index
            ? new ConfigurationKeyException("Key", ConfigurationKeyError.ValueUnstable, index)
            : new ConfigurationKeyException("Key", ConfigurationKeyError.ValueUnstable);

        public ValueStatus TryGetValue(string key, out string? value) => throw Exception;

        public ValueStatus TryGetChar(string key, Span<char> destination, out int charsWritten) => throw Exception;

        public ValueStatus TryGetBytes(string key, Span<byte> destination, out int bytesWritten) => throw Exception;
    }

    /// <summary>
    /// A byte-based provider whose reported sizes and actual contents are scripted per call.
    /// </summary>
    private sealed class ScriptedBytesProvider(int?[] counts, string?[] reads) : ValueProviderBase
    {
        private int _countCalls;

        public int ReadCalls { get; private set; }

        public static ScriptedBytesProvider Fixed(string value)
        {
            var count = Encoding.UTF8.GetByteCount(value);
            return new ScriptedBytesProvider([count, count, count, count], [value, value, value, value]);
        }

        public static ScriptedBytesProvider Missing() => new([null, null, null], []);

        public bool CharCount(string key, out int charCount) => TryGetCharCount(key, out charCount);

        public override ValueStatus TryGetBytes(string key, Span<byte> destination, out int bytesWritten)
        {
            bytesWritten = 0;
            var value = reads[ReadCalls++];
            if (value is null)
            {
                return ValueStatus.NotFound;
            }

            return Encoding.UTF8.TryGetBytes(value, destination, out bytesWritten)
                ? ValueStatus.Found
                : ValueStatus.DestinationTooSmall;
        }

        protected override bool TryGetByteCount(string key, out int byteCount)
        {
            var count = counts[_countCalls++ % counts.Length];
            byteCount = count ?? 0;
            return count is not null;
        }
    }
}
