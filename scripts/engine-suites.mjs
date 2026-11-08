/**
 * The Showcase's integration suite on each engine host (#535): Showcase.IntegrationTests' files, linked into a test project per engine and run
 * against that engine's host. What keeps the copy the same suite:
 *
 * - It leaves out only the files on ALLOWED_FILE_EXCLUSIONS, and only the tests on ALLOWED_TEST_EXCLUSIONS, each for its engine and with the
 *   exclusion it follows from. Nothing is left out silently: a `<Compile Remove>` that is not on the list fails the gate, and so does an entry
 *   no project removes, or a listed test that no longer exists.
 * - An exclusion that follows from a feature the module projects leave out names that file, and that file is on engine-projects.mjs's allowed
 *   list for the same engine. The others follow from an owner decision on test code (2026-11-06): the test's own code is bound to EF Core, or
 *   to the SQL database, not the feature it exercises; or from an owner decision on what an engine guarantees (a per-entity limit under
 *   concurrent writes on documents, 2026-10-26).
 * - A test left out is filtered from the run (`suiteFilter`, which the gate's runner passes to `dotnet test`), not deleted from the source.
 *
 *   node --test scripts/engine-suites.test.mjs
 */
import { dirname, join, normalize } from 'node:path';
import { ALLOWED_EXCLUSIONS } from './engine-projects.mjs';

/** The suite every engine copy is made of. */
export const SUITE_DIR = 'examples/showcase/tests/Showcase.IntegrationTests';

/**
 * The engine copies, by project path. Named Showcase.{Engine}.IntegrationTests: the gate finds a test project by its name ending in Tests,
 * and the engine is read from the segment before it.
 */
export const ENGINE_SUITES = [
  'examples/showcase/tests/Showcase.Sql.IntegrationTests/Showcase.Sql.IntegrationTests.csproj',
  'examples/showcase/tests/Showcase.Documents.IntegrationTests/Showcase.Documents.IntegrationTests.csproj',
];

/** The engine an engine suite runs on, from its name (Showcase.Sql.IntegrationTests); null for any other. */
export function engineOf(path) {
  return /Showcase\.(Sql|Documents)\.IntegrationTests\.csproj$/.exec(path.replaceAll('\\', '/'))?.[1] ?? null;
}

const REPLACED = 'replaced by the engine suite\'s own file: the engine host takes its content root and connection strings differently';
const REPLACED_FIXTURE = 'replaced by the engine suite\'s own fixture, under the name every test takes';
const EF_ONLY = 'EF Core only (#363): it exercises a feature the module projects leave out on the engines';
const NO_NAVIGATION = 'refused by the documents engine by design (#479): it exercises an operation the module projects leave out there';
const TEST_ON_EF = 'test code bound to EF Core (owner decision, 2026-11-06): the test itself reads or prepares rows through EF Core';
const TEST_ON_SQL = 'test code bound to the SQL database: the test reads or migrates PostgreSQL itself, and the documents host has none';
const LIMIT_BEST_EFFORT = 'a per-entity limit is best effort on the documents engine (owner decision, 2026-10-26, #397): counted before the write, '
  + 'with no serializable transaction, so concurrent adds can cross it';

const both = ['Sql', 'Documents'];
const s = (path) => `${SUITE_DIR}/${path}`;
const catalog = (path) => `examples/showcase/src/Showcase.Catalog/${path}`;

