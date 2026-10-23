namespace Pragmatic.Persistence.Sql.Generator.Models;

/// <summary>
///     A plan class of the generated file. <see cref="ClassName" /> is what <c>PlanRegistry</c> names; a plan over an anonymous row type is
///     generic and stays out of it (<see cref="Registered" /> false): <c>SqlSession.WarmupAsync</c> cannot build it without its type.
/// </summary>
internal abstract record PlanSectionModel(string ClassName, bool Registered);
