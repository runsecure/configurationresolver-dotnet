using System.Text;
using ConfigurationResolver.Readers;
using ConfigurationResolver.Tokens;

namespace ConfigurationResolver.IntegrationTests;

/// <summary>
/// Exercises <see cref="ConfigurationItemReader"/> and <see cref="ConfigurationItemTokenBase"/>.
/// </summary>
public sealed class ReaderAndTokenScenarioTests
{
    private const string Text = "this is my ${ value 1 }, and this is ${value2} and lastly this is ${value3}";

    private readonly ConfigurationItemReader _reader = new(new DefaultToken());

    [Fact]
    public void ReadKeys_FromCharsBytesAndStream_ReturnTheSameKeys()
    {
        string[] expected = ["value 1", "value2", "value3"];
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Text));

        Assert.Equal(expected, _reader.ReadKeys(Text.AsSpan()));
        Assert.Equal(expected, _reader.ReadKeys(Encoding.UTF8.GetBytes(Text)));
        Assert.Equal(expected, _reader.ReadKeys(stream));
    }

    [Fact]
    public void ReadKeys_StreamWithExplicitEncoding_DecodesWithIt()
    {
        using var stream = new MemoryStream(Encoding.Latin1.GetBytes("${ café }"));

        Assert.Equal(["café"], _reader.ReadKeys(stream, Encoding.Latin1));
    }

    [Fact]
    public void ReadKeys_StreamIsReadFromItsPositionAndLeftOpen()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("${skipped} ${read}"));
        stream.Position = "${skipped} ".Length;

        Assert.Equal(["read"], _reader.ReadKeys(stream));
        Assert.True(stream.CanRead);
    }

    [Theory]
    [InlineData("", new string[0])]
    [InlineData("no items", new string[0])]
    [InlineData("${} ${ } ${a}", new[] { "a" })]
    [InlineData("${a} ${unterminated", new[] { "a" })]
    [InlineData("${a}${b}", new[] { "a", "b" })]
    public void ReadKeys_SkipsEmptyAndUnterminatedItems(string text, string[] expected)
    {
        Assert.Equal(expected, _reader.ReadKeys(text.AsSpan()));
    }

    [Fact]
    public void Keys_ReturnsTheMostRecentRead()
    {
        Assert.Empty(_reader.Keys());

        _reader.ReadKeys("${first}".AsSpan());
        _reader.ReadKeys("${second}".AsSpan());

        Assert.Equal(["second"], _reader.Keys());
    }

    [Fact]
    public void ReadKeys_WithCustomToken_UsesItsSequences()
    {
        var reader = new ConfigurationItemReader(new CustomToken("{{", "}}"));

        Assert.Equal(["a"], reader.ReadKeys("{{a}} ${b}".AsSpan()));
    }

    [Fact]
    public void Reader_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new ConfigurationItemReader(null!));
        Assert.Throws<ArgumentNullException>(() => _reader.ReadKeys((Stream)null!));
    }

    [Fact]
    public void Token_DefaultsAndOverrides()
    {
        var defaults = new DefaultToken();
        var custom = new CustomToken("<%", "%>");

        Assert.Equal(ConfigurationItemTokenBase.DefaultStartSequence, defaults.StartSequence);
        Assert.Equal(ConfigurationItemTokenBase.DefaultEndSequence, defaults.EndSequence);
        Assert.Equal("<%", custom.StartSequence);
        Assert.Equal("%>", custom.EndSequence);
    }

    [Theory]
    [InlineData(null, "}")]
    [InlineData("", "}")]
    [InlineData("${", null)]
    [InlineData("${", "")]
    public void Token_NullOrEmptySequence_Throws(string? start, string? end)
    {
        Assert.ThrowsAny<ArgumentException>(() => new CustomToken(start!, end!));
    }
}
