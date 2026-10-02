using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Persistence.EFCore.Samples.Temporal;

/// <summary>
///     Demonstrates the generated <c>[TemporalRelation&lt;Department&gt;]</c> query extensions on
///     <see cref="EmployeeAssignment"/>: <c>Active()</c>, <c>ActiveAt(date)</c>,
///     <c>ForDepartment(id)</c>, <c>ActiveForDepartment(id)</c>, and <c>IncludeHistory()</c>.
///     A null <c>ValidTo</c> means the relation is currently open.
/// </summary>
public static class TemporalRelationSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══ Temporal Relation ([TemporalRelation<Department>]) ═══");
        Console.WriteLine();

        var options = new DbContextOptionsBuilder<TemporalDbContext>()
            .UseInMemoryDatabase($"Temporal_{Guid.NewGuid():N}")
            .Options;

        await using var db = new TemporalDbContext(options);

        var engineering = new Department { Name = "Engineering" };
        var sales = new Department { Name = "Sales" };
        db.Departments.AddRange(engineering, sales);

        var now = DateTimeOffset.UtcNow;

        // Engineering: Alice was head last year (closed), Bob is head now (open).
        var alicePast = Assign("alice", engineering.Id, "Head", now.AddYears(-1), now.AddMonths(-1));
        var bobNow = Assign("bob", engineering.Id, "Head", now.AddMonths(-1), null);
        // Sales: Carol is head now (open).
        var carolNow = Assign("carol", sales.Id, "Head", now.AddDays(-10), null);

        db.Assignments.AddRange(alicePast, bobNow, carolNow);
        await db.SaveChangesAsync();

        // ── Active() — only currently-open relations ──
        var active = await db.Assignments.Active().ToListAsync();
        Console.WriteLine($"  Active() across all depts   : {active.Count} (expects 2 — Bob, Carol)");

        // ── ActiveAt(date) — point-in-time view (6 months ago Alice was still head) ──
        var sixMonthsAgo = await db.Assignments.ActiveAt(now.AddMonths(-6)).ToListAsync();
        Console.WriteLine($"  ActiveAt(-6 months)         : {sixMonthsAgo.Count} (expects 1 — Alice)");

        // ── ForDepartment(id) — all history for one parent ──
        var engHistory = await db.Assignments.ForDepartment(engineering.Id).ToListAsync();
        Console.WriteLine($"  ForDepartment(Engineering)  : {engHistory.Count} (expects 2 — Alice + Bob)");

        // ── ActiveForDepartment(id) — current head of a parent ──
        var engHead = await db.Assignments.ActiveForDepartment(engineering.Id).ToListAsync();
        Console.WriteLine($"  ActiveForDepartment(Eng)    : {engHead.Count} active (current head = employee {engHead.FirstOrDefault()?.EmployeeId})");
        Console.WriteLine();

        // GenerateTimeline note: [GenerateTimeline] additionally emits a GetTimeline() CTE
        // (LAG/LEAD over ValidFrom/ValidTo) for gap/overlap reporting — relational-provider only,
        // so it is omitted from this in-memory walkthrough.
        Console.WriteLine("  ([GenerateTimeline] adds a GetTimeline() CTE — relational provider only.)");
        Console.WriteLine();
    }

    private static EmployeeAssignment Assign(
        string employee, Guid departmentId, string role, DateTimeOffset from, DateTimeOffset? to)
    {
        var assignment = new EmployeeAssignment
        {
            // Deterministic GUID from the employee label so the output is readable.
            EmployeeId = new Guid(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(employee))),
            Role = role,
            ValidFrom = from,
            ValidTo = to
        };
        assignment.SetDepartmentId(departmentId);
        return assignment;
    }
}
