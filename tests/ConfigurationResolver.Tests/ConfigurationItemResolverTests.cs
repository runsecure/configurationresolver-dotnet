using System.Text;
using ConfigurationResolver.Exceptions;
using ConfigurationResolver.Interfaces;
using ConfigurationResolver.Providers;
using ConfigurationResolver.Readers;
using ConfigurationResolver.Resolvers;

namespace ConfigurationResolver.Tests;

public class ConfigurationItemResolverTests
{
    private static readonly Dictionary<string, string> Values = new()
    {
        ["Name"] = "Ada",
        ["value 1"] = "ONE",
        ["value2"] = "TWO",
        ["value3"] = "THREE",
        ["Db:Password"] = "p@ss",
        ["Empty"] = "",
        ["Unicode"] = "café",
    };

    private readonly IConfigurationResolver _resolver =
        new ReadingResolver(new DefaultToken(), new DictionaryValueProvider(Values));

    public static TheoryData<string, string> ResolvedText => new()
    {
        { "", "" },
        { "plain text", "plain text" },
        { "${Name}", "Ada" },
        { "Hello ${Name}!", "Hello Ada!" },
        { "${ Name }", "Ada" },
        { "${\tName\n}", "Ada" },
        { "${Name}${Name}", "AdaAda" },
        { "${Db:Password}", "p@ss" },
        { "[${Empty}]", "[]" },
        { "ünï ${Unicode} ✓", "ünï café ✓" },
        {
            "this is my ${ value 1 }, and this is ${value2} and lastly this is ${value3}",
            "this is my ONE, and this is TWO and lastly this is THREE"
        },
    };

    public static TheoryData<string> UnchangedText => new()
    {
        "${}",
        "${ }",
        "start ${Name",
        "} stray end }",
        "$ {Name} $Name {Name}",
        "$${",
    };

    public static TheoryData<string, string> PartiallyResolvedText => new()
    {
        { "${Name}} extra", "Ada} extra" },
        { "${Name} then ${unterminated", "Ada then ${unterminated" },
        { "${} then ${Name}", "${} then Ada" },
    };

    [Theory]
    [MemberData(nameof(ResolvedText))]
    [MemberData(nameof(PartiallyResolvedText))]
    public void Resolve_Chars_ReplacesItems(string source, string expected)
    {
        Assert.Equal(expected, ResolveChars(_resolver, source));
    }

    [Theory]
    [MemberData(nameof(ResolvedText))]
    [MemberData(nameof(PartiallyResolvedText))]
    public void Resolve_Bytes_ReplacesItems(string source, string expected)
    {
        Assert.Equal(expected, ResolveBytes(_resolver, source));
    }

    [Theory]
    [MemberData(nameof(UnchangedText))]
    public void Resolve_Chars_CopiesTextWithoutCompleteItemsUnchanged(string source)
    {
        Assert.Equal(source, ResolveChars(_resolver, source));
    }

    [Theory]
    [MemberData(nameof(UnchangedText))]
    public void Resolve_Bytes_CopiesTextWithoutCompleteItemsUnchanged(string source)
    {
        Assert.Equal(source, ResolveBytes(_resolver, source));
    }

    [Fact]
    public void Resolve_Chars_ReturnsCountAndLeavesRestOfDestinationUntouched()
    {
        var destination = Filled('#', 16);

        var written = _resolver.Resolve("x=${Name}", destination);

        Assert.Equal(5, written);
        Assert.Equal("x=Ada###########", new string(destination));
    }

    [Fact]
    public void Resolve_Bytes_ReturnsCountAndLeavesRestOfDestinationUntouched()
    {
        var destination = Filled((byte)'#', 8);

        var written = _resolver.Resolve("${Unicode}"u8, destination);

        Assert.Equal(5, written);
        Assert.Equal("café###", Encoding.UTF8.GetString(destination));
    }

    [Fact]
    public void Resolve_Chars_SucceedsWhenDestinationIsExactlyTheResolvedLength()
    {
        Assert.Equal("x=Ada", ResolveChars(_resolver, "x=${Name}", destinationLength: 5));
    }

