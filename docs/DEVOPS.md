# Deployment roadmap

## Common delivery flow

1. Pull-request restore/build, dependency/security checks and integration suite against isolated SQL databases.
2. Publish an immutable API artifact and versioned migration scripts/bundles for all four contexts.
3. Promote the same artifact through staging and production; inject environment configuration.
4. Apply backward-compatible migrations in a dedicated deployment job with a migration identity.
5. Deploy, warm up, test health and role-isolated API access, then shift traffic.
6. Monitor error rate/latency, SQL health and audit failures. Retain the prior compatible artifact.

Use the organization's existing Git/CI provider; Azure DevOps YAML Pipelines is a suitable default. DevExpress feed credentials belong in CI secret storage. Only trusted deployment jobs should access internal infrastructure. Do not run integration tests against production databases.

## On-premises first staging

- Dedicated Windows Server/IIS host with the .NET 9 Hosting Bundle for the `net9` branch, HTTPS certificate and restricted service identity.
- Separate staging Security/Sales/Inventory/Audit databases. BGALT-NAP02 is the current development server, not an implied production target.
- Versioned publish folders; for reduced downtime, use two API instances behind a reverse proxy/load balancer.
- OpenTelemetry Collector to an approved persistent log/trace/metric backend.
- SQL backup schedules, off-host copies, tested restores and certificate/OS/SQL patch ownership.
- If running multiple API instances, share signing material and any required ASP.NET Data Protection keys appropriately.

## Azure validation

- App Service for the single API; four Azure SQL databases after compatibility testing.
- Managed identity for Azure SQL/Key Vault; configure least-privilege database users.
- Private networking and restricted management endpoints according to organization policy.
- Application Insights/Azure Monitor via OpenTelemetry, with cost/retention budgets.
- Bicep or Terraform for repeatable environments.
- A deployment slot on a supporting App Service tier, with environment-specific settings and identities validated before swap.
- Slot rollback restores application content, not a database schema. Avoid incompatible migrations.

## Release and recovery gates

Define availability, RPO and RTO before sizing. Test partial migration failures, application rollback, audit-writer failure, restore consistency across four databases and interrupted future background jobs. Keep cross-database operations explicit. No production deployment or external CI connection has been created by the POC.
