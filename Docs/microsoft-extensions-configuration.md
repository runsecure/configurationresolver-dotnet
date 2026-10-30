# Using ConfigurationResolver with Microsoft.Extensions.Configuration

ConfigurationResolver works with Microsoft.Extensions.Configuration (`IConfiguration`,
`ConfigurationBuilder`, `appsettings.json`) in three ways, and you can use them together:

1. **Resolve a configuration file before loading it.** Ship `appsettings.template.json` with
   `${...}` items in it, fill the values in from environment variables or secret files, then
   load the result with `AddJsonStream`.
2. **Use `IConfiguration` as the source of values.** `ConfigurationValueProvider` looks keys up in
   an `IConfigurationRoot` or a section, so `${Database:Host}` reads `Database:Host` from your
   configuration.
3. **Resolve items inside configuration values as they are read.** `ResolvingConfigurationRoot`
   wraps a built configuration, so a value such as `Server=${Database:Host}` in `appsettings.json`
   reads as `Server=db.internal`, the way Spring Boot resolves `${...}` in properties.

The integration tests in
[JsonConfigurationScenarioTests.cs](../tests/ConfigurationResolver.IntegrationTests/JsonConfigurationScenarioTests.cs)
and
[ResolvingConfigurationRootScenarioTests.cs](../tests/ConfigurationResolver.IntegrationTests/ResolvingConfigurationRootScenarioTests.cs)
cover them.

## Install

```bash
dotnet add package ConfigurationResolver
dotnet add package ConfigurationResolver.Configuration   # for ConfigurationValueProvider and ResolvingConfigurationRoot
dotnet add package Microsoft.Extensions.Configuration.Json
dotnet add package Microsoft.Extensions.Configuration.EnvironmentVariables   # for AddEnvironmentVariables()
```

ASP.NET Core and generic-host apps already include both Microsoft.Extensions packages.

## Scenario 1: resolve `appsettings.template.json` before loading it

`appsettings.template.json` is checked in and ships with the app:

```json
{
  "ConnectionStrings": {
    "Default": "Host=${Database:Host};Port=${Database:Port};Password=${Database:Password}"
  },
  "Greeting": "${Greeting}"
}
```

At startup, resolve it with values from secret files and environment variables, then add the
result to the configuration:

```csharp
using ConfigurationResolver.Providers;
using ConfigurationResolver.Readers;
using ConfigurationResolver.Resolvers;
using ConfigurationResolver.Tokens;
using Microsoft.Extensions.Configuration;

var templatePath = Path.Combine(AppContext.BaseDirectory, "appsettings.template.json");
var template = File.ReadAllBytes(templatePath);

// The template ships with the app, so it is trusted: only the keys it names may be resolved.
var token = new DefaultToken();
var reader = new ConfigurationItemReader(token);
reader.ReadKeys(template);

// Secrets come from /run/secrets/Database__Password and similar files.
var resolver = new ConfigurationItemResolver(token, reader, new FileSecretValueProvider("/run/secrets"));

var resolved = new byte[64 * 1024];
var written = resolver.Resolve(template, resolved);

var configuration = new ConfigurationBuilder()
    .AddJsonStream(new MemoryStream(resolved, 0, written, writable: false))
    .AddEnvironmentVariables()
    .Build();

Console.WriteLine(configuration["ConnectionStrings:Default"]);
```

Notes:

- **Use the byte overload.** `AddJsonStream` reads UTF-8, so reading the template with
  `File.ReadAllBytes` and resolving bytes means the resolved file never becomes a `string`. A UTF-8
  byte order mark at the start of the template is fine.
- **Values are not escaped for JSON.** A value containing `"` or `\` makes the JSON invalid, or
  changes its meaning. Validate values before resolving, or store them already escaped. For
  example, a secret file that contains `s3cr\"t!` loads as `s3cr"t!`.
- **Order matters.** Providers added after `AddJsonStream`, such as `AddEnvironmentVariables()`,
  override values from the resolved file, just as they would override `appsettings.json`.
- **`AddJsonStream` reads the stream once,** when `Build()` is called. Unlike `AddJsonFile`, it
  doesn't reload. To pick up changed secrets, resolve again and build a new configuration.
- **Missing values fail at startup.** If the template names a key that has no value, `Resolve`
  throws `ConfigurationKeyException` with `Error = NotFound` before anything is loaded. To load the
  file anyway, set `IgnoreUnresolvablePlaceholders = true` on the resolver. The item then stays in
  the loaded configuration as written, so `${Greeting}` is the value of `Greeting`.

### In an ASP.NET Core or generic host

Resolve the template and add the stream to the host's configuration builder:

```csharp
var builder = WebApplication.CreateBuilder(args);