    [Fact]
    public void Resolve_Bytes_SucceedsWhenDestinationIsExactlyTheResolvedLength()
    {
        // "café" is 4 characters but 5 UTF-8 bytes.
        Assert.Equal("x=café", ResolveBytes(_resolver, "x=${Unicode}", destinationLength: 7));
    }

    [Fact]
    public void Resolve_EmptySourceIntoEmptyDestination_WritesNothing()
    {
        Assert.Equal(0, _resolver.Resolve(ReadOnlySpan<char>.Empty, Span<char>.Empty));
        Assert.Equal(0, _resolver.Resolve(ReadOnlySpan<byte>.Empty, Span<byte>.Empty));
    }

    [Theory]
    [InlineData("{{", "}}", "{{Name}} and ${Name}", "Ada and ${Name}")]
    [InlineData("{{", "}}", "{{ Name }}{{Name}", "Ada{{Name}")]
    [InlineData("<%", "%>", "<%Db:Password%>", "p@ss")]
    [InlineData("«", "»", "« Unicode » and «Name»", "café and Ada")]
    public void Resolve_UsesTheTokensStartAndEndSequences(string start, string end, string source, string expected)
    {
        var resolver = new ReadingResolver(new CustomToken(start, end), new DictionaryValueProvider(Values));

        Assert.Equal(expected, ResolveChars(resolver, source));
        Assert.Equal(expected, ResolveBytes(resolver, source));
    }

    [Fact]
    public void Resolve_Chars_WritesValuesDirectlyIntoTheDestination()
    {
        var spy = new SpyValueProvider(Values);
        var resolver = new ReadingResolver(new DefaultToken(), spy);

        Assert.Equal("ab-Ada-TWO", ResolveChars(resolver, "ab-${Name}-${value2}", destinationLength: 32));

        Assert.Equal(["Name", "value2"], spy.TryGetCharCalls);
        Assert.Equal([32 - 3, 32 - 7], spy.BufferLengths);
        Assert.Empty(spy.TryGetValueCalls);
        Assert.Empty(spy.TryGetBytesCalls);
    }

    [Fact]
    public void Resolve_Bytes_WritesValuesDirectlyIntoTheDestination()
    {
        var spy = new SpyValueProvider(Values);
        var resolver = new ReadingResolver(new DefaultToken(), spy);

        Assert.Equal("ü-café", ResolveBytes(resolver, "ü-${Unicode}", destinationLength: 32));

        Assert.Equal(["Unicode"], spy.TryGetBytesCalls);
        Assert.Equal([32 - 3], spy.BufferLengths);
        Assert.Empty(spy.TryGetValueCalls);
        Assert.Empty(spy.TryGetCharCalls);
    }

    [Theory]
    [InlineData("${Name} ${missing}", "missing", 64)]
    [InlineData("${Name} ${value2}", "value2", 5)]
    public void Resolve_NeverReadsValuesAsStrings_EvenWhenItFails(string source, string failingKey, int destinationLength)
    {
        var spy = new SpyValueProvider(Values);
        var resolver = new ReadingResolver(new DefaultToken(), spy);

        Assert.ThrowsAny<Exception>(() => ResolveChars(resolver, source, destinationLength));
        Assert.ThrowsAny<Exception>(() => ResolveBytes(resolver, source, destinationLength));

        Assert.Equal(["Name", failingKey], spy.TryGetCharCalls);
        Assert.Equal(["Name", failingKey], spy.TryGetBytesCalls);
        Assert.Empty(spy.TryGetValueCalls);
    }

