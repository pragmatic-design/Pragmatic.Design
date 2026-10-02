using Pragmatic.Messaging.Attributes;
using Pragmatic.Notifications.Attributes;
using Showcase.Accounts.Entities;
using Showcase.Catalog.Entities;

namespace Showcase.Booking;

/// <summary>
/// Boundary for booking-related domain actions and mutations.
/// All types under Showcase.Booking.* are captured by namespace prefix matching.
///
/// ReadAccess declarations: Booking reads Catalog entities via SQL join
/// (valid only when both boundaries share the same database — see ShowcaseHostModule).
/// </summary>
[Boundary]
[ReadAccess<Property>]
[ReadAccess<RoomType>]
// ⚠️ The three below are not read by any Booking operation: they are what Property and RoomType
// navigate to, and [ReadAccess] brings the owner's configuration navigations included. A target the
// reader does not have is dropped from the model, and the first query naming it fails at run time —
// PRAG0639, which reported these four the moment it became a warning.
[ReadAccess<Category>]
[ReadAccess<Amenity>]
[ReadAccess<CancellationPolicy>]
// ⚠️ And the join entity, which is the cascade's second step. Reading Amenity brings its
// many-to-many with RoomType, and a skip navigation without its join entity is a model EF refuses to
// build at all: "The skip navigation 'Amenity.RoomTypes' doesn't have a foreign key associated with
// it" — every request 500, not just the ones that name it.
[ReadAccess<RoomTypeAmenity>]
// The staff member behind a StaffAssignment.StaffId. Booking has no navigation to a user and cannot
// have one — the id is written from a token, not from a relation — so GetStaffAssignmentsQuery
// reaches it with [Join<AppUser>(ForeignKey = "StaffId")]. ⚠️ This declaration is what makes that
// join possible: EF Core composes a join only inside one DbContext instance, and [ReadAccess] is what
// puts AppUser in this boundary's model.
[ReadAccess<AppUser>]
[EnableSagaPersistence] // CheckInSaga state is persisted in BookingDbContext (__SagaInstances/__SagaSteps).
// Notification records (__Notifications) live here: this boundary is the one that sends them — every
// confirmation and cancellation the guest receives is raised by a Booking operation.
//
// ⚠️ It took two halves. The attribute maps the table into this boundary's generated DbContext, and
// the schema an application creates comes from the MIGRATION context, whose tables are hand-mirrored
// per package in SchemaMetadataTransform — so the first half alone still failed with
// `relation "__Notifications" does not exist`, measured on this suite.
[StoresNotifications]
public partial class BookingBoundary;
