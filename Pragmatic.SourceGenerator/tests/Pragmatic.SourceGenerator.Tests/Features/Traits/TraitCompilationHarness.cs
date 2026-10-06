using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Pragmatic.SourceGenerator.Tests.Features.Traits;

/// <summary>
/// Runs the FULL <see cref="PragmaticSourceGenerator"/> over a realistic entity source and hands
/// back both the generated files and the diagnostics.
///
/// Trait tests that build a model by hand can never catch a defect in the transform (an option the
/// consumer cannot even express, a generic argument nobody reads). These runs start from source, so
/// they exercise attribute → transform → model → template as a whole.
///
/// The generated trait code references BCL types by short name (Guid, Task, ICollection,
/// DateTimeOffset, Func) without emitting the corresponding usings — relying on the consumer
/// project's <c>ImplicitUsings=enable</c>. To reproduce real-world conditions, the harness injects
/// an equivalent set of global usings as a second syntax tree.
/// </summary>
internal static class TraitCompilationHarness
{
    // Replicates <ImplicitUsings>enable</ImplicitUsings> — without these the generated
    // trait code (which uses bare Guid/Task/ICollection/...) cannot bind, exactly as it
    // would not in a project without implicit usings.
    private const string GlobalUsings = """
        global using global::System;
        global using global::System.Collections.Generic;
        global using global::System.IO;
        global using global::System.Linq;
        global using global::System.Threading;
        global using global::System.Threading.Tasks;
        """;

    private static MetadataReference[]? _cachedReferences;

    public static MetadataReference[] References => _cachedReferences ??= BuildReferences();

    /// <summary>
    /// Runs the generator and returns every generated file (hint name → source text) plus the
    /// generator's own diagnostics.
    /// </summary>
    public static (Dictionary<string, string> Sources, List<Diagnostic> GeneratorDiagnostics) Generate(string entitySource)
    {
        var compilation = CreateCompilation(entitySource);
        var driver = CSharpGeneratorDriver.Create(new PragmaticSourceGenerator());
        var result = driver.RunGenerators(compilation).GetRunResult();

        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var generated in result.Results.SelectMany(r => r.GeneratedSources))
            sources[generated.HintName] = generated.SourceText.ToString();

