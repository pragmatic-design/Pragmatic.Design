using Pragmatic.Persistence.Sql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>Projections: a Select lambda or a declared DTO, read into the columns and the row constructor.</summary>
internal static partial class SqlInterceptorGenerator
{
    /// <summary>A Select body read once: the columns it needs and the C# that builds the result from a row.</summary>
    internal sealed partial class Projection
    {
        private Projection(List<string> columns, string construct, string resultName, bool isAnonymous, List<ProjectedCollection>? collections = null, string? keyProperty = null, bool isValueType = false)
        {
            Columns = columns;
            Construct = construct;
            ResultName = resultName;
            IsAnonymous = isAnonymous;
            Collections = collections ?? [];
            KeyProperty = keyProperty;
            IsValueType = isValueType;
        }

        /// <summary>True when the row type is a value type (a scalar, a record struct): <c>FirstOrDefaultAsync</c> then returns it as it is, not nullable (<c>T?</c> on an unconstrained T).</summary>
        public bool IsValueType { get; }

        /// <summary>The column fragments in reader order (an aggregate over a collection is a correlated subquery, commas included: never split <see cref="ColumnList" />).</summary>
        public List<string> Columns { get; }

        /// <summary>The member each column feeds, with the kind of its values, in the columns' order: what a shaped read exposes to the client. Null when the projection has no named members (a single value, a join's row shape).</summary>
        public List<(string Name, string Kind)>? Members { get; set; }

        /// <summary>The dialect pieces the columns refer to as <c>{dN}</c> (a grouped projection's STRING_AGG): the plan absorbs them.</summary>
        public List<DialectPiece> Pieces { get; set; } = [];

        public string ColumnList => string.Join(", ", Columns);

        public string Construct { get; }

        public string ResultName { get; }

        public bool IsAnonymous { get; }

        /// <summary>How many columns the projection reads: a companion appends the parent key after them.</summary>
        public int ColumnCount => Columns.Count;

        /// <summary>A declared DTO's collection members, each a companion statement; empty for a lambda projection.</summary>
        public List<ProjectedCollection> Collections { get; }

        /// <summary>The DTO property that reads the entity's key (the loader groups the collections on it); null when the DTO has none.</summary>
        public string? KeyProperty { get; }

        /// <summary>A composite key's DTO properties, one per key column in its order, when the DTO has collections; null otherwise.</summary>
        public List<string>? KeyProperties { get; set; }

        /// <summary>Null when the body is outside the subset; the diagnostic goes to <paramref name="diagnostics" /> when given (the Select call site reports, the terminal stays quiet).</summary>
        public static Projection? TryRead(SemanticModel model, LambdaExpressionSyntax lambda, IMethodSymbol select, LambdaTranslator translator, List<Diagnostic>? diagnostics, Location where)
            => select.TypeArguments.Length == 1 ? TryRead(model, lambda, select.TypeArguments[0], translator, diagnostics, where) : null;

