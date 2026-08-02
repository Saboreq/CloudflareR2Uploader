# Releasing

Release tags must be `vMAJOR.MINOR.PATCH` or a SemVer prerelease such as `v1.2.3-beta.1`.

Before tagging, run:

```powershell
.\build\Build-Release.ps1 -Version 1.2.3
```

Confirm that all tests pass, inspect the Inno Setup EXE under `dist`, confirm that `dist` contains no second release file, and verify the working tree did not acquire an `AssemblyInfo.cs` change. Push the reviewed commit, then create and push the tag:

```powershell
git tag v1.2.3
git push origin v1.2.3
```

The tagged-release workflow installs Inno Setup, validates the tag, restores, builds, tests, validates the staged payload, compiles the installer, then creates a GitHub Release containing only the setup EXE. Prerelease suffixes produce a prerelease. Set the repository variable `UPDATE_BASE_URL` to the HTTPS public R2/custom-domain base URL before publishing update-enabled builds; `UPDATE_PREFIX` is optional and defaults to `cloudflare-r2-uploader`.

Inspect and verify a downloaded installer on Windows:

```powershell
(Get-FileHash .\CloudflareR2Uploader-v1.2.3-Setup.exe -Algorithm SHA256).Hash.ToLowerInvariant()
```

After the GitHub Release, publish the same installer to the R2 update channel with `deploy\Publish-CloudflareUpdate.ps1 -SkipBuild`. The versioned installer is uploaded first and `manifest.json` last. See [the Cloudflare update guide](../deploy/README.md).

If a release fails, fix the cause and create a new tag after confirming no release was published. Do not silently overwrite an existing release, and do not reuse a published tag without a documented exceptional reason.
