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
const REPLACED_COMMITS = 'replaced by each engine suite\'s own file: it counts a boundary\'s saves through EF Core\'s DbContext, which the engines '
  + 'count through a save phase of the boundary';
const REPLACED_JOB_STORE = 'replaced by each engine suite\'s own file: the job suites\' store is EF Core\'s over a DbContext of the test\'s own, '
  + 'and the engine\'s over the Booking boundary\'s session on each engine';
const EF_ONLY = 'EF Core only (#363): it exercises a feature the module projects leave out on the engines';
const NO_NAVIGATION = 'refused by the documents engine by design (#479): it exercises an operation the module projects leave out there';
const TEST_ON_EF = 'test code bound to EF Core (owner decision, 2026-11-06): the test itself reads or prepares rows through EF Core';
const TEST_ON_SQL = 'test code bound to the SQL database: the test reads or migrates PostgreSQL itself, and the documents host has none';
const EF_PROVIDER = 'the EF Core provider\'s own mechanism, which the engines do not have by construction: the test is of that mechanism, '
  + 'not of a feature';
const COPY_NAMESPACE = 'names a type the generator puts in the assembly\'s namespace: an engine copy is another assembly (Showcase.Booking.Sql), '
  + 'so the same type has another name there; not a gap';
const LIMIT_BEST_EFFORT = 'a per-entity limit is best effort on the documents engine (owner decision, 2026-10-26, #397): counted before the write, '
  + 'with no serializable transaction, so concurrent adds can cross it';
const NO_TRANSACTION = 'a transaction the test opens: the documents host\'s MongoDB is standalone and runs none, so the unit of work refuses it; '
  + 'what a commit and an abort report on that engine is tested on a replica set (DocCommitHooksTests)';
const COMPILED_AT_CALL ='a bulk write the test calls itself: the Sql engine compiles one where it is called, and the test assembly is not one its '
  + 'generator runs on; the bulk write is tested where it is compiled (Pragmatic.Persistence.Sql.Tests, BulkWritesTests), not a gap';

/**
 * A real gap a measurement found (#819): the feature is missing on the engine, and the issue named implements it there. The exclusion goes
 * with that issue, not before.
 */
const REAL_GAP = 'a real gap, which its issue implements on the engines: ';

/** The reasons an exclusion gives, by name: what the coverage page (engine-coverage.mjs) sorts the exclusions by. */
export const SUITE_REASONS = { REPLACED, REPLACED_FIXTURE, REPLACED_COMMITS, REPLACED_JOB_STORE, EF_ONLY, EF_PROVIDER, NO_NAVIGATION, TEST_ON_EF, TEST_ON_SQL, LIMIT_BEST_EFFORT, REAL_GAP, COPY_NAMESPACE, COMPILED_AT_CALL, NO_TRANSACTION };

const both = ['Sql', 'Documents'];
const s = (path) => `${SUITE_DIR}/${path}`;
const catalog = (path) => `examples/showcase/src/Showcase.Catalog/${path}`;

