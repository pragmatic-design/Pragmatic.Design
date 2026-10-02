# Examples — pragmatic-use-privacy

Copied from `examples/time-off/src`, which compiles in the repository and is exercised by
`examples/time-off/tests/TimeOff.IntegrationTests`. **Do not edit here**: change the source and run
`node scripts/sync-skill-examples.mjs`. The gate refuses a copy that differs from its source.

| File | What it shows |
|---|---|
| [`TimeOff.Leave/Employees/Employee.cs`](TimeOff.Leave/Employees/Employee.cs) | `[DataSubject]`, and `[PersonalData]` with a category and an erasure strategy on each field — pseudonymized where a column must stay unique, anonymized otherwise |
| [`TimeOff.Leave/LeaveRequests/LeaveRequest.cs`](TimeOff.Leave/LeaveRequests/LeaveRequest.cs) | `[LinksToSubject]`, and the one crypto-shredded field: a `ProtectedValue` with `ErasureStrategy.DestroyKey` |
| [`TimeOff.Leave/Employees/Actions/ExportMyPersonalDataAction.cs`](TimeOff.Leave/Employees/Actions/ExportMyPersonalDataAction.cs) | An access request answered from the classification with `ISubjectAccess.CollectAsync` |
| [`TimeOff.Leave/Employees/Actions/EraseEmployeeAction.cs`](TimeOff.Leave/Employees/Actions/EraseEmployeeAction.cs) | Erasure with `ISubjectErasure.EraseAsync`, passing the key destroyer the crypto-shredded field needs |
| [`TimeOff.Leave/Infrastructure/Privacy/CloseTheAccount.cs`](TimeOff.Leave/Infrastructure/Privacy/CloseTheAccount.cs) | An `IErasureStep` for what the classification cannot reach: the owned account |
| [`TimeOff.Leave/LeaveRequests/Actions/ReadTheReasonAction.cs`](TimeOff.Leave/LeaveRequests/Actions/ReadTheReasonAction.cs) | Reading an encrypted field through `ISubjectDataProtector`: three outcomes, erased among them, and `[RecordAccess]` on the read |
| [`TimeOff.Leave/Compliance/Actions/GetProcessingRegisterAction.cs`](TimeOff.Leave/Compliance/Actions/GetProcessingRegisterAction.cs) | The register of processing activities, built from the code on every call |
| [`TimeOff.Host/Program.cs`](TimeOff.Host/Program.cs) | The host's half: the keys from configuration, the extra erasure step, the controller and the purposes of the register |
