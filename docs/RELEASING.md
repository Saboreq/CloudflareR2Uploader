# Releasing

Release tags must be `vMAJOR.MINOR.PATCH` or a strict SemVer prerelease such as `v1.2.3-beta.1`. Release work requires Windows, the .NET 10 SDK with Windows desktop targeting, PowerShell 7+, and Inno Setup 6.7.1. `CloudflareR2Uploader.sln` is the only solution used by the release path.

## Configure the trusted update source

Before the first signed release, the repository owner must generate a password-protected RSA PFX outside the repository. The private key, PFX, and password must never enter source control, command arguments, logs, packages, workflow secrets, or artifacts. Follow [the update-channel guide](../deploy/README.md) to export the public SPKI with `export-public-key` while the password exists only in `UPDATE_SIGNING_CERTIFICATE_PASSWORD` in the publishing shell.

Store the exported base64 SPKI in the repository variable `UPDATE_MANIFEST_PUBLIC_KEY`. Store the public HTTPS origin in `UPDATE_BASE_URL`; optionally set `UPDATE_PREFIX`. Tagged builds require the URL and public key as a pair and fail closed if either is absent. Ordinary CI supplies neither and performs no publication.

Manifest signing authenticates the manifest and installer digest. It does not replace Authenticode signing of the Windows executable; Authenticode is a separate defense-in-depth option.

## Build and tag

Before tagging, run the exact build/test/package entry point:

```powershell
.\build\Build-Release.ps1 -Version 1.2.3
```

Confirm that every test passes, inspect `dist\CloudflareR2Uploader-v1.2.3-Setup.exe`, and confirm that `dist` contains exactly one release file. The payload must contain `LICENSE`, `README.md`, and `THIRD-PARTY-NOTICES.md`, and must contain no PFX, private key, generated manifest, or PDB. The script stamps versions through MSBuild properties and must leave the working tree clean.

Push the reviewed commit, then create and push the tag:

```powershell
git tag v1.2.3
git push origin v1.2.3
```

The tagged-release workflow validates the tag, restores, builds, and tests all ten .NET 10 projects, runs the PowerShell release-security harness, publishes the framework-dependent x64 WPF application, validates its payload, compiles the Inno Setup installer, and creates a GitHub Release containing only the setup EXE. Prerelease suffixes produce a prerelease.

Verify a downloaded installer on Windows:

```powershell
(Get-FileHash .\CloudflareR2Uploader-v1.2.3-Setup.exe -Algorithm SHA256).Hash.ToLowerInvariant()
```

## Publish the Cloudflare update

Publish the exact reviewed GitHub Release asset with `deploy\Publish-CloudflareUpdate.ps1 -SkipBuild`. Pass the password-protected PFX path and the environment-variable name `UPDATE_SIGNING_CERTIFICATE_PASSWORD`; never pass the password value itself. Use the deployment guide's caller-side `try`/`finally` wrapper because parameter binding occurs before the script body and can otherwise bypass its internal cleanup.

The publisher consumes the named password, validates release notes as at most 8,000 Unicode scalar values, snapshots the installer, signs through the shared .NET verifier, uploads the installer under a content-addressed `<sha256>-<installer-name>` key, and writes the signed `manifest.json` last. It streams authenticated R2 read-back through exact byte and time limits and performs bounded public HTTPS read-back of both installer and manifest before reporting success. Wrangler remains fixed at `wrangler@4.120.0`.

## Safe key rotation

First release a transition client that embeds the new public key, but publish its manifest with the old PFX so installed clients can authenticate it. Only after that transition is adopted may the owner change `UPDATE_MANIFEST_PUBLIC_KEY` and sign later manifests with the new PFX. Retain the old key through the transition window and never publish an unsigned fallback.

This planned rotation procedure applies only while the old private key remains controlled and trustworthy. If the PFX or private key is lost or compromised, immediately suspend publication: the compromised key must not sign another manifest, and clients must not trust a release authenticated only by that key. Distribute an independently trusted, manual client release that embeds the new public key through an authenticated channel such as a manually verified GitHub Release. Do not resume publication until users have established trust in that replacement client; never use the compromised PFX as a transition key.

## Failure and recovery

If verification fails after `manifest.json` was uploaded, the release may already be live. Do not retry blindly, overwrite a content-addressed installer, reuse a tag, or publish a different installer under the same version.

Perform the deployment guide's authenticated inspection: download the manifest through authenticated R2 access and a cache-busted public HTTPS request, authenticate both copies with the configured public key, and compare them byte-for-byte. Download the referenced installer through both paths, compare it byte-for-byte with the exact reviewed GitHub Release asset, and run `verify-installer` for each copy. If every check agrees, treat the release as live. Otherwise preserve the evidence, stop publication, correct the channel, and recover with a higher version.
