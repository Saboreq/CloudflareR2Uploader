# Contributing

Development requires Windows 10 or 11, Visual Studio 2022 with the .NET desktop development workload, the .NET Framework 4.8 targeting pack, PowerShell 5.1 or newer, and Git. Release packaging additionally requires Inno Setup 6 or 7.

The application is under `src/CloudflareR2Uploader`, MSTest tests are under `tests/CloudflareR2Uploader.Tests`, build automation is under `build`, and GitHub automation is under `.github/workflows`.

Run the complete local check with:

```powershell
.\build\Build-Release.ps1 -Version 1.0.0-dev
```

To build without packaging, restore and build `CloudflareR2Uploader.sln` in Visual Studio using `Release | Any CPU`. To rerun the compiled tests, use Visual Studio Test Explorer or the VSTest command printed by the release script.

All production and test code must remain compatible with C# 7.3 and .NET Framework 4.8. These are legacy non-SDK projects: every new `.cs` file must be explicitly added to the appropriate `.csproj`. Do not use newer language syntax or convert project formats.

Pull requests should be focused, explain behavior and security impact, keep existing tests passing, and add deterministic tests for changed logic. Include before/after screenshots for visible UI changes, but do not commit credentials, signed URLs, bucket data, local settings, logs, preview-cache content, `bin`, `obj`, or `dist` output. Use synthetic object keys in issues and tests. Keep commits reviewable and avoid unrelated formatting churn.
