/**
 * The Showcase's module and host projects on each persistence engine (examples/showcase/engines/, #533 and #534): what keeps them the same
 * application as src/, on another engine.
 *
 * - None reaches EF Core: no project it references, at any depth, references an Entity Framework Core package or a Pragmatic .EFCore project.
 *   A module project that did would run its reads on EF Core with the engine beside it, and say nothing.
 * - It leaves out only the files the owner allowed (#479), each for its engine: the EF Core-only list of #363, and what the documents engine
 *   refuses by design. Anything else the module does not compile on an engine is a defect of the generator or of the engine, an issue of its
 *   own, never an exclusion. Each exclusion sits under a comment that says why.
 *
 *   node --test scripts/engine-projects.test.mjs
 */
import { dirname, join, normalize } from 'node:path';

/** The projects this check reads, by folder. */
export const ENGINE_PROJECTS_DIR = 'examples/showcase/engines';

const EF_ONLY = 'EF Core only (#363): it reads through an IQueryable';
const NO_NAVIGATION = 'refused by the documents engine by design (#479): a document keeps a relation as its key';
const HOST_WIRING = 'the host\'s wiring on EF Core (#534), in place of which each engine host compiles its own ShowcaseEngine';

/** The files a module project may leave out, the engines it may leave each out on, and why. Paths from the repository's root. */
export const ALLOWED_EXCLUSIONS = {
  'examples/showcase/src/Showcase.Catalog/Amenities/Actions/CountAmenitiesAction.cs': { engines: ['Sql', 'Documents'], reason: EF_ONLY },
  'examples/showcase/src/Showcase.Catalog/Properties/Endpoints/DevExpressSearchPropertiesEndpoint.cs': { engines: ['Sql', 'Documents'], reason: EF_ONLY },
  'examples/showcase/src/Showcase.Catalog/Properties/Endpoints/GridSearchPropertiesEndpoint.cs': { engines: ['Sql', 'Documents'], reason: EF_ONLY },
  'examples/showcase/src/Showcase.Catalog/Properties/Mutations/DeletePropertyMutation.cs': { engines: ['Documents'], reason: NO_NAVIGATION + ' (PRAG3112)' },
  'examples/showcase/src/Showcase.Catalog/Properties/Mutations/RestorePropertyMutation.cs': { engines: ['Documents'], reason: NO_NAVIGATION + ' (PRAG3112)' },
  'examples/showcase/src/Showcase.Catalog/RoomTypes/Mutations/UpdateRoomTypeMutation.cs': { engines: ['Documents'], reason: NO_NAVIGATION + ' (PRAG3112)' },
  'examples/showcase/src/Showcase.Booking/Reservations/Actions/GetReservationAction.cs': { engines: ['Documents'], reason: NO_NAVIGATION + ' (PRAG3111)' },
  'examples/showcase/src/Showcase.Billing/Invoices/Actions/DownloadInvoicePdfAction.cs': { engines: ['Documents'], reason: NO_NAVIGATION + ' (PRAG3111)' },
  'examples/showcase/src/Showcase.Host/Engine/ShowcaseEngine.EfCore.cs': { engines: ['Sql', 'Documents'], reason: HOST_WIRING },
};

/** The engine a module project is for, from its name (Showcase.Booking.Sql, Showcase.Booking.Documents); null for any other. */
export function engineOf(path) {
  const match = /\.(Sql|Documents)\.csproj$/.exec(path.replaceAll('\\', '/'));
  return match ? match[1] : null;
}

const toRepo = (path) => normalize(path).replaceAll('\\', '/');

/**
 * The build properties the generator reads, which an engine project sets as its source project does (#582): one left out makes the
 * generator emit different code for the same module. `PragmaticGenerateJsonContext` left out of Showcase.Booking.Sql took away its JSON
 * context, and a body with a missing field answered 400 there and 422 on EF Core.
 */
export const GENERATOR_PROPERTIES = ['PragmaticGenerateJsonContext', 'PublishAot'];

const property = (text, name) => new RegExp(`<${name}>([^<]*)</${name}>`).exec(text)?.[1].trim() ?? null;

