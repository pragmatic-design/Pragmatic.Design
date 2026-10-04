# Examples: pragmatic-use-foundation

Copied from `examples`, which compiles in the repository and is exercised by
`examples/invoicing/tests/Invoicing.IntegrationTests` and `examples/showcase/tests/Showcase.IntegrationTests`. **Do not edit here**: change the source and run
`node scripts/sync-skill-examples.mjs`. The gate refuses a copy that differs from its source.

| File | What it shows |
|---|---|
| [`invoicing/src/Invoicing.Billing/Payments/Errors/OverpaymentError.cs`](invoicing/src/Invoicing.Billing/Payments/Errors/OverpaymentError.cs) | A typed error: a code, a status (422, and why not 409), and the values it carries, as named parameters for the localized message and extensions in the problem details |
| [`invoicing/src/Invoicing.Billing/Payments/Actions/RecordPaymentAction.cs`](invoicing/src/Invoicing.Billing/Payments/Actions/RecordPaymentAction.cs) | An action answering `Result<T, IError>` with the errors it declares, and `[PositiveMoney]`/`[MaxLength]` on its input |
| [`invoicing/src/Invoicing.Registry/Customers/Mutations/CreateCustomerMutation.cs`](invoicing/src/Invoicing.Registry/Customers/Mutations/CreateCustomerMutation.cs) | Validation on an input: `[Required]`, `[Email]`, `[OneOf]`, `[Range]`, `[MaxLength]` |
| [`invoicing/src/Invoicing.Registry/Customers/Dtos/CustomerDto.cs`](invoicing/src/Invoicing.Registry/Customers/Dtos/CustomerDto.cs) | A DTO: `[MapFrom<Customer>]` and `[GenerateProjection]` |
| [`invoicing/src/Invoicing.Registry/Customers/Dtos/PostalAddressDto.cs`](invoicing/src/Invoicing.Registry/Customers/Dtos/PostalAddressDto.cs) | A value object mapped as one value, nested rather than flattened |
| [`invoicing/src/Invoicing.Billing/Invoices/InvoiceSpecifications.cs`](invoicing/src/Invoicing.Billing/Invoices/InvoiceSpecifications.cs) | Specifications in the generated container: a named rule composed with `&`, and a cutoff captured as a value instead of computed inside the expression |
| [`showcase/src/Showcase.Catalog/Amenities/Mutations/PatchAmenityDto.cs`](showcase/src/Showcase.Catalog/Amenities/Mutations/PatchAmenityDto.cs) | `[GeneratePatch<Amenity>]`: an absent field is not a null one |
| [`showcase/src/Showcase.Catalog/Amenities/Endpoints/PatchAmenityEndpoint.cs`](showcase/src/Showcase.Catalog/Amenities/Endpoints/PatchAmenityEndpoint.cs) | An HTTP PATCH applying it with `ApplyTo` and answering the members it set |
