# PRAG diagnostics dictionary

Every `DiagnosticDescriptor` declared in the repository, extracted from the code rather than written by hand.
What each diagnostic means and how to fix it is on the [diagnostics reference](../site/docs/src/content/docs/reference/diagnostics.md).

> **Regenerate, do not edit.** `node scripts/sync-diagnostics.mjs`. When this document and the code
> disagree, the document is stale.

## Summary

| | |
|---|---|
| Declared descriptors | **413** |
| Distinct IDs | **413** |
| Collisions | **0** |
| IDs outside the declared ranges | **0** |

## By range

### Result — 2 in use · range `PRAG0001`–`PRAG0099`

| ID | Symbol | Severity | Title |
|---|---|---|---|
| `PRAG0001` | `UnsafeValueAccess` | Warning | Unsafe Result.Value access |
| `PRAG0002` | `MissingPartialOnErrorWithCustomProperties` | Warning | Missing 'partial' on error type with custom properties |

*First free after the last used:* `PRAG0003`.

### Ensure — 1 in use · range `PRAG0100`–`PRAG0199`

| ID | Symbol | Severity | Title |
|---|---|---|---|
| `PRAG0100` | `HandWrittenArgumentGuard` | Info | Hand-written argument guard where Ensure applies |

*First free after the last used:* `PRAG0101`.

### Validation — 12 in use · range `PRAG0200`–`PRAG0299`

| ID | Symbol | Severity | Title |
|---|---|---|---|
| `PRAG0200` | `Prag0200` | Error | Type with validation attributes must be partial |
| `PRAG0201` | `ValidatorMustImplementInterface` | Error | [Validator] class must implement IValidator<T> |
| `PRAG0203` | `ComparisonPropertyNotFound` | Error | Comparison property not found |
| `PRAG0204` | `ValidateElementsOnNonCollection` | Warning | [ValidateElements] on non-collection type |
| `PRAG0205` | `ValidateElementsNotValidatable` | Error | [ValidateElements] element type doesn't implement ISyncValidator |
| `PRAG0209` | `IncompatibleComparisonTypes` | Warning | Incompatible comparison types |
| `PRAG0210` | `Prag0210` | Warning | Validation attribute is ignored by the generator |
| `PRAG0215` | `AsyncValidatorOutsideTheOperationsAssembly` | Warning | An operation's async validator must be declared in the operation's assembly |
| `PRAG0220` | `ValidEnumOnNonEnum` | Error | [ValidEnum] requires an enum |
| `PRAG0221` | `ContainingTypeMustBePartial` | Error | A validated type's containers must be partial |
| `PRAG0222` | `MessageKeyCannotBeRead` | Warning | A MessageKey the generator cannot read |
| `PRAG0223` | `ValidateElementsConfiguresNothing` | Error | [ValidateElements] configures nothing here |

*Free inside the used span:* `PRAG0202`, `PRAG0206`, `PRAG0207`, `PRAG0208`, `PRAG0211`, `PRAG0212`, `PRAG0213`, `PRAG0214`, `PRAG0216`, `PRAG0217`, `PRAG0218`, `PRAG0219`.

*First free after the last used:* `PRAG0224`.

### Mapping — 35 in use · range `PRAG0300`–`PRAG0399`

| ID | Symbol | Severity | Title |
|---|---|---|---|
| `PRAG0300` | `Prag0300` | Error | Type must be partial |
| `PRAG0302` | `PropertyNotFound` | Error | Property not found on source type |
| `PRAG0303` | `NoMatchingSourceProperty` | Warning | No matching source property |
| `PRAG0304` | `IncompatibleTypes` | Error | Incompatible types |
| `PRAG0305` | `ConverterMustImplementInterface` | Error | Converter must implement IValueConverter |
| `PRAG0306` | `ConverterMustHaveParameterlessConstructor` | Error | Converter must have parameterless constructor |
| `PRAG0307` | `RequiredPropertyNotMapped` | Warning | Required property not mapped |
| `PRAG0309` | `NestedTypeMissingMapFrom` | Error | Nested type missing [MapFrom] |
| `PRAG0310` | `ProjectionRequiresMapFrom` | Error | [GenerateProjection] requires [MapFrom] |
| `PRAG0313` | `CircularReferenceDetected` | Info | Circular reference detected |
| `PRAG0314` | `ConflictingAttributes` | Error | Conflicting attributes |
| `PRAG0315` | `NestedDtoMismatch` | Error | Nested DTO property mismatch |
| `PRAG0316` | `NoSuitableConstructor` | Error | No suitable constructor found |
| `PRAG0317` | `NullableWithoutDefault` | Error | Nullable to non-nullable without Default |
| `PRAG0319` | `CustomizeMappingIgnoredInProjection` | Warning | CustomizeMapping ignored in Projection |
| `PRAG0320` | `ConverterNotSupportedInProjection` | Warning | [MapConverter] not supported in Projection |
| `PRAG0321` | `FormatNotSupportedInProjection` | Info | Format not supported in Projection |
| `PRAG0322` | `ComplexDictionaryNotSupported` | Info | Complex dictionary not supported |
| `PRAG0323` | `AmbiguousMapping` | Warning | Ambiguous mapping |
| `PRAG0324` | `IdPropertyExcluded` | Info | ID property excluded from ToEntity |
| `PRAG0325` | `UnmappedSourceProperty` | Hidden | Source property not mapped to DTO |
| `PRAG0326` | `NestedProjectionMappingDropped` | Warning | Nested projection drops complex mapping |
| `PRAG0327` | `ProjectionDepthExceeded` | Warning | Nested projection truncated by MaxDepth |
| `PRAG0328` | `EnumMemberMissing` | Error | Enum member missing on target enum |
| `PRAG0329` | `ConditionMethodInvalid` | Error | [MapCondition] predicate missing or invalid |
| `PRAG0330` | `InvalidDerivedMapping` | Error | Invalid [MapDerived] pair |
| `PRAG0331` | `DerivedMappingIgnoredInProjection` | Info | [MapDerived] ignored in Projection |
| `PRAG0332` | `ConditionIgnoredInProjection` | Info | [MapCondition] ignored in Projection |
| `PRAG0333` | `CollectionElementsCannotBeMatched` | Error | This collection's elements cannot be matched |
| `PRAG0334` | `PropertyCrossesABoundary` | Error | This path crosses a boundary |
| `PRAG0335` | `LinkIdsNeedsTheTrackedForm` | Warning | [LinkIds] is written only through the tracked form |
| `PRAG0336` | `TargetPropertyIsReadOnly` | Info | Mapped property cannot be written: the entity computes it |
| `PRAG0338` | `UnusableGenericArgument` | Warning | Mapping attribute names an open type argument |
| `PRAG0340` | `PropertyDroppedFromProjection` | Info | Property omitted from Projection |
| `PRAG0341` | `NullableSourceDefaulted` | Info | Nullable source on a non-nullable property |

*Free inside the used span:* `PRAG0301`, `PRAG0308`, `PRAG0311`, `PRAG0312`, `PRAG0318`, `PRAG0337`, `PRAG0339`.

*First free after the last used:* `PRAG0342`.

### Actions — 67 in use · range `PRAG0400`–`PRAG0499`

