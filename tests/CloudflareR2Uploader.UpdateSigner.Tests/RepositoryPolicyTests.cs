using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class RepositoryPolicyTests
    {
        [TestMethod]
        public void ApplicationLicense_IsExactMitTextAndRequiredInTheInstallerPayload()
        {
            const string expectedLicense = """
MIT License

Copyright (c) 2026 Saboreq

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
""";

            string licensePath = RepositoryPath("LICENSE");
            Assert.IsTrue(File.Exists(licensePath), "The application MIT license is missing.");
            Assert.AreEqual(
                NormalizeNewlines(expectedLicense).TrimEnd(),
                NormalizeNewlines(File.ReadAllText(licensePath, Encoding.UTF8)).TrimEnd());

            string build = ReadRepositoryFile("build", "Build-Release.ps1");
            CollectionAssert.Contains(ExtractPowerShellArray(build, "$requiredInputs = @("), "'LICENSE'");
            CollectionAssert.Contains(ExtractPowerShellArray(build, "$expected = @("), "'LICENSE'");
            StringAssert.Contains(
                build,
                "Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination $stagingDirectory");
            Assert.IsFalse(
                build.Contains("if (Test-Path -LiteralPath $licensePath", StringComparison.Ordinal),
                "A release must fail closed rather than silently omit the application license.");

            string installer = ReadRepositoryFile("installer", "CloudflareR2Uploader.iss");
            StringAssert.Contains(installer, "Source: \"{#PayloadDir}\\*\"");
        }

        [TestMethod]
        public void Workflows_UseImmutableActionsAndReviewedBuildToolVersions()
        {
            var expectedActions = new Dictionary<string, (string Sha, string Comment)>(StringComparer.Ordinal)
            {
                ["actions/checkout"] = ("d23441a48e516b6c34aea4fa41551a30e30af803", "v6"),
                ["actions/setup-dotnet"] = ("26b0ec14cb23fa6904739307f278c14f94c95bf1", "v5"),
                ["actions/upload-artifact"] = ("043fb46d1a93c77aae656e7c1c64a875d1fc6a0a", "v7")
            };

            string workflowsDirectory = RepositoryPath(".github", "workflows");
            string[] workflowPaths = Directory.EnumerateFiles(workflowsDirectory, "*", SearchOption.AllDirectories)
                .Where(path => string.Equals(Path.GetExtension(path), ".yml", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetExtension(path), ".yaml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            Assert.IsTrue(workflowPaths.Length > 0, "No GitHub Actions workflows were found.");

            foreach (string workflowPath in workflowPaths)
            {
                AssertWorkflowPolicy(File.ReadAllText(workflowPath), RelativePath(workflowPath), expectedActions);
            }

            AssertPolicyRejectsMutation(
                "name: mutant\njobs:\n  test:\n    steps:\n      - uses: vendor/action@v1 # v1\n",
                "vendor/action@v1");
            AssertPolicyRejectsMutation(
                "name: mutant\njobs:\n  test:\n    steps:\n      - uses: actions/checkout@d23441a48e516b6c34aea4fa41551a30e30af803 # v6\n      - run: .\\build\\Build-Release.ps1\n      - run: choco install innosetup --version=6.7.1 --no-progress -y\n      - run: choco install innosetup -y\n",
                "second unpinned Inno install command");
            AssertPolicyRejectsMutation(
                "name: mutant\njobs:\n  test:\n    steps:\n      - { uses: vendor/action@v1 }\n",
                "flow-style action mapping");
            AssertPolicyRejectsMutation(
                "name: mutant\njobs:\n  reusable:\n    uses: vendor/repository/.github/workflows/build.yml@v1 # v1\n",
                "unpinned external reusable workflow");
            AssertPolicyRejectsMutation(
                "name: mutant\njobs:\n  reusable:\n    uses: vendor/repository/.github/workflows/build.yml@0000000000000000000000000000000000000000 # v1\n",
                "unapproved pinned external reusable workflow");
            AssertPolicyRejectsMutation(
                "name: mutant\njobs:\n  test:\n    steps:\n      - uses: actions/checkout@D23441A48E516B6C34AEA4FA41551A30E30AF803 # v6\n",
                "uppercase action SHA");
            AssertWorkflowPolicy(
                "name: local\njobs:\n  test:\n    steps:\n      - uses: ./build/local-action\n      - run: .\\build\\Build-Release.ps1\n      - run: choco install innosetup --version=6.7.1 --no-progress -y\n",
                "local action fixture",
                expectedActions);
            AssertWorkflowPolicy(
                "name: docs\njobs:\n  lint:\n    steps:\n      - uses: actions/checkout@d23441a48e516b6c34aea4fa41551a30e30af803 # v6\n      - run: dotnet format --verify-no-changes\n",
                "non-packaging workflow fixture",
                expectedActions);
            AssertWorkflowPolicy(
                "name: continued\njobs:\n  package:\n    steps:\n      - run: .\\build\\Build-Release.ps1\n      - run: |\n          choco install `\n            \"innosetup\" `\n            --version \"6.7.1\" `\n            --no-progress -y\n",
                "quoted continued approved Inno fixture",
                expectedActions);
            AssertWorkflowPolicy(
                "name: quoted\njobs:\n  'package-job':\n    steps:\n      - 'uses': ./build/local-action\n      - run: |\n          .\\build\\Build-Release.ps1\n          & \"choco.exe\" install \"innosetup\" --version \"6.7.1\" --no-progress -y\n",
                "quoted job, local action, and executable fixture",
                expectedActions);
            AssertWorkflowPolicy(
                "name: scalar\njobs:\n  lint:\n    steps:\n      - run: |\n          $payload = '{\"uses\":\"text only\"}'\n          Write-Output $payload\n",
                "run-block JSON fixture",
                expectedActions);
            AssertWorkflowPolicy(
                "name: inline-json\njobs:\n  lint:\n    steps:\n      - run: '$payload = ''{\"uses\":\"text only\"}''; Write-Output $payload'\n",
                "inline run JSON fixture",
                expectedActions);
            AssertPolicyRejectsMutation(
                "name: mutant\njobs:\n  package:\n    steps:\n      - run: .\\build\\Build-Release.ps1\n      - run: |\n          choco install `\n            'innosetup' `\n            --version=6.7.2 `\n            --no-progress -y\n",
                "continued wrong Inno version");
            AssertPolicyRejectsMutation(
                "name: mutant\njobs:\n  package:\n    steps:\n      - run: .\\build\\Build-Release.ps1\n      - run: |\n          & 'chocolatey' install `\n            'innosetup' `\n            --version '6.7.2' --no-progress -y\n",
                "quoted executable and continued wrong Inno version");
            AssertPolicyRejectsMutation(
                "name: mutant\njobs:\n  package:\n    steps:\n      - run: .\\build\\Build-Release.ps1\n      - run: choco install innosetup --version=6.7.1 --no-progress -y\n      - run: '& \"choco.exe\" install \"innosetup\" --version \"6.7.1\" --no-progress -y'\n",
                "quoted duplicate Inno install");
            AssertPolicyRejectsMutation(
                "name: mutant\njobs:\n  package:\n    steps:\n      - run: .\\build\\Build-Release.ps1\n      - run: '& \"choco.exe\" install \"innosetup\" -y'\n",
                "quoted unversioned Inno install");
            AssertPolicyRejectsMutation(
                "name: mutant\njobs:\n  package:\n    steps: [{ uses: vendor/action@v1 }]\n",
                "flow sequence containing a structural mapping");

            foreach (string path in EnumerateRepositoryFiles(".github", "deploy", "docs"))
            {
                if (RelativePath(path).StartsWith("docs" + Path.DirectorySeparatorChar + "superpowers", StringComparison.Ordinal))
                    continue;
                string content = File.ReadAllText(path);
                foreach (Match match in Regex.Matches(content, @"wrangler@([^\s`'""\]]+)", RegexOptions.CultureInvariant))
                    Assert.AreEqual("4.120.0", match.Groups[1].Value, RelativePath(path));
            }
        }

        [TestMethod]
        public void WorkflowPolicy_RejectsStructuralFlowCollectionsButAcceptsQuotedFlowText()
        {
            AssertPolicyRejectsMutation(
                "name: mutant\njobs:\n  package:\n    steps: [uses: vendor/action@v1]\n",
                "flow sequence containing an implicit structural mapping");
            AssertWorkflowPolicy(
                "name: quoted-flow-text\njobs:\n  lint:\n    steps:\n      - run: Write-Output '[{\"uses\":\"text only\"}]'\n",
                "quoted inline JSON text fixture",
                ExpectedReviewedActions());
            AssertWorkflowPolicy(
                "name: hashtable\njobs:\n  lint:\n    steps:\n      - run: Write-Output @{ Name = 'value' }\n",
                "inline PowerShell hashtable fixture",
                ExpectedReviewedActions());
        }

        [TestMethod]
        public void WorkflowPolicy_GovernsEscapedAndExplicitYamlMappingKeys()
        {
            AssertPolicyRejectsMutation(
                "name: escaped-uses\njobs:\n  test:\n    steps:\n      - \"\\u0075ses\": vendor/action@v1 # v1\n",
                "escaped uses key");
            AssertPolicyRejectsMutation(
                "name: escaped-run\njobs:\n  package:\n    steps:\n      - run: .\\build\\Build-Release.ps1\n" +
                    "      - run: choco install innosetup --version=6.7.1 --no-progress -y\n" +
                    "      - \"\\u0072un\": choco install innosetup --version=6.7.2 --no-progress -y\n",
                "escaped run key");
            AssertPolicyRejectsMutation(
                "name: explicit-uses\njobs:\n  test:\n    steps:\n      - ? uses\n        : vendor/action@v1 # v1\n",
                "explicit uses mapping");
            AssertPolicyRejectsMutation(
                "name: explicit-run\njobs:\n  package:\n    steps:\n      - run: .\\build\\Build-Release.ps1\n" +
                    "      - run: choco install innosetup --version=6.7.1 --no-progress -y\n" +
                    "      - ? run\n        : choco install innosetup --version=6.7.2 --no-progress -y\n",
                "explicit run mapping");
        }

        [TestMethod]
        public void WorkflowPolicy_NormalizesPowerShellBarewordBacktickEscapes()
        {
            foreach ((string hidden, string description) in new[]
            {
                ("ch`oco install innosetup --version=6.7.2 --no-progress -y", "escaped Chocolatey executable"),
                ("choco install inno`setup --version=6.7.2 --no-progress -y", "escaped Inno package")
            })
            {
                AssertPolicyRejectsMutation(
                    "name: escaped-powershell\njobs:\n  package:\n    steps:\n      - run: .\\build\\Build-Release.ps1\n" +
                        "      - run: choco install innosetup --version=6.7.1 --no-progress -y\n" +
                        "      - run: " + hidden + "\n",
                    description);
            }
        }

        [TestMethod]
        public void WorkflowPolicy_RecognizesEveryValidYamlBlockScalarHeader()
        {
            foreach (string scalarHeader in new[] { "|", "|-", ">", "|2", "|2-", "|-2", ">2", ">+2", ">2+" })
            {
                AssertPolicyRejectsMutation(
                    "name: scalar-mutant\njobs:\n  package:\n    steps:\n      - run: " + scalarHeader +
                        "\n          .\\build\\Build-Release.ps1\n          choco install innosetup --version=6.7.2 --no-progress -y\n",
                    "wrong Inno command hidden by valid " + scalarHeader + " block scalar header");
                AssertWorkflowPolicy(
                    "name: benign-scalar\njobs:\n  lint:\n    steps:\n      - run: " + scalarHeader +
                        "\n          Write-Output '[{\"uses\":\"text only\"}]'\n",
                    "benign " + scalarHeader + " block scalar fixture",
                    ExpectedReviewedActions());
            }
            AssertPolicyRejectsMutation(
                "name: invalid-scalar\njobs:\n  lint:\n    steps:\n      - run: |0\n          Write-Output 'not a valid YAML block scalar header'\n",
                "invalid zero indentation block scalar header");

            foreach (string scalarHeader in new[]
            {
                "&payload |", "! |", "!<tag:yaml.org,2002:str> |2-", "&payload !!str >+2",
                "!!str &payload |-2"
            })
            {
                AssertPolicyRejectsMutation(
                    "name: property-scalar-mutant\njobs:\n  package:\n    steps:\n      - run: " + scalarHeader +
                        "\n          .\\build\\Build-Release.ps1\n          choco install innosetup --version=6.7.2 --no-progress -y\n",
                    "wrong Inno command hidden by property-bearing " + scalarHeader + " block scalar header");
                AssertWorkflowPolicy(
                    "name: property-scalar\njobs:\n  package:\n    steps:\n      - run: " + scalarHeader +
                        "\n          .\\build\\Build-Release.ps1\n          choco install innosetup --version=6.7.1 --no-progress -y\n",
                    "approved property-bearing " + scalarHeader + " block scalar fixture",
                    ExpectedReviewedActions());
            }
            AssertWorkflowPolicy(
                "name: property-text\njobs:\n  lint:\n    steps:\n      - run: Write-Output '&payload |'\n",
                "anchor-like script text fixture",
                ExpectedReviewedActions());
            AssertPolicyRejectsMutation(
                "name: alias-mutant\njobs:\n  package:\n    steps:\n      - run: *payload\n",
                "unsupported run scalar alias");
            AssertPolicyRejectsMutation(
                "name: step-alias-mutant\njobs:\n  package:\n    steps:\n      - &inno-step\n        run: choco install innosetup --version=6.7.1 --no-progress -y\n" +
                    "      - run: .\\build\\Build-Release.ps1\n      - *inno-step\n",
                "step alias that duplicates an approved Inno install at runtime");
        }

        [TestMethod]
        public void WorkflowPolicy_RejectsEveryAlternateChocolateyInnoOperation()
        {
            foreach ((string command, string description) in new[]
            {
                ("cinst innosetup --version=6.7.1 --no-progress -y", "cinst install alias"),
                ("choco i innosetup --version=6.7.1 --no-progress -y", "choco i operation alias"),
                ("choco upgrade innosetup --version=6.7.1 --no-progress -y", "choco upgrade operation"),
                ("choco up innosetup --version=6.7.1 --no-progress -y", "choco up operation alias"),
                ("cup innosetup --version=6.7.1 --no-progress -y", "cup upgrade alias"),
                ("& \"cinst.exe\" \"innosetup\" --version \"6.7.1\" --no-progress -y", "quoted cinst executable and package"),
                ("& \"choco.exe\" \"up\" \"innosetup\" --version \"6.7.1\" --no-progress -y", "quoted choco upgrade alias"),
                ("choco install innosetup --version=6.7.1 --no-progress -y --source https://packages.example.test", "alternate Chocolatey source"),
                ("& 'C:\\ProgramData\\chocolatey\\bin\\choco.exe' install innosetup --version=6.7.1 --no-progress -y", "full executable path"),
                ("& '.\\tools\\choco.exe' install innosetup --version=6.7.1 --no-progress -y", "relative executable path"),
                ("choco install innosetup.install --version=6.7.1 --no-progress -y", "alternate package identifier"),
                ("choco install innosetup.portable --version=6.7.1 --no-progress -y", "alternate package identifier suffix"),
                ("& 'C:\\tools\\cinst.exe' innosetup.install --version=6.7.1 --no-progress -y", "path-qualified alias and alternate package identifier")
            })
            {
                AssertPolicyRejectsMutation(
                    "name: chocolatey-mutant\njobs:\n  package:\n    steps:\n      - run: .\\build\\Build-Release.ps1\n" +
                        "      - run: choco install innosetup --version=6.7.1 --no-progress -y\n" +
                        "      - run: '" + command.Replace("'", "''", StringComparison.Ordinal) + "'\n",
                    description + " hidden beside the approved install");
            }
        }

        [TestMethod]
        public void Dependabot_IsWeeklyBoundedAndUsesTheWarsawMaintenanceWindow()
        {
            const string expected = """
version: 2
updates:
  - package-ecosystem: nuget
    directory: /
    schedule:
      interval: weekly
      day: monday
      time: "06:00"
      timezone: Europe/Warsaw
    open-pull-requests-limit: 5
    groups:
      nuget-minor-and-patch:
        update-types: [minor, patch]
  - package-ecosystem: github-actions
    directory: /
    schedule:
      interval: weekly
      day: monday
      time: "06:00"
      timezone: Europe/Warsaw
    open-pull-requests-limit: 3
""";

            Assert.AreEqual(
                NormalizeNewlines(expected).TrimEnd(),
                NormalizeNewlines(ReadRepositoryFile(".github", "dependabot.yml")).TrimEnd());
        }

        [TestMethod]
        public void SupportedRepositoryDocumentation_IsCanonicalDotNet10WpfOnly()
        {
            string[] documents =
            {
                "README.md",
                "CONTRIBUTING.md",
                "SECURITY.md",
                "THIRD-PARTY-NOTICES.md",
                Path.Combine("docs", "RELEASING.md"),
                Path.Combine("docs", "WPF_DESIGN_MAPPING.md"),
                Path.Combine("deploy", "README.md")
            };
            string documentation = string.Join(
                "\n",
                documents.Select(path => ReadRepositoryFile(path.Split(Path.DirectorySeparatorChar))));

            foreach (string prohibited in new[]
            {
                @"CloudflareR2Uploader\.Wpf\.sln",
                @"\.NET Framework 4\.8",
                @"C# 7\.3",
                @"\blinked files?\b",
                @"\bcompatibility (?:project|projects|build|gate)\b",
                @"\bOokii(?:\.Dialogs\.WinForms)?\b",
                @"\bFody\b",
                @"\bCostura(?:\.Fody)?\b",
                @"\bboth front ends\b",
                @"\bdual[- ]frontend\b"
            })
            {
                Assert.IsFalse(
                    Regex.IsMatch(documentation, prohibited, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                    "Stale supported-repository claim: " + prohibited);
            }

            string readme = ReadRepositoryFile("README.md");
            foreach (string required in new[]
            {
                ".NET 10", "WPF", "CloudflareR2Uploader.sln",
                "src/CloudflareR2Uploader.Core", "src/CloudflareR2Uploader.Infrastructure.R2",
                "src/CloudflareR2Uploader.Platform.Windows", "src/CloudflareR2Uploader.Wpf",
                "tools/CloudflareR2Uploader.UpdateSigner", "build/Build-Release.ps1"
            })
                StringAssert.Contains(readme, required);

            string sourceComments = string.Join(
                "\n",
                EnumerateRepositoryFiles("src")
                    .Where(IsLiveSourceFile)
                    .Select(File.ReadAllText));
            Assert.IsFalse(
                Regex.IsMatch(sourceComments, @"\bWinForms\b|\.NET Framework 4\.8|\bboth front ends\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                "Live source comments must describe the supported WPF architecture; migration history should use release-era terms.");
        }

        [TestMethod]
        public void GitHubMarkdown_DescribesTheDotNet10SdkWpfArchitecture()
        {
            string githubDirectory = RepositoryPath(".github");
            string mutationDirectory = Path.Combine(githubDirectory, "ISSUE_TEMPLATE");
            Directory.CreateDirectory(mutationDirectory);
            try
            {
                foreach ((string name, string content) in new[]
                {
                    ("framework", "Report a supported .NET Framework 4.7.2 problem."),
                    ("winforms", "WinForms remains supported for current releases."),
                    ("winforms-current", "WinForms is the current production desktop client."),
                    ("frontends", "Both front ends are supported by the current compatibility build."),
                    ("legacy-current", "The legacy desktop client remains active in current production releases."),
                    ("compatibility-current", "The compatibility desktop client remains maintained and active.")
                })
                {
                    string mutationPath = Path.Combine(
                        mutationDirectory,
                        "bug_report-policy-mutation-" + name + "-" + Guid.NewGuid().ToString("N") + ".md");
                    try
                    {
                        File.WriteAllText(mutationPath, content, Encoding.UTF8);
                        string[] discoveredPaths = Directory.EnumerateFiles(githubDirectory, "*.md", SearchOption.AllDirectories)
                            .OrderBy(path => path, StringComparer.Ordinal)
                            .ToArray();
                        CollectionAssert.Contains(
                            discoveredPaths,
                            mutationPath,
                            ".github/ISSUE_TEMPLATE/*.md must be included in the Markdown policy corpus.");
                        Assert.ThrowsException<AssertFailedException>(
                            () => AssertNoStaleGitHubMarkdown(discoveredPaths),
                            "The Markdown policy accepted a stale nested issue template: " + content);
                    }
                    finally
                    {
                        if (File.Exists(mutationPath)) File.Delete(mutationPath);
                    }
                }

                string genericTemplate = Path.Combine(
                    mutationDirectory,
                    "generic-security-question-" + Guid.NewGuid().ToString("N") + ".md");
                string architectureTemplate = Path.Combine(
                    mutationDirectory,
                    "architecture-" + Guid.NewGuid().ToString("N") + ".md");
                try
                {
                    File.WriteAllText(genericTemplate, "Describe the issue without including credentials.", Encoding.UTF8);
                    AssertNoStaleGitHubMarkdown(new[] { genericTemplate });
                    File.WriteAllText(architectureTemplate, "Describe the desktop architecture.", Encoding.UTF8);
                    Assert.ThrowsException<AssertFailedException>(
                        () => AssertNoStaleGitHubMarkdown(new[] { architectureTemplate }),
                        "An architecture-bearing GitHub document omitted the canonical architecture.");
                }
                finally
                {
                    if (File.Exists(genericTemplate)) File.Delete(genericTemplate);
                    if (File.Exists(architectureTemplate)) File.Delete(architectureTemplate);
                }
            }
            finally
            {
                if (Directory.Exists(mutationDirectory) && !Directory.EnumerateFileSystemEntries(mutationDirectory).Any())
                    Directory.Delete(mutationDirectory);
            }

            string[] templatePaths = Directory.EnumerateFiles(githubDirectory, "*.md", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            Assert.IsTrue(templatePaths.Length > 0, "No GitHub Markdown files were found.");
            AssertNoStaleGitHubMarkdown(templatePaths);
        }

        [TestMethod]
        public void ContributorTooling_UsesANet10CompatibleIde()
        {
            string documentation = ReadRepositoryFile("CONTRIBUTING.md") + "\n" + ReadRepositoryFile("README.md");
            StringAssert.Contains(documentation, "Visual Studio 2026");
            StringAssert.Contains(documentation, "18.0+");
            Assert.IsFalse(
                documentation.Contains("Visual Studio 2022", StringComparison.OrdinalIgnoreCase),
                "The supported tooling must not direct contributors to an IDE that predates .NET 10.");
        }

        [TestMethod]
        public void SigningGuides_CoverSecretHandlingTrustConfigurationAndRotationOrdering()
        {
            string deploy = ReadRepositoryFile("deploy", "README.md");
            string releasing = ReadRepositoryFile("docs", "RELEASING.md");
            string combined = deploy + "\n" + releasing;

            foreach (string required in new[]
            {
                "password-protected PFX", "outside the repository", "UPDATE_SIGNING_CERTIFICATE_PASSWORD",
                "export-public-key", "UPDATE_MANIFEST_PUBLIC_KEY", "UPDATE_BASE_URL",
                "Publish-CloudflareUpdate.ps1", "try {", "finally {", "key rotation",
                "new public key", "old PFX", "Authenticode", "does not replace"
            })
                StringAssert.Contains(combined, required);

            Assert.IsFalse(
                Regex.IsMatch(
                    combined,
                    @"UPDATE_SIGNING_CERTIFICATE_PASSWORD\s*=\s*['""][^'""]+['""]",
                    RegexOptions.CultureInvariant),
                "Documentation must not contain a literal signing password example.");

            foreach (string required in new[]
            {
                "lost or compromised", "suspend publication", "must not sign", "must not trust",
                "independently trusted", "manual client release", "resume publication"
            })
                StringAssert.Contains(combined, required);
        }

        [TestMethod]
        public void ReleaseTooling_RequiresPowerShell7()
        {
            string documentation = string.Join(
                "\n",
                ReadRepositoryFile("README.md"),
                ReadRepositoryFile("CONTRIBUTING.md"),
                ReadRepositoryFile("docs", "RELEASING.md"),
                ReadRepositoryFile("deploy", "README.md"));
            StringAssert.Contains(documentation, "PowerShell 7");
            Assert.IsFalse(
                Regex.IsMatch(documentation, @"PowerShell\s+5\.1|PowerShell\s+5\.1\s+or newer", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                "The release/security tooling uses APIs that are not available in Windows PowerShell 5.1.");
        }

        [TestMethod]
        public void ReleaseBuild_ValidatesExactlyInnoSetup671BeforeChildWork()
        {
            string build = ReadRepositoryFile("build", "Build-Release.ps1");
            StringAssert.Contains(build, "$requiredInnoSetupVersion = '6.7.1'");
            StringAssert.Contains(build, ". (Join-Path $PSScriptRoot 'InnoSetupRegistration.ps1')");
            StringAssert.Contains(build, "Resolve-InnoSetupCompilerRegistration");
            Assert.IsFalse(build.Contains("GetVersionInfo($InnoSetupCompiler)", StringComparison.Ordinal), "Official Inno Setup 6.7.1 does not expose its package version through ISCC.exe file metadata.");
            string registration = ReadRepositoryFile("build", "InnoSetupRegistration.ps1");
            StringAssert.Contains(registration, "Inno Setup 6_is1");
            StringAssert.Contains(registration, "AppId");
            StringAssert.Contains(registration, "DisplayVersion");
            StringAssert.Contains(registration, "InstallLocation");
            StringAssert.Contains(registration, "Registry32");
            Assert.IsFalse(registration.Contains("Registry64", StringComparison.Ordinal), "The official 6.7.1 package is registered only in the 32-bit uninstall view.");
            StringAssert.Contains(registration, "CurrentUser");
            StringAssert.Contains(registration, "LocalMachine");
            StringAssert.Contains(registration, "Test-InnoSetupRegistrationCandidate");
            StringAssert.Contains(registration, "Select-InnoSetupCompilerCandidate");
            int rawInstallLocationCheck = registration.IndexOf(
                "IsPathFullyQualified([string]$Candidate.InstallLocation)",
                StringComparison.Ordinal);
            int installLocationCanonicalization = registration.IndexOf(
                "$canonicalInstallLocation = [System.IO.Path]::GetFullPath",
                StringComparison.Ordinal);
            Assert.IsTrue(
                rawInstallLocationCheck >= 0 && rawInstallLocationCheck < installLocationCanonicalization,
                "The raw REG_SZ InstallLocation must be fully qualified before canonicalization.");
            StringAssert.Contains(registration, "Test-InnoSetupPathChain");
            StringAssert.Contains(registration, "ReparsePoint");
            StringAssert.Contains(registration, "InstallPathTrusted");
            StringAssert.Contains(registration, "CompilerFileTrusted");
            StringAssert.Contains(registration, "Test-InnoSetupLocalDrivePath");
            foreach (string rejectedPathForm in new[] { @"\\server\share", @"\\?\", @"\.\", @"\??\" })
                StringAssert.Contains(registration, rejectedPathForm);
            Assert.IsTrue(
                build.IndexOf("Resolve-InnoSetupCompilerRegistration", StringComparison.Ordinal) <
                    build.IndexOf("& $dotnet.Source build $signerProject", StringComparison.Ordinal),
                "The selected compiler registration must be validated before the signer build child process.");
            Assert.IsTrue(
                build.IndexOf("Resolve-InnoSetupCompilerRegistration", StringComparison.Ordinal) <
                    build.IndexOf("& $iconGenerator", StringComparison.Ordinal),
                "The selected compiler registration must be validated before packaging child work.");

            string harness = ReadRepositoryFile("tests", "PowerShell", "Test-UpdateReleaseScripts.ps1");
            StringAssert.Contains(harness, "Test-BuildReleaseInnoRegistrationBoundary");
            StringAssert.Contains(harness, "Resolve-InnoSetupCompilerRegistration");
            StringAssert.Contains(harness, "unregistered-inno-compiler");
            foreach (string fixture in new[]
            {
                "HKLM Registry32", "HKCU Registry32", "Registry64 rejection", "wrong version",
                "same-path dedupe", "ambiguous registrations", "explicit disambiguation", "unrelated child key",
                "dot InstallLocation", "relative InstallLocation", "install-directory reparse",
                "compiler reparse", "normal absolute path"
            })
                StringAssert.Contains(harness, fixture);
            foreach (string fixture in new[]
            {
                "UNC InstallLocation", "extended-length device InstallLocation",
                "DOS device InstallLocation", "rooted current-drive InstallLocation",
                "local DOS drive InstallLocation"
            })
                StringAssert.Contains(harness, fixture);
        }

        [TestMethod]
        public void LiveSourceScan_ExcludesOnlyExactWpfTemporaryProjectArtifacts()
        {
            string wpfDirectory = RepositoryPath("src", "CloudflareR2Uploader.Wpf");
            string transientPath = Path.Combine(
                wpfDirectory,
                "CloudflareR2Uploader.Wpf_policytest_wpftmp.csproj");
            string ordinaryPath = Path.Combine(wpfDirectory, "OtherProject_policytest_wpftmp.csproj");
            try
            {
                File.WriteAllText(transientPath, "WinForms transient compiler input", Encoding.UTF8);
                File.WriteAllText(ordinaryPath, "ordinary repository file", Encoding.UTF8);
                string[] enumerated = EnumerateRepositoryFiles("src").ToArray();
                CollectionAssert.DoesNotContain(enumerated, transientPath);
                CollectionAssert.Contains(enumerated, ordinaryPath);
            }
            finally
            {
                if (File.Exists(transientPath)) File.Delete(transientPath);
                if (File.Exists(ordinaryPath)) File.Delete(ordinaryPath);
            }
        }

        [TestMethod]
        public void ThirdPartyNotices_ListTheDirectTestSdkAndExactWranglerLicense()
        {
            string notices = ReadRepositoryFile("THIRD-PARTY-NOTICES.md");
            StringAssert.Contains(notices, "| Microsoft.NET.Test.Sdk | 17.12.0 |");
            StringAssert.Contains(
                notices,
                "| Wrangler 4.120.0 | Publishes and reads back immutable installer and manifest objects | https://github.com/cloudflare/workers-sdk | MIT OR Apache-2.0 |");
            StringAssert.Contains(notices, "Cloudflare R2 Uploader is distributed under the MIT License");
        }

        [TestMethod]
        public void GitIgnore_CoversPrivateSigningMaterialAndGeneratedUpdateScratchOnly()
        {
            string[] patterns = NormalizeNewlines(ReadRepositoryFile(".gitignore"))
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && line[0] != '#')
                .ToArray();

            foreach (string required in new[]
            {
                "deploy/private/", "*.pfx", "*.p12", "*.key", "key.pem", "rsa-key.pem", "update-key.pem",
                "release-key.pem", "private.pem", "private-key.pem", "*-private.pem", "*-private-key.pem", "signing-key.pem",
                "private-signing-key.pem", "production-signing-key.pem",
                "update-signing-key.pem", "release-signing-key.pem", "manifest-signing-key.pem",
                "deploy/signed-manifest*.json", "deploy/signer-scratch/", "deploy/publisher-scratch/"
            })
                CollectionAssert.Contains(patterns, required);

            CollectionAssert.DoesNotContain(patterns, "*.cer");
            CollectionAssert.DoesNotContain(patterns, "*.pem");
            CollectionAssert.DoesNotContain(patterns, "*private*.pem");
            CollectionAssert.DoesNotContain(patterns, "*signing-key*.pem");

            foreach (string privatePath in new[]
            {
                "nested/private.pem", "nested/private-key.pem", "nested/key.pem", "nested/rsa-key.pem", "nested/update-key.pem",
                "nested/release-key.pem", "nested/update-private.pem", "nested/release-private-key.pem",
                "nested/signing-key.pem", "nested/private-signing-key.pem", "nested/production-signing-key.pem",
                "nested/update-signing-key.pem", "nested/release-signing-key.pem"
            })
                Assert.IsTrue(IsIgnoredByGit(privatePath), privatePath + " must be ignored by effective Git policy.");
            foreach (string publicPath in new[]
            {
                "nested/public-key.pem", "nested/public-signing-key.pem", "nested/public-certificate.pem",
                "nested/public-certificate.cer"
            })
                Assert.IsFalse(IsIgnoredByGit(publicPath), publicPath + " must remain trackable.");
        }

        [TestMethod]
        public void TrackedRepositoryContent_ContainsNoPrivatePemBlocks()
        {
            Encoding[] encodings =
            {
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
                new UnicodeEncoding(bigEndian: false, byteOrderMark: true),
                new UnicodeEncoding(bigEndian: true, byteOrderMark: true),
                new UTF32Encoding(bigEndian: false, byteOrderMark: true),
                new UTF32Encoding(bigEndian: true, byteOrderMark: true)
            };
            foreach (string privateKeyType in new[]
            {
                "PRIVATE KEY", "ENCRYPTED PRIVATE KEY", "RSA PRIVATE KEY", "EC PRIVATE KEY",
                "DSA PRIVATE KEY", "OPENSSH PRIVATE KEY"
            })
            {
                foreach (string boundary in new[] { "BEGIN", "END" })
                {
                    string mutationDelimiter = "-----" + boundary + " " + privateKeyType + "-----";
                    foreach (Encoding encoding in encodings)
                    {
                        byte[] encoded = encoding.GetPreamble().Concat(encoding.GetBytes(mutationDelimiter)).ToArray();
                        Assert.ThrowsException<AssertFailedException>(
                            () => AssertNoPrivatePemHeader(Path.Combine("docs", "public-signing-key.pem"), encoded),
                            "A private PEM block hidden behind a public-looking filename was accepted: " +
                                boundary + " " + privateKeyType + " in " + encoding.WebName);
                    }
                }
            }

            AssertNoPrivatePemHeader(
                Path.Combine("docs", "private-key-guidance.md"),
                Encoding.UTF8.GetBytes("Describe BEGIN PRIVATE KEY in prose without a complete armor delimiter."));
            AssertNoPrivatePemHeader(
                Path.Combine("docs", "partial-private-key-guidance.md"),
                Encoding.UTF8.GetBytes("-----BEGIN PRIVATE KEY----"));
            AssertNoPrivatePemHeader(
                Path.Combine("docs", "random-public-key.bin"),
                new byte[] { 0xff, 0xfe, 0x2d, 0x00, 0x42, 0x00, 0x45, 0x00, 0x47 });

            foreach (string path in EnumerateTrackedRepositoryFiles())
                AssertNoPrivatePemHeader(RelativePath(path), File.ReadAllBytes(path));
        }

        private static Dictionary<string, (string Sha, string Comment)> ExpectedReviewedActions()
        {
            return new Dictionary<string, (string Sha, string Comment)>(StringComparer.Ordinal)
            {
                ["actions/checkout"] = ("d23441a48e516b6c34aea4fa41551a30e30af803", "v6"),
                ["actions/setup-dotnet"] = ("26b0ec14cb23fa6904739307f278c14f94c95bf1", "v5"),
                ["actions/upload-artifact"] = ("043fb46d1a93c77aae656e7c1c64a875d1fc6a0a", "v7")
            };
        }

        private static void AssertWorkflowPolicy(
            string workflow,
            string source,
            Dictionary<string, (string Sha, string Comment)> expectedActions)
        {
            WorkflowPolicyDocument document = ReadWorkflowPolicyDocument(workflow, source);
            foreach (WorkflowUsesNode usesNode in document.UsesNodes)
            {
                string reference = usesNode.Reference;
                if (reference.StartsWith("./", StringComparison.Ordinal)) continue;
                int separator = reference.LastIndexOf('@');
                Assert.IsTrue(separator > 0, source + " has an external action without a ref: " + reference);
                string action = reference.Substring(0, separator);
                string sha = reference.Substring(separator + 1);
                Assert.IsTrue(Regex.IsMatch(sha, @"\A[0-9a-f]{40}\z", RegexOptions.CultureInvariant), source + " action is not pinned to a lowercase 40-character SHA: " + reference);
                Assert.IsFalse(string.IsNullOrWhiteSpace(usesNode.Comment), source + " action pin has no human version comment: " + reference);
                Assert.IsTrue(expectedActions.TryGetValue(action, out (string Sha, string Comment) expected), source + " uses an action that is not in the reviewed mapping: " + action);
                Assert.AreEqual(expected.Sha, sha, source + ": " + action);
                Assert.AreEqual(expected.Comment, usesNode.Comment, source + ": " + action);
            }

            foreach (WorkflowPolicyJob job in document.Jobs)
            {
                List<IReadOnlyList<string>> innoCommands = job.RunScripts
                    .SelectMany(FindGovernedChocolateyInnoCommands)
                    .ToList();
                bool invokesReleaseBuild = job.RunScripts.Any(script => Regex.IsMatch(
                    script,
                    @"(?:^|[\\/])Build-Release\.ps1\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
                Assert.AreEqual(
                    invokesReleaseBuild ? 1 : 0,
                    innoCommands.Count,
                    source + " job " + job.Name + " must install Inno Setup exactly once if and only if it invokes Build-Release.ps1.");
                foreach (IReadOnlyList<string> command in innoCommands)
                {
                    Assert.IsTrue(
                        IsApprovedInnoInstallCommand(command),
                        source + " job " + job.Name + " has an unapproved Inno Setup install command: " + string.Join(' ', command));
                }
            }
        }

        private static WorkflowPolicyDocument ReadWorkflowPolicyDocument(string workflow, string source)
        {
            string[] lines = NormalizeNewlines(workflow).Split('\n');
            var document = new WorkflowPolicyDocument();
            WorkflowPolicyJob currentJob = null;
            int? jobsIndent = null;
            int? jobIndent = null;
            int scalarOwnerIndent = -1;
            string scalarKey = null;
            string scalarStyle = null;
            WorkflowPolicyJob scalarJob = null;
            var scalarLines = new List<string>();

            void FinishScalar()
            {
                if (string.Equals(scalarKey, "run", StringComparison.OrdinalIgnoreCase) && scalarJob != null)
                {
                    string separator = scalarStyle != null && scalarStyle[0] == '>' ? " " : "\n";
                    scalarJob.RunScripts.Add(string.Join(separator, scalarLines));
                }
                scalarOwnerIndent = -1;
                scalarKey = null;
                scalarStyle = null;
                scalarJob = null;
                scalarLines.Clear();
            }

            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index];
                int indent = CountYamlIndent(line, source);
                if (scalarKey != null)
                {
                    if (line.Length == 0 || string.IsNullOrWhiteSpace(line) || indent > scalarOwnerIndent)
                    {
                        scalarLines.Add(line.Trim());
                        continue;
                    }
                    FinishScalar();
                }

                string trimmed = line.TrimStart();
                if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
                Match mapping = Regex.Match(
                    line,
                    @"^(?<indent> *)(?:-\s+)?(?<key>[A-Za-z_][A-Za-z0-9_-]*|'[^']+'|""[^""]+"")\s*:\s*(?<value>.*)$",
                    RegexOptions.CultureInvariant);
                if (!mapping.Success)
                {
                    Assert.IsFalse(
                        StartsWithUnsupportedYamlFlowCollection(trimmed) ||
                            StartsWithUnsupportedYamlStructuralIndirection(trimmed) ||
                            StartsWithExplicitYamlMappingIndicator(trimmed),
                        source + " uses unsupported YAML structural syntax: " + trimmed);
                    continue;
                }

                string encodedKey = mapping.Groups["key"].Value;
                Assert.IsFalse(
                    encodedKey.StartsWith('"') && encodedKey.Contains('\\'),
                    source + " uses an escaped YAML mapping key that policy cannot inspect safely.");
                string key = UnquoteYamlScalar(encodedKey);
                SplitYamlValueAndComment(mapping.Groups["value"].Value, out string value, out string comment);
                Assert.IsFalse(
                    StartsWithUnsupportedYamlFlowCollection(value),
                    source + " uses an unsupported flow-style structural mapping: " + trimmed);

                if (string.Equals(key, "jobs", StringComparison.OrdinalIgnoreCase) && indent == 0)
                {
                    Assert.IsNull(jobsIndent, source + " contains multiple jobs: mappings.");
                    Assert.IsTrue(value.Length == 0, source + " uses an unsupported flow-style jobs mapping.");
                    jobsIndent = indent;
                    currentJob = null;
                    continue;
                }

                if (jobsIndent.HasValue && indent <= jobsIndent.Value)
                {
                    currentJob = null;
                    continue;
                }

                if (jobsIndent.HasValue)
                {
                    if (!jobIndent.HasValue) jobIndent = indent;
                    if (indent == jobIndent.Value)
                    {
                        Assert.IsTrue(value.Length == 0, source + " job " + key + " must use a block mapping.");
                        currentJob = new WorkflowPolicyJob(key);
                        document.Jobs.Add(currentJob);
                        continue;
                    }
                }

                if (string.Equals(key, "uses", StringComparison.OrdinalIgnoreCase))
                {
                    Assert.IsNotNull(currentJob, source + " contains uses: outside a job block.");
                    string reference = UnquoteYamlScalar(value);
                    Assert.IsFalse(string.IsNullOrWhiteSpace(reference), source + " contains an empty uses: node.");
                    document.UsesNodes.Add(new WorkflowUsesNode(reference, comment));
                }

                if (TryGetBlockScalarStyle(value, out char blockScalarStyle))
                {
                    scalarOwnerIndent = indent;
                    scalarKey = key;
                    scalarStyle = blockScalarStyle.ToString();
                    scalarJob = currentJob;
                    continue;
                }
                Assert.IsFalse(
                    value.StartsWith('|') || value.StartsWith('>') ||
                        IsPlainYamlPropertyOrAlias(value),
                    source + " uses an unsupported YAML scalar form: " + value);

                if (string.Equals(key, "run", StringComparison.OrdinalIgnoreCase))
                {
                    Assert.IsNotNull(currentJob, source + " contains run: outside a job block.");
                    currentJob.RunScripts.Add(UnquoteYamlScalar(value));
                }
            }
            if (scalarKey != null) FinishScalar();

            Assert.IsNotNull(jobsIndent, source + " does not contain a jobs: mapping.");
            Assert.IsTrue(document.Jobs.Count > 0, source + " does not contain a block-mapped job.");
            return document;
        }

        private static List<IReadOnlyList<string>> FindGovernedChocolateyInnoCommands(string script)
        {
            var commands = new List<IReadOnlyList<string>>();
            foreach (string logicalLine in JoinPowerShellContinuationLines(script))
            {
                List<string> tokens = TokenizePowerShell(logicalLine);
                for (int index = 0; index < tokens.Count; index++)
                {
                    int packageOffset;
                    if (IsChocolateyCommandExecutable(tokens[index]) && index + 1 < tokens.Count &&
                        IsGovernedChocolateyOperation(tokens[index + 1]))
                    {
                        packageOffset = 2;
                    }
                    else if (IsChocolateyImplicitOperationAlias(tokens[index]))
                    {
                        packageOffset = 1;
                    }
                    else
                    {
                        continue;
                    }

                    int end = index + packageOffset;
                    while (end < tokens.Count && !IsPowerShellCommandSeparator(tokens[end])) end++;
                    List<string> command = tokens.GetRange(index, end - index);
                    if (command.Skip(packageOffset).Any(IsGovernedInnoPackage))
                        commands.Add(command);
                    index = end - 1;
                }
            }
            return commands;
        }

        private static bool IsApprovedInnoInstallCommand(IReadOnlyList<string> command)
        {
            if ((command.Count != 6 && command.Count != 7) || !IsApprovedChocolateyExecutable(command[0]) ||
                !string.Equals(command[1], "install", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(command[2], "innosetup", StringComparison.OrdinalIgnoreCase))
                return false;

            int versionOffset;
            if (string.Equals(command[3], "--version=6.7.1", StringComparison.OrdinalIgnoreCase))
            {
                versionOffset = 4;
            }
            else
            {
                if (command.Count != 7 || !string.Equals(command[3], "--version", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(command[4], "6.7.1", StringComparison.Ordinal))
                    return false;
                versionOffset = 5;
            }
            return string.Equals(command[versionOffset], "--no-progress", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(command[versionOffset + 1], "-y", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsChocolateyCommandExecutable(string token)
        {
            string executable = GetPowerShellCommandFileName(token);
            return string.Equals(executable, "choco", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(executable, "choco.exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(executable, "chocolatey", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(executable, "chocolatey.exe", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsApprovedChocolateyExecutable(string token)
        {
            return string.Equals(token, "choco", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(token, "choco.exe", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsChocolateyImplicitOperationAlias(string token)
        {
            string executable = GetPowerShellCommandFileName(token);
            return string.Equals(executable, "cinst", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(executable, "cinst.exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(executable, "cup", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(executable, "cup.exe", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetPowerShellCommandFileName(string token)
        {
            int separator = Math.Max(token.LastIndexOf('\\'), token.LastIndexOf('/'));
            return separator >= 0 ? token.Substring(separator + 1) : token;
        }

        private static bool IsGovernedInnoPackage(string token)
        {
            return string.Equals(token, "innosetup", StringComparison.OrdinalIgnoreCase) ||
                token.StartsWith("innosetup.", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsGovernedChocolateyOperation(string token)
        {
            return string.Equals(token, "install", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(token, "i", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(token, "upgrade", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(token, "up", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPowerShellCommandSeparator(string token)
        {
            return token is ";" or "|" or "||" or "&&";
        }

        private static List<string> TokenizePowerShell(string line)
        {
            var tokens = new List<string>();
            var token = new StringBuilder();
            char quote = '\0';
            void FinishToken()
            {
                if (token.Length == 0) return;
                tokens.Add(token.ToString());
                token.Clear();
            }

            for (int index = 0; index < line.Length; index++)
            {
                char current = line[index];
                if (quote != '\0')
                {
                    if (current == quote)
                    {
                        if (quote == '\'' && index + 1 < line.Length && line[index + 1] == '\'')
                        {
                            token.Append('\'');
                            index++;
                        }
                        else
                        {
                            quote = '\0';
                        }
                    }
                    else if (quote == '"' && current == '`' && index + 1 < line.Length)
                    {
                        token.Append(line[++index]);
                    }
                    else
                    {
                        token.Append(current);
                    }
                    continue;
                }

                if (current is '\'' or '"')
                {
                    quote = current;
                    continue;
                }
                if (char.IsWhiteSpace(current))
                {
                    FinishToken();
                    continue;
                }
                if (current == '#' && (index == 0 || char.IsWhiteSpace(line[index - 1])))
                {
                    FinishToken();
                    break;
                }
                if (current is ';' or '|')
                {
                    FinishToken();
                    string separator = current.ToString();
                    if (index + 1 < line.Length && line[index + 1] == current)
                    {
                        separator += current;
                        index++;
                    }
                    tokens.Add(separator);
                    continue;
                }
                if (current == '&')
                {
                    FinishToken();
                    if (index + 1 < line.Length && line[index + 1] == '&')
                    {
                        tokens.Add("&&");
                        index++;
                    }
                    continue;
                }
                if (current == '`')
                {
                    Assert.IsTrue(
                        index + 1 < line.Length,
                        "A PowerShell escape marker has no following character.");
                    token.Append(line[++index]);
                    continue;
                }
                token.Append(current);
            }
            Assert.AreEqual('\0', quote, "An unterminated PowerShell quote cannot be inspected safely.");
            FinishToken();
            return tokens;
        }

        private static IEnumerable<string> JoinPowerShellContinuationLines(string content)
        {
            string[] lines = NormalizeNewlines(content).Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                string logicalLine = lines[index].Trim();
                while (Regex.IsMatch(logicalLine, @"`\s*\z", RegexOptions.CultureInvariant))
                {
                    Assert.IsTrue(index + 1 < lines.Length, "A PowerShell continuation marker has no following line.");
                    logicalLine = Regex.Replace(logicalLine, @"`\s*\z", string.Empty, RegexOptions.CultureInvariant).TrimEnd();
                    index++;
                    logicalLine += " " + lines[index].Trim();
                }
                yield return logicalLine;
            }
        }

        private static int CountYamlIndent(string line, string source)
        {
            int indent = 0;
            while (indent < line.Length && line[indent] == ' ') indent++;
            Assert.IsFalse(indent < line.Length && line[indent] == '\t', source + " uses tab indentation that policy cannot inspect safely.");
            return indent;
        }

        private static bool StartsWithExplicitYamlMappingIndicator(string value)
        {
            string candidate = value;
            if (candidate.StartsWith("- ", StringComparison.Ordinal))
                candidate = candidate.Substring(2).TrimStart();
            return candidate.Length > 0 &&
                (candidate[0] == '?' || candidate[0] == ':') &&
                (candidate.Length == 1 || char.IsWhiteSpace(candidate[1]));
        }

        private static void SplitYamlValueAndComment(string raw, out string value, out string comment)
        {
            char quote = '\0';
            for (int index = 0; index < raw.Length; index++)
            {
                char current = raw[index];
                if (quote == '\0' && current is '\'' or '"')
                {
                    quote = current;
                    continue;
                }
                if (quote != '\0' && current == quote)
                {
                    if (quote == '\'' && index + 1 < raw.Length && raw[index + 1] == '\'')
                    {
                        index++;
                        continue;
                    }
                    quote = '\0';
                    continue;
                }
                if (quote == '\0' && current == '#' && (index == 0 || char.IsWhiteSpace(raw[index - 1])))
                {
                    value = raw.Substring(0, index).Trim();
                    comment = raw.Substring(index + 1).Trim();
                    return;
                }
            }
            Assert.AreEqual('\0', quote, "An unterminated YAML quote cannot be inspected safely.");
            value = raw.Trim();
            comment = string.Empty;
        }

        private static string UnquoteYamlScalar(string value)
        {
            string trimmed = value.Trim();
            if (trimmed.Length < 2) return trimmed;
            if (trimmed[0] == '\'' && trimmed[^1] == '\'')
                return trimmed.Substring(1, trimmed.Length - 2).Replace("''", "'", StringComparison.Ordinal);
            if (trimmed[0] == '"' && trimmed[^1] == '"')
                return trimmed.Substring(1, trimmed.Length - 2).Replace("\\\"", "\"", StringComparison.Ordinal);
            return trimmed;
        }

        private static bool TryGetBlockScalarStyle(string value, out char style)
        {
            style = '\0';
            string[] tokens = value.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0 || tokens.Length > 3) return false;
            string indicator = tokens[^1];
            if (!Regex.IsMatch(
                    indicator,
                    @"\A[|>](?:(?:[1-9][+-]?)|(?:[+-][1-9]?))?\z",
                    RegexOptions.CultureInvariant))
                return false;

            bool hasAnchor = false;
            bool hasTag = false;
            for (int index = 0; index < tokens.Length - 1; index++)
            {
                string property = tokens[index];
                if (Regex.IsMatch(property, @"\A&[^\s,\[\]{}]+\z", RegexOptions.CultureInvariant))
                {
                    if (hasAnchor) return false;
                    hasAnchor = true;
                }
                else if (Regex.IsMatch(
                    property,
                    @"\A!(?:<[^>\s]+>|[^\s,\[\]{}]*)\z",
                    RegexOptions.CultureInvariant))
                {
                    if (hasTag) return false;
                    hasTag = true;
                }
                else
                {
                    return false;
                }
            }
            style = indicator[0];
            return true;
        }

        private static bool IsPlainYamlPropertyOrAlias(string value)
        {
            string trimmed = value.TrimStart();
            return trimmed.StartsWith('*') || trimmed.StartsWith('&') || trimmed.StartsWith('!');
        }

        private static bool StartsWithUnsupportedYamlFlowCollection(string value)
        {
            string trimmed = TrimYamlSequenceIndicator(value);
            return trimmed.StartsWith('[') || trimmed.StartsWith('{');
        }

        private static bool StartsWithUnsupportedYamlStructuralIndirection(string value)
        {
            string trimmed = TrimYamlSequenceIndicator(value);
            return trimmed.StartsWith('&') || trimmed.StartsWith('*') || trimmed.StartsWith('!') ||
                trimmed.StartsWith("<<:", StringComparison.Ordinal);
        }

        private static string TrimYamlSequenceIndicator(string value)
        {
            string trimmed = value.TrimStart();
            return trimmed.StartsWith("- ", StringComparison.Ordinal)
                ? trimmed.Substring(2).TrimStart()
                : trimmed;
        }

        private sealed class WorkflowPolicyDocument
        {
            public List<WorkflowPolicyJob> Jobs { get; } = new();

            public List<WorkflowUsesNode> UsesNodes { get; } = new();
        }

        private sealed class WorkflowPolicyJob
        {
            public WorkflowPolicyJob(string name)
            {
                Name = name;
            }

            public string Name { get; }

            public List<string> RunScripts { get; } = new();
        }

        private sealed class WorkflowUsesNode
        {
            public WorkflowUsesNode(string reference, string comment)
            {
                Reference = reference;
                Comment = comment;
            }

            public string Reference { get; }

            public string Comment { get; }
        }

        private static void AssertNoStaleGitHubMarkdown(IEnumerable<string> paths)
        {
            foreach (string path in paths)
            {
                string content = File.ReadAllText(path, Encoding.UTF8);
                foreach (string prohibited in new[]
                {
                    @"\.NET\s+Framework(?:\s+[0-9]+(?:\.[0-9]+){1,3})?",
                    @"\bnet4(?:[0-9]{1,3}|x)\b",
                    @"\bC#\s*7\.3\b",
                    @"CloudflareR2Uploader\.Wpf\.sln",
                    @"\blegacy\b[^\r\n]{0,80}\.csproj\b",
                    @"\bWinForms\b",
                    @"\bdual[- ]frontends?\b",
                    @"\bboth\s+front\s*ends?\b",
                    @"\bcompatibility\s+(?:project|projects|build|gate|requirement|requirements)\b",
                    @"\b(?:legacy|compatibility)\b[^\r\n]{0,80}\b(?:current|production|supported|active|maintained|required)\b",
                    @"\b(?:current|production|supported|active|maintained|required)\b[^\r\n]{0,80}\b(?:legacy|compatibility)\b"
                })
                {
                    Assert.IsFalse(
                        Regex.IsMatch(content, prohibited, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                        "Stale GitHub Markdown instruction in " + RelativePath(path) + ": " + prohibited);
                }

                if (!IsArchitectureBearingGitHubMarkdown(path, content)) continue;
                foreach (string required in new[] { ".NET 10", "WPF", "SDK-style" })
                {
                    Assert.IsTrue(
                        content.Contains(required, StringComparison.OrdinalIgnoreCase),
                        RelativePath(path) + " must independently name the canonical " + required + " architecture; " +
                            "another template cannot satisfy this file's policy.");
                }
            }
        }

        private static bool IsArchitectureBearingGitHubMarkdown(string path, string content)
        {
            string fileName = Path.GetFileName(path);
            return string.Equals(fileName, "pull_request_template.md", StringComparison.OrdinalIgnoreCase) ||
                fileName.Contains("architecture", StringComparison.OrdinalIgnoreCase) ||
                fileName.Contains("design", StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(
                    content,
                    @"\b(?:desktop|application|repository|project|solution)\s+architecture\b|\bproject\s+structure\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static void AssertNoPrivatePemHeader(string source, byte[] content)
        {
            Encoding[] encodings =
            {
                Encoding.ASCII,
                Encoding.Unicode,
                Encoding.BigEndianUnicode,
                new UTF32Encoding(bigEndian: false, byteOrderMark: false),
                new UTF32Encoding(bigEndian: true, byteOrderMark: false)
            };
            foreach (string keyType in new[]
            {
                "PRIVATE KEY", "ENCRYPTED PRIVATE KEY", "RSA PRIVATE KEY", "EC PRIVATE KEY",
                "DSA PRIVATE KEY", "OPENSSH PRIVATE KEY"
            })
            {
                foreach (string boundary in new[] { "BEGIN", "END" })
                {
                    string delimiter = "-----" + boundary + " " + keyType + "-----";
                    foreach (Encoding encoding in encodings)
                    {
                        Assert.IsTrue(
                            content.AsSpan().IndexOf(encoding.GetBytes(delimiter)) < 0,
                            source + " contains private PEM key material: " + delimiter + " (" + encoding.WebName + ")");
                    }
                }
            }
        }

        private static IEnumerable<string> EnumerateTrackedRepositoryFiles()
        {
            var startInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = RepositoryRoot(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("ls-files");
            startInfo.ArgumentList.Add("-z");
            using Process process = Process.Start(startInfo)!;
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.AreEqual(0, process.ExitCode, "git ls-files failed: " + error);
            foreach (string relativePath in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                string path = RepositoryPath(relativePath.Split('/'));
                if (File.Exists(path)) yield return path;
            }
        }

        private static void AssertPolicyRejectsMutation(string workflow, string mutation)
        {
            var expectedActions = new Dictionary<string, (string Sha, string Comment)>(StringComparer.Ordinal)
            {
                ["actions/checkout"] = ("d23441a48e516b6c34aea4fa41551a30e30af803", "v6"),
                ["actions/setup-dotnet"] = ("26b0ec14cb23fa6904739307f278c14f94c95bf1", "v5"),
                ["actions/upload-artifact"] = ("043fb46d1a93c77aae656e7c1c64a875d1fc6a0a", "v7")
            };
            Assert.ThrowsException<AssertFailedException>(
                () => AssertWorkflowPolicy(workflow, "mutation fixture: " + mutation, expectedActions),
                "Repository policy accepted mutation: " + mutation);
        }

        private static bool IsIgnoredByGit(string relativePath)
        {
            var startInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = RepositoryRoot(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("check-ignore");
            startInfo.ArgumentList.Add("--no-index");
            startInfo.ArgumentList.Add("--quiet");
            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add(relativePath);
            using Process process = Process.Start(startInfo)!;
            process.WaitForExit();
            Assert.IsTrue(
                process.ExitCode == 0 || process.ExitCode == 1,
                "git check-ignore failed for " + relativePath + ": " + process.StandardError.ReadToEnd());
            return process.ExitCode == 0;
        }

        private static string[] ExtractPowerShellArray(string script, string marker)
        {
            int start = script.IndexOf(marker, StringComparison.Ordinal);
            Assert.IsTrue(start >= 0, "Missing PowerShell array: " + marker);
            start += marker.Length;
            int end = script.IndexOf(')', start);
            Assert.IsTrue(end > start, "Unterminated PowerShell array: " + marker);
            return NormalizeNewlines(script.Substring(start, end - start))
                .Split('\n')
                .Select(line => line.Trim().TrimEnd(','))
                .Where(line => line.Length > 0)
                .ToArray();
        }

        private static IEnumerable<string> EnumerateRepositoryFiles(params string[] topLevelDirectories)
        {
            string root = RepositoryRoot();
            foreach (string topLevelDirectory in topLevelDirectories)
            {
                string directory = Path.Combine(root, topLevelDirectory);
                if (!Directory.Exists(directory)) continue;
                foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                {
                    if (path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                        path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                        continue;
                    if (IsExactWpfTemporaryProjectArtifact(path)) continue;
                    yield return path;
                }
            }
        }

        private static bool IsExactWpfTemporaryProjectArtifact(string path)
        {
            string relativePath = Path.GetRelativePath(RepositoryRoot(), path)
                .Replace(Path.DirectorySeparatorChar, '/');
            return Regex.IsMatch(
                relativePath,
                @"\Asrc/CloudflareR2Uploader\.Wpf/CloudflareR2Uploader\.Wpf_[A-Za-z0-9]+_wpftmp\.csproj\z",
                RegexOptions.CultureInvariant);
        }

        private static bool IsLiveSourceFile(string path)
        {
            string extension = Path.GetExtension(path);
            return string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".props", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".manifest", StringComparison.OrdinalIgnoreCase);
        }

        private static string RelativePath(string path)
        {
            return Path.GetRelativePath(RepositoryRoot(), path);
        }

        private static string NormalizeNewlines(string content)
        {
            return content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        }

        private static string ReadRepositoryFile(params string[] segments)
        {
            return File.ReadAllText(RepositoryPath(segments), Encoding.UTF8);
        }

        private static string RepositoryPath(params string[] segments)
        {
            string path = RepositoryRoot();
            foreach (string segment in segments) path = Path.Combine(path, segment);
            return path;
        }

        private static string RepositoryRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "CloudflareR2Uploader.sln")))
                directory = directory.Parent;
            Assert.IsNotNull(directory, "Repository root was not found from the test output directory.");
            return directory.FullName;
        }
    }
}
