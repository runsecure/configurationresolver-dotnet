<#
.SYNOPSIS
    Build, pack and package-verification helpers for this repository's NuGet
    packages, shared by the local signed build (Build-Signed.ps1) and the
    GitHub Actions workflows. Dot-source this file; it defines functions and
    does nothing on its own:

        . (Join-Path $PSScriptRoot "ConfigurationResolverPackaging.ps1")

.DESCRIPTION
    Every project under src\ is a package. The order of a signed release is:

      1. Invoke-ConfigurationResolverBuild
                                    restore (locked), build Release, test
      2. Get-ConfigurationResolverBuiltAssemblies
                                    the assemblies built from this repository,
                                    in bin\, which is where `dotnet pack`
                                    takes them from - sign these
                                    (Invoke-ArtifactSigning in ArtifactSigning.ps1)
      3. Invoke-ConfigurationResolverPack
                                    pack --no-build, so the packages carry the
                                    signed assemblies
      4. Assert-ConfigurationResolverPackagesSigned
                                    open every package and verify every
                                    assembly in it, as shipped

    Works in Windows PowerShell 5.1 and PowerShell 7.
#>

$script:ConfigurationResolverConfiguration = "Release"
$script:ConfigurationResolverSolution = "ConfigurationResolver.slnx"

# The release version from a tag: v1.2.3 -> 1.2.3, v1.2.3-rc.1 -> 1.2.3-rc.1.
function ConvertFrom-ConfigurationResolverTag {
    param([Parameter(Mandatory)][string]$Tag)
    if ($Tag -notmatch '^v(\d+\.\d+\.\d+(-[0-9A-Za-z]+(\.[0-9A-Za-z]+)*)?)$') {
        throw "Tag '$Tag' is not vMAJOR.MINOR.PATCH, optionally with a pre-release suffix such as -rc.1."
    }
    return $Matches[1]
}

# The VersionPrefix in Directory.Build.props: the version of the next release.
function Get-ConfigurationResolverVersionPrefix {
    param([Parameter(Mandatory)][string]$RepoRoot)
    [xml]$props = Get-Content -Raw (Join-Path $RepoRoot "Directory.Build.props")
    $prefix = @($props.Project.PropertyGroup | ForEach-Object { $_.VersionPrefix } | Where-Object { $_ })
    if ($prefix.Count -ne 1) { throw "Directory.Build.props must set VersionPrefix exactly once." }
    return $prefix[0].Trim()
}

# Fails unless the version's MAJOR.MINOR.PATCH is Directory.Build.props'
# VersionPrefix, so a release can't ship a version the source doesn't claim.
function Assert-ConfigurationResolverVersionMatchesSource {
    param([Parameter(Mandatory)][string]$RepoRoot, [Parameter(Mandatory)][string]$Version)
    $prefix = Get-ConfigurationResolverVersionPrefix -RepoRoot $RepoRoot
    $core = ($Version -split '-', 2)[0]
    if ($core -ne $prefix) {
        throw "Version $Version does not match VersionPrefix $prefix in Directory.Build.props. Update VersionPrefix first, or tag v$prefix."
    }
}

# True for a version published as a GitHub pre-release: any 0.x version, or
# one with a pre-release suffix.
function Test-ConfigurationResolverPrerelease {
    param([Parameter(Mandatory)][string]$Version)
    return ($Version -like "0.*" -or $Version -like "*-*")
}

# The packable projects: every project directly under src\.
function Get-ConfigurationResolverPackableProjects {
    param([Parameter(Mandatory)][string]$RepoRoot)
    $projects = @(Get-ChildItem (Join-Path $RepoRoot "src") -Directory |
        ForEach-Object { Get-ChildItem $_.FullName -Filter *.csproj -File } |
        Sort-Object Name)
    if ($projects.Count -eq 0) { throw "No projects found under $RepoRoot\src" }
    return $projects
}

function Invoke-Dotnet {
    param([Parameter(Mandatory)][string[]]$Arguments)
    Write-Host "dotnet $($Arguments -join ' ')"
    # To the console, never into the caller's return value: a function that
    # returns paths must return only paths.
    & dotnet @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments[0]) failed (exit $LASTEXITCODE)" }
}

# Restores exactly the locked dependency graph, builds the whole solution in
# Release with the version stamped in, and runs the tests with coverage.
function Invoke-ConfigurationResolverBuild {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$Version,
        [switch]$SkipTests
    )
    $solution = Join-Path $RepoRoot $script:ConfigurationResolverSolution
    Invoke-Dotnet @("restore", $solution, "--locked-mode")
    Invoke-Dotnet @("build", $solution, "--configuration", $script:ConfigurationResolverConfiguration, "--no-restore",
        "-p:Version=$Version", "-nologo")
    if (-not $SkipTests) {
        Invoke-Dotnet @("test", $solution, "--configuration", $script:ConfigurationResolverConfiguration, "--no-build", "-nologo",
            "--collect:XPlat Code Coverage", "--results-directory", (Join-Path $RepoRoot "artifacts\test-results"))
    }
}

# Every assembly built from this repository that ships: <Project>.dll in each
# packable project's bin\Release\<tfm>\, for every target framework. Fails if
# a project has none. Dependencies and framework assemblies are never listed.
function Get-ConfigurationResolverBuiltAssemblies {
    param([Parameter(Mandatory)][string]$RepoRoot)
    $assemblies = @()
    foreach ($project in Get-ConfigurationResolverPackableProjects -RepoRoot $RepoRoot) {
        $name = $project.BaseName
        $bin = Join-Path $project.DirectoryName "bin\$($script:ConfigurationResolverConfiguration)"
        $found = @(Get-ChildItem $bin -Directory -ErrorAction SilentlyContinue |
            ForEach-Object { Join-Path $_.FullName "$name.dll" } |
            Where-Object { Test-Path $_ })
        if ($found.Count -eq 0) { throw "No built $name.dll under $bin. Build first." }
        $assemblies += $found
    }
    return $assemblies
}

# Packs every packable project from its existing build output (no rebuild),
# with symbols, into OutputDirectory. Returns the .nupkg paths; the matching
# .snupkg files sit beside them.
function Invoke-ConfigurationResolverPack {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$OutputDirectory
    )
    if (Test-Path $OutputDirectory) { Remove-Item -Recurse -Force $OutputDirectory }
    New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
    $packages = @()
    foreach ($project in Get-ConfigurationResolverPackableProjects -RepoRoot $RepoRoot) {
        Invoke-Dotnet @("pack", $project.FullName, "--configuration", $script:ConfigurationResolverConfiguration,
            "--no-build", "--no-restore", "-p:Version=$Version", "--output", $OutputDirectory, "-nologo")
        $package = Join-Path $OutputDirectory "$($project.BaseName).$Version.nupkg"
        if (-not (Test-Path $package)) { throw "dotnet pack did not produce $package" }
        if (-not (Test-Path ([IO.Path]::ChangeExtension($package, ".snupkg")))) {
            throw "dotnet pack did not produce the symbols package for $package"
        }
        $packages += $package
    }
    return $packages
}

# Opens every package and requires each assembly in it to carry a valid,
# timestamped Authenticode signature (signtool verify /pa /tw, then
# Get-AuthenticodeSignature) - checking the files that actually ship, not the
# copies in bin\. Also requires each package to contain its own assembly, so a
# package that somehow lost it can't pass by having nothing to check.
function Assert-ConfigurationResolverPackagesSigned {
    param([Parameter(Mandatory)][string[]]$Packages, [Parameter(Mandatory)] $Config)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $root = Join-Path ([IO.Path]::GetTempPath()) ("configurationresolver-verify-" + [Guid]::NewGuid().ToString("N"))
    try {
        $assemblies = @()
        foreach ($package in $Packages) {
            $id = [IO.Path]::GetFileNameWithoutExtension($package) -replace '\.\d+\.\d+\.\d+(-.+)?$', ''
            $dir = Join-Path $root ([IO.Path]::GetFileName($package))
            [IO.Compression.ZipFile]::ExtractToDirectory((Resolve-Path $package).Path, $dir)
            $inPackage = @(Get-ChildItem $dir -Recurse -File -Include *.dll, *.exe | Select-Object -ExpandProperty FullName)
            if (-not ($inPackage | Where-Object { [IO.Path]::GetFileName($_) -eq "$id.dll" })) {
                throw "$package does not contain $id.dll"
            }
            $assemblies += $inPackage
        }
        Write-Host "Verifying $($assemblies.Count) assembly(ies) inside $($Packages.Count) package(s)..."
        Assert-ArtifactSignature -Files $assemblies -Config $Config
    } finally {
        if (Test-Path $root) { Remove-Item -Recurse -Force $root }
    }
}