| ID | Symbol | Severity | Title |
|---|---|---|---|
| `PRAG0400` | `Prag0400` | Error | Action class must be partial |
| `PRAG0401` | `MustInheritFromDomainAction` | Error | Action class must inherit from DomainAction base class |
| `PRAG0403` | `LogicalKeyCannotBeReturned` | Error | The mutation cannot return its logical key |
| `PRAG0404` | `LoadEntityIdPropertyNotFound` | Error | [LoadEntity] or [LoadEntities] key property not found |
| `PRAG0405` | `LoadEntityKeyTypeNotFound` | Error | [LoadEntity] or [LoadEntities] could not determine entity key type |
| `PRAG0406` | `Prag0406` | Error | [Boundary] class must be partial |
| `PRAG0407` | `BoundaryNoNamespace` | Error | [Boundary] class must be in a namespace |
| `PRAG0408` | `ActionMustBeTopLevel` | Error | Action must be declared at namespace level |
| `PRAG0409` | `MutationMustInheritFromBase` | Error | Mutation class must inherit from Mutation<TEntity> |
| `PRAG0410` | `MutationModeNotDetermined` | Error | Mutation mode could not be determined |
| `PRAG0411` | `LoadEntityKeyTypeMismatch` | Error | [LoadEntity] or [LoadEntities] key is not of the entity's key type |
| `PRAG0412` | `SubBoundaryNestingTooDeep` | Warning | SubBoundary nesting deeper than 2 levels |
| `PRAG0413` | `SubBoundaryInferred` | Info | SubBoundary inferred from namespace |
| `PRAG0414` | `MutationPropertyNoMatchingSetter` | Warning | Mutation property has no matching setter on entity |
| `PRAG0415` | `PolicyTypeUnresolved` | Warning | Policy type could not be resolved |
| `PRAG0416` | `SubBoundaryNameCannotBeAGroup` | Error | [SubBoundary] names no group |
| `PRAG0418` | `PermissionConstNotResolved` | Warning | Permission constant could not be resolved |
| `PRAG0419` | `DependencyTypeAmbiguous` | Warning | Cannot decide whether the field is an injected dependency |
| `PRAG0420` | `ResiliencePolicyNameEmpty` | Warning | Resilience policy name is empty |
| `PRAG0421` | `ExplicitPermissionNotResolved` | Warning | Explicit permission could not be resolved |
| `PRAG0422` | `EmptyPermissionRequirement` | Error | Permission requirement declares no permissions |
| `PRAG0423` | `DelegationSubjectNotFound` | Error | Delegation subject property not found |
| `PRAG0424` | `UnrecordedPartialWriteRisk` | Warning | Work crosses a boundary inside one transaction |
| `PRAG0425` | `CompensatorDoesNotMatch` | Error | Compensator does not compensate this action |
| `PRAG0426` | `TransactionCrossesBoundary` | Error | A transactional action calls another boundary |
| `PRAG0427` | `CompositeHasNoSteps` | Warning | Composite action has no steps |
| `PRAG0428` | `UndeclaredCompositionStrategy` | Warning | Composition does not say how its steps commit |
| `PRAG0429` | `CompensationIsNotDurable` | Info | Compensation across a boundary is a saga without a log |
| `PRAG0430` | `PerStepOnComposite` | Warning | A composite cannot commit per step |
| `PRAG0431` | `TransactionalOnBoundary` | Warning | A boundary cannot declare a transaction |
| `PRAG0432` | `CommitDeclarationWithoutBoundary` | Error | The commit declaration has no unit of work to govern |
| `PRAG0433` | `EventParameterHasNoSource` | Warning | An event parameter has nothing to bind to |
| `PRAG0434` | `MutationAssignsStateMachineProperty` | Warning | Mutation assigns a state-machine property instead of transitioning |
| `PRAG0435` | `MutationIdPropertyUnusable` | Error | Mutation cannot address the row it operates on |
| `PRAG0436` | `MutationChildIsNotPartOfTheAggregate` | Error | Mutation writes an entity that is not part of its aggregate |
| `PRAG0437` | `MutationChildElementsCannotBeMatched` | Error | Mutation child elements cannot be matched |
| `PRAG0438` | `MutationTargetsAChildOfAnAggregate` | Error | Mutation targets an entity declared part of another aggregate |
| `PRAG0439` | `MutationChildHasNoNavigation` | Error | Mutation carries children the entity cannot hold |
| `PRAG0440` | `CompositeExposedWithoutPermission` | Error | Exposed composite action declares no permission |
| `PRAG0441` | `Prag0441` | Warning | Boundary actions facade injected outside a trusted caller |
| `PRAG0442` | `MutationCarriesADtoChild` | Error | A mutation writes its children through mutations, not DTOs |
| `PRAG0443` | `MutationChildDoesNotMatchTheNavigation` | Error | The child writes an entity the navigation does not hold |
| `PRAG0444` | `MutationChildCrossesABoundary` | Error | A mutation writes a child of another boundary |
| `PRAG0445` | `MutationRetargetNeedsMapping` | Warning | [MapProperty] on a mutation needs Pragmatic.Mapping |
| `PRAG0446` | `MutationMissesAConstructorArgument` | Error | The entity's constructor needs a value the mutation does not carry |
| `PRAG0447` | `CompositeWithoutBoundary` | Warning | The composite belongs to no boundary |
| `PRAG0448` | `KeyedServiceWithoutBoundary` | Warning | The keyed service belongs to no boundary |
| `PRAG0449` | `PackageNeedsABoundaryAtTheImport` | Error | The imported package needs a boundary |
| `PRAG0450` | `PackageImportsNameDifferentBoundaries` | Error | Two package imports name different boundaries |
| `PRAG0451` | `CurrentUserCannotBeLoaded` | Error | [LoadCurrentUser] cannot be generated |
| `PRAG0452` | `LoadedValidationMisshapen` | Error | ValidateLoaded has a shape the invoker does not call |
| `PRAG0453` | `LoadEntityIncludeNotANavigation` | Error | An include path names no navigation |
| `PRAG0454` | `LoadSpecificationNotFound` | Error | [LoadEntity] or [LoadEntities] Specification names no specification of the entity |
| `PRAG0455` | `LoadSpecificationParameterUnbound` | Error | A parameter of the load's specification binds no property |
| `PRAG0456` | `LoadEntityKeyOrSpecification` | Error | A load names a key or a specification, not both |
| `PRAG0457` | `LoadReadPermissionUnknown` | Error | RequireReadPermission on an entity with no known read permission |
| `PRAG0458` | `LoadFromNotTheQuerysAnswer` | Error | [LoadFrom] property cannot hold the query's result |
| `PRAG0459` | `LoadFromInputUnbound` | Error | An input of the [LoadFrom] query binds no property |
| `PRAG0460` | `LoadByNotTheLogicKey` | Error | [LoadEntity] By names no logic key of the entity |
| `PRAG0461` | `LoadByKeyTypeMismatch` | Error | [LoadEntity] By key property is not of the logic key's type |
| `PRAG0462` | `RequireExistsBesideALoad` | Warning | [RequireExists] beside a [LoadEntity] of the same key |
| `PRAG0463` | `InvariantCannotBeCalled` | Error | The invariant cannot be called, so the rule never fires |
| `PRAG0464` | `ResilienceAttributeNothingReads` | Warning | Nothing reads this resilience attribute on this kind of class |
| `PRAG0465` | `TransitionHasNoEntity` | Error | [TransitionsTo] names no entity the invoker can move |
| `PRAG0466` | `BodyAlsoTransitions` | Error | The body transitions to the state the invoker already moves the entity to |
| `PRAG0467` | `AfterBodyOnAnAction` | Error | A domain action cannot transition after its body |
| `PRAG0468` | `TransitionOnAMutationThatIsNotAnUpdate` | Error | [TransitionsTo] needs an Update mutation |

