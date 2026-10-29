using System.Text;
using ConfigurationResolver.Interfaces;
using ConfigurationResolver.Providers;

namespace ConfigurationResolver.IntegrationTests;

/// <summary>
/// Exercises how <see cref="FileSecretValueProvider"/> follows symbolic links, which are allowed only while
/// they stay inside the secrets directory.
/// </summary>
/// <remarks>
/// Most scenarios simulate links by overriding <c>GetLinkTarget</c>, because creating real symbolic links
/// needs extra privileges on Windows. The scenarios that create real links are skipped when that fails.
/// </remarks>
public sealed class FileSecretLinkScenarioTests : IDisposable
{
    private readonly TempDirectory _root = new();
    private readonly string _secrets;

    public FileSecretLinkScenarioTests()
    {
        _secrets = Path.Combine(_root.Path, "secrets");
        Directory.CreateDirectory(Path.Combine(_secrets, "..2024_01_01"));
        File.WriteAllText(Path.Combine(_secrets, "..2024_01_01", "Database__Password"), "k8s-secret");
        File.WriteAllText(Path.Combine(_secrets, "Plain"), "plain");
        File.WriteAllText(Path.Combine(_root.Path, "outside"), "leaked");
    }

    public void Dispose() => _root.Dispose();

    [Fact]
    public void KubernetesLayout_IsReadThroughItsLinks()
    {
        // Kubernetes links each key to ..data/key, and ..data to a timestamped directory it swaps atomically.
        var provider = Simulate(new()
        {
            ["Database__Password"] = "..data/Database__Password",
            ["..data"] = "..2024_01_01",
        });
        var bytes = new byte[32];

        Assert.Equal(ValueStatus.Found, provider.TryGetValue("Database:Password", out var value));
        Assert.Equal(ValueStatus.Found, provider.TryGetBytes("Database:Password", bytes, out var bytesWritten));

        Assert.Equal("k8s-secret", value);
        Assert.Equal("k8s-secret", Encoding.UTF8.GetString(bytes, 0, bytesWritten));
    }

    [Fact]
    public void AbsoluteLinkInsideTheDirectory_IsFollowed()
    {
        var provider = Simulate(new() { ["Absolute"] = Path.Combine(_secrets, "Plain") });

        Assert.Equal(ValueStatus.Found, provider.TryGetValue("Absolute", out var value));
        Assert.Equal("plain", value);
    }

    [Fact]
    public void DanglingLinkInsideTheDirectory_IsNotFound()
    {
        var provider = Simulate(new() { ["Dangling"] = "Missing" });

        Assert.Equal(ValueStatus.NotFound, provider.TryGetValue("Dangling", out _));
    }

    [Fact]
    public void LinkToTheDirectoryItself_CannotBeReadAsAFile()
    {
        var provider = Simulate(new() { ["Self"] = "." });

        Assert.Throws<UnauthorizedAccessException>(() => provider.TryGetValue("Self", out _));
    }

    public static TheoryData<string> OutsideTargets()
    {
        var targets = new TheoryData<string> { "../outside", "..", "..data/../../outside" };
        if (OperatingSystem.IsWindows())
        {
            // Another root, so the relative path from the secrets directory is itself rooted.
            targets.Add(@"\\server\share\outside");
        }

        return targets;
    }

    [Theory]
    [MemberData(nameof(OutsideTargets))]
    public void LinkOutsideTheDirectory_IsRefused(string target)
    {
        var provider = Simulate(new() { ["Leak"] = target, ["..data"] = "..2024_01_01" });

        var exception = Assert.Throws<UnauthorizedAccessException>(() => provider.TryGetValue("Leak", out _));

        Assert.DoesNotContain("Leak", exception.Message);
        Assert.DoesNotContain(_root.Path, exception.Message);
    }

    [Fact]
    public void LinkThroughADirectoryLinkOutsideTheDirectory_IsRefused()
    {
        var provider = Simulate(new() { ["Database__Password"] = "..data/Database__Password", ["..data"] = ".." });

        Assert.Throws<UnauthorizedAccessException>(() => provider.TryGetValue("Database:Password", out _));
    }

