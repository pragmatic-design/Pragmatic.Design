using System.Globalization;

namespace Pragmatic.Documents.Templating.Pipes;

/// <summary>
/// A template pipe that transforms a value: <c>{{value | pipeName:"arg"}}</c>.
/// </summary>
public interface ITemplatePipe
{
    string Name { get; }
    object? Execute(object? input, IReadOnlyList<string> args, CultureInfo culture);
}
