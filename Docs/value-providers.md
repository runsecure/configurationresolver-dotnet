# Value providers

A value provider supplies the value for each key the resolver looks up. Every provider implements
`IValueProvider`, which returns a `ValueStatus` of `Found`, `NotFound` or `DestinationTooSmall`.

| Provider | Package | Reads values from | Good for secrets |
| --- | --- | --- | --- |
| `DictionaryValueProvider` | `ConfigurationResolver` | An `IReadOnlyDictionary<string, string>` | No |
| `EnvironmentVariableValueProvider` | `ConfigurationResolver` | The process's environment variables | No |
| `FileSecretValueProvider` | `ConfigurationResolver` | One file per key in a directory, such as `/run/secrets` | Yes |
| `ConfigurationValueProvider` | `ConfigurationResolver.Configuration` | A Microsoft.Extensions.Configuration `IConfiguration` | No; see [Microsoft.Extensions.Configuration](microsoft-extensions-configuration.md) |

Providers built on strings can't protect secrets, because a `string` can't be wiped from memory.
`FileSecretValueProvider` reads bytes straight from the file into the destination.

## Dictionary

```csharp
var provider = new DictionaryValueProvider(new Dictionary<string, string>
{
    ["Database:Host"] = "db.internal",
});
```

Keys match with the dictionary's own comparer. To make lookups case-insensitive, pass
`StringComparer.OrdinalIgnoreCase` when you create the dictionary.

## Environment variables

```csharp
var provider = new EnvironmentVariableValueProvider();

// ${Database:Host} reads the variable Database__Host.
```

- `:` in a key becomes `__` in the variable name, matching the convention of
  Microsoft.Extensions.Configuration.
- Variables are read on each lookup, so changes made after the provider is created are seen.
- Whether names are case-sensitive depends on the operating system. They are case-insensitive on
  Windows and case-sensitive on Linux and macOS.
- Child processes inherit environment variables, and other tools on the machine can often read
  them, so don't use them for secrets.

## Secret files

```csharp
var provider = new FileSecretValueProvider("/run/secrets");

// ${Database:Password} reads the file /run/secrets/Database__Password.
```

This is the layout that Docker secrets and Kubernetes secret volumes use: each file holds one
value, and its name is the key.

- `:` in a key becomes `__` in the file name.
- Files are read on each lookup, so rotated secrets are seen the next time you resolve.
- Files must be UTF-8. A UTF-8 byte order mark is skipped. Trailing newlines are **not** trimmed,
  so write secret files without one (for example, `printf '%s' "$value" > Database__Password`).
- A key that isn't a plain file name, such as `..` or one containing `/`, is not found, so a key
  can't reach outside the directory.
- Symbolic links and junctions are followed only while they stay inside the directory, as with the
  `..data` links that Kubernetes uses to swap secrets atomically. A link that leads outside throws
  `UnauthorizedAccessException`. The directory itself may be a link.
- If a file keeps changing size while it is read, the resolver throws `ConfigurationKeyException`
  with `Error = ValueUnstable`.

## Writing your own provider

Derive from one of the two base classes, which implement the rest of `IValueProvider` for you.

### Values that are already strings

Derive from `StringValueProviderBase` and implement `TryGetString`:

```csharp
using ConfigurationResolver.Providers;

public sealed class CommandLineValueProvider(IReadOnlyDictionary<string, string> arguments) : StringValueProviderBase
{
    protected override bool TryGetString(string key, out string? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        return arguments.TryGetValue(key, out value);
    }
}
```

### Values that are bytes, such as secrets

Derive from `ValueProviderBase` and implement `TryGetBytes` and `TryGetByteCount`. The base class
builds the `char` and `string` methods on them, using a temporary pinned buffer that it wipes
afterwards.

```csharp
using ConfigurationResolver.Interfaces;
using ConfigurationResolver.Providers;

public sealed class VaultValueProvider(IVaultClient vault) : ValueProviderBase
{
    public override ValueStatus TryGetBytes(string key, Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0;
        if (!vault.TryGetLength(key, out var length))
        {
            return ValueStatus.NotFound;
        }

        if (length > destination.Length)
        {
            return ValueStatus.DestinationTooSmall;
        }

        // Write the secret straight into the destination, without an intermediate copy.
        vault.ReadInto(key, destination[..length]);
        bytesWritten = length;
        return ValueStatus.Found;
    }

    protected override bool TryGetByteCount(string key, out int byteCount)
    {
        return vault.TryGetLength(key, out byteCount);
    }
}
```

`IVaultClient` stands in for your own secret store's client.

Rules for any provider:

- Return `NotFound` and set the count to 0 when the key doesn't exist.
- Return `DestinationTooSmall` when the value doesn't fit, and don't leave part of the value in
  the destination.
- To report a failure with a reason, throw `ConfigurationKeyException(key, error)`. The resolver
  adds the item's position in the source.

## Combining providers

A resolver takes a single provider. To look in several sources, try each in turn:

```csharp
using ConfigurationResolver.Interfaces;

public sealed class FirstFoundValueProvider(params IValueProvider[] providers) : IValueProvider
{
    public ValueStatus TryGetValue(string key, out string? value)
    {
        foreach (var provider in providers)
        {
            if (provider.TryGetValue(key, out value) == ValueStatus.Found)
            {
                return ValueStatus.Found;
            }
        }

        value = null;
        return ValueStatus.NotFound;
    }

    public ValueStatus TryGetChar(string key, Span<char> destination, out int charsWritten)
    {
        foreach (var provider in providers)
        {
            var status = provider.TryGetChar(key, destination, out charsWritten);
            if (status != ValueStatus.NotFound)
            {
                return status;
            }
        }

        charsWritten = 0;
        return ValueStatus.NotFound;
    }

    public ValueStatus TryGetBytes(string key, Span<byte> destination, out int bytesWritten)
    {
        foreach (var provider in providers)
        {
            var status = provider.TryGetBytes(key, destination, out bytesWritten);
            if (status != ValueStatus.NotFound)
            {
                return status;
            }
        }

        bytesWritten = 0;
        return ValueStatus.NotFound;
    }
}
```

```csharp
var provider = new FirstFoundValueProvider(
    new FileSecretValueProvider("/run/secrets"),
    new ConfigurationValueProvider(configuration));
```

Put the provider you trust most first. A key found there is never looked up in the others.
