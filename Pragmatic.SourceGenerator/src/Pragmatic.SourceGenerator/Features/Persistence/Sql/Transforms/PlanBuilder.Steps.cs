using Pragmatic.Persistence.Sql;
using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>The operators before the terminal, one at a time: each adds its predicate, ordering, grouping, projection, include or paging to the plan.</summary>
internal sealed partial class PlanBuilder
{
    /// <summary>One operator before the terminal. False when it was reported at its own call site (nothing to report here), or with <see cref="StepError" /> set.</summary>
    public bool AddStep(SqlInterceptorGenerator.Step step, SemanticModel model) => SetStep(step, model) ?? ChainStep(step, model);

    /// <summary>An operator of one statement (a chain without set operations, or one member of them).</summary>
    private bool ChainStep(SqlInterceptorGenerator.Step step, SemanticModel model)
    {
        switch (step.Name)
        {
            case "Where":
                return step.Lambda is not null && Predicate(model, step.Lambda, null);

            case "OrderBy":
            case "OrderByDescending":
            case "ThenBy":
            case "ThenByDescending":
            {
                if (step.Lambda is null || !SqlInterceptorGenerator.TryParameters(model, step.Lambda, out var parameter, out var arg))
                    return false;
                if (arg is not null && _valuesInSlots)
                    return false; // a [Query] definition writes the one-parameter form
                // The argument form ((e, q) => Sql.CosineDistance(e.Vector, q), query): the value is a parameter of the ORDER BY, bound from the chain's slot.
                var argVariable = arg is null ? null : $"arg{_slot}";
                var translator = Bound(new LambdaTranslator(model, _entities, Row, parameter, _groupKey, arg, argVariable, inline: true) { InlineConstants = true }); // after GroupBy the key is an aggregate or g.Key
                var column = translator.ColumnOf(SqlInterceptorGenerator.Body(step.Lambda));
                if (column is null || (arg is null && translator.ParameterPaths.Count > 0))
                    return false;
                if (arg is not null)
                {
                    _binds.Add($"var {argVariable} = ({arg.Type.ToDisplayString(SqlInterceptorGenerator.Annotated)})q.Slot({_slot})!;");
                    column = BindValues(translator, column)[0];
                    _slot++;
                }
                else
                    column = Absorb(translator, column, _kinds.Count);
                var reset = !step.Name.StartsWith("Then", StringComparison.Ordinal);
                var descending = step.Name.EndsWith("Descending", StringComparison.Ordinal);
                if (reset)
                    _orderings.Clear();
                _orderings.Add(column + (descending ? " DESC" : ""));
                RecordOrdering(_groupKey is null ? translator.LastColumn : null, column, descending, reset);
                Merge(_joins, translator.Joins);
                return true;
            }

            case "After":
                return AddCursor(step, before: false);

            case "Before":
                return AddCursor(step, before: true);

            case "AsOf":
            {
                // The instant from the chain's slot, as the FOR SYSTEM_TIME parameter on SQL Server; the other dialects read the current rows (reported).
                int slot;
                if (_valuesInSlots)
                {
                    ValueSlots.Add(step.Invocation.ArgumentList.Arguments[0].ToString());
                    slot = ValueSlots.Count - 1;
                }
                else
                    slot = _slot++;
                _asOfParameter = "@p" + _kinds.Count;
                _binds.Add($"p[{_kinds.Count}].Value = global::Pragmatic.Persistence.Sql.Planning.SqlValue.ToDatabase(d, q.Slot({slot}));");
                _kinds.Add("DateTime");
                if (!_entity.Temporal) // [Temporal]: the history table SqlTemporal's triggers keep, read at the instant
                {
                    TerminalEmulations.Add(("AsOf", SqlDialect.Sqlite, $"SQLite has no system-versioned tables: the current rows are read (mark {_entity.Type.Name} [Temporal] for a history table with triggers)"));
                    TerminalEmulations.Add(("AsOf", SqlDialect.PostgreSql, $"PostgreSQL has no system-versioned tables: the current rows are read (mark {_entity.Type.Name} [Temporal] for a history table with triggers)"));
                    TerminalEmulations.Add(("AsOf", SqlDialect.MySql, $"MySQL has no system-versioned tables: the current rows are read (mark {_entity.Type.Name} [Temporal] for a history table with triggers)"));
                }
                return true;
            }

            case "OrderedBy":
            {
                // An ordering composed at runtime (SqlOrdering<T>): its mark in the ORDER BY, rendered from the slot when the statement runs.
                if (_rowOpen || _row is not (null or EntityRow) || _groupKey is not null || _orderKeys.Count > 0)
                {
                    StepError = "OrderedBy orders the entity's own rows, alone: put it before the joins, projections and groups, without another ordering or a cursor";
                    return false;
                }

                var slot = _valuesInSlots ? -1 : _slot++;
                if (_valuesInSlots)
                {
                    ValueSlots.Add(step.Invocation.ArgumentList.Arguments[0].ToString());
                    slot = ValueSlots.Count - 1;
                }

                _orderings.Add("{dyn:" + slot + "}");
                _predicateSlots.Add(slot);
                return true;
            }

            case "Matching":
            {
                // A predicate composed at runtime (SqlPredicate<T>): its mark in the predicates, rendered from the slot when the statement runs.
                if (_rowOpen || _row is not (null or EntityRow) || _groupKey is not null)
                {
                    StepError = "Matching reads the entity's own rows: put it before the joins, projections and groups";
                    return false;
                }

                int slot;
                if (_valuesInSlots)
                {
                    ValueSlots.Add(step.Invocation.ArgumentList.Arguments[0].ToString());
                    slot = ValueSlots.Count - 1;
                }
                else
                    slot = _slot++;
                _predicates.Add("{dyn:" + slot + "}");
                _predicateSlots.Add(slot);
                return true;
            }

            case "OfType":
            {
                // The chain narrowed to a derived type: the plan already starts from it (the call site roots the terminal there); before any join, so the row is the entity's.
                if (step.Method.TypeArguments.Length != 1 || step.Method.TypeArguments[0] is not INamedTypeSymbol narrowed || _entities(narrowed) is not { DiscriminatorPredicate: not null } derived)
                    return false; // reported at its own call site
                if (_rowOpen || _row is not (null or EntityRow))
                {
                    StepError = "OfType narrows the entity's rows: put it before the joins";
                    return false;
                }

                if (SymbolEqualityComparer.Default.Equals(narrowed, _entity.Type))
                    return true;
                if (!SymbolEqualityComparer.Default.Equals(narrowed.BaseType, _entity.Type))
                    return false; // reported at its own call site
                _entity = derived;
                _row = null;
                return true;
            }

            case "Join":
            case "LeftJoin":
                return AddJoin(step, model);

            case "Fragment":
                return AddFragment(step, model);

            case "GroupJoin":
                return AddGroupJoin(step, model);

            case "SelectMany":
                return AddSelectMany(step, model);

            case "Sql":
                return AddRaw(step, model);

            case "Function":
                return AddFunction(step, model);

            case "GroupBy":
            {
                if (step.Lambda is null || !SqlInterceptorGenerator.TryParameter(model, step.Lambda, out var parameter) || step.Method.TypeArguments.Length != 1)
                    return false;
                _groupKey = GroupKey.TryRead(model, step.Lambda, step.Method.TypeArguments[0], Bound(new LambdaTranslator(model, _entities, Row, parameter)), _joins, out var keyError);
                if (_groupKey is null && _rowOpen)
                    StepError = keyError;
                _rowOpen = false;
                return _groupKey is not null;
            }

            case "Select" when _groupKey is not null:
            {
                if (step.Lambda is null)
                    return false;
                var problems = new List<Diagnostic>();
                _projection = SqlInterceptorGenerator.Projection.TryGrouped(model, step.Lambda, step.Method, Row, _entities, _groupKey, problems, step.Invocation.GetLocation());
                if (_projection is null && problems.Count > 0 && _rowOpen)
                    StepError = problems[0].GetMessage(); // on a joined row the Select has no call site of its own to report at
                if (_projection is { Pieces.Count: > 0 })
                {
                    // The projection's own pieces (STRING_AGG per dialect) after the plan's, its tokens renumbered.
                    var offset = _dialectPieces.Count;
                    _dialectPieces.AddRange(_projection.Pieces);
                    for (var i = 0; i < _projection.Columns.Count; i++)
                        _projection.Columns[i] = PieceToken.Replace(_projection.Columns[i], m => "{d" + (offset + int.Parse(m.Groups[1].Value)) + "}");
                }

                return _projection is not null;
            }

            case "Select":
            {
                if (step.Lambda is null || !SqlInterceptorGenerator.TryParameter(model, step.Lambda, out var parameter))
                    return false;
                var translator = Bound(new LambdaTranslator(model, _entities, Row, parameter) { InlineConstants = true });
                _projection = SqlInterceptorGenerator.Projection.TryRead(model, step.Lambda, step.Method, translator, null, step.Invocation.GetLocation());
                Merge(_joins, translator.Joins);
                _rowOpen = false;
                if (_projection is null)
                    return false;
                if (translator.DialectPieces.Count > 0)
                {
                    // The pieces once, then every column's tokens renumbered by the same offset.
                    var offset = _dialectPieces.Count;
                    _projection.Columns[0] = Absorb(translator, _projection.Columns[0], _kinds.Count);
                    for (var i = 1; i < _projection.Columns.Count; i++)
                        _projection.Columns[i] = PieceToken.Replace(_projection.Columns[i], m => "{d" + (offset + int.Parse(m.Groups[1].Value)) + "}");
                }

                return true;
            }

            case "Project":
            {
                var dto = step.Method.TypeArguments.Length == 1 ? step.Method.TypeArguments[0] as INamedTypeSymbol : null;
                _projection = dto is null ? null : SqlInterceptorGenerator.Projection.TryDeclared(dto, _entity, _entities, _joins, null, step.Invocation.GetLocation(), _compilation);
                if (_projection is not null)
                    MarkDeclaredJoins(_entity); // a flattened member's navigation: its target's rules in the join
                return _projection is not null;
            }

            case "Include":
            {
                if (_rowOpen)
                    return Refuse("Include works on the entity's rows: put it before the join, or project the joined row's members");
                var hops = SqlInterceptorGenerator.IncludeHops(step.Invocation, model, _entity, _entities, out _);
                if (hops is null)
                    return false;
                AddIncludePath(hops);
                return true;
            }

            case "Skip":
                _hasSkip = true;
                SkipTexts.Add(step.Invocation.ArgumentList.Arguments.Count == 1 ? step.Invocation.ArgumentList.Arguments[0].ToString() : "0");
                return true;
            case "Take":
                _hasTake = true;
                TakeTexts.Add(step.Invocation.ArgumentList.Arguments.Count == 1 ? step.Invocation.ArgumentList.Arguments[0].ToString() : "0");
                return true;
            case "DistinctOn":
                return AddDistinctOn(step, model);

            case "With":
                return AddWith(step, model);

            case "ForUpdate":
            {
                // The mode is a constant of the call (the default Wait when omitted); SQLite has no row locks: said once per plan (PRAG3012).
                var argument = step.Invocation.ArgumentList.Arguments.Count > 0 ? step.Invocation.ArgumentList.Arguments[0].Expression : null;
                var constant = argument is null ? default : model.GetConstantValue(argument);
                if (argument is not null && !(constant.HasValue && constant.Value is int))
                {
                    StepError = "ForUpdate takes a constant SqlLockMode (Wait, SkipLocked, NoWait)";
                    return false;
                }

                _lock = constant.HasValue && constant.Value is int mode ? mode : 0;
                TerminalEmulations.Add(("ForUpdate", SqlDialect.Sqlite, "SQLite locks the database as a whole: FOR UPDATE, SKIP LOCKED and NOWAIT add nothing there"));
                return true;
            }

            case "WithoutFilters":
                _withoutFilters = true;
                return true;
            case "IncludeDeleted":
                if (_entity.SoftDeleteColumn is null)
                {
                    StepError = $"{_entity.Type.Name} is not [SoftDelete]: IncludeDeleted() reads its deleted rows";
                    return false;
                }

                _includeDeleted = true;
                return true;
            case "WithoutFilter":
            {
                // One rule left out (Showcase's [WithoutFilter<ActiveOnly>]): the entity's other rules stay.
                if (model.GetSymbolInfo(step.Invocation).Symbol is not IMethodSymbol { TypeArguments.Length: 1 } without || without.TypeArguments[0] is not INamedTypeSymbol rule)
                    return false;
                if (!_entity.Rules.Exists(r => SymbolEqualityComparer.Default.Equals(r, rule)))
                {
                    StepError = $"{rule.Name} is not a [VisibleWhen] rule of {_entity.Type.Name}: WithoutFilter<TRule>() leaves out one of the entity's rules";
                    return false;
                }

                _withoutRules.Add(rule);
                return true;
            }
            case "ForOwner":
            {
                // The repository's [PolymorphicAttachment]: OwnerType holds typeof(TOwner).FullName (namespace, nested types after '+').
                if (model.GetSymbolInfo(step.Invocation).Symbol is not IMethodSymbol { TypeArguments.Length: 1 } forOwner || forOwner.TypeArguments[0] is not INamedTypeSymbol owner)
                    return false;
                if (_entity.Column("OwnerType") is not { } ownerType)
                {
                    StepError = $"ForOwner<{owner.Name}>() filters an attachment ([PolymorphicAttachment]): {_entity.Type.Name} has no OwnerType";
                    return false;
                }

                var name = owner.Name;
                for (var outer = owner.ContainingType; outer is not null; outer = outer.ContainingType)
                    name = outer.Name + "+" + name;
                if (!owner.ContainingNamespace.IsGlobalNamespace)
                    name = owner.ContainingNamespace.ToDisplayString() + "." + name;
                _predicates.Add($"{_alias}.\"{ownerType.Column}\" = {LambdaTranslator.SqlLiteral(name)}");
                return true;
            }

            case "AsNoTracking":
                return true;
            default:
                return false;
        }
    }
}
