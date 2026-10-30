# Getting started

This guide resolves a short template, then explains the pieces involved and the errors you can
get.

## Install

```bash
dotnet add package ConfigurationResolver
```

## Resolve a template

```csharp
using ConfigurationResolver.Providers;
using ConfigurationResolver.Readers;
using ConfigurationResolver.Resolvers;

var template = "Host=${Database:Host};Port=${Database:Port}";

var token = new DefaultToken();
var reader = new ConfigurationItemReader(token);
reader.ReadKeys(template);

var provider = new DictionaryValueProvider(new Dictionary<string, string>
{
    ["Database:Host"] = "db.internal",
    ["Database:Port"] = "5432",
});

var resolver = new ConfigurationItemResolver(token, reader, provider);

var destination = new char[1024];
var written = resolver.Resolve(template, destination);

Console.WriteLine(destination.AsSpan(0, written).ToString());
// Host=db.internal;Port=5432
```

## How it fits together

| Piece | Job |
| --- | --- |
| **Token** (`DefaultToken`, or your own `ConfigurationItemTokenBase`) | Defines the start and end sequences of an item. `DefaultToken` uses `${` and `}`. |
| **Reader** (`ConfigurationItemReader`) | Reads a template and records the keys it contains. Those keys are the only ones the resolver will look up. |
| **Value provider** (`IValueProvider`) | Supplies the value for a key, writing it into the destination. |
| **Resolver** (`ConfigurationItemResolver`) | Copies the source to the destination, replacing each item with its value. |

### Read keys from a trusted template

The resolver only looks up keys that the reader returned from its most recent `ReadKeys` call.
Any other key throws a `ConfigurationKeyException` with `ConfigurationKeyError.NotAllowed`, and
the provider is never asked for it.

Read the keys from a template you trust, such as one that ships with your application. If an
attacker can change the text you resolve later, they still can't use it to pull out other values,
such as a password the trusted template never mentioned.

`ReadKeys` accepts a `string` or `ReadOnlySpan<char>`, UTF-8 bytes, or a `Stream`:

```csharp
using (var stream = File.OpenRead("appsettings.template.json"))
{
    reader.ReadKeys(stream);
}
```

### Item syntax

- Whitespace around the key is trimmed, so `${ Database:Port }` reads the key `Database:Port`.
- An empty item such as `${}` is copied to the output unchanged.
- A start sequence with no end sequence after it is copied unchanged.
- The resolver inserts values exactly as they are and does not escape them for JSON, XML or any
  other format. Make sure the values are valid for the place they go.

### Chars or UTF-8 bytes

`Resolve` has two overloads, one for `char` spans and one for UTF-8 `byte` spans. Use the byte
overload when the template is a file and the result goes into a stream, so the text is never
turned into a `string`:

```csharp
var source = File.ReadAllBytes("appsettings.template.json");
var destination = new byte[16 * 1024];
var written = resolver.Resolve(source, destination);
```

If the error is in UTF-8 bytes, `ConfigurationKeyException.SourceIndex` is a byte index.
Otherwise it is a character index.

## Sizing the destination

You allocate the destination yourself. If it is too small for the resolved text, `Resolve`
throws an `ArgumentException` and wipes the destination. To handle output whose size you don't
know, retry with a larger buffer:

```csharp
static string ResolveToString(IConfigurationResolver resolver, string template)
{
    for (var length = Math.Max(template.Length * 2, 256); ; length *= 2)
    {
        var destination = new char[length];
        try
        {
            var written = resolver.Resolve(template, destination);
            return new string(destination, 0, written);
        }
        catch (ArgumentException ex) when (ex.ParamName == "destination" && length < 1024 * 1024)
        {
            // Too small; try again with twice the space.
        }
    }
}
```

This helper returns a `string`, so don't use it for secrets. [Handling secrets](secrets.md)
shows how to keep them out of strings.

## Errors

| Exception | When |
| --- | --- |
| `ArgumentException` (parameter `destination`) | The destination is too small for the resolved text. |
| `ConfigurationKeyException` with `Error = NotAllowed` | The key isn't in the reader's most recent keys. |
| `ConfigurationKeyException` with `Error = NotFound` | The key is allowed, but the provider has no value for it. |
| `ConfigurationKeyException` with `Error = ValueUnstable` | The value kept changing size while it was read, such as a secret file being rewritten. |

On any failure the resolver wipes the destination, so no partly resolved values are left behind.

The message of a `ConfigurationKeyException` contains only the item's position and the error, so
it is safe to log. `Key` holds the key itself. It comes from the text being resolved, which may be
untrusted, so treat it as untrusted input before you log or display it.

```csharp
try
{
    written = resolver.Resolve(template, destination);
}
catch (ConfigurationKeyException ex)
{
    logger.LogError("Could not resolve configuration item at {Index}: {Error}", ex.SourceIndex, ex.Error);
    throw;
}
```

## Next steps

- [Microsoft.Extensions.Configuration](microsoft-extensions-configuration.md)
- [Value providers](value-providers.md)
- [Handling secrets](secrets.md)
