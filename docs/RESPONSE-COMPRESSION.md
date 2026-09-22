# API response compression

Sparta.Api uses ASP.NET Core response-compression middleware to reduce the transfer size of dynamic text responses. Brotli is preferred when a client supports it, with Gzip as the compatibility fallback.

## Decision summary

| Concern | Decision |
|---|---|
| Primary algorithm | Brotli (`br`) |
| Compatibility fallback | Gzip (`gzip`) |
| Compression level | `CompressionLevel.Fastest` for both providers |
| Eligible content | ASP.NET Core default text MIME types plus `application/problem+json` |
| HTTPS | Enabled explicitly, with the security constraints below |
| Rollback | `ResponseCompression:Enabled=false` |
| NuGet dependency | None; the Web SDK supplies the ASP.NET Core shared framework |
| Zstandard | Not adopted on .NET 9 |

## Before and after

### Before

- `Sparta.Api.csproj` used `Microsoft.NET.Sdk.Web` but the application did not register response compression.
- The request pipeline did not call `UseResponseCompression`.
- OData, controller JSON, Problem Details, Scalar HTML, and the OpenAPI document were sent uncompressed by the application.
- A hosting layer such as IIS could still compress a response independently, so a `Content-Encoding` header was not necessarily proof of application behavior.

### After

- Brotli and Gzip are registered explicitly, in that preference order.
- Clients negotiate an encoding through `Accept-Encoding`; unsupported or absent encodings leave the response uncompressed.
- Dynamic responses use the fastest compression level to favor throughput and latency over the smallest possible payload.
- JSON and the framework's other default text MIME types are eligible. `application/problem+json` is added for error responses.
- HTTPS compression is an explicit application setting rather than an accidental hosting-layer behavior.
- Operations can turn the feature off through configuration without a rebuild.

Example configuration:

```json
{
  "ResponseCompression": {
    "Enabled": true,
    "EnableForHttps": true
  }
}
```

Environment-variable overrides use the normal ASP.NET Core form:

```text
ResponseCompression__Enabled=false
ResponseCompression__EnableForHttps=false
```

A configuration change requires an application restart.

## Why no `.csproj` package was added

`Sparta.Api.csproj` uses `Microsoft.NET.Sdk.Web` and targets `net9.0` through `Directory.Build.props`. The Web SDK references the `Microsoft.AspNetCore.App` shared framework, which already contains:

- `ResponseCompressionMiddleware`
- `BrotliCompressionProvider`
- `GzipCompressionProvider`
- their options and dependency-injection extensions

Adding the old standalone `Microsoft.AspNetCore.ResponseCompression` package would be redundant and would introduce another version to maintain. The existing Scalar package change in `Sparta.Api.csproj` is unrelated to response compression.

## Benefits and drawbacks

### Brotli

Benefits:

- Usually produces smaller text payloads than Gzip at comparable settings.
- Is supported by current browsers and many HTTP clients.
- Is automatically preferred when the client advertises `br` with a suitable quality value.

Drawbacks:

- Higher quality settings can be CPU-intensive and increase response latency.
- Some legacy clients and intermediaries do not support Brotli.
- Dynamic compression consumes application CPU on every uncached response.

### Gzip

Benefits:

- Has very broad client and proxy compatibility.
- Provides a dependable fallback when Brotli is not advertised.
- Is well understood by existing operations tooling.

Drawbacks:

- Typically produces larger text payloads than Brotli.
- Still consumes CPU and can be wasteful for small bodies.

### Zstandard and custom providers

Zstandard is not a built-in response-compression provider in ASP.NET Core on .NET 9. A custom or third-party provider would add dependency, interoperability, and proxy-validation costs. Brotli plus Gzip covers the current browser/API-client requirement with no extra package, so Zstandard is deferred until a framework upgrade and measured client demand justify it.

## Why `Fastest`

Sparta produces dynamic OData and JSON rather than precompressed static assets. `CompressionLevel.Optimal` can reduce output further, but it spends more CPU per request and may worsen tail latency under load. `Fastest` is the safer initial production setting. Change it only after benchmarking representative payloads, concurrency, CPU utilization, response size, and p95/p99 latency.

