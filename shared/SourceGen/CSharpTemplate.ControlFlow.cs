// =============================================================================
// Pragmatic.Design - CSharpTemplate: Control Flow Methods
// If, ElseIf, Else, Switch, Case, Return
// =============================================================================

namespace Pragmatic.SourceGen;

internal abstract partial class CSharpTemplate
{
    // =============================================================================
    // Control Flow
    // =============================================================================

    protected void If(string condition, Action body)
    {
        if (parent != null)
        {
            parent.If(condition, body);
            return;
        }

        AppendLine($"if ({condition})");
        Block(body);
    }

    protected void ElseIf(string condition, Action body)
    {
        if (parent != null)
        {
            parent.ElseIf(condition, body);
            return;
        }

        AppendLine($"else if ({condition})");
        Block(body);
    }

    protected void Else(Action body)
    {
        if (parent != null)
        {
            parent.Else(body);
            return;
        }

        AppendLine("else");
        Block(body);
    }

    protected void Switch(string expression, Action body)
    {
        AppendLine($"switch ({expression})");
        Block(body);
    }

    protected void Case(string pattern, Action body)
    {
        AppendLine($"case {pattern}:");
        IncreaseIndent();
        body();
        DecreaseIndent();
    }

    protected void Default(Action body)
    {
        AppendLine("default:");
        IncreaseIndent();
        body();
        DecreaseIndent();
    }

    protected void Break()
    {
        AppendLine("break;");
    }

    protected void Continue()
    {
        AppendLine("continue;");
    }

    protected void Return(string expression)
    {
        AppendLine($"return {expression};");
    }
}