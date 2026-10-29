# configurationresolver-dotnet
A simple configuration resolver for dotnet

## Building

```bash
dotnet restore ConfigurationResolver.slnx
dotnet build ConfigurationResolver.slnx
dotnet test ConfigurationResolver.slnx
```

Every project records its resolved dependency graph in `packages.lock.json`, and CI restores in
locked mode, so a changed or substituted package fails the build. After changing a package
version in `Directory.Packages.props`, update the lock files with
`dotnet restore ConfigurationResolver.slnx --force-evaluate` and commit them. `nuget.config`
restricts every package to nuget.org.

The tests for secret files behind real symbolic links are skipped on Windows unless symbolic links
can be created (Developer Mode or an elevated prompt). CI also runs every test on Linux, where they
always run.

## Versioning

The version of the next release is `VersionPrefix` in `Directory.Build.props`, currently
**0.9.0**. A `0.x` version marks the API as pre-release: it may still change before 1.0. CI builds
are versioned `0.9.0-ci.<run number>`.

## Signing and releases

### What is signed

Every assembly built from this repository (`ConfigurationResolver*.dll`) is Authenticode-signed
through Azure Artifact Signing before it is packed, with the `RunSecureSigning` /
`runsecure-release` certificate profile. Artifact Signing certificates are valid for about three
days, so every signature is RFC 3161 timestamped, which keeps it valid after the certificate
expires.

**The NuGet packages themselves are not author-signed.** The assemblies inside them are signed;
the `.nupkg` and `.snupkg` files are not. nuget.org can't yet accept Artifact Signing's
short-lived certificates without registering each new certificate by hand
([NuGet/NuGetGallery#10027](https://github.com/NuGet/NuGetGallery/issues/10027)). nuget.org adds
its own repository signature to every package it accepts.

Third-party and framework assemblies are never signed by this repository. Signing is separate
from strong naming, which these assemblies don't use.

### Cutting a release

```bash
git tag v0.9.0
git push origin v0.9.0
```

The tag must be `vMAJOR.MINOR.PATCH`, optionally with a pre-release suffix (`v0.9.0-rc.1`), and its
`MAJOR.MINOR.PATCH` must match `VersionPrefix`. The **Release** workflow
(`.github/workflows/release.yml`) then:

1. **build** (the shared `build.yml`, the same one CI runs): restores the locked dependency
   graph, builds Release, runs the tests on Windows and Linux, and uploads the unsigned Windows
   output. It holds no signing access.
2. **sign** (Windows x64, in the `release` environment): signs each project's assembly, packs with
   `--no-build` so the packages carry the signed assemblies, opens every package to verify each
   assembly in it is validly signed and timestamped, and attests build provenance of the packages
   and assemblies. Any failure fails the release; there is no unsigned fallback.
3. **publish**: creates a GitHub Release with every `.nupkg`, `.snupkg` and `.sha256`. Versions
   `0.x`, and any with a pre-release suffix, are published as GitHub pre-releases.

Nothing is pushed to a NuGet feed yet.

### Signing locally

`scripts\build-signed.bat` runs the same build, signing and verification on a Windows machine,
using your own `az login`. It needs the Windows SDK's signtool (10.0.22621 or newer), the Artifact
Signing client tools (`winget install -e --id Microsoft.Azure.ArtifactSigningClientTools`), and the
Artifact Signing Certificate Profile Signer role. The packages land in `artifacts\packages`.
