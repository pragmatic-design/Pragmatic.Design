---
title: Diagnostics
description: Complete reference of Pragmatic compile-time diagnostics (PRAG codes).
---

Pragmatic Design emits compile-time diagnostics prefixed with `PRAG` to guide correct usage of its attributes, detect configuration issues, and surface framework misuse early. They come from three places:

- the **unified source generator** (`Pragmatic.SourceGenerator`), which reports while generating code;
- **analyzers** that inspect your own code (`Pragmatic.Result.Analyzers`, `Pragmatic.Persistence.Analyzers`, `Pragmatic.Temporal.Analyzers`, `Pragmatic.Abstractions.Analyzers`, `Pragmatic.SourceGenerator.Analyzers`);
- **diagnostic suppressors** (`PRAGS###`), which turn *off* compiler and analyzer warnings that are false alarms in generated code.

All of them are visible in the IDE (Rider, Visual Studio, VS Code) and in `dotnet build` output.

## Severity legend

- **Error**: compilation fails; the construct is invalid or the generated code would not work.
- **Warning**: compilation succeeds; the construct is likely wrong or will behave surprisingly.
- **Info**: hint; the construct is unusual but not incorrect.
- **Hidden**: not shown by default; raise it in `.editorconfig` when you want to act on it.

## ID ranges by module