*Free inside the used span:* `PRAG0402`, `PRAG0417`.

*First free after the last used:* `PRAG0469`.

### Endpoints — 36 in use · range `PRAG0500`–`PRAG0599`

| ID | Symbol | Severity | Title |
|---|---|---|---|
| `PRAG0500` | `Prag0500` | Error | Endpoint class must be partial |
| `PRAG0501` | `MustInheritFromEndpoint` | Error | Endpoint class must inherit from Endpoint base class |
| `PRAG0502` | `RouteRequired` | Error | Route is required |
| `PRAG0503` | `TooManyErrorTypes` | Error | Too many error types |
| `PRAG0504` | `RouteParameterNotFound` | Warning | Route parameter not found |
| `PRAG0505` | `DuplicateEndpointName` | Error | Duplicate endpoint name |
| `PRAG0507` | `GroupNotFound` | Error | Endpoint group not found |
| `PRAG0512` | `ImplicitBodyProperty` | Info | Property implicitly binds to request body |
| `PRAG0513` | `IdempotentOnSafeVerb` | Warning | [Idempotent] on a safe HTTP verb |
| `PRAG0514` | `HeadEndpointHasResponseBody` | Warning | HEAD endpoint declares a response type |
| `PRAG0515` | `AutocompleteMissingKey` | Error | Entity has no key property for autocomplete |
| `PRAG0516` | `InvalidMaxFileSize` | Error | Invalid [MaxFileSize] limit |
| `PRAG0517` | `InvalidMaxBodySize` | Error | Invalid [MaxBodySize] limit |
| `PRAG0518` | `InvalidExampleJson` | Warning | Invalid example JSON |
| `PRAG0520` | `ResponseCacheOnStreaming` | Error | [ResponseCache] on a streaming endpoint |
| `PRAG0521` | `StreamingVerbInvalid` | Error | Streaming endpoint verb must be GET or POST |
| `PRAG0522` | `StatusOverrideOnStreaming` | Error | Status override on a streaming endpoint |
| `PRAG0523` | `VersioningOnStreaming` | Warning | Versioning on a streaming endpoint |
| `PRAG0524` | `PostProcessorOnStreaming` | Error | [PostProcessor] on a streaming endpoint |
| `PRAG0525` | `EndpointOnAMemberNothingDerives` | Warning | [Endpoint] on a member that derives no query |
| `PRAG0526` | `ApiRouteNameCollision` | Warning | ApiRoutes member name collision |
| `PRAG0527` | `DependencyTypeAmbiguous` | Warning | Cannot decide whether the field is an injected dependency |
| `PRAG0528` | `PermissionConstNotResolved` | Warning | Permission constant could not be resolved |
| `PRAG0529` | `DuplicateRoute` | Error | Two endpoints answer the same verb and route |
| `PRAG0531` | `ReturnsDtoCannotMapFromEntity` | Error | This DTO cannot be built from the entity |
| `PRAG0532` | `GetCannotCarryComplexProperty` | Error | A GET cannot carry this property |
| `PRAG0533` | `CreateCannotReturnANavigatedDto` | Error | A create cannot answer with a DTO that reads through a navigation |
| `PRAG0534` | `ProcessorNotConstructible` | Error | Processor cannot be constructed by the container |
| `PRAG0535` | `ReturnsDtoBesideAKeyReturnType` | Error | The ReturnType answers, not the declared DTO |
| `PRAG0536` | `OptionalInitValueWithoutConstantDefault` | Error | An optional init property needs a constant default |
| `PRAG0537` | `DeclaredStatusContradictsTheError` | Warning | An error documents one status and answers with another |
| `PRAG0538` | `MemberIsStrippedFromTheWire` | Info | A published member never reaches the wire |
| `PRAG0550` | `AutocompleteRequiresStringProperty` | Error | [Autocomplete] requires a string property |
| `PRAG0551` | `VersioningRequiresAspVersioning` | Warning | Versioned methods require Asp.Versioning.Http |
| `PRAG0552` | `MultipartCannotCarryComplexProperty` | Error | A multipart request cannot carry this property |
| `PRAG0554` | `SharedResponseCacheOnAuthenticatedEndpoint` | Warning | A shared [ResponseCache] on an endpoint that requires authentication keeps nothing |

*Free inside the used span:* `PRAG0506`, `PRAG0508`, `PRAG0509`, `PRAG0510`, `PRAG0511`, `PRAG0519`, `PRAG0530`, `PRAG0539`, `PRAG0540`, `PRAG0541`, `PRAG0542`, `PRAG0543`, `PRAG0544`, `PRAG0545`, `PRAG0546`, `PRAG0547`, `PRAG0548`, `PRAG0549`, `PRAG0553`.

*First free after the last used:* `PRAG0555`.

### Persistence.EFCore — 44 in use · range `PRAG0600`–`PRAG0699`

