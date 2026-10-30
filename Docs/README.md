# ConfigurationResolver guides

ConfigurationResolver replaces items such as `${Database:Host}` in text or UTF-8 bytes with
values from a value provider. It writes each value straight into a buffer you supply.

| Guide | Read it when you want to |
| --- | --- |
| [Getting started](getting-started.md) | Resolve your first template and understand the reader, resolver, token and provider |
| [Microsoft.Extensions.Configuration](microsoft-extensions-configuration.md) | Resolve an `appsettings.json` template before loading it, use `IConfiguration` as the source of values, or resolve `${...}` inside configuration values with `ResolvingConfigurationRoot` |
| [Value providers](value-providers.md) | Read values from a dictionary, environment variables or secret files, or write your own provider |
| [Handling secrets](secrets.md) | Keep resolved secrets out of strings with `SecretBuffer<T>`, and handle failures safely |
| [Custom tokens](custom-tokens.md) | Use delimiters other than `${` and `}` |

## Packages

| Package | Contains |
| --- | --- |
| `ConfigurationResolver` | The reader, resolver, `ConfigurationResolverBuilder`, tokens, `SecretBuffer<T>`, the dictionary, environment-variable and secret-file providers, and `CompositeValueProvider` for combining them |
| `ConfigurationResolver.Configuration` | `ConfigurationValueProvider`, which reads values from a Microsoft.Extensions.Configuration `IConfiguration`, and `ResolvingConfigurationRoot`, which resolves the items in a configuration's values |

Both packages target .NET 10. The API is pre-release (`0.x`) and may change before 1.0.
