# Cloudflare R2 update channel

The application can check an HTTPS R2 manifest at startup. It shows the available version and release notes, and installs nothing until the user selects **Update now**. The manifest must have a valid RSA-PSS-SHA256 signature from the public key embedded in the installed application, and the downloaded installer must match both `sizeBytes` and `sha256`.

## One-time Cloudflare setup

1. Install the .NET 10 SDK, PowerShell 7+, Inno Setup 6.7.1, and Node.js 20 or newer; confirm `curl.exe` is available for bounded public HTTPS read-back; then authenticate Wrangler with `npx --yes wrangler@4.120.0 login`.
2. Create a dedicated bucket, for example:

   ```powershell
   npx --yes wrangler@4.120.0 r2 bucket create cloudflare-r2-uploader-updates
   ```

3. In Cloudflare Dashboard, open **R2 object storage**, select that bucket, open **Settings**, and connect a production custom domain such as `downloads.example.com`. A Cloudflare-managed `r2.dev` URL can be used for testing, but is rate-limited and is not intended for production.
4. Do not put private files in this bucket. The installer and manifest must be publicly readable so installed clients can update without receiving publisher credentials.

## One-time signing setup

The repository owner must generate a dedicated RSA certificate outside the repository and export it as a password-protected PFX. Keep the PFX outside the checkout and keep its password only in the publishing shell; never commit either value or upload either as a release artifact. For example, an owner can use OpenSSL in a private directory:

```powershell
openssl req -x509 -newkey rsa:3072 -sha256 -keyout update-signing-key.pem -out update-signing-cert.pem -days 3650 -subj '/CN=Cloudflare R2 Uploader Updates'
openssl pkcs12 -export -out update-signing.pfx -inkey update-signing-key.pem -in update-signing-cert.pem
```

Set the password only in the named process environment variable `UPDATE_SIGNING_CERTIFICATE_PASSWORD`, then export the public SubjectPublicKeyInfo. The command prints public key material only. The `finally` block ensures the password leaves the publishing shell even when the command fails:

```powershell
$securePassword = Read-Host 'PFX password' -AsSecureString
$env:UPDATE_SIGNING_CERTIFICATE_PASSWORD = [Net.NetworkCredential]::new('', $securePassword).Password
try {
  dotnet run --project .\tools\CloudflareR2Uploader.UpdateSigner -c Release -- `
    export-public-key --pfx C:\private\update-signing.pfx --password-env UPDATE_SIGNING_CERTIFICATE_PASSWORD
}
finally {
  Remove-Item Env:UPDATE_SIGNING_CERTIFICATE_PASSWORD -ErrorAction SilentlyContinue
}
```

Store that base64 SPKI output in the `UPDATE_MANIFEST_PUBLIC_KEY` repository variable and store the HTTPS origin in `UPDATE_BASE_URL`. Tagged releases require this URL+key pair and fail when either member is absent. `UPDATE_PREFIX` is optional and defaults to `cloudflare-r2-uploader`.

## Build and publish

Run this from the repository root, replacing the bucket and public domain. Keep the caller-side `try`/`finally`: PowerShell parameter binding occurs before the publisher script body, so an unknown or malformed parameter can prevent the script itself from consuming the password.

```powershell
$passwordVariableName = 'UPDATE_SIGNING_CERTIFICATE_PASSWORD'
$securePassword = Read-Host 'PFX password' -AsSecureString
$env:UPDATE_SIGNING_CERTIFICATE_PASSWORD = [Net.NetworkCredential]::new('', $securePassword).Password
$publisherParameters = @{
  Version = '1.1.0'
  BucketName = 'cloudflare-r2-uploader-updates'
  PublicBaseUrl = 'https://downloads.example.com'
  SigningCertificatePath = 'C:\private\update-signing.pfx'
  SigningCertificatePasswordEnvironmentVariable = $passwordVariableName
  ReleaseNotes = 'Improved downloads and fixed update checks.'
}
try {
  .\deploy\Publish-CloudflareUpdate.ps1 @publisherParameters
}
finally {
  Remove-Item Env:UPDATE_SIGNING_CERTIFICATE_PASSWORD -ErrorAction SilentlyContinue
}
```

The script:

- builds the single setup EXE with `update-source.json` embedded in its application payload;
- snapshots the installer once, then uploads it to the content-addressed key `cloudflare-r2-uploader/releases/1.1.0/<sha256>-<installer-name>`;
- creates, signs, and uploads `cloudflare-r2-uploader/manifest.json` last;
- sets the versioned installer to immutable caching and the manifest to no-store;
- streams each authenticated R2 read-back through Wrangler with a five-minute timeout and an independent exact byte cap, deleting partial files on missing, oversized, failed, or interrupted reads;
- performs a public HTTPS read-back of the installer and manifest, cache-busting the manifest request, and verifies both with the same Core verifier used by the application before reporting success.

For an already-built installer, pass `-SkipBuild -InstallerPath <path>`. The build must have used the same update URL, prefix, and public key; otherwise installed clients will not trust the channel.

## Safe key rotation

To rotate keys without stranding installed clients, first build a transition release with the **new public key** embedded, then publish that transition manifest with the **old PFX** by using `-SkipBuild`. Existing clients can authenticate and install the transition. Only after adoption should the owner update `UPDATE_MANIFEST_PUBLIC_KEY` and sign later manifests with the new PFX. Retain the old key securely until the transition window is complete; never publish an unsigned fallback.

That planned rotation requires the old private key to remain controlled and trustworthy. If the PFX or private key is lost or compromised, immediately suspend publication. The compromised key must not sign another manifest, and installed clients must not trust a release authenticated only by it. Create an independently trusted, manual client release that embeds the new public key and distribute it through an authenticated channel such as a manually verified GitHub Release. Do not resume publication until users have established trust in the replacement client; never use the compromised PFX for a transition release.

Manifest signing authenticates the update metadata and installer digest. It does not replace Authenticode signing of the Windows executable; Authenticode remains a separate defense-in-depth option.

Once parameter binding succeeds, the publisher consumes the named password environment variable: it removes the value before validation and never restores it. The documented caller wrapper also clears it when binding fails. Set it again for a later publication. Never replace an installer at an existing content-addressed key; publish a higher SemVer version. The manifest schema is documented in [update-manifest.schema.json](update-manifest.schema.json).

Release notes are limited to 8,000 Unicode scalar values across the publisher, signer, runtime verifier, and JSON Schema. Malformed surrogate sequences are rejected before child processes or network work.

### Failure after manifest publication

A valid release may already be live if the publisher fails during authenticated or public verification after uploading `manifest.json`. Do not blindly retry, overwrite the content-addressed installer, or create another tag. First download the current manifest through both authenticated Wrangler access and a cache-busted public HTTPS request, authenticate both copies with the configured public key using the signer's `verify` command, and compare them byte-for-byte. Download the referenced installer through both paths, compare it byte-for-byte with the reviewed installer supplied to this publication (the GitHub Release asset when following the recommended `-SkipBuild` release procedure), then run `verify-installer` against each copy. If all checks agree, treat the release as live even though the publisher returned a failure. If they do not, preserve the evidence, stop publication, and recover with a higher version after correcting the channel rather than overwriting a published object or reusing a tag.

If a custom-domain Cache Rule overrides origin cache headers, exclude `*/manifest.json` from caching or purge that exact URL after publishing. Clients also append a cache-busting query parameter.
