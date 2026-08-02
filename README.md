# Cloudflare R2 Uploader

Cloudflare R2 Uploader is a Windows Forms desktop client for uploading files and
folders directly to a Cloudflare R2 bucket through the S3-compatible API. It
supports single-request uploads for smaller files and controlled, resumable
multipart uploads for large and multi-gigabyte files.

## Requirements

- Windows 10 or Windows 11
- Microsoft .NET Framework 4.8 on the machine that runs the application
- Visual Studio 2022 or newer with the **.NET desktop development** workload and
  the .NET Framework 4.8 targeting pack to build the solution
- A Cloudflare account, an R2 bucket, and bucket-scoped R2 S3 credentials

No Worker, command-line upload tool, external UI framework, or installation
service is used for uploads.

## Build the solution

Open `CloudflareR2Uploader.sln` in Visual Studio, select `Release | Any CPU`,
then choose **Build > Rebuild Solution**.

From PowerShell, the complete build, test, and distribution step is:

```powershell
.\build\Build-Release.ps1
```

The script uses the newest Visual Studio MSBuild found by `vswhere.exe`, restores
NuGet packages, rebuilds Release, runs the MSTest suite, and copies the woven
single-file application to:

```text
dist\CloudflareR2Uploader.exe
```

Fody and Costura.Fody embed the managed AWS SDK dependencies into the
application. The `dist` directory does not need AWSSDK DLLs, configuration
files, JSON files, images, or PDB files beside the executable.

## Create the correct Cloudflare credentials

1. Open the Cloudflare dashboard and select **R2 object storage**.
2. Create or select the destination bucket.
3. Open **Manage R2 API Tokens** and create a token with **Object Read & Write**
   permission scoped to that bucket. Read access is needed for connection tests,
   overwrite checks, multipart resumption, and post-upload verification.
4. Copy the generated **Access Key ID** and **Secret Access Key** when Cloudflare
   displays them.
5. Find the **Account ID** on the R2 overview page or the account overview page.

The Access Key ID and Secret Access Key are the S3 credentials derived from an
R2 API token. They are not the normal Cloudflare dashboard API token string.
Pasting the dashboard token into either credential field will not authenticate
to the S3-compatible endpoint.

Cloudflare documentation:

- [R2 API tokens](https://developers.cloudflare.com/r2/api/tokens/)
- [Use the S3-compatible API](https://developers.cloudflare.com/r2/get-started/s3/)
- [AWS SDK for .NET example](https://developers.cloudflare.com/r2/examples/aws/aws-sdk-net/)

## Configure and use the application

1. Run `CloudflareR2Uploader.exe` and open **Settings**.
2. Enter the Account ID, Access Key ID, Secret Access Key, and exact bucket name.
3. Normally leave **Custom endpoint** empty. The application derives:
   `https://{ACCOUNT_ID}.r2.cloudflarestorage.com`.
4. Optionally enter a public or custom domain. It is used only to display a final
   URL; it does not make a private object public.
5. Choose whether Windows should remember the credentials, then select
   **Test connection** and **Save**.
6. Drop files or folders on the upload area, or use the browse buttons.
7. Set the destination prefix, folder-structure behavior, and overwrite policy,
   then select **Upload all**.

Uploading remains disabled until the current bucket and credentials pass the
connection test. The test reads at most one object entry from the configured
bucket and does not require account-wide bucket-list permission.

## Browse and manage bucket files

Select **Files** below the application header to browse the currently selected
bucket without opening another window. Switching between Upload and Files uses
a short transition while preserving both pages and the upload queue in memory.

Folders shown by the browser are virtual prefixes separated by `/`; R2 does not
store real directories. Listings contain at most 250 entries per page. Use
**Previous** and **Next** to move through the continuation-token pages, and use
**Refresh** when recent uploads are not yet shown. **New folder** creates an
empty virtual folder in the current location by storing a zero-byte folder
marker whose key ends in `/`.

Right-click an object to download it, overwrite it from a local file, copy it,
rename it, move it to another key, or delete it. Right-click a virtual folder to
rename, copy, move, or recursively delete every object under that prefix.
Renaming refuses to replace an existing file or folder and uses the same safe
copy-then-delete behavior as Move. **Copy** stores an
in-application clipboard entry; **Paste** copies it into the current or selected
folder and chooses a non-conflicting `- Copy` name when necessary. The clipboard
is cleared when the bucket profile changes.

Move is implemented safely as copy-then-delete: the source is not removed until
all copies complete and their sizes are verified. Large objects use multipart
server-side copy. Folder moves can merge with an existing destination prefix,
so the confirmation describes that behavior before any request is sent.

Listing and downloading require **Object Read** permission. New folder, rename,
overwrite, copy, move, and delete require **Object Read & Write** permission. Delete operations
are permanent and always show a confirmation. Selecting a row does not alter
the normal upload destination, and double-clicking a file still performs no
network operation.

## Multipart uploads and resumption

Files below the configured threshold use one streaming `PutObject` request.
Files at or above the threshold use the low-level multipart API:

1. initiate the multipart upload;
2. upload bounded file ranges in parallel;
3. save each confirmed part number and ETag;
4. complete only when every part is present and the source file is unchanged;
5. verify the final object length with a metadata request.

Defaults are a 100 MiB threshold, 64 MiB parts, four parallel parts, and five
attempts per operation. Part size grows automatically when necessary to remain
under the 10,000-part limit. Every upload request disables the AWS SDK streaming
payload-signing and default-checksum flow required for Cloudflare R2, and the
application refuses non-HTTPS endpoints.

Pause stops scheduling new multipart parts and new files. Parts already in
flight finish so their ETags can be saved. A single in-flight `PutObject` request
cannot be paused safely, so it finishes while the queue remains paused before
the next file. Cancellation lets you keep multipart parts for later resumption
or abort and remove the incomplete upload.

## Local data and security

Runtime data is stored under:

```text
%LocalAppData%\CloudflareR2Uploader\
```

- `settings.json` contains non-secret application settings.
- `credentials.dat` contains the Access Key ID and Secret Access Key only when
  remembering is enabled. Windows DPAPI encrypts it for the current user.
- `UploadState\` contains resumable multipart metadata and never credentials.
- `Logs\` contains sanitized daily logs.

Disabling **Remember credentials** prevents a leftover encrypted blob from being
loaded. The settings window also offers to remove it from disk. Secrets,
authorization headers, signed URLs, and encrypted credential blobs are not
written to logs or technical error reports.

## Tests

The automated suite covers object keys and duplicate names, browser paths,
response mapping and continuation-token history, multipart sizing and part
limits, progress calculations, formatting, MIME detection, transient error
classification, retry delays, settings and resume-state JSON, DPAPI encryption,
path handling, UI construction, and the AWS SDK configuration contract.

The suite does not perform a live R2 upload. A live integration check requires
the operator's own bucket and valid R2 S3 credentials.
