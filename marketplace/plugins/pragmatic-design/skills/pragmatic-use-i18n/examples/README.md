# Examples — pragmatic-use-i18n

Copied from `examples/time-off/src`, which compiles in the repository and is exercised by
`examples/time-off/tests/TimeOff.IntegrationTests`. **Do not edit here**: change the source and run
`node scripts/sync-skill-examples.mjs`. The gate refuses a copy that differs from its source.

| File | What it shows |
|---|---|
| [`TimeOff.Leave/translations/it.json`](TimeOff.Leave/translations/it.json) | A module's translations: validation messages, and each error's title and detail under its code, with named `{placeholders}` |
| [`TimeOff.Leave/Infrastructure/Internationalization/TranslationKeys.cs`](TimeOff.Leave/Infrastructure/Internationalization/TranslationKeys.cs) | `[assembly: TranslationKeys]`: the keys as symbols (`T`, `TKeys`), the texts read by the host at run time |
| [`TimeOff.Leave/LeaveRequests/Actions/SubmitLeaveRequestAction.cs`](TimeOff.Leave/LeaveRequests/Actions/SubmitLeaveRequestAction.cs) | Validation rules naming their message with `MessageKey = TKeys.…`, so a renamed key is a compile error |
| [`TimeOff.Leave/Employees/Employee.cs`](TimeOff.Leave/Employees/Employee.cs) | `[ProfileProperty] PreferredCulture` on the `[PragmaticUser]`: the generator writes the provider that answers in the employee's language |
| [`TimeOff.Leave/Employees/Actions/ChooseMyLanguageAction.cs`](TimeOff.Leave/Employees/Actions/ChooseMyLanguageAction.cs) | Changing that language, and dropping the cached entry with `InvalidateCache()` |
| [`TimeOff.Host/Program.cs`](TimeOff.Host/Program.cs) | `UseI18N` — default and supported cultures, JSON translations beside the application, localized problem details — and `UseUserCulture()` |
| [`TimeOff.Leave/AbsenceKinds/AbsenceKind.cs`](TimeOff.Leave/AbsenceKinds/AbsenceKind.cs) | `LocalizedString`: a name HR writes in each language and a reader gets in theirs |
