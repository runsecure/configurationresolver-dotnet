using System.Text;
using ConfigurationResolver.Interfaces;
using ConfigurationResolver.Providers;
using ConfigurationResolver.Readers;
using ConfigurationResolver.Resolvers;
using ConfigurationResolver.Tokens;
using Microsoft.Extensions.Configuration;

namespace ConfigurationResolver.IntegrationTests;

/// <summary>
/// The default <c>${</c> and <c>}</c> token.
/// </summary>
internal sealed class DefaultToken : ConfigurationItemTokenBase
{
}

/// <summary>
/// A token with custom start and end sequences.
/// </summary>
internal sealed class CustomToken(string startSequence, string endSequence)
    : ConfigurationItemTokenBase(startSequence, endSequence)
{
}

/// <summary>
/// Exposes the protected sizing methods of <see cref="ValueProviderBase"/> for testing.
/// </summary>
internal sealed class SizingDictionaryValueProvider(IReadOnlyDictionary<string, string> values)
    : DictionaryValueProvider(values)
{
    public bool CharCount(string key, out int charCount) => TryGetCharCount(key, out charCount);

    public bool ByteCount(string key, out int byteCount) => TryGetByteCount(key, out byteCount);
}

/// <summary>
/// A temporary directory that is deleted when disposed.
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ConfigurationResolver.IntegrationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string WriteText(string fileName, string content, bool byteOrderMark = false)
    {
        var path = System.IO.Path.Combine(Path, fileName);
        File.WriteAllText(path, content, new UTF8Encoding(byteOrderMark));
        return path;
    }

    public string WriteBytes(string fileName, byte[] content)
    {
        var path = System.IO.Path.Combine(Path, fileName);
        File.WriteAllBytes(path, content);
        return path;
    }

    public void Dispose()
    {
        Directory.Delete(Path, recursive: true);
    }
}

/// <summary>
/// Resolves template files on disk and loads the result as JSON configuration.
/// </summary>
internal static class TemplateFile
{
    public const int BufferLength = 4096;

    /// <summary>
    /// Reads the keys of a trusted template and creates a resolver that allows only those keys.
    /// </summary>
    public static ConfigurationItemResolver CreateResolver(ConfigurationItemTokenBase token, IValueProvider provider, string template)
    {
        var reader = new ConfigurationItemReader(token);
        reader.ReadKeys(template);
        return new ConfigurationItemResolver(token, reader, provider);
    }

    public static string ResolveChars(IConfigurationResolver resolver, string path)
    {
        var destination = new char[BufferLength];
        var written = resolver.Resolve(File.ReadAllText(path), destination);
        return new string(destination, 0, written);
    }

    public static byte[] ResolveBytes(IConfigurationResolver resolver, string path)
    {
        var destination = new byte[BufferLength];
        var written = resolver.Resolve(File.ReadAllBytes(path), destination);
        return destination[..written];
    }

    public static IConfigurationRoot LoadJson(string json) => LoadJson(Encoding.UTF8.GetBytes(json));

    public static IConfigurationRoot LoadJson(byte[] json)
    {
        return new ConfigurationBuilder().AddJsonStream(new MemoryStream(json)).Build();
    }
}
