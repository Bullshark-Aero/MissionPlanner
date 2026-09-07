# BSMP release identity

The BSMP version is separate from upstream Mission Planner's AssemblyFileVersion.
The default lives in BsmpVersion.targets. Release builds may override it with
`/p:BsmpVersion=1.0.6`. This generates AssemblyInformationalVersion, which is
exposed by Application.ProductVersion to the title, config exporter and importer.

New-BsmpPortableRelease.ps1 checks that the executable's ProductVersion exactly
matches the requested archive version. The self-extractor contains one directory,
`BSMP 1.0.6 20260907` for this release, and no aircraft config or aircraft plugin.
Distribute the signed aircraft config separately.

Schema 2 MinimumBsmpVersion and MaximumBsmpVersionExclusive use BSMP release
versions, not the upstream MP version. Semantic versions compare using SemVer
precedence, including prerelease identifiers. Build metadata is ignored.
Legacy four-part numeric versions remain readable. Unknown or malformed versions
fail validation. Nothing translates upstream 1.3.83 into BSMP 1.0.6.

A config with incorrect compatibility bounds must be regenerated and re-signed
by its publisher. Do not edit a signed ZIP or bypass its signature check.
