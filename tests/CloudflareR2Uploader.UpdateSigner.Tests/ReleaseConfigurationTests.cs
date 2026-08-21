using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class ReleaseConfigurationTests
    {
        [TestMethod]
        public void BuildRelease_RequiresPairedUpdateUrlAndPublicKeyAndWritesBoth()
        {
            string script = ReadRepositoryFile("build", "Build-Release.ps1");

            StringAssert.Contains(script, "$hasUpdateUrl -ne $hasUpdateKey");
            StringAssert.Contains(script, "UpdateBaseUrl and UpdateManifestPublicKey must be supplied together.");
            StringAssert.Contains(script, "manifestUrl = $manifestUrl");
            StringAssert.Contains(script, "manifestPublicKey = $UpdateManifestPublicKey");
            StringAssert.Contains(script, "$parsedUpdateBase.Fragment");
            StringAssert.Contains(script, "$parsedUpdateBase.Query");
            StringAssert.Contains(script, "[switch]$RequireSignedUpdate");
            StringAssert.Contains(script, "'validate-public-key'");
        }

        [TestMethod]
        public void Publisher_UsesSignerForSignAndPostUploadVerifyAndPinsWrangler()
        {
            string script = ReadRepositoryFile("deploy", "Publish-CloudflareUpdate.ps1");

            StringAssert.Contains(script, "[string]$SigningCertificatePath");
            StringAssert.Contains(script, "[string]$SigningCertificatePasswordEnvironmentVariable");
            StringAssert.Contains(script, "'export-public-key'");
            StringAssert.Contains(script, "'sign'");
            StringAssert.Contains(script, "'verify'");
            StringAssert.Contains(script, "wrangler@4.120.0");
            StringAssert.Contains(script, "$baseUri.Query");
            StringAssert.Contains(script, "'verify-installer'");
            StringAssert.Contains(script, "The immutable installer object contains different bytes.");
            Assert.IsFalse(script.Contains("wrangler@latest", StringComparison.Ordinal));
        }

        [TestMethod]
        public void Publisher_RejectsInstallerKeyMaterialBeforeStartingAnyChild()
        {
            string script = ReadRepositoryFile("deploy", "Publish-CloudflareUpdate.ps1");
            string harness = ReadRepositoryFile("tests", "PowerShell", "Test-UpdateReleaseScripts.ps1");

            int installerResolution = script.IndexOf("$installer = [System.IO.Path]::GetFullPath", StringComparison.Ordinal);
            int aliasRejection = script.IndexOf("Test-PathsIdentifySameFile $installer $signerCertificate", StringComparison.Ordinal);
            int credibleInstallerCheck = script.IndexOf("Assert-CredibleInstaller $installer $signerCertificate", StringComparison.Ordinal);
            int firstChild = script.IndexOf("& $dotnet.Source build", StringComparison.Ordinal);
            Assert.IsTrue(installerResolution >= 0 && aliasRejection > installerResolution);
            Assert.IsTrue(credibleInstallerCheck > aliasRejection && firstChild > credibleInstallerCheck);
            StringAssert.Contains(script, "The installer must not contain the signing certificate bytes.");
            StringAssert.Contains(harness, "Test-PublisherRejectsInstallerCertificateAliasesBeforeWork");
            StringAssert.Contains(harness, "New-Item -ItemType HardLink");
            StringAssert.Contains(harness, "The PFX bytes changed");
        }

        [TestMethod]
        public void Publisher_RecognizesOnlyWranglersExactMissingKeyDiagnostic()
        {
            string script = ReadRepositoryFile("deploy", "Publish-CloudflareUpdate.ps1");
            string harness = ReadRepositoryFile("tests", "PowerShell", "Test-UpdateReleaseScripts.ps1");

            StringAssert.Contains(script, "The specified key does not exist.");
            Assert.IsFalse(script.Contains("NoSuchKey", StringComparison.Ordinal));
            StringAssert.Contains(harness, "The specified key does not exist.");
            StringAssert.Contains(harness, "permission-failure");
            StringAssert.Contains(harness, "network-failure");
        }

        [TestMethod]
        public void Publisher_StreamsAuthenticatedR2ReadsThroughAnIndependentHardLimit()
        {
            string script = ReadRepositoryFile("deploy", "Publish-CloudflareUpdate.ps1");
            string harness = ReadRepositoryFile("tests", "PowerShell", "Test-UpdateReleaseScripts.ps1");

            StringAssert.Contains(script, "'--pipe'");
            StringAssert.Contains(script, "ProcessStartInfo");
            StringAssert.Contains(script, "$MaximumBytes");
            StringAssert.Contains(script, "$totalBytes -gt $MaximumBytes");
            StringAssert.Contains(script, "The authenticated R2 object is larger than allowed.");
            StringAssert.Contains(script, "$AuthenticatedReadTimeoutSeconds");
            StringAssert.Contains(script, "Stopwatch]::StartNew");
            StringAssert.Contains(script, "StandardError.ReadToEndAsync");
            StringAssert.Contains(script, "$readTask.Wait(");
            StringAssert.Contains(script, "$process.Kill($true)");
            StringAssert.Contains(script, "$process.WaitForExit(");
            Assert.IsFalse(
                script.Contains(".Token.Register([Action]", StringComparison.Ordinal),
                "The authenticated-read timeout invokes a PowerShell callback without a runspace.");
            Assert.IsFalse(
                script.Contains("r2 object get $ObjectPath --remote --file", StringComparison.Ordinal),
                "Authenticated retrieval still delegates unbounded file materialization to Wrangler.");
            foreach (string mode in new[]
            {
                "authenticated-preflight-oversize",
                "authenticated-installer-readback-oversize",
                "authenticated-manifest-readback-oversize",
                "authenticated-partial-failure",
                "authenticated-missing-length",
                "authenticated-dishonest-length",
                "authenticated-during-materialization-overflow",
                "authenticated-hanging-child-timeout"
            })
                StringAssert.Contains(harness, mode);
        }

        [TestMethod]
        public void ReleaseNotesSchemaAndPublisherUseUnicodeScalarLength()
        {
            using JsonDocument schema = JsonDocument.Parse(
                ReadRepositoryFile("deploy", "update-manifest.schema.json"));
            int maximum = schema.RootElement.GetProperty("properties")
                .GetProperty("notes").GetProperty("maxLength").GetInt32();
            string astral = char.ConvertFromUtf32(0x1f680);
            string accepted = string.Concat(Enumerable.Repeat(astral, 8000));
            string rejected = accepted + astral;
            string publisher = ReadRepositoryFile("deploy", "Publish-CloudflareUpdate.ps1");
            string harness = ReadRepositoryFile("tests", "PowerShell", "Test-UpdateReleaseScripts.ps1");

            Assert.AreEqual(8000, maximum);
            Assert.AreEqual(maximum, accepted.EnumerateRunes().Count());
            Assert.AreEqual(maximum + 1, rejected.EnumerateRunes().Count());
            StringAssert.Contains(publisher, "Get-UnicodeScalarCount");
            StringAssert.Contains(publisher, "$releaseNotesScalarCount -gt 8000");
            StringAssert.Contains(harness, "maximum-scalar-release-notes");
            StringAssert.Contains(harness, "oversized-scalar-release-notes");
            StringAssert.Contains(harness, "malformed-surrogate-release-notes");
        }

        [TestMethod]
        public void Publisher_UsesOnePrivateInstallerSnapshotForEveryPublicationOperation()
        {
            string script = ReadRepositoryFile("deploy", "Publish-CloudflareUpdate.ps1");
            string harness = ReadRepositoryFile("tests", "PowerShell", "Test-UpdateReleaseScripts.ps1");

            StringAssert.Contains(script, "$installerSnapshot = Join-Path $publicationDirectory 'installer-snapshot.exe'");
            StringAssert.Contains(script, "$installerSourceLock.CopyTo($installerSnapshotLock)");
            StringAssert.Contains(script, "$installerSnapshotLock.Flush($true)");
            StringAssert.Contains(script, "Get-StreamSha256 $installerSnapshotLock");
            StringAssert.Contains(script, "'--installer', $installerSnapshot");
            StringAssert.Contains(script, "'--file', $installerSnapshot");
            StringAssert.Contains(script, "'--held-read-lock', 'true'");
            StringAssert.Contains(harness, "source-mutation");
            StringAssert.Contains(harness, "validation-boundary-swap");
        }

        [TestMethod]
        public void Publisher_HoldsTheCreatingSnapshotHandleAcrossValidationHashAndPublication()
        {
            string script = ReadRepositoryFile("deploy", "Publish-CloudflareUpdate.ps1");
            string harness = ReadRepositoryFile("tests", "PowerShell", "Test-UpdateReleaseScripts.ps1");

            StringAssert.Contains(script, "[System.IO.FileAccess]::ReadWrite");
            StringAssert.Contains(script, "[System.IO.FileShare]::Read");
            StringAssert.Contains(script, "$installerSourceLock.CopyTo($installerSnapshotLock)");
            StringAssert.Contains(script, "$installerSnapshotLock.Flush($true)");
            StringAssert.Contains(script, "Get-StreamSha256 $installerSnapshotLock");
            Assert.IsFalse(script.Contains("$snapshotWriter.Dispose()", StringComparison.Ordinal));
            StringAssert.Contains(harness, "creation-boundary-swap");
            StringAssert.Contains(harness, "validation-boundary-swap");
            StringAssert.Contains(harness, "snapshot-swap-blocked");
        }

        [TestMethod]
        public void Publisher_RejectsCustomInstallerForFreshBuildBeforeAnyChild()
        {
            string script = ReadRepositoryFile("deploy", "Publish-CloudflareUpdate.ps1");
            string harness = ReadRepositoryFile("tests", "PowerShell", "Test-UpdateReleaseScripts.ps1");

            int rejection = script.IndexOf("InstallerPath cannot be supplied unless SkipBuild is set.", StringComparison.Ordinal);
            int firstChild = script.IndexOf("& $dotnet.Source build", StringComparison.Ordinal);
            Assert.IsTrue(rejection >= 0 && firstChild > rejection);
            StringAssert.Contains(harness, "fresh-build-custom-installer");
            StringAssert.Contains(harness, "A stale custom installer started a child process.");
        }

        [TestMethod]
        public void Publisher_RequiresExpectedInnoMetadataAndNumericVersionBeforeSigningOrNetwork()
        {
            string script = ReadRepositoryFile("deploy", "Publish-CloudflareUpdate.ps1");
            string build = ReadRepositoryFile("build", "Build-Release.ps1");
            string installer = ReadRepositoryFile("installer", "CloudflareR2Uploader.iss");
            string harness = ReadRepositoryFile("tests", "PowerShell", "Test-UpdateReleaseScripts.ps1");

            int metadata = script.IndexOf("'validate-installer-metadata'", StringComparison.Ordinal);
            int sign = script.IndexOf("'sign', '--pfx'", StringComparison.Ordinal);
            int network = script.IndexOf("Receive-WranglerObject", script.IndexOf("$remoteInstallerExists", StringComparison.Ordinal), StringComparison.Ordinal);
            Assert.IsTrue(metadata >= 0 && sign > metadata && network > metadata);
            StringAssert.Contains(build, "$versionInfo.FileMajorPart -ne $versionMajor");
            StringAssert.Contains(build, "$versionInfo.FileMinorPart -ne $versionMinor");
            StringAssert.Contains(build, "$versionInfo.FileBuildPart -ne $versionPatch");
            StringAssert.Contains(build, "$versionInfo.FilePrivatePart -ne 0");
            Assert.IsFalse(
                build.Contains("$versionInfo.FileVersion -ne $numericVersion", StringComparison.Ordinal),
                "Installer validation must use fixed numeric version fields instead of localized display text.");
            StringAssert.Contains(build, "Actual numeric file version: {0}.{1}.{2}.{3}.");
            StringAssert.Contains(installer, "VersionInfoProductName=Cloudflare R2 Uploader");
            StringAssert.Contains(installer, "VersionInfoDescription=Cloudflare R2 Uploader Setup");
            foreach (string caseName in new[] { "metadata-stub", "metadata-wrong-product", "metadata-wrong-version", "metadata-malformed" })
                StringAssert.Contains(harness, caseName);
            StringAssert.Contains(harness, "CloudflareR2Uploader.UpdateSigner.Tests.dll");
        }

        [TestMethod]
        public void ReleaseVersionContracts_DeriveTheGenuineFixtureVersionAndRejectMalformedSemVerBeforeWork()
        {
            string build = ReadRepositoryFile("build", "Build-Release.ps1");
            string publisher = ReadRepositoryFile("deploy", "Publish-CloudflareUpdate.ps1");
            string ci = ReadRepositoryFile(".github", "workflows", "ci.yml");
            string release = ReadRepositoryFile(".github", "workflows", "release.yml");
            string harness = ReadRepositoryFile("tests", "PowerShell", "Test-UpdateReleaseScripts.ps1");
            string signerTests = ReadRepositoryFile(
                "tests", "CloudflareR2Uploader.UpdateSigner.Tests", "SignerCommandTests.cs");

            StringAssert.Contains(harness, "$genuineInstallerVersion");
            StringAssert.Contains(harness, "FileMajorPart");
            StringAssert.Contains(harness, "FileMinorPart");
            StringAssert.Contains(harness, "FileBuildPart");
            Assert.IsFalse(harness.Contains("-Version '2.0.0'", StringComparison.Ordinal));
            StringAssert.Contains(signerTests, "FixtureSemanticVersion()");
            StringAssert.Contains(ci, "BUILD_VERSION: 0.0.0-ci.${{ github.run_number }}");
            StringAssert.Contains(release, "Version = $env:RELEASE_VERSION");
            foreach (string workflow in new[] { ci, release })
            {
                int releaseBuild = workflow.IndexOf("Build-Release.ps1", StringComparison.Ordinal);
                int behavioralHarness = workflow.IndexOf("Test-UpdateReleaseScripts.ps1", StringComparison.Ordinal);
                Assert.IsTrue(
                    releaseBuild >= 0 && behavioralHarness > releaseBuild,
                    "The fixture must be built with the workflow's global version before the harness derives it.");
            }

            foreach (string contract in new[] { build, publisher, release })
            {
                StringAssert.Contains(contract, "[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*");
                StringAssert.Contains(contract, "[uint16]::TryParse");
            }
            StringAssert.Contains(harness, "Test-SemanticVersionValidation");
            foreach (string caseName in new[]
            {
                "numeric-prerelease-leading-zero", "empty-prerelease", "invalid-build",
                "file-version-overflow", "valid-prerelease"
            })
            {
                StringAssert.Contains(harness, caseName);
            }

            int publisherVersionValidation = publisher.IndexOf("$Version -notmatch", StringComparison.Ordinal);
            int publisherFirstChild = publisher.IndexOf("Get-Command dotnet", StringComparison.Ordinal);
            Assert.IsTrue(
                publisherVersionValidation >= 0 && publisherFirstChild > publisherVersionValidation,
                "Publisher SemVer validation must precede child discovery.");
        }

        [TestMethod]
        public void PowerShellBehavioralHarness_UsesNestedTypeSyntaxForNamedCurves()
        {
            string harness = ReadRepositoryFile("tests", "PowerShell", "Test-UpdateReleaseScripts.ps1");

            StringAssert.Contains(
                harness,
                "[System.Security.Cryptography.ECCurve+NamedCurves]::nistP256");
            Assert.IsFalse(
                harness.Contains("[System.Security.Cryptography.ECCurve]::NamedCurves", StringComparison.Ordinal),
                "PowerShell resolves nested CLR types with '+', not a static-property member chain.");
        }

        [TestMethod]
        public void ReleaseVersionPatterns_AgreeOnTheStrictPublishableSemVerGrammar()
        {
            string buildPattern = ExtractPublishableSemanticVersionPattern(
                ReadRepositoryFile("build", "Build-Release.ps1"));
            string publisherPattern = ExtractPublishableSemanticVersionPattern(
                ReadRepositoryFile("deploy", "Publish-CloudflareUpdate.ps1"));
            string tagPattern = ExtractPublishableSemanticVersionPattern(
                ReadRepositoryFile(".github", "workflows", "release.yml"));

            foreach (string valid in new[] { "0.0.0", "65535.65535.65535", "1.2.3-rc.1", "1.2.3-alpha-1" })
            {
                Assert.IsTrue(Regex.IsMatch(valid, buildPattern, RegexOptions.CultureInvariant), "build: " + valid);
                Assert.IsTrue(Regex.IsMatch(valid, publisherPattern, RegexOptions.CultureInvariant), "publisher: " + valid);
                Assert.IsTrue(Regex.IsMatch("v" + valid, tagPattern, RegexOptions.CultureInvariant), "tag: " + valid);
            }

            foreach (string invalid in new[]
            {
                "01.2.3", "1.2.3-01", "1.2.3-", "1.2.3-alpha..1",
                "1.2.3-alpha_1", "1.2.3+build.1", " 1.2.3", "1.2.3 ", "v1.2.3",
                "1.2.3\n", "1.2.3\r\n"
            })
            {
                Assert.IsFalse(Regex.IsMatch(invalid, buildPattern, RegexOptions.CultureInvariant), "build: " + invalid);
                Assert.IsFalse(Regex.IsMatch(invalid, publisherPattern, RegexOptions.CultureInvariant), "publisher: " + invalid);
                Assert.IsFalse(Regex.IsMatch("v" + invalid, tagPattern, RegexOptions.CultureInvariant), "tag: " + invalid);
            }

            Assert.IsFalse(Regex.IsMatch("1.2.3", tagPattern, RegexOptions.CultureInvariant));
        }

        [TestMethod]
        public void ReleaseVersionSurfacesEnforceThe128CharacterSignedManifestLimit()
        {
            string build = ReadRepositoryFile("build", "Build-Release.ps1");
            string publisher = ReadRepositoryFile("deploy", "Publish-CloudflareUpdate.ps1");
            string release = ReadRepositoryFile(".github", "workflows", "release.yml");
            string harness = ReadRepositoryFile("tests", "PowerShell", "Test-UpdateReleaseScripts.ps1");
            string maximumVersion = "1.2.3-" + new string('a', 122);
            string oversizedVersion = maximumVersion + "a";

            Assert.AreEqual(128, maximumVersion.Length);
            Assert.AreEqual(129, oversizedVersion.Length);
            Assert.IsTrue(Regex.IsMatch(
                maximumVersion,
                ExtractPublishableSemanticVersionPattern(build),
                RegexOptions.CultureInvariant));
            Assert.IsTrue(Regex.IsMatch(
                oversizedVersion,
                ExtractPublishableSemanticVersionPattern(build),
                RegexOptions.CultureInvariant),
                "The explicit length gate, rather than a regex accident, must reject 129 characters.");
            StringAssert.Contains(build, "$Version.Length -gt 128");
            StringAssert.Contains(publisher, "$Version.Length -gt 128");
            StringAssert.Contains(release, "$releaseVersionCandidate.Length -gt 128");
            StringAssert.Contains(harness, "maximum-length");
            StringAssert.Contains(harness, "over-maximum-length");

            using JsonDocument schema = JsonDocument.Parse(
                ReadRepositoryFile("deploy", "update-manifest.schema.json"));
            JsonElement versionSchema = schema.RootElement
                .GetProperty("properties")
                .GetProperty("version");
            Assert.AreEqual(128, versionSchema.GetProperty("maxLength").GetInt32());
            Assert.IsTrue(Regex.IsMatch(
                maximumVersion,
                versionSchema.GetProperty("pattern").GetString(),
                RegexOptions.CultureInvariant));
        }

        [TestMethod]
        public void Publisher_VerifiesPublicHttpsManifestAndInstallerBeforeSuccess()
        {
            string script = ReadRepositoryFile("deploy", "Publish-CloudflareUpdate.ps1");
            string harness = ReadRepositoryFile("tests", "PowerShell", "Test-UpdateReleaseScripts.ps1");

            StringAssert.Contains(script, "'--proto', '=https'");
            StringAssert.Contains(script, "'--proto-redir', '=https'");
            StringAssert.Contains(script, "public-installer.bin");
            StringAssert.Contains(script, "public-manifest.json");
            StringAssert.Contains(script, "cacheBust=");
            StringAssert.Contains(script, "'verify-installer'");
            StringAssert.Contains(script, "'verify'");
            StringAssert.Contains(script, "The public manifest did not match the signed manifest that was uploaded.");
            foreach (string mode in new[]
            {
                "public-wrong", "public-stale", "public-downgrade", "public-oversize", "public-failure"
            })
            {
                StringAssert.Contains(harness, mode);
            }
        }

        [TestMethod]
        public void Publisher_ConsumesPasswordBeforeValidationAndClearsItOnEveryExit()
        {
            string script = ReadRepositoryFile("deploy", "Publish-CloudflareUpdate.ps1");
            string harness = ReadRepositoryFile("tests", "PowerShell", "Test-UpdateReleaseScripts.ps1");

            int capture = script.IndexOf("[Environment]::GetEnvironmentVariable($SigningCertificatePasswordEnvironmentVariable)", StringComparison.Ordinal);
            int removal = script.IndexOf("[Environment]::SetEnvironmentVariable($SigningCertificatePasswordEnvironmentVariable, $null)", StringComparison.Ordinal);
            int preferences = script.IndexOf("$ErrorActionPreference = 'Stop'", StringComparison.Ordinal);
            int validation = script.IndexOf("$Version -notmatch", StringComparison.Ordinal);
            Assert.IsTrue(capture >= 0 && removal > capture && preferences > removal && validation > removal);
            StringAssert.Contains(script, "finally {");
            StringAssert.Contains(harness, "Test-PublisherConsumesPasswordOnEveryFailure");
            StringAssert.Contains(harness, "invalid-version");
            StringAssert.Contains(harness, "missing-certificate");
        }

        [TestMethod]
        public void DocumentationWrapsPublisherInvocationSoBindingFailureStillClearsPassword()
        {
            string deployReadme = ReadRepositoryFile("deploy", "README.md");
            string releasing = ReadRepositoryFile("docs", "RELEASING.md");
            string harness = ReadRepositoryFile("tests", "PowerShell", "Test-UpdateReleaseScripts.ps1");

            StringAssert.Contains(deployReadme, "try {");
            StringAssert.Contains(deployReadme, "finally {");
            StringAssert.Contains(deployReadme, "parameter binding occurs before the publisher script body");
            StringAssert.Contains(releasing, "caller-side `try`/`finally`");
            StringAssert.Contains(harness, "unknown-binding-parameter");
            StringAssert.Contains(harness, "The caller wrapper did not clear the password after parameter binding failed.");
        }

        [TestMethod]
        public void PowerShellHarnessUsesTheRealSignerAndFakesOnlyBuildAndNetwork()
        {
            string harness = ReadRepositoryFile("tests", "PowerShell", "Test-UpdateReleaseScripts.ps1");

            StringAssert.Contains(harness, "CreateSelfSigned");
            StringAssert.Contains(harness, "UPDATE_RELEASE_TEST_REAL_DOTNET");
            StringAssert.Contains(harness, "& $env:UPDATE_RELEASE_TEST_REAL_DOTNET @args");
            StringAssert.Contains(harness, "curl-fake.ps1");
            Assert.IsFalse(harness.Contains("signature = 'synthetic'", StringComparison.Ordinal));
            Assert.IsFalse(harness.Contains("'verify' { exit 0 }", StringComparison.Ordinal));
        }

        [TestMethod]
        public void DeploymentDocumentationDescribesContentAddressingAndDualReadback()
        {
            string deployReadme = ReadRepositoryFile("deploy", "README.md");
            string releasing = ReadRepositoryFile("docs", "RELEASING.md");

            StringAssert.Contains(deployReadme, "<sha256>-<installer-name>");
            StringAssert.Contains(deployReadme, "authenticated R2 read-back");
            StringAssert.Contains(deployReadme, "public HTTPS read-back");
            StringAssert.Contains(deployReadme, "installer and manifest");
            StringAssert.Contains(releasing, "content-addressed");
            StringAssert.Contains(releasing, "authenticated R2");
            StringAssert.Contains(releasing, "public HTTPS");
        }

        [TestMethod]
        public void DeploymentDocumentationExplainsRecoveryAfterManifestPublicationFailure()
        {
            string deployReadme = ReadRepositoryFile("deploy", "README.md");
            string releasing = ReadRepositoryFile("docs", "RELEASING.md");

            StringAssert.Contains(deployReadme, "may already be live");
            StringAssert.Contains(deployReadme, "byte-for-byte");
            StringAssert.Contains(deployReadme, "verify-installer");
            StringAssert.Contains(deployReadme, "Do not blindly retry");
            StringAssert.Contains(releasing, "authenticated inspection");
        }

        [TestMethod]
        public void CurlHarnessEnforcesTheExactBoundedHttpsArgumentContractAndFailureCleanup()
        {
            string harness = ReadRepositoryFile("tests", "PowerShell", "Test-UpdateReleaseScripts.ps1");

            foreach (string value in new[]
            {
                "--fail", "--location", "--connect-timeout", "--max-time", "--max-filesize",
                "--write-out", "--proto", "=https", "--proto-redir", "268435456", "65536"
            })
                StringAssert.Contains(harness, value);
            foreach (string mode in new[]
            {
                "public-partial-failure", "public-mixed-failure", "public-unsafe-credentials",
                "public-unsafe-fragment", "public-installer-oversize", "public-oversize"
            })
                StringAssert.Contains(harness, mode);
            StringAssert.Contains(harness, "Unexpected curl argument contract");
            StringAssert.Contains(harness, "A failed curl left a partial verification file.");
        }

        [TestMethod]
        public void TaggedRelease_WiresBothUpdateVariablesWhileCiWiresNeither()
        {
            string release = ReadRepositoryFile(".github", "workflows", "release.yml");
            string ci = ReadRepositoryFile(".github", "workflows", "ci.yml");

            StringAssert.Contains(release, "UPDATE_BASE_URL: ${{ vars.UPDATE_BASE_URL }}");
            StringAssert.Contains(release, "UPDATE_MANIFEST_PUBLIC_KEY: ${{ vars.UPDATE_MANIFEST_PUBLIC_KEY }}");
            StringAssert.Contains(release, "UpdateManifestPublicKey = $env:UPDATE_MANIFEST_PUBLIC_KEY.Trim()");
            StringAssert.Contains(release, "if ([string]::IsNullOrWhiteSpace($env:UPDATE_BASE_URL)");
            StringAssert.Contains(release, "RequireSignedUpdate = $true");
            Assert.IsFalse(ci.Contains("UPDATE_BASE_URL", StringComparison.Ordinal));
            Assert.IsFalse(ci.Contains("UPDATE_MANIFEST_PUBLIC_KEY", StringComparison.Ordinal));
            StringAssert.Contains(ci, "Test-UpdateReleaseScripts.ps1");
            StringAssert.Contains(release, "Test-UpdateReleaseScripts.ps1");
        }

        [TestMethod]
        public void ManifestSchema_RequiresEverySignedAndSignatureField()
        {
            using JsonDocument schema = JsonDocument.Parse(
                ReadRepositoryFile("deploy", "update-manifest.schema.json"));
            JsonElement root = schema.RootElement;

            Assert.IsFalse(root.GetProperty("additionalProperties").GetBoolean());
            JsonElement required = root.GetProperty("required");
            foreach (string property in new[]
            {
                "schemaVersion", "version", "installerUrl", "sha256", "sizeBytes",
                "publishedUtc", "notes", "signatureAlgorithm", "signature"
            })
            {
                Assert.IsTrue(Contains(required, property), property);
            }
            Assert.AreEqual(
                "RSA-PSS-SHA256",
                root.GetProperty("properties").GetProperty("signatureAlgorithm").GetProperty("const").GetString());
            Assert.AreEqual(
                "^[0-9a-f]{64}$",
                root.GetProperty("properties").GetProperty("sha256").GetProperty("pattern").GetString());
        }

        [TestMethod]
        public void ManifestSchema_UsesTheSameStrictPublishablePrereleaseGrammar()
        {
            using JsonDocument schema = JsonDocument.Parse(
                ReadRepositoryFile("deploy", "update-manifest.schema.json"));
            string pattern = schema.RootElement
                .GetProperty("properties")
                .GetProperty("version")
                .GetProperty("pattern")
                .GetString();

            Assert.IsTrue(Regex.IsMatch("1.2.3-rc.1", pattern, RegexOptions.CultureInvariant));
            Assert.IsFalse(Regex.IsMatch("1.2.3-01", pattern, RegexOptions.CultureInvariant));
            Assert.IsFalse(Regex.IsMatch("1.2.3-", pattern, RegexOptions.CultureInvariant));
            Assert.IsFalse(Regex.IsMatch("1.2.3-alpha..1", pattern, RegexOptions.CultureInvariant));
            Assert.IsFalse(Regex.IsMatch("1.2.3+build.1", pattern, RegexOptions.CultureInvariant));
            Assert.IsFalse(Regex.IsMatch("1.2.3\n", pattern, RegexOptions.CultureInvariant));
        }

        private static bool Contains(JsonElement array, string expected)
        {
            foreach (JsonElement value in array.EnumerateArray())
            {
                if (string.Equals(value.GetString(), expected, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static string ExtractPublishableSemanticVersionPattern(string content)
        {
            const string marker = "$publishableSemanticVersionPattern = '";
            int start = content.IndexOf(marker, StringComparison.Ordinal);
            Assert.IsTrue(start >= 0, "The publishable Semantic Version pattern is missing.");
            start += marker.Length;
            int end = content.IndexOf('\'', start);
            Assert.IsTrue(end > start, "The publishable Semantic Version pattern is unterminated.");
            return content.Substring(start, end - start);
        }

        private static string ReadRepositoryFile(params string[] segments)
        {
            DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "CloudflareR2Uploader.sln")))
                directory = directory.Parent;
            Assert.IsNotNull(directory, "Repository root was not found from the test output directory.");

            string path = directory.FullName;
            foreach (string segment in segments) path = Path.Combine(path, segment);
            return File.ReadAllText(path);
        }
    }
}
