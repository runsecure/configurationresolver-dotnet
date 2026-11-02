using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using ConfigurationResolver.Buffers;
using ConfigurationResolver.Exceptions;
using ConfigurationResolver.Interfaces;
using ConfigurationResolver.Providers;
using ConfigurationResolver.Tokens;

namespace ConfigurationResolver.IntegrationTests;

/// <summary>
/// Exercises resolving templates into a <see cref="SecretBuffer{T}"/>, and checks that the buffer's memory
/// is wiped whenever its contents are no longer needed.
/// </summary>
public sealed class SecretBufferScenarioTests
{
    private const string Template = "Password=${Database:Password};Host=${Database:Host}";
    private const string Resolved = "Password=s3cr3t;Host=db.internal";

    private static readonly DictionaryValueProvider Values = new(new Dictionary<string, string>
    {
        ["Database:Password"] = "s3cr3t",
        ["Database:Host"] = "db.internal",
        ["Short"] = "ab",
    });

    private readonly IConfigurationResolver _resolver =
        TemplateFile.CreateResolver(new DefaultToken(), Values, Template + "${Short}${Missing}");

    [Fact]
    public void Chars_AreResolvedIntoTheBuffer()
    {
        using var buffer = new SecretBuffer<char>(64);

        var written = _resolver.Resolve(Template, buffer);

        Assert.Equal(Resolved.Length, written);
        Assert.Equal(written, buffer.Length);
        Assert.Equal(64, buffer.Capacity);
        Assert.Equal(Resolved, buffer.Span.ToString());
    }

    [Fact]
    public void Bytes_AreResolvedIntoTheBuffer()
    {
        using var buffer = new SecretBuffer<byte>(64);

        var written = _resolver.Resolve(Encoding.UTF8.GetBytes(Template), buffer);

        Assert.Equal(Encoding.UTF8.GetByteCount(Resolved), written);
        Assert.Equal(Resolved, Encoding.UTF8.GetString(buffer.Span));
    }

    [Fact]
    public void EmptyTemplate_FitsAZeroCapacityBuffer()
    {
        using var buffer = new SecretBuffer<char>(0);

        Assert.Equal(0, _resolver.Resolve("", buffer));
        Assert.True(buffer.Span.IsEmpty);
    }

    [Fact]
    public void ReusingTheBuffer_LeavesNothingOfTheEarlierValue()
    {
        using var buffer = new SecretBuffer<char>(64);
        _resolver.Resolve(Template, buffer);

        _resolver.Resolve("${Short}", buffer);

        Assert.Equal("ab", buffer.Span.ToString());
        Assert.All(Contents(buffer)[2..], c => Assert.Equal('\0', c));
    }

    [Fact]
    public void FailedResolve_WipesTheBuffer()
    {
        using var chars = new SecretBuffer<char>(64);
        using var bytes = new SecretBuffer<byte>(64);
        _resolver.Resolve(Template, chars);
        _resolver.Resolve(Encoding.UTF8.GetBytes(Template), bytes);

        Assert.Throws<ConfigurationKeyException>(() => _resolver.Resolve(Template + "${Missing}", chars));
        Assert.Throws<ConfigurationKeyException>(() => _resolver.Resolve(Encoding.UTF8.GetBytes(Template + "${Missing}"), bytes));

        AssertWiped(chars);
        AssertWiped(bytes);
    }

    [Fact]
    public void ResolverThatDoesNotWipe_IsWipedByTheBuffer()
    {
        var resolver = new FakeResolver("secret", length: null);
        using var chars = new SecretBuffer<char>(16);
        using var bytes = new SecretBuffer<byte>(16);

        Assert.Throws<InvalidOperationException>(() => resolver.Resolve("x", chars));
        Assert.Throws<InvalidOperationException>(() => resolver.Resolve("x"u8, bytes));

        AssertWiped(chars);
        AssertWiped(bytes);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(17)]
    public void ResolverReportingAnImpossibleLength_IsRefusedAndWiped(int length)
    {
        var resolver = new FakeResolver("secret", length);
        using var chars = new SecretBuffer<char>(16);
        using var bytes = new SecretBuffer<byte>(16);

        Assert.Throws<InvalidOperationException>(() => resolver.Resolve("x", chars));
        Assert.Throws<InvalidOperationException>(() => resolver.Resolve("x"u8, bytes));

        AssertWiped(chars);
        AssertWiped(bytes);
    }