| ID | Symbol | Severity | Title |
|---|---|---|---|
| `PRAG0600` | `Prag0600` | Error | Type must be partial |
| `PRAG0602` | `Prag0602` | Error | Database context must be partial |
| `PRAG0610` | `InverseContradictsTheDeclaredName` | Error | The two ends name the same navigation differently |
| `PRAG0611` | `DeleteBehaviourNotOwned` | Error | The delete behaviour belongs on the side that owns the relationship |
| `PRAG0612` | `AmbiguousRelation` | Warning | Ambiguous relation |
| `PRAG0613` | `InversePropertyNotFound` | Error | Inverse navigation not found |
| `PRAG0614` | `InversePropertyTypeMismatch` | Error | Inverse navigation has the wrong type |
| `PRAG0615` | `DuplicateNavigationName` | Error | Duplicate navigation name |
| `PRAG0616` | `JoinEntityKeysNotNamed` | Error | Join entity foreign keys are not named |
| `PRAG0617` | `InverseNameNotDeclared` | Error | The inverse navigation needs a name |
| `PRAG0618` | `OneToOnePrincipalNotDecided` | Error | A one-to-one needs exactly one principal |
| `PRAG0619` | `RelationWrittenByHand` | Error | A relation is declared, not written |
| `PRAG0620` | `StateMachineMissingInitialState` | Error | State machine has no initial state |
| `PRAG0621` | `StateMachineUnreachableState` | Warning | Unreachable state in state machine |
| `PRAG0622` | `StateMachineInvalidTransitionSource` | Error | Invalid transition source in state machine |
| `PRAG0623` | `StateMachinePropertyNotFound` | Error | State machine property does not exist on the entity |
| `PRAG0624` | `PartialTraitProperties` | Error | A trait's properties are only partly declared |
| `PRAG0625` | `MixedLogicKeyScope` | Error | The parts of a domain key disagree about its scope |
| `PRAG0626` | `TenantEntityWithManualId` | Warning | A tenant-scoped entity assigns its own primary key |
| `PRAG0627` | `UniqueIndexPropertyNotFound` | Error | A unique index names a property that does not exist |
| `PRAG0628` | `MultipleModulesInOneAssembly` | Error | An assembly declares more than one module |
| `PRAG0629` | `EntityClaimedByNoBoundary` | Error | No boundary owns this entity |
| `PRAG0630` | `EntityClaimedByTwoBoundaries` | Error | Two boundaries own the same entity |
| `PRAG0631` | `PartOfWithoutRelation` | Error | [PartOf] needs the relation it leans on |
| `PRAG0632` | `PartOfViaRequired` | Error | [PartOf] needs Via |
| `PRAG0633` | `PartOfViaNotFound` | Error | [PartOf] Via does not resolve |
| `PRAG0634` | `PartOfEdgeDoesNotCascade` | Error | A part dies with its whole |
| `PRAG0635` | `EfCoreRelationAttribute` | Error | EF Core relational attributes do not declare a relation |
| `PRAG0636` | `LogicKeyPartNotFound` | Error | A domain key names a part that does not exist |
| `PRAG0637` | `LogicKeyDeclaredTwice` | Error | The domain key is declared in two places |
| `PRAG0638` | `StateMachineMultipleInitialStates` | Error | State machine has more than one initial state |
| `PRAG0639` | `ReadNavigationTargetIsNotRead` | Warning | The read entity navigates to a type this boundary does not have |
| `PRAG0651` | `PropertyTypeMayNeedConverter` | Warning | Property may need value converter |
| `PRAG0652` | `ProtectedValueNeedsCryptographyEFCore` | Error | A protected value needs Pragmatic.Cryptography.EFCore |
| `PRAG0680` | `PreferEntityCreate` | Warning | Use Entity.Create() factory instead of new |
| `PRAG0681` | `DoNotDefaultEntity` | Warning | Do not use default for entity types |
| `PRAG0682` | `DoNotReflectEntity` | Warning | Do not use Activator.CreateInstance for entity types |
| `PRAG0683` | `EntityShouldHaveNoBehavior` | Warning | Entity should not declare behavior methods |
| `PRAG0684` | `RawSqlInjection` | Warning | Avoid non-constant SQL in FromSqlRaw/ExecuteSqlRaw |
| `PRAG0685` | `AggregateTooLarge` | Info | Aggregate has many child collections — consider splitting |
| `PRAG0686` | `CrossBoundaryDbContext` | Warning | Cross-boundary DbContext access (reach-in) |
| `PRAG0687` | `BulkDeleteOnSoftDeleteEntity` | Warning | Bulk delete permanently removes a soft-delete entity |
| `PRAG0688` | `BulkUpdateSkipsInterceptors` | Warning | Bulk update skips the interceptors the entity declares |
| `PRAG0690` | `InstantsAreNotNormalisedToUtc` | Warning | Stored instants are not normalised to UTC |

*Free inside the used span:* `PRAG0601`, `PRAG0603`, `PRAG0604`, `PRAG0605`, `PRAG0606`, `PRAG0607`, `PRAG0608`, `PRAG0609`, `PRAG0640`, `PRAG0641`, `PRAG0642`, `PRAG0643`, `PRAG0644`, `PRAG0645`, `PRAG0646`, `PRAG0647`, `PRAG0648`, `PRAG0649`, `PRAG0650`, `PRAG0653`, `PRAG0654`, `PRAG0655`, `PRAG0656`, `PRAG0657`, `PRAG0658`, `PRAG0659`, `PRAG0660`, `PRAG0661`, `PRAG0662`, `PRAG0663`, `PRAG0664`, `PRAG0665`, `PRAG0666`, `PRAG0667`, `PRAG0668`, `PRAG0669`, `PRAG0670`, `PRAG0671`, `PRAG0672`, `PRAG0673`, `PRAG0674`, `PRAG0675`, `PRAG0676`, `PRAG0677`, `PRAG0678`, `PRAG0679`, `PRAG0689`.

*First free after the last used:* `PRAG0691`.

### Persistence.Query — 42 in use · range `PRAG0700`–`PRAG0799`

| ID | Symbol | Severity | Title |
|---|---|---|---|
| `PRAG0701` | `BetweenOperatorNotSupported` | Error | FilterOperator.Between is not implemented |
| `PRAG0702` | `CascadeTargetWithoutForeignKey` | Error | [CascadeOn] target has no foreign key to the source |
| `PRAG0703` | `DeclaredOptionNotHonoured` | Warning | This option is declared and has no effect |
| `PRAG0704` | `ResultTypeHasNoProjection` | Error | The query's result type has no Projection |
| `PRAG0705` | `SoftDeleteRequiredNavigation` | Warning | Required navigation to a soft-deletable entity |
| `PRAG0706` | `ReadAccessCrossDatabase` | Warning | [ReadAccess] target lives on another database |
| `PRAG0707` | `QueryInputGeneratesNoFilter` | Error | Query input generates no filter |
| `PRAG0708` | `HierarchyHasNoParentKey` | Error | [GenerateHierarchy] finds no self-referencing relation |
| `PRAG0709` | `BindSpecificationWithoutSpecification` | Error | [BindSpecification] with no specification to feed |
| `PRAG0710` | `DtoNavigationWithoutInclude` | Warning | DTO references navigation without Include |
| `PRAG0711` | `DeepIncludeWithoutLoadWith` | Warning | Deep Include depth on loading profile |
| `PRAG0712` | `Prag0712` | Error | [Query] on a type that is not partial |
| `PRAG0713` | `HierarchyViaRequired` | Error | [GenerateHierarchy] needs Via |
| `PRAG0714` | `HierarchyViaNotFound` | Error | [GenerateHierarchy] Via does not resolve |
| `PRAG0715` | `HierarchyEdgeRequired` | Error | A hierarchy's edge must be optional |
| `PRAG0716` | `DtoWithManyNavigationLevels` | Warning | DTO with many navigation levels |
| `PRAG0717` | `WrongEntity` | Error | Visibility rule is for another entity |
| `PRAG0718` | `NotConstructible` | Error | Visibility rule cannot be constructed while the model is built |
| `PRAG0719` | `WithoutFilterNeedsPermission` | Error | Lifting a query filter needs a permission |
| `PRAG0720` | `DisableByTypeDoesNothing` | Error | Disabling a visibility rule by type has no effect |
| `PRAG0721` | `WriteNeedsWithoutFilter` | Warning | Writing the property a visibility rule keys on |
| `PRAG0722` | `CountClauseDoesNotNameTheRow` | Warning | Count clause does not name the row |
| `PRAG0723` | `GridRequestWithoutBridge` | Error | Grid request without a bridge |
| `PRAG0724` | `GridRequestPagesTwice` | Warning | Grid request pages twice |
| `PRAG0725` | `QueryPermissionDoesNotResolve` | Error | Query permission does not resolve |
| `PRAG0726` | `DerivedQueryNameCollides` | Error | Two specifications derive the same query type |
| `PRAG0727` | `PagingRequestIsRedundant` | Warning | Paged = true on a query that already pages |
| `PRAG0728` | `PublishedQueryMethodNameCollides` | Error | Two published queries contribute the same contract method name |
| `PRAG0729` | `SpecificationQueryDerivesNothing` | Warning | The [Query] on this member derives nothing |
| `PRAG0730` | `BoundPropertyIsSettable` | Error | A [FromCurrentUser] property must be set by the invoker alone |
| `PRAG0731` | `BindingCannotBeGenerated` | Error | The [FromCurrentUser] binding cannot be generated |
| `PRAG0732` | `GroupKeyDoesNotResolve` | Error | A group key names no member of the entity |
| `PRAG0733` | `ComputedFilterCannotBeGenerated` | Error | A [ComputedFilter] the generator cannot write |
| `PRAG0734` | `ClockBindingCannotBeGenerated` | Error | The [FromClock] binding cannot be generated |
| `PRAG0735` | `SpecificationReadsTheRow` | Error | A specification in a computed body takes a value from the row |
| `PRAG0736` | `EagerLoadPathNamesNoNavigation` | Error | [EagerLoad] path names no navigation |
| `PRAG0737` | `JoinPathNamesNoNavigation` | Error | [Join] Via names no navigation |
| `PRAG0738` | `KeyJoinNeedsAResultType` | Error | A key join needs a result type |
| `PRAG0739` | `JoinKeyNamesNoProperty` | Error | [Join] key names no property |
| `PRAG0740` | `JoinedResultPropertyHasNoSource` | Error | A joined result property has no source |
| `PRAG0741` | `JoinTypeCannotBeGenerated` | Error | This join type cannot be generated |
| `PRAG0742` | `JoinTargetIsOutsideTheBoundary` | Error | The joined entity is not in this boundary's model |

