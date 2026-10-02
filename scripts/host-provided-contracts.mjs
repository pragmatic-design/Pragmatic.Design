#!/usr/bin/env node
/**
 * host-provided-contracts.mjs — a contract a package registers, and does not say so.
 *
 *   node scripts/host-provided-contracts.mjs [--all]
 *
 * A module's generator sees the `[Service]` classes of its own compilation and nothing a host adds
 * when it starts, so a `[Service]` that depends on `IFileStorage` — registered by `UseStorage()` —
 * looked unregistered to it and was refused with PRAG1641. The answer is `[ProvidedByHost]` on the
 * contract, in the package that registers it.
 *
 * WHAT THIS CLOSES. The attribute keeps the copy of the name out of the generator, but nothing *fails*
 * when a new package forgets it: every suite here passes, the gate is green, the package ships, and
 * the first application that injects the contract from a `[Service]` gets PRAG1641 on code that is
 * correct. The four contracts it exists for are `IFileStorage`, `IEmailSender`, `ITenantStore` and
 * `IStringLocalizer`, each registered by a `Use*` call — and one undeclared contract hides the next,
 * because a build that fails on it never goes one step further.
 *
 * WHAT IS COUNTED: a registration — `services.AddScoped<IFoo, Foo>()` and its Try/Singleton/Transient
 * variants — of an interface THIS repository declares, anywhere in a Pragmatic package's `src`, when
 * the interface's own file says nothing with `[ProvidedByHost]`. Counted once per contract: the
 * number that matters is how many contracts are undeclared, not how many times each is registered.
 *
 * ⚠️ WHY NOT "INSIDE A Use/Add EXTENSION". That narrower reading sees only a fraction of the
 * registrations, and it misses `IFileStorage`, one of the four contracts this exists for, because
 * `UseStorage()` delegates to a builder class whose `AddFileStorage<TStorage>()` does the registering,
 * and `AddLocalDiskStorage()` to a private `ForwardCapabilities` helper. A `Use*` that delegates is
 * the ordinary shape, not the exception, so the narrower reading is not more precise — it is blind to
 * most of the population.
 *
 * WHAT IT IS NOT: a bug list. A contract only the framework resolves — an internal seam no module
 * would ever inject — is registered exactly the same way and is counted here. What makes a reported
 * contract real is the question "would an application's own `[Service]` ever take this in its
 * constructor?", and the budget holds the line while that is answered one package at a time.
 *
 * ⚠️ WHY NOT A DIAGNOSTIC. This is a property of OUR packages, verified over the whole repository
 * once, not a rule to enforce in a user's compilation — so it belongs beside the other ratchets and
 * not in a PRAG id somebody has to own.
 *
 * ⚠️ THE SOURCE GENERATOR IS EXCLUDED. Its "registrations" are strings inside templates: code it
 * writes into somebody else's host, where the contract's declaration is the consuming module's
 * business and not the generator's.
 *
 * BUDGET is a freeze, not a target. It falls when a contract gains its declaration — or when one is
 * ruled an internal seam and the reading learns to say so; it is never raised to make a build pass.
 */
import { existsSync, readdirSync, readFileSync } from "node:fs";
import { join, relative } from "node:path";
import { pathToFileURL } from "node:url";

/**
 * Undeclared contracts at the last measure, once the contracts an application actually injects were
 * declared. The rest are mostly internal seams no application would take in a constructor.
 */
export const BUDGET = 100;

/** The repository root, overridable so the test can run against a throwaway tree. */
function repositoryRoot() {
  return process.env.PRAGMATIC_CONTRACTS_ROOT ?? process.cwd();
}

/** Every `.cs` under a directory, as paths relative to the repository root. */
function sourceFilesUnder(dir, root) {
  const found = [];

  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.name === "obj" || entry.name === "bin") continue;

    const full = join(dir, entry.name);
    if (entry.isDirectory()) found.push(...sourceFilesUnder(full, root));
    else if (entry.name.endsWith(".cs")) found.push(relative(root, full).split("\\").join("/"));
  }

  return found;
}

/** The src tree of every Pragmatic package, minus the generator's — see the note at the top. */
function packageSources(root) {
  return readdirSync(root, { withFileTypes: true })
    .filter((e) => e.isDirectory() && e.name.startsWith("Pragmatic.") && e.name !== "Pragmatic.SourceGenerator")
    .map((e) => join(root, e.name, "src"))
    .filter((dir) => existsSync(dir))
    .flatMap((dir) => sourceFilesUnder(dir, root));
}

/** Source with comment lines removed: a registration quoted in a remark is not one. */
function code(text) {
  return text
    .split("\n")
    .filter((line) => !/^\s*(\/\/|\/\*|\*)/.test(line))
    .join("\n");
}

/**
 * The interfaces this repository declares, by simple name → { file, declared }.
 *
 * ⚠️ By simple name, because that is how the registration call names it. A name declared twice is
 * dropped rather than guessed at: answering for the wrong one of two would be worse than not
 * answering.
 *
 * ⚠️ And `declared` is a search for the word, not for `[ProvidedByHost`: the attribute is written
 * fully qualified on every contract the framework declares
 * (`[global::Pragmatic.Composition.Attributes.ProvidedByHost(…)]`), so the bracket form found none
 * of them — the first version of this ratchet reported all twenty-four as missing.
 */
function declaredInterfaces(root, files) {
  const byName = new Map();
  const ambiguous = new Set();

  for (const file of files) {
    const text = readFileSync(join(root, file), "utf-8");
    const declared = text.includes("ProvidedByHost");

    for (const match of code(text).matchAll(/^\s*public\s+(?:partial\s+)?interface\s+(I[A-Za-z0-9_]*)/gm)) {
      const name = match[1];
      if (byName.has(name)) ambiguous.add(name);
      byName.set(name, { file, declared });
    }
  }

  for (const name of ambiguous) byName.delete(name);
  return byName;
}

/**
 * @returns {{ registrations: number, declared: number, undeclared: {contract: string, file: string}[] }}
 */
export function hostProvidedContracts() {
  const root = repositoryRoot();
  const files = packageSources(root);
  const interfaces = declaredInterfaces(root, files);

  const registration =
    /\b(?:Try)?Add(?:Singleton|Scoped|Transient)\s*<\s*(?:global::)?([A-Za-z0-9_.]+)\s*[,>]/g;

  const undeclared = new Map();
  const declared = new Set();
  let registrations = 0;

  for (const file of files) {
    for (const match of code(readFileSync(join(root, file), "utf-8")).matchAll(registration)) {
      const contract = match[1].split(".").pop();
      const known = interfaces.get(contract);
      if (!known) continue;

      registrations++;
      if (known.declared) declared.add(contract);
      else undeclared.set(contract, { contract, file: known.file });
    }
  }

  return {
    registrations,
    declared: declared.size,
    undeclared: [...undeclared.values()].sort((a, b) => a.contract.localeCompare(b.contract))
  };
}

// --- CLI ---------------------------------------------------------------------------------------

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const { registrations, declared, undeclared } = hostProvidedContracts();
  const all = process.argv.includes("--all");

  console.log(`registrations of a contract declared here: ${registrations}`);
  console.log(`contracts with [ProvidedByHost]: ${declared}`);
  console.log(`contracts without: ${undeclared.length} · budget ${BUDGET}\n`);

  for (const entry of undeclared.slice(0, all ? undeclared.length : 25))
    console.log(`  ${entry.contract.padEnd(34)} ${entry.file}`);

  if (!all && undeclared.length > 25) console.log(`  … and ${undeclared.length - 25} more (--all)`);
}
