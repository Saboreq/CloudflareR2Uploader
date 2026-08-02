# Cloudflare R2 update channel

The application can check an HTTPS R2 manifest at startup. It shows the available version and release notes, and installs nothing until the user selects **Update now**. The downloaded installer must match both `sizeBytes` and `sha256`.

## One-time Cloudflare setup

1. Install Node.js 20 or newer, then authenticate Wrangler with `npx wrangler@latest login`.
2. Create a dedicated bucket, for example:

   ```powershell
   npx wrangler@latest r2 bucket create cloudflare-r2-uploader-updates
   ```

3. In Cloudflare Dashboard, open **R2 object storage**, select that bucket, open **Settings**, and connect a production custom domain such as `downloads.example.com`. A Cloudflare-managed `r2.dev` URL can be used for testing, but is rate-limited and is not intended for production.
4. Do not put private files in this bucket. The installer and manifest must be publicly readable so installed clients can update without receiving publisher credentials.

## Build and publish

Run this from the repository root, replacing the bucket and public domain:

```powershell
.\deploy\Publish-CloudflareUpdate.ps1 `
  -Version 1.1.0 `
  -BucketName cloudflare-r2-uploader-updates `
  -PublicBaseUrl https://downloads.example.com `
  -ReleaseNotes 'Improved downloads and fixed update checks.'
```

The script:

- builds the single setup EXE with `update-source.json` embedded in its application payload;
- uploads the installer to `cloudflare-r2-uploader/releases/1.1.0/CloudflareR2Uploader-v1.1.0-Setup.exe`;
- creates and uploads `cloudflare-r2-uploader/manifest.json` last;
- sets the versioned installer to immutable caching and the manifest to no-store;
- verifies the public manifest using a cache-busting request.

For an already-built installer, pass `-SkipBuild -InstallerPath <path>`. The build must have used the same `-UpdateBaseUrl` and `-UpdatePrefix`; otherwise installed clients will not know where to check.

Never replace an installer at an existing versioned key. Publish a higher SemVer version. The manifest schema is documented in [update-manifest.schema.json](update-manifest.schema.json).

If a custom-domain Cache Rule overrides origin cache headers, exclude `*/manifest.json` from caching or purge that exact URL after publishing. Clients also append a cache-busting query parameter.