*First free after the last used:* `PRAG0743`.

### Messaging — 19 in use · range `PRAG0800`–`PRAG0899`

| ID | Symbol | Severity | Title |
|---|---|---|---|
| `PRAG0800` | `HandlerMustImplementInterface` | Error | Message handler must implement IMessageHandler<T> |
| `PRAG0801` | `Prag0801` | Error | Message handler must be partial |
| `PRAG0802` | `InvalidRetryConfig` | Error | Invalid retry configuration |
| `PRAG0803` | `MiddlewareMustImplementInterface` | Error | Message middleware must implement IMessageMiddleware |
| `PRAG0811` | `SagaOrphanedState` | Info | Saga state has no handler |
| `PRAG0813` | `SagaStateNotEnum` | Error | Saga state must be an enum |
| `PRAG0814` | `SagaMissingStart` | Error | Saga has no start handler |
| `PRAG0816` | `EventWithoutConsumers` | Warning | Event type has no consumers |
| `PRAG0819` | `MultiplePartitionKeys` | Warning | Multiple [PartitionKey] properties |
| `PRAG0820` | `SagaEventWithoutCorrelation` | Error | Saga event has no correlation |
| `PRAG0821` | `MultipleCorrelationKeys` | Warning | Multiple [CorrelationKey] properties |
| `PRAG0822` | `Prag0822` | Warning | Domain-event cascade cycle |
| `PRAG0831` | `EnableOutboxWithoutEFCore` | Warning | [EnableOutbox] requires Pragmatic.Messaging.EFCore |
| `PRAG0832` | `EnableSagaPersistenceWithoutEFCore` | Warning | [EnableSagaPersistence] requires Pragmatic.Messaging.EFCore |
| `PRAG0833` | `ConflictingOutboxAttributes` | Warning | Conflicting outbox attributes on one boundary |
| `PRAG0834` | `MultipleBatchProgressBoundaries` | Warning | [EnableBatchProgress] must mark exactly one boundary |
| `PRAG0835` | `EnableBatchProgressWithoutBatch` | Warning | [EnableBatchProgress] requires Pragmatic.Messaging.Batch |
| `PRAG0836` | `PublicEventOnNonDomainEvent` | Warning | [PublicEvent] marks a domain event, and this type is not one |
| `PRAG0837` | `EventHandlerOnOutboxBoundary` | Warning | An [EventHandler] of an [EnableOutbox] boundary never runs |

*Free inside the used span:* `PRAG0804`, `PRAG0805`, `PRAG0806`, `PRAG0807`, `PRAG0808`, `PRAG0809`, `PRAG0810`, `PRAG0812`, `PRAG0815`, `PRAG0817`, `PRAG0818`, `PRAG0823`, `PRAG0824`, `PRAG0825`, `PRAG0826`, `PRAG0827`, `PRAG0828`, `PRAG0829`, `PRAG0830`.

*First free after the last used:* `PRAG0838`.

### Temporal — 6 in use · range `PRAG0900`–`PRAG0999`

| ID | Symbol | Severity | Title |
|---|---|---|---|
| `PRAG0900` | `AvoidDateTimeNow` | Warning | Avoid DateTime.Now |
| `PRAG0901` | `AvoidDateTimeToday` | Warning | Avoid DateTime.Today |
| `PRAG0902` | `AvoidDateTimeWithoutKind` | Warning | DateTime created without DateTimeKind |
| `PRAG0903` | `AvoidDateTimeOffsetDirectComparison` | Warning | DateTimeOffset compared with relational operators |
| `PRAG0904` | `AvoidDateTimeNowInTests` | Info | DateTime.Now used in test code |
| `PRAG0905` | `UnsupportedPropertyType` | Warning | Timezone conversion attribute on unsupported property type |

*First free after the last used:* `PRAG0906`.

### Identity / Authorization — 16 in use · range `PRAG1000`–`PRAG1099`

| ID | Symbol | Severity | Title |
|---|---|---|---|
| `PRAG1001` | `DuplicatePermissionName` | Error | Duplicate permission name |
| `PRAG1003` | `EmptyRoleName` | Error | IRole.Name must be non-empty |
| `PRAG1004` | `PermissionOutsideTheBoundaries` | Error | A declared permission names no boundary of this assembly |
| `PRAG1005` | `PermissionConstantNameTaken` | Error | A declared permission's constant would take a name already in use |
| `PRAG1006` | `RoleMustBePartial` | Error | A [Role] class must be a top-level partial class |
| `PRAG1007` | `RoleInclusionCycle` | Error | Roles include each other |
| `PRAG1008` | `GrantedPermissionNotResolved` | Error | A granted permission does not resolve |
| `PRAG1009` | `IncludedRoleCannotBeRead` | Error | An included role's permissions cannot be read |
| `PRAG1010` | `RoleSeedingFileNotJson` | Error | roles.pragmatic.json is not valid JSON |
| `PRAG1011` | `RoleSeedingFileInvalid` | Error | roles.pragmatic.json declares something it cannot |
| `PRAG1012` | `RoleSeedingFileIgnored` | Error | Only one roles.pragmatic.json is read |
| `PRAG1013` | `RoleSpreadCannotBeRead` | Warning | A spread in a role's permissions cannot be read |
| `PRAG1014` | `SignInMemberWithoutRole` | Error | An access level signs in as no role |
| `PRAG1015` | `RoleListCannotBeRead` | Warning | A role's permissions live in a list this compilation cannot read |
| `PRAG1016` | `PermissionSetCannotBeRead` | Warning | A [PermissionSet] list cannot be read |
| `PRAG1050` | `DuplicateUsePackage` | Error | Duplicate UsePackage declaration |

*Free inside the used span:* `PRAG1002`, `PRAG1017`, `PRAG1018`, `PRAG1019`, `PRAG1020`, `PRAG1021`, `PRAG1022`, `PRAG1023`, `PRAG1024`, `PRAG1025`, `PRAG1026`, `PRAG1027`, `PRAG1028`, `PRAG1029`, `PRAG1030`, `PRAG1031`, `PRAG1032`, `PRAG1033`, `PRAG1034`, `PRAG1035`, `PRAG1036`, `PRAG1037`, `PRAG1038`, `PRAG1039`, `PRAG1040`, `PRAG1041`, `PRAG1042`, `PRAG1043`, `PRAG1044`, `PRAG1045`, `PRAG1046`, `PRAG1047`, `PRAG1048`, `PRAG1049`.

