using System.Security.Cryptography;
using System.Text;
using ConfigurationResolver.Interfaces;

namespace ConfigurationResolver.Providers;

/// <summary>
/// Provides configuration values from secret files in a directory, such as the secret volumes that
/// Docker and Kubernetes mount into containers.
/// </summary>
/// <remarks>
/// Each file in the directory holds one value, and its file name is the key. The <c>:</c> separator in
/// keys is translated to <c>__</c>, so the key <c>Database:Password</c> is read from the file
/// <c>Database__Password</c>. Files are read on each lookup and are expected to be UTF-8; a UTF-8 byte
/// order mark is skipped. Files are opened without buffering, so secrets are not copied into internal
/// buffers that cannot be wiped.
/// <para>
/// Symbolic links and junctions below the directory are followed only while they stay inside it, such as
/// the <c>..data</c> links Kubernetes uses to swap secrets atomically; a link that leads outside throws
/// <see cref="UnauthorizedAccessException"/>. The directory itself is trusted and may be a link. Links are
/// checked before the file is opened, so this does not stop someone who can change the directory while it
/// is being read, and hard links cannot be detected.
/// </para>
/// </remarks>
public class FileSecretValueProvider : ValueProviderBase
{
    /// <summary>
    /// The separator used in secret file names.
    /// </summary>
    public const string FileNameSeparator = "__";

    /// <summary>
    /// The separator used in configuration keys.
    /// </summary>
    public const string KeySeparator = ":";

    /// <summary>
    /// The most links followed for one secret file, the same limit Linux uses.
    /// </summary>
    public const int MaxLinkHops = 40;

    private readonly string _directory;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSecretValueProvider"/> class.
    /// </summary>
    /// <param name="directory">The directory that contains the secret files, such as <c>/run/secrets</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="directory"/> is <see langword="null"/> or empty.</exception>
    public FileSecretValueProvider(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);

        _directory = Path.GetFullPath(directory);
    }

    /// <summary>
    /// Gets the full path of the directory that contains the secret files.
    /// </summary>
    public string Directory => _directory;

    /// <summary>
    /// Attempts to read the secret file for the specified key directly into <paramref name="destination"/>.
    /// </summary>
    /// <remarks>
    /// The file is read without an intermediate buffer, so the secret is written only to
    /// <paramref name="destination"/>.
    /// </remarks>
    /// <inheritdoc />
    public override ValueStatus TryGetBytes(string key, Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0;

        using var stream = OpenSecret(key);
        if (stream is null)
        {
            return ValueStatus.NotFound;
        }

        var length = SkipPreamble(stream);
        if (length > destination.Length)
        {
            return ValueStatus.DestinationTooSmall;
        }

        stream.ReadExactly(destination[..length]);
        bytesWritten = length;
        return ValueStatus.Found;
    }

    /// <inheritdoc />
    protected override bool TryGetByteCount(string key, out int byteCount)
    {
        using var stream = OpenSecret(key);
        byteCount = stream is null ? 0 : SkipPreamble(stream);
        return stream is not null;
    }

    /// <summary>
    /// Moves past a UTF-8 byte order mark, if the file starts with one.
    /// </summary>
    /// <returns>The number of bytes left in the file.</returns>
    private static int SkipPreamble(FileStream stream)
    {
        var preamble = Encoding.UTF8.Preamble;
        Span<byte> start = stackalloc byte[preamble.Length];
        var startLength = stream.ReadAtLeast(start, start.Length, throwOnEndOfStream: false);
        if (startLength != preamble.Length || !start.SequenceEqual(preamble))
        {
            stream.Position = 0;
        }

        // The bytes just read may be the start of the secret.
        CryptographicOperations.ZeroMemory(start);
        return checked((int)(stream.Length - stream.Position));
    }

    private FileStream? OpenSecret(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var fileName = key.Replace(KeySeparator, FileNameSeparator, StringComparison.Ordinal);

        // Reject anything that isn't a plain file name, so a key can't reach outside the directory.
        if (fileName.Length == 0
            || fileName is "." or ".."
            || fileName.AsSpan().IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return null;
        }

        var path = ResolveLinks(Path.Combine(_directory, fileName));
        try
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 0);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the target of a symbolic link or junction.
    /// </summary>
    /// <param name="path">The full path of a file or directory, which may not exist.</param>
    /// <returns>The link's target as stored in the link, or <see langword="null"/> if the path is not a link.</returns>
    protected virtual string? GetLinkTarget(string path)
    {
        return System.IO.Directory.Exists(path) ? new DirectoryInfo(path).LinkTarget : new FileInfo(path).LinkTarget;
    }

    /// <summary>
    /// Follows every link below <see cref="Directory"/> in <paramref name="path"/>, and returns the path
    /// with no links left in it.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">A link points outside <see cref="Directory"/>.</exception>
    /// <exception cref="IOException">The links loop, or there are more than <see cref="MaxLinkHops"/> of them.</exception>
    private string ResolveLinks(string path)
    {
        var pending = new List<string>(RelativeSegments(path));
        var resolved = _directory;
        var hops = 0;

        while (pending.Count > 0)
        {
            var next = Path.Combine(resolved, pending[0]);
            pending.RemoveAt(0);

            var target = GetLinkTarget(next);
            if (target is null)
            {
                resolved = next;
                continue;
            }

            if (++hops > MaxLinkHops)
            {
                throw new IOException($"A secret file path has more than {MaxLinkHops} links, or its links loop.");
            }

            var targetPath = Path.GetFullPath(target, Path.GetDirectoryName(next)!);
            if (!IsInsideDirectory(targetPath))
            {
                throw new UnauthorizedAccessException("A secret file links to a path outside the secrets directory.");
            }

            // Start again from the directory, since the target may itself go through other links.
            pending.InsertRange(0, RelativeSegments(targetPath));
            resolved = _directory;
        }

        return resolved;
    }

    private string[] RelativeSegments(string path)
    {
        var relative = Path.GetRelativePath(_directory, path);
        return relative == "." ? [] : relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
    }

    private bool IsInsideDirectory(string path)
    {
        // GetRelativePath always uses DirectorySeparatorChar, and returns a rooted path for another drive.
        var relative = Path.GetRelativePath(_directory, path);
        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}