/** The files an engine suite may leave out, the engines it may leave each out on, why, and the module file it follows from. */
export const ALLOWED_FILE_EXCLUSIONS = {
  [s('Infrastructure/ShowcaseWebFactory.cs')]: { engines: both, reason: REPLACED },
  [s('Infrastructure/PostgresFixture.cs')]: { engines: ['Documents'], reason: REPLACED_FIXTURE },
  // CommitCountTests' counter: the DbContext's SavingChanges on EF Core; on each engine a save phase keyed by the boundary.
  [s('Infrastructure/CommitCounter.cs')]: { engines: both, reason: REPLACED_COMMITS },

  // GET /api/invoices/{id}/pdf is DownloadInvoicePdfAction, which loads the invoice with its lines: refused on the documents engine (#874).
  [s('Endpoints/TheInvoiceAsAPdfTests.cs')]: {
    engines: ['Documents'], reason: NO_NAVIGATION, follows: ['examples/showcase/src/Showcase.Billing/Invoices/Actions/DownloadInvoicePdfAction.cs'],
  },
  // A read by key with navigations (#887): the documents engine keeps a relation as its key, so its repository refuses every include path.
  [s('EntityPersistence/ARowReadWithItsNavigationsStaysInsideTheFiltersTests.cs')]: { engines: ['Documents'], reason: NO_NAVIGATION },
  // Composes the document with DownloadInvoicePdfAction, from the invoice read with its lines through IIncludingReadRepository (#887).
  [s('Endpoints/TheInvoiceInTheGuestsLanguageTests.cs')]: {
    engines: ['Documents'], reason: NO_NAVIGATION, follows: ['examples/showcase/src/Showcase.Billing/Invoices/Actions/DownloadInvoicePdfAction.cs'],
  },
  // POST /api/amenities/{id}/touch is CountAmenitiesAction, which injects the boundary's DbContext and counts through its entity set (#873).
  [s('CrossCutting/BoundaryKeyedServicesTests.cs')]: { engines: both, reason: EF_ONLY, follows: [catalog('Amenities/Actions/CountAmenitiesAction.cs')] },

  // The set [ReadAccess] adds to the reading boundary's DbContext, and its tenant filter. On the engines the reader reads through the
  // owning boundary's repository: there is no second set to filter.
  [s('CrossCutting/ReadAccessSetTests.cs')]: { engines: both, reason: EF_PROVIDER },
  // The queryable extension of a declared specification composes onto an IQueryable, which the engines do not read through. Its repository
  // terminals, in DeclaredSpecificationTests, run there.
  [s('CrossCutting/DeclaredSpecificationQueryableTests.cs')]: { engines: both, reason: EF_PROVIDER },
  // A DTO's projection, composed onto an IQueryable and translated to SQL through EF Core's value converter. The rest of the localized
  // field's round trip, in LocalizedFieldTests, runs on the engines.
  [s('CrossCutting/LocalizedFieldProjectionTests.cs')]: { engines: both, reason: EF_PROVIDER },
  // Whether two boundaries' DbContexts share a DbConnection, which UseTransaction needs. That each boundary has a unit of work of its own,
  // in SharedDatabaseTransactionTests, runs on the engines.
  [s('DomainActions/BoundaryDbConnectionTests.cs')]: { engines: both, reason: EF_PROVIDER },
  // The column lengths of the RBAC package's entities in EF Core's model of the importing boundary. On the engines the package's rows are
  // types of its own that declare them; that the entities are stored at all, in ThePackageEntitiesHaveATableTests, runs on the engines.
  [s('Authorization/ThePackagesColumnLengthsTests.cs')]: { engines: both, reason: EF_PROVIDER },

  // The route constants by their full name, Showcase.Booking.ApiRoutes: on an engine copy the generator names them after that assembly, and
  // the file would not compile there. The typed client tests, in TypedClientTests, run on the engines.
  [s('Endpoints/ApiRoutesConstantsTests.cs')]: { engines: both, reason: COPY_NAMESPACE },

  // The temporal query extensions compose onto an IQueryable, which the engines do not read through. Their repository form, in
  // TemporalRepositoryFormTests (#899), runs there, as do the history scope, the constraints and the database's refusals
  // (TemporalRelationTests) and the timeline (#898).
  [s('EntityPersistence/TemporalQueryExtensionsTests.cs')]: { engines: both, reason: EF_PROVIDER },

  // The job suites' store (#904): EF Core's over a DbContext of the test's own, the engine's over the Booking boundary's session. The
  // three suites that use it run on every engine.
  [s('Jobs/JobStoreUnderTest.cs')]: { engines: both, reason: REPLACED_JOB_STORE },
  [s('Jobs/JobsDbContext.cs')]: { engines: both, reason: REPLACED_JOB_STORE },

  [s('EntityPersistence/OneToOneUniquenessTests.cs')]: { engines: ['Documents'], reason: TEST_ON_SQL },
  [s('EntityPersistence/TheFeeHierarchyIsOneTableTests.cs')]: { engines: ['Documents'], reason: TEST_ON_SQL },
  [s('Migrations/MigrationPipelineTests.cs')]: { engines: ['Documents'], reason: TEST_ON_SQL },
  [s('Migrations/MigrationRunnerE2ETests.cs')]: { engines: ['Documents'], reason: TEST_ON_SQL },
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
    'CrossCutting/BoundaryTests.cs': ['BoundaryFacade_InvoicePaid_TriggersBookingPaymentReceived', 'ReadAccess_BookingReservation_IncludesCatalogPropertyName'],
    'CrossCutting/DataOwnershipTests.cs': [
      'OwnedEntity_AdminWithBypassPermission_SeesAll', 'OwnedEntity_DifferentUser_CannotSeeOtherReservation', 'OwnedEntity_SameUser_CanSeeOwnReservation',
      'Restore_OfTheCallersOwnRetiredReservation_BringsTheRowBack',
    ],
    'CrossCutting/DomainEventTests.cs': ['StateMachine_ConfirmReservation_StatusChangesToConfirmed', 'StateMachine_FullLifecycle_PendingToConfirmedToCheckedIn'],
    'CrossCutting/MessagingDeliveryTests.cs': ['FullPaymentFlow_Booking_Billing_Booking_RoundTrip'],
    'CrossCutting/MessagingPipelineTests.cs': ['MessagingPipeline_InvoicePaid_ReservationTransitionsToPaymentReceived'],
    'CrossCutting/NegativePathTests.cs': ['GetReservation_NonExistentId_Returns404'],
    'CrossCutting/SagaFlowTests.cs': ['SagaFlow_HttpLifecycle_ReservationReachesCheckedInState'],
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
  [test('EntityPersistence/SoftDeleteTests.cs', 'DeleteProperty_SetsIsDeletedTrue')]: deletesAProperty('EntityPersistence/SoftDeleteTests.cs'),
  [test('EntityPersistence/SoftDeleteTests.cs', 'DeletedProperty_NotVisibleInSearch')]: deletesAProperty('EntityPersistence/SoftDeleteTests.cs'),
  [test('EntityPersistence/SoftDeleteTests.cs', 'RestoreProperty_ResetsDeletedFields_VisibleAgain')]: {
    ...deletesAProperty('EntityPersistence/SoftDeleteTests.cs'),
    follows: [catalog('Properties/Mutations/DeletePropertyMutation.cs'), catalog('Properties/Mutations/RestorePropertyMutation.cs')],
  },

  // The repository's own delete, in and out of SoftDeleteScope.Suspend(), runs on both engines; its bulk form, on the documents one.
  [test('EntityPersistence/SoftDeleteTests.cs', 'BulkDeleteThroughTheRepository_InsideASuspendedScope_DeletesTheRow')]: {
    file: s('EntityPersistence/SoftDeleteTests.cs'), engines: ['Sql'], reason: COMPILED_AT_CALL,
  },


  // A room type updated: UpdateRoomTypeMutation, which the documents engine refuses (PRAG3112) and the Catalog project leaves out there.
  [test('Endpoints/AutoIncludeTests.cs', 'AnUpdate_WhoseResponseFlattensANavigation_LoadsIt')]: {
    file: s('Endpoints/AutoIncludeTests.cs'), engines: ['Documents'], reason: NO_NAVIGATION, follows: [catalog('RoomTypes/Mutations/UpdateRoomTypeMutation.cs')],
  },

  // A lookup write inside a transaction, committed or rolled back: the documents host's MongoDB is standalone and runs no transaction, so
  // the unit of work refuses to begin one. The write with no transaction, in the same file, runs there.
  ...Object.fromEntries(['ACategoryAddedInATransaction_IsInTheCacheOnceItCommits', 'ACategoryRolledBack_NeverReachesTheCache'].map((method) => [
    test('CrossCutting/ALookupWriteReloadsTheCacheTests.cs', method),
    { file: s('CrossCutting/ALookupWriteReloadsTheCacheTests.cs'), engines: ['Documents'], reason: NO_TRANSACTION },
  ])),

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