        /// <summary>The same for a body whose result type is known (a join's result selector: the last type argument, two parameters the translator knows).</summary>
        public static Projection? TryRead(SemanticModel model, LambdaExpressionSyntax lambda, ITypeSymbol resultType, LambdaTranslator translator, List<Diagnostic>? diagnostics, Location where)
        {
            void Report(string expression, string reason) => diagnostics?.Add(Diagnostic.Create(Diagnostics.UnsupportedExpression, where, expression, "projection", reason));
            var body = Body(lambda);
            var columns = new List<string>();
            var readers = new List<string>();
            List<(string Name, string Kind)>? named = null; // the members by name, for a shaped read
            string construct;
            switch (body)
            {
                case MemberAccessExpressionSyntax or InvocationExpressionSyntax:
                {
                    // One column, or one function of columns (Sql.*, a [SqlFunction]): read with the result type's reader (a converted column with its own).
                    var column = translator.ColumnOf(body);
                    var reader = body is MemberAccessExpressionSyntax && translator.LastColumn is { } source ? source.ProjectedReader(resultType) : EntityModel.ReaderFor(resultType);
                    if (column is null || reader is null || translator.ParameterPaths.Count > 0)
                    {
                        Report(translator.ErrorExpression ?? body.ToString(), translator.Error ?? $"type {resultType.ToDisplayString()} is not read");
                        return null;
                    }

                    columns.Add(column);
                    construct = reader.Replace("{r}", "r").Replace("{i}", "0").Replace("{d}", "d");
                    break;
                }

                case ObjectCreationExpressionSyntax { Initializer: { } initializer } creation when creation.ArgumentList is null || creation.ArgumentList.Arguments.Count == 0:
                {
                    var assignments = new List<string>();
                    foreach (var expression in initializer.Expressions)
                    {
                        if (expression is not AssignmentExpressionSyntax { Left: IdentifierNameSyntax target } assignment)
                        {
                            Report(expression.ToString(), "only 'Member = o.Column' assignments are translated");
                            return null;
                        }

                        var memberSymbol = model.GetSymbolInfo(target).Symbol as IPropertySymbol;
                        var before = columns.Count;
                        if (!TryMember(translator, assignment.Right, memberSymbol?.Type, columns, readers, anonymous: false, out var reason))
                        {
                            Report(assignment.Right.ToString(), reason);
                            return null;
                        }

                        assignments.Add($"{target.Identifier.ValueText} = {readers[readers.Count - 1]}");
                        if (columns.Count == before + 1)
                            (named ??= []).Add((target.Identifier.ValueText, LambdaTranslator.KindOf(memberSymbol?.Type)));
                        else
                            named = NoNames; // a member of several columns (an owned type): the shape could not name them one by one
                    }

                    construct = $"new {resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} {{ {string.Join(", ", assignments)} }}";
                    break;
                }

                case AnonymousObjectCreationExpressionSyntax anonymous:
                {
                    var members = new List<string>();
                    foreach (var declarator in anonymous.Initializers)
                    {
                        var memberName = declarator.NameEquals?.Name.Identifier.ValueText ?? (declarator.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.ValueText ?? (declarator.Expression as IdentifierNameSyntax)?.Identifier.ValueText;
                        if (memberName is null)
                        {
                            Report(declarator.ToString(), "an anonymous member needs a name");
                            return null;
                        }

                        var type = model.GetTypeInfo(declarator.Expression).Type;
                        var before = columns.Count;
                        if (!TryMember(translator, declarator.Expression, type, columns, readers, anonymous: true, out var reason))
                        {
                            Report(declarator.Expression.ToString(), reason);
                            return null;
                        }

                        members.Add($"{memberName} = {readers[readers.Count - 1]}");
                        if (columns.Count == before + 1)
                            (named ??= []).Add((memberName, LambdaTranslator.KindOf(type)));
                        else
                            named = NoNames;
                    }

                    // Anonymous types of one assembly unify by shape: the row built here is the caller's type, reached through the plan's type parameter.
                    construct = $"(TElement)(object)new {{ {string.Join(", ", members)} }}";
                    break;
                }

                default:
                    Report(body.ToString(), "a projection is a member, a DTO with an object initializer, or an anonymous object; a method call in it runs after the query, on the list");
                    return null;
            }

            var isAnonymous = body is AnonymousObjectCreationExpressionSyntax;
            return new Projection(columns, construct, isAnonymous ? "TElement" : resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), isAnonymous, isValueType: !isAnonymous && resultType.IsValueType) { Members = ReferenceEquals(named, NoNames) ? null : named };
        }

        /// <summary>A marker: the projection has members the shape cannot name one by one (one of them spans several columns).</summary>
        private static readonly List<(string Name, string Kind)> NoNames = [];

        /// <summary>The row itself read back by a command (<c>returning: o => o</c>): the entity's columns, read by the entity's reader (an empty construct says so).</summary>
        public static Projection WholeRow(EntityModel entity)
            => new(entity.Columns.Where(c => !c.IsDiscriminator).Select(c => c.Select("t")).ToList(), "", entity.FullName, isAnonymous: false);