    [Fact]
    public void ResolverReceivesAWipedBuffer()
    {
        using var buffer = new SecretBuffer<char>(64);
        _resolver.Resolve(Template, buffer);
        var resolver = new FakeResolver("", length: 0) { ExpectWiped = true };

        Assert.Equal(0, resolver.Resolve("x", buffer));
    }

    [Fact]
    public void Clear_WipesTheBuffer()
    {
        using var buffer = new SecretBuffer<char>(64);
        _resolver.Resolve(Template, buffer);

        buffer.Clear();

        AssertWiped(buffer);
    }

    [Fact]
    public void Dispose_WipesTheBufferAndPreventsFurtherUse()
    {
        var buffer = new SecretBuffer<char>(64);
        _resolver.Resolve(Template, buffer);
        var contents = Contents(buffer);

        buffer.Dispose();
        buffer.Dispose();

        Assert.All(contents, c => Assert.Equal('\0', c));
        Assert.Throws<ObjectDisposedException>(() => buffer.Span.Length);
        Assert.Throws<ObjectDisposedException>(() => buffer.Length);
        Assert.Throws<ObjectDisposedException>(buffer.Clear);
        Assert.Throws<ObjectDisposedException>(() => _resolver.Resolve(Template, buffer));
        Assert.Equal("SecretBuffer<Char>(disposed)", buffer.ToString());
    }

    [Fact]
    public void UndisposedBuffer_IsWipedByTheFinalizer()
    {
        var contents = FillAndAbandon(_resolver);

        GC.Collect();
        GC.WaitForPendingFinalizers();

        Assert.All(contents, c => Assert.Equal('\0', c));
    }

    [Fact]
    public void ToString_DoesNotRevealTheContents()
    {
        using var buffer = new SecretBuffer<char>(64);
        _resolver.Resolve(Template, buffer);

        Assert.Equal($"SecretBuffer<Char>(Length = {Resolved.Length}, Capacity = 64)", buffer.ToString());
    }

    [Fact]
    public void InvalidArguments_Throw()
    {
        using var chars = new SecretBuffer<char>(4);
        using var bytes = new SecretBuffer<byte>(4);

        Assert.Throws<ArgumentOutOfRangeException>(() => new SecretBuffer<char>(-1));
        Assert.Throws<ArgumentNullException>(() => SecretBufferExtensions.Resolve(null!, "x", chars));
        Assert.Throws<ArgumentNullException>(() => SecretBufferExtensions.Resolve(null!, "x"u8, bytes));
        Assert.Throws<ArgumentNullException>(() => _resolver.Resolve("x", (SecretBuffer<char>)null!));
        Assert.Throws<ArgumentNullException>(() => _resolver.Resolve("x"u8, (SecretBuffer<byte>)null!));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static char[] FillAndAbandon(IConfigurationResolver resolver)
    {
        var buffer = new SecretBuffer<char>(64);
        resolver.Resolve(Template, buffer);
        return Contents(buffer);
    }

    private static T[] Contents<T>(SecretBuffer<T> buffer)
        where T : unmanaged
    {
        var field = typeof(SecretBuffer<T>).GetField("_buffer", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (T[])field.GetValue(buffer)!;
    }

    private static void AssertWiped<T>(SecretBuffer<T> buffer)
        where T : unmanaged, IEquatable<T>
    {
        Assert.Equal(0, buffer.Length);
        Assert.All(Contents(buffer), item => Assert.Equal(default, item));
    }

    /// <summary>
    /// A resolver that writes a value without wiping it, then either throws or reports the given length.
    /// </summary>
    private sealed class FakeResolver(string value, int? length) : IConfigurationResolver
    {
        public bool ExpectWiped { get; init; }

        public int Resolve(ReadOnlySpan<char> source, Span<char> destination)
        {
            CheckWiped(destination);
            value.AsSpan().CopyTo(destination);
            return length ?? throw new InvalidOperationException("resolver failed");
        }

        public int Resolve(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            CheckWiped(destination);
            Encoding.UTF8.GetBytes(value, destination);
            return length ?? throw new InvalidOperationException("resolver failed");
        }

        private void CheckWiped<T>(Span<T> destination)
            where T : unmanaged, IEquatable<T>
        {
            if (ExpectWiped)
            {
                Assert.All(destination.ToArray(), item => Assert.Equal(default, item));
            }
        }
    }
}
