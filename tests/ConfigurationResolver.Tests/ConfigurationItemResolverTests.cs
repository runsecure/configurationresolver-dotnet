using System.Text;
using ConfigurationResolver.Exceptions;
using ConfigurationResolver.Interfaces;
using ConfigurationResolver.Providers;
using ConfigurationResolver.Readers;
using ConfigurationResolver.Resolvers;
using ConfigurationResolver.Tokens;

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

        Assert.Equal("valueProviders", exception.ParamName);
    }

    [Fact]
    public void Constructor_NoValueProviders_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new ConfigurationItemResolver(new DefaultToken(), new ConfigurationItemReader(new DefaultToken())));
    }

    [Fact]
    public void Constructor_NullAmongValueProviders_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new ConfigurationItemResolver(
                new DefaultToken(), new ConfigurationItemReader(new DefaultToken()), new DictionaryValueProvider(Values), null!));
    }

    [Fact]
    public void Resolve_MultipleProviders_UsesTheFirstProviderThatHasTheKey()
    {
        var high = new SpyValueProvider(new Dictionary<string, string> { ["Name"] = "Grace" });
        var low = new SpyValueProvider(Values);
        var resolver = new ReadingResolver(new DefaultToken(), high, low);

        Assert.Equal("Grace p@ss", ResolveChars(resolver, "${Name} ${Db:Password}"));
        Assert.Equal("Grace p@ss", ResolveBytes(resolver, "${Name} ${Db:Password}"));

        Assert.Equal(["Name", "Db:Password"], high.TryGetCharCalls);
        Assert.Equal(["Db:Password"], low.TryGetCharCalls);
        Assert.Equal(["Name", "Db:Password"], high.TryGetBytesCalls);
        Assert.Equal(["Db:Password"], low.TryGetBytesCalls);
    }

    [Fact]
    public void Resolve_MultipleProviders_NoneHasTheKey_ThrowsNotFound()
    {
        var resolver = new ReadingResolver(
            new DefaultToken(),
            new DictionaryValueProvider(new Dictionary<string, string> { ["Name"] = "Grace" }),
            new DictionaryValueProvider(Values));

        var charException = Assert.Throws<ConfigurationKeyException>(() => ResolveChars(resolver, "${Name} ${missing}"));
        var byteException = Assert.Throws<ConfigurationKeyException>(() => ResolveBytes(resolver, "${Name} ${missing}"));

        AssertKeyException(charException, "missing", ConfigurationKeyError.NotFound, 8);
        AssertKeyException(byteException, "missing", ConfigurationKeyError.NotFound, 8);
    }

    [Fact]
    public void Resolve_MultipleProviders_ValueTooLargeInTheFirstProvider_DoesNotFallThrough()
    {
        var high = new DictionaryValueProvider(new Dictionary<string, string> { ["Name"] = "Grace Hopper" });
        var low = new SpyValueProvider(Values);
        var resolver = new ReadingResolver(new DefaultToken(), high, low);

        Assert.Throws<ArgumentException>(() => ResolveChars(resolver, "${Name}", destinationLength: 5));
        Assert.Throws<ArgumentException>(() => ResolveBytes(resolver, "${Name}", destinationLength: 5));

        Assert.Empty(low.TryGetCharCalls);
        Assert.Empty(low.TryGetBytesCalls);
    }

    [Theory]
    [InlineData("${missing}", "${missing}")]
    [InlineData("a ${ missing } b ${Name}", "a ${ missing } b Ada")]
    [InlineData("${Name}${missing}${Name}", "Ada${missing}Ada")]
    [InlineData("é ${missing} ${Unicode}", "é ${missing} café")]
    public void Resolve_IgnoreUnresolvablePlaceholders_CopiesMissingItemsUnchanged(string source, string expected)
    {
        var resolver = new ReadingResolver(new DefaultToken(), new DictionaryValueProvider(Values)) { IgnoreUnresolvablePlaceholders = true };

        Assert.Equal(expected, ResolveChars(resolver, source));
        Assert.Equal(expected, ResolveBytes(resolver, source));
    }

    [Fact]
    public void Resolve_IgnoreUnresolvablePlaceholders_CopiesTheTokensOwnSequences()
    {
        var resolver = new ReadingResolver(new CustomToken("{{", "}}"), new DictionaryValueProvider(Values)) { IgnoreUnresolvablePlaceholders = true };

        Assert.Equal("Ada {{ missing }}", ResolveChars(resolver, "{{Name}} {{ missing }}"));
        Assert.Equal("Ada {{ missing }}", ResolveBytes(resolver, "{{Name}} {{ missing }}"));
    }

    [Fact]
    public void Resolve_IgnoreUnresolvablePlaceholders_KeyNotReadByTheReader_StillThrows()
    {
        var reader = new ConfigurationItemReader(new DefaultToken());
        var spy = new SpyValueProvider(Values);
        var resolver = new ConfigurationItemResolver(new DefaultToken(), reader, spy) { IgnoreUnresolvablePlaceholders = true };
        reader.ReadKeys("${Name}");

        var exception = Assert.Throws<ConfigurationKeyException>(() => ResolveChars(resolver, "${Name} ${Db:Password}"));

        AssertKeyException(exception, "Db:Password", ConfigurationKeyError.NotAllowed, 8);
        Assert.Equal(["Name"], spy.TryGetCharCalls);
    }

    [Fact]
    public void Resolve_IgnoreUnresolvablePlaceholders_ProviderErrorStillPropagates()
    {
        var spy = new SpyValueProvider(Values) { ThrowOnLookup = new ConfigurationKeyException("Name", ConfigurationKeyError.ValueUnstable) };
        var resolver = new ReadingResolver(new DefaultToken(), spy) { IgnoreUnresolvablePlaceholders = true };

        var exception = Assert.Throws<ConfigurationKeyException>(() => ResolveChars(resolver, "${Name}"));

        Assert.Equal(ConfigurationKeyError.ValueUnstable, exception.Error);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(13)]
    public void Resolve_IgnoreUnresolvablePlaceholders_DestinationTooSmallForTheItem_ThrowsAndClearsDestination(int destinationLength)
    {
        var resolver = new ReadingResolver(new DefaultToken(), new DictionaryValueProvider(Values)) { IgnoreUnresolvablePlaceholders = true };
        var chars = Filled('#', destinationLength);
        var bytes = Filled((byte)'#', destinationLength);

        var charException = Assert.Throws<ArgumentException>(() => resolver.Resolve("Ada ${missing}", chars));
        var byteException = Assert.Throws<ArgumentException>(() => resolver.Resolve("Ada ${missing}"u8, bytes));

        Assert.Equal("destination", charException.ParamName);
        Assert.Equal("destination", byteException.ParamName);
        Assert.All(chars, c => Assert.Equal('\0', c));
        Assert.All(bytes, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Resolve_IgnoreUnresolvablePlaceholders_IsOffByDefault()
    {
        var resolver = new ConfigurationItemResolver(new DefaultToken(), new ConfigurationItemReader(new DefaultToken()), new DictionaryValueProvider(Values));

        Assert.False(resolver.IgnoreUnresolvablePlaceholders);
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

    [Theory]
    [InlineData("$${Name}", "${Name}")]
    [InlineData("$${", "${")]
    [InlineData("$${Name} is ${Name}", "${Name} is Ada")]
    [InlineData("$$${Name}", "$${Name}")]
    [InlineData("$${a ${Name}}", "${a Ada}")]
    [InlineData("$ ${Name} $$", "$ Ada $$")]
    [InlineData("é $${Unicode}", "é ${Unicode}")]
    public void Resolve_EscapedStartSequence_IsCopiedWithoutTheEscape(string source, string expected)
    {
        Assert.Equal(expected, ResolveChars(_resolver, source));
        Assert.Equal(expected, ResolveBytes(_resolver, source));
    }

    [Fact]
    public void Resolve_EscapedItem_IsNotAKeySoItNeedsNoPermission()
    {
        var (reader, resolver, spy) = CreateAllowListed();
        reader.ReadKeys("${Name}");

        Assert.Equal("Ada ${Db:Password}", ResolveChars(resolver, "${Name} $${Db:Password}"));
        Assert.Equal("Ada ${Db:Password}", ResolveBytes(resolver, "${Name} $${Db:Password}"));

        Assert.Equal(["Name"], spy.TryGetCharCalls);
        Assert.Equal(["Name"], spy.TryGetBytesCalls);
    }

    [Theory]
    [InlineData(null, "${{Name}} \\{{Name}}", "$Ada \\Ada")]
    [InlineData("\\", "${{Name}} \\{{Name}}", "$Ada {{Name}}")]
    public void Resolve_UsesTheTokensEscapeSequence(string? escape, string source, string expected)
    {
        var resolver = new ReadingResolver(new CustomToken("{{", "}}", escape), new DictionaryValueProvider(Values));

        Assert.Equal(expected, ResolveChars(resolver, source));
        Assert.Equal(expected, ResolveBytes(resolver, source));
    }

    [Fact]
    public void MaxResolutionDepth_DefaultsToOne_AndMustBePositive()
    {
        var resolver = new ConfigurationItemResolver(new DefaultToken(), new ConfigurationItemReader(new DefaultToken()), new DictionaryValueProvider(Values));

        Assert.Equal(1, resolver.MaxResolutionDepth);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ConfigurationItemResolver(new DefaultToken(), new ConfigurationItemReader(new DefaultToken()), new DictionaryValueProvider(Values))
            {
                MaxResolutionDepth = 0,
            });
    }

    [Theory]
    [InlineData(1, "${Url}", "https://${Host}:${Port}/")]
    [InlineData(2, "${Url}", "https://${Region}.db.internal:5432/")]
    [InlineData(3, "${Url}", "https://eu.db.internal:5432/")]
    [InlineData(10, "go ${Url} now", "go https://eu.db.internal:5432/ now")]
    [InlineData(1, "${Escaped}", "$${Region}")]
    [InlineData(2, "${Escaped}", "${Region}")]
    [InlineData(2, "${Ping}", "${Ping}")]
    [InlineData(2, "${Port}${Host}", "5432eu.db.internal")]
    public void Resolve_ItemsInValues_AreResolvedUpToTheMaximumDepth(int depth, string source, string expected)
    {
        var resolver = CreateNested(depth);

        Assert.Equal(expected, ResolveChars(resolver, source));
        Assert.Equal(expected, ResolveBytes(resolver, source));
    }

    [Theory]
    [InlineData(2, "${Self}", "Self", 0)]
    [InlineData(5, "x ${Self}", "Self", 2)]
    [InlineData(3, "${Ping}", "Ping", 0)]
    [InlineData(10, "ab ${Pong}", "Pong", 3)]
    public void Resolve_ValueThatLeadsBackToItsKey_ThrowsCycleAndClearsDestination(int depth, string source, string key, int sourceIndex)
    {
        var resolver = CreateNested(depth);
        var chars = Filled('#', 256);
        var bytes = Filled((byte)'#', 256);

        var charException = Assert.Throws<ConfigurationKeyException>(() => resolver.Resolve(source, chars));
        var byteException = Assert.Throws<ConfigurationKeyException>(() => resolver.Resolve(Encoding.UTF8.GetBytes(source), bytes));

        AssertKeyException(charException, key, ConfigurationKeyError.Cycle, sourceIndex);
        AssertKeyException(byteException, key, ConfigurationKeyError.Cycle, sourceIndex);
        Assert.All(chars, c => Assert.Equal('\0', c));
        Assert.All(bytes, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Resolve_KeyInAValue_MustBeAllowed()
    {
        var spy = new SpyValueProvider(Nested);
        var resolver = new ReadingResolver(new DefaultToken(), spy) { MaxResolutionDepth = 3 };

        var exception = Assert.Throws<ConfigurationKeyException>(() => ResolveChars(resolver, "go ${Host}"));

        AssertKeyException(exception, "Region", ConfigurationKeyError.NotAllowed, 3);
        Assert.Equal(["Host"], spy.TryGetCharCalls);
    }

    [Fact]
    public void Resolve_MissingKeyInAValue_ThrowsWithTheSourceItemsIndex_OrIsKeptWhenIgnored()
    {
        var strict = CreateNested(2);
        var lenient = new ReadingResolver(new DefaultToken(), new DictionaryValueProvider(Nested))
        {
            MaxResolutionDepth = 2,
            IgnoreUnresolvablePlaceholders = true,
            AlsoAllowed = NestedKeys,
        };

        var exception = Assert.Throws<ConfigurationKeyException>(() => ResolveBytes(strict, "é ${Missing}"));

        AssertKeyException(exception, "Nope", ConfigurationKeyError.NotFound, 3);
        Assert.Equal("é [${ Nope }]", ResolveChars(lenient, "é ${Missing}"));
        Assert.Equal("é [${ Nope }]", ResolveBytes(lenient, "é ${Missing}"));
    }

    [Fact]
    public void Resolve_ValueThatShrinksWhenResolved_LeavesNoTraceOfTheRawValue()
    {
        var resolver = CreateNested(2);
        var chars = Filled('#', 64);
        var bytes = Filled((byte)'#', 64);

        var charsWritten = resolver.Resolve("${Shrinks}", chars);
        var bytesWritten = resolver.Resolve("${Shrinks}"u8, bytes);

        Assert.Equal("eu!", new string(chars, 0, charsWritten));
        Assert.Equal("eu!", Encoding.UTF8.GetString(bytes, 0, bytesWritten));
        Assert.DoesNotContain("Region", new string(chars));
        Assert.DoesNotContain("Region", Encoding.UTF8.GetString(bytes));
        Assert.Equal(new string('#', 64 - "${Region}!".Length), new string(chars, "${Region}!".Length, 64 - "${Region}!".Length));
    }

    [Fact]
    public void Resolve_ItemsInValues_AreWrittenWithoutReadingValuesAsStrings()
    {
        var spy = new SpyValueProvider(Nested);
        var resolver = new ReadingResolver(new DefaultToken(), spy) { MaxResolutionDepth = 3, AlsoAllowed = NestedKeys };

        Assert.Equal("https://eu.db.internal:5432/", ResolveChars(resolver, "${Url}"));

        Assert.Equal(["Url", "Host", "Region", "Port"], spy.TryGetCharCalls);
        Assert.Empty(spy.TryGetValueCalls);
    }

    private static readonly Dictionary<string, string> Nested = new()
    {
        ["Url"] = "https://${Host}:${Port}/",
        ["Host"] = "${Region}.db.internal",
        ["Region"] = "eu",
        ["Port"] = "5432",
        ["Self"] = "a${Self}",
        ["Ping"] = "${Pong}",
        ["Pong"] = "${Ping}",
        ["Escaped"] = "$${Region}",
        ["Missing"] = "[${ Nope }]",
        ["Shrinks"] = "${Region}!",
    };

    private static readonly string[] NestedKeys = [.. Nested.Values];

    private static ReadingResolver CreateNested(int depth)
    {
        return new ReadingResolver(new DefaultToken(), new DictionaryValueProvider(Nested)) { MaxResolutionDepth = depth, AlsoAllowed = NestedKeys };
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
