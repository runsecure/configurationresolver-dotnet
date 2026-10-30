# Handling secrets

A .NET `string` can't be wiped. It stays in memory until the garbage collector reuses the space,
and the collector may have copied it in the meantime. ConfigurationResolver lets you resolve
secrets without turning them into strings:

- The resolver never reads values as strings. Each provider writes its value directly into your
  destination buffer.
- `FileSecretValueProvider` reads secret files without buffering, straight into the destination.
- If resolving fails, the resolver wipes the destination with
  `CryptographicOperations.ZeroMemory`, so no partly resolved values are left behind.
- `SecretBuffer<T>` gives you a destination that the collector can't move and that is wiped when
  you dispose it.

These protections only hold if the rest of the path avoids strings too. A secret that is already in
`IConfiguration`, a dictionary or an environment variable is a string before the resolver sees it.

## Resolve into a `SecretBuffer<T>`

```csharp
using ConfigurationResolver.Buffers;
using ConfigurationResolver.Providers;
using ConfigurationResolver.Readers;
using ConfigurationResolver.Resolvers;

var template = "Host=db.internal;Password=${Database:Password}"u8;

var token = new DefaultToken();
var reader = new ConfigurationItemReader(token);
reader.ReadKeys(template);
var resolver = new ConfigurationItemResolver(token, reader, new FileSecretValueProvider("/run/secrets"));

using var connectionString = new SecretBuffer<byte>(capacity: 1024);
resolver.Resolve(template, connectionString);

// Use the span directly, inside the using scope.
UseConnectionString(connectionString.Span);
```

`SecretBuffer<T>`:

- **Is allocated on the pinned object heap**, so the garbage collector never moves it and never
  leaves copies behind.
- **Is wiped** by `Clear()`, by `Dispose()`, and by its finalizer if you forget to dispose it.
  `Resolve` also wipes it before writing and after a failure.
- **Hides its contents.** It exposes them only through `Span`. `ToString()` and the debugger show
  only the length and capacity.
- **Has a fixed capacity.** If the resolved text doesn't fit, `Resolve` throws `ArgumentException`
  and the buffer is left empty.
- **Can be reused.** Each `Resolve` call wipes the buffer before it writes.

Use `SecretBuffer<char>` with a `char` template, and `SecretBuffer<byte>` with UTF-8.

## Keep the secret out of strings afterwards

The buffer only protects the secret until you copy it somewhere else. Pass `Span` to APIs that
accept spans, and don't call `ToString()`, `new string(...)` or `Encoding.GetString` on it.

```csharp
// Good: hashes the bytes in place.
var hash = SHA256.HashData(connectionString.Span);

// Bad: creates a string that can't be wiped.
var text = Encoding.UTF8.GetString(connectionString.Span);
```

Many APIs, such as most database drivers, accept a connection string only as a `string`. When you
have to hand one over, create the string at the last moment and keep it short-lived. Expect it to
stay in memory, and don't rely on the buffer to protect it.

## Errors and logging

- `ConfigurationKeyException.Message` contains only the item's position and the error, never the
  key or the value, so it is safe to log.
- `ConfigurationKeyException.Key` comes from the text you resolved. Treat it as untrusted input.
- The built-in providers never put a value in an exception. Keep it that way in providers you
  write.

## Limiting what a template can reach

The resolver looks up only the keys that the reader read from a trusted template. If someone
substitutes a template that asks for `${Database:Password}`, and the trusted one didn't, resolving
throws `ConfigurationKeyException` with `Error = NotAllowed`, and the provider is never asked.

To narrow this further:

- Point `FileSecretValueProvider` at a directory that holds only the secrets this template needs.
- With `IConfiguration`, pass a section (`configuration.GetSection("Database")`) instead of the
  root.
