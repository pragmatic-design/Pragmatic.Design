---
title: "Pragmatic.Mapping - Feature Matrix"
description: "> Tracks the implemented and tested features."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Mapping/docs/feature-matrix.md
sidebar:
  order: 5
---
> Tracks the implemented and tested features.
> Every cell is a combination that MUST be supported and tested.

**Test Summary**: see `Pragmatic.Mapping.Tests` and `Pragmatic.Mapping.EFCore.Tests` (Mapping 291 tests + the EFCore Postgres suite)

---

## Legend

| Symbol | Meaning |
|--------|---------|
| ✅ | Implemented & Tested |
| ⚠️ | Implemented, needs fix |
| ❌ | Not implemented |
| 🔲 | Planned |

---

## 1. Type Kinds (DTO Type)

| Type Kind | FromEntity | ToEntity | Projection | Test File |
|-----------|------------|----------|------------|-----------|
| `class` | ✅ | ✅ | ✅ | `NullableAndNestedGeneratorTests.PartialClass_GeneratesCorrectly` |
| `record` | ✅ | ✅ | ✅ | `BasicMappingGeneratorTests` |
| `struct` | ✅ | 🔲 | 🔲 | `NullableAndNestedGeneratorTests.PartialStruct_GeneratesCorrectly` |
| `record struct` | ✅ | 🔲 | 🔲 | `BasicMappingGeneratorTests.MapFrom_RecordStruct_GeneratesCorrectly` |

---

## 2. Source Type Kinds (Entity Type)

| Source Type Kind | Support | Test |
|------------------|---------|------|
| `class` | ✅ | All basic tests |
| `record` | ✅ | - |
| `struct` | ✅ | `NullableAndNestedGeneratorTests.PartialStruct_GeneratesCorrectly` |
| `record struct` | ✅ | - |

