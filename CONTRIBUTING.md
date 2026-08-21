# Contributing

Development requires Windows 10 or 11, the .NET 10 SDK with Windows desktop targeting, and Git. Use Visual Studio 2026 (18.0+) with the .NET desktop development workload, or use the .NET 10 CLI from another editor such as VS Code. Release packaging and the security harness require PowerShell 7+ and the reviewed Inno Setup 6.7.1 toolchain.

`CloudflareR2Uploader.sln` is the only supported solution. Its SDK-style project graph contains the Core, Infrastructure.R2, Platform.Windows, and WPF application layers, the UpdateSigner tool, and five .NET 10 MSTest projects. The WPF project under `src/CloudflareR2Uploader.Wpf` is the only desktop frontend.

Run the normal developer checks from the repository root:

```powershell
dotnet restore .\CloudflareR2Uploader.sln
dotnet build .\CloudflareR2Uploader.sln -c Release --no-restore
dotnet test .\CloudflareR2Uploader.sln -c Release --no-build
```

Run the same build, tests, WPF publish, payload validation, and installer packaging used by CI with:

```powershell
.\build\Build-Release.ps1 -Version 1.0.0-dev
```

The release script must remain the canonical packaging entry point. Keep the PowerShell 7 security harness after the versioned build in both workflows, keep workflow permissions least-privilege, and pin external build actions and tools to reviewed immutable versions.

Pull requests should be focused, explain behavior and security impact, and add deterministic tests for changed logic. Include before/after screenshots for visible UI changes. Preserve settings, credential, and multipart-state migration readers when changing current schemas; those readers support existing installations even though only the .NET 10 WPF application is built.

Never commit R2 credentials, signing private keys or PFX files, signing passwords, signed URLs, bucket data, local settings, logs, preview-cache content, generated manifests, `bin`, `obj`, `artifacts`, or `dist` output. Use ephemeral keys and synthetic object data in tests. Keep commits reviewable and avoid unrelated formatting or dependency churn.