/** The files an engine suite may leave out, the engines it may leave each out on, why, and the module file it follows from. */
export const ALLOWED_FILE_EXCLUSIONS = {
  [s('Infrastructure/ShowcaseWebFactory.cs')]: { engines: both, reason: REPLACED },
  [s('Infrastructure/PostgresFixture.cs')]: { engines: ['Documents'], reason: REPLACED_FIXTURE },

  [s('Endpoints/GridSearchTests.cs')]: { engines: both, reason: EF_ONLY, follows: [catalog('Properties/Endpoints/GridSearchPropertiesEndpoint.cs')] },
  [s('Endpoints/TheGridInItsOwnFormatTests.cs')]: {
    engines: both, reason: EF_ONLY,
    follows: [catalog('Properties/Endpoints/DevExpressSearchPropertiesEndpoint.cs'), catalog('Properties/Endpoints/GridSearchPropertiesEndpoint.cs')],
  },

  [s('EntityPersistence/OneToOneUniquenessTests.cs')]: { engines: ['Documents'], reason: TEST_ON_SQL },
  [s('Migrations/MigrationPipelineTests.cs')]: { engines: ['Documents'], reason: TEST_ON_SQL },
  [s('Migrations/MigrationRunnerE2ETests.cs')]: { engines: ['Documents'], reason: TEST_ON_SQL },

  ...Object.fromEntries([
    'Authorization/ThePackageEntitiesHaveATableTests.cs',
    'Authorization/WhoTheRowBelongsToWhenSomebodyElseTypedItTests.cs',
    'CrossCutting/BoundaryKeyedServicesTests.cs',
    'CrossCutting/BoundaryTests.cs',
    'CrossCutting/CachingIntegrationTests.cs',
    'CrossCutting/CascadeWiringTests.cs',
    'CrossCutting/DeclaredSpecificationTests.cs',
    'CrossCutting/InfrastructureTests.cs',
    'CrossCutting/LocalizedFieldTests.cs',
    'CrossCutting/LookupCacheTests.cs',
    'CrossCutting/SagaFlowTests.cs',
    'CrossCutting/TheRotaShowsWhoIsOnItTests.cs',
    'CrossCutting/UserCultureTests.cs',
    'CrossCutting/WhatTheApplicationSentIsKeptTests.cs',
    'DomainActions/CommitCountTests.cs',
    'DomainActions/GuestPreferencesAreSetAndThenChangedTests.cs',
    'DomainActions/SharedDatabaseTransactionTests.cs',
    'Endpoints/AnUploadWhoseCommitIsNotAcknowledgedTests.cs',
    'Endpoints/ApiVersioningTests.cs',
    'Endpoints/AttachmentEndpointTests.cs',
    'Endpoints/GeneratedClientTests.cs',
    'Endpoints/TheInvoiceAsAPdfTests.cs',
    'Endpoints/TypedClientTests.cs',
    'EntityPersistence/AuditedE2ETests.cs',
    'EntityPersistence/CrudLifecycleTests.cs',
    'EntityPersistence/GeneratedValueTests.cs',
    'EntityPersistence/InheritanceTests.cs',
    'EntityPersistence/PrimitiveCollectionTests.cs',
    'EntityPersistence/SoftDeleteTests.cs',
    'EntityPersistence/TemporalRelationTests.cs',
    'EntityPersistence/TheTimelineOfARoleTests.cs',
    'EntityPersistence/ValueObjectE2ETests.cs',
    'Jobs/JobExecutionEndToEndTests.cs',
    'Jobs/JobLeaseIntegrationTests.cs',
    'Jobs/JobsDbContext.cs',
    'Jobs/WhatRunsAfterTheJobFinishesTests.cs',
    'Queries/SpecificationCompositionTests.cs',
    'Validation/WhenTheExpensiveRuleRunsTests.cs',
  ].map((path) => [s(path), { engines: both, reason: TEST_ON_EF }])),
};

/**
 * The single tests an engine suite may leave out of the run, by their full name (namespace, class, method), with the file that declares them,
 * the engines, why, and the module file they follow from. For a file whose other tests run there.
 */