        return (sources, result.Diagnostics.ToList());
    }

    /// <summary>
    /// Runs the generator and splits the resulting compilation errors into those raised inside
    /// files owned by the trait pipeline and everything else (the parent entity's own persistence /
    /// infra output needs the full host reference closure, which is outside this scope).
    /// </summary>
    public static (List<Diagnostic> Trait, List<Diagnostic> Other) CompileAndSplitErrors(
        string entitySource, Func<string, bool> isTraitFile)
    {
        var compilation = CreateCompilation(entitySource);
        var driver = CSharpGeneratorDriver.Create(new PragmaticSourceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

        var errors = outputCompilation
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        var trait = errors
            .Where(d => isTraitFile(d.Location.SourceTree?.FilePath ?? string.Empty))
            .ToList();
        return (trait, errors.Except(trait).ToList());
    }

    /// <summary>
    /// Runs the generator and returns the errors <b>and warnings</b> raised inside the generated files
    /// <paramref name="isFile"/> selects.
    /// </summary>
    /// <remarks>
    /// For what an application builds with warnings as errors: a nullable warning in a generated file
    /// stops it as surely as an error, and <see cref="CompileAndSplitErrors"/> does not see it.
    /// </remarks>
    public static List<Diagnostic> CompileAndCollectWarnings(string entitySource, Func<string, bool> isFile)
    {
        var compilation = CreateCompilation(entitySource);
        var driver = CSharpGeneratorDriver.Create(new PragmaticSourceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

        return outputCompilation
            .GetDiagnostics()
            .Where(d => d.Severity >= DiagnosticSeverity.Warning
                        && isFile(d.Location.SourceTree?.FilePath ?? string.Empty))
            .ToList();
    }

    public static string FormatErrors(IEnumerable<Diagnostic> errors)
        => string.Join(
            Environment.NewLine,
            errors.Select(d =>
            {
                var f = Path.GetFileName(d.Location.SourceTree?.FilePath ?? "(input)");
                return $"  {d.Id} {d.GetMessage()}  @ {f} {d.Location.GetLineSpan().Span}";
            }));

    private static CSharpCompilation CreateCompilation(string entitySource)
    {
        var trees = new[]
        {
            CSharpSyntaxTree.ParseText(GlobalUsings),
            // LocationInfo.From returns null for an empty file path, which silently suppresses every
            // location-guarded diagnostic — always parse with a path.
            CSharpSyntaxTree.ParseText(entitySource, path: "TestSource.cs"),
        };

        return CSharpCompilation.Create(
            "TraitE2ETestAssembly",
            trees,
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));
    }

    private static MetadataReference[] BuildReferences()
    {
        var refs = new List<MetadataReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? location)
        {
            if (string.IsNullOrEmpty(location)) return;
            if (!seen.Add(location!)) return;
            try { refs.Add(MetadataReference.CreateFromFile(location!)); }
            catch { /* skip native / unreadable */ }
        }

        // Every managed assembly in the test output directory: BCL implementation
        // assemblies (System.*, netstandard) + all referenced Pragmatic.* runtime packages.
        var baseDir = AppContext.BaseDirectory;
        foreach (var dll in Directory.EnumerateFiles(baseDir, "*.dll"))
        {
            var name = Path.GetFileNameWithoutExtension(dll);
            if (name == "Pragmatic.SourceGenerator") continue; // the analyzer itself
            Add(dll);
        }

        // Belt-and-suspenders: ensure the trait runtime + key dependency assemblies are
        // present even if directory enumeration missed them.
        Add(typeof(Pragmatic.Tags.HasTagsAttribute).Assembly.Location);
        Add(typeof(Pragmatic.Tags.TagBase).Assembly.Location);
        Add(typeof(Pragmatic.Notes.NoteBase<>).Assembly.Location);
        Add(typeof(Pragmatic.Comments.HasCommentsAttribute).Assembly.Location);
        Add(typeof(Pragmatic.Attachments.HasAttachmentsAttribute).Assembly.Location);
        Add(typeof(Pragmatic.Storage.IFileStorage).Assembly.Location);
        // System.Data.Common: the generated add/upload actions open a transaction with an explicit
        // IsolationLevel and inspect DbException.SqlState. Both types are type-forwarded, so the
        // facade in the output directory is not enough — without this the harness fails to compile
        // code that every real consumer builds fine, exactly as it once did for System.Net.Http.
        Add(typeof(System.Data.IsolationLevel).Assembly.Location);
        Add(typeof(System.Data.Common.DbException).Assembly.Location);
        Add(typeof(Pragmatic.Persistence.Entity.IEntity).Assembly.Location);
        Add(typeof(Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute).Assembly.Location);
        Add(typeof(object).Assembly.Location);

        // Core BCL implementation assemblies from the shared runtime dir. Some of these are
        // not copied to the test output dir, but the generated code (and the global usings)
        // bind against them. System.ComponentModel.TypeConverter holds IListSource, surfaced
        // transitively by EF Core types in generated repository code.
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (var bcl in new[]
                 {
                     "System.Runtime.dll", "netstandard.dll", "System.Collections.dll",
                     "System.Linq.dll", "System.Linq.Expressions.dll", "System.Linq.Queryable.dll",
                     "System.Threading.dll", "System.Threading.Tasks.dll",
                     "System.ComponentModel.dll", "System.ComponentModel.TypeConverter.dll",
                     "System.Private.CoreLib.dll", "System.Private.Uri.dll",
                     "System.Diagnostics.DiagnosticSource.dll",
                     "System.Memory.dll", "System.ObjectModel.dll",
                     // Generated attachment upload code binds Stream / Path.
                     "System.IO.dll", "System.Runtime.Extensions.dll",
                     // Generated endpoint auth now emits the permission fallback (ctx.User → ClaimsPrincipal)
                     // when Identity.AspNetCore is absent, so the compilation needs System.Security.Claims.
                     "System.Security.Claims.dll",
                     // Every real project has HttpClient, and the boundary generator SKIPS the remote
                     // implementation when it cannot find it — without this the harness silently covers
                     // only half of what a consumer compiles.
                     "System.Net.Http.dll", "System.Net.Primitives.dll",
                     // …and the remote implementation posts through PostAsJsonAsync: without this a
                     // test compiling the facade finds errors every real consumer does not have.
                     "System.Net.Http.Json.dll",
                     // Generated endpoints read their body through a JsonTypeInfo — ASP.NET cannot
                     // bind a generated handler under AOT, so the binding is ours and it is typed.
                     "System.Text.Json.dll",
                     // The purge job logs through generated call sites, whose state pre-encodes its
                     // property names with JsonEncodedText.Encode — an overload that names JavaScriptEncoder.
                     "System.Text.Encodings.Web.dll",
                 })
        {
            Add(Path.Combine(runtimeDir, bcl));
        }

        // ASP.NET Core shared framework — the generated trait endpoint + DI-registration code
        // references Microsoft.AspNetCore.* and Microsoft.Extensions.*. The test host already
        // loads the AspNetCore.App framework (see runtimeconfig), so harvest those assemblies
        // straight from the current AppDomain — robust regardless of install layout.
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var n = asm.GetName().Name;
            if (n is null) continue;
            if (n.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
                || n.StartsWith("Microsoft.Extensions", StringComparison.Ordinal))
            {
                try { Add(asm.Location); } catch { /* dynamic/in-memory asm */ }
            }
        }

        // Belt-and-suspenders: also pull from the shared framework dir on disk.
        AddAspNetCoreFramework(Add, runtimeDir);

        return refs.ToArray();
    }

    private static void AddAspNetCoreFramework(Action<string?> add, string runtimeDir)
    {
        var major = Environment.Version.Major;

        // Candidate "shared" framework roots — the one next to the runtime that loaded us, plus
        // the well-known dotnet install locations (the test host may run from a private runtime
        // whose sibling has no AspNetCore.App).
        var candidates = new List<string?>
        {
            Path.GetDirectoryName(Path.GetDirectoryName(runtimeDir)),
            Path.Combine(Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? string.Empty, "shared"),
            Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles") ?? @"C:\Program Files", "dotnet", "shared"),
        };

        string? versionDir = null;
        foreach (var shared in candidates)
        {
            if (string.IsNullOrEmpty(shared)) continue;
            var aspNetRoot = Path.Combine(shared!, "Microsoft.AspNetCore.App");
            if (!Directory.Exists(aspNetRoot)) continue;

            versionDir = Directory.EnumerateDirectories(aspNetRoot)
                .Where(d => Path.GetFileName(d).StartsWith(major + ".", StringComparison.Ordinal))
                .OrderByDescending(d => d)
                .FirstOrDefault()
                ?? Directory.EnumerateDirectories(aspNetRoot).OrderByDescending(d => d).FirstOrDefault();

            if (versionDir is not null) break;
        }

        if (versionDir is null) return;

        // The ASP.NET Core shared framework dir also carries Microsoft.Extensions.* (DI,
        // hosting, etc.) which the generated DI-registration code binds against.
        foreach (var dll in Directory.EnumerateFiles(versionDir, "Microsoft.AspNetCore*.dll"))
            add(dll);
        foreach (var dll in Directory.EnumerateFiles(versionDir, "Microsoft.Extensions*.dll"))
            add(dll);
    }
}
