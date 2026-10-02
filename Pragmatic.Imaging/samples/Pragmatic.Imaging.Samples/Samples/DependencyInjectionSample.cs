using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Imaging;
using Pragmatic.Imaging.Extensions;

namespace Pragmatic.Imaging.Samples.Samples;

/// <summary>
///     Wiring Pragmatic.Imaging into a DI container with <c>AddPragmaticImaging</c>.
///     This registers an <see cref="ImagingOptions"/> singleton (so app code can inject the
///     configured safety limits) and installs a safe native-library resolver that restricts
///     DLL search to application and system directories.
///     <para>
///     The sample uses only <c>Microsoft.Extensions.DependencyInjection.Abstractions</c>
///     (transitively available through Pragmatic.Imaging): it registers services and inspects
///     the resulting <see cref="IServiceCollection"/> descriptors, then uses the configured
///     options to drive a real Load + Encode — exactly what an injected service would do.
///     </para>
/// </summary>
public static class DependencyInjectionSample
{
    public static void Run(string outputDir)
    {
        Console.WriteLine("--- DI registration (AddPragmaticImaging) ---");

        // Register with the Strict preset — typical for an API that accepts user uploads.
        IServiceCollection services = new ServiceCollection();
        services.AddPragmaticImaging(ImagingOptions.Strict);

        // AddPragmaticImaging registers ImagingOptions as a singleton instance.
        // Pull it straight from the descriptor so we do not need the DI implementation package.
        var options = services
            .Where(d => d.ServiceType == typeof(ImagingOptions))
            .Select(d => d.ImplementationInstance)
            .OfType<ImagingOptions>()
            .Single();

        Console.WriteLine($"  registered services   {services.Count}");
        Console.WriteLine($"  resolved options      MaxInputBytes={options.MaxInputBytes / (1024 * 1024)} MB, MaxMegapixels={options.MaxMegapixels} MP");

        // Use the configured options exactly as a service class would after injection.
        var source = QrCode.GeneratePng("di-demo", moduleSize: 8);
        using var pipeline = ImagePipeline.Load(source, options);
        var encoded = pipeline.Encode(ImageFormat.Png);
        File.WriteAllBytes(Path.Combine(outputDir, "di-output.png"), encoded);
        Console.WriteLine($"  di-output.png         {encoded.Length} bytes (encoded using injected options)");

        // The default overload registers ImagingOptions.Default instead.
        IServiceCollection defaultServices = new ServiceCollection();
        defaultServices.AddPragmaticImaging();
        var defaultOptions = defaultServices
            .Where(d => d.ServiceType == typeof(ImagingOptions))
            .Select(d => d.ImplementationInstance)
            .OfType<ImagingOptions>()
            .Single();
        Console.WriteLine($"  default registration  MaxMegapixels={defaultOptions.MaxMegapixels} MP (ImagingOptions.Default)");

        Console.WriteLine();
    }
}