export const ALLOWED_TEST_EXCLUSIONS = {
  // Every read of a reservation by id: GET /api/reservations/{id} is GetReservationAction, which the documents engine refuses (PRAG3111: a
  // [LoadEntity] with an Include, a document keeps a relation as its key) and the Booking project leaves out there, so the route answers 405.
  ...readsAReservationById({
    'Authorization/PermissionEnforcementTests.cs': ['GetReservation_WithExactPermission_Returns200'],
    'CrossCutting/ANavigationsOwnerDoesNotHideTheRootTests.cs': ['AReservation_IsReadWithItsGuest_ByACallerWhoCannotReadTheGuest'],
    'CrossCutting/AuthorizationTests.cs': ['GetReservation_DifferentUser_FilteredByRowLevelSecurity', 'GetReservation_SameUser_CanSeeOwn'],
    'CrossCutting/DataOwnershipTests.cs': [
      'OwnedEntity_AdminWithBypassPermission_SeesAll', 'OwnedEntity_DifferentUser_CannotSeeOtherReservation', 'OwnedEntity_SameUser_CanSeeOwnReservation',
      'Restore_OfTheCallersOwnRetiredReservation_BringsTheRowBack',
    ],
    'CrossCutting/DomainEventTests.cs': ['StateMachine_ConfirmReservation_StatusChangesToConfirmed', 'StateMachine_FullLifecycle_PendingToConfirmedToCheckedIn'],
    'CrossCutting/MessagingDeliveryTests.cs': ['FullPaymentFlow_Booking_Billing_Booking_RoundTrip'],
    'CrossCutting/MessagingPipelineTests.cs': ['MessagingPipeline_InvoicePaid_ReservationTransitionsToPaymentReceived'],
    'CrossCutting/NegativePathTests.cs': ['GetReservation_NonExistentId_Returns404'],
    'CrossCutting/TheZoneAMomentIsWrittenInTests.cs': [
      'AWallClockTheFrontDeskTyped_IsReadAgainstTheHotelsZone', 'AWallClockTheGuestTyped_IsReadAgainstTheGuestsZone',
      'ThePropertyThatFollowsTheReader_AndTheOneThatDoesNot_DisagreeInOnePayload', 'TheSameWallClockFromAnotherZone_IsAnotherInstant',
    ],
    'DomainActions/CompositeActionTests.cs': ['CreateReservation_CompositeAction_CreatesReservationAndAssignsRoom'],
    'DomainActions/NestedMutationEventsTests.cs': ['ClosingAProperty_CancelsEveryReservationInOneTransaction'],
    'Endpoints/EndpointFeatureTests.cs': ['GetReservation_ById_ReturnsEntity'],
    'EntityPersistence/AdvancedEntityTests.cs': ['MapIgnore_ReservationDto_StatusLabelComputed', 'MapProperty_ReservationDto_FlattensNavigationProperties'],
    'EntityPersistence/StateMachineTests.cs': [
      'Reservation_CancelAfterConfirm_ValidTransition', 'Reservation_Cancel_FromPending_ValidTransition', 'Reservation_ConfirmedToCheckedIn_ValidTransition',
      'Reservation_ConfirmedToPaymentReceived_ValidTransition', 'Reservation_PendingToConfirmed_ValidTransition',
    ],
  }),

  // A property deleted (and restored): DeletePropertyMutation and RestorePropertyMutation, which the documents engine refuses (PRAG3112: a
  // write that cascades through a navigation) and the Catalog project leaves out there.
  [test('Queries/VisibilityRuleTests.cs', 'TheAdminRead_StillDoesNotSeeDeletedRows')]: deletesAProperty('Queries/VisibilityRuleTests.cs'),
  [test('EntityPersistence/AdvancedEntityTests.cs', 'SoftDeleteCascade_DeleteProperty_CancellationPolicyExcluded')]: deletesAProperty('EntityPersistence/AdvancedEntityTests.cs'),
  [test('EntityPersistence/AdvancedEntityTests.cs', 'SoftDeleteCascade_DeleteProperty_RoomTypesExcludedFromSearch')]: deletesAProperty('EntityPersistence/AdvancedEntityTests.cs'),
  [test('CrossCutting/TenantMutationIsolationTests.cs', 'RestoreProperty_CrossTenant_CannotRestoreAnotherTenantsSoftDeletedRow')]: {
    ...deletesAProperty('CrossCutting/TenantMutationIsolationTests.cs'),
    follows: [catalog('Properties/Mutations/DeletePropertyMutation.cs'), catalog('Properties/Mutations/RestorePropertyMutation.cs')],
  },

  // A room type updated: UpdateRoomTypeMutation, which the documents engine refuses (PRAG3112) and the Catalog project leaves out there.
  [test('Endpoints/AutoIncludeTests.cs', 'AnUpdate_WhoseResponseFlattensANavigation_LoadsIt')]: {
    file: s('Endpoints/AutoIncludeTests.cs'), engines: ['Documents'], reason: NO_NAVIGATION, follows: [catalog('RoomTypes/Mutations/UpdateRoomTypeMutation.cs')],
  },

  // Six racers for a guest's last tag slot: on the documents engine more than one can take it, as decided. The sequential limit and the
  // idempotent re-add, in the same file, still run there.
  [test('Endpoints/TagConcurrencyTests.cs', 'ConcurrentAdds_NeverExceedTheLimit')]: {
    file: s('Endpoints/TagConcurrencyTests.cs'), engines: ['Documents'], reason: LIMIT_BEST_EFFORT,
  },
};

/** A test's full name, from its file under the suite (the namespace follows the folder) and its method. */
function test(path, method) {
  const folder = path.split('/').slice(0, -1).join('.');
  const type = path.split('/').pop().replace(/\.cs$/, '');
  return `Showcase.IntegrationTests.${folder ? folder + '.' : ''}${type}.${method}`;
}

function readsAReservationById(byFile) {
  return Object.fromEntries(Object.entries(byFile).flatMap(([path, methods]) => methods.map((method) => [test(path, method), {
    file: s(path), engines: ['Documents'], reason: NO_NAVIGATION, follows: ['examples/showcase/src/Showcase.Booking/Reservations/Actions/GetReservationAction.cs'],
  }])));
}

function deletesAProperty(path) {
  return { file: s(path), engines: ['Documents'], reason: NO_NAVIGATION, follows: [catalog('Properties/Mutations/DeletePropertyMutation.cs')] };
}

const toRepo = (path) => normalize(path).replaceAll('\\', '/');
const attribute = (text, element, name) => [...text.matchAll(new RegExp(`<${element}\\s+${name}="([^"]+)"`, 'g'))];

/** The files a project leaves out of the suite, as paths from the repository's root, with where each `<Compile Remove>` sits. */
function removedFiles(path, text) {
  return attribute(text, 'Compile', 'Remove').map((m) => ({ file: toRepo(join(dirname(path), m[1].replaceAll('\\', '/'))), index: m.index }));
}

