using Pragmatic.Jobs.Attributes;

namespace Invoicing.Billing;

/// <summary>
///     The documents: an invoice, its lines, the payments recorded against it.
/// </summary>
/// <remarks>
///     <c>[EnableJobPersistence]</c> puts the durable job store's tables — <c>__Jobs</c> and
///     <c>__RecurringJobs</c> — in this boundary's database, so the reminder sweep survives a restart and
///     two hosts cannot both run the same occurrence. The tables live here because this is where the job
///     is: the sweep chases the invoices. <c>OnModelCreating</c> is generated, so this attribute is the
///     way to ask for them without hand-writing a second <c>DbContext</c> over the same database.
/// </remarks>
[Boundary]
[EnableJobPersistence]
public partial class BillingBoundary;
