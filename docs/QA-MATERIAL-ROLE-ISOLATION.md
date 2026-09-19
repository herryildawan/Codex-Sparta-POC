# QA scenario: material access restricted by role

Scenario ID: SEC-MAT-001  
Project: Sparta Web API  
Priority: High  
Status: Planned — not implemented as automated tests and not executed  
Added: 2026-09-19

## Requirement

Treat material as the existing Inventory Product entity. Role X may read and edit Material A only; Role Y may read and edit Material B only. All other products are inaccessible. Creation and deletion are denied for both roles in this scenario. Field permissions still apply within allowed records.

This is a new acceptance scenario. Existing Inventory reader/manager roles grant broader product access; existing sales ownership tests demonstrate the mechanism but do not prove this material restriction.

## Fixtures and permissions

Use isolated QA databases with unique run-prefixed fixture names. Do not alter existing demo accounts or their roles.

| Fixture | Configuration |
|---|---|
| Material A, Material B | Active Products with distinct IDs, codes, names, costs, and known stock quantities |
| Material C | Active unassigned Product to prove access is an allowlist |
| Role X | DenyAllByDefault; Product read/write object criteria matching A's actual ID |
| Role Y | DenyAllByDefault; Product read/write object criteria matching B's actual ID |
| User X | Role X only, plus minimum self/authentication permissions |
| User Y | Role Y only, plus minimum self/authentication permissions |
| User XY | Both roles, to test combined allow permissions |
| User None | Authentication/self permissions only; no Product grants |
| QA administrator | Fixture setup and authoritative persisted-state inspection only |

Do not hard-code product IDs or use mutable product codes as authorization keys. Both scoped roles deny access to StandardCost for the field-security checks and receive no administrative or broad Inventory role. Configure related-object grants separately; restricting Product does not automatically restrict every object containing its ID or a copied name.

For related-data cases, add scoped movement-read and Inventory audit-read permissions sufficient to exercise permitted A/B data. For sales-line cases, add narrowly scoped Sales permissions allowing test order/line creation and reads. These supplemental grants must not broaden Product access. Record the exact role configuration used.

## Expected access matrix

| User | Material A read/edit | Material B read/edit | Material C read/edit | Create/delete Product |
|---|---|---|---|---|
| X | Allow | Deny | Deny | Deny |
| Y | Deny | Allow | Deny | Deny |
| XY | Allow | Allow | Deny | Deny |
| None | Deny | Deny | Deny | Deny |

The XY expectation applies to these two scoped allow roles with no conflicting object denies. A separate broad-role case below must expose configuration that widens access rather than claiming the narrow role always wins.

## Test cases

Run symmetric cases with X/A/B and Y/B/A. Authenticate as the actual test user; UI-only filtering is not acceptance evidence.

