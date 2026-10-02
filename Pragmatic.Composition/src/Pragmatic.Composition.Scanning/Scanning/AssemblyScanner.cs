using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyModel;
using Microsoft.Extensions.Logging;
using Pragmatic.Telemetry.Conventions;

namespace Pragmatic.Composition.Scanning;

internal sealed partial class AssemblyScanner : IAssemblyScanner
{
    private static readonly ActivitySource ActivitySource = new("Pragmatic.Composition", "1.0.0");
    private readonly List<Assembly> _assemblies = [];
    private readonly ILogger<AssemblyScanner>? _logger;
    private readonly Action<string, Exception?>? _onScanError;
    private readonly IServiceCollection _services;

    internal AssemblyScanner(
        IServiceCollection services,
        Action<string, Exception?>? onScanError = null,
        ILogger<AssemblyScanner>? logger = null)
    {
        _services = services;
        _onScanError = onScanError;
        _logger = logger;
    }

    public IAssemblyScanner FromAssemblyOf<T>()
    {
        _assemblies.Add(typeof(T).Assembly);
        return this;
    }

    public IAssemblyScanner FromAssemblies(params Assembly[] assemblies)
    {
        _assemblies.AddRange(assemblies);
        return this;
    }

    [global::System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
        "Matching assemblies by file pattern loads them at run time; the trimmer cannot follow.")]
    public IAssemblyScanner FromAssembliesMatching(string pattern)
    {
        using var activity = ActivitySource.StartActivity("composition.scan.assemblies_matching");
        activity?.SetTag(CompositionTags.Pattern, pattern);

        // Glob → regex: escape EVERY regex metacharacter (otherwise '+ ( ) [ ] { } ? | ^ $' in a
        // pattern would be interpreted as regex), then turn the escaped '\*' wildcard back into '.*'.
        // '*' stays the only wildcard, everything else is literal.
        var regexBody = Regex.Escape(pattern).Replace("\\*", ".*");
        var regex = new Regex("^" + regexBody + "$", RegexOptions.IgnoreCase);

        var baseDir = AppContext.BaseDirectory;
        if (_logger is not null)
            LogScanningAssemblies(_logger, pattern, baseDir);

        // Security: reject overly broad patterns that match everything
        if (pattern is "*" or ".*" or "*.*")
        {
            if (_logger is not null)
                _logger.LogWarning("Assembly scan pattern '{Pattern}' rejected as too broad", pattern);
            throw new InvalidOperationException($"Assembly scanning pattern '{pattern}' is too broad. Use a specific prefix like 'MyApp.*' or 'Pragmatic.*'.");
        }

        var dllFiles = Directory.GetFiles(baseDir, "*.dll")
            .Where(f => regex.IsMatch(Path.GetFileNameWithoutExtension(f)))
            .ToList();

        if (_logger is not null)
            LogMatchedFiles(_logger, dllFiles.Count, pattern);
        activity?.SetTag(CompositionTags.MatchedFiles, dllFiles.Count);

        var assemblies = dllFiles
            .Select(TryLoadAssembly)
            .Where(a => a is not null)
            .Cast<Assembly>()
            .ToList();

        if (_logger is not null)
            LogAssembliesLoaded(_logger, assemblies.Count, pattern);
        activity?.SetTag(CompositionTags.LoadedAssemblies, assemblies.Count);

        _assemblies.AddRange(assemblies);
        return this;
    }

    public IAssemblyScanner FromCallingAssembly()
    {
        _assemblies.Add(Assembly.GetCallingAssembly());
        return this;
    }

    public IAssemblyScanner FromEntryAssembly()
    {
        var entry = Assembly.GetEntryAssembly();
        if (entry is not null)
            _assemblies.Add(entry);
        return this;
    }

    public IAssemblyScanner FromDependencyContext(Func<string, bool>? predicate = null)
    {
        using var activity = ActivitySource.StartActivity("composition.scan.dependency_context");

        var context = DependencyContext.Default;
        if (context is null)
        {
            if (_logger is not null)
                LogDependencyContextNull(_logger);
            return this;
        }

        var libraries = context.RuntimeLibraries
            .Where(lib => lib.Type == "project" || predicate?.Invoke(lib.Name) == true)
            .ToList();

        if (_logger is not null)
            LogLibrariesFound(_logger, libraries.Count);
        activity?.SetTag(CompositionTags.LibraryCount, libraries.Count);

        var loadedCount = 0;
        foreach (var library in libraries)
            foreach (var assemblyName in library.GetDefaultAssemblyNames(context))
            {
                var assembly = TryLoadAssemblyByName(assemblyName);
                if (assembly is not null)
                {
                    _assemblies.Add(assembly);
                    loadedCount++;
                }
            }

        if (_logger is not null)
            LogAssembliesFromContext(_logger, loadedCount);
        activity?.SetTag(CompositionTags.LoadedAssemblies, loadedCount);

        return this;
    }

    public IAssemblyScanner FromDependencyContext(string prefix)
    {
        return FromDependencyContext(name =>
            name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <remarks>
    ///     Declared incompatible with trimming and AOT rather than worked around. Discovering types by
    ///     walking assemblies is the one thing a source generator cannot replace: the whole point is to
    ///     find what was not named at compile time, which is exactly what a trimmer needs named. Every
    ///     registration Pragmatic emits is static; this is the explicit opt-out for an app that wants
    ///     the other behaviour, and it should say so at the call site.
    /// </remarks>
    [global::System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
        "Assembly scanning discovers types at run time; the trimmer cannot know which ones to keep. Register services explicitly, or use the generated registrations.")]
    public ITypeSelector AddClasses(Action<ITypeFilter>? filter = null)
    {
        var types = _assemblies
            .SelectMany(GetExportedTypes)
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false });

        var typeFilter = new TypeFilter(types);
        filter?.Invoke(typeFilter);

        return new TypeSelector(_services, typeFilter.GetFilteredTypes());
    }

    public ITypeSelector AddType<T>() where T : class
        => new TypeSelector(_services, [typeof(T)]);

    public ITypeSelector AddTypes<T1, T2>() where T1 : class where T2 : class
        => new TypeSelector(_services, [typeof(T1), typeof(T2)]);

    public ITypeSelector AddTypes<T1, T2, T3>() where T1 : class where T2 : class where T3 : class
        => new TypeSelector(_services, [typeof(T1), typeof(T2), typeof(T3)]);

    public ITypeSelector AddTypes(params Type[] types)
    {
        var validTypes = types.Where(t => t is { IsClass: true, IsAbstract: false });
        return new TypeSelector(_services, validTypes);
    }

    [global::System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
        "Loading an assembly by path brings in types the trimmer never saw.")]
    private Assembly? TryLoadAssembly(string path)
    {
        try
        {
            var assembly = Assembly.LoadFrom(path);
            if (_logger is not null)
                LogAssemblyLoadedFromPath(_logger, path);
            return assembly;
        }
        catch (Exception ex)
        {
            if (_logger is not null)
                LogAssemblyLoadFailed(_logger, path, ex);
            _onScanError?.Invoke($"Failed to load assembly from '{path}'", ex);
            return null;
        }
    }

    private Assembly? TryLoadAssemblyByName(AssemblyName assemblyName)
    {
        try
        {
            var assembly = Assembly.Load(assemblyName);
            if (_logger is not null)
                LogAssemblyLoadedByName(_logger, assemblyName.FullName);
            return assembly;
        }
        catch (Exception ex)
        {
            if (_logger is not null)
                LogAssemblyLoadByNameFailed(_logger, assemblyName.FullName, ex);
            _onScanError?.Invoke($"Failed to load assembly '{assemblyName.FullName}'", ex);
            return null;
        }
    }

    [global::System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
        "Enumerating an assembly's exported types is the discovery a trimmer cannot follow.")]
    private IEnumerable<Type> GetExportedTypes(Assembly assembly)
    {
        try
        {
            var types = assembly.GetExportedTypes();
            if (_logger is not null)
                LogTypesExtracted(_logger, types.Length, assembly.GetName().Name);
            return types;
        }
        catch (Exception ex)
        {
            if (_logger is not null)
                LogGetTypesFailed(_logger, assembly.FullName, ex);
            _onScanError?.Invoke($"Failed to get exported types from '{assembly.FullName}'", ex);
            return [];
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Scanning for assemblies matching '{Pattern}' in {BaseDir}")]
    private static partial void LogScanningAssemblies(ILogger logger, string pattern, string baseDir);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Found {Count} DLL files matching pattern '{Pattern}'")]
    private static partial void LogMatchedFiles(ILogger logger, int count, string pattern);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded {LoadedCount} assemblies matching pattern '{Pattern}'")]
    private static partial void LogAssembliesLoaded(ILogger logger, int loadedCount, string pattern);

    [LoggerMessage(Level = LogLevel.Debug, Message = "DependencyContext.Default is null, skipping dependency context scan")]
    private static partial void LogDependencyContextNull(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Found {LibraryCount} libraries in dependency context")]
    private static partial void LogLibrariesFound(ILogger logger, int libraryCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded {LoadedCount} assemblies from dependency context")]
    private static partial void LogAssembliesFromContext(ILogger logger, int loadedCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Loaded assembly from path: {Path}")]
    private static partial void LogAssemblyLoadedFromPath(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to load assembly from '{Path}'")]
    private static partial void LogAssemblyLoadFailed(ILogger logger, string path, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Loaded assembly by name: {AssemblyName}")]
    private static partial void LogAssemblyLoadedByName(ILogger logger, string? assemblyName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to load assembly '{AssemblyName}'")]
    private static partial void LogAssemblyLoadByNameFailed(ILogger logger, string? assemblyName, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Extracted {TypeCount} types from assembly: {AssemblyName}")]
    private static partial void LogTypesExtracted(ILogger logger, int typeCount, string? assemblyName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to get exported types from assembly '{AssemblyName}'")]
    private static partial void LogGetTypesFailed(ILogger logger, string? assemblyName, Exception ex);
}
