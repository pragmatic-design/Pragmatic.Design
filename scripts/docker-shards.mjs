/**
 * Which CI job runs which container suite (`check.mjs --tier docker --shard k/n`).
 *
 * The container suites run one at a time because, together, they exhaust one host's Docker. Jobs on
 * separate runners do not share a Docker, so CI splits the suites across three jobs and keeps each job
 * sequential. One job of twenty suites took ~24 minutes after a ~6-minute build and ran after the gate;
 * three jobs beside the gate take the longest of them.
 *
 * The map is written out, not derived from a hash of the name, so that a reader can find where a suite
 * runs and the shards stay balanced by what the suites cost. The durations below are the CI runner's, run
 * 38082703243 (2026-10-10); rebalance from a newer run when a shard grows well past the others.
 *
 * A container suite in no shard would stop running in CI without a word: the full tier fails on it
 * (`shardProblems`), as it fails on a shard that names a suite that no longer exists.
 */
export const DOCKER_SHARDS = [
  // ~398 s
  [
    'TimeOff.IntegrationTests', //            6m 11s
    'Invoicing.IntegrationTests', //            25s
    'Pragmatic.Configuration.Redis.Tests', //    2s
  ],
  // ~403 s
  [
    'Casework.IntegrationTests', //            3m 03s
    'Warehouse.IntegrationTests', //           1m 33s
    'Pragmatic.Messaging.Tests', //            1m 09s
    'Pragmatic.Configuration.Aws.Tests', //      31s
    'Pragmatic.Privacy.E2E.Tests', //            27s
  ],
  // ~407 s
  [
    'Showcase.IntegrationTests', //            3m 00s
    'Pragmatic.Mapping.EFCore.Tests', //       1m 08s
    'Pragmatic.Migrations.Tests', //             40s
    'Conformance.Tests', //                      29s
    'Pragmatic.Persistence.EFCore.Tests', //     25s
    'Pragmatic.Configuration.Database.Tests', // 17s
    'Pragmatic.Events.Tests', //                 10s
    'Pragmatic.Caching.Redis.Tests', //           9s
    'Pragmatic.Configuration.Vault.Tests', //     9s
    'Showcase.Host.Distributed.Tests', //         8s
    'Showcase.Billing.Host.Tests', //             6s
    'Pragmatic.Configuration.Consul.Tests', //    6s
  ],
];

/**
 * What is wrong with `shards` against the container suites on disk: a suite in no shard, a name that is
 * not a container suite, a suite in more than one shard. Empty when the map is whole.
 *
 * @param {string[][]} shards
 * @param {string[]} suites the names of the projects that declare `<RequiresDocker>true</RequiresDocker>`
 * @returns {string[]}
 */
export function shardProblems(shards, suites) {
  const problems = [];
  const where = new Map();
  shards.forEach((names, i) => {
    for (const name of names) where.set(name, [...(where.get(name) ?? []), i + 1]);
  });

  for (const suite of suites) if (!where.has(suite)) problems.push(`${suite} requires Docker and is in no shard`);

  for (const [name, at] of where) {
    if (!suites.includes(name)) problems.push(`${name} is in shard ${at.join(', ')} and is not a container suite`);
    else if (at.length > 1) problems.push(`${name} is in shards ${at.join(' and ')}`);
  }

  return problems;
}

/**
 * Reads `--shard k/n`. `n` has to be the number of shards in the map: a CI matrix of a different width
 * would leave suites unrun, or run some twice, with every job green.
 *
 * @param {string} value
 * @param {string[][]} shards
 * @returns {{ index: number, names: string[] }}
 */
export function parseShard(value, shards) {
  const m = /^(\d+)\/(\d+)$/.exec(String(value));
  if (!m) throw new Error(`--shard takes k/n, as in 1/${shards.length}; got '${value}'`);

  const k = Number(m[1]);
  const n = Number(m[2]);
  if (n !== shards.length) throw new Error(`--shard ${value}: ${n} shards asked for, the map has ${shards.length}`);
  if (k < 1 || k > n) throw new Error(`--shard takes k/n with k from 1 to ${n}; got '${value}'`);

  return { index: k - 1, names: shards[k - 1] };
}
