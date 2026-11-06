/**
 * Where a source generator threw, read from the compiler's SARIF error logs (#566).
 *
 * ⚠️ A generator that throws in a pipeline step (a `Select`, an attribute transform) fails as CS8785, and
 * the build's console shows only the first line of it: the exception's type and message. That was all the
 * merge queue's log said of a crash seen once in four builds of the same tree, and a re-run is green. The
 * compiler writes the whole stack into its error log, so the gate turns a log on for each project
 * (`PragmaticErrorLogDirectory`, Directory.Build.targets) and prints the stack from there.
 */
import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';

/** The id of "a generator failed to generate source", which carries the exception and its stack. */
export const GENERATOR_FAILED = 'CS8785';

/**
 * Every CS8785 in the `.sarif` files of `dir`: the project's log and the compiler's whole message, its stack
 * included. A log that does not parse is reported as such, not skipped: the build that wrote it failed, and
 * a crash it holds would otherwise read as no crash.
 */
export function generatorCrashes(dir) {
  if (!existsSync(dir))
    return [];
  const crashes = [];
  for (const file of readdirSync(dir).filter((f) => f.endsWith('.sarif')).sort()) {
    let log;
    try {
      log = JSON.parse(readFileSync(join(dir, file), 'utf8'));
    } catch (error) {
      crashes.push({ log: file, message: `the error log does not parse (${error.message})` });
      continue;
    }

    for (const run of log.runs ?? []) {
      for (const result of run.results ?? []) {
        if (result.ruleId !== GENERATOR_FAILED)
          continue;
        // SARIF 1 (the compiler's default) holds the message as a string, SARIF 2 as { text }.
        const message = typeof result.message === 'string' ? result.message : result.message?.text ?? '';
        crashes.push({ log: file, message: message.replace(/\r\n/g, '\n').trimEnd() });
      }
    }
  }

  return crashes;
}
