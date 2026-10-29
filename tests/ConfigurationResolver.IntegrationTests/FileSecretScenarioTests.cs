using System.Text;
using ConfigurationResolver.Interfaces;
using ConfigurationResolver.Providers;

namespace ConfigurationResolver.IntegrationTests;

/// <summary>
/// Exercises <see cref="FileSecretValueProvider"/> against a secrets directory laid out like a container
/// secret volume.
/// </summary>
public sealed class FileSecretScenarioTests : IDisposable
{
    private readonly TempDirectory _directory = new();
    private readonly FileSecretValueProvider _provider;

    public FileSecretScenarioTests()
    {
        _directory.WriteText("Database__Password", "café");
        _directory.WriteText("WithBom", "bom!", byteOrderMark: true);
        _directory.WriteText("Empty", "");
        _directory.WriteBytes("Short", "ab"u8.ToArray());
        _directory.WriteText("TrailingNewline", "value\n");
        Directory.CreateDirectory(Path.Combine(_directory.Path, "Folder"));

        _provider = new FileSecretValueProvider(_directory.Path);
    }

    public void Dispose() => _directory.Dispose();

    [Theory]
    [InlineData("Database:Password", "café")]
    [InlineData("Database__Password", "café")]
    [InlineData("WithBom", "bom!")]
    [InlineData("Empty", "")]
    [InlineData("Short", "ab")]
    [InlineData("TrailingNewline", "value\n")]
    public void ExistingSecret_IsReadThroughEveryMethod(string key, string expected)
    {
        Span<char> chars = stackalloc char[32];
        Span<byte> bytes = stackalloc byte[32];

        Assert.Equal(ValueStatus.Found, _provider.TryGetValue(key, out var value));
        Assert.Equal(expected, value);

        Assert.Equal(ValueStatus.Found, _provider.TryGetChar(key, chars, out var charsWritten));
        Assert.Equal(expected, chars[..charsWritten].ToString());

        Assert.Equal(ValueStatus.Found, _provider.TryGetBytes(key, bytes, out var bytesWritten));
        Assert.Equal(expected, Encoding.UTF8.GetString(bytes[..bytesWritten]));
    }

    [Fact]
    public void LargeSecret_IsReadThroughEveryMethod()
    {
        var expected = string.Concat(Enumerable.Repeat("0123456789abcdé", 100));
        _directory.WriteText("Certificate", expected);
        var chars = new char[expected.Length];
        var bytes = new byte[Encoding.UTF8.GetByteCount(expected)];

        Assert.Equal(ValueStatus.Found, _provider.TryGetValue("Certificate", out var value));
        Assert.Equal(ValueStatus.Found, _provider.TryGetChar("Certificate", chars, out var charsWritten));
        Assert.Equal(ValueStatus.Found, _provider.TryGetBytes("Certificate", bytes, out var bytesWritten));

        Assert.Equal(expected, value);
        Assert.Equal(expected, new string(chars, 0, charsWritten));
        Assert.Equal(bytes.Length, bytesWritten);
    }

    [Fact]
    public void SecretThatExactlyFillsTheBuffer_IsRead()
    {
        Span<char> chars = stackalloc char[4];
        Span<byte> bytes = stackalloc byte[5];

        Assert.Equal(ValueStatus.Found, _provider.TryGetChar("Database:Password", chars, out var charsWritten));
        Assert.Equal(ValueStatus.Found, _provider.TryGetBytes("Database:Password", bytes, out var bytesWritten));

        Assert.Equal(4, charsWritten);
        Assert.Equal(5, bytesWritten);
    }

    [Fact]
    public void ByteOrderMark_DoesNotCountTowardsTheBufferSize()
    {
        Span<byte> bytes = stackalloc byte[4];

        Assert.Equal(ValueStatus.Found, _provider.TryGetBytes("WithBom", bytes, out var bytesWritten));
        Assert.Equal("bom!", Encoding.UTF8.GetString(bytes[..bytesWritten]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void CharBufferTooSmall_ReportsDestinationTooSmallAndClearsTheBuffer(int length)
    {
        var buffer = new char[length];
        Array.Fill(buffer, '#');

        Assert.Equal(ValueStatus.DestinationTooSmall, _provider.TryGetChar("Database:Password", buffer, out var charsWritten));

        Assert.Equal(0, charsWritten);
        Assert.All(buffer, c => Assert.Equal('\0', c));
    }

    [Fact]
    public void ByteBufferTooSmall_ReportsDestinationTooSmallWithoutWriting()
    {
        var buffer = new byte[4];
        Array.Fill(buffer, (byte)'#');

        Assert.Equal(ValueStatus.DestinationTooSmall, _provider.TryGetBytes("Database:Password", buffer, out var bytesWritten));

        Assert.Equal(0, bytesWritten);
        Assert.All(buffer, b => Assert.Equal((byte)'#', b));
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../outside")]
    [InlineData("nested/file")]
    [InlineData("null\0char")]
    public void MissingOrInvalidKey_IsNotFound(string key)
    {
        Assert.Equal(ValueStatus.NotFound, _provider.TryGetValue(key, out var value));
        Assert.Null(value);
        Assert.Equal(ValueStatus.NotFound, _provider.TryGetChar(key, new char[32], out var charsWritten));
        Assert.Equal(0, charsWritten);
        Assert.Equal(ValueStatus.NotFound, _provider.TryGetBytes(key, new byte[32], out var bytesWritten));
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void KeyOutsideTheDirectory_DoesNotReadTheFile()
    {
        var parent = Path.GetDirectoryName(_directory.Path)!;
        var outsideName = "outside-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(parent, outsideName), "leaked");
        try
        {
            Assert.Equal(ValueStatus.NotFound, _provider.TryGetValue("../" + outsideName, out _));
            Assert.Equal(ValueStatus.NotFound, _provider.TryGetValue("..\\" + outsideName, out _));
        }
        finally
        {
            File.Delete(Path.Combine(parent, outsideName));
        }
    }

    [Fact]
    public void MissingDirectory_IsNotFound()
    {
        var provider = new FileSecretValueProvider(Path.Combine(_directory.Path, "does-not-exist"));

        Assert.Equal(ValueStatus.NotFound, provider.TryGetValue("Anything", out _));
    }

    [Fact]
    public void KeyNamingADirectory_PropagatesTheAccessError()
    {
        Assert.Throws<UnauthorizedAccessException>(() => _provider.TryGetValue("Folder", out _));
    }

    [Fact]
    public void SecretChangedOnDisk_IsReadOnTheNextLookup()
    {
        _directory.WriteText("Rotating", "first");
        Assert.Equal(ValueStatus.Found, _provider.TryGetValue("Rotating", out var first));

        _directory.WriteText("Rotating", "second");
        Assert.Equal(ValueStatus.Found, _provider.TryGetValue("Rotating", out var second));

        Assert.Equal("first", first);
        Assert.Equal("second", second);
    }

    [Fact]
    public void Directory_IsTheFullPath()
    {
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, _directory.Path);

        Assert.Equal(_directory.Path, new FileSecretValueProvider(relative).Directory);
    }

    [Fact]
    public void Constructor_NullOrEmptyDirectory_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new FileSecretValueProvider(null!));
        Assert.Throws<ArgumentException>(() => new FileSecretValueProvider(""));
    }

    [Fact]
    public void NullKey_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _provider.TryGetValue(null!, out _));
    }
}
