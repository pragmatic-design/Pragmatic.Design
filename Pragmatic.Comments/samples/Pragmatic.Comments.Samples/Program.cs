using Pragmatic.Comments.Samples;

// ─────────────────────────────────────────────────────────────────────────────
// Pragmatic.Comments — runnable samples.
//
// Pragmatic.Comments is a TRAIT package. Its [HasComments] SOURCE GENERATOR IS
// implemented: in a real Pragmatic host it emits the typed {Parent}Comment
// entity, an EF Core configuration, the Add/Update/Delete actions, the REST
// endpoints and the permission constants for every entity decorated with
// [HasComments]. A plain console app does NOT run that host SG pipeline, so the
// samples below exercise only the runtime surface that runs WITHOUT the
// generator:
//   1. HasCommentsAttribute        — options / defaults
//   2. CommentBase<Guid> subclass  — entity shape + CommentStatus/CommentVisibility
//   3. ICommentPolicy<Guid>        — default-interface-method hooks + a custom policy
// ─────────────────────────────────────────────────────────────────────────────

Console.WriteLine("Pragmatic.Comments Samples");
Console.WriteLine("==========================");
Console.WriteLine("(The [HasComments] SG runs in a real Pragmatic host; this console");
Console.WriteLine(" app demos only the runtime types that execute without it.)");
Console.WriteLine();

HasCommentsOptionsSample.Run();
CommentEntitySample.Run();
await CommentPolicySample.RunAsync();

Console.WriteLine("All samples completed.");
