using ConfigurationResolver.Builders;
using ConfigurationResolver.Interfaces;
using ConfigurationResolver.Providers;
using ConfigurationResolver.Readers;
using ConfigurationResolver.Resolvers;
using ConfigurationResolver.Tokens;

namespace ConfigurationResolver.Tests;

public class ConfigurationResolverBuilderTests
{
    private readonly DictionaryValueProvider _high = new(new Dictionary<string, string> { ["Name"] = "Grace" });
    private readonly DictionaryValueProvider _low = new(new Dictionary<string, string> { ["Name"] = "Ada", ["Lang"] = "${Name}" });

    [Fact]
    public void Build_UsesTheReaderAndProvidersInTheOrderAdded()
    {
        var reader = new ConfigurationItemReader(new DefaultToken());
        reader.ReadKeys("${Name} ${Lang}");

        var resolver = new ConfigurationResolverBuilder()
            .WithReader(reader)
            .AddValueProvider(_high)
            .AddValueProvider(_low)
            .Build();

        Assert.IsType<ConfigurationItemResolver>(resolver);
        Assert.Equal("Grace ${Name} $${Name}", Resolve(resolver, "${Name} ${Lang} $$${Name}"));
    }

    [Fact]
    public void Build_DefaultsToTheDefaultTokenAndOptions()
    {
        var resolver = (ConfigurationItemResolver)new ConfigurationResolverBuilder()
            .WithReader(new ConfigurationItemReader(new DefaultToken()))
            .AddValueProvider(_low)
            .Build();

        Assert.False(resolver.IgnoreUnresolvablePlaceholders);
        Assert.Equal(1, resolver.MaxResolutionDepth);
        Assert.Equal("${Name}", Resolve(resolver, "$${Name}"));
    }

    [Fact]
    public void Build_AppliesTheTokenAndOptions()
    {
        var token = new CustomToken("{{", "}}");
        var reader = new ConfigurationItemReader(token);
        reader.ReadKeys("{{Lang}} {{Name}} {{Missing}}");

        var resolver = (ConfigurationItemResolver)new ConfigurationResolverBuilder()
            .WithToken(token)
            .WithReader(reader)
            .AddValueProvider(new DictionaryValueProvider(new Dictionary<string, string> { ["Lang"] = "{{Name}}", ["Name"] = "Ada" }))
            .IgnoreUnresolvablePlaceholders()
            .WithMaxResolutionDepth(2)
            .Build();

        Assert.True(resolver.IgnoreUnresolvablePlaceholders);
        Assert.Equal(2, resolver.MaxResolutionDepth);
        Assert.Equal("Ada {{Missing}} ${Lang}", Resolve(resolver, "{{Lang}} {{Missing}} ${Lang}"));
    }

    [Fact]
    public void Build_EachCallReturnsANewResolver()
    {
        var builder = new ConfigurationResolverBuilder()
            .WithReader(new ConfigurationItemReader(new DefaultToken()))
            .AddValueProvider(_low);

        Assert.NotSame(builder.Build(), builder.Build());
    }

    [Fact]
    public void Build_WithoutAReaderOrAProvider_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => new ConfigurationResolverBuilder().AddValueProvider(_low).Build());
        Assert.Throws<InvalidOperationException>(
            () => new ConfigurationResolverBuilder().WithReader(new ConfigurationItemReader(new DefaultToken())).Build());
    }

    [Fact]
    public void InvalidArguments_Throw()
    {
        var builder = new ConfigurationResolverBuilder();

        Assert.Throws<ArgumentNullException>(() => builder.WithToken(null!));
        Assert.Throws<ArgumentNullException>(() => builder.WithReader(null!));
        Assert.Throws<ArgumentNullException>(() => builder.AddValueProvider(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithMaxResolutionDepth(0));
    }

    private static string Resolve(IConfigurationResolver resolver, string source)
    {
        var destination = new char[256];
        var written = resolver.Resolve(source, destination);
        return new string(destination, 0, written);
    }
}