*First free after the last used:* `PRAG1051`.

### Persistence.Ownership — 2 in use · range `PRAG1100`–`PRAG1199`

| ID | Symbol | Severity | Title |
|---|---|---|---|
| `PRAG1100` | `Prag1100` | Error | [HasOwner] requires partial class |
| `PRAG1104` | `OwnedEntityManualOwnerId` | Info | OwnerId manually declared |

*Free inside the used span:* `PRAG1101`, `PRAG1102`, `PRAG1103`.

*First free after the last used:* `PRAG1105`.

### DependencyInjection — 3 in use · range `PRAG1400`–`PRAG1499`

| ID | Symbol | Severity | Title |
|---|---|---|---|
| `PRAG1450` | `CaptiveDependency` | Warning | Captive dependency — scoped service injected into a singleton-lifetime type |
| `PRAG1451` | `BuildServiceProvider` | Warning | Avoid BuildServiceProvider() — it creates a second container |
| `PRAG1452` | `OptionalInjection` | Warning | Injection is optional — a missing service is injected as null |

*First free after the last used:* `PRAG1453`.

### Composition — 44 in use · range `PRAG1600`–`PRAG1699`

| ID | Symbol | Severity | Title |
|---|---|---|---|
| `PRAG1601` | `ModuleDependencyNotDeclared` | Error | Module dependency not declared |
| `PRAG1602` | `CircularDependency` | Error | Circular dependency detected |
| `PRAG1603` | `HostedModuleDependencyNotHosted` | Error | A hosted module depends on a module the host does not host |
| `PRAG1607` | `DuplicateModuleName` | Warning | Duplicate module name |
| `PRAG1608` | `IncludeModuleNotDiscovered` | Warning | Included module not discovered |
| `PRAG1609` | `DatabaseConfigKeyMissing` | Warning | Database connection config key missing |
| `PRAG1610` | `IncompatibleSchemaVersion` | Error | Incompatible metadata schema version |
| `PRAG1611` | `NewerSchemaVersion` | Warning | Newer metadata schema version |
| `PRAG1612` | `LegacySchemaVersion` | Info | Legacy metadata schema version |
| `PRAG1613` | `ModuleManifestUnreadable` | Warning | Module manifest is not valid JSON |
| `PRAG1614` | `ServiceTypeArgumentNotYetGenerated` | Warning | Service type argument is not resolvable yet |
| `PRAG1630` | `StartupMustImplementInterface` | Error | [StartupStep] must implement IStartupStep |
| `PRAG1631` | `StartupMustBeClass` | Error | [StartupStep] must be on a class |
| `PRAG1632` | `NeedsStepTypeNotFound` | Error | [NeedsStep<T>] references unavailable step type |
| `PRAG1640` | `ServiceRequiresClass` | Error | Service attribute requires class |
| `PRAG1641` | `DependencyNotRegistered` | Warning | Dependency not registered |
| `PRAG1642` | `LifetimeMismatch` | Warning | Lifetime mismatch |
| `PRAG1643` | `NoInterfaceFound` | Warning | No interface found |
| `PRAG1645` | `AbstractClassCannotBeService` | Error | Abstract class cannot be service |
| `PRAG1646` | `KeyedServicesRequireNet8` | Warning | Keyed services require .NET 8+ |
| `PRAG1647` | `InjectOnOpenGenericUnsupported` | Warning | [Inject] is not supported on an open-generic service |
| `PRAG1651` | `BoundaryWithoutDatabase` | Warning | Boundary has no database configured |
| `PRAG1652` | `DbContextNameCollision` | Error | DbContext name collision across different databases |
| `PRAG1660` | `DecoratorMustImplementInterface` | Error | Decorator must implement interface |
| `PRAG1661` | `DecoratorMissingInnerService` | Error | Decorator missing inner service |
| `PRAG1670` | `EventHandlerMissingInterface` | Error | EventHandler missing IDomainEventHandler<T> |
| `PRAG1680` | `ExposeEndpointNotFromPackage` | Warning | ExposeEndpoint references a non-package action |
| `PRAG1681` | `ExposedEndpointInputCannotBeBound` | Error | An exposed endpoint's input cannot come from the query string |
| `PRAG1682` | `ExposedEndpointGroupNotFound` | Error | An exposed endpoint's group cannot be mapped |
| `PRAG1685` | `RemoteBoundaryOverlapsInclude` | Error | RemoteBoundary overlaps with Include |
| `PRAG1686` | `RemoteBoundaryNoActions` | Warning | RemoteBoundary module has no actions |
| `PRAG1687` | `RemoteBoundaryNoBaseUrl` | Info | RemoteBoundary base URL not configured |
| `PRAG1688` | `ConfigKeyMissing` | Warning | Required configuration key missing from appsettings.json |
| `PRAG1689` | `RemoteBoundaryCompensationUnreachable` | Warning | Compensation cannot run across a remote boundary |
| `PRAG1690` | `DiscoveredModules` | Info | Discovered Pragmatic modules |
| `PRAG1691` | `DiscoveredModulesInfo` | Info | Module composition info |
| `PRAG1692` | `AnonymousHostCannotHoldADerivedPermission` | Warning | An anonymous host has a route nobody can call |
| `PRAG1693` | `NoServicesDiscovered` | Info | No Pragmatic services discovered |
| `PRAG1694` | `NoPipelineStepsDiscovered` | Info | No startup steps discovered |
| `PRAG1695` | `AuthorizationWithoutIdentity` | Error | Authorization referenced without Identity |
| `PRAG1696` | `IdentityPersistenceWithoutAuthorization` | Warning | Identity.Persistence referenced without Authorization |
| `PRAG1697` | `DuplicateEndpointRoute` | Error | Two operations are published at the same address |
| `PRAG1698` | `ClockBindingWithoutAClock` | Warning | An operation takes the clock and this host registers none |
| `PRAG1699` | `UncalledMessagingRegistration` | Warning | A referenced assembly's message handlers are registered by nobody |

*Free inside the used span:* `PRAG1604`, `PRAG1605`, `PRAG1606`, `PRAG1615`, `PRAG1616`, `PRAG1617`, `PRAG1618`, `PRAG1619`, `PRAG1620`, `PRAG1621`, `PRAG1622`, `PRAG1623`, `PRAG1624`, `PRAG1625`, `PRAG1626`, `PRAG1627`, `PRAG1628`, `PRAG1629`, `PRAG1633`, `PRAG1634`, `PRAG1635`, `PRAG1636`, `PRAG1637`, `PRAG1638`, `PRAG1639`, `PRAG1644`, `PRAG1648`, `PRAG1649`, `PRAG1650`, `PRAG1653`, `PRAG1654`, `PRAG1655`, `PRAG1656`, `PRAG1657`, `PRAG1658`, `PRAG1659`, `PRAG1662`, `PRAG1663`, `PRAG1664`, `PRAG1665`, `PRAG1666`, `PRAG1667`, `PRAG1668`, `PRAG1669`, `PRAG1671`, `PRAG1672`, `PRAG1673`, `PRAG1674`, `PRAG1675`, `PRAG1676`, `PRAG1677`, `PRAG1678`, `PRAG1679`, `PRAG1683`, `PRAG1684`.

*Range exhausted.*

### Caching — 8 in use · range `PRAG1700`–`PRAG1799`

