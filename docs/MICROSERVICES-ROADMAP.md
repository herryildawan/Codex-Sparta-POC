# Sparta Web API: microservices roadmap and delivery plan

Status: proposed implementation plan. No services have been extracted or deployed.
Tracking: [Inventory-first microservices migration #4](https://github.com/herryildawan/Codex-Sparta-POC/issues/4). Blazor lookup is tracked separately in [#5](https://github.com/herryildawan/Codex-Sparta-POC/issues/5).
Prepared: 2026-09-19. Scope: existing Sparta API and its connected web client.

## Objective and approach

Evolve the modular monolith into independently deployable Sales and Inventory services while preserving business behavior, XAF permissions, and audit visibility. Extract Inventory first, validate the operational benefit, then decide whether to continue with Sales and asynchronous audit processing.

This roadmap authorizes no production cutover by itself. Estimates below are planning ranges, not delivery commitments. They assume one implementation engineer with QA support, an available staging environment, and timely infrastructure/security decisions.

## Current baseline

2026-09-22 decision: keep contracts in `Sparta.SharedKernel/Contracts/Inventory` for the modular monolith, without a new Contracts project. Product validation/snapshot capture happens on Sales line creation; historical updates preserve snapshots without a catalog re-read. Blazor integration remains planned and reuses OData Product. See [contract decision](MODULAR-CONTRACTS.md). Folder separation alone does not provide independent service deployment.

- Sparta.Api hosts Sales, Inventory, authentication, session capabilities, and audit endpoints in one process.
- Sales and Inventory already have separate databases and EF contexts. Security and Audit use two additional databases.
- Sales references products through logical IDs and immutable snapshots. IProductCatalog currently performs an in-process, secured XAF lookup.
- XAF supplies business permissions, including record ownership and hidden members; Entra authentication does not replace those permissions.
- Aspire and OpenTelemetry provide development orchestration and observability foundations.
- Migrations and seeding currently run from the API host; integration tests currently host the API in process and use real configured SQL databases.
- Audit persistence can fail after a business commit. Stock movement creation currently lacks idempotency. An order does not automatically issue stock; negative stock is allowed.

Evidence: README.md; src/Sparta.Api/Startup.cs; src/Sparta.Modules.Inventory/BusinessObjects/ProductCatalog.cs; src/Sparta.SharedKernel/Contracts/Inventory/IProductCatalog.cs; src/Sparta.SharedKernel/Entity.cs; docs/DEVOPS.md.

## Proposed target

| Component | Ownership and role | Delivery stage |
|---|---|---|
| Gateway | Stable client entry point, routing, request correlation | Inventory pilot |
| Existing API | Sales, existing authentication/session functions, transitional audit reads | Inventory pilot |
| Inventory API | Products, warehouses, stock movements, stock queries, Inventory migrations/database | First extracted service |
| Sales API | Customers, orders, lines, Sales migrations/database | After pilot review |
| Identity and permissions | Entra identity integration plus explicit ownership/distribution of XAF permission data | Design before extraction |
| Audit worker/store | Reliable ingestion, deduplication, retention, authorized reads | Reliability spike first; separate worker later if validated |

Shared libraries are not automatically services. Keep SharedKernel small; extract transport DTOs from EF/XAF base types so API contracts do not require persistence dependencies. Each business service must enforce its own authorization and own its business database. An initial shared Security database is a documented transitional dependency, not full service autonomy.

## Phase 0 — Baseline and measurable goals

Planning range: 3–5 working days. Dependency: none.

- P0.1 Inventory all custom and generated OData routes, session capabilities, audit routes, and web-client consumers.
- P0.2 Run the existing integration suite against dedicated test databases; record current behavior and distinguish pre-existing failures.
- P0.3 Capture response shapes, status codes, OData metadata/query behavior, rowversion/If-Match semantics, and seeded-role permission expectations.
- P0.4 Measure representative API latency, throughput, error rate, and audit delay with a repeatable workload.
- P0.5 Agree on pilot success thresholds: acceptable lookup latency/error rate, availability, maximum audit lag, permission-revocation delay, RPO, and RTO. Record actual values before implementation proceeds.

Deliverables: endpoint/consumer inventory, permission matrix, QA baseline, performance baseline, approved measurable pilot criteria.

Exit gate: existing behavior is reproducible in isolation and success thresholds have named owners.

## Phase 1 — Resolve architecture and framework risks

Planning range: 5–8 working days. Dependency: Phase 0.

- P1.1 Define Inventory and Sales ownership, including controllers, entities, migrations, health checks, and seed responsibilities.
- P1.2 Prove a minimal Inventory-only XAF host can enforce current object/member permissions. Validate module registration, security type metadata, audit integration, and deployment/licensing requirements with the installed DevExpress version.
- P1.3 Define authentication between services: token audiences, trusted callers, user delegation, and service identities. Product lookup must preserve the caller's Inventory permissions; a privileged service identity must not silently broaden access.
- P1.4 Choose a transitional permission model and its failure behavior. If sharing Security storage initially, restrict identities, assign schema ownership, and test missing/unavailable permission data. Record the path toward central policy APIs or service-local permission projections if later required.
- P1.5 Specify an asynchronous, versioned product lookup contract with cancellation, timeouts, unavailable/forbidden/not-found semantics, and immutable product snapshots. Adapt callers explicitly; avoid synchronous blocking of network operations in entity save hooks.
- P1.6 Design gateway compatibility for existing OData paths, $metadata, $batch, and client queries. Do not assume two independent OData models can be merged by routing. Inventory current usage and choose explicit route/model changes where needed.
- P1.7 Assign ownership of /api/session and audit reads; define aggregation and unavailable-service behavior without granting capabilities on failure.
- P1.8 Prototype a durable audit approach compatible with XAF. Establish whether complete actor/member changes can be captured in the same local transaction as the business write. Select and document outbox interception, application-managed events, or a validated reconciliation alternative; do not assume the current audit hook is atomic.

Deliverables: architecture decisions, API/event contract drafts, successful XAF/security and audit spikes, updated estimates.

Exit gate: permission parity is demonstrated, OData compatibility has a feasible solution, and audit durability has a tested design. Rework the plan if a spike fails.

## Phase 2 — Build the extraction foundation

Planning range: 5–8 working days. Dependency: Phase 1.

- P2.1 Add the Inventory API host and gateway; update Aspire orchestration for development.
- P2.2 Move Inventory runtime configuration and migrations to their owner while preserving existing migration history and data. Keep a single migration owner per database.
- P2.3 Add a secured product lookup endpoint and async client adapter. Remove Sales dependence on Inventory implementation types where found.
- P2.4 Add bounded timeouts and safe retry policies. Retry reads only where appropriate; do not retry non-idempotent writes automatically.
- P2.5 Propagate trace context and authenticated actor identity securely. Add dependency metrics and separate readiness/liveness behavior.
- P2.6 Create independently buildable/publishable artifacts, configuration/secrets, restricted runtime database identities, and dedicated migration jobs.
- P2.7 Adapt test infrastructure to launch separate processes and isolated databases. Keep fast module checks alongside distributed integration checks.

Deliverables: locally runnable service topology, independent artifacts, contract tests, migration ownership checks.

Exit gate: Sales can query Inventory through the service boundary with correct caller permissions and trace correlation; Inventory runtime cannot access Sales business data.

## Phase 3 — Inventory pilot and reliability

Planning range: 8–12 working days. Dependency: Phase 2 and validated audit design.

- P3.1 Transfer product, warehouse, stock movement, stock aggregation, and Inventory permission endpoints to Inventory API.
- P3.2 Route Inventory traffic through the gateway; update the web client only where the agreed compatibility design requires it.
- P3.3 Implement durable movement idempotency: scope keys to caller/operation, store request fingerprints and results, reject changed-payload reuse, and handle concurrent duplicate submissions. Commit deduplication state with the business write.
- P3.4 Implement the approved durable audit design for extracted writes, with retry/deduplication, backlog monitoring, and an auditable recovery procedure. If central delivery is asynchronous, preserve authorization for reads and document expected lag.
- P3.5 Remove Inventory endpoint registration from the existing API at cutover. Coordinate routing and deployment so incompatible old/new writers never run simultaneously against the same data.
- P3.6 Exercise full web-client workflows and independently restart, deploy, and roll back Inventory in staging.

Deliverables: staging Inventory service, reliability evidence, client regression report, rollback rehearsal.

Exit gate: permission and business parity pass; duplicate requests cause one movement; failed dependencies do not produce silent data loss; agreed performance/recovery thresholds pass.

## Phase 4 — Review the pilot and optionally extract Sales

Planning range: 5–10 working days. Dependency: Phase 3 and an explicit pilot review decision.

- P4.1 Compare measured release independence, reliability, performance, and operating effort against Phase 0 criteria. Stop at the hybrid architecture if further extraction has no demonstrated benefit.
- P4.2 If proceeding, move Sales endpoints, migrations, and runtime configuration into a dedicated Sales host.
- P4.3 Add idempotency to order creation where ambiguous failures/retries could create duplicates; preserve unique business keys and existing concurrency rules.
- P4.4 Finalize ownership of authentication, session aggregation, and audit querying before retiring the original host.
- P4.5 Apply the validated audit delivery mechanism to Sales and verify historical audit continuity.
- P4.6 Keep product snapshots and existing stock behavior unchanged. Order-driven stock reservation/issuance is a separate business project requiring explicit workflow and compensation rules.

Deliverables: independent Sales release path, complete routing/ownership map, retirement or retained-role decision for the original API.

Exit gate: each service can release independently without coordinating a release of the other for compatible changes.

## Phase 5 — Production readiness and controlled rollout

Planning range: 5–10 working days. Dependency: Phase 3 for an Inventory-only rollout, or Phase 4 for both services.

- P5.1 Extend docs/DEVOPS.md with service-specific pipelines, TLS, secret rotation, least-privilege identities, dependency checks, and environment configuration.
- P5.2 Choose deployment infrastructure based on organizational operations. Separate IIS applications or other supported hosts can be evaluated; Kubernetes is not a prerequisite.
- P5.3 Configure durable telemetry, alerts, dependency dashboards, audit backlog limits, and operational ownership.
- P5.4 Rehearse schema compatibility, traffic rollback, backup restore, dependency outages, outbox replay, and reconciliation.
- P5.5 Define cutover checkpoints: backup/recovery verification, additive migrations, controlled traffic switch, smoke tests, and threshold-based rollback.
- P5.6 Validate rollback against already committed data. Rolling back an application does not roll back its database or replayed events.
- P5.7 Obtain the organization's release approval for the specific validated deployment and monitor the agreed stabilization window.

Deliverables: release evidence, runbooks, recovery results, approved rollout package.

Exit gate: operational owners accept the measured SLO/recovery results and the release checklist passes.

## QA work packages and acceptance evidence

| Area | Required scenarios | Passing evidence |
|---|---|---|
| Security | All existing roles; own/other orders; hidden fields; direct service access; revoked permissions; invalid/wrong-audience tokens | Same permitted data/actions as baseline; no gateway bypass or privilege expansion |
| Product lookup | Active/inactive/missing/unreadable products; timeout; caller identity propagation | Valid snapshots; explicit failure semantics; no unauthorized lookup |
| API compatibility | Custom routes, OData CRUD/query/metadata/batch where used, session capabilities, web client flows | Approved contracts and consumer checks pass |
| Concurrency | Stale rowversion, missing/stale If-Match, concurrent updates | Existing protections preserved; no silent lost update |
| Movements | Signed corrections, zero/invalid quantities, inactive references, future timestamps, PATCH/DELETE attempts | Append-only history and current business rules preserved |
| Idempotency | Repeated key, changed payload, concurrent duplicates, response lost after commit | Exactly one business effect for a valid repeated operation; deterministic conflict/replay behavior |
| Audit | Store/worker outage, commit boundary failure, crash/restart, duplicate delivery, unauthorized reads | Durable recoverable records, no duplicate logical audit entries, bounded measured lag, correct filtering |
| Failure isolation | Inventory unavailable, Security unavailable, gateway restart | Documented dependency failures; independent functions remain usable where their dependencies permit |
| Deployment | Independent release, compatible old/new versions, interrupted migration, route rollback | Compatible traffic/data and rehearsed recovery within agreed targets |
| Performance | Repeat baseline workload through gateway and service calls | Phase 0 thresholds met with recorded dependency latency and error rates |

Tests must use isolated environments. Existing integration checks can mutate seeded product values and create permanent history; never point them at production. Extend tests for new distributed failure modes instead of merely copying implementation details.

## Sequencing, effort, and first backlog

Critical path: baseline → architecture spikes → service foundation → Inventory pilot → pilot decision → optional Sales extraction → production rollout. Production preparation can begin during the pilot, but release gates remain sequential.

Total planning range if all phases proceed: 31–53 engineering working days, approximately 7–11 working weeks for one implementation engineer, excluding external approvals/infrastructure waits and additional stabilization time. Re-estimate after Phase 1; authentication, XAF audit behavior, and OData compatibility are the largest uncertainties.

Suggested first sprint:

1. P0.1/P0.3: endpoint, consumer, and permission inventory — implementation + QA.
2. P0.2: isolated regression baseline — QA.
3. P0.4/P0.5: measurements and numeric success criteria — QA + service owner.
4. P1.1/P1.2: Inventory-only host and XAF security spike — implementation.
5. P1.3/P1.4: identity delegation and permission decision — implementation + security owner.
6. P1.6/P1.8: OData and durable-audit feasibility spikes — implementation + QA.

Create a tracked work item for each P-number when a project tracker is chosen. Each item should include an owner, dependencies, acceptance evidence, and rollback implications. This document is the planning artifact; external work items have not been created.

## Scope controls

- Do not combine extraction with a DevExpress/.NET/EF upgrade unless a validated blocker requires it.
- Do not create one service per entity/table.
- Do not add automatic inventory issuance, reservation, or new stock constraints as part of extraction.
- Do not introduce a message broker before the delivery requirements and audit spike justify the choice; a durable SQL outbox with a worker is a candidate, not an assumed implementation.
- Preserve historical IDs, attribution, permissions, and audit readability throughout migration.

Reference: [Microsoft microservices architecture guidance](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/architect-microservice-container-applications/microservices-architecture).
