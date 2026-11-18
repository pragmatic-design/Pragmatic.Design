using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Templates;

/// <summary>
///     The patch converter reads a property's metadata in the form the consumer's System.Text.Json has: the cast on 10, the typed
///     <c>GetTypeInfo&lt;T&gt;()</c> on 11 (#838).
/// </summary>
/// <remarks>
///     <see cref="Generator.PatchJsonConverterTests" /> runs the net10.0 form; the net11.0 form calls an API this suite's runtime does
///     not have, so it is asserted on the template's output, and the same call is compiled and run on .NET 11 by the composition host
///     suite (<c>TheInvokeEndpointReadsTypedMetadataOnElevenTests</c>).
/// </remarks>
public class PatchJsonConverterTemplateTests
{
    [Theory]
    [InlineData(false, "=> options.GetTypeInfo(typeof(T)) as global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>", "options.GetTypeInfo<T>()")]
    [InlineData(true, "=> options.GetTypeInfo<T>();", "GetTypeInfo(typeof(T))")]
    public void TheMetadataLookup_IsInTheFormTheConsumerHas(bool typedTypeInfo, string written, string notWritten)
    {
        var model = new MutationMetadataModel
        {
            Namespace = "MyApp.DTOs",
            TypeName = "UpdateOrder",
            Accessibility = "public",
            EntityFullTypeName = "MyApp.Entities.Order",
            EntityTypeName = "Order",
            EntityIdType = "System.Guid",
            Properties =
            [
                new MutationPropertyModel
                {
                    PropertyName = "Name",
                    PropertyType = "string?",
                    TargetPropertyName = "Name",
                    EntityPropertyExists = true,
                    IsNullable = true
                }
            ]
        };

        var source = new PatchJsonConverterTemplate(model, typedTypeInfo).RenderOutput().Text;

        source.Should().Contain(written);
        source.Should().NotContain(notWritten);
    }
}