var template = File.ReadAllBytes(Path.Combine(builder.Environment.ContentRootPath, "appsettings.template.json"));
var token = new DefaultToken();
var reader = new ConfigurationItemReader(token);
reader.ReadKeys(template);
var resolver = new ConfigurationItemResolver(token, reader, new FileSecretValueProvider("/run/secrets"));

var resolved = new byte[64 * 1024];
var written = resolver.Resolve(template, resolved);

// Added last, so the resolved values override appsettings.json and environment variables.
builder.Configuration.AddJsonStream(new MemoryStream(resolved, 0, written, writable: false));

var app = builder.Build();
```

If you want environment variables to keep overriding the resolved file, insert the stream source
earlier instead. `builder.Configuration.Sources` is an ordinary list, and sources later in the
list win.

## Scenario 2: use `IConfiguration` as the source of values

`ConfigurationValueProvider` (in `ConfigurationResolver.Configuration`) reads values from any
`IConfiguration`. Use it to fill in a template from values you already loaded with
`ConfigurationBuilder`:

```csharp
using ConfigurationResolver.Configuration;
using ConfigurationResolver.Readers;
using ConfigurationResolver.Resolvers;
using ConfigurationResolver.Tokens;
using Microsoft.Extensions.Configuration;

// values.json: { "Database": { "Host": "db.internal", "Port": "5432" } }
var values = new ConfigurationBuilder()
    .AddJsonFile("values.json")
    .AddEnvironmentVariables()
    .Build();

var template = "Server=${Database:Host};Port=${Database:Port}";

var token = new DefaultToken();
var reader = new ConfigurationItemReader(token);
reader.ReadKeys(template);
var resolver = new ConfigurationItemResolver(token, reader, new ConfigurationValueProvider(values));

var destination = new char[1024];
var written = resolver.Resolve(template, destination);

Console.WriteLine(destination.AsSpan(0, written).ToString());
// Server=db.internal;Port=5432
```

How keys are looked up:

- **Keys use `:`**, the same separator as `IConfiguration`. Because the lookup goes through
  `IConfiguration`, each configuration provider's own mapping still applies. For example,
  `AddEnvironmentVariables()` maps the variable `Database__Host` to `Database:Host`.
- **Keys are case-insensitive**, as they are in `IConfiguration`.
- **A section has no value.** `${Database}` is not found when `Database` only has children.
- **Values are read on each lookup,** so a reloaded configuration (`reloadOnChange: true`) is seen
  the next time you resolve.

### Resolving relative to a section

Pass a section to make keys relative to it:

```csharp
var provider = new ConfigurationValueProvider(values.GetSection("Database"));

// ${Host} now reads Database:Host.
var template = "${Host}:${Port}";
```

The provider can only reach keys inside that section, which is a simple way to stop a template
from reading anything else in the configuration.

### `IConfiguration` is not a secret store

Configuration providers hold every value as a `string` for the life of the process, and strings
can't be wiped from memory. `ConfigurationValueProvider` works for ordinary settings, but it can't
protect a secret that is already in `IConfiguration`. Read secrets with `FileSecretValueProvider`,
and resolve them into a `SecretBuffer<T>` if they must stay out of strings. See
[Handling secrets](secrets.md).

## Combining both scenarios

A common setup keeps a single template that takes passwords from secret files and everything else
from ordinary configuration. Pass both providers to the resolver, highest priority first. For each
key, the first provider that has it supplies the value; see
[Combining providers](value-providers.md#combining-providers).

```csharp
var settings = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .AddEnvironmentVariables()
    .Build();

var template = File.ReadAllBytes("appsettings.template.json");
var token = new DefaultToken();
var reader = new ConfigurationItemReader(token);
reader.ReadKeys(template);

