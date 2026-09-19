# Architecture verification

## Planned security scenario

[SEC-MAT-001: material access restricted by role](QA-MATERIAL-ROLE-ISOLATION.md) specifies Role X access to Material A and Role Y access to Material B, including API mutations, related-data leakage, combined roles, and revocation checks. Status: **planned; not automated or executed**. This scenario is separate from the historical results below.

## Historical verification results

Validated on 2026-09-18 against the four POC databases on BGALT-NAP02.

Release build: **0 warnings, 0 errors**. Integration suite: **60 assertions passed**.

- .NET 10 API and modular libraries compile with pinned DevExpress 26.1.4 / EF Core 8.0.28.
- No XAF Blazor, WinForms or Middle Tier projects or corresponding UI packages in the API dependency graph.
- Separate EF migration histories and correct security/business/audit table ownership.
- SQL integer identity keys on custom business entities; native XAF security/audit keys retained.
- Shared JWT identities, deny-by-default roles, explicit association permissions, owner-only order access.
- Both generated OData and custom endpoints tested.
- Cross-module product lookup validates Inventory access and product existence when creating sales lines.
- Server-assigned user ID/username snapshots and protection against attribution changes.
- Member-level restrictions prevent note/cost disclosure through both data and audit endpoints.
- Audit endpoints enforce module and record permissions, return trace correlation metadata and cap page size.
- HTTP/SQL/business traces and business metrics emitted; SQL statement text excluded.
- Aspire AppHost started and its API returned HTTP 200 Healthy through the managed endpoint.

## Audit consistency findings

The integration suite creates separate in-process API hosts for fault injection. It never takes an existing SQL database offline.

| Injected failure | HTTP result | Business row committed |
|---|---|---|
| Audit connection points to a nonexistent database | 500; readiness 503 | No |
| Audit SaveChanges interceptor throws before persistence | 500 | **Yes** |

This is a demonstrated cross-database consistency boundary. The POC is not a guarantee of durable audit delivery. Add a transactional business-database outbox and central audit delivery/reconciliation before production if audit completeness is mandatory. Retry handling must account for an already-committed business row.

## Running the checks

`dotnet run --project tests/Sparta.IntegrationTests` runs the executable assertions against real configured POC databases. `scripts/Verify.ps1` additionally builds Release and applies migrations/seeding first. Expected injected-failure logs appear during successful test runs; the final SUCCESS line and process exit code indicate the result.

Test runs leave uniquely identified synthetic orders and their audit history. They are not suitable for production connections. Raw local test/build logs are under git-ignored `artifacts/`.
# Entra authentication update

The subsequent connected Web UI milestone extends the API suite to **81 passing assertions**. It adds current-user/selected-product permission checks, manager product updates, reader denial, stale-version rejection and audit visibility. The separate Web UI browser suite passed 16 checks; see the sibling `Build Sparta Web/VERIFICATION.md`.

The extended integration suite passed **73 assertions**, including 13 Entra checks using ephemeral RSA signing keys and test discovery metadata. These cover linked-user authentication, XAF RBAC despite misleading token username/role claims, issuer/audience/tenant/signature/lifetime/scope rejection, unlinked and inactive users, Swagger OAuth metadata, and disabling the password endpoint in Entra-only mode. Temporary Entra test accounts and login links are removed afterward.

The supplied tenant and application identifiers are stored in local User Secrets. Real Microsoft sign-in remains unverified pending confirmation of the exposed scope, SPA redirect registration/consent, and the user's Entra Object ID and intended Sparta account. See [setup](ENTRA.md).
