using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     Which class carries <c>[Attachable&lt;T&gt;]</c>, and which way the type argument points.
/// </summary>
/// <remarks>
///     <para>
///         The pair is declared on the <b>attachment</b>: <c>[PolymorphicAttachment]</c> says «my rows
///         belong to owners of more than one type», and each <c>[Attachable&lt;TOwner&gt;]</c> beside it
///         names one of those owner types. What comes out lands on the <b>owner</b> — extension methods
///         to query and batch-load the attachments of an instance.
///     </para>
///     <para>
///         ⚠️ Two readings of it are wrong, in different ways. «Marks an entity as being able to
///         <em>own</em> polymorphic attachments of type TAttachment» has the two roles swapped: the
///         marked class is the attachment and the argument is the owner. And «the entity can be
///         attached to the context without reloading it» is a different feature altogether, one this
///         attribute does not have.
///     </para>
///     <para>
///         Neither is catchable by a check: the identifier exists, so a ratchet that verifies names
///         finds it. What it cannot verify is whether the sentence around the name is about the same
///         thing.
///     </para>
/// </remarks>
public class TheAttachmentNamesItsOwnersTests
{
    private static string Source(string onDocument, string onInvoice) => $$"""
        using System;
        using Pragmatic.Persistence.Entity;

        namespace Sales
        {
            [Entity]
            {{onInvoice}}
            public partial class Invoice : IEntity
            {
                public Guid PersistenceId { get; set; }
                public string Number { get; private set; } = "";
            }

            [Entity]
            {{onDocument}}
            public partial class Document : IEntity
            {
                public Guid PersistenceId { get; set; }
                public string FileName { get; private set; } = "";
            }
        }
        """;

    private static SourceGenRunResult Run(string onDocument, string onInvoice = "")
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Source(onDocument, onInvoice), [
            GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.IEntity>(),
            GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.PolymorphicAttachmentAttribute>(),
            GeneratorTestHelper.FromType<Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Specification.Spec<>)),
        ]);

    /// <summary>The pair, declared the way it is meant to be, reaches the owner.</summary>
    [Fact]
    public void TheAttachmentDeclaresItsOwners_AndTheOwnerGetsTheNavigation()
    {
        var generated = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(
            Run("[PolymorphicAttachment]\n    [Attachable<Invoice>]"));

        var navigation = generated.FirstOrDefault(kv => kv.Key.Contains("DocumentNavigation")).Value;
        navigation.Should().NotBeNull("the owner gets the way in to its attachments");
        navigation.Should().Contain("this global::Sales.Invoice owner",
            "the extension hangs off the owner, which is the type argument");
        navigation.Should().Contain("QueryDocuments(");
        navigation.Should().Contain("a.OwnerType == \"Sales.Invoice\"",
            "and the rows are found by the owner's type name, which is what makes it polymorphic");
    }

    /// <summary>
    ///     The control that pins the direction: the attribute read the other way round produces nothing.
    /// </summary>
    /// <remarks>
    ///     This is the reading the attribute's own summary invited — «Invoice can own Documents», so
    ///     <c>[Attachable&lt;Document&gt;]</c> on <c>Invoice</c>. It generates nothing at all, silently,
    ///     which is exactly how a wrong sentence survives: the author gets no error, only no feature.
    /// </remarks>
    [Fact]
    public void DeclaredOnTheOwnerInstead_NothingIsGenerated()
    {
        var generated = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(
            Run("", "[Attachable<Document>]"));

        generated.Keys.Should().NotContain(k => k.Contains("Navigation"),
            "the transform starts from [PolymorphicAttachment] on the attachment: the other way round is not a declaration");
    }

    /// <summary>And the attachment itself gets the owner columns and the typed read.</summary>
    [Fact]
    public void TheAttachment_GetsItsOwnerColumnsAndATypedRead()
    {
        var generated = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(
            Run("[PolymorphicAttachment]\n    [Attachable<Invoice>]"));

        var attachment = generated.FirstOrDefault(kv => kv.Key.Contains("Document.PolymorphicAttachment")).Value
                         ?? generated.FirstOrDefault(kv => kv.Key.Contains("Document") && kv.Value.Contains("ForOwner")).Value;

        attachment.Should().NotBeNull();
        attachment.Should().Contain("ForOwner<global::Sales.Invoice>()",
            "the typed read is the half that lives on the attachment");
    }

    /// <summary>The second control: without the pair, none of it appears.</summary>
    [Fact]
    public void WithoutTheDeclaration_NeitherHalfIsGenerated()
    {
        var generated = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(Run(""));

        generated.Values.Should().NotContain(v => v.Contains("ForOwner"));
        generated.Keys.Should().NotContain(k => k.Contains("Navigation"));
    }
}
