/**
 * The ratchet that says which contracts a package registers without declaring it.
 *
 * The cases worth a test are the ones where "registered" and "declared" come apart, and the control
 * that keeps the check from being satisfied by annotating everything: an interface nothing registers
 * must not be asked for a declaration it has no reason to carry.
 *
 *   node --test scripts/host-provided-contracts.test.mjs
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { tmpdir } from 'node:os';

/** A throwaway repository: `files` is a map of path → source text. */
function scan(files) {
  const root = mkdtempSync(join(tmpdir(), 'host-provided-contracts-'));

  for (const [path, text] of Object.entries(files)) {
    const full = join(root, path);
    mkdirSync(dirname(full), { recursive: true });
    writeFileSync(full, text, 'utf-8');
  }

  process.env.PRAGMATIC_CONTRACTS_ROOT = root;
  // Imported per call: the module reads the root at call time, but a fresh import keeps each case
  // independent of any state a future version might hold.
  return import(`./host-provided-contracts.mjs?case=${encodeURIComponent(root)}`)
    .then((module) => {
      const result = module.hostProvidedContracts();
      delete process.env.PRAGMATIC_CONTRACTS_ROOT;
      rmSync(root, { recursive: true, force: true });
      return result;
    });
}

const contract = (name, declared) =>
  `namespace Pragmatic.Probe;\n\n${declared ? '[global::Pragmatic.Composition.Attributes.ProvidedByHost(global::Pragmatic.Composition.Attributes.Lifetime.Singleton)]\n' : ''}public interface ${name}\n{\n}\n`;

const registers = (name) =>
  `namespace Pragmatic.Probe;\n\npublic static class ProbeExtensions\n{\n    public static void UseProbe(this IServiceCollection services)\n    {\n        services.AddSingleton<${name}, Probe>();\n    }\n}\n`;

test('a contract registered and not declared is reported', async () => {
  const { undeclared, registrations } = await scan({
    'Pragmatic.Probe/src/Pragmatic.Probe/IProbeStore.cs': contract('IProbeStore', false),
    'Pragmatic.Probe/src/Pragmatic.Probe/ProbeExtensions.cs': registers('IProbeStore'),
  });

  assert.equal(registrations, 1);
  assert.deepEqual(undeclared.map((u) => u.contract), ['IProbeStore']);
});

test('the same contract with the declaration is not reported', async () => {
  const { undeclared, declared } = await scan({
    'Pragmatic.Probe/src/Pragmatic.Probe/IProbeStore.cs': contract('IProbeStore', true),
    'Pragmatic.Probe/src/Pragmatic.Probe/ProbeExtensions.cs': registers('IProbeStore'),
  });

  assert.equal(declared, 1);
  assert.deepEqual(undeclared, []);
});

test('an interface nothing registers is not reported', async () => {
  const { undeclared, registrations } = await scan({
    'Pragmatic.Probe/src/Pragmatic.Probe/IProbeStore.cs': contract('IProbeStore', false),
  });

  assert.equal(registrations, 0);
  assert.deepEqual(undeclared, []);
});

test('a registration quoted in a comment is not one', async () => {
  const { undeclared, registrations } = await scan({
    'Pragmatic.Probe/src/Pragmatic.Probe/IProbeStore.cs': contract('IProbeStore', false),
    'Pragmatic.Probe/src/Pragmatic.Probe/ProbeExtensions.cs':
      'namespace Pragmatic.Probe;\n\n// services.AddSingleton<IProbeStore, Probe>();\npublic static class ProbeExtensions\n{\n}\n',
  });

  assert.equal(registrations, 0);
  assert.deepEqual(undeclared, []);
});

test('a name two packages declare is dropped rather than guessed at', async () => {
  const { undeclared } = await scan({
    'Pragmatic.Probe/src/Pragmatic.Probe/IProbeStore.cs': contract('IProbeStore', false),
    'Pragmatic.Other/src/Pragmatic.Other/IProbeStore.cs': contract('IProbeStore', true),
    'Pragmatic.Probe/src/Pragmatic.Probe/ProbeExtensions.cs': registers('IProbeStore'),
  });

  assert.deepEqual(undeclared, []);
});

test("the source generator's own templates are not a package's wiring", async () => {
  const { undeclared, registrations } = await scan({
    'Pragmatic.Probe/src/Pragmatic.Probe/IProbeStore.cs': contract('IProbeStore', false),
    'Pragmatic.SourceGenerator/src/Pragmatic.SourceGenerator/ProbeTemplate.cs':
      'namespace Pragmatic.SourceGenerator;\n\npublic static class ProbeTemplate\n{\n    public const string Line = "services.AddSingleton<IProbeStore, Probe>();";\n}\n',
  });

  assert.equal(registrations, 0);
  assert.deepEqual(undeclared, []);
});

test('a contract registered in one package and declared in another is read from its declaration', async () => {
  const { undeclared } = await scan({
    'Pragmatic.Abstractions/src/Pragmatic.Abstractions/IProbeStore.cs': contract('IProbeStore', true),
    'Pragmatic.Probe/src/Pragmatic.Probe/ProbeExtensions.cs': registers('IProbeStore'),
  });

  assert.deepEqual(undeclared, []);
});