**Note**: Null check skipped for value types (structs can't be null).

---

## 3. Property Types

### 3.1 Simple Types

| Type | Direct Mapping | Test |
|------|----------------|------|
| `int` | ✅ | `BasicMappingGeneratorTests` |
| `int?` | ✅ | `NullableAndNestedGeneratorTests.NullableInt_MapsDirectly` |
| `string` | ✅ | Multiple tests |
| `string?` | ✅ | `NullableAndNestedGeneratorTests.NullableString_MapsDirectly` |
| `decimal` | ✅ | `NullableAndNestedGeneratorTests.PartialClass_GeneratesCorrectly` |
| `DateTime` | ✅ | - |
| `DateTime?` | ✅ | `NullableAndNestedGeneratorTests.NullableDateTime_MapsDirectly` |
| `Guid` | ✅ | - |
| `Guid?` | ✅ | `TypeConversionGeneratorTests.NullableGuid_MapsDirectly` |
| `DateOnly` | ✅ | `TypeConversionGeneratorTests.DateOnly_MapsDirectly` |
| `TimeOnly` | ✅ | `TypeConversionGeneratorTests.DateOnly_MapsDirectly` |
| `DateOnly?` | ✅ | `TypeConversionGeneratorTests.NullableDateOnly_MapsDirectly` |
| `TimeOnly?` | ✅ | `TypeConversionGeneratorTests.NullableDateOnly_MapsDirectly` |
| `enum` | ✅ | `TypeConversionGeneratorTests.Enum_SameType_MapsDirectly` |
| `enum?` | ✅ | `TypeConversionGeneratorTests.NullableEnum_MapsDirectly` |

### 3.2 Collection Types

| Collection Type | Simple Element | DTO Element | Test |
|-----------------|----------------|-------------|------|
| `T[]` | ✅ | ✅ | `CollectionMappingGeneratorTests.ArrayOfInt`, `ArrayOfDto` |
| `List<T>` | ✅ | ✅ | `CollectionMappingGeneratorTests.ListOfString`, `ListOfDto` |
| `List<T>?` | ✅ | ✅ | `NullableListOfNullableInt`, `NullableListOfNullableNestedDto` |
| `IList<T>` | ✅ | ✅ | `CollectionMappingGeneratorTests.IListOfInt` |
| `ICollection<T>` | ✅ | ✅ | `CollectionMappingGeneratorTests.ICollectionOfInt` |
| `IEnumerable<T>` | ✅ | ✅ | `CollectionMappingGeneratorTests.IEnumerableOfInt` |
| `IReadOnlyList<T>` | ✅ | 🔲 | `CollectionMappingGeneratorTests.IReadOnlyListOfInt` |
| `IReadOnlyCollection<T>` | ✅ | 🔲 | `CollectionMappingGeneratorTests.IReadOnlyCollectionOfInt` |
| `HashSet<T>` | ✅ | 🔲 | `CollectionMappingGeneratorTests.HashSetOfInt` |

### 3.3 Nested DTO Types

| Scenario | Status | Test |
|----------|--------|------|
| Single non-nullable DTO | ✅ | `NullableAndNestedGeneratorTests.NestedDto_MapsWithFromEntity` |
| Single nullable DTO | ✅ | `NullableAndNestedGeneratorTests.NullableNestedDto_HandlesNullSafely` |
| Array of DTO | ✅ | `NullableAndNestedGeneratorTests.ArrayOfNestedDto_MapsEachElement` |
| List of DTO | ✅ | `CollectionMappingGeneratorTests.ListOfDto_MapsEachElement` |
| List of nullable DTO | ✅ | `NullableAndNestedGeneratorTests.NullableListOfNullableNestedDto_HandlesCorrectly` |

**Implementation**: Uses `SymbolDisplayFormat.FullyQualifiedFormat` for type names to avoid namespace resolution issues.

### 3.4 Dictionary Types

| Dictionary Type | Simple Value | DTO Value | Test |
|-----------------|--------------|-----------|------|
| `Dictionary<K,V>` | ✅ | ✅ | `CollectionMappingGeneratorTests.DictionaryStringInt`, `TypeConversionGeneratorTests.DictionaryWithDtoValue` |
| `IDictionary<K,V>` | 🔲 | 🔲 | - |
| `IReadOnlyDictionary<K,V>` | 🔲 | 🔲 | - |

### 3.5 Type Conversions (Implicit/Widening)

| Conversion | Status | Test |
|------------|--------|------|
| `int` → `long` | ✅ | `TypeConversionGeneratorTests.IntToLong_ImplicitConversion` |
| `int` → `decimal` | ✅ | `TypeConversionGeneratorTests.IntToDecimal_ImplicitConversion` |
| `int?` → `int` (no default) | ⚠️ CS0266 | `TypeConversionGeneratorTests.NullableIntToInt_WithoutDefault_ShouldCompile` |
| `int?` → `int` (with default) | ✅ | `AdvancedMappingGeneratorTests.MapProperty_WithDefault_UsesDefaultWhenNull` |

**Note**: C# implicit conversions (widening) work automatically. Nullable → Non-nullable requires `[MapProperty(Default=)]`.

### 3.6 Type Conversions (Explicit) ✅ IMPLEMENTED

| Conversion | Status | Generated Code | Test |
|------------|--------|----------------|------|
| `int` → `string` | ✅ | `.ToString()` | `TypeConversionGeneratorTests.IntToString` |
| `string` → `int` | ✅ | `int.Parse()` | `TypeConversionGeneratorTests.StringToInt` |
| `enum` → `string` | ✅ | `.ToString()` | `TypeConversionGeneratorTests.EnumToString` |
| `string` → `enum` | ✅ | `Enum.Parse<T>()` | `TypeConversionGeneratorTests.StringToEnum` |
| `Guid` → `string` | ✅ | `.ToString()` | `TypeConversionGeneratorTests.GuidToString` |
| `string` → `Guid` | ✅ | `Guid.Parse()` | `TypeConversionGeneratorTests.StringToGuid` |
| `DateTime` → `DateOnly` | ✅ | `DateOnly.FromDateTime()` | `TypeConversionGeneratorTests.DateTimeToDateOnly` |
| `DateOnly` → `DateTime` | ✅ | `.ToDateTime(TimeOnly.MinValue)` | `TypeConversionGeneratorTests.DateOnlyToDateTime` |
| `DateTime` → `TimeOnly` | ✅ | `TimeOnly.FromDateTime()` | `TypeConversionGeneratorTests.DateTimeToTimeOnly` |
| `decimal` → `string` | ✅ | `.ToString()` | `TypeConversionGeneratorTests.DecimalToString` |
| `string` → `decimal` | ✅ | `decimal.Parse()` | `TypeConversionGeneratorTests.StringToDecimal` |
| `bool` → `string` | ✅ | `.ToString()` | `TypeConversionGeneratorTests.BoolToString` |
| `string` → `bool` | ✅ | `bool.Parse()` | `TypeConversionGeneratorTests.StringToBool` |
| `DateTime` → `string` | ✅ | `.ToString()` | `TypeConversionGeneratorTests.DateTimeToString` |
| `string` → `DateTime` | ✅ | `DateTime.Parse()` | `TypeConversionGeneratorTests.StringToDateTime` |
| `string` → `DateOnly` | ✅ | `DateOnly.Parse()` | `TypeConversionGeneratorTests.StringToDateOnly` |
| `string` → `TimeOnly` | ✅ | `TimeOnly.Parse()` | `TypeConversionGeneratorTests.StringToTimeOnly` |

**Implementation**: Generator detects source/target type mismatch and applies known conversion patterns automatically.
Nullable variants also supported (e.g., `int?` → `string`, `string` → `int?`).

### 3.7 Collection Type Conversions

| Source → Target | Status | Test |
|-----------------|--------|------|
| `List<T>` → `T[]` | ✅ | `TypeConversionGeneratorTests.ListToArray_Conversion` |
| `T[]` → `List<T>` | ✅ | `TypeConversionGeneratorTests.ArrayToList_Conversion` |
| `IEnumerable<T>` → `List<T>` | ✅ | `TypeConversionGeneratorTests.IEnumerableToList_Conversion` |
| `List<T>` → `IEnumerable<T>` | ✅ | `TypeConversionGeneratorTests.ListToIEnumerable_Conversion` |
| `List<T>` → `HashSet<T>` | ✅ | `TypeConversionGeneratorTests.ListToHashSet_Conversion` |
| `T[]` → `IReadOnlyList<T>` | ✅ | `TypeConversionGeneratorTests.ArrayToIReadOnlyList_Conversion` |

---

## 4. Mapping Attributes

| Attribute | Status | Test |
|-----------|--------|------|
| `[MapFrom<T>]` | ✅ | All FromEntity tests |
| `[MapTo<T>]` | ✅ | `AdvancedMappingGeneratorTests.MapTo_*` |
| `[MapProperty]` (path / Default / Format / Target / Separator) | ✅ | `AdvancedMappingGeneratorTests.MapProperty_*` |
| `[MapIgnore]` | ✅ | `AdvancedMappingGeneratorTests.MapIgnore_*` |
| `[MapConverter<T>]` (property-level) | ✅ | `AdvancedMappingGeneratorTests.Converter_*` |
| `[MapConverter<T>]` (class-level, signature-matched) | ✅ | `ConventionAndTypeLevelTests` |
| `[MapConstructor]` | ✅ | `MapConstructorAndSeparatorGeneratorTests` |
| `[MapCondition]` (runtime gate, and projection gate for an expression-bodied predicate; PRAG0329/0332) | ✅ | `DiagnosticTests`, `RuntimeDispatchTests`, `ADetailInOneProjectionTests` |
| `[MapDerived<TS,TD>]` (polymorphic; PRAG0330/0331) | ✅ | `DiagnosticTests`, `RuntimeDispatchTests` |
| `[GenerateProjection(MaxDepth=)]` | ✅ | `ProjectionGeneratorTests`, `DiagnosticTests` (PRAG0327) |
| `[GenerateBodyOnlyVariant]` | ✅ | `BodyOnlyVariantGeneratorTests` |

---

## 5. Mapping Features

### 5.1 Resolution Strategies

| Strategy | Status | Test |
|----------|--------|------|
| Direct name match | ✅ | All basic tests |
| Case-insensitive match | ✅ | - |
| Flattening (`AddressCity` → `Address.City`) | ✅ | `AdvancedMappingGeneratorTests.Flattening_*` |
| Concatenation (`FullName` → `FirstName + LastName`) | ✅ | `BasicMappingGeneratorTests.MapFrom_WithFullNameConvention` |
| Explicit path via `[MapProperty]` | ✅ | `AdvancedMappingGeneratorTests.MapProperty_*` |

### 5.2 Special Handling

| Feature | Status | Test |
|---------|--------|------|
| ID property exclusion in ToEntity | ✅ | `AdvancedMappingGeneratorTests.MapTo_ExcludesIdByDefault` |
| Init-only properties | ✅ | - |
| Required properties | ✅ | - |
| Circular reference detection | ✅ | - |
| Null safety | ✅ | `NullableAndNestedGeneratorTests.*` |

### 5.3 Generated Methods

| Method | Status | Struct Support | Test |
|--------|--------|----------------|------|
| `FromEntity(entity)` | ✅ | ✅ | All FromEntity tests |
| `ToEntity()` | ✅ | 🔲 | MapTo tests |
| `Projection` | ✅ | 🔲 | ProjectionGeneratorTests |

> **Note**: `ApplyTo(entity)` is generated by Pragmatic.Mapping for DTOs marked with `[MapTo<T>]` (updates an existing entity, partial-update semantics). It is suppressed only when the DTO also carries `[Patch<T>]` or `[Mutation<T>]`, in which case the respective generator owns the update path.

### 5.4 Customization Hooks

| Hook | Status | Test |
|------|--------|------|
| `partial void BeforeMapping()` | ✅ | - |
| `partial void CustomizeMapping()` | ✅ | - |
| `MappingContext` struct | ✅ | - |

---

## 6. Accessibility

| DTO Accessibility | Support | Extensions Class | Test |
|-------------------|---------|------------------|------|
| `public` | ✅ | `public static` | All basic tests |
| `internal` | ✅ | `internal static` | `AdvancedMappingGeneratorTests.MapFrom_InternalClass_GeneratesInternalMethods` |
| `private` (nested) | 🔲 | - | - |

---

## 7. Fixed Bugs

| Bug | Status | Fix |
|-----|--------|-----|
| ~~Struct source + `Ensure.ThrowIfNull`~~ | ✅ FIXED | Skip null check for value types |
| ~~Nullable DTO type name includes `?`~~ | ✅ FIXED | Use `SymbolDisplayFormat.FullyQualifiedFormat` |
| ~~`List<string>` treated as DTO collection~~ | ✅ FIXED | Check `IsElementSimple` in `AnalyzeNestedDto` |
| ~~Extensions class ignores accessibility~~ | ✅ FIXED | Added `ParseAccessibility()` in `MappingExtensionsTemplate` |
| ~~`[MapProperty(Default=)]` not implemented~~ | ✅ FIXED | Added fallback resolution by property name when only Default is specified |

## 7.1 Recently Fixed Bugs

| Issue | Status | Fix | Test |
|-------|--------|-----|------|
| `[MapProperty(Format=)]` for non-DateTime | ✅ FIXED | Added format support to `GenerateNumericToString` and `GenerateToString` | `AdvancedMappingGeneratorTests.MapProperty_WithFormat_*` |
| `[MapProperty] + [MapConverter]` together | ✅ FIXED | Moved converter check before MapProperty check, passed converter to `CreateExplicitMapping` | `AdvancedMappingGeneratorTests.MapProperty_WithMapConverter_*` |

---

## 8. Test Coverage Summary

| Test File | Tests | Passing | Failing |
|-----------|-------|---------|---------|
| `BasicMappingGeneratorTests` | 7 | 7 | 0 |
| `AdvancedMappingGeneratorTests` | 16 | 16 | 0 |
| `CollectionMappingGeneratorTests` | 9 | 9 | 0 |
| `ProjectionGeneratorTests` | 5 | 5 | 0 |
| `NullableAndNestedGeneratorTests` | 9 | 9 | 0 |
| `TypeConversionGeneratorTests` | 51 | 51 | 0 |
| `AutomaticConversionGeneratorTests` | 12 | 12 | 0 |
| `AutoDefaultGeneratorTests` | 12 | 12 | 0 |
| `DiagnosticTests` | 4 | 4 | 0 |
| **TOTAL** | **125** | **125** | **0** |

**Note**: EF Core integration tests use TestContainers with PostgreSQL.

---

## 9. Sample Coverage

| Sample File | Features Demonstrated |
|-------------|----------------------|
| `BasicMappingSample` | FromEntity, extension methods, collection mapping, FullName concatenation, flattening |
| `NestedMappingSample` | Nested DTOs, collection of DTOs, flattened DTO |
| `BidirectionalMappingSample` | MapFrom + MapTo, ToEntity() |
| `StructMappingSample` | record struct, zero-allocation, Projection |
| `ProjectionSample` | GenerateProjection, Expression<Func<T,TDto>> |
| `TypeConversionSample` | ToString, Parse, DateTime↔DateOnly, Dictionary with DTOs, Inheritance |
| `NullableAndAdvancedSample` | Auto-default, Collection type conversions, Circular references, DateOnly→DateTime |
| `TypeCombinationsSample` | class→class, class→record, record→record, record→class, MapIgnore |
| `AdvancedAttributesSample` | MapIgnore, MapConverter, MapProperty(path), MapProperty(Format) |

---

## 10. Completed Milestones ✅

- [x] Core source generator (FromEntity, ToEntity, Projection)
- [x] All property types (simple, nullable, collections, nested DTOs)
- [x] Property resolution (direct match, flattening, concatenation)
- [x] MapProperty with Default value
- [x] MapIgnore, MapConverter attributes
- [x] EF Core integration package with extension methods
- [x] Comprehensive samples (Basic, Nested, Bidirectional, Struct, Projection, TypeConversion)
- [x] README documentation
- [x] **P0: Auto-default for primitives** (GetValueOrDefault, ?? "")
- [x] **P1: Type conversions** (ToString, Parse, enum↔string, Guid↔string)
- [x] **P2: DateTime conversions** (DateTime↔DateOnly/TimeOnly)
- [x] **P3: Dictionary with DTO values, Inheritance mapping**

---

## 11. Priority Order for Remaining Work

### P0 - Critical (MVP completeness) ✅ COMPLETE

| Feature | Description | Status |
|---------|-------------|--------|
| **Auto-default for primitives** | `int?` → `int` uses `GetValueOrDefault()`, `string?` → `string` uses `?? ""` | ✅ Implemented |
| **Auto-default in Projection** | Uses `?? 0`, `?? ""`, `?? Guid.Empty` for EF Core Expression Tree compatibility | ✅ Implemented |

### P1 - High (User experience) ✅ COMPLETE

| Feature | Description | Status | Test |
|---------|-------------|--------|------|
| **ToString conversions** | `int` → `string` via `.ToString()` | ✅ | `TypeConversionGeneratorTests.IntToString` |
| **Enum → string** | `Status.Active` → `"Active"` | ✅ | `TypeConversionGeneratorTests.EnumToString` |
| **Parse conversions** | `string` → `int` via `int.Parse()` | ✅ | `TypeConversionGeneratorTests.StringToInt` |
| **Guid ↔ string** | `Guid.ToString()` / `Guid.Parse()` | ✅ | `TypeConversionGeneratorTests.GuidToString/StringToGuid` |

### P2 - Medium (Completeness) ✅ COMPLETE

| Feature | Description | Status | Test |
|---------|-------------|--------|------|
| **DateTime → DateOnly** | `DateOnly.FromDateTime()` | ✅ | `TypeConversionGeneratorTests.DateTimeToDateOnly` |
| **DateTime → TimeOnly** | `TimeOnly.FromDateTime()` | ✅ | `TypeConversionGeneratorTests.DateTimeToTimeOnly` |
| **DateOnly → DateTime** | `.ToDateTime(TimeOnly.MinValue)` | ✅ | `TypeConversionGeneratorTests.DateOnlyToDateTime` |
| **String ↔ decimal** | Parse/ToString | ✅ | `TypeConversionGeneratorTests.StringToDecimal/DecimalToString` |

### P3 - Low (Nice to have) ✅ COMPLETE

| Feature | Description | Status | Test |
|---------|-------------|--------|------|
| Dictionary with DTO values | `Dictionary<string, CategoryDto>` | ✅ | `TypeConversionGeneratorTests.DictionaryWithDtoValue` |
| Inheritance mapping | Base → Derived DTO properties | ✅ | `TypeConversionGeneratorTests.InheritedProperties` |
| Interface-based DTOs | `IUserDto` | 🔲 | Future |

---

## 12. Required Test Coverage (VERIFIED)

### Must-Have Test Scenarios

| Scenario | Category | Test File | Status |
|----------|----------|-----------|--------|
| Same-name property mapping | Basic | `BasicMappingGeneratorTests` | ✅ |
| Different-name property mapping | Advanced | `AdvancedMappingGeneratorTests` | ✅ |
| Custom converter | Advanced | `AdvancedMappingGeneratorTests` | ✅ |
| Nested DTO mapping | Nested | `NullableAndNestedGeneratorTests` | ✅ |
| Collection of DTOs | Collection | `CollectionMappingGeneratorTests` | ✅ |

### Diagnostic Tests (Behavior Validation)

| Scenario | Test | Status |
|----------|------|--------|
| Non-partial class skips generation | `DiagnosticTests.NonPartialClass_SkipsGeneration` | ✅ |
| Partial class generates correctly | `DiagnosticTests.PartialClass_GeneratesCorrectly` | ✅ |
| Unmatched property uses default | `DiagnosticTests.UnmatchedProperty_GeneratesWithDefault` | ✅ |
| Invalid MapProperty path | `DiagnosticTests.MapProperty_WithInvalidPath_CompilesButUnmapped` | ✅ |
| [GenerateProjection] without [MapFrom] | `DiagnosticTests.GenerateProjection_WithoutMapFrom_SkipsProjection` | ✅ |
| [MapIgnore] takes precedence | `DiagnosticTests.MapIgnore_TakesPrecedence_OverMapProperty` | ✅ |
| Nested DTO with [MapFrom] | `DiagnosticTests.NestedDto_WithMapFrom_GeneratesCorrectly` | ✅ |
| Collection with simple elements | `DiagnosticTests.Collection_WithSimpleElements_MapsDirectly` | ✅ |
| Struct source skips null check | `DiagnosticTests.StructSource_SkipsNullCheck` | ✅ |
| Class source includes null check | `DiagnosticTests.ClassSource_IncludesNullCheck` | ✅ |
| MapTo excludes ID by default | `DiagnosticTests.MapTo_ExcludesIdByDefault` | ✅ |
| Internal class generates internal extensions | `DiagnosticTests.InternalClass_GeneratesInternalExtensions` | ✅ |
| Empty DTO generates minimal | `DiagnosticTests.EmptyDto_GeneratesMinimalMapping` | ✅ |
| Multiple [MapFrom] causes error | `DiagnosticTests.MultipleMapFrom_CausesDuplicateAttributeError` | ✅ |
| Bidirectional mapping | `DiagnosticTests.MapFrom_And_MapTo_OnSameType_GeneratesBothMethods` | ✅ |

---

## 13. Feature Backlog

### Implemented ✅
- [x] `[MapProperty(Default = "value")]` per default values
- [x] Flattening convention (`Address.City` → `AddressCity`)
- [x] Concatenation convention (`FullName` → `FirstName + LastName`)
- [x] EF Core projection expressions

### Recently Completed ✅
- [x] **Auto-default for primitives** (`int?` → `int` = `GetValueOrDefault()`)
- [x] **Auto-default for strings** (`string?` → `string` = `?? ""`)
- [x] **Auto-default in Projections** (EF Core compatible with `?? value`)
- [x] **Type conversions** (ToString, Parse, enum↔string, Guid↔string)
- [x] **DateTime conversions** (DateTime↔DateOnly, DateTime↔TimeOnly)
- [x] **Dictionary with DTO values** (`Dictionary<string, CategoryDto>`)
- [x] **Inheritance mapping** (base class properties mapped automatically)

### Planned 🔲
- [ ] Custom projection expressions
- [ ] Async mapping per lazy loading
- [ ] Interface-based DTOs

---

## 14. EF Core Integration

### The Include Problem

When using EF Core, navigation properties require explicit `Include()` calls when using `FromEntity()`:

```csharp
// ⚠️ PROBLEM: Lazy loading or null navigation properties
var users = await _db.Users.ToListAsync();
var dtos = users.ToUserDto();  // Orders will be null/empty!

// ✅ SOLUTION 1: Manual Include
var users = await _db.Users
    .Include(u => u.Orders)
    .ToListAsync();
var dtos = users.ToUserDto();

// ✅ SOLUTION 2: Use Projection (BEST - optimized SQL)
var dtos = await _db.Users
    .Select(UserDto.Projection)
    .ToListAsync();
```

### Implemented Features (Pragmatic.Mapping.EFCore)

| Feature | Status | Description |
|---------|--------|-------------|
| `SelectDto(projection)` | ✅ | Projects query using expression |
| `ToListDtoAsync(projection)` | ✅ | Async projection to list |
| `FirstOrDefaultDtoAsync(projection)` | ✅ | Async single item projection |
| `SingleOrDefaultDtoAsync(projection)` | ✅ | Async single-or-default projection |
| `ToArrayDtoAsync(projection)` | ✅ | Async projection to array |
| `ToPagedDtoAsync(projection, page, size)` | ✅ | Paginated projection with metadata |
| `ToSliceDtoAsync(projection, offset, limit)` | ✅ | Offset/limit projection |

### Usage (Current)

```csharp
// Projection-based (generates optimized SQL)
var dtos = await _db.Users
    .Where(u => u.IsActive)
    .ToListDtoAsync(UserDto.Projection);

// Paginated results
var page = await _db.Users
    .Where(u => u.IsActive)
    .OrderBy(u => u.Name)
    .ToPagedDtoAsync(UserDto.Projection, pageNumber: 1, pageSize: 20);
// page.Items, page.TotalCount, page.TotalPages, page.HasNextPage, etc.
```

### Future Features (Pragmatic.Mapping.DomainEntity)

| Feature | Status | Description |
|---------|--------|-------------|
| `RequiredIncludes<TDto>()` | 🔲 Planned | Returns Include chain for FromEntity |
| `WithRequiredIncludes<TDto>()` | 🔲 Planned | Applies required includes to query |

> **Note**: Auto-Include features require DomainEntity metadata (not yet implemented).
