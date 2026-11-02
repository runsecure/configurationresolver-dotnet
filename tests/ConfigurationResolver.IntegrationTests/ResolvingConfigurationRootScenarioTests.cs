using ConfigurationResolver.Builders;
using ConfigurationResolver.Configuration;
using ConfigurationResolver.Exceptions;
using ConfigurationResolver.Interfaces;
using ConfigurationResolver.Providers;
using ConfigurationResolver.Readers;
using ConfigurationResolver.Resolvers;
using ConfigurationResolver.Tokens;
using Microsoft.Extensions.Configuration;

namespace ConfigurationResolver.IntegrationTests;

/// <summary>
/// Loads JSON configuration whose values contain configuration items, and reads it through a
/// <see cref="ResolvingConfigurationRoot"/> that fills them in from secret files and the configuration itself.
/// </summary>
public sealed class ResolvingConfigurationRootScenarioTests : IDisposable
{
    private const string Settings = """
        {
          "Database": { "Host": "db.internal", "Port": "5432", "Name": "orders" },
          "ConnectionStrings": { "Default": "Host=${Database:Host};Port=${Database:Port};Password=${Database:Password}" },
          "Api": { "Url": "https://${Api:Host}/v1", "Host": "api.${Region}" },
          "Region": "eu",
          "Literal": "$${Database:Host}",
          "Plain": "no items"
        }
        """;

    private readonly TempDirectory _directory = new();

    public void Dispose()
    {
        _directory.Dispose();
    }

    [Fact]
    public void Values_AreResolvedFromSecretFilesAndTheConfiguration()
    {
        var (root, _, _) = Arrange();

        Assert.Equal("Host=db.internal;Port=5432;Password=s3cret", root["ConnectionStrings:Default"]);
        Assert.Equal("api.eu", root["api:host"]);
        Assert.Equal("${Database:Host}", root["Literal"]);
        Assert.Equal("no items", root["Plain"]);
        Assert.Null(root["Missing"]);
        Assert.Null(root["Database"]);
    }

    [Theory]
    [InlineData(1, "https://api.${Region}/v1")]
    [InlineData(2, "https://api.eu/v1")]
    public void ItemsInValues_AreResolvedUpToTheResolversDepth(int depth, string expected)
    {
        var (root, _, _) = Arrange(depth);

        Assert.Equal(expected, root["Api:Url"]);
    }

    [Fact]
    public void SectionsAndChildren_ResolveTheirValues()
    {
        var (root, _, _) = Arrange(depth: 2);

        var api = root.GetSection("Api");
        var values = root.AsEnumerable().ToDictionary(pair => pair.Key, pair => pair.Value);

        Assert.Equal("https://api.eu/v1", api["Url"]);
        Assert.Equal("api.eu", api.GetSection("Host").Value);
        Assert.Equal(["Host", "Url"], api.GetChildren().Select(child => child.Key).Order());
        Assert.All(api.GetChildren(), child => Assert.DoesNotContain("${", child.Value));
        Assert.Contains("ConnectionStrings", root.GetChildren().Select(child => child.Key));
        Assert.Equal("Host=db.internal;Port=5432;Password=s3cret", values["ConnectionStrings:Default"]);
        Assert.Equal("${Database:Host}", values["Literal"]);
    }

    [Fact]
    public void Reload_ResolvesKeysThatAreNewInTheReloadedValues()
    {
        var (root, _, settingsPath) = Arrange();
        File.WriteAllText(settingsPath, """{ "Region": "${Fallback:Region}", "Fallback": { "Region": "us" } }""");

        root.Reload();

        Assert.Equal("us", root["Region"]);
    }

    [Fact]
    public void SettingAValue_StoresItUnresolvedAndReadsItResolved()
    {
        var (root, inner, _) = Arrange();

        root["Extra"] = "${Region}-${Database:Name}";

        Assert.Equal("${Region}-${Database:Name}", inner["Extra"]);
        Assert.Equal("eu-orders", root["Extra"]);
    }

