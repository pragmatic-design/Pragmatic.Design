# Examples: pragmatic-use-authorization

Copied from `examples/time-off/src/TimeOff.Leave`, which compiles in the repository and is exercised by
`examples/time-off/tests/TimeOff.IntegrationTests`. **Do not edit here**: change the source and run
`node scripts/sync-skill-examples.mjs`. The gate refuses a copy that differs from its source.

| File | What it shows |
|---|---|
| [`Infrastructure/Authorization/Permissions.cs`](Infrastructure/Authorization/Permissions.cs) | Permissions that are not an entity's CRUD, declared with `[assembly: Permission]`: a description, a category, and why each stands on its own |
| [`Infrastructure/Authorization/EmployeeRole.cs`](Infrastructure/Authorization/EmployeeRole.cs) | A role: `[Role]` and `[Grants]` of generated constants, one of them another module's (`LocalIdentityPermissions`) |
| [`Infrastructure/Authorization/ManagerRole.cs`](Infrastructure/Authorization/ManagerRole.cs) | A role that includes another (`[IncludesRole<EmployeeRole>]`) and adds one permission |
| [`Infrastructure/Authorization/HrAdministratorRole.cs`](Infrastructure/Authorization/HrAdministratorRole.cs) | Granting an entity's whole permission set with the generated `.All` wildcards, and `ViewAll` to see every row |
| [`Infrastructure/Authorization/TeamScopes.cs`](Infrastructure/Authorization/TeamScopes.cs) | The scope a row carries for its team's manager, built with `ScopeIdentifiers.ForRole` |
| [`LeaveRequests/LeaveRequest.cs`](LeaveRequests/LeaveRequest.cs) | `[HasAccessScopes]`: the requester's and the team manager's scopes stamped when the request opens, and moved when it follows its employee to another team |
| [`LeaveRequests/Mutations/ApproveLeaveRequestMutation.cs`](LeaveRequests/Mutations/ApproveLeaveRequestMutation.cs) | `[RequirePermission]` on an operation, while the row filter decides whose rows: another team's request is not found |