        /// <summary>
        ///     The Select after a GroupBy: an object initializer or an anonymous object whose members are <c>g.Key</c> and the
        ///     aggregates <c>g.Count()</c>, <c>g.Sum/Min/Max/Average(x => x.Column)</c>. The columns are the GROUP BY key and
        ///     the aggregate expressions; the readers follow the members' types.
        /// </summary>
        public static Projection? TryGrouped(SemanticModel model, LambdaExpressionSyntax lambda, IMethodSymbol select, RowShape rowShape, Func<INamedTypeSymbol, EntityModel?> entities, GroupKey key, List<Diagnostic>? diagnostics, Location where)
        {
            void Report(string expression, string reason) => diagnostics?.Add(Diagnostic.Create(Diagnostics.UnsupportedExpression, where, expression, "grouped projection", reason));
            var resultType = select.TypeArguments.Length == 1 ? select.TypeArguments[0] : null;
            if (resultType is null || !TryParameter(model, lambda, out var group))
                return null;
            var body = Body(lambda);
            var members = new List<(string Name, ExpressionSyntax Value, ITypeSymbol? Type)>();
            switch (body)
            {
                case ObjectCreationExpressionSyntax { Initializer: { } initializer } creation when creation.ArgumentList is null || creation.ArgumentList.Arguments.Count == 0:
                    foreach (var expression in initializer.Expressions)
                    {
                        if (expression is not AssignmentExpressionSyntax { Left: IdentifierNameSyntax target } assignment)
                        {
                            Report(expression.ToString(), "only 'Member = g.Key' and 'Member = g.Aggregate(…)' assignments are translated");
                            return null;
                        }

                        members.Add((target.Identifier.ValueText, assignment.Right, (model.GetSymbolInfo(target).Symbol as IPropertySymbol)?.Type));
                    }

                    break;
                case AnonymousObjectCreationExpressionSyntax anonymous:
                    foreach (var declarator in anonymous.Initializers)
                    {
                        var memberName = declarator.NameEquals?.Name.Identifier.ValueText ?? (declarator.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.ValueText;
                        if (memberName is null)
                        {
                            Report(declarator.ToString(), "an anonymous member needs a name");
                            return null;
                        }

                        members.Add((memberName, declarator.Expression, model.GetTypeInfo(declarator.Expression).Type));
                    }

                    break;
                default:
                    Report(body.ToString(), "a grouped projection is a DTO with an object initializer or an anonymous object of g.Key and aggregates");
                    return null;
            }

            var columns = new List<string>();
            var assignments = new List<string>();
            var pieces = new List<DialectPiece>();
            foreach (var (memberName, written, type) in members)
            {
                string column;
                string? reader;
                var value = LambdaTranslator.Unwrap(written); // a cast to the nullable type ((decimal?)g.Where(…).Sum(…)) declares that a filtered aggregate may be NULL; the member's type keeps it
                if (value is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Key", Expression: IdentifierNameSyntax root } && model.GetSymbolInfo(root).Symbol is IParameterSymbol p && SymbolEqualityComparer.Default.Equals(p, group))
                {
                    if (key.IsComposite)
                    {
                        // The whole composite key: its columns, rebuilt as the anonymous object or the tuple the GroupBy declared.
                        var wholeKey = key.Construct(t => EntityModel.ReaderFor(t), columns.Count);
                        if (wholeKey is null)
                        {
                            Report(value.ToString(), "a column of the group key has a type the reader does not map");
                            return null;
                        }

                        columns.AddRange(key.Parts.Select(part => part.Column));
                        assignments.Add($"{memberName} = {wholeKey}");
                        continue;
                    }

                    column = key.Parts[0].Column;
                    reader = EntityModel.ReaderFor(key.Type);
                }
                else if (value is MemberAccessExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Key", Expression: IdentifierNameSyntax keyRoot } } keyMember && model.GetSymbolInfo(keyRoot).Symbol is IParameterSymbol kp && SymbolEqualityComparer.Default.Equals(kp, group))
                {
                    // One member of a composite key (g.Key.Status, g.Key.Item2).
                    var part = key.Column(keyMember.Name.Identifier.ValueText);
                    if (part is null)
                    {
                        Report(value.ToString(), $"'{keyMember.Name.Identifier.ValueText}' is not a member of the group key");
                        return null;
                    }

                    column = part;
                    reader = type is null ? null : EntityModel.ReaderFor(type);
                }
                else if (value is InvocationExpressionSyntax && GroupAggregate.IsOnGroup(value, model, group))
                {
                    // An aggregate of the group: Count/Sum/Min/Max/Average, filtered by a Where, distinct, or string.Join over a Select.
                    var sql = GroupAggregate.Render(value, model, group, lambda => TryParameter(model, lambda, out var row) ? new LambdaTranslator(model, entities, rowShape, row) : null, piece =>
                    {
                        pieces.Add(piece);
                        return "{d" + (pieces.Count - 1) + "}";
                    }, out var kind, out var error);
                    if (sql is null)
                    {
                        Report(value.ToString(), error ?? "the aggregate is outside the translated subset");
                        return null;
                    }

                    column = sql;
                    reader = kind switch
                    {
                        GroupAggregate.Kind.Count => "global::Pragmatic.Persistence.Sql.SqlValue.Count({r}, {i}, {d})",
                        GroupAggregate.Kind.Average => type?.SpecialType == SpecialType.System_Decimal ? "(decimal)global::Pragmatic.Persistence.Sql.SqlValue.Average({r}, {i}, {d})" : "global::Pragmatic.Persistence.Sql.SqlValue.Average({r}, {i}, {d})",
                        GroupAggregate.Kind.Text => "global::Pragmatic.Persistence.Sql.SqlValue.NullableString({r}, {i})",
                        _ => type is null ? null : EntityModel.ReaderFor(type), // a filtered aggregate over no matching row is NULL: declare the member nullable ((decimal?)g.Where(…).Sum(…))
                    };
                }
                else
                {
                    Report(value.ToString(), "a grouped member is g.Key or an aggregate of the group; a column of the row is not defined after GROUP BY");
                    return null;
                }

                if (reader is null)
                {
                    Report(value.ToString(), $"type {type?.ToDisplayString()} is not read");
                    return null;
                }

                columns.Add(column);
                assignments.Add($"{memberName} = {reader.Replace("{r}", "r").Replace("{i}", (columns.Count - 1).ToString()).Replace("{d}", "d")}");
            }

            var isAnonymous = body is AnonymousObjectCreationExpressionSyntax;
            var resultName = isAnonymous ? "TElement" : resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var construct = isAnonymous ? $"(TElement)(object)new {{ {string.Join(", ", assignments)} }}" : $"new {resultName} {{ {string.Join(", ", assignments)} }}";
            return new Projection(columns, construct, resultName, isAnonymous, isValueType: !isAnonymous && resultType.IsValueType) { Pieces = pieces };
        }

        private static bool TryMember(LambdaTranslator translator, ExpressionSyntax expression, ITypeSymbol? type, List<string> columns, List<string> readers, bool anonymous, out string reason)
        {
            reason = "";
            if (type is null)
            {
                reason = "the member's type could not be resolved";
                return false;
            }

            if (translator.Model.GetSymbolInfo(expression).Symbol is IPropertySymbol { Type: var ownedType } property && EntityModel.IsOwnedProperty(property)
                && EntityModel.Unwrap(ownedType) is INamedTypeSymbol owned)
                return TryOwned(translator, expression, owned, EntityModel.IsNullable(ownedType), columns, readers, out reason);

            var before = translator.ParameterPaths.Count;
            var column = translator.ColumnOf(expression);
            if (column is null || translator.ParameterPaths.Count != before)
            {
                reason = translator.Error ?? "a projected member must be a column (o.X or o.Nav.X); a constant or a computed value is set after the query";
                return false;
            }

            if (translator.LastColumn?.VectorDimensions is not null)
                column = SqlDialects.VectorSelect(column); // a vector member read as text

            string? reader;
            if (translator.LastColumnOptional)
            {
                // The joined row of a LEFT JOIN may be missing: the member reads null, so its type must admit it (a reference type in an anonymous object does, its annotation aside).
                if (translator.LastColumn?.Converter is not null)
                {
                    reason = "a converted column of a LEFT JOIN's inner row is not read (ASSUMPTION of the spike: read the stored value through a plain column, or use an INNER JOIN)";
                    return false;
                }

                if (!EntityModel.IsNullable(type) && (type.IsValueType || !anonymous))
                {
                    reason = $"a LEFT JOIN reads the inner row's columns as null: declare the member nullable ({(anonymous ? $"a cast, ({type.ToDisplayString()}?){expression}" : type.ToDisplayString() + "?")})";
                    return false;
                }

                reader = EntityModel.ReaderFor(type, forceNullable: true);
            }
            else
            {
                reader = translator.LastColumn is { } source ? source.ProjectedReader(type) : EntityModel.ReaderFor(type); // a converted column's own reader; an optional owned member as nullable
            }

            if (reader is null)
            {
                reason = $"type {type.ToDisplayString()} is not read";
                return false;
            }

            columns.Add(column);
            readers.Add(reader.Replace("{r}", "r").Replace("{i}", (columns.Count - 1).ToString()).Replace("{d}", "d"));
            return true;
        }

        /// <summary>An owned object read whole (<c>x.Address</c>): its members' columns, rebuilt as the entity's reader builds it (an initializer, or a value object's constructor); null when its columns are, for an optional one.</summary>
        private static bool TryOwned(LambdaTranslator translator, ExpressionSyntax expression, INamedTypeSymbol owned, bool optional, List<string> columns, List<string> readers, out string reason)
        {
            reason = "";
            var members = translator.OwnedColumns(expression, owned);
            if (members is null || members.Count == 0 || translator.LastColumnOptional)
            {
                reason = translator.Error ?? $"'{expression}' is an owned object read whole from the row itself or a required navigation's: its members' columns";
                return false;
            }

            var model = new OwnedModel(expression.ToString(), owned.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), optional, members.Select(m => m.Column).ToList());
            if (EntityModel.ConstructorMembers(owned, model.Columns) is not { } constructor)
            {
                reason = $"{owned.Name} has neither a public parameterless constructor nor one whose parameters are its members";
                return false;
            }

            model.Constructor = constructor;
            var first = columns.Count;
            columns.AddRange(members.Select(m => m.Sql));
            var construct = EntityModel.ConstructOwned(model, m => model.Columns[m].Read("r", (first + m).ToString(), "d"));
            readers.Add(optional ? $"(r.IsDBNull({first}) ? null : {construct})" : construct);
            return true;
        }
    }
}