// Secret files win; anything they don't have comes from configuration.
var resolver = new ConfigurationItemResolver(
    token,
    reader,
    new FileSecretValueProvider("/run/secrets"),
    new ConfigurationValueProvider(settings));

var resolved = new byte[64 * 1024];
var written = resolver.Resolve(template, resolved);

var configuration = new ConfigurationBuilder()
    .AddConfiguration(settings)
    .AddJsonStream(new MemoryStream(resolved, 0, written, writable: false))
    .Build();
```

## Scenario 3: resolve items inside configuration values with `ResolvingConfigurationRoot`

`appsettings.json` can refer to other settings and to secrets directly:

```json
{
  "Database": { "Host": "db.internal", "Port": "5432" },
  "ConnectionStrings": {
    "Default": "Host=${Database:Host};Port=${Database:Port};Password=${Database:Password}"
  },
  "Api": { "Url": "https://${Api:Host}/v1", "Host": "api.${Region}" },
  "Region": "eu"
}
```

Build the configuration as usual, then wrap it:

```csharp
using ConfigurationResolver.Builders;
using ConfigurationResolver.Configuration;
using ConfigurationResolver.Providers;
using ConfigurationResolver.Readers;
using ConfigurationResolver.Tokens;
using Microsoft.Extensions.Configuration;

var inner = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .AddEnvironmentVariables()
    .Build();

var token = new DefaultToken();
var reader = new ConfigurationItemReader(token);   // the root fills it in
var resolver = new ConfigurationResolverBuilder()
    .WithToken(token)
    .WithReader(reader)
    .AddValueProvider(new FileSecretValueProvider("/run/secrets"))   // secrets win
    .AddValueProvider(new ConfigurationValueProvider(inner))         // then other settings
    .WithMaxResolutionDepth(2)   // Api:Url -> Api:Host -> Region
    .Build();

var configuration = new ResolvingConfigurationRoot(inner, reader, resolver);

Console.WriteLine(configuration["ConnectionStrings:Default"]);   // Host=db.internal;Port=5432;Password=<secret file>
Console.WriteLine(configuration.GetSection("Api")["Url"]);       // https://api.eu/v1
```

How it behaves:

- **Values are resolved each time they are read,** through the indexer, `GetSection`,
  `GetChildren`, `AsEnumerable` and anything built on them.
- **The allowlist comes from the configuration itself.** The root reads every key referenced in
  the inner configuration's values into the reader when it is created. When a value refers to a
  key the reader doesn't have, such as after a reload, it reads them again and retries once. A
  value from a value provider, such as a secret file, can only refer to keys that some
  configuration value refers to. Pass the reader the resolver uses.
- **Give the providers the inner configuration,** `new ConfigurationValueProvider(inner)`, not the
  resolving root. Use `WithMaxResolutionDepth` to resolve settings that refer to settings that
  contain items, as `Api:Url` does above.
- **Setting a value** stores it unresolved in the inner configuration:
  `configuration["X"] = "${Region}"` reads back as `eu`.
- **Errors surface when a value is read.** A missing value throws `ConfigurationKeyException` with
  `NotFound`, unless the resolver ignores unresolvable placeholders. A loop throws `Cycle`.
- **Write `$${` for a literal `${`** in a value: `"$${Database:Host}"` reads as `${Database:Host}`.
- `Providers`, `Reload()` and `GetReloadToken()` are those of the inner configuration.
- **Not for secrets that must stay out of strings.** Every value read through the root is a
  `string`, including any secret it pulls in. See [Handling secrets](secrets.md).

## Troubleshooting

| Symptom | Cause |
| --- | --- |
| `ConfigurationKeyException` with `NotAllowed` | The key isn't in the template passed to `ReadKeys`, or you read a different template after creating the resolver. The resolver uses the reader's most recent keys. |
| `ConfigurationKeyException` with `NotFound` | The provider has no value for the key. With a section provider, check that the key is relative to the section. |
| `ArgumentException` for `destination` | The destination buffer is too small. Allocate more space, or see [Sizing the destination](getting-started.md#sizing-the-destination). |
| A `JsonException` (such as `JsonReaderException`) from `Build()` | A value contains `"`, `\` or a control character, and broke the JSON after it was inserted. |
| A value from the template is overridden | A configuration source added after `AddJsonStream` has the same key. |