    [Fact]
    public void Constructor_NullToken_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => new ConfigurationItemResolver(null!, new ConfigurationItemReader(new DefaultToken()), new DictionaryValueProvider(Values)));

        Assert.Equal("token", exception.ParamName);
    }

    [Fact]
    public void Constructor_NullReader_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => new ConfigurationItemResolver(new DefaultToken(), null!, new DictionaryValueProvider(Values)));

        Assert.Equal("reader", exception.ParamName);
    }

    [Fact]
    public void Constructor_NullValueProvider_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => new ConfigurationItemResolver(new DefaultToken(), new ConfigurationItemReader(new DefaultToken()), null!));

        Assert.Equal("valueProvider", exception.ParamName);
    }

    [Fact]
    public void Resolve_KeysReadFromATrustedTemplate_AreResolvedInAnotherSource()
    {
        var (reader, resolver, _) = CreateAllowListed();
        reader.ReadKeys("${Name} ${ Db:Password }");

        Assert.Equal("p@ss for Ada", ResolveChars(resolver, "${Db:Password} for ${Name}"));
        Assert.Equal("p@ss for Ada", ResolveBytes(resolver, "${Db:Password} for ${Name}"));
    }

    [Theory]
    [InlineData("${Db:Password}", "Db:Password", 0, 0)]
    [InlineData("${Name} then ${Db:Password}", "Db:Password", 13, 13)]
    [InlineData("${name}", "name", 0, 0)]
    [InlineData("ü ${ Db:Password }", "Db:Password", 2, 3)]
    public void Resolve_KeyNotReadByTheReader_ThrowsWithoutAskingTheProvider(string source, string rejectedKey, int charIndex, int byteIndex)
    {
        var (reader, resolver, spy) = CreateAllowListed();
        reader.ReadKeys("${Name}");
        var chars = Filled('#', 64);
        var bytes = Filled((byte)'#', 64);

        var charException = Assert.Throws<ConfigurationKeyException>(() => resolver.Resolve(source, chars));
        var byteException = Assert.Throws<ConfigurationKeyException>(() => resolver.Resolve(Encoding.UTF8.GetBytes(source), bytes));

        AssertKeyException(charException, rejectedKey, ConfigurationKeyError.NotAllowed, charIndex);
        AssertKeyException(byteException, rejectedKey, ConfigurationKeyError.NotAllowed, byteIndex);
        Assert.DoesNotContain(rejectedKey, spy.TryGetCharCalls);
        Assert.DoesNotContain(rejectedKey, spy.TryGetBytesCalls);
        Assert.All(chars, c => Assert.Equal('\0', c));
        Assert.All(bytes, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Resolve_BeforeAnyKeysAreRead_ResolvesOnlyTextWithoutItems()
    {
        var (_, resolver, spy) = CreateAllowListed();

        Assert.Equal("plain ${} text", ResolveChars(resolver, "plain ${} text"));
        Assert.Throws<ConfigurationKeyException>(() => ResolveChars(resolver, "${Name}"));
        Assert.Empty(spy.TryGetCharCalls);
    }

    [Fact]
    public void Resolve_UsesTheReadersMostRecentKeys()
    {
        var (reader, resolver, _) = CreateAllowListed();
        reader.ReadKeys("${Name}");
        Assert.Equal("Ada", ResolveChars(resolver, "${Name}"));

        reader.ReadKeys("${value2}");

        Assert.Equal("TWO", ResolveChars(resolver, "${value2}"));
        Assert.Throws<ConfigurationKeyException>(() => ResolveChars(resolver, "${Name}"));
    }

    [Theory]
    [InlineData("${missing}", "missing", 0)]
    [InlineData("${Name} then ${ missing }", "missing", 13)]
    [InlineData("${name}", "name", 0)]
    [InlineData("${a${Name}}", "a${Name", 0)]
    [InlineData("é ${missing}", "missing", 2)]
    public void Resolve_Chars_MissingKey_ThrowsAndClearsDestination(string source, string missingKey, int sourceIndex)
    {
        var destination = Filled('#', 64);

        var exception = Assert.Throws<ConfigurationKeyException>(() => _resolver.Resolve(source, destination));

        AssertKeyException(exception, missingKey, ConfigurationKeyError.NotFound, sourceIndex);
        Assert.All(destination, c => Assert.Equal('\0', c));
    }

    [Theory]
    [InlineData("${missing}", "missing", 0)]
    [InlineData("${Name} then ${ missing }", "missing", 13)]
    [InlineData("${name}", "name", 0)]
    [InlineData("${a${Name}}", "a${Name", 0)]
    [InlineData("é ${missing}", "missing", 3)]
    public void Resolve_Bytes_MissingKey_ThrowsAndClearsDestination(string source, string missingKey, int sourceIndex)
    {
        var destination = Filled((byte)'#', 64);

        var exception = Assert.Throws<ConfigurationKeyException>(() => _resolver.Resolve(Encoding.UTF8.GetBytes(source), destination));

        AssertKeyException(exception, missingKey, ConfigurationKeyError.NotFound, sourceIndex);
        Assert.All(destination, b => Assert.Equal(0, b));
    }

    [Theory]
    [InlineData("literal too long", 4)]
    [InlineData("ab ${Name}", 2)]
    [InlineData("ab ${Name}", 5)]
    [InlineData("${Name} trailing text", 6)]
    [InlineData("${Db:Password} ${Name}", 6)]
    [InlineData("x", 0)]
    public void Resolve_Chars_DestinationTooSmall_ThrowsAndClearsDestination(string source, int destinationLength)
    {
        var destination = Filled('#', destinationLength);

        var exception = Assert.Throws<ArgumentException>(() => _resolver.Resolve(source, destination));

        Assert.Equal("destination", exception.ParamName);
        Assert.All(destination, c => Assert.Equal('\0', c));
    }

    [Theory]
    [InlineData("literal too long", 4)]
    [InlineData("ab ${Name}", 2)]
    [InlineData("ab ${Name}", 5)]
    [InlineData("${Unicode}", 4)]
    [InlineData("${Db:Password} ${Name}", 6)]
    [InlineData("x", 0)]
    public void Resolve_Bytes_DestinationTooSmall_ThrowsAndClearsDestination(string source, int destinationLength)
    {
        var destination = Filled((byte)'#', destinationLength);

        var exception = Assert.Throws<ArgumentException>(() => _resolver.Resolve(Encoding.UTF8.GetBytes(source), destination));

        Assert.Equal("destination", exception.ParamName);
        Assert.All(destination, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Resolve_ProviderThrows_PropagatesAndClearsDestination()
    {
        var spy = new SpyValueProvider(Values) { ThrowOnLookup = new InvalidOperationException("provider failed") };
        var resolver = new ReadingResolver(new DefaultToken(), spy);
        var chars = Filled('#', 16);
        var bytes = Filled((byte)'#', 16);

        Assert.Throws<InvalidOperationException>(() => resolver.Resolve("ab ${Name}", chars));
        Assert.Throws<InvalidOperationException>(() => resolver.Resolve("ab ${Name}"u8, bytes));

        Assert.All(chars, c => Assert.Equal('\0', c));
        Assert.All(bytes, b => Assert.Equal(0, b));
    }

    private static void AssertKeyException(ConfigurationKeyException exception, string key, ConfigurationKeyError error, int sourceIndex)
    {
        Assert.Equal(key, exception.Key);
        Assert.Equal(error, exception.Error);
        Assert.Equal(sourceIndex, exception.SourceIndex);
        Assert.Equal($"The configuration item at index {sourceIndex} could not be resolved ({error}).", exception.Message);
        Assert.DoesNotContain(key, exception.Message);
    }

    private static (ConfigurationItemReader Reader, ConfigurationItemResolver Resolver, SpyValueProvider Spy) CreateAllowListed()
    {
        var reader = new ConfigurationItemReader(new DefaultToken());
        var spy = new SpyValueProvider(Values);
        return (reader, new ConfigurationItemResolver(new DefaultToken(), reader, spy), spy);
    }

    private static string ResolveChars(IConfigurationResolver resolver, string source, int destinationLength = 1024)
    {
        var destination = new char[destinationLength];
        var written = resolver.Resolve(source, destination);
        return new string(destination, 0, written);
    }

    private static string ResolveBytes(IConfigurationResolver resolver, string source, int destinationLength = 1024)
    {
        var destination = new byte[destinationLength];
        var written = resolver.Resolve(Encoding.UTF8.GetBytes(source), destination);
        return Encoding.UTF8.GetString(destination, 0, written);
    }

    private static T[] Filled<T>(T value, int length)
    {
        var array = new T[length];
        Array.Fill(array, value);
        return array;
    }
}