| ID | Symbol | Severity | Title |
|---|---|---|---|
| `PRAG1700` | `Prag1700` | Error | Type must be partial |
| `PRAG1701` | `InvalidDuration` | Warning | Invalid cache duration |
| `PRAG1702` | `NoKeyProperties` | Error | No cache key properties |
| `PRAG1703` | `InvalidPlaceholder` | Error | Invalid placeholder |
| `PRAG1704` | `InvalidatesMustBePartial` | Error | Type must be partial |
| `PRAG1705` | `KeyPropertyNotFullyRead` | Warning | Cache key cannot read a complex property to the bottom |
| `PRAG1750` | `DuplicateOrder` | Warning | Duplicate cache key order |
| `PRAG1751` | `AllPropertiesExcluded` | Warning | All properties excluded from cache key |

*Free inside the used span:* `PRAG1706`, `PRAG1707`, `PRAG1708`, `PRAG1709`, `PRAG1710`, `PRAG1711`, `PRAG1712`, `PRAG1713`, `PRAG1714`, `PRAG1715`, `PRAG1716`, `PRAG1717`, `PRAG1718`, `PRAG1719`, `PRAG1720`, `PRAG1721`, `PRAG1722`, `PRAG1723`, `PRAG1724`, `PRAG1725`, `PRAG1726`, `PRAG1727`, `PRAG1728`, `PRAG1729`, `PRAG1730`, `PRAG1731`, `PRAG1732`, `PRAG1733`, `PRAG1734`, `PRAG1735`, `PRAG1736`, `PRAG1737`, `PRAG1738`, `PRAG1739`, `PRAG1740`, `PRAG1741`, `PRAG1742`, `PRAG1743`, `PRAG1744`, `PRAG1745`, `PRAG1746`, `PRAG1747`, `PRAG1748`, `PRAG1749`.

*First free after the last used:* `PRAG1752`.

### Internationalization — 6 in use · range `PRAG1800`–`PRAG1899`

| ID | Symbol | Severity | Title |
|---|---|---|---|
| `PRAG1800` | `InvalidTranslationFile` | Error | Invalid translation file |
| `PRAG1801` | `DuplicateTranslationKey` | Warning | Duplicate translation key |
| `PRAG1802` | `MissingTranslationKey` | Warning | Missing translation key |
| `PRAG1803` | `EmptyTranslationFile` | Info | Empty translation file |
| `PRAG1804` | `MessageKeyNotTranslated` | Info | MessageKey has no translation |
| `PRAG1805` | `KeyIsAlsoAGroup` | Error | Translation key is also a group |

*First free after the last used:* `PRAG1806`.

### Documents — 1 in use · range `PRAG1900`–`PRAG1999`

| ID | Symbol | Severity | Title |
|---|---|---|---|
| `PRAG1900` | `UnsupportedPropertyType` | Warning | CSV property type is not round-trippable |

*First free after the last used:* `PRAG1901`.

### Configuration — 4 in use · range `PRAG2000`–`PRAG2099`

| ID | Symbol | Severity | Title |
|---|---|---|---|
| `PRAG2000` | `Prag2000` | Error | Configuration class must be partial |
| `PRAG2001` | `MustNotBeStaticOrAbstract` | Error | Configuration class cannot be static or abstract |
| `PRAG2002` | `InvariantMustBeCallable` | Error | [ConfigInvariant] method cannot be called |
| `PRAG2050` | `RequiredWithDefault` | Warning | Required property has default value |

*Free inside the used span:* `PRAG2003`, `PRAG2004`, `PRAG2005`, `PRAG2006`, `PRAG2007`, `PRAG2008`, `PRAG2009`, `PRAG2010`, `PRAG2011`, `PRAG2012`, `PRAG2013`, `PRAG2014`, `PRAG2015`, `PRAG2016`, `PRAG2017`, `PRAG2018`, `PRAG2019`, `PRAG2020`, `PRAG2021`, `PRAG2022`, `PRAG2023`, `PRAG2024`, `PRAG2025`, `PRAG2026`, `PRAG2027`, `PRAG2028`, `PRAG2029`, `PRAG2030`, `PRAG2031`, `PRAG2032`, `PRAG2033`, `PRAG2034`, `PRAG2035`, `PRAG2036`, `PRAG2037`, `PRAG2038`, `PRAG2039`, `PRAG2040`, `PRAG2041`, `PRAG2042`, `PRAG2043`, `PRAG2044`, `PRAG2045`, `PRAG2046`, `PRAG2047`, `PRAG2048`, `PRAG2049`.

*First free after the last used:* `PRAG2051`.

### Notifications — 1 in use · range `PRAG2100`–`PRAG2149`

| ID | Symbol | Severity | Title |
|---|---|---|---|
| `PRAG2100` | `StoresNotificationsWithoutEFCore` | Warning | [StoresNotifications] requires Pragmatic.Notifications.EFCore |

*First free after the last used:* `PRAG2101`.

### Patch — 7 in use · range `PRAG2200`–`PRAG2249`

| ID | Symbol | Severity | Title |
|---|---|---|---|
| `PRAG2200` | `Prag2200` | Error | Patch type must be partial |
| `PRAG2201` | `EntityTypeNotFound` | Error | Entity type not found |
| `PRAG2202` | `NoProperties` | Warning | No patchable properties |
| `PRAG2203` | `CollectionElementsCannotBeMatched` | Error | Patch collection elements cannot be matched |
| `PRAG2204` | `RelatedDtoCannotWrite` | Warning | Patch child DTO cannot write its entity |
| `PRAG2205` | `PatchOnlyElementCannotBeAdded` | Info | Patch collection updates matched elements only |
| `PRAG2206` | `PatchIgnoreNameNotFound` | Warning | [PatchIgnore] names a property that does not exist |

*First free after the last used:* `PRAG2207`.

### Client — 5 in use · range `PRAG2300`–`PRAG2349`

| ID | Symbol | Severity | Title |
|---|---|---|---|
| `PRAG2300` | `ManifestUnreadable` | Error | API manifest could not be read |
| `PRAG2301` | `UnresolvedType` | Warning | Client type degraded to object |
| `PRAG2302` | `BoundaryFilterMatchedNothing` | Warning | Client boundary filter matched no endpoint |
| `PRAG2303` | `ResponseTypeMissing` | Warning | Endpoint response type missing from the manifest |
| `PRAG2304` | `SharedTypeShapeConflict` | Warning | Shared client type described differently by two manifests |

*First free after the last used:* `PRAG2305`.

### Testing — 6 in use · range `PRAG2350`–`PRAG2399`

| ID | Symbol | Severity | Title |
|---|---|---|---|
| `PRAG2350` | `NotAnInterface` | Warning | [GenerateMock<T>] requires an interface or a derivable class |
| `PRAG2352` | `GenericMember` | Info | Generic method not configurable in generated mock |
| `PRAG2353` | `DuplicateDeclaration` | Warning | Duplicate [GenerateMock<T>] declaration |
| `PRAG2360` | `NoComparableMembers` | Warning | [GenerateComparer<T>] needs readable public members |
| `PRAG2361` | `DuplicateDeclaration` | Warning | Duplicate [GenerateComparer<T>] declaration |
| `PRAG2363` | `TransitionHalfUnreachable` | Info | A state-transition contract has no walk to its source state |

*Free inside the used span:* `PRAG2351`, `PRAG2354`, `PRAG2355`, `PRAG2356`, `PRAG2357`, `PRAG2358`, `PRAG2359`, `PRAG2362`.