Small responses may gain little or can even grow because of encoding overhead. The middleware has no application-specific minimum body-size setting. If measurements show significant wasted CPU on small bodies, prefer gateway compression with a configured minimum size or introduce a measured custom response-compression policy.

## HTTPS and security

ASP.NET Core disables HTTPS response compression by default because compression can expose a length oracle when a response combines secrets with attacker-controlled content. CRIME/BREACH-style attacks depend on application behavior, not on whether Brotli or Gzip is chosen.

Sparta enables HTTPS compression deliberately because its API authorization uses bearer tokens in the `Authorization` header rather than ambient authentication cookies. This makes it harder for another origin to trigger authenticated victim requests, but it does not make compression universally safe.

Keep these constraints:

1. Do not introduce cookie authentication for API endpoints without reassessing HTTPS compression.
2. Do not reflect attacker-controlled values in responses that also contain CSRF tokens, credentials, access tokens, or other secrets.
3. Keep CORS restricted if browser clients are introduced.
4. Disable compression for a sensitive endpoint or set `ResponseCompression:EnableForHttps=false` if its threat model is uncertain.
5. Do not use response-size differences in externally visible authentication error messages.
6. Revisit the decision during security review and penetration testing.

## MIME-type scope

The framework defaults cover:

- `application/javascript`
- `application/json`
- `application/xml`
- `text/css`
- `text/html`
- `text/json`
- `text/plain`
- `text/xml`

Sparta additionally includes `application/problem+json`. MIME parameters such as the OData metadata parameter do not prevent the underlying JSON media type from matching.

Do not add PNG, JPEG, WebP, ZIP, Gzip, Brotli, PDF, video, or other formats that are already compressed. Recompressing them usually increases CPU cost for little or no size reduction.

## Hosting guidance

Microsoft recommends server-based compression in IIS, Apache, or Nginx when available because it can outperform application middleware. Sparta also runs directly on Kestrel through Aspire, where application middleware gives consistent behavior.

Choose one production owner:

- For direct Kestrel hosting, leave application compression enabled.
- If IIS, Nginx, a gateway, or a CDN owns dynamic compression, benchmark it and set `ResponseCompression:Enabled=false` in Sparta to avoid duplicate negotiation and CPU work.
- With Nginx, verify whether the proxy preserves or removes `Accept-Encoding` before relying on application compression.

The middleware automatically adds `Vary: Accept-Encoding` to compressed responses so shared caches keep encoded and unencoded variants separate.

## Verification

Build first:

```powershell
dotnet build src/Sparta.Api/Sparta.Api.csproj --nologo
```

Run the API in Development and inspect the large OpenAPI response without automatic client decompression:

```powershell
curl.exe -sS -D - -o NUL -H "Accept-Encoding: br" http://localhost:5180/swagger/v1/swagger.json
curl.exe -sS -D - -o NUL -H "Accept-Encoding: gzip" http://localhost:5180/swagger/v1/swagger.json
curl.exe -sS -D - -o NUL http://localhost:5180/swagger/v1/swagger.json
```

Expected headers:

| Request | Expected result |
|---|---|
| `Accept-Encoding: br` | `Content-Encoding: br` and `Vary: Accept-Encoding` |
| `Accept-Encoding: gzip` | `Content-Encoding: gzip` and `Vary: Accept-Encoding` |
| No `Accept-Encoding` | No `Content-Encoding` |
| Feature disabled | No application-generated `Content-Encoding` |

Repeat the test against the production HTTPS path or its staging equivalent. If a proxy is present, compare Kestrel-direct and public responses to establish which layer produced the encoding.

## References

- [Response compression in ASP.NET Core](https://learn.microsoft.com/aspnet/core/performance/response-compression?view=aspnetcore-9.0)
- [ResponseCompressionOptions for ASP.NET Core 9](https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.responsecompression.responsecompressionoptions?view=aspnetcore-9.0)
- [BrotliCompressionProviderOptions for ASP.NET Core 9](https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.responsecompression.brotlicompressionprovideroptions?view=aspnetcore-9.0)