/** The source project whose files an engine project links (Compile Include="..\..\src\Showcase.Booking\**\*.cs"), from the root; null without one. */
function sourceProjectOf(path, text) {
  const include = attribute(text, 'Compile', 'Include').map((i) => i.replaceAll('\\', '/')).find((i) => i.endsWith('/**/*.cs'));
  if (!include) return null;
  const folder = toRepo(join(dirname(path), include.slice(0, -'/**/*.cs'.length)));
  return `${folder}/${folder.split('/').pop()}.csproj`;
}

const attribute = (text, element, name) => [...text.matchAll(new RegExp(`<${element}\\s+${name}="([^"]+)"`, 'g'))].map((m) => m[1]);

/** A project's own reference to EF Core: an Entity Framework Core package, or a Pragmatic .EFCore project; null when it has none. */
function efCoreReference(text) {
  const efPackage = attribute(text, 'PackageReference', 'Include').find((p) => p.startsWith('Microsoft.EntityFrameworkCore'));
  if (efPackage) return efPackage;
  return attribute(text, 'ProjectReference', 'Include').map((p) => p.replaceAll('\\', '/').split('/').pop()).find((p) => /\.EFCore\.csproj$/.test(p)) ?? null;
}

/**
 * What is wrong with the module projects: one line each, none when they hold.
 *
 * @param {{ path: string, text: string }[]} projects the module projects, their paths from the repository's root
 * @param {(path: string) => string | null} read a project's text by its path from the root; null when there is no such file
 * @param {(path: string) => boolean} exists true when a source file exists
 */
export function engineProjectProblems(projects, read, exists) {
  const problems = [];
  for (const { path, text } of projects) {
    const engine = engineOf(path);
    if (!engine) {
      problems.push(`${path}: a module project is named for its engine (.Sql or .Documents)`);
      continue;
    }

    // EF Core anywhere below it: the references followed to every depth, the first one found reported with the way it was reached.
    const seen = new Set([toRepo(path)]);
    const pending = [{ path: toRepo(path), text, via: [] }];
    while (pending.length > 0) {
      const current = pending.shift();
      const reference = efCoreReference(current.text);
      if (reference) {
        problems.push(`${path} reaches EF Core: ${[...current.via, reference].join(' -> ')}`);
        break;
      }

      for (const include of attribute(current.text, 'ProjectReference', 'Include')) {
        const target = toRepo(join(dirname(current.path), include.replaceAll('\\', '/')));
        if (seen.has(target)) continue;
        seen.add(target);
        const targetText = read(target);
        if (targetText !== null) pending.push({ path: target, text: targetText, via: [...current.via, target.split('/').pop()] });
      }
    }

    // The generator's build properties: as the source project sets them.
    const source = sourceProjectOf(path, text);
    const sourceText = source ? read(source) : null;
    if (sourceText !== null) {
      for (const name of GENERATOR_PROPERTIES) {
        const mine = property(text, name);
        const theirs = property(sourceText, name);
        if (mine !== theirs)
          problems.push(`${path} sets ${name} to ${mine ?? '(unset)'} where ${source} sets it to ${theirs ?? '(unset)'}: the generator emits different code on the two`);
      }
    }

    // Each file left out: allowed for this engine, existing, and under the comment that says why.
    for (const match of text.matchAll(/<Compile\s+Remove="([^"]+)"/g)) {
      const removed = toRepo(join(dirname(path), match[1].replaceAll('\\', '/')));
      const allowed = ALLOWED_EXCLUSIONS[removed];
      if (!allowed || !allowed.engines.includes(engine)) {
        problems.push(`${path} leaves out ${removed}, which is not an allowed exclusion on the ${engine} engine: a file the module cannot compile there is an issue, not an exclusion`);
        continue;
      }

      if (!exists(removed)) problems.push(`${path} leaves out ${removed}, which does not exist`);
      const before = text.slice(0, match.index);
      const comment = before.lastIndexOf('-->');
      if (comment < 0 || before.slice(comment).includes('</ItemGroup>')) problems.push(`${path} leaves out ${removed} with no comment saying why`);
    }
  }

  return problems;
}
