/**
 * The source forms the C# 15 compiler reads differently from C# 14, or no longer accepts (#820), as
 * the .NET 11 compiler breaking changes list them:
 * https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/breaking-changes/compiler%20breaking%20changes%20-%20dotnet%2011
 *
 * Generated code is compiled with the consumer's language version, not ours: an application on net11.0
 * compiles it as C# 15 by default. Each form here compiles today and would change meaning, or fail,
 * there. The `@`-escaped spelling is the fix the page gives, so it is never reported.
 *
 *   node --test scripts/csharp15-forms.test.mjs
 */
import { readdirSync, readFileSync } from 'node:fs';
import { basename, join } from 'node:path';

const FORMS = [
  {
    name: '`with(...)` as a collection expression element',
    why: 'C# 15 binds it as the collection constructor arguments, not as a call to a method named `with`; write `@with(`',
    // The opening bracket of the expression, or a comma before the element. `@with` is the escaped call.
    pattern: /(?:\[|,)\s*with\s*\(/,
  },
  {
    name: 'a type or alias named `union` or `closed`',
    why: 'C# 15 makes them contextual keywords in type declarations (CS9370, CS9380); write `@union` / `@closed`',
    pattern: /\b(?:class|struct|record|interface|enum|delegate)\s+(?:union|closed)\b|\busing\s+(?:union|closed)\s*=/,
  },
  {
    name: 'a member declared with the type `union` or `closed`',
    why: 'C# 15 reads `union X;` as a union declaration and `closed X;` as a modifier (CS9370, CS1519); write `@union` / `@closed`',
    pattern: /^\s*(?:(?:public|private|internal|protected|static|readonly|required|new|volatile)\s+)*(?:union|closed)\s+[A-Za-z_]\w*\s*(?:[;={]|=>)/,
  },
  {
    name: '`nameof(this.…)` or `nameof(base.…)` in an attribute',
    why: 'disallowed from C# 14 compilers 10.0.200 onward; drop the `this.` qualifier',
    pattern: /^\s*\[[^\]]*nameof\(\s*(?:this|base)\./,
  },
  {
    name: 'a parenthesised name followed by `when`',
    why: 'C# 15 parses `(X.Y) when` in a switch arm as a constant pattern with a guard, not as a cast of `when`',
    pattern: /\(\s*[A-Za-z_][\w.]*\s*\)\s*when\b/,
  },
];

/**
 * Every file a Pragmatic generator wrote under `root` (a build's obj folder), read for the forms.
 * Only below `generated/Pragmatic.*`: the other generators' output is not ours to change.
 * Returns `{ files, offenders }`, so that zero files read is told apart from zero forms found.
 */
export function csharp15FormsInGenerated(root) {
  const offenders = [];
  let files = 0;
  const walk = (dir, ours) => {
    let entries;
    try { entries = readdirSync(dir, { withFileTypes: true }); } catch { return; }
    for (const entry of entries) {
      const full = join(dir, entry.name);
      if (entry.isDirectory()) { walk(full, ours || (basename(dir) === 'generated' && entry.name.startsWith('Pragmatic.'))); continue; }
      if (!ours || !entry.name.endsWith('.cs')) continue;
      files++;
      for (const finding of csharp15Forms(readFileSync(full, 'utf-8')))
        offenders.push({ site: `${full}:${finding.line}`, ...finding });
    }
  };
  walk(root, false);
  return { files, offenders };
}

/** The lines of a C# source that hold one of the forms, comments left out: [{ line, form, why, text }]. */
export function csharp15Forms(source) {
  const found = [];
  const lines = source.split('\n');
  for (let i = 0; i < lines.length; i++) {
    const text = lines[i];
    if (/^\s*(?:\/\/|\*|\/\*)/.test(text)) continue;
    for (const form of FORMS)
      if (form.pattern.test(text)) found.push({ line: i + 1, form: form.name, why: form.why, text: text.trim() });
  }
  return found;
}
