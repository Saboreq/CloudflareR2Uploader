# Third-party notices

Cloudflare R2 Uploader includes or directly depends on the following external components. Copyright remains with each component's contributors. The repository does not currently declare an application license of its own.

| Package | Version | Purpose | Project | License |
| --- | ---: | --- | --- | --- |
| AWSSDK.S3 | 3.7.511.8 | Cloudflare R2 S3-compatible API client | https://github.com/aws/aws-sdk-net | Apache-2.0 |
| Microsoft.Web.WebView2 | 1.0.4078.44 | Local PDF and supported rich preview hosting | https://github.com/MicrosoftEdge/WebView2Feedback | BSD-3-Clause |
| MetadataExtractor | 2.9.3 | Safe image and document metadata extraction | https://github.com/drewnoakes/metadata-extractor-dotnet | Apache-2.0 |
| Newtonsoft.Json | 13.0.4 | JSON preview parsing and formatting | https://github.com/JamesNK/Newtonsoft.Json | MIT |
| CommunityToolkit.Mvvm | 8.4.0 | Observable state and command generation for WPF view models | https://github.com/CommunityToolkit/dotnet | MIT |
| Microsoft.Extensions.DependencyInjection | 10.0.0 | Application composition and service lifetime management | https://github.com/dotnet/runtime | MIT |
| Inter | 4.1 | Embedded interface typeface used by the Paper reference design | https://github.com/rsms/inter | SIL Open Font License 1.1 |
| MSTest.TestFramework | 3.6.4 | Automated test framework | https://github.com/microsoft/testfx | MIT |
| MSTest.TestAdapter | 3.6.4 | Visual Studio test discovery and execution | https://github.com/microsoft/testfx | MIT |

## Build and packaging tools

| Component | Purpose | Project URL | License/status |
| --- | --- | --- | --- |
| Inno Setup 6 or 7 | Compiles the Windows setup EXE; it is not bundled as an application runtime dependency | https://jrsoftware.org/isinfo.php | Inno Setup license; review commercial licensing terms where applicable |

Release packages preserve runtime companion files required by WebView2. Package license texts and notices remain available from the linked upstream projects and NuGet packages.

The retained compatibility-only WinForms project still references Ookii.Dialogs.WinForms, Fody, and Costura.Fody. Those packages are not part of the .NET 10 WPF release payload and will leave the repository when the compatibility project is removed.