    [Fact]
    public void ValueFromASecretFile_CantReferToAKeyTheConfigurationNeverDoes()
    {
        var (root, _, _) = Arrange(depth: 2, password: "${Database:Name}");

        var exception = Assert.Throws<ConfigurationKeyException>(() => root["ConnectionStrings:Default"]);

        Assert.Equal("Database:Name", exception.Key);
        Assert.Equal(ConfigurationKeyError.NotAllowed, exception.Error);
    }

    [Fact]
    public void UnresolvableItems_ThrowUnlessIgnored()
    {
        var (strict, _, _) = Arrange();
        var (lenient, _, _) = Arrange(ignoreUnresolvable: true);
        strict["Optional"] = "${Feature:Flag}";
        lenient["Optional"] = "x ${Feature:Flag}";

        var exception = Assert.Throws<ConfigurationKeyException>(() => strict["Optional"]);

        Assert.Equal(ConfigurationKeyError.NotFound, exception.Error);
        Assert.Equal("x ${Feature:Flag}", lenient["Optional"]);
    }

    [Fact]
    public void ValueThatLeadsBackToItself_ThrowsCycle()
    {
        var (root, _, _) = Arrange(depth: 5);
        root["Ping"] = "${Pong}";
        root["Pong"] = "${Ping}";

        var exception = Assert.Throws<ConfigurationKeyException>(() => root["Ping"]);

        Assert.Equal(ConfigurationKeyError.Cycle, exception.Error);
    }

    [Fact]
    public void ValueMuchLongerOnceResolved_IsReturnedWhole()
    {
        var long1 = new string('x', 1000);
        var inner = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Long"] = long1, ["Ref"] = "${Long}${Long}${Long}" })
            .Build();
        var reader = new ConfigurationItemReader(new DefaultToken());
        var resolver = new ConfigurationItemResolver(new DefaultToken(), reader, new ConfigurationValueProvider(inner));
        var root = new ResolvingConfigurationRoot(inner, reader, resolver);

        Assert.Equal(long1 + long1 + long1, root["Ref"]);
    }

    [Fact]
    public void ProvidersAndReloadToken_AreThoseOfTheInnerConfiguration()
    {
        var (root, inner, _) = Arrange();

        Assert.Equal(inner.Providers, root.Providers);
        Assert.NotNull(root.GetReloadToken());
    }

    [Fact]
    public void NullArguments_Throw()
    {
        var inner = new ConfigurationBuilder().Build();
        var reader = new ConfigurationItemReader(new DefaultToken());
        var resolver = new ConfigurationItemResolver(new DefaultToken(), reader, new ConfigurationValueProvider(inner));

        Assert.Throws<ArgumentNullException>(() => new ResolvingConfigurationRoot(null!, reader, resolver));
        Assert.Throws<ArgumentNullException>(() => new ResolvingConfigurationRoot(inner, null!, resolver));
        Assert.Throws<ArgumentNullException>(() => new ResolvingConfigurationRoot(inner, reader, null!));
    }

    private (ResolvingConfigurationRoot Root, IConfigurationRoot Inner, string SettingsPath) Arrange(
        int depth = 1,
        bool ignoreUnresolvable = false,
        string password = "s3cret")
    {
        var settingsPath = _directory.WriteText($"appsettings.{Guid.NewGuid():N}.json", Settings);
        var secrets = Path.Combine(_directory.Path, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(secrets);
        File.WriteAllText(Path.Combine(secrets, "Database__Password"), password);

        var inner = new ConfigurationBuilder().AddJsonFile(settingsPath).Build();
        var token = new DefaultToken();
        var reader = new ConfigurationItemReader(token);
        IConfigurationResolver resolver = new ConfigurationResolverBuilder()
            .WithToken(token)
            .WithReader(reader)
            .AddValueProvider(new FileSecretValueProvider(secrets))
            .AddValueProvider(new ConfigurationValueProvider(inner))
            .WithMaxResolutionDepth(depth)
            .IgnoreUnresolvablePlaceholders(ignoreUnresolvable)
            .Build();

        return (new ResolvingConfigurationRoot(inner, reader, resolver), inner, settingsPath);
    }
}
