# API — Web Server (nginx + PHP) Integration

This document explains exactly what the Trion client expects from the backend API
when serving the nginx and PHP portable packages, and what you need to add to the
Flying-Phoenix API to make the **Download & Install** button work end-to-end.

---

## How the client downloads packages

The client shares one generic download endpoint with MySQL, emulators, and all
other packages.  The package is identified by the `emulator` query parameter.

### Endpoint

```
GET /Trion/DownloadFile?emulator=<package>
```

The full URL is built in `ApiEndpoints.cs`:

```csharp
// src/Trion.Core/Constants/ApiEndpoints.cs

public static string NginxDownloadUrl(string baseUrl) =>
    $"{baseUrl}{DownloadFile}?emulator=nginx";

public static string PhpDownloadUrl(string baseUrl) =>
    $"{baseUrl}{DownloadFile}?emulator=php";
```

Both URLs are then surfaced through `AppLinks`:

```csharp
// src/Trion.Desktop/Models/AppLinks.cs

public static string NginxApiDownloadUrl => ApiEndpoints.NginxDownloadUrl(ApiBaseUrl);
public static string PhpApiDownloadUrl   => ApiEndpoints.PhpDownloadUrl(ApiBaseUrl);
```

### Request headers

| Header      | Value                              | Required |
|-------------|------------------------------------|----------|
| `X-API-Key` | User's API key (supporter tier key) | No — omitted for guest/free tier |
| `User-Agent` | `Mozilla/5.0 … TrionControlPanel/1.0` | Yes |

### Expected response

- **Status**: `200 OK`
- **Body**: raw binary stream of the `.zip` file
- **`Content-Length`**: strongly recommended — the client uses it to calculate
  download progress percentage and ETA
- **`Content-Type`**: `application/zip` or `application/octet-stream`

The client writes the stream directly to a temp file and validates the ZIP magic
bytes (`PK\x03\x04`) after download.  Minimum accepted file size is 512 KB —
smaller responses are treated as corrupt and retried.

### Fallback on API failure

If the API returns a non-2xx status or times out, the client falls back to the
CDN URLs defined in `ExternalLinks.cs`:

```csharp
// src/Trion.Core/Constants/ExternalLinks.cs

public const string NginxVersion     = "1.28.2";
public const string NginxCdnDownload = "https://nginx.org/download/nginx-1.28.2.zip";

public const string PhpVersion       = "8.3.20";
public const string PhpCdnDownload   =
    "https://windows.php.net/downloads/releases/php-8.3.20-nts-Win32-vs16-x64.zip";
```

Update both the version constant **and** the CDN URL together whenever you
upgrade a package.

---

## What the API must serve

### `emulator=nginx`

Serve the **Windows portable nginx** zip.  The client extracts it into
`<appdir>/webserver/nginx/` and expects to find `nginx.exe` directly inside
that folder (one level of nesting is automatically unwrapped during extraction).

| Field           | Value |
|-----------------|-------|
| Current version | `1.28.2` |
| Official source | `https://nginx.org/download/nginx-1.28.2.zip` |
| Expected root   | `nginx-1.28.2\` (single top-level folder inside the zip) |
| Key binary      | `nginx.exe` |

The API can either:
- Re-serve the upstream zip as-is (proxy/mirror), or
- Host a pre-downloaded copy on the Flying-Phoenix CDN

### `emulator=php`

Serve the **Windows PHP NTS x64** zip.

> **NTS (Non-Thread-Safe) is required.** The TS (Thread-Safe) build does not
> work with php-cgi.exe in FastCGI mode.

| Field           | Value |
|-----------------|-------|
| Current version | `8.3.20` |
| Build variant   | NTS, Win32, VS16, x64 |
| Official source | `https://windows.php.net/downloads/releases/php-8.3.20-nts-Win32-vs16-x64.zip` |
| Expected root   | `php-8.3.20-nts-Win32-vs16-x64\` (single top-level folder) |
| Key binary      | `php-cgi.exe` |

---

## Adding the handler to the API (example)

The existing `/Trion/DownloadFile` endpoint likely already handles `emulator=mysql`.
Add the two new cases to the same switch/map:

```csharp
// Example — adapt to your actual API framework

