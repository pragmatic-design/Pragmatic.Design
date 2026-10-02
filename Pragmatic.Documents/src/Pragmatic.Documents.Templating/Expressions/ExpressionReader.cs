namespace Pragmatic.Documents.Templating.Expressions;

/// <summary>Recursive descent expression reader (ref struct for zero-alloc parsing).</summary>
internal ref struct ExpressionReader(ReadOnlySpan<char> source)
{
    private readonly ReadOnlySpan<char> _source = source;
    private int _pos = 0;
    private int _depth = 0;

    // Guards against a stack-overflow from pathologically nested expressions — e.g. deep parentheses,
    // nested ternaries, or long `??` chains. StackOverflowException is uncatchable and kills the process,
    // so we fail loudly with a catchable TemplateParseException well before the native stack limit.
    private const int MaxDepth = 512;

    // Precedence (low to high): ternary, null-coalesce, or, and, equality, comparison, add/sub, mul/div, unary, pipe, primary

    /// <summary>
    ///     Reads a whole expression. Text the grammar cannot place is an error: a reader that stops where
    ///     the grammar stops would read <c>items[0].name</c> as <c>items</c> and tell nobody.
    /// </summary>
    public TemplateExpression ParseToEnd()
    {
        var expression = ParseTernary();
        SkipWhitespace();

        if (_pos < _source.Length)
        {
            throw new TemplateParseException(
                $"Unexpected '{_source[_pos..].ToString()}' at position {_pos} in '{_source.ToString()}'.");
        }

        return expression;
    }

    public TemplateExpression ParseTernary()
    {
        if (++_depth > MaxDepth)
            throw new TemplateParseException($"Expression nesting exceeds the maximum depth of {MaxDepth}.");
        try
        {
            var expr = ParseNullCoalesce();
            SkipWhitespace();

            if (Peek() == '?' && PeekAt(1) != '?')
            {
                Advance(); // skip ?
                SkipWhitespace();
                var trueVal = ParseTernary();
                SkipWhitespace();
                Expect(':');
                SkipWhitespace();
                var falseVal = ParseTernary();
                return new TernaryExpression(expr, trueVal, falseVal);
            }

            return expr;
        }
        finally { _depth--; }
    }

    private TemplateExpression ParseNullCoalesce()
    {
        if (++_depth > MaxDepth)
            throw new TemplateParseException($"Expression nesting exceeds the maximum depth of {MaxDepth}.");
        try
        {
            var left = ParseOr();
            SkipWhitespace();

            if (Peek() == '?' && PeekAt(1) == '?')
            {
                Advance(); Advance(); // skip ??
                SkipWhitespace();
                var right = ParseNullCoalesce();
                return new NullCoalescingExpression(left, right);
            }

            return left;
        }
        finally { _depth--; }
    }

    private TemplateExpression ParseOr()
    {
        var left = ParseAnd();
        while (MatchOperator("||"))
        {
            var right = ParseAnd();
            left = new BinaryExpression(left, BinaryOp.Or, right);
        }
        return left;
    }

    private TemplateExpression ParseAnd()
    {
        var left = ParseEquality();
        while (MatchOperator("&&"))
        {
            var right = ParseEquality();
            left = new BinaryExpression(left, BinaryOp.And, right);
        }
        return left;
    }

    private TemplateExpression ParseEquality()
    {
        var left = ParseComparison();
        while (true)
        {
            if (MatchOperator("==")) left = new BinaryExpression(left, BinaryOp.Eq, ParseComparison());
            else if (MatchOperator("!=")) left = new BinaryExpression(left, BinaryOp.Neq, ParseComparison());
            else break;
        }
        return left;
    }

    private TemplateExpression ParseComparison()
    {
        var left = ParseAddSub();
        while (true)
        {
            if (MatchOperator(">=")) left = new BinaryExpression(left, BinaryOp.Gte, ParseAddSub());
            else if (MatchOperator("<=")) left = new BinaryExpression(left, BinaryOp.Lte, ParseAddSub());
            else if (MatchOperator(">")) left = new BinaryExpression(left, BinaryOp.Gt, ParseAddSub());
            else if (MatchOperator("<")) left = new BinaryExpression(left, BinaryOp.Lt, ParseAddSub());
            else break;
        }
        return left;
    }

    private TemplateExpression ParseAddSub()
    {
        var left = ParseMulDiv();
        while (true)
        {
            SkipWhitespace();
            if (Peek() == '+') { Advance(); SkipWhitespace(); left = new BinaryExpression(left, BinaryOp.Add, ParseMulDiv()); }
            else if (Peek() == '-') { Advance(); SkipWhitespace(); left = new BinaryExpression(left, BinaryOp.Sub, ParseMulDiv()); }
            else break;
        }
        return left;
    }

    private TemplateExpression ParseMulDiv()
    {
        var left = ParseUnary();
        while (true)
        {
            SkipWhitespace();
            if (Peek() == '*') { Advance(); SkipWhitespace(); left = new BinaryExpression(left, BinaryOp.Mul, ParseUnary()); }
            else if (Peek() == '/' && PeekAt(1) != '/') { Advance(); SkipWhitespace(); left = new BinaryExpression(left, BinaryOp.Div, ParseUnary()); }
            else if (Peek() == '%') { Advance(); SkipWhitespace(); left = new BinaryExpression(left, BinaryOp.Mod, ParseUnary()); }
            else break;
        }
        return left;
    }

    private TemplateExpression ParseUnary()
    {
        SkipWhitespace();
        if (Peek() == '!')
        {
            Advance(); SkipWhitespace();
            var operand = ParseUnary();
            return new BinaryExpression(operand, BinaryOp.Eq, new LiteralExpression(false));
        }
        if (Peek() == '-' && char.IsDigit(PeekAt(1)))
        {
            return ParsePipe();
        }
        return ParsePipe();
    }

    private TemplateExpression ParsePipe()
    {
        var expr = ParsePrimary();
        while (true)
        {
            SkipWhitespace();
            if (Peek() != '|' || PeekAt(1) == '|') break;
            Advance();
            SkipWhitespace();
            var pipeName = ReadIdentifier();
            var args = new List<string>();
            SkipWhitespace();
            if (Peek() == ':')
            {
                Advance();
                args.AddRange(ReadPipeArgs());
            }
            expr = new PipeExpression(expr, pipeName, args);
        }
        return expr;
    }

    private TemplateExpression ParsePrimary()
    {
        SkipWhitespace();
        var c = Peek();

        if (c is '"' or '\'') return new LiteralExpression(ReadString());
        if (c == '-' || char.IsDigit(c)) return new LiteralExpression(ReadNumber());
        if (TryMatchKeyword("true")) return new LiteralExpression(true);
        if (TryMatchKeyword("false")) return new LiteralExpression(false);
        if (TryMatchKeyword("null")) return new LiteralExpression(null);

        if (c == '(')
        {
            Advance(); SkipWhitespace();
            var inner = ParseTernary();
            SkipWhitespace();
            Expect(')');
            return inner;
        }

        if (char.IsLetter(c) || c == '_')
            return ParseIdentifierOrAggregate();

        throw new TemplateParseException($"Unexpected character '{c}' at position {_pos}");
    }

    private TemplateExpression ParseIdentifierOrAggregate()
    {
        var path = ReadDottedPath();

        var lastDot = path.LastIndexOf('.');
        if (lastDot > 0)
        {
            var suffix = path[(lastDot + 1)..];
            if (Enum.TryParse<AggregateFunction>(suffix, ignoreCase: true, out var func))
            {
                var collectionPath = path[..lastDot];

                if (func == AggregateFunction.Count)
                    return new AggregateExpression(collectionPath, func, null);

                SkipWhitespace();
                if (Peek() == '(')
                {
                    Advance(); SkipWhitespace();
                    var propName = ReadIdentifier();
                    SkipWhitespace();
                    Expect(')');
                    return new AggregateExpression(collectionPath, func, propName);
                }
            }
        }

        return new PropertyAccessExpression(path);
    }

    // --- Readers ---

    private string ReadIdentifier()
    {
        var start = _pos;
        while (_pos < _source.Length && (char.IsLetterOrDigit(_source[_pos]) || _source[_pos] == '_'))
            _pos++;
        if (_pos == start)
            throw new TemplateParseException($"Expected identifier at position {_pos}");
        return _source[start.._pos].ToString();
    }

    private string ReadDottedPath()
    {
        var start = _pos;
        while (_pos < _source.Length && (char.IsLetterOrDigit(_source[_pos]) || _source[_pos] == '_' || _source[_pos] == '.' || _source[_pos] == '$'))
            _pos++;
        if (_pos == start)
            throw new TemplateParseException($"Expected path at position {_pos}");
        return _source[start.._pos].ToString();
    }

    private string ReadString()
    {
        var quote = _source[_pos];
        Advance();

        // Fast path: scan for a closing quote with no escapes, slicing directly (zero-alloc copy).
        var start = _pos;
        var hasEscape = false;
        while (_pos < _source.Length && _source[_pos] != quote)
        {
            if (_source[_pos] == '\\')
            {
                hasEscape = true;
                _pos++; // skip the escaped char so a '\"' does not terminate the literal
            }
            _pos++;
        }

        string value;
        if (!hasEscape)
        {
            value = _source[start.._pos].ToString();
        }
        else
        {
            var sb = new System.Text.StringBuilder(_pos - start);
            var i = start;
            while (i < _pos)
            {
                var ch = _source[i];
                if (ch == '\\' && i + 1 < _pos)
                {
                    var next = _source[i + 1];
                    sb.Append(next switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        'r' => '\r',
                        'b' => '\b',
                        'f' => '\f',
                        '0' => '\0',
                        '\\' => '\\',
                        '"' => '"',
                        '\'' => '\'',
                        _ => next // unknown escape: keep the char verbatim (drop the backslash)
                    });
                    i += 2;
                }
                else
                {
                    sb.Append(ch);
                    i++;
                }
            }
            value = sb.ToString();
        }

        if (_pos >= _source.Length)
            throw new TemplateParseException($"Unterminated string starting at position {start - 1}.");

        Advance();
        return value;
    }

    private object ReadNumber()
    {
        var start = _pos;
        if (Peek() == '-') _pos++;
        while (_pos < _source.Length && (char.IsDigit(_source[_pos]) || _source[_pos] == '.'))
            _pos++;

        var numStr = _source[start.._pos].ToString();
        if (numStr.Contains('.'))
            return double.TryParse(numStr, System.Globalization.CultureInfo.InvariantCulture, out var dd)
                ? dd
                : throw new TemplateParseException($"Invalid number literal '{numStr}'.");
        // int → long → double: 19+ digit integers overflow long, so fall back to double rather than
        // throwing OverflowException on user-provided template data.
        //
        // One return per branch, deliberately. Written as a conditional chain — `int.TryParse(…) ? i
        // : long.TryParse(…) ? l : d` — every branch converts to the expression's natural common
        // type, which is double, and the method would return a double for EVERY literal. The boxing
        // hides that, and an assertion library that compares numbers across types hides it downstream.
        if (int.TryParse(numStr, out var i))
            return i;

        if (long.TryParse(numStr, out var l))
            return l;

        if (double.TryParse(numStr, System.Globalization.CultureInfo.InvariantCulture, out var d))
            return d;

        throw new TemplateParseException($"Invalid number literal '{numStr}'.");
    }

    private List<string> ReadPipeArgs()
    {
        var args = new List<string>();
        while (_pos < _source.Length)
        {
            SkipWhitespace();
            var c = Peek();
            if (c == '"' || c == '\'') { args.Add(ReadString()); }
            else if (char.IsDigit(c) || c == '-') { args.Add(ReadNumber().ToString()!); }
            else
            {
                var start = _pos;
                while (_pos < _source.Length && _source[_pos] != ',' && _source[_pos] != '|' && _source[_pos] != ' ')
                    _pos++;
                if (_pos > start) args.Add(_source[start.._pos].ToString());
            }
            SkipWhitespace();
            if (Peek() == ',') { Advance(); continue; }
            break;
        }
        return args;
    }

    // --- Helpers ---

    private bool MatchOperator(string op)
    {
        SkipWhitespace();
        if (_pos + op.Length > _source.Length) return false;
        if (!_source[_pos..(_pos + op.Length)].SequenceEqual(op.AsSpan())) return false;
        if (op.Length == 1 && (op[0] == '>' || op[0] == '<') && _pos + 1 < _source.Length && _source[_pos + 1] == '=')
            return false;
        _pos += op.Length;
        SkipWhitespace();
        return true;
    }

    private bool TryMatchKeyword(string keyword)
    {
        if (_pos + keyword.Length > _source.Length) return false;
        if (!_source[_pos..(_pos + keyword.Length)].SequenceEqual(keyword.AsSpan())) return false;
        if (_pos + keyword.Length < _source.Length && char.IsLetterOrDigit(_source[_pos + keyword.Length])) return false;
        _pos += keyword.Length;
        return true;
    }

    private char Peek() => _pos < _source.Length ? _source[_pos] : '\0';
    private char PeekAt(int offset) => _pos + offset < _source.Length ? _source[_pos + offset] : '\0';
    private void Advance() => _pos++;

    private void Expect(char c)
    {
        if (Peek() != c)
            throw new TemplateParseException($"Expected '{c}' at position {_pos}, got '{Peek()}'");
        Advance();
    }

    private void SkipWhitespace()
    {
        while (_pos < _source.Length && char.IsWhiteSpace(_source[_pos])) _pos++;
    }
}
