# Employee updates: canonical profile sources

This change corrects navigation and scalar profile field bindings. No production database was accessed, no schema or data migration was performed, and no salary formulas were changed.

- Navigation considers the financial/payment section and selects one module/link only.
- Employee scalar fields use `Employees`, including PositionId, BranchId, JoiningDate and ContractType. Position name is synchronized with the selected position ID. Assignment choices and validation remain inside the employee's current company; cross-company transfer is not introduced here.
- Financial scalar fields use `EmployeeFinancialInfos`, like the employee profile and FinancialInfo editor. Active allowance totals are read from `EmployeeAllowances`, not the legacy scalar compensation total.
- Dynamic fields retain choices, required metadata and checkbox semantics. Financial dynamic fields require compensation permissions.
- Financial view permissions gate field rendering and history; edit and assignment permissions are rechecked before application.
- Staging and locking are transactional. Locking serializes on the batch, rejects stale before-values and unsupported historical fields, and rolls back the entire batch on rejection. Missing POST fields do not clear unrelated data.

## Deliberate workflow boundaries

Structured/translatable names, family identity number, photo/signature, documents, dependents, contract records, allowance records, deductions and shift assignments keep their existing dedicated editors, linked from EmployeeUpdates. They are not reimplemented as arbitrary scalar/text writes. Tax/GOSI profile assignment and base-mode selectors also retain their FinancialInfo editor for scoped validation. Existing history is retained. Unsupported old open batches must be reviewed and recreated; they are not silently redirected to another data source.

No automatic copy from `EmployeeCompensations` or existing custom fields into canonical financial/employee columns is included. Such a migration would need a separately approved reconciliation using a non-production copy first.

## Verification

Tests include per-field typed entity round trips, null/zero/false preservation, four-decimal invariant financial conversion, malformed values, read-only workflow protection, dynamic choices and 5 synthetic browser navigation cases with both menu scripts loaded. Browser fixtures contain no real employee data and do not connect to the application. These tests are not a production database or full SQL integration test.