| Range | Owner | Source |
|-------|-------|--------|
| `PRAG0001-0099` | Result | [`Pragmatic.Result.Analyzers`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.Result/src/Pragmatic.Result.Analyzers/DiagnosticDescriptors.cs) |
| `PRAG0100-0199` | Ensure | [`Pragmatic.Ensure.Analyzers`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.Ensure/src/Pragmatic.Ensure.Analyzers/DiagnosticDescriptors.cs) |
| `PRAG0200-0299` | Validation | [`Features/Validation/Diagnostics/`](https://github.com/pragmatic-design/Pragmatic.Design/tree/main/Pragmatic.SourceGenerator/src/Pragmatic.SourceGenerator/Features/Validation/Diagnostics) |
| `PRAG0300-0399` | Mapping | [`Features/Mapping/Diagnostics/`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.SourceGenerator/src/Pragmatic.SourceGenerator/Features/Mapping/Diagnostics/MappingDiagnostics.cs) |
| `PRAG0400-0449` (plus `0450-0462`, in use while the range extension is decided) | Actions | [`Features/Actions/Diagnostics/`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.SourceGenerator/src/Pragmatic.SourceGenerator/Features/Actions/Diagnostics/ActionsDiagnostics.cs) |
| `PRAG0500-0599` | Endpoints | [`Features/Endpoints/Diagnostics/`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.SourceGenerator/src/Pragmatic.SourceGenerator/Features/Endpoints/Diagnostics/EndpointsDiagnostics.cs) |
| `PRAG0600-0699` | Persistence and EF Core | [`Features/Persistence/Diagnostics/`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.SourceGenerator/src/Pragmatic.SourceGenerator/Features/Persistence/Diagnostics/PersistenceDiagnostics.cs), [`Pragmatic.Persistence.Analyzers`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.Persistence/src/Pragmatic.Persistence.Analyzers/DiagnosticDescriptors.cs) |
| `PRAG0700-0799` | Persistence: query pipeline | [`QueryPipelineDiagnostics.cs`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.SourceGenerator/src/Pragmatic.SourceGenerator/Features/Persistence/Diagnostics/QueryPipelineDiagnostics.cs) |
| `PRAG0800-0899` | Messaging | [`Features/Messaging/Diagnostics/`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.SourceGenerator/src/Pragmatic.SourceGenerator/Features/Messaging/Diagnostics/MessagingDiagnostics.cs) |
| `PRAG0900-0999` | Temporal | [`Pragmatic.Temporal.Analyzers`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.Temporal/src/Pragmatic.Temporal.Analyzers/DiagnosticDescriptors.cs), [`Features/Temporal/Diagnostics/`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.SourceGenerator/src/Pragmatic.SourceGenerator/Features/Temporal/Diagnostics/TemporalDiagnostics.cs) |
| `PRAG1000-1099` | Identity and Authorization | [`Features/Identity/Diagnostics/`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.SourceGenerator/src/Pragmatic.SourceGenerator/Features/Identity/Diagnostics/IdentityDiagnostics.cs) |
| `PRAG1100-1199` | Persistence: data ownership | [`OwnershipDiagnostics.cs`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.SourceGenerator/src/Pragmatic.SourceGenerator/Features/Persistence/Diagnostics/OwnershipDiagnostics.cs) |
| `PRAG1400-1499` | Dependency injection | [`Pragmatic.Abstractions.Analyzers`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.Abstractions/src/Pragmatic.Abstractions.Analyzers/DiagnosticDescriptors.cs) |
| `PRAG1600-1699` | Composition, DI registration, host aggregation | [`Features/Composition/Diagnostics/`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.SourceGenerator/src/Pragmatic.SourceGenerator/Features/Composition/Diagnostics/CompositionDiagnostics.cs) |
| `PRAG1700-1799` | Caching | [`Features/Caching/Diagnostics/`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.SourceGenerator/src/Pragmatic.SourceGenerator/Features/Caching/Diagnostics/CachingDiagnostics.cs) |
| `PRAG1800-1899` | Internationalization | [`Features/I18n/Diagnostics/`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.SourceGenerator/src/Pragmatic.SourceGenerator/Features/I18n/Diagnostics/I18NDiagnostics.cs) |
| `PRAG1900-1999` | Documents | [`Pragmatic.Documents.Csv.Generator`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.Documents/src/Pragmatic.Documents.Csv.Generator/Diagnostics/CsvDiagnostics.cs) |
| `PRAG2000-2099` | Configuration | [`Features/Configuration/Diagnostics/`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.SourceGenerator/src/Pragmatic.SourceGenerator/Features/Configuration/Diagnostics/ConfigurationDiagnostics.cs) |
| `PRAG2100-2149` | Notifications | [`Features/Notifications/Diagnostics/`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.SourceGenerator/src/Pragmatic.SourceGenerator/Features/Notifications/Diagnostics/NotificationsDiagnostics.cs) |
| `PRAG2200-2249` | Patch | [`Features/Patch/Diagnostics/`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.SourceGenerator/src/Pragmatic.SourceGenerator/Features/Patch/Diagnostics/PatchDiagnostics.cs) |
| `PRAG2400-2449` | Logging (call sites) | [`Pragmatic.SourceGenerator.Analyzers`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.SourceGenerator/src/Pragmatic.SourceGenerator.Analyzers/LogCallSiteDescriptors.cs) |
| `PRAG2500-2549` | Jobs | [`Features/Jobs/Diagnostics/`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.SourceGenerator/src/Pragmatic.SourceGenerator/Features/Jobs/Diagnostics/JobsDiagnostics.cs) |
| `PRAG2600-2699` | Traits and Resource (shared range) | [`Features/Traits/Diagnostics/`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.SourceGenerator/src/Pragmatic.SourceGenerator/Features/Traits/Diagnostics/TraitDiagnostics.cs), [`Features/Resource/Diagnostics/`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.SourceGenerator/src/Pragmatic.SourceGenerator/Features/Resource/Diagnostics/ResourceDiagnostics.cs) |
| `PRAG2700-2749` | Value objects | [`Features/ValueObject/Diagnostics/`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.SourceGenerator/src/Pragmatic.SourceGenerator/Features/ValueObject/Diagnostics/ValueObjectDiagnostics.cs) |
| `PRAG2750-2799` | Entity lifecycle events | [`Features/Lifecycle/Diagnostics/`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.SourceGenerator/src/Pragmatic.SourceGenerator/Features/Lifecycle/Diagnostics/LifecycleEventsDiagnostics.cs) |
| `PRAG2800-2899` | Serialization / AOT | [`Pragmatic.Abstractions.Analyzers`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.Abstractions/src/Pragmatic.Abstractions.Analyzers/DiagnosticDescriptors.cs) |
| `PRAG2900-2999` | Privacy | [`PrivacyDiagnostics.cs`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.SourceGenerator/src/Pragmatic.SourceGenerator/Features/Privacy/Diagnostics/PrivacyDiagnostics.cs) |
| `PRAG9000-9099` | Source generator infrastructure | [`SafeSourceOutput.cs`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/shared/SourceGen/SafeSourceOutput.cs) |
| `PRAGS001-PRAGS999` | Diagnostic suppressors | [`Suppressors/`](https://github.com/pragmatic-design/Pragmatic.Design/tree/main/Pragmatic.SourceGenerator/src/Pragmatic.SourceGenerator/Suppressors) |

## Every diagnostic, by ID

### Result: `PRAG0001-0002`

| ID | Severity | Meaning and fix |
|----|----------|-----------------|
| `PRAG0001` | Warning | `.Value` is read without checking `IsSuccess`, which may throw. Match on the result, or check `IsSuccess` first. |
| `PRAG0002` | Warning | An error type declares custom properties but is not `partial`, so the generator cannot emit its `WriteExtensions`, and the properties never reach `ProblemDetails`. Add `partial`. |

### Ensure: `PRAG0100`

| ID | Severity | Meaning and fix |
|----|----------|-----------------|
| `PRAG0100` | Info | An argument guard is written by hand where `Ensure` says the same thing. Use `Ensure.ThrowIfNull(…)` and its siblings: the guard vocabulary is one, and a hand-written one drifts from it. |

### Validation: `PRAG0200-0222`

| ID | Severity | Meaning and fix |
|----|----------|-----------------|
| `PRAG0200` | Error | A type carries validation attributes but is not `partial`, so `Validate()` cannot be generated. Add `partial`. |
| `PRAG0201` | Error | A `[Validator]` class does not implement `IValidator<T>`. Implement it. |
| `PRAG0203` | Error | A comparison attribute (`[EqualTo]`, `[GreaterThanProperty]`, …) names a property that does not exist on the type. Fix the name. |
| `PRAG0204` | Warning | `[ValidateElements]` is on a property that is not a collection. Remove it, or change the property type. |
| `PRAG0205` | Error | The element type of a `[ValidateElements]` collection does not implement `ISyncValidator`. Give the element type validation attributes and make it `partial`. |
| `PRAG0209` | Warning | The two properties in a comparison have incompatible types. Compare properties of compatible types. |
| `PRAG0210` | Warning | An attribute on a validated property is not a Pragmatic validation attribute, so no check is generated for it. Use the attributes from `Pragmatic.Validation.Attributes`, or write the rule in a `[Validator]`. |
| `PRAG0215` | Warning | An async validator is declared in an assembly other than the one that declares the operation it validates. Whether the operation awaits a validator is decided where the operation is compiled, which cannot see this one, so it never runs. Move the validator beside the operation, or put `[Validate]` on the operation. |
| `PRAG0220` | Error | `[ValidEnum]` is on a property whose type is not an enum, so it has nothing to check. Remove it, or apply it to an enum property. |
| `PRAG0221` | Error | A validated type is nested in a type that is not `partial`, so the generated code cannot reopen the container. Add `partial` to the enclosing type, or move the validated type out of it. |
| `PRAG0222` | Warning | A `MessageKey` names something the generator cannot read at generation time, so the rule reports its default key instead. Name a generated translation-key constant (`TKeys.…`), or write the key as a string. |
| `PRAG0223` | Error | `[ValidateElements]` written **bare**, where it configures nothing. A collection's elements are validated (with indexed error paths) whenever the element type implements `ISyncValidator`, which is the attribute's own precondition (`PRAG0205` enforces it). Write `[ValidateElements(StopOnFirstError = true)]`, which is the one setting that changes the generated loop, or remove it: the elements are validated either way. ⚠️ Reported **only where the declaring type carries other validation rules**, or a `required` member, and would therefore have a validator without the attribute. On a type whose only annotation is this one, the bare form is legal and has to stay: the entry gate reads attributes and `required` members, so deleting it there leaves the type with no validator at all. |

### Mapping: `PRAG0300-0341`

| ID | Severity | Meaning and fix |
|----|----------|-----------------|
| `PRAG0300` | Error | A `[MapFrom]`/`[MapTo]` type is not `partial`. Add `partial`. |
| `PRAG0302` | Error | The property named in the mapping does not exist on the source type. |
| `PRAG0303` | Warning | A target property has no matching source property. Map it with `[MapProperty]` or skip it with `[MapIgnore]`. |
| `PRAG0304` | Error | Source and target property types cannot be converted. Add `[MapConverter<T>]` or change a type. |
| `PRAG0305` | Error | The converter does not implement `IValueConverter<TFrom, TTo>`. |
| `PRAG0306` | Error | The converter has no public parameterless constructor. |
| `PRAG0307` | Warning | A `required` target property is not mapped. Map it, or drop `required`. |
| `PRAG0309` | Error | A nested DTO type is missing `[MapFrom<T>]`. |
| `PRAG0310` | Error | `[GenerateProjection]` needs `[MapFrom<T>]` on the same type. |
| `PRAG0313` | Info | A circular reference was detected; the generated mapper uses instance tracking. |
| `PRAG0314` | Error | A property has both `[MapIgnore]` and `[MapProperty]`. Remove one. |
| `PRAG0315` | Error | A nested DTO property does not match the source navigation property type. |
| `PRAG0316` | Error | No constructor is suitable for mapping the type's init-only properties. |
| `PRAG0317` | Error | A nullable source maps to a non-nullable target with no `Default`. Set `Default = …`. |
| `PRAG0319` | Warning | `CustomizeMapping` is ignored by the EF projection. It applies to `FromEntity` only. |
| `PRAG0320` | Warning | `[MapConverter]` cannot run in SQL; the property is excluded from the projection. |
| `PRAG0321` | Info | A format string is not translatable to SQL; the property is excluded from the projection. |
| `PRAG0322` | Info | A dictionary with complex values is not supported. Use `[MapIgnore]`. |
| `PRAG0323` | Warning | A property matches both a direct property and a flattening convention. Disambiguate with `[MapProperty]`. |
| `PRAG0324` | Info | The ID property is excluded from `ToEntity()` by default. Use `[MapProperty]` to include it. |
| `PRAG0325` | Hidden | A source property is not mapped to the DTO, so its data is silently dropped. Raise the severity in `.editorconfig` to act on it. |
| `PRAG0326` | Warning | A nested DTO uses a converter, concatenation, format or flattening that cannot be inlined into the projection; those members are omitted. Project the nested DTO via its own `.Projection`. |
| `PRAG0327` | Warning | Nested projection was truncated by `MaxDepth`; deeper members are omitted. Raise `[GenerateProjection(MaxDepth = …)]`. |
| `PRAG0328` | Error | Enums map by member name and the source member has no same-named target member. Add the member or use a converter. |
| `PRAG0329` | Error | The `[MapCondition]` predicate must be a `static bool` method taking the source type. |
| `PRAG0330` | Error | `[MapDerived]` must pair a source deriving the `[MapFrom]` source with a DTO deriving the base DTO. |
| `PRAG0331` | Info | `[MapDerived]` is not honored by the EF projection (base shape only). Query derived DTOs explicitly. |
| `PRAG0332` | Info | `[MapCondition]` applies to `FromEntity` only; the projection maps the property unconditionally. |
| `PRAG0333` | Error | A mapped collection is updated with a strategy that matches incoming elements against the existing children, and the two sides share no key. Give the element DTO an `Id`, or the child entity a `[LogicKey]` the DTO also carries, or declare `[CollectionStrategy(CollectionStrategy.Replace)]` if rebuilding the collection is what you mean. |
| `PRAG0334` | Error | A mapped path reaches an entity across a boundary. The two live in different `DbContext`s, so no navigation is generated and there is nothing for EF Core to include. Declare `[ReadAccess<T>]` on the source entity to read across. |
| `PRAG0335` | Warning | A DTO declares `[LinkIds]`, which attaches rows to the change tracker and needs a `DbContext`. Write it with `ApplyTo(entity, context)`: `ToEntity()` builds a detached object and cannot link rows, so the declaration has no effect there. |
| `PRAG0336` | Info | A mapped property has no setter on the entity, which computes it, so the write path leaves it alone. Mark it `[MapIgnore(MappingDirection.ToEntity)]` to say so; it stays in the response either way. |
| `PRAG0338` | Warning | A mapping attribute names an open type parameter, which has no properties to map, so no mapper is generated. Close the type argument, or move the attribute to a closed derived type. |
| `PRAG0340` | Info | A mapped property cannot be translated to SQL, so the projection leaves it at its default and reads that go through the projection do not carry it. |
| `PRAG0341` | Info | A nullable source is mapped onto a non-nullable property, so a null becomes `default`. Declare the property nullable, or give it an explicit `[MapProperty(Default = …)]`, if that value would be mistaken for data. |

### Actions: `PRAG0400-0464`

| ID | Severity | Meaning and fix |
|----|----------|-----------------|
| `PRAG0400` | Error | An action class is not `partial`, so the invoker cannot be generated. |
| `PRAG0401` | Error | The action does not inherit `DomainAction<T>` or `VoidDomainAction`. |
| `PRAG0403` | Error | A mutation declares `ReturnType = LogicalKey`, but its entity has no `[LogicKey]`, or a part of it is a foreign key only the relation graph can type. Return `Id` or `Entity`, or declare the parts on properties the entity has. |
| `PRAG0404` | Error | `[LoadEntity<T>]` names an ID property that does not exist on the action. |
| `PRAG0405` | Error | The key type of the `[LoadEntity<T>]` entity could not be determined. |
| `PRAG0406` | Error | A `[Boundary]` class is not `partial`. |
| `PRAG0407` | Error | A `[Boundary]` class is not inside a namespace. |
| `PRAG0408` | Error | An action is declared inside another type. The generated invoker cannot extend a nested type, so the build fails in generated code. Move the action to namespace level. |
| `PRAG0409` | Error | A `[Mutation]` class does not inherit `Mutation<TEntity>`. |
| `PRAG0410` | Error | The mutation mode is ambiguous. Set `[Mutation(Mode = …)]` or use a `Create`/`Update` class-name prefix. |
| `PRAG0411` | Error | The property a `[LoadEntity]`/`[LoadEntities]` loads by is not of the entity's key type, so the load cannot be generated. Change the property's type, or load through a `Specification`. |
| `PRAG0412` | Warning | A sub-boundary is nested more than two levels deep. Consider flattening. |
| `PRAG0413` | Info | A sub-boundary was inferred from the namespace structure. Not reported for a group a `[SubBoundary(Name = …)]` names: an author who wrote the name does not need to be told what they wrote. |
| `PRAG0416` | Error | A `[SubBoundary(Name = …)]` that cannot be a group: an empty name, which would fall back to the namespace and make the declaration a no-op, or the boundary's own name, which separates nothing and would publish `I{Boundary}{Boundary}Actions`. The operation goes where it would have gone without the attribute. |
| `PRAG0414` | Warning | A mutation property has no matching `Set{Property}()` on the entity. Add the setter method, or `[MapIgnore]` the property. |
| `PRAG0415` | Warning | The `[AuthorizationPolicy]` type could not be resolved. Check the type exists and is accessible. |
| `PRAG0418` | Warning | `[RequirePermission(constant)]` names a constant that could not be resolved to a permission value, so it would **not** be enforced. Use a literal string, or a generated entity permission. |
| `PRAG0419` | Warning | A field's concrete type makes it impossible to tell an injected service from plain state, so the field is **not** injected. Depend on an interface or abstract type, or mark the type `[Service]`. |
| `PRAG0420` | Warning | `[ResiliencePolicy]` declares an empty or whitespace-only name. No such policy can be registered, so the attribute is ignored. |
| `PRAG0421` | Warning | `[ExplicitPermission]` names a constant that is not in this compilation's permission catalog, so the auto-derived permission is used instead. Name a constant this assembly generates (an entity's CRUD, or an `[assembly: Permission]`), or pass the permission as a string. |
| `PRAG0422` | Error | A permission attribute declares no permission. Nothing would reach the generated registry and the pipeline would treat the operation as having no requirement at all: fail-open. Name at least one permission, or remove the attribute. |
| `PRAG0423` | Error | `[StartsDelegation]` names a property the action does not have, so the delegation scope cannot be generated and the action would run as the caller while claiming to act for someone else. Use `nameof()` on a string property of the action. |
| `PRAG0424` | Warning | An operation writes in its own boundary and calls another within one invocation. A boundary is a transaction boundary (each saves separately, inner first), so a failure afterwards leaves the inner writes committed. Raise a domain event, or declare `[UndoWith<T>]` on the step, or record `[AcceptsPartialWrites("reason")]` if the leftovers are harmless. |
| `PRAG0425` | Error | `[UndoWith<T>]` names a type that does not implement the compensator interface, so no compensation is generated and the declaration would silence PRAG0424 without undoing anything. |
| `PRAG0426` | Error | A `[Transactional]` action calls another boundary, whose writes commit through their own unit of work: a rollback here cannot undo them. Raise a domain event, or drop `[Transactional]` and answer PRAG0424 instead. |
| `PRAG0427` | Warning | A `[CompositeAction]` declares no step property (a `Mutation<T>`, `DomainAction<T>` or `VoidDomainAction`), so no invoker is generated. Either the work is in `Execute` and the attribute should go, or the action does nothing at all. |
| `PRAG0428` | Warning | An operation invokes other operations without saying how they commit. Declare `[Transactional]`, `[CommitStrategy(CommitMode.Once)]` or `[CommitStrategy(CommitMode.PerStep)]`. |
| `PRAG0429` | Info | An operation undoes another boundary's work in-request: a saga's compensating step without a saga's durability. A crash between the other boundary's commit and the undo keeps the write, with nothing to retry it. Where that matters, publish an event or model the flow as a saga. |
| `PRAG0430` | Warning | A `[CompositeAction]` declares `[CommitStrategy(CommitMode.PerStep)]`, which has no effect: a composite commits once by construction. Remove it, or compose in the body, where `PerStep` is honoured. |
| `PRAG0431` | Warning | A boundary declares `[Transactional]`, which has no effect and as a boundary-wide default would cost a round trip per action. Declare it on the actions that need it, or use `[CommitStrategy]` for a boundary-wide policy. |
| `PRAG0432` | Error | A commit declaration sits on an operation that belongs to no boundary, so there is no unit of work to govern. Name the boundary with `[BelongsTo<TBoundary>]`. |
| `PRAG0433` | Warning | An event is raised with a parameter that matches no input property, so it is passed as `default` and the handler receives an empty value. Rename the parameter to match, or add the property. |
| `PRAG0434` | Warning | A mutation auto-maps a property the entity governs with a state machine, bypassing the declared transitions. Mark it `[MapIgnore]` and declare the move with `[TransitionsTo<TState>(target)]` on the mutation. |
| `PRAG0435` | Error | A mutation loads an existing row but cannot address it. Declare `public required {Key} Id { get; init; }` on it. |
| `PRAG0436` | Error | A mutation carries a child that writes an entity not declared part of its aggregate. Add `[PartOf<TAggregate>]` to the child entity if it has no life of its own, or write it through its own mutation, composed with this one. |
| `PRAG0437` | Error | A carried collection's elements have no key in common with the child entity. Give the element DTO an `Id`, or the child entity a `[LogicKey]` the DTO also carries, or declare `[CollectionStrategy(CollectionStrategy.Replace)]`. |
| `PRAG0438` | Error | A mutation targets an entity that declares `[PartOf<T>]`, so it is written through its aggregate. Remove `[PartOf<T>]` if the entity has operations of its own, or remove this mutation. |
| `PRAG0439` | Error | A mutation carries children the entity has no navigation for. Declare the relation with `[Relation.OneToMany<T>]`, or name the property after the navigation it writes. |
| `PRAG0440` | Error | An exposed `[CompositeAction]` declares no permission of its own while its steps require one: the door to the route says nothing, and an authenticated caller holding nothing reaches the steps. Declare the permission the composite requires. |
| `PRAG0441` | Warning | A boundary's actions facade is injected outside a trusted caller. It enters an internal call, which authorization filters skip, so its operations run without checking the caller's permission. Inject `IMutationInvoker<,>` or `IDomainActionInvoker<,>` for the one operation needed. |
| `PRAG0442` | Error | A mutation carries a child that is a DTO. A child of an aggregate is written through a mutation of its own, so that its validation and permissions run: declare a `[Mutation]` on the child entity and carry that instead. |
| `PRAG0443` | Error | A carried child writes an entity the named navigation does not hold. Carry the mutation that writes the navigation's own type, or name the navigation that holds this one. |
| `PRAG0444` | Error | A mutation carries a child of another boundary. A boundary is a transaction boundary: the child would be committed by the wrong unit of work, past the rules its own boundary states. Read across the boundary instead, and write through its operations. |
| `PRAG0445` | Warning | A mutation property declares a target with `[MapProperty]` while `Pragmatic.Mapping` is not referenced, so the target is not read and the property is written nowhere. Reference `Pragmatic.Mapping`, or rename the property to match the entity member. |
| `PRAG0446` | Error | The entity's constructor requires a value the mutation does not carry, which would be filled with `default`. Add the property, give the parameter a default of its own, or construct through a factory. |
| `PRAG0447` | Warning | A `[CompositeAction]` belongs to no boundary: its namespace matches none and it declares no `[BelongsTo<TBoundary>]`. The generated invoker needs a unit of work, registered per boundary, so the application fails to start. Name the boundary, or move the type under its namespace. |
| `PRAG0448` | Warning | An operation declares a keyed service and belongs to no boundary, so the generated invoker would ask for it without a key and the application fails to start. Name the boundary with `[BelongsTo<TBoundary>]`. |
| `PRAG0449` | Error | An imported package's operation needs a `DbContext` registered keyed by boundary, and the import named none. Import it as `[UsePackage<TPackage, TBoundary>]`. |
| `PRAG0450` | Error | A module imports packages naming more than one boundary. The unkeyed registration that answers a package's `DbContext` is one per container, so the second shadows the first. Name the same boundary on every import. |
| `PRAG0451` | Error | `[LoadCurrentUser]` cannot be generated: the module needs exactly one `[PragmaticUser]` entity and a reference to `Pragmatic.Identity.Persistence` for its resolver. |
| `PRAG0452` | Error | A `ValidateLoaded` member has a shape the invoker does not call, so it reads like a rule and never runs. Declare `ValidationError ValidateLoaded()` or `Task<ValidationError> ValidateLoadedAsync(CancellationToken)`, an instance method, not generic. |
| `PRAG0453` | Error | An include path names no navigation of the entity it is read on: a `[LoadEntity(Include = …)]` path, or an `[EagerLoad]` path of a mutation. Each segment is a navigation (declared, or generated by a `[Relation]`); the path is not emitted. |
| `PRAG0454` | Error | A load's `Specification` names no specification of the entity. The rule is a static method, property or field returning `Specification<TEntity>`, named with `nameof(EntitySpecifications.Member)` or by the bare member name of `{Entity}Specifications`. |
| `PRAG0455` | Error | A parameter of the load's specification binds to no property of the operation. Parameters bind by name, ignoring case, to a property whose value converts to them; an optional parameter with no property keeps its default. |
| `PRAG0456` | Error | A load names both a key property and a `Specification`. Name one. |
| `PRAG0457` | Error | `RequireReadPermission` asks for the read permission of an entity that has none known here. The entity's permissions are generated with it, by the persistence generator, in the module that owns it. |
| `PRAG0458` | Error | A `[LoadFrom]` property cannot hold what the query answers: the result for a `Single` query, `PagedResult<TResult>` for a paged one, `IReadOnlyList<TResult>` otherwise. |
| `PRAG0459` | Error | An input of the `[LoadFrom]` query binds to no property of the operation. Inputs bind by name, ignoring case, from a property whose value converts to them; an input that is not required keeps its default. |
| `PRAG0460` | Error | `[LoadEntity(By = …)]` names something that is not the entity's `[LogicKey]` member. `By` names one part of the domain key and goes with the key property that holds its value. |
| `PRAG0461` | Error | The property a `[LoadEntity(By = …)]` reads the key from is not of the logic key's type. Declare it with the key's type. |
| `PRAG0462` | Warning | `[RequireExists<T>]` is declared beside a `[LoadEntity<T>]` of the same key, which already proves the row exists: only the load runs. Remove the `[RequireExists]`. |
| `PRAG0463` | Error | A method carries `[Invariant]` and the generated invoker cannot call it, so the rule is enforced on no path. Five conditions have to hold (parameterless, instance, returns `bool`, **at least `internal`**, a name no other invariant of the entity uses), and the message names the one that failed. A `private` rule is the common case: the invoker is a generated class beside the entity. |
| `PRAG0464` | Warning | `[Retry]`, `[Timeout]` or `[CircuitBreaker]` sits on a class no engine reads it on. Jobs read `[Retry]` and `[Timeout]` on a `[Job]` or `[RecurringJob]`; the messaging engine reads all three on a `[MessageHandler]`. On a `[DomainAction]` or a `[Mutation]` the operation runs once; on a domain action declare `[ResiliencePolicy("name")]` instead. |
| `PRAG0465` | Error | `[TransitionsTo<TState>]` names no entity the invoker can move: the mutation's entity has no `[StateMachine<TState>]`, or on an action no `[LoadEntity]` row has one, or more than one does, and the generator will not guess. |
| `PRAG0466` | Error | The invoker performs the `[TransitionsTo]` move (BeforeBody or AfterBody) and the body still calls `TransitionTo(target)`: the second call is a move from the target to itself, refused with a 409 on every request. Remove the call, or declare `When = TransitionTiming.ByBody` if the body is where the move belongs. |
| `PRAG0467` | Error | `[TransitionsTo(When = AfterBody)]` on a domain action, which builds its response in the body and would answer with the state before the move. Use BeforeBody, or ByBody. |
| `PRAG0468` | Error | `[TransitionsTo]` on a mutation that is not an `Update`: a create starts in the initial state, a delete or a restore has a lifecycle of its own. |

### Endpoints: `PRAG0500-0551`

| ID | Severity | Meaning and fix |
|----|----------|-----------------|
| `PRAG0500` | Error | An `[Endpoint]` class is not `partial`. |
| `PRAG0501` | Error | The endpoint does not inherit `Endpoint<T>`, `VoidEndpoint`, `DomainAction<T>`, `VoidDomainAction` or `Mutation<T>`. |
| `PRAG0502` | Error | `[Endpoint]` has no route pattern. |
| `PRAG0503` | Error | More than six error types were declared. |
| `PRAG0504` | Warning | A `{route parameter}` matches no public property. Add the property (PascalCase of the parameter). |
| `PRAG0505` | Error | Two endpoints share a name, which breaks link generation. Set a distinct `Name`. |
| `PRAG0507` | Error | The referenced endpoint group does not exist or lacks `[EndpointGroup]`. |
| `PRAG0512` | Info | A property binds implicitly to the request body. Add `[FromBody]`/`[FromQuery]`/`[FromRoute]`/`[FromHeader]` for clarity. |
| `PRAG0513` | Warning | `[Idempotent]` on a safe verb (GET/HEAD/OPTIONS): those are idempotent by definition, so no filter is emitted. Use `[ResponseCache]` for caching semantics. |
| `PRAG0514` | Warning | A `HttpVerb.Head` endpoint declares a response type. HTTP forbids a body on HEAD, so it is suppressed. Prefer `VoidEndpoint`. |
| `PRAG0515` | Error | `[Autocomplete]` needs the entity to have an `Id`, `{Entity}Id` or `[Key]` property. |
| `PRAG0516` | Error | `[MaxFileSize]` must be a positive byte count; a non-positive limit rejects every upload. |
| `PRAG0517` | Error | `[MaxBodySize]` must be a positive byte count. |
| `PRAG0518` | Warning | `[RequestExample]`/`[ResponseExample]` contains invalid JSON. It is still emitted, but OpenAPI tooling may reject it. |
| ~~`PRAG0519`~~ | n/a | **Retired**, and not reused. It asked a boundary library whether the host authenticates, which it cannot know: its only possible answer was "which packages do I reference", and that pinned `Pragmatic.Identity.AspNetCore` to libraries needing nothing from it. The host says it now: `PRAG1692` for an `[AnonymousHost]` with a derived-permission route, `PRAG1695` for a host with no authentication at all. |
| `PRAG0520` | Error | `[ResponseCache]` on a streaming (SSE) endpoint. A `text/event-stream` response cannot be cached. |
| `PRAG0521` | Error | A streaming endpoint must be mapped to GET (EventSource-compatible) or POST. |
| `PRAG0522` | Error | `[HttpStatus]`/`[CreatedAt]` on a streaming endpoint. SSE always opens with 200. |
| `PRAG0523` | Warning | Versioned handler methods on a streaming endpoint. Only the default version is generated. |
| `PRAG0524` | Error | `[PostProcessor]` on a streaming endpoint. There is no materialized result to observe. |
| `PRAG0525` | Warning | `[Endpoint]` is on a member nothing derives a route from. On a member the attribute is read only for a specification that also carries `[Query]`. Add `[Query<TEntity>]` beside it, or move the `[Endpoint]` onto an operation. |
| `PRAG0526` | Warning | Several endpoints in one boundary share a name, so only the first gets an `ApiRoutes` member. Set a distinct `Name`. |
| `PRAG0527` | Warning | A field's concrete type makes it impossible to tell an injected service from plain state on an endpoint, so the field is **not** injected. Depend on an interface or abstract type, or mark the type `[Service]`. |
| `PRAG0528` | Warning | `[RequirePermission]` names a constant that cannot be resolved to a permission value, so it would **not** be enforced: fail-open. Use a literal permission string, or a permission this compilation generates. |
| `PRAG0529` | Error | Two endpoints declare the same verb and route. Both are registered and the route then answers 500 on every call, because routing cannot choose between them. Give one of them a different route. |
| `PRAG0531` | Error | A DTO declared as a mutation's response does not map from the mutation's entity, so there is no `FromEntity` to call. Add `[MapFrom<TEntity>]` to it. |
| `PRAG0532` | Error | A property a query string cannot carry, on an operation exposed as `GET`. A GET has no body, so its values come from the query string, which carries scalars only. Make it a scalar, take it as JSON in one value, or use a verb with a body. |
| `PRAG0533` | Error | A create mutation answers with a DTO that reads through a navigation onto another aggregate. A create has no loaded entity to read it from, and `[EagerLoad]` has no query to attach to. Answer with a DTO of this aggregate alone, or read the fuller shape back with a query. |
| `PRAG0534` | Error | A `[PreProcessor<T>]`/`[PostProcessor<T>]` cannot be constructed by the service container, so no registration is generated and requests to the endpoint fail. Make the type concrete with a public constructor the container can satisfy. |
| `PRAG0535` | Error | A mutation declares both `[ReturnsDto<T>]` and `ReturnType`, and the key answers: the DTO applies only when the mutation returns the entity. Remove one of the two. |
| `PRAG0536` | Error | An optional request value sits on an init-only property whose initializer is not a constant the generated endpoint can repeat when the value is absent. Give it a constant default (a literal, an enum member, `null` or `default`), or a `set` accessor. |
| `PRAG0537` | Warning | An error type declares `[HttpStatus(n)]` and its own `StatusCode` answers another, so the contract documents one status and the caller receives the other. Make the two agree. |
| `PRAG0538` | Info | A published type declares a member the serializer strips from every response (`TenantId`, `OwnerId`, `AccessScopes`, `RowVersion`, `PersistenceId`), so the contract does not publish it either. Rename it, or drop it from the shape the endpoint answers with. |
| `PRAG0550` | Error | `[Autocomplete]` requires a `string` property: it generates a `Contains()` filter. |
| `PRAG0551` | Warning | Versioned methods (`ExecuteV2`, `HandleAsyncV2`, …) need the `Asp.Versioning.Http` package. Without it only the default version is generated. |
| `PRAG0552` | Error | A property a form field cannot carry, on an operation that carries a file. A file makes the request `multipart/form-data`, so there is no JSON body and every value travels as a form field: the scalars are bound from the form whether or not `[FromForm]` is written on them, and a nested object has nowhere to go. Make it a scalar, take it as JSON in one value, or move the file to an operation of its own. |
| `PRAG0554` | Warning | A shared `[ResponseCache]` (the default location) on an endpoint that requires authentication. The output cache never keeps a request that carries credentials, so the attribute keeps nothing. Mark the endpoint `[AllowAnonymous]` if its answer is the same for everybody, use `Location = ResponseCacheLocation.Client` for a per-browser cache, or remove it. |

### Persistence: `PRAG0600-0690`

| ID | Severity | Meaning and fix | Source |
|----|----------|-----------------|--------|
| `PRAG0600` | Error | A type carrying a Pragmatic persistence attribute (`[Entity]`, `[Repository]`, …) is not `partial`. | Analyzer |
| `PRAG0602` | Error | A `[Database]` context type is not `partial`. | Analyzer |
| `PRAG0610` | Error | The two ends of a relation name the generated navigation differently, and one member cannot have both names. Drop the `Inverse`, or make the two agree. |
| `PRAG0611` | Error | `OnDelete` is set on the dependent side while the other end declares the collection that owns the relationship. Move the value to the owning side, where it is read. |
| `PRAG0612` | Warning | An entity declares more than one relation to the same target and this one leaves its navigation to the convention, which cannot tell them apart. Name it with `[Relation.*<T>.WithNavigation("…")]`. |
| `PRAG0613` | Error | `Inverse` names no navigation on the target entity. Name an existing one, or declare the other side on the target. |
| `PRAG0614` | Error | `Inverse` resolves to a navigation of the wrong type. The inverse is the other end of the same relationship: point it at the navigation that holds the declaring entity, or a collection of it. |
| `PRAG0615` | Error | Two relations would generate the same navigation name, and the second is dropped. Rename it with `[Relation.*<T>.WithNavigation("…")]`. |
| `PRAG0616` | Error | A many-to-many through an explicit join entity does not name its foreign keys. Add `LeftKey` and `RightKey`, naming the properties of the join entity that hold each side's key. |
| `PRAG0617` | Error | An entity declares several relations that each write a member on the same target, and this one leaves the inverse to the convention. Name it with `Inverse = "…"` on every one of them. |
| `PRAG0618` | Error | A one-to-one does not say which end is the principal, or says it on both. Declare `IsPrincipal = true` on exactly one end. |
| `PRAG0619` | Error | A navigation or foreign key is written by hand. Declare the relationship with `[Relation.*]` and remove the property: the generator emits it, with its configuration. |
| `PRAG0620` | Warning | A state machine has no `[InitialState]`, so the generated `Create()` cannot set a default state. | Generator |
| `PRAG0621` | Warning | A state has no incoming transition and is not `[InitialState]`, so it can never be reached. | Generator |
| `PRAG0622` | Error | `[TransitionFrom(...)]` names a value that is not a member of the enum. | Generator |
| `PRAG0623` | Error | `[StateMachine<TState>]` governs a property the entity does not have. Name the property that holds the state with `Property = nameof(…)`; it defaults to `Status`. |
| `PRAG0624` | Error | Some, but not all, of a trait's properties are declared by hand. The generator emits a trait's properties as a group, so it can only stand down on the group: declare them all, or remove the ones you wrote. |
| `PRAG0625` | Error | The parts of a composite `[LogicKey]` ask for different uniqueness scopes. A composite key is one index and one index has one scope: say the same `Scope` on every part. |
| `PRAG0626` | Warning | A tenant-scoped entity assigns its own `PersistenceId`. Two tenants that assign the same value collide on the primary key, which no tenant filter can separate. Let the generator assign it and keep the carried-over identifier as an ordinary property. |
| `PRAG0627` | Error | `[Unique]` names a property the entity does not have. Use `nameof` so the compiler keeps the name honest when the property is renamed. |
| `PRAG0628` | Error | An assembly declares more than one `[Module]`. An assembly has one module, and every boundary in it belongs to that module. Split the second into its own project. |
| `PRAG0629` | Error | No boundary owns this entity, so it has no `DbContext` and no migration creates its table. Add `[Owns<T>]` to the boundary whose context it belongs in. |
| `PRAG0630` | Error | Two boundaries claim the same entity, which would run the transaction line through the middle of it. Keep `[Owns<T>]` on the boundary that writes it; the other reads it with `[ReadAccess<T>]`. |
| `PRAG0631` | Error | `[PartOf<T>]` has no relation to live on. Declare the `[Relation.ManyToOne<T>]` it leans on, on this entity. |
| `PRAG0632` | Error | An entity declares more than one relation to its parent, so `[PartOf<T>]` has to say which one carries the ownership: `[PartOf<T>(Via = "…")]`. |
| `PRAG0633` | Error | `[PartOf(Via = …)]` names no relation of this entity. `Via` is matched against the navigation names of its `[Relation.ManyToOne<TParent>]` declarations. |
| `PRAG0634` | Error | A `[PartOf<T>]` edge does not cascade. A part has no life of its own, so set `OnDelete = DeleteBehavior.Cascade` on the side that owns it, or drop `[PartOf]`. |
| `PRAG0635` | Error | An EF Core relational attribute describes a relationship the generator does not read, so the relation is not declared. Declare it with `[Relation.*]` instead. |
| `PRAG0636` | Error | `[LogicKey]` names something that is neither a property of the entity nor a key a relation generates on it. Name a declared property, or the foreign key a `[Relation.*]` generates. |
| `PRAG0637` | Error | `[LogicKey]` is declared both on the class and on properties. One entity has one domain key, declared in one place: keep the class form when a part is a generated key, otherwise mark the properties. |
| `PRAG0638` | Error | A state machine marks more than one value with `[InitialState]`. An entity has one entry state: leave it on exactly one enum value. |
| `PRAG0639` | Warning | An entity read here navigates to a type this boundary neither owns nor reads, so that type is ignored in the generated model and any query naming the navigation fails at run time. Declare `[ReadAccess<T>]`, or stop reading through it. |
| `PRAG0651` | Warning | A property type may need an EF Core value converter. Register a `ValueConverter`, or use a supported primitive. | Generator |
| `PRAG0652` | Error | A `ProtectedValue` property, and `Pragmatic.Cryptography.EFCore` is not referenced: the generated configuration stores one through `ProtectedValueConverter`, which lives there. Add the reference, or store something else. | Generator |
| `PRAG0680` | Warning | An entity is created with `new` instead of the generated `Entity.Create()` factory. | Analyzer |
| `PRAG0681` | Warning | An entity is produced via `default`. Use `Entity.Create()`. | Analyzer |
| `PRAG0682` | Warning | An entity is produced via `Activator.CreateInstance`. Use `Entity.Create()`. | Analyzer |
| `PRAG0683` | Warning | An entity declares a behavior method. Entities are anemic: move the state change into a mutation or domain action. | Analyzer |
| `PRAG0684` | Warning | `FromSqlRaw`/`ExecuteSqlRaw` is called with non-constant SQL. Use the interpolated `FromSql`/`ExecuteSql`, or pass values as parameters. | Analyzer |
| `PRAG0685` | Info | An aggregate owns many child collections. Consider splitting it, or referencing other aggregates by id. | Analyzer |
| `PRAG0686` | Warning | A type injects another boundary's `DbContext`. Call that boundary's actions/queries, or react to its integration events. | Analyzer |
| `PRAG0687` | Warning | `ExecuteDelete` removes the rows of a `[SoftDelete]` entity for good: the statement goes straight to SQL without passing the change tracker, so nothing turns it into the flag. Use the repository, or state that the erasure is meant with `SoftDeleteScope.Suspend()`. |
| `PRAG0688` | Warning | `ExecuteUpdate` goes straight to SQL, so the interceptors the entity declares do not run: audit columns are not stamped and the concurrency token does not move. Use the repository, or accept the trade deliberately. |
| `PRAG0690` | Warning | A host stores instant properties and does not reference `Pragmatic.Temporal.EFCore`, so nothing normalises what reaches the column while the rest of the framework reads it as UTC. Add the package. |

### Persistence query pipeline: `PRAG0701-0736`

| ID | Severity | Meaning and fix |
|----|----------|-----------------|
| `PRAG0701` | Error | `FilterOperator.Between` is declared and no generator renders it: the filter falls back to equality, and on a collection property it does not compile. Express the range as two properties over the same column. |
| `PRAG0702` | Error | `[CascadeOn]` names a source the target has no foreign key to, so the generated handler cannot find the rows that belong to it. Add the foreign key, or declare the relationship. |
| `PRAG0703` | Warning | An option is read by the generator and consumed by nothing. Remove it, or use the shape that is implemented; the message names it. |
| `PRAG0704` | Error | A query projects a type that declares no `Projection`, and the generated `Apply` names a member that does not exist. Put `[MapFrom<TEntity>]` and `[GenerateProjection]` on the result type. |
| `PRAG0705` | Warning | A required navigation points at a `[SoftDelete]` entity. EF Core turns a required navigation into an `INNER JOIN`, so soft-deleting the target hides every row that points to it from any query that joins it. Make the navigation optional, or soft-delete both ends. |
| `PRAG0706` | Warning | `[ReadAccess<T>]` names an entity owned by a boundary on another database. The type is added as a `DbSet` and excluded from this database's migrations, so the table exists only in the other one and the generated join fails at run time. |
| `PRAG0707` | Error | A query input generates no filter: the value the caller sends is read and dropped. A property becomes a filter when it carries `[Filter]`, when it is `required`, or when it is nullable. |
| `PRAG0708` | Error | `[GenerateHierarchy]` finds no self-referencing relation, so there is no edge to walk and no hierarchy query is generated. Declare `[Relation.ManyToOne<TSelf>]`. |
| `PRAG0709` | Error | `[BindSpecification]` has no `Specification<T>` property to feed. Add the specification the input builds. |
| `PRAG0710` | Warning | A DTO property looks like a navigation but matches none on the entity, so `[LoadWith]` will not include it. |
| `PRAG0711` | Warning | `[LoadWith<T>(MaxDepth = …)]` exceeds a depth of 3. Reduce the depth, or switch to `QueryStrategy.Projection`. |
| `PRAG0712` | Error | A `[Query]` type is not `partial`, so nothing is generated for it. Add `partial`: the generator writes `Apply`, the projection and `ToSpecification` into it. |
| `PRAG0713` | Error | An entity declares more than one relation to itself, so `[GenerateHierarchy]` has to say which one is the tree: `[GenerateHierarchy(Via = "…")]`. |
| `PRAG0714` | Error | `[GenerateHierarchy(Via = …)]` names no self-referencing relation. `Via` is matched against the navigation names of the entity's `[Relation.ManyToOne<TSelf>]` declarations. |
| `PRAG0715` | Error | A hierarchy's edge is required, so every row must have a parent and no root can exist. Declare the relation with `Required = false`. |
| `PRAG0716` | Warning | A DTO pulls in many navigation paths. Consider `QueryStrategy.Projection`. |
| `PRAG0717` | Error | `[VisibleWhen<T>]` names a rule typed for another entity, so it would never apply here. Name a `VisibilityRule<TEntity>` of this entity. |
| `PRAG0718` | Error | A visibility rule is abstract, generic, or has no public parameterless constructor. Its predicate is read once when the EF model is built, so it has to be constructible with no arguments. |
| `PRAG0719` | Error | `[WithoutFilter<T>]` is declared without a permission. Reading past a filter is a privilege: declare the permission that grants it, or drop the attribute. |
| `PRAG0720` | Error | `Disable<T>()` is called for a declared visibility rule, which EF Core enforces rather than the filter provider that call reaches: it compiles and does nothing. Call `DisableVisibilityRule<T>()` instead. |
| `PRAG0721` | Warning | An operation writes the property a visibility rule keys on, so a row that stops satisfying the rule cannot be loaded to change it back. Add `[WithoutFilter<T>]` beside the permission that allows it. |
| `PRAG0722` | Warning | A `[Count<T>(Where = …)]` clause never mentions the row. The clause is a lambda body over the row (write it as `x.Property`), otherwise the generated count does not compile. |
| `PRAG0723` | Error | A query takes a canonical grid request and the entity declares no `[GenerateGridBridge]`, so there is no bridge to apply it with. Put `[GenerateGridBridge]` on the entity and `[Filterable]` on each property the grid may name. |
| `PRAG0724` | Warning | A query takes a canonical grid request and declares `Page`/`PageSize` of its own, so the rows are paged twice. Keep one of the two. |
| `PRAG0725` | Error | A query requires a permission no generated constant matches, so its invoker would enforce nothing. Name a constant this compilation produces, or write the permission as a string literal. |
| `PRAG0726` | Error | Two specifications derive the same query type, so two generated files would carry one hint name, and Roslyn drops the whole generator's output when that happens. Rename one of them, or move one to a namespace of its own. |
| `PRAG0727` | Warning | `Paged = true` is declared on a query that already has `Page` and `PageSize` of its own, so it adds nothing. Remove it, or remove the two properties and let the generator write them. |
| `PRAG0728` | Error | Two published queries contribute the same method name to a contract, which cannot declare both. Give one an explicit `[Published(MethodName = "…")]`. |
| `PRAG0729` | Warning | A member carries `[Query]` and nothing is derived from it. A derived query reads a static member returning `Specification<TEntity>` declared in a non-generic type. Move the rule to one, or remove the attribute. |
| `PRAG0730` | Error | A `[FromCurrentUser]` property can be set by its caller. Declare it `{ get; private set; }`, so the generated invoker is the only one to write it. |
| `PRAG0731` | Error | A `[FromCurrentUser]` binding cannot be generated. Without a member it binds `ICurrentUser.Id` to a string property; with one it binds that member of the `[PragmaticUser]` entity. |
| `PRAG0732` | Error | A view groups by something the entity does not have, so it would add up across it. Name a property of the entity (declared, or generated by its relations and traits) and reach another entity with `Via = "Navigation"`. |
| `PRAG0733` | Error | A `[ComputedFilter]` cannot be generated. It is a `bool` property or an instance method returning `bool`, with an expression body; a method's parameters are the values the rule takes from outside the row. |
| `PRAG0734` | Error | A `[FromClock]` binding cannot be generated. It fills a `DateOnly` with `IClock.UtcToday` and a `DateTimeOffset` with `IClock.UtcNow`, and the property is declared `{ get; private set; }`. |
| `PRAG0735` | Error | A specification used inside a `[Projectable]` or `[ComputedFilter]` body takes a value from the row, and the query builds the specification before it reads any row. Pass a value from outside the row, or write the predicate inline. |
| `PRAG0736` | Error | An `[EagerLoad]` path of a query names no navigation of the entity it is read on. Each segment is a navigation (declared, or generated by a `[Relation]`); the path is not emitted. |
| `PRAG0737` | Error | A `[Join<T>(Via = "…")]` whose path names no navigation of the query's entity, read segment by segment through the same resolver as `PRAG0736`. The path is not emitted: copied into the generated `Apply` unread, it would reach the author as a `CS1061` at a line of a file they did not write. ⚠️ A cross-boundary `[Relation]` generates the foreign key and no navigation property, so the target is not reachable by name from that side. |
| `PRAG0738` | Error | A `[Join<T>(ForeignKey = …)]` on a query whose result **is** the entity. A key join delivers the joined entity's columns through the result type, and there is none, so the join would have nowhere to put them. Declare `[Query<TEntity, TResult>]` with a result that names them, or drop the join: as a filter on the root alone it says no more than a `[Filter]` does. |
| `PRAG0739` | Error | A `[Join<T>]` whose `ForeignKey` or `TargetKey` names no property: the first read on the query's entity, the second on the joined type. The same rule `Via` gets from `PRAG0737`, and for the same reason. ⚠️ A foreign key that a `[Relation]` generates is not visible to the generator that writes it; but if a `[Relation]` exists there is a navigation, so reach the target with `Via` instead. |
| `PRAG0740` | Error | A property of a joined query's result that neither the entity nor any joined target answers. The generated step builds the result property by property, so it has to know the source of each. Name it as the entity spells it, prefix it with the joined type (`CustomerName` for `Customer.Name`), or prefix it with the join's `Alias` when two joins reach the same type. Without this it would be a `CS0117` inside a generated file. |
| `PRAG0741` | Error | A `[Join<T>(Type = …)]` EF Core cannot translate. `Inner`, `Left` and `Cross` generate. `Full` has no LINQ spelling that becomes a FULL OUTER JOIN; `Right` is `Left` with the operands swapped, and the generated step receives the root set **already filtered, sorted and paged**, so rows of the target the root's filters never selected cannot be added back. Swap the query's entity and its join to say the same thing. |
| `PRAG0742` | Error | A `[Join<T>(ForeignKey = …)]` whose target belongs to another boundary that this one does not read. EF Core composes a join only inside one `DbContext` instance and a host builds one per boundary, so there is no set to join against. Add `[ReadAccess<T>]` to the boundary (which is what puts the other boundary's `DbSet` in this model), or reach the target through its own query. ⚠️ `[ReadAccess]` also requires the two boundaries to share a database; `PRAG0706` reports it when they do not. |

### Messaging: `PRAG0800-0835`

| ID | Severity | Meaning and fix |
|----|----------|-----------------|
| `PRAG0800` | Error | A `[MessageHandler]` does not implement `IMessageHandler<T>`. |
| `PRAG0801` | Error | A `[MessageHandler]` is not `partial`, so no pipeline code can be generated. |
| `PRAG0802` | Error | `[Retry]` has `MaxAttempts <= 0`. |
| `PRAG0803` | Error | A `[MessageMiddleware]` does not implement `IMessageMiddleware`. |
| `PRAG0811` | Info | A saga state has no `[InState]` handler. Terminal states do not need one. |
| `PRAG0813` | Error | The `[Saga<TState>]` type argument is not an enum. |
| `PRAG0814` | Error | A saga has no `[SagaStart]` method. Add it to exactly one handler. |
| `PRAG0816` | Warning | An event is published but no `[MessageHandler]` consumes it. |
| `PRAG0819` | Warning | Several properties carry `[PartitionKey]`; one is used and the rest are ignored. |
| `PRAG0820` | Error | A saga consumes an event that neither implements `ICorrelatedMessage` nor declares `[CorrelationKey]`, so it cannot be routed to an instance. |
| `PRAG0821` | Warning | Several properties carry `[CorrelationKey]`; the first is used and the rest are ignored. |
| `PRAG0822` | Warning | Domain events form a cascade cycle: a handler re-raises an event that loops back. Make a handler idempotent or terminal, or guard the re-raise. |
| `PRAG0831` | Warning | `[EnableOutbox]` without a reference to `Pragmatic.Messaging.EFCore`: the outbox table is not mapped and no delivery pump runs, so the attribute is a no-op. |
| `PRAG0832` | Warning | `[EnableSagaPersistence]` without a reference to `Pragmatic.Messaging.EFCore`: the saga tables are not mapped. |
| `PRAG0833` | Warning | A boundary carries both `[EnableOutbox]` and `[EnableEventOutbox]`; both capture and clear the same domain events, so one silently wins. Keep exactly one. |
| `PRAG0834` | Warning | More than one boundary carries `[EnableBatchProgress]`. Only one may host the `__BatchProgress` table. |
| `PRAG0835` | Warning | `[EnableBatchProgress]` without a reference to `Pragmatic.Messaging.Batch`. |
| `PRAG0836` | Warning | `[PublicEvent]` on a type that does not implement `IDomainEvent`. The marker says "published"; it does not make the type an event, so nothing reads it: the type stays out of the AsyncAPI document and out of the transactional outbox. Implement `IIntegrationEvent`, which is `IDomainEvent` plus published. |
| `PRAG0837` | Warning | An `[EventHandler]` on a boundary marked `[EnableOutbox]`. The outbox interceptor takes the entity's domain events during the save, so the unit of work (which dispatches after the commit, deliberately) finds none: the handler stays registered and is never entered, with no log and no dead letter. On such a boundary the events leave as messages: write it as `[MessageHandler] IMessageHandler<T>`, or take `[EnableOutbox]` off to keep in-process dispatch. |

### Temporal: `PRAG0900-0905`

| ID | Severity | Meaning and fix | Source |
|----|----------|-----------------|--------|
| `PRAG0900` | Warning | `DateTime.Now` / `UtcNow` / `DateTimeOffset.Now` / `UtcNow` used directly. Inject `IClock` (Pragmatic.Temporal) or `TimeProvider` (BCL). | Analyzer |
| `PRAG0901` | Warning | `DateTime.Today` used directly. Use `IClock.Today`. | Analyzer |
| `PRAG0902` | Warning | A `DateTime` is constructed without a `DateTimeKind`, so its `Kind` is `Unspecified`. Pass a kind, or use `DateTimeOffset`. | Analyzer |
| `PRAG0903` | Warning | Two `DateTimeOffset` values are compared with `<`/`>`/`<=`/`>=`. Convert with `.UtcDateTime` or `.ToUniversalTime()` first. | Analyzer |
| `PRAG0904` | Info | `DateTime.Now`/`UtcNow` in test code. Use `IClock`/`TestClock` for deterministic tests. | Analyzer |
| `PRAG0905` | Warning | A timezone-conversion attribute is on a property that is not `DateTimeOffset`/`DateTime` (or their nullable forms), so the attribute is ignored. | Generator |

### Identity and Authorization: `PRAG1001-1016`

| ID | Severity | Meaning and fix |
|----|----------|-----------------|
| `PRAG1001` | Error | Two types define the same permission name. Permission names are unique per assembly. |
| `PRAG1003` | Error | A type implements `IRole` but its `Name` is empty or unresolvable. Return a non-empty string literal from the `static abstract Name`. |
| `PRAG1004` | Error | A declared permission's first segment names no boundary of this assembly. A custom permission is `{boundary}.{resource}.{verb}`, and its constant goes into that boundary's permissions class. |
| `PRAG1005` | Error | A declared permission needs a constant name another permission, resource or entity already uses. Pick a value whose segments do not reuse, at the same depth, a name that is taken. |
| `PRAG1006` | Error | A `[Role]` class is not a top-level `partial` class, so its `IRole` members cannot be generated and it is no role at all. Declare it `partial`, outside any other type. |
| `PRAG1007` | Error | Roles include each other in a loop. Remove one `[IncludesRole<T>]` from it. |
| `PRAG1008` | Error | A role grants a permission no generator of this compilation writes, so it would be catalogued granting less than it declares. Name a generated constant, one of a referenced assembly, or write the permission's value. |
| `PRAG1009` | Error | A role includes a hand-written role of another assembly, whose permissions are a property body the generator cannot read. Declare that role with `[Role]` and `[Grants]` in its own assembly. |
| `PRAG1010` | Error | `roles.pragmatic.json` is not valid JSON, so none of the roles and groups it declares is seeded. `SeedFromJson()` is generated from the file only when it parses. |
| `PRAG1011` | Error | `roles.pragmatic.json` declares something it cannot. A role has `description`, `permissions` and `inherits`; a group has `description` and `roles`. |
| `PRAG1012` | Error | More than one `roles.pragmatic.json` is present and only the first is read, so the roles and groups the others declare would not exist. Keep one per project. |
| `PRAG1013` | Error | A role spreads into `DefaultPermissions` something the generator cannot read, so it grants those permissions at run time while the registry does not list them. Spread another role's `DefaultPermissions`, or a list held in a field or property of this compilation. |
| `PRAG1014` | Error | An enum member has no `[SignsInAs<TRole>]` while others of the same enum do, so a user with it would sign in with no role. Declare the role it signs in as. |
| `PRAG1015` | Warning | A role reads its `DefaultPermissions` from a list held in another assembly that does not publish it, so the registry lists an empty grant while the runtime grants every entry. Mark the list `[PermissionSet]` in the assembly that declares it, or move it into this one. |
| `PRAG1016` | Warning | A `[PermissionSet]` list cannot be read by the generator, so the assembly publishes nothing for it. Write it as a collection expression or an array initializer, the shapes a role's `DefaultPermissions` accepts. |

### Data ownership: `PRAG1100-1104`

| ID | Severity | Meaning and fix | Source |
|----|----------|-----------------|--------|
| `PRAG1100` | Error | An `[HasOwner]` type is not `partial`. | Analyzer |
| `PRAG1104` | Info | The type already declares `OwnerId`, so only the ownership filter is generated. Remove the manual property to use the generated one. | Generator |

### Dependency injection: `PRAG1450-1452`

| ID | Severity | Meaning and fix |
|----|----------|-----------------|
| `PRAG1450` | Warning | A singleton-lifetime host service injects a scoped service, capturing it for the app's lifetime. Inject the factory suggested in the message instead. |
| `PRAG1451` | Warning | `BuildServiceProvider()` builds a second container: singletons and options are duplicated and disposables leak. Resolve from the app's provider. |
| `PRAG1452` | Warning | `[Inject]` is optional (`Required = false`), so a missing service is injected as `null` and throws at first use. Set `Required = true` to fail fast at startup, or keep the optional contract deliberately. |

### Composition: `PRAG1050`, `PRAG1601-1698`

| ID | Severity | Meaning and fix |
|----|----------|-----------------|
| `PRAG1050` | Error | `[UsePackage<T>]` is declared more than once on a module. |
| `PRAG1601` | Error | A module's `[IncludeModule<T>]` names no known module. Whether the host hosts it is PRAG1603. |
| `PRAG1602` | Error | Modules form a circular dependency. |
| `PRAG1603` | Error | The host hosts a module whose `[IncludeModule<T>]` dependency it neither includes nor declares remote. The host registers only what it declares, so the dependency would fail when first resolved. Add `[Include<T>]` for it, or `[RemoteBoundary<T>]` if it runs elsewhere. |
| `PRAG1607` | Warning | Two database-bound modules resolve to the same name, so the 2-arity `[Include<TModule, TDb>]` binding is ambiguous. Rename a boundary, or use the 3-arity form. |
| `PRAG1608` | Warning | `[Include<T, …>]` found no discovered module metadata, so the DbContext is **not** registered and will fail at runtime. Ensure the persistence generator ran in that module, or use the 3-arity form. |
| `PRAG1609` | Warning | A relational database has no `ConfigKey`, so the generated DbContext has no connection string. Set `[PragmaticDatabase(ConfigKey = "…")]`. |
| `PRAG1610` | Error | Incompatible metadata schema version between packages. Align the package major versions. |
| `PRAG1611` | Warning | A referenced assembly carries a newer metadata schema than this Composition supports. Update `Pragmatic.Composition`. |
| `PRAG1612` | Info | A legacy metadata schema version is being read in backward-compatibility mode. |
| `PRAG1613` | Warning | A module's embedded manifest is not valid JSON and was left out of the aggregated manifest. Rebuild that module with the current generator; the host is otherwise unaffected. |
| `PRAG1614` | Warning | A registration names a type no generator has written at this point, so it is emitted with an unqualified name. Write it fully qualified, or register the service by hand in a startup step. |
| `PRAG1630` | Error | A `[StartupStep]` class does not implement `IStartupStep`. |
| `PRAG1631` | Error | `[StartupStep]` is applied to something that is not a class. |
| `PRAG1632` | Error | `[NeedsStep<T>]` references a type that is not available. Add the package reference. |
| `PRAG1640` | Error | `[Service]` is applied to something that is not a class. |
| `PRAG1641` | Warning | A service depends on a type that is not registered. |
| `PRAG1642` | Warning | A singleton depends on a shorter-lived service (captive dependency). Align the lifetimes, or inject `IServiceScopeFactory`. |
| `PRAG1643` | Warning | A service implements no interface. Use `AsSelf = true`, or implement one. |
| `PRAG1645` | Error | `[Service]` is applied to an abstract class. |
| `PRAG1646` | Warning | Keyed services require .NET 8 or later. |
| `PRAG1647` | Warning | An open-generic service's `[Inject]` members are ignored: DI resolves open generics by constructor injection only. Use constructor parameters. |
| `PRAG1651` | Warning | A boundary is included without a database assignment. Use `[Include<TModule, TDatabase>]`. |
| `PRAG1652` | Error | Two databases generate the same DbContext name. Use `[Include<TModule, TDatabase, TDbContext>]` with distinct types. |
| `PRAG1660` | Error | A `[Decorator]` implements no interface to decorate. |
| `PRAG1661` | Error | A `[Decorator]` has no constructor parameter of the decorated interface type. |
| `PRAG1670` | Error | An `[EventHandler]` class does not implement `IDomainEventHandler<TEvent>`. |
| `PRAG1680` | Warning | `[ExposeEndpoint<T>]` references an action that was not imported via `[UsePackage]`. Use `[Endpoint]` on your own actions. |
| `PRAG1681` | Error | An `[ExposeEndpoint<T>]` on a verb that carries no body (GET, DELETE, HEAD, OPTIONS) whose action takes an input a query value cannot produce. A query value is text, so a scalar, an enum or an `IParsable` can come from one and a nested object cannot. Expose it on a verb that carries a body, or give the action a scalar input. |
| `PRAG1682` | Error | The group named by `[ExposeEndpoint<TAction, TGroup>]` cannot be mapped: `TGroup` (or a parent group) is not declared with `[EndpointGroup("prefix")]`, or the host cannot resolve it. The route belongs inside the group (its prefix and its `ConfigureGroup` options), so it is not published on the root instead. Declare the group, or expose the action without one. |
| `PRAG1685` | Error | A module is declared both `[Include]` and `[RemoteBoundary]`. A boundary is local or remote, not both. |
| `PRAG1686` | Warning | `[RemoteBoundary<T>]` was declared but the module exposes no actions to invoke. |
| `PRAG1687` | Info | `[RemoteBoundary<T>]` has no `BaseUrl`. Configure `Pragmatic:RemoteBoundaries:{Module}:BaseUrl` at runtime. |
| `PRAG1688` | Warning | A configuration key required at startup is missing from `appsettings.json`. |
| `PRAG1689` | Warning | A remote boundary's action declares a compensator that will never run here: it executes in the process that owns the action. Publish an event, or model the undo as a saga. |
| `PRAG1690` | Info | Reports how many Pragmatic modules were discovered from referenced assemblies. |
| `PRAG1691` | Info | Reports which modules are being composed. |
| `PRAG1692` | Warning | The host declares `[AnonymousHost]` and one of the routes it discovered enforces a **derived** permission, one `[Autocomplete]` computed from the boundary and the entity, which the author never declared and cannot relax. No caller can hold a permission here, so that route answers 403 to everyone. Drop `[Autocomplete]` on that property, or give the host an authentication method. |
| `PRAG1693` | Info | No `[Service]` or `[Decorator]` registrations were found. Check that library projects reference `Pragmatic.Composition`. |
| `PRAG1694` | Info | No `[StartupStep]` registrations were found. |
| `PRAG1695` | Error | A host references `Pragmatic.Authorization` without `Pragmatic.Identity` and does not declare `[AnonymousHost]`, so its endpoints require an authorization nothing can grant. Add `Pragmatic.Identity.AspNetCore`, or put `[AnonymousHost]` on the host's `[Module]` class. |
| `PRAG1696` | Warning | `Pragmatic.Identity.Persistence` is referenced without `Pragmatic.Authorization`. |
| `PRAG1697` | Error | Two operations are published at the same verb and address. A caller cannot choose between them, and which one answers depends on registration order. Give one of them a route of its own. |
| `PRAG1698` | Warning | An operation of this application declares `[FromClock]`, whose generated invoker resolves `IClock` on every request, and this host references no `Pragmatic.Temporal` to supply one, so that route answers 500. Reference `Pragmatic.Temporal`, and the generated host registers the system clock for you; or register an `IClock` of your own. |
| `PRAG1699` | Warning | A referenced assembly declares a message-handler registration and this host does not call it. The host composes the assemblies it discovered, and discovery is by module: an assembly that is not one (a contracts project, typically) is skipped with everything it declares, so its handlers, message type registry and transport subscriptions are simply absent at run time and nothing fails at build time. Call the registration once in `Program.cs`, or give that assembly a `[Module]` if it is one. A module you deliberately left out of `[Include<T>]` is not reported: that is what the attribute is for. |

### Caching: `PRAG1700-1751`

| ID | Severity | Meaning and fix |
|----|----------|-----------------|
| `PRAG1700` | Error | A `[Cacheable]` type is not `partial`. |
| `PRAG1701` | Warning | The cache duration is not parseable. Use `5m`, `1h`, `1d`, or a `TimeSpan` string. |
| `PRAG1702` | Error | The type has no property left to build a cache key from. Add one, or remove `[Cacheable]`. |
| `PRAG1703` | Error | A `{Placeholder}` in a key or tag names a property that does not exist. |
| `PRAG1704` | Error | An `[InvalidatesCache]` type is not `partial`. |
| `PRAG1705` | Warning | A cache key property nests deeper than the key walks, or refers to itself, so two requests differing only below that point share a cache entry. Flatten the property, or exclude it with `[CacheKey(Exclude = true)]`. |
| `PRAG1750` | Warning | Two cache-key properties share an `Order`, so the key order is non-deterministic. |
| `PRAG1751` | Warning | Every property is excluded from the cache key, which risks collisions. Keep one identifying property. |

### Internationalization: `PRAG1800-1805`

| ID | Severity | Meaning and fix |
|----|----------|-----------------|
| `PRAG1800` | Error | A translation file could not be parsed. Check the JSON. |
| `PRAG1801` | Warning | A translation key is defined in several files for the same culture; the last one wins. |
| `PRAG1802` | Warning | A key present in the default culture is missing in another. Runtime falls back to the default. |
| `PRAG1803` | Info | A translation file contains no string leaf values. |
| `PRAG1804` | Info | A `MessageKey` resolves under a key no translation file defines. The resolver looks it up verbatim, so the default text is returned. Add the key, or name an existing one. |
| `PRAG1805` | Error | A translation key is also the prefix of other keys, so the generated class would need a member and a nested class of the same name, and no constant is generated for it. Rename it so that neither key is a prefix of the other. |

### Documents: `PRAG1900`

| ID | Severity | Meaning and fix |
|----|----------|-----------------|
| `PRAG1900` | Warning | A CSV property is written as text but cannot be read back into its CLR type, so it is left at its default on read. Use a supported type, or `[CsvColumn(Ignore = true)]`. |

### Configuration: `PRAG2000-2050`

| ID | Severity | Meaning and fix |
|----|----------|-----------------|
| `PRAG2000` | Error | A `[Configuration]` class is not `partial`. |
| `PRAG2001` | Error | A `[Configuration]` class is `static` or `abstract`. |
| `PRAG2002` | Error | A `[ConfigInvariant]` method is static, takes parameters or does not return `bool`, so the validator cannot call it and the rule never runs. Make it a parameterless instance method returning `bool`. |
| `PRAG2050` | Warning | A `[Required]` property also has a default value, which satisfies the requirement. |

### Notifications: `PRAG2100`

| ID | Severity | Meaning and fix |
|----|----------|-----------------|
| `PRAG2100` | Warning | A boundary is marked `[StoresNotifications]` and its project does not reference `Pragmatic.Notifications.EFCore`, so `__Notifications` is mapped nowhere and the first send fails at run time. Reference the package. |

### Patch: `PRAG2200-2206`

| ID | Severity | Meaning and fix |
|----|----------|-----------------|
| `PRAG2200` | Error | A `[GeneratePatch<T>]` type is not `partial`. Add `partial` to the class or record. |
| `PRAG2201` | Error | The entity type could not be resolved from `[GeneratePatch]`. Check that the type argument names a real, accessible entity. |
| `PRAG2202` | Warning | The entity has no settable properties suitable for patching, so the generated patch would be empty. |
| `PRAG2203` | Error | A patched collection's elements have no key in common with the child entity. Give the element DTO an `Id`, or the child entity a `[LogicKey]` the DTO also carries. |
| `PRAG2204` | Warning | A patch child DTO has neither `[MapTo<T>]` nor `[Patch<T>]`, so `ApplyPatch` has no way to create or update the entity behind it and leaves it alone. |
| `PRAG2205` | Info | A patched collection updates children that already exist: its element DTO has no `[MapTo<T>]`, so an element matching nothing is skipped rather than created. |
| `PRAG2206` | Warning | `[PatchIgnore]` names a property the type does not have, so the property it was meant to exclude is still in the patch. Fix the name. |

### Client: `PRAG2300-2304`

| ID | Severity | Meaning and fix |
|----|----------|-----------------|
| `PRAG2300` | Error | The API manifest could not be read (it is not valid JSON, or does not match the expected schema), so no typed client was generated from it. Rebuild the project that produces it, or check the file passed as `AdditionalFiles`. |
| `PRAG2301` | Warning | A type the manifest does not describe is exposed as `object` by the generated client: it compiles, and leaves the caller without a typed payload. Expose the type as a DTO on the endpoint that returns it. |
| `PRAG2302` | Warning | `PragmaticClientBoundaries` matched no endpoint in the manifest, so no client was generated. The filter matches the prefix of each endpoint's `operationId` (`Booking` for `Booking.CreateGuest`), and a typo silently produces no output. |
| `PRAG2303` | Warning | An endpoint is not void and the manifest carries no response type, so the generated client returns `object`. Unlike PRAG2301, nothing is stated at all: declare what the endpoint answers. |
| `PRAG2304` | Warning | Two manifests describe a shared type differently. The first description was kept and the second discarded, so one boundary's client may be typed against the wrong shape. Rebuild both against the same contract. |

### Testing: `PRAG2350-2363`

| ID | Severity | Meaning and fix |
|----|----------|-----------------|
| `PRAG2350` | Warning | `[GenerateMock<T>]` names something that is neither an interface nor a derivable class (a sealed or static class cannot be subclassed), so no mock was generated. Declare an interface, or hand-write a fake. |
| `PRAG2352` | Info | A generic method cannot be configured in a typed way on a generated mock: it is implemented and returns `default`. |
| `PRAG2353` | Warning | A mock for the same type is declared more than once in this assembly; the extra declarations are ignored. |
| `PRAG2360` | Warning | `[GenerateComparer<T>]` names a type with no readable public property, so no comparer was generated: one would report every pair as equivalent, which is worse than not having it. |
| `PRAG2361` | Warning | A comparer for the same type is declared more than once; the extra declarations are ignored. |
| `PRAG2363` | Info | Half of a state-transition contract has no walk: no declared transition of the entity reaches a state from which the target is legal (or illegal), so that half is not generated, and not emitted as a skipped test either. Expose the transition that leads there, or write the contract by hand in the partial test class. |

### Logging call sites: `PRAG2400-2410`

Reported by the analyzer where a `[LoggerMessage]` method is written. A method with any Error here gets no generated body.

| ID | Severity | Meaning and fix |
|----|----------|-----------------|
| `PRAG2400` | Error | A `[LoggerMessage]` method is not a non-generic `partial void` declaration whose parameters are passed by value, so no body can be written for it. Change the declaration to that shape. |
| `PRAG2401` | Error | A placeholder in the message names no parameter. Placeholders match parameters by name, ignoring case and a leading `@`: rename one of the two, or add the parameter. |
| `PRAG2402` | Warning | A parameter is not named in the message. It is still logged as a structured property; usually a placeholder is missing. |
| `PRAG2403` | Error | The method has no logger. A static method takes an `ILogger` parameter; an instance method finds exactly one `ILogger` parameter, field, property or primary-constructor parameter. With two, pass the one to use as a parameter. |
| `PRAG2404` | Error | The attribute sets no `Level` and no parameter is a `LogLevel`. Set one or the other. |
| `PRAG2405` | Warning | Two call sites of one type share an event id, so an operator cannot tell their entries apart. Set distinct ids; a call site that sets none gets one derived from its event name. |
| `PRAG2406` | Error | The message template does not parse, or a placeholder sets an alignment (`{Name,8}`). Placeholders are `{Name}` or `{Name:format}`; a literal brace is `{{` or `}}`. |
| `PRAG2407` | Error | A type that encloses the method is not `partial`. The generated body is written into it, and C# reopens only a partial type. |
| `PRAG2408` | Error | In a project that uses Pragmatic call sites, a `[LoggerMessage]` written by its simple name binds to Microsoft's attribute: the global alias that selects Pragmatic's did not arrive, so Microsoft's generator owns the method and its `[NotLogged]`/`[PersonalData]` parameters are not masked. Restore the alias (`Pragmatic.SourceGenerator` declares it through MSBuild), or write `[Microsoft.Extensions.Logging.LoggerMessage]` to hand that method to Microsoft's generator on purpose. |
| `PRAG2410` | Error | `[NotLogged]` or `[PersonalData]` on a parameter that is not a `[LoggerMessage]` method's. Only a log call site reads the attribute on a parameter; anywhere else it changes nothing. On a positional record, write `[property: NotLogged]` so it reaches the property. |

### Jobs: `PRAG2500-2509`

| ID | Severity | Meaning and fix |
|----|----------|-----------------|
| `PRAG2500` | Error | A `[Job]`/`[RecurringJob]` class does not implement `IJob` or `IJob<T>`. |
| `PRAG2501` | Error | `[RecurringJob]` has an empty or missing cron expression. |
| `PRAG2502` | Error | A job class is not `partial`, so no invoker is generated and the job never runs. Add `partial`. |
| `PRAG2503` | Error | Two recurring jobs share an ID. Set a unique `Id`. |
| `PRAG2504` | Error | `[Retry]` has `MaxAttempts <= 0`. |
| `PRAG2505` | Error | A `[Continuation<T>]` target does not implement `IJob` or `IJob<T>`. |
| `PRAG2506` | Error | The continuation chain contains a cycle. Remove one `[ContinueWith<T>]`. |
| `PRAG2507` | Error | A job declares more than one `[RecurringJob]` and a second schedule has no `Id`: the default is derived from the class name and both schedules would take it. Give the later ones an explicit `Id`. |
| `PRAG2508` | Warning | A boundary is marked `[EnableJobPersistence]` and its project does not reference `Pragmatic.Jobs.EFCore`, whose two `IEntityTypeConfiguration` the generated `DbContext` applies, so `__Jobs` and `__RecurringJobs` are mapped nowhere and no migration creates them. Reference the package. |
| `PRAG2509` | Error | More than one boundary is marked `[EnableJobPersistence]`. The job store is a single store (a lease taken in one database says nothing about a queue in another), so one boundary hosts `__Jobs` and `__RecurringJobs`. |

### Traits and Resource: `PRAG2600-2651`

| ID | Severity | Meaning and fix |
|----|----------|-----------------|
| `PRAG2600` | Error | A type carries a trait attribute without `[Entity]`. Add `[Entity]` before applying traits. |
| `PRAG2601` | Warning | A trait attribute is applied without `[Resource]` on the parent. The trait entity and actions are still generated, but **no endpoints** are: the route segment comes from `[Resource]`. |
| `PRAG2602` | Error | A `[Resource]` segment must be lowercase kebab-case (`reservations`, `room-types`). |
| `PRAG2603` | Error | Two entities in the same boundary use the same resource segment. |
| `PRAG2605` | Info | `[Resource]` declares capabilities without `Read`, so `GET /{segment}/{id}` is not generated. |
| `PRAG2606` | Warning | The generic argument of `[HasNotes<TParent>]` is ignored: notes are always generated for the annotated type. `[HasNotes<Foo>] class Bar` silently produces `BarNote`. Change it to `[HasNotes<Bar>]`. |
| `PRAG2607` | Warning | A partial part carries operation attributes and matches no type `[Resource]` generates in this namespace, so they have no effect and the scaffolded default still applies. Match the namespace and type name of a scaffolded operation. |
| `PRAG2608` | Error | A DTO declared as the shape of a scaffolded operation has no projection, so the query would return nothing. Add `[MapFrom<TEntity>]` and `[GenerateProjection]` to it. |
| `PRAG2609` | Error | A DTO declared on a scaffolded operation maps from another entity, so its projection cannot be applied to this resource's query. Its `[MapFrom<T>]` must name the entity the resource is declared on. |
| `PRAG2610` | Warning | A resource asks for a capability the entity cannot support, and nothing is generated for it. Remove the capability, or give the entity what it needs: `[SoftDelete]` is what makes a delete leave a row `Restore` can bring back. |
| `PRAG2611` | Error | An entity declares `[PartOf<TParent>]` (it is written through its parent) and `[Resource]`, which would give it create, update and delete endpoints of its own, bypassing the parent. Keep one of the two. |
| `PRAG2612` | Warning | A `[Resource]` declares no `Capabilities`, so nothing is scaffolded for it: no DTO, no action, no endpoint. Set `Capabilities`, or remove the attribute. |
| `PRAG2650` | Error | A trait generates a navigation whose name the type already declares. Rename the existing member: the generated navigation cannot be renamed, and without this the build fails with CS0102 inside generated code. |
| `PRAG2651` | Warning | `[HasAttachments]` asks for a thumbnail and this compilation has no `Pragmatic.Imaging`, so none is generated. Reference the package; it P/Invokes a native library shipped per runtime identifier. |

### Value objects: `PRAG2700-2701`

| ID | Severity | Meaning and fix |
|----|----------|-----------------|
| `PRAG2700` | Warning | A `[ValueObject]` type is not `partial`, so `Create`/`CreateUnsafe` cannot be generated. |
| `PRAG2701` | Warning | A `[ValueObject]` type declares no `private static` `Validate` method, so `Create` cannot be generated. |

### Entity lifecycle events: `PRAG2750-2753`

| ID | Severity | Meaning and fix |
|----|----------|-----------------|
| `PRAG2750` | Error | A type with `[Raises<T>]` does not derive from `DomainEventSource`, so the generated lifecycle events cannot be raised. |
| `PRAG2751` | Warning | An event constructor parameter matched no entity member by name and is passed `default`. Rename it to match, or raise the event from a domain method. |
| `PRAG2752` | Warning | `[EnableEventOutbox]` without a reference to `Pragmatic.Events.EFCore`: the outbox is not wired, so the attribute is a no-op. |
| `PRAG2753` | Error | `[Raises<T>]` on an **entity's method**: nothing generates that raise, while the class-level form on the same entity is wired. Declare it on the class, use `[RaisesEvent<T>]` on the state machine's target member, or call `RaiseEvent(...)` in the body. |

### Serialization / AOT: `PRAG2800`

| ID | Severity | Meaning and fix |
|----|----------|-----------------|
| `PRAG2800` | Info | A type serialized by Pragmatic (message, event or job payload) is not covered by a `[JsonSerializable]` entry in the project's `JsonSerializerContext`. Add it so serialization stays AOT-safe. |

### Privacy: `PRAG2900-2913`

| ID | Severity | Meaning and fix |
|----|----------|-----------------|
| `PRAG2900` | Error | A type declares personal data and no path to a `[DataSubject]`, so its rows can never be erased and an erasure request would silently leave them behind. Add `[LinksToSubject(nameof(…))]` naming the property that leads to the subject. |
| `PRAG2901` | Error | `ErasureStrategy.Retain` is declared without a `Reason`. Retaining data against an erasure request is legitimate; retaining it without saying why is the same as forgetting to erase it. State the obligation that justifies it. |
| `PRAG2902` | Error | `ErasureStrategy.DestroyKey` is declared on a property that is not encrypted. Destroying a key that protects nothing reports an erasure that did not happen: set `Encrypted = true`, or choose another strategy. |
| `PRAG2903` | Error | A property reachable from a `[DataSubject]` carries no `[PersonalData]` classification. Classify it, or mark it as non-personal: an unclassified field is indistinguishable from one nobody thought about. |
| `PRAG2904` | Warning | An endpoint exposes `DataCategory.Special` data without naming who may read it. Requiring only a signed-in user lets every account read it. Add a permission, role or policy. |
| `PRAG2906` | Error | `[LinksToSubject]` names a property that does not exist or is not a navigation. A path that cannot be followed produces an erasure that misses rows without failing. Name a navigation or foreign-key property leading to the subject. |
| `PRAG2907` | Error | Personal data declares an erasure strategy and nothing can write the property: its setter is not public and the type is not an `[Entity]`, so no setter is generated. Make the setter public, or declare the type an entity. |
| `PRAG2908` | Warning | The subject identifier is of a type the generated data source and erasure step cannot compare against the identity the registry resolves. Use `string`, `System.Guid`, `int` or `long`. |
| `PRAG2909` | Error | `ErasureStrategy.Null` is declared on a property whose type cannot hold null: the write would fail at `SaveChanges` against the `NOT NULL` column. Make the property nullable, or choose `Anonymize`, `Pseudonymize` or `Retain`. |
| `PRAG2910` | Warning | `[RecordAccess]` is declared and the assembly does not reference `Pragmatic.Audit`, so the reads are not recorded. Add the reference, or remove the attribute. |
| `PRAG2911` | Warning | An operation composes through another whose entities cannot be inferred and declares no `[ProcessesData]`, so the Article 30 register does not list it as processing anything. Declare `[ProcessesData<TEntity>]` for each entity it reaches. |
| `PRAG2912` | Error | `ErasureStrategy.Anonymize` is declared on a type that has no value identifying nobody which a `NOT NULL` column would accept. Make the property nullable, or choose `Pseudonymize`, `Delete` or `Retain`. |
| `PRAG2913` | Info | `[ProcessesData<T>]` restates what the generator already infers from the operation's repositories, invokers and loads. Remove it: the register lists the entity either way. |

### Source generator infrastructure: `PRAG9000-9001`

| ID | Severity | Meaning and fix |
|----|----------|-----------------|
| `PRAG9000` | Error | A generator output failed. The code it should have produced is missing, so expect unresolved-type errors elsewhere. Other outputs are unaffected; please report the triggering code. |
| `PRAG9001` | Warning | A type the operation names does not resolve (a missing `using` in its own file, most often), so nothing was generated for it. Fix that name and the generated code comes back with it. |

## Suppressors: `PRAGS001-PRAGS004`

Generated code routinely trips compiler and analyzer rules that reason about only one half of a `partial` type. Rather than force `#pragma` noise into your source, Pragmatic ships `DiagnosticSuppressor`s that switch those warnings off, but only where the generator really is the missing half. Outside those conditions the original warning stays visible, because there it is a genuine finding.

| ID | Suppresses | Applies when |
|----|-----------|--------------|
| `PRAGS001` | `CS8618`: non-nullable property not initialized | The member is a **property** with a setter that is non-public or `init`, on a `partial` type carrying `[Entity]` (directly or inherited). Those are the properties the generated `Create()` factory and the trait templates assign. A public setter or a field is yours, so the warning stays. |
| `PRAGS002` | `CA1822`: member can be marked `static` | The containing type is `partial` and Pragmatic-decorated (`[Entity]`, `[DomainAction]`, `[Mutation]`, `[Query]`, `[MapFrom]`, `[MapTo]`), **and** either the member sits in a generated file, or it is one half of a `partial` method. A plain hand-written method that ignores instance state is still reported. |
| `PRAGS003` | `CA1062`: validate arguments of public methods | The diagnostic falls inside a generated file (`*.g.cs` / `*.generated.cs`) of a Pragmatic-decorated type. Generated invokers and repositories receive their dependencies from the generated DI constructor. In a hand-written file `CA1062` remains a real finding, even in the other half of the same partial type. |
| `PRAGS004` | `IDE0051`: private member unused | The containing type is `partial` and Pragmatic-decorated. The generated half may reference the member from code the IDE cannot see. Without `partial` there is no such half, so an unused private member is genuinely dead. |

Suppressors need no configuration; they ship with the generator package.

## Suppressing a diagnostic

```xml
<!-- Project-wide, in the .csproj -->
<PropertyGroup>
  <NoWarn>$(NoWarn);PRAG0303</NoWarn>
</PropertyGroup>
```

```csharp
// Per-file or per-line
#pragma warning disable PRAG0303 // No matching source property; set manually after mapping
public partial class UserDto { }
#pragma warning restore PRAG0303
```

```ini
# .editorconfig: change the severity instead of silencing it
[*.cs]
dotnet_diagnostic.PRAG0325.severity = warning
```

Prefer a **targeted suppression with a comment** explaining why. These diagnostics are opinionated guardrails, and suppressing one is usually a signal that a framework assumption does not fit your case. Avoid suppressing Error-level codes: they mark generated code that will not work.

## Contributing new diagnostics

New diagnostics pick an ID inside an unused part of their module's range and follow the convention:

1. Descriptor in `{Module}Diagnostics.cs` with `Category = "Pragmatic.{Module}"`.
2. Title as a one-line imperative sentence.
3. `MessageFormat` with positional `{0}`/`{1}` placeholders, never string interpolation.
4. Severity: Error only when the generated code will not work; Warning for "works but surprising"; Info for hints.
5. The descriptor must actually be reported somewhere, and a test in the module's `Generator/` or `Analyzers/` test project must assert both emission and non-emission.
6. A row in the section of its range on this page, saying what it means and what to do. The gate reads it: `node scripts/site-diagnostics.mjs` fails when a declared descriptor has no row here, and a mention in prose does not count as one.
