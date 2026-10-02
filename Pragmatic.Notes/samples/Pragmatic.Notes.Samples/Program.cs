using Pragmatic.Notes.Samples.Samples;

Console.WriteLine("=== Pragmatic.Notes Samples ===");
Console.WriteLine("(The [HasNotes<T>] generator runs in a consuming project — see the Showcase.)");
Console.WriteLine("Only the HasNotes options attribute and the NoteBase<TEntityId> base entity are runnable.\n");

HasNotesOptionsSample.Run();
Console.WriteLine();
NoteBaseEntitySample.Run();
Console.WriteLine();

Console.WriteLine("=== All samples completed ===");
