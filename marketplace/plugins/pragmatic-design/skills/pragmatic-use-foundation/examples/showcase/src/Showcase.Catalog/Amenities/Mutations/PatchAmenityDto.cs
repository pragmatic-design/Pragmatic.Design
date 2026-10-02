using Pragmatic.Patch.Attributes;

namespace Showcase.Catalog.Amenities.Mutations;

/// <summary>
///     Patch DTO for Amenity — supports partial updates.
///     Demonstrates [GeneratePatch]: SG generates Optional{T} properties,
///     ApplyTo(entity), ModifiedProperties, and a JSON converter.
/// </summary>
/// <remarks>
///     ⚠️ <c>Name</c> is left out on purpose. It is the amenity's <c>[LogicKey]</c> — what the
///     catalogue is keyed on and what <c>[Autocomplete]</c> resolves against — so renaming one is a
///     decision, not a field somebody corrects in passing. The full update
///     (<see cref="UpdateAmenityMutation" />) still carries it, which is where that decision is
///     made; the patch shape is where it must not be possible by accident.
///     <para>
///         The infrastructure columns — id, audit, soft delete, tenant, owner and scope — are
///         already excluded by the generator, because a patch that reached them would be a
///         mass-assignment hole. <c>[PatchIgnore]</c> is where a <b>domain</b> invariant says the
///         same thing, and the name is given with <c>nameof</c> so a rename that forgets this line
///         does not compile.
///     </para>
/// </remarks>
[GeneratePatch<Amenity>]
[PatchIgnore(nameof(Amenity.Name))]
public partial record PatchAmenityDto;
