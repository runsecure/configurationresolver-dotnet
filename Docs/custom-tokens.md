# Custom tokens

A token defines the sequences that start and end a configuration item. `DefaultToken` uses `${`
and `}`. To use other delimiters, derive from `ConfigurationItemTokenBase` and pass them to the
base constructor:

```csharp
using ConfigurationResolver.Tokens;

public sealed class MustacheToken() : ConfigurationItemTokenBase("{{", "}}")
{
}
```

```csharp
var token = new MustacheToken();
var reader = new ConfigurationItemReader(token);
reader.ReadKeys(template);
var resolver = new ConfigurationItemResolver(token, reader, provider);

// "{{Database:Host}}" is resolved; "${Database:Host}" is copied unchanged.
```

Use the same token for the reader and the resolver. Otherwise the reader records keys from
different items than the ones the resolver finds.

## Choosing delimiters

- The start and end sequences can't be null or empty.
- They are matched exactly, using ordinal comparison.
- An item ends at the **first** end sequence after its start, and items can't be nested. Choose an
  end sequence that never appears inside a key.
- With `DefaultToken` in a JSON template, an item that is missing its `}` runs on to the next `}`
  in the file, usually the end of the JSON object. The key then contains JSON text, and resolving
  throws `NotAllowed` or `NotFound`.
- Values are inserted as-is, so a value can contain the start sequence without being resolved
  again.
