using System.Text;
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
/// Writes JSON configuration templates to disk, supplies their values from each kind of provider, and
/// loads the resolved JSON with Microsoft.Extensions.Configuration.
/// </summary>
public sealed class JsonConfigurationScenarioTests : IDisposable
{
    private const string Template = """
        {
          "ConnectionStrings": {
            "Default": "Host=${Database:Host};Port=${ Database:Port };Password=${Database:Password}"
          },
          "Greeting": "${Greeting} wörld",
          "Literal": "${} and $ {not an item}"
        }
        """;

    private readonly TempDirectory _directory = new();
    private readonly string _environmentPrefix = "CRIT_" + Guid.NewGuid().ToString("N");

    public void Dispose()
    {
        foreach (var name in new[] { "Database__Host", "Database__Port", "Database__Password", "Greeting" })
        {
            Environment.SetEnvironmentVariable($"{_environmentPrefix}__{name}", null);
        }

        _directory.Dispose();
    }

    public static TheoryData<string> Providers => new() { "json", "dictionary", "environment", "secrets" };

    [Theory]
    [MemberData(nameof(Providers))]
    public void ResolvedCharTemplate_LoadsAsConfiguration(string providerName)
    {
        var (resolver, templatePath) = Arrange(providerName);

        var configuration = TemplateFile.LoadJson(TemplateFile.ResolveChars(resolver, templatePath));

        AssertResolved(configuration);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void ResolvedByteTemplate_LoadsAsConfiguration(string providerName)
    {
        var (resolver, templatePath) = Arrange(providerName);

        var configuration = TemplateFile.LoadJson(TemplateFile.ResolveBytes(resolver, templatePath));

        AssertResolved(configuration);
    }

    [Fact]
    public void ReaderListsTheKeysInTheTemplateFile()
    {
        var (_, templatePath) = Arrange("json");
        var reader = new ConfigurationItemReader(new DefaultToken());
        string[] expected = ["Database:Host", "Database:Port", "Database:Password", "Greeting"];

        using (var stream = File.OpenRead(templatePath))
        {
            Assert.Equal(expected, reader.ReadKeys(stream));
        }

        Assert.Equal(expected, reader.Keys());
        Assert.Equal(expected, reader.ReadKeys(File.ReadAllBytes(templatePath)));
    }

    [Fact]
    public void SectionProvider_ResolvesKeysRelativeToTheSection()
    {
        var values = new ConfigurationBuilder().AddJsonFile(_directory.WriteText("values.json", ValuesJson())).Build();
        var templatePath = _directory.WriteText("section.template.json", """{ "Db": "${Host}:${Port}" }""");
        var resolver = CreateResolver(new DefaultToken(), new ConfigurationValueProvider(values.GetSection("Database")), templatePath);

        var configuration = TemplateFile.LoadJson(TemplateFile.ResolveBytes(resolver, templatePath));

        Assert.Equal("db.internal:5432", configuration["Db"]);
    }

    [Fact]
    public void CustomToken_ResolvesOnlyItsOwnItems()
    {
        var templatePath = _directory.WriteText("custom.template.json", """{ "Host": "{{Database:Host}}", "Kept": "${Database:Host}" }""");
        var resolver = CreateResolver(new CustomToken("{{", "}}"), CreateProvider("dictionary"), templatePath);

        var configuration = TemplateFile.LoadJson(TemplateFile.ResolveChars(resolver, templatePath));

        Assert.Equal("db.internal", configuration["Host"]);
        Assert.Equal("${Database:Host}", configuration["Kept"]);
    }

    [Fact]
    public void TemplateWithByteOrderMark_ResolvesAndLoads()
    {
        var templatePath = _directory.WriteText("bom.template.json", """{ "Host": "${Database:Host}" }""", byteOrderMark: true);
        var resolver = CreateResolver(new DefaultToken(), CreateProvider("dictionary"), templatePath);

        var configuration = TemplateFile.LoadJson(TemplateFile.ResolveBytes(resolver, templatePath));

        Assert.Equal("db.internal", configuration["Host"]);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TemplateWithMissingValue_Throws(string providerName)
    {
        var templatePath = _directory.WriteText("missing.template.json", """{ "Value": "${Database:Missing}" }""");
        var resolver = CreateResolver(new DefaultToken(), CreateProvider(providerName), templatePath);

        var exception = Assert.Throws<ConfigurationKeyException>(() => TemplateFile.ResolveChars(resolver, templatePath));
        Assert.Throws<ConfigurationKeyException>(() => TemplateFile.ResolveBytes(resolver, templatePath));

        Assert.Equal("Database:Missing", exception.Key);
        Assert.Equal(ConfigurationKeyError.NotFound, exception.Error);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void DestinationTooSmallForAValue_ThrowsAndClearsTheSecret(string providerName)
    {
        var template = "${Database:Password}${Database:Password}";
        var resolver = TemplateFile.CreateResolver(new DefaultToken(), CreateProvider(providerName), template);
        var chars = new char["s3cr\"t!".Length + 3];
        var bytes = new byte[chars.Length];

        Assert.Throws<ArgumentException>(() => resolver.Resolve(template, chars));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(Encoding.UTF8.GetBytes(template), bytes));

        Assert.All(chars, c => Assert.Equal('\0', c));
        Assert.All(bytes, b => Assert.Equal(0, b));
    }

    [Fact]
    public void DestinationTooSmallForTheTemplateText_ThrowsAndClearsTheDestination()
    {
        var template = File.ReadAllText(_directory.WriteText("small.template.json", Template));
        var resolver = TemplateFile.CreateResolver(new DefaultToken(), CreateProvider("dictionary"), template);
        var chars = new char[template.IndexOf("${", StringComparison.Ordinal) - 1];
        var bytes = new byte[chars.Length];

        Assert.Throws<ArgumentException>(() => resolver.Resolve(template, chars));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(Encoding.UTF8.GetBytes(template), bytes));

        Assert.All(chars, c => Assert.Equal('\0', c));
        Assert.All(bytes, b => Assert.Equal(0, b));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TemplateUsingAKeyOutsideTheTrustedTemplate_IsRejected(string providerName)
    {
        // The trusted template only asks for the host; a substituted template also asks for the password.
        var trustedPath = _directory.WriteText("trusted.template.json", """{ "Host": "${Database:Host}" }""");
        var injectedPath = _directory.WriteText("injected.template.json", """{ "Host": "${Database:Host}", "Leak": "${Database:Password}" }""");
        var resolver = CreateResolver(new DefaultToken(), CreateProvider(providerName), trustedPath);

        Assert.Equal("db.internal", TemplateFile.LoadJson(TemplateFile.ResolveBytes(resolver, trustedPath))["Host"]);

        var charException = Assert.Throws<ConfigurationKeyException>(() => TemplateFile.ResolveChars(resolver, injectedPath));
        var byteException = Assert.Throws<ConfigurationKeyException>(() => TemplateFile.ResolveBytes(resolver, injectedPath));

        var leakIndex = File.ReadAllText(injectedPath).IndexOf("${Database:Password}", StringComparison.Ordinal);
        foreach (var exception in new[] { charException, byteException })
        {
            Assert.Equal("Database:Password", exception.Key);
            Assert.Equal(ConfigurationKeyError.NotAllowed, exception.Error);
            Assert.Equal(leakIndex, exception.SourceIndex);
            Assert.DoesNotContain("Database:Password", exception.Message);
        }
    }

    [Fact]
    public void SecretFilesThenConfiguration_TheFirstProviderWithTheKeyWins()
    {
        // The password and an overriding host come from secret files; everything else from configuration.
        var secrets = Path.Combine(_directory.Path, "secrets");
        Directory.CreateDirectory(secrets);
        File.WriteAllText(Path.Combine(secrets, "Database__Password"), "s3cr\\\"t!");
        File.WriteAllText(Path.Combine(secrets, "Database__Host"), "db.secret");
        var settings = new ConfigurationBuilder().AddJsonFile(_directory.WriteText("values.json", ValuesJson())).Build();
        var templatePath = _directory.WriteText("appsettings.template.json", Template);
        var reader = new ConfigurationItemReader(new DefaultToken());
        reader.ReadKeys(File.ReadAllBytes(templatePath));
        var resolver = new ConfigurationItemResolver(
            new DefaultToken(), reader, new FileSecretValueProvider(secrets), new ConfigurationValueProvider(settings));

        var configuration = TemplateFile.LoadJson(TemplateFile.ResolveBytes(resolver, templatePath));

        Assert.Equal("Host=db.secret;Port=5432;Password=s3cr\"t!", configuration["ConnectionStrings:Default"]);
        Assert.Equal("héllo wörld", configuration["Greeting"]);
    }

    [Fact]
    public void IgnoreUnresolvablePlaceholders_LeavesMissingItemsInTheLoadedConfiguration()
    {
        var templatePath = _directory.WriteText("optional.template.json", """{ "Host": "${Database:Host}", "Optional": "${ Feature:Flag }" }""");
        var reader = new ConfigurationItemReader(new DefaultToken());
        reader.ReadKeys(File.ReadAllBytes(templatePath));
        var resolver = new ConfigurationItemResolver(new DefaultToken(), reader, CreateProvider("dictionary"))
        {
            IgnoreUnresolvablePlaceholders = true,
        };

        var configuration = TemplateFile.LoadJson(TemplateFile.ResolveBytes(resolver, templatePath));

        Assert.Equal("db.internal", configuration["Host"]);
        Assert.Equal("${ Feature:Flag }", configuration["Optional"]);
    }

    [Fact]
    public void UnterminatedItemInJson_RunsToTheNextClosingBrace()
    {
        // JSON's own closing brace ends the item, so the key includes the JSON that follows it.
        var templatePath = _directory.WriteText("unterminated.template.json", """{ "Value": "${unterminated" }""");
        var resolver = CreateResolver(new DefaultToken(), CreateProvider("dictionary"), templatePath);

        var exception = Assert.Throws<ConfigurationKeyException>(() => TemplateFile.ResolveChars(resolver, templatePath));

        Assert.Equal("unterminated\"", exception.Key);
    }

    [Fact]
    public void ValueThatBreaksJson_ProducesInvalidConfiguration()
    {
        // Values are inserted as-is; the resolver does not escape them for JSON.
        var templatePath = _directory.WriteText("quote.template.json", """{ "Value": "${Quote}" }""");
        var resolver = CreateResolver(
            new DefaultToken(),
            new DictionaryValueProvider(new Dictionary<string, string> { ["Quote"] = "a\"b" }),
            templatePath);

        var resolved = TemplateFile.ResolveBytes(resolver, templatePath);

        Assert.ThrowsAny<Exception>(() => TemplateFile.LoadJson(resolved));
    }

    private (IConfigurationResolver Resolver, string TemplatePath) Arrange(string providerName)
    {
        var templatePath = _directory.WriteText("appsettings.template.json", Template);
        return (CreateResolver(new DefaultToken(), CreateProvider(providerName), templatePath), templatePath);
    }

    private static ConfigurationItemResolver CreateResolver(ConfigurationItemTokenBase token, IValueProvider provider, string templatePath)
    {
        return TemplateFile.CreateResolver(token, provider, File.ReadAllText(templatePath));
    }

    private IValueProvider CreateProvider(string providerName)
    {
        switch (providerName)
        {
            case "json":
                var valuesPath = _directory.WriteText("values.json", ValuesJson());
                return new ConfigurationValueProvider(new ConfigurationBuilder().AddJsonFile(valuesPath).Build());

            case "dictionary":
                return new DictionaryValueProvider(new Dictionary<string, string>
                {
                    ["Database:Host"] = "db.internal",
                    ["Database:Port"] = "5432",
                    ["Database:Password"] = "s3cr\\\"t!",
                    ["Greeting"] = "héllo",
                });

            case "environment":
                Environment.SetEnvironmentVariable($"{_environmentPrefix}__Database__Host", "db.internal");
                Environment.SetEnvironmentVariable($"{_environmentPrefix}__Database__Port", "5432");
                Environment.SetEnvironmentVariable($"{_environmentPrefix}__Database__Password", "s3cr\\\"t!");
                Environment.SetEnvironmentVariable($"{_environmentPrefix}__Greeting", "héllo");
                return new PrefixedProvider(_environmentPrefix, new EnvironmentVariableValueProvider());

            case "secrets":
                var secrets = Path.Combine(_directory.Path, "secrets");
                Directory.CreateDirectory(secrets);
                File.WriteAllText(Path.Combine(secrets, "Database__Host"), "db.internal");
                File.WriteAllText(Path.Combine(secrets, "Database__Port"), "5432");
                File.WriteAllText(Path.Combine(secrets, "Database__Password"), "s3cr\\\"t!", new UTF8Encoding(true));
                File.WriteAllText(Path.Combine(secrets, "Greeting"), "héllo");
                return new FileSecretValueProvider(secrets);

            default:
                throw new ArgumentOutOfRangeException(nameof(providerName), providerName, null);
        }
    }

    private static string ValuesJson() => """
        {
          "Database": { "Host": "db.internal", "Port": "5432", "Password": "s3cr\\\"t!" },
          "Greeting": "héllo"
        }
        """;

    private static void AssertResolved(IConfiguration configuration)
    {
        Assert.Equal("Host=db.internal;Port=5432;Password=s3cr\"t!", configuration["ConnectionStrings:Default"]);
        Assert.Equal("héllo wörld", configuration["Greeting"]);
        Assert.Equal("${} and $ {not an item}", configuration["Literal"]);
    }

    /// <summary>
    /// Lets environment-variable scenarios use the same template keys as the other providers, while
    /// keeping their variable names unique to this test run.
    /// </summary>
    private sealed class PrefixedProvider(string prefix, IValueProvider inner) : IValueProvider
    {
        public ValueStatus TryGetValue(string key, out string? value) => inner.TryGetValue(Prefix(key), out value);

        public ValueStatus TryGetChar(string key, Span<char> buffer, out int charsWritten) => inner.TryGetChar(Prefix(key), buffer, out charsWritten);

        public ValueStatus TryGetBytes(string key, Span<byte> buffer, out int bytesWritten) => inner.TryGetBytes(Prefix(key), buffer, out bytesWritten);

        private string Prefix(string key) => $"{prefix}:{key}";
    }
}