/** The test methods a source declares: each `[Fact]` or `[Theory]` method's name. */
export function testMethods(text) {
  return [...text.matchAll(/\[(?:Fact|Theory)[^\]]*\][\s\S]*?(?:public|internal)\s+(?:async\s+)?[\w<>]+\s+(\w+)\s*\(/g)].map((m) => m[1]);
}

/**
 * What is wrong with the engine suites: one line each, none when they hold.
 *
 * @param {{ path: string, text: string }[]} suites the engine suites' projects, their paths from the repository's root
 * @param {(path: string) => string | null} read a source's text by its path from the root; null when there is no such file
 */
export function engineSuiteProblems(suites, read) {
  const problems = [];
  for (const { path, text } of suites) {
    const engine = engineOf(path);
    if (!engine) {
      problems.push(`${path}: an engine suite is named for its engine (Showcase.Sql.IntegrationTests or Showcase.Documents.IntegrationTests)`);
      continue;
    }

    const removed = removedFiles(path, text);
    for (const { file, index } of removed) {
      const allowed = ALLOWED_FILE_EXCLUSIONS[file];
      if (!allowed || !allowed.engines.includes(engine)) {
        problems.push(`${path} leaves out ${file}, which is not an allowed exclusion on the ${engine} engine: a test that fails there is an issue, not an exclusion`);
        continue;
      }

      if (read(file) === null) problems.push(`${path} leaves out ${file}, which does not exist`);
      const before = text.slice(0, index);
      const comment = before.lastIndexOf('-->');
      if (comment < 0 || before.slice(comment).includes('</ItemGroup>')) problems.push(`${path} leaves out ${file} with no comment saying why`);
    }

    // The list is the suite's, not a menu: an exclusion allowed on this engine that the project does not take is a stale entry.
    const taken = new Set(removed.map((r) => r.file));
    for (const [file, allowed] of Object.entries(ALLOWED_FILE_EXCLUSIONS)) {
      if (allowed.engines.includes(engine) && !taken.has(file)) problems.push(`${file} is allowed out of the ${engine} suite and ${path} compiles it: take it off the list, or out of the project`);
    }

    for (const [name, allowed] of Object.entries(ALLOWED_TEST_EXCLUSIONS)) {
      if (!allowed.engines.includes(engine)) continue;
      const source = read(allowed.file);
      const method = name.split('.').pop();
      if (source === null || !testMethods(source).includes(method)) problems.push(`${name} is left out of the ${engine} suite and ${allowed.file} declares no such test`);
      else if (taken.has(allowed.file)) problems.push(`${name} is left out of the ${engine} suite by name, and its file is left out whole`);
    }
  }

  // What an exclusion follows from: a module file the module projects leave out on the same engine.
  for (const [what, allowed] of [...Object.entries(ALLOWED_FILE_EXCLUSIONS), ...Object.entries(ALLOWED_TEST_EXCLUSIONS)]) {
    for (const follows of allowed.follows ?? []) {
      const module = ALLOWED_EXCLUSIONS[follows];
      for (const engine of allowed.engines) {
        if (!module || !module.engines.includes(engine)) problems.push(`${what} follows from ${follows}, which the module projects do not leave out on the ${engine} engine`);
      }
    }
  }

  return problems;
}

/** The `dotnet test` arguments that leave the listed tests out of an engine suite's run; none for any other project. */
export function suiteFilter(projectPath) {
  const path = toRepo(projectPath);
  if (!ENGINE_SUITES.includes(path)) return [];
  const engine = engineOf(path);
  const names = Object.entries(ALLOWED_TEST_EXCLUSIONS).filter(([, a]) => a.engines.includes(engine)).map(([name]) => name).sort();
  return names.length === 0 ? [] : ['--filter', names.map((n) => `FullyQualifiedName!=${n}`).join('&')];
}

/**
 * The test methods an engine suite compiles and runs, beside the EF Core suite's: what each copy reports so that a count that falls is seen.
 *
 * @param {string} path the engine suite's project
 * @param {string[]} suiteFiles the EF Core suite's sources, paths from the root
 * @param {(path: string) => string | null} read
 */
export function suiteCount(path, suiteFiles, read) {
  const engine = engineOf(path);
  const removed = new Set(removedFiles(path, read(path) ?? '').map((r) => r.file));
  let all = 0;
  let compiled = 0;
  for (const file of suiteFiles) {
    const methods = testMethods(read(file) ?? '').length;
    all += methods;
    if (!removed.has(file)) compiled += methods;
  }

  const filtered = Object.values(ALLOWED_TEST_EXCLUSIONS).filter((a) => a.engines.includes(engine)).length;
  return { all, files: removed.size, filtered, runs: compiled - filtered };
}