| ID | Action | Expected result |
|---|---|---|
| MAT-01 | List /api/odata/Product as each user | X sees A only; Y sees B only; XY sees A and B; None sees none; C never appears |
| MAT-02 | Request allowed and disallowed Products by OData key | Allowed record is readable; disallowed record body exposes no protected data |
| MAT-03 | Query by denied ID/code using $filter, $select, $count and supported query options | No denied record, name, cost, or count contribution is disclosed |
| MAT-04 | PUT /api/inventory/products/{id} for the permitted record with current RowVersion | Update succeeds and persists; attribution remains unchanged |
| MAT-05 | PUT a disallowed record with an otherwise valid body and version supplied by the fixture | Custom endpoint returns 404; authoritative inspection confirms no change |
| MAT-06 | PATCH permitted/disallowed Products through generated OData endpoints | Permission enforcement matches custom API; denied writes never persist |
| MAT-07 | Create a Product, then attempt deletion of existing allowed/disallowed Products through applicable custom and OData routes | Both operations denied; use otherwise valid requests and an unreferenced product for delete checks so business validation cannot mask authorization |
| MAT-08 | Read/select StandardCost on allowed products; try changing it through custom and OData endpoints | Cost is hidden according to the endpoint contract; no cost write persists |
| MAT-09 | Update permitted material using a stale RowVersion | Custom endpoint returns 409 and does not overwrite newer data |
| MAT-10 | Query product permission/capability endpoints and inspect web-client controls | Per-record permissions match access matrix; direct API calls remain protected regardless of control visibility |
| MAT-11 | Read StockMovement, stock totals, and supported Product expansions | Permitted material data remains usable; disallowed material quantities, references, names, and IDs do not leak |
| MAT-12 | Create a sales line referencing permitted versus disallowed material | Permitted lookup succeeds with appropriate Sales grants; disallowed lookup creates no line or product snapshot |
| MAT-13 | Read existing sales-line snapshots for a disallowed material under the strict material-isolation requirement | No copied material data leaks; if current independent Sales permissions expose it, record a gap requiring explicit policy/implementation resolution |
| MAT-14 | Read /api/Inventory/audit?entityType=Product&entityId={id} for both materials | Scoped audit role does not bypass current record/member permissions; denied material and hidden cost remain undisclosed |
| MAT-15 | Remove Role X from User X after login and repeat calls with the same valid token | Next request reflects current permissions; prior access is not retained through token role claims or cached permissions |
| MAT-16 | Add/remove Role Y for User X and repeat reads | Access follows current assigned grants; removing Y removes B access |
| MAT-17 | Temporarily assign a broad Inventory reader role to a separate scoped test user | Capture effective access and flag broadened access as incompatible with the strict role-assignment policy; remove the broad role afterward |
| MAT-18 | Rename an allowed Product's code without changing its ID | ID-based access remains stable; code changes cannot move material ownership |
| MAT-19 | Anonymous requests and manual URL/body tampering with disallowed IDs | Authentication/authorization holds; no unauthorized state change or data disclosure |

For generated OData denials, capture the actual status and response shape before pinning endpoint-specific assertions. A collection may validly return HTTP 200 with an empty result. Do not treat absence of HTTP 403 as access, or an arbitrary 500/validation error as proof of correct authorization. Disallowed writes require persisted-state verification as well as response checks.

Related-data cases express the requested strict material boundary, including historical snapshots. They are not claims about current behavior. If business owners require a separate Sales-history visibility policy, record that decision and revise MAT-13 explicitly before sign-off.

## Execution and evidence

1. Build and start the API using dedicated QA databases. Record build/version and database environment without secrets.
2. Create fixtures and criteria using their generated IDs; capture the permission configuration.
3. Execute all applicable cases against both generated and custom endpoints. Use fresh versions for successful writes and valid non-security fields for negative checks.
4. Save sanitized requests/responses, actual versus expected results, and before/after state for denied writes. Never retain passwords or bearer tokens in reports.
5. Record findings with case ID, affected user/role, endpoint, reproducible steps, and severity. Cross-material disclosure or mutation is a release-blocking security defect for this feature.
6. Clean up only run-owned data, users, and roles in dependency order; retain audit evidence according to QA retention policy. Never delete unrelated business history.

Result template: Case ID | Build | User/roles | Expected | Actual | Pass/Fail/Blocked | Evidence | Defect.

## Implementation backlog and completion gate

- Add isolated fixture creation/cleanup and scoped-role setup to the integration test infrastructure.
- Automate API access-matrix, mutation, role-refresh, and leakage cases in tests/Sparta.IntegrationTests; keep UI verification in the connected web-client suite.
- Add related-record criteria or endpoint fixes wherever the new tests identify missing material scoping.
- Link the executed evidence and defects from docs/VERIFICATION.md.

Completion requires all cases to pass (or an explicitly documented requirement revision), no unresolved cross-material disclosure/mutation, and a repeatable isolated test run. Documentation of this scenario alone does not establish a passed security test.

Re-run these cases before and after the Inventory extraction described in [the microservices roadmap](MICROSERVICES-ROADMAP.md), including direct service access and caller identity propagation.