*First free after the last used:* `PRAG2364`.

### Jobs — 10 in use · range `PRAG2500`–`PRAG2549`

| ID | Symbol | Severity | Title |
|---|---|---|---|
| `PRAG2500` | `MustImplementInterface` | Error | Job class must implement IJob or IJob<T> |
| `PRAG2501` | `InvalidCronExpression` | Error | Invalid cron expression |
| `PRAG2502` | `Prag2502` | Error | Job class must be partial |
| `PRAG2503` | `DuplicateRecurringJobId` | Error | Duplicate recurring job ID |
| `PRAG2504` | `InvalidRetryMaxAttempts` | Error | Invalid retry configuration |
| `PRAG2505` | `ContinuationMustBeJob` | Error | Continuation must be a job |
| `PRAG2506` | `ContinuationCycleDetected` | Error | Continuation cycle detected |
| `PRAG2507` | `RecurringScheduleNeedsAnId` | Error | A second schedule has to name itself |
| `PRAG2508` | `JobPersistenceWithoutEfCore` | Warning | [EnableJobPersistence] requires Pragmatic.Jobs.EFCore |
| `PRAG2509` | `JobPersistenceOnMoreThanOneBoundary` | Error | [EnableJobPersistence] must mark exactly one boundary |

*First free after the last used:* `PRAG2510`.

### Traits + Resource — 14 in use · range `PRAG2600`–`PRAG2699`

| ID | Symbol | Severity | Title |
|---|---|---|---|
| `PRAG2600` | `TraitRequiresEntity` | Error | Trait requires [Entity] |
| `PRAG2601` | `TraitRequiresResource` | Warning | Trait endpoint requires [Resource] |
| `PRAG2602` | `SegmentNotKebabCase` | Error | Resource segment must be kebab-case |
| `PRAG2603` | `DuplicateSegment` | Error | Duplicate resource segment |
| `PRAG2605` | `ReadRecommended` | Info | Consider adding Read capability |
| `PRAG2606` | `TraitRequiresClock` | Warning | Trait actions need a clock |
| `PRAG2607` | `OverrideMatchesNoOperation` | Warning | This declaration decorates no scaffolded operation |
| `PRAG2608` | `DeclaredDtoHasNoProjection` | Error | This DTO cannot be projected |
| `PRAG2609` | `DeclaredDtoMapsFromAnotherEntity` | Error | This DTO maps from another entity |
| `PRAG2610` | `CapabilityCannotApply` | Warning | This capability cannot apply to this entity |
| `PRAG2611` | `ResourceOnAggregatePart` | Error | This entity is part of another aggregate and cannot be a resource |
| `PRAG2612` | `NoCapabilities` | Warning | Resource scaffolds nothing |
| `PRAG2650` | `TraitNavigationCollides` | Error | Trait navigation name already used |
| `PRAG2651` | `TraitThumbnailRequiresImaging` | Warning | Thumbnails need Pragmatic.Imaging |

*Free inside the used span:* `PRAG2604`, `PRAG2613`, `PRAG2614`, `PRAG2615`, `PRAG2616`, `PRAG2617`, `PRAG2618`, `PRAG2619`, `PRAG2620`, `PRAG2621`, `PRAG2622`, `PRAG2623`, `PRAG2624`, `PRAG2625`, `PRAG2626`, `PRAG2627`, `PRAG2628`, `PRAG2629`, `PRAG2630`, `PRAG2631`, `PRAG2632`, `PRAG2633`, `PRAG2634`, `PRAG2635`, `PRAG2636`, `PRAG2637`, `PRAG2638`, `PRAG2639`, `PRAG2640`, `PRAG2641`, `PRAG2642`, `PRAG2643`, `PRAG2644`, `PRAG2645`, `PRAG2646`, `PRAG2647`, `PRAG2648`, `PRAG2649`.

*First free after the last used:* `PRAG2652`.

### ValueObject — 2 in use · range `PRAG2700`–`PRAG2749`

| ID | Symbol | Severity | Title |
|---|---|---|---|
| `PRAG2700` | `NotPartial` | Warning | [ValueObject] type must be partial |
| `PRAG2701` | `MissingValidate` | Warning | [ValueObject] type missing Validate method |

*First free after the last used:* `PRAG2702`.

### Lifecycle — 4 in use · range `PRAG2750`–`PRAG2799`

| ID | Symbol | Severity | Title |
|---|---|---|---|
| `PRAG2750` | `MustBeDomainEventSource` | Error | Entity with [Raises<T>] must derive from DomainEventSource |
| `PRAG2751` | `UnmatchedConstructorParameter` | Warning | Lifecycle event constructor parameter is unmatched |
| `PRAG2752` | `EnableEventOutboxWithoutEFCore` | Warning | [EnableEventOutbox] requires Pragmatic.Events.EFCore |
| `PRAG2753` | `RaisesOnAnEntityMethodGeneratesNothing` | Error | [Raises<T>] on an entity's method generates nothing |

*First free after the last used:* `PRAG2754`.

### Serialization / AOT — 1 in use · range `PRAG2800`–`PRAG2899`

| ID | Symbol | Severity | Title |
|---|---|---|---|
| `PRAG2800` | `MissingJsonContextForBoundaryType` | Info | Serialized type is not covered by a JsonSerializerContext (AOT) |

*First free after the last used:* `PRAG2801`.

### Privacy — 13 in use · range `PRAG2900`–`PRAG2999`

| ID | Symbol | Severity | Title |
|---|---|---|---|
| `PRAG2900` | `NoPathToSubject` | Error | Personal data cannot be reached from any data subject |
| `PRAG2901` | `RetainWithoutReason` | Error | Retained personal data needs a stated reason |
| `PRAG2902` | `DestroyKeyWithoutEncryption` | Error | DestroyKey requires the property to be encrypted |
| `PRAG2903` | `UnclassifiedProperty` | Error | Property on a subject-reachable entity is not classified |
| `PRAG2904` | `SpecialCategoryWithoutPolicy` | Warning | Special-category data exposed without an authorization policy |
| `PRAG2906` | `InvalidSubjectPath` | Error | [LinksToSubject] names a property that cannot be followed |
| `PRAG2907` | `UnwritableProperty` | Error | Personal data with an erasure strategy that cannot be applied |
| `PRAG2908` | `UnsupportedSubjectIdentifierType` | Warning | Subject identifier type cannot be matched against a subject reference |
| `PRAG2909` | `NullOnNonNullableProperty` | Error | Null erasure needs a property that can hold null |
| `PRAG2910` | `RecordAccessWithoutAuditTrail` | Warning | [RecordAccess] has no audit trail to write to |
| `PRAG2911` | `CompositionWithoutDeclaredData` | Warning | Composing operation declares no personal data |
| `PRAG2912` | `NoAnonymousValue` | Error | Anonymize needs a type with an anonymous value |
| `PRAG2913` | `RedundantProcessesData` | Info | [ProcessesData] restates what the generator infers |

*Free inside the used span:* `PRAG2905`.

*First free after the last used:* `PRAG2914`.

### Generator infrastructure — 2 in use · range `PRAG9000`–`PRAG9099`

| ID | Symbol | Severity | Title |
|---|---|---|---|
| `PRAG9000` | `FeatureFailed` | Error | Source generator output failed |
| `PRAG9001` | `UnresolvedTypeStoppedGeneration` | Warning | A type the operation names does not resolve |

*First free after the last used:* `PRAG9002`.