app.MapGet("/Trion/DownloadFile", async (string emulator, HttpContext ctx) =>
{
    var (fileName, filePath) = emulator.ToLowerInvariant() switch
    {
        "mysql" => ("mysql-8.4.8-winx64.zip",                     _config["Packages:MySqlPath"]),
        "nginx" => ("nginx-1.28.2.zip",                            _config["Packages:NginxPath"]),
        "php"   => ("php-8.3.20-nts-Win32-vs16-x64.zip",          _config["Packages:PhpPath"]),
        _       => throw new BadHttpRequestException("Unknown package")
    };

    ctx.Response.ContentType = "application/zip";
    ctx.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{fileName}\"";
    await ctx.Response.SendFileAsync(filePath);
});
```

If you proxy the upstream URL instead of hosting locally:

```csharp
"nginx" => await ProxyDownloadAsync(ctx, "https://nginx.org/download/nginx-1.28.2.zip"),
"php"   => await ProxyDownloadAsync(ctx, "https://windows.php.net/downloads/releases/php-8.3.20-nts-Win32-vs16-x64.zip"),
```

---

## Upgrading a package version

When you want to ship a newer nginx or PHP version:

1. Update the version constants and CDN URLs in the client:

   ```csharp
   // src/Trion.Core/Constants/ExternalLinks.cs
   public const string NginxVersion     = "1.29.0";           // ← new version
   public const string NginxCdnDownload = "https://nginx.org/download/nginx-1.29.0.zip";

   public const string PhpVersion       = "8.3.21";
   public const string PhpCdnDownload   = "https://windows.php.net/downloads/releases/php-8.3.21-nts-Win32-vs16-x64.zip";
   ```

2. Update the API to serve the new zip under the same `emulator=nginx` /
   `emulator=php` keys.

3. The client will detect the new zip at next install or reinstall — existing
   installations are unaffected until the user clicks **Reinstall**.

---

## Supporter tier vs. free tier

The client sends `X-API-Key` when the user has an API key stored
(`AppSettings.AccountApiKey`).  The API can use this to:

- Serve packages from a faster CDN for paying supporters
- Apply rate-limiting on the free tier
- Log download statistics per user

If no key is present (guest / free user), the header is omitted.  The API
**must still serve the zip** to keyless requests — the header is optional, not
required for authentication.

---

## Client download flow (for reference)

```
InstallWebServerCommand (Settings → Web Server → Download & Install)
    └─ RunStartupCheckAsync()
        ├─ GET /Trion/DownloadFile?emulator=nginx  (X-API-Key if available)
        │   ├─ 200 OK → stream to temp zip → extract to webserver/nginx/
        │   └─ non-200  → fallback: GET https://nginx.org/download/nginx-1.28.2.zip
        │
        ├─ GET /Trion/DownloadFile?emulator=php    (X-API-Key if available)
        │   ├─ 200 OK → stream to temp zip → extract to webserver/php/
        │   └─ non-200  → fallback: GET https://windows.php.net/…
        │
        ├─ GenerateNginxConf()   → webserver/nginx/conf/nginx.conf
        ├─ GeneratePhpIni()      → webserver/php/php.ini
        ├─ GenerateSelfSignedCert() (if HTTPS enabled)
        ├─ AppSettings.WebServerInstalled = true
        └─ StartAsync()  →  php-cgi.exe -b 127.0.0.1:9000  +  nginx.exe
```

Progress text (including download speed and ETA) is streamed back through
`INginxPhpSetupService.ProgressChanged` and displayed in the settings panel's
progress bar while `IsWebServerBusy` is `true`.
