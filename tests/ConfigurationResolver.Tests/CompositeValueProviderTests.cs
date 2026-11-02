using System.Text;
using ConfigurationResolver.Exceptions;
using ConfigurationResolver.Interfaces;
using ConfigurationResolver.Providers;

namespace ConfigurationResolver.Tests;

public class CompositeValueProviderTests
{
    private readonly SpyValueProvider _high = new(new Dictionary<string, string> { ["Shared"] = "high", ["HighOnly"] = "h" });
    private readonly SpyValueProvider _low = new(new Dictionary<string, string> { ["Shared"] = "low", ["LowOnly"] = "lö" });

    [Fact]
    public void TryGetValue_ReturnsTheValueFromTheFirstProviderThatHasTheKey()
    {
        var provider = new CompositeValueProvider(_high, _low);

        Assert.Equal(ValueStatus.Found, provider.TryGetValue("Shared", out var shared));
        Assert.Equal(ValueStatus.Found, provider.TryGetValue("LowOnly", out var lowOnly));
        Assert.Equal(ValueStatus.NotFound, provider.TryGetValue("Missing", out var missing));

        Assert.Equal("high", shared);
        Assert.Equal("lö", lowOnly);
        Assert.Null(missing);
        Assert.Equal(["Shared", "LowOnly", "Missing"], _high.TryGetValueCalls);
        Assert.Equal(["LowOnly", "Missing"], _low.TryGetValueCalls);
    }

    [Fact]
    public void TryGetChar_WritesTheValueFromTheFirstProviderThatHasTheKey()
    {
        var provider = new CompositeValueProvider(_high, _low);
        Span<char> destination = stackalloc char[8];

        Assert.Equal(ValueStatus.Found, provider.TryGetChar("Shared", destination, out var sharedWritten));
        Assert.Equal("high", destination[..sharedWritten].ToString());
        Assert.Equal(ValueStatus.Found, provider.TryGetChar("LowOnly", destination, out var lowWritten));
        Assert.Equal("lö", destination[..lowWritten].ToString());
        Assert.Equal(ValueStatus.NotFound, provider.TryGetChar("Missing", destination, out var missingWritten));
        Assert.Equal(0, missingWritten);
        Assert.Equal(["LowOnly", "Missing"], _low.TryGetCharCalls);
    }

    [Fact]
    public void TryGetBytes_WritesTheValueFromTheFirstProviderThatHasTheKey()
    {
        var provider = new CompositeValueProvider(_high, _low);
        Span<byte> destination = stackalloc byte[8];

        Assert.Equal(ValueStatus.Found, provider.TryGetBytes("Shared", destination, out var sharedWritten));
        Assert.Equal("high", Encoding.UTF8.GetString(destination[..sharedWritten]));
        Assert.Equal(ValueStatus.Found, provider.TryGetBytes("LowOnly", destination, out var lowWritten));
        Assert.Equal("lö", Encoding.UTF8.GetString(destination[..lowWritten]));
        Assert.Equal(ValueStatus.NotFound, provider.TryGetBytes("Missing", destination, out var missingWritten));
        Assert.Equal(0, missingWritten);
        Assert.Equal(["LowOnly", "Missing"], _low.TryGetBytesCalls);
    }

    [Fact]
    public void DestinationTooSmallInTheFirstProvider_DoesNotFallThrough()
    {
        var provider = new CompositeValueProvider(_high, _low);

        Assert.Equal(ValueStatus.DestinationTooSmall, provider.TryGetChar("Shared", stackalloc char[2], out var charsWritten));
        Assert.Equal(ValueStatus.DestinationTooSmall, provider.TryGetBytes("Shared", stackalloc byte[2], out var bytesWritten));

        Assert.Equal(0, charsWritten + bytesWritten);
        Assert.Empty(_low.TryGetCharCalls);
        Assert.Empty(_low.TryGetBytesCalls);
    }

    [Fact]
    public void ProviderException_Propagates()
    {
        var failing = new SpyValueProvider(new Dictionary<string, string>())
        {
            ThrowOnLookup = new ConfigurationKeyException("Shared", ConfigurationKeyError.ValueUnstable),
        };
        var provider = new CompositeValueProvider(failing, _low);

        Assert.Throws<ConfigurationKeyException>(() => provider.TryGetChar("Shared", new char[8], out _));
        Assert.Throws<ConfigurationKeyException>(() => provider.TryGetBytes("Shared", new byte[8], out _));
        Assert.Empty(_low.TryGetCharCalls);
        Assert.Empty(_low.TryGetBytesCalls);
    }

    [Fact]
    public void Providers_AreInPriorityOrderAndCopied()
    {
        var list = new List<IValueProvider> { _high, _low };
        var provider = new CompositeValueProvider(list);

        list.Clear();

        Assert.Equal([_high, _low], provider.Providers);
    }

    [Fact]
    public void Constructor_InvalidProviders_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CompositeValueProvider((IEnumerable<IValueProvider>)null!));
        Assert.Throws<ArgumentException>(() => new CompositeValueProvider());
        Assert.Throws<ArgumentException>(() => new CompositeValueProvider(_high, null!));
    }
}