    [Fact]
    public void AbsoluteLinkOutsideTheDirectory_IsRefused()
    {
        var provider = Simulate(new() { ["Leak"] = Path.Combine(_root.Path, "outside") });

        Assert.Throws<UnauthorizedAccessException>(() => provider.TryGetBytes("Leak", new byte[32], out _));
    }

    [Fact]
    public void LinksThatLoop_Throw()
    {
        var provider = Simulate(new() { ["A"] = "B", ["B"] = "A" });

        Assert.Throws<IOException>(() => provider.TryGetValue("A", out _));
    }

    [Theory]
    [InlineData(FileSecretValueProvider.MaxLinkHops, true)]
    [InlineData(FileSecretValueProvider.MaxLinkHops + 1, false)]
    public void ChainOfLinks_IsFollowedUpToTheLimit(int hops, bool expectRead)
    {
        // Link0 -> Link1 -> ... -> Plain, which takes exactly `hops` links.
        var links = new Dictionary<string, string>();
        for (var i = 0; i < hops; i++)
        {
            links[$"Link{i}"] = i == hops - 1 ? "Plain" : $"Link{i + 1}";
        }

        var provider = Simulate(links);

        if (expectRead)
        {
            Assert.Equal(ValueStatus.Found, provider.TryGetValue("Link0", out var value));
            Assert.Equal("plain", value);
        }
        else
        {
            Assert.Throws<IOException>(() => provider.TryGetValue("Link0", out _));
        }
    }

    [SkippableFact]
    public void RealKubernetesLayout_IsRead()
    {
        CreateLinkOrSkip(Path.Combine(_secrets, "..data"), "..2024_01_01", directory: true);
        CreateLinkOrSkip(Path.Combine(_secrets, "Database__Password"), Path.Combine("..data", "Database__Password"), directory: false);
        var provider = new FileSecretValueProvider(_secrets);

        Assert.Equal(ValueStatus.Found, provider.TryGetValue("Database:Password", out var value));
        Assert.Equal("k8s-secret", value);
    }

    [SkippableFact]
    public void RealLinkOutsideTheDirectory_IsRefused()
    {
        CreateLinkOrSkip(Path.Combine(_secrets, "Leak"), Path.Combine("..", "outside"), directory: false);
        var provider = new FileSecretValueProvider(_secrets);

        Assert.Throws<UnauthorizedAccessException>(() => provider.TryGetValue("Leak", out _));
    }

    [SkippableFact]
    public void RealDirectoryLinkOutsideTheDirectory_IsRefused()
    {
        CreateLinkOrSkip(Path.Combine(_secrets, "..data"), _root.Path, directory: true);
        CreateLinkOrSkip(Path.Combine(_secrets, "Leak"), Path.Combine("..data", "outside"), directory: false);
        var provider = new FileSecretValueProvider(_secrets);

        Assert.Throws<UnauthorizedAccessException>(() => provider.TryGetValue("Leak", out _));
    }

    private SimulatedLinksProvider Simulate(Dictionary<string, string> links) => new(_secrets, links);

    private static void CreateLinkOrSkip(string path, string target, bool directory)
    {
        try
        {
            if (directory)
            {
                Directory.CreateSymbolicLink(path, target);
            }
            else
            {
                File.CreateSymbolicLink(path, target);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Skip.If(true, $"Symbolic links cannot be created here: {ex.Message}");
        }
    }

    /// <summary>
    /// Treats the given paths, relative to the secrets directory, as links to the given targets.
    /// </summary>
    private sealed class SimulatedLinksProvider(string directory, Dictionary<string, string> links)
        : FileSecretValueProvider(directory)
    {
        protected override string? GetLinkTarget(string path)
        {
            return links.TryGetValue(Path.GetRelativePath(Directory, path), out var target) ? target : null;
        }
    }
}
