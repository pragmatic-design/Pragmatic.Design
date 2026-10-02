namespace Pragmatic.Email.Model;

/// <summary>Fluent builder for <see cref="EmailSection"/>.</summary>
public sealed class SectionBuilder
{
    private string? _backgroundColor;
    private EmailPadding _padding = EmailPadding.Default;
    private readonly List<EmailColumn> _columns = [];

    public SectionBuilder BackgroundColor(string color) { _backgroundColor = color; return this; }
    public SectionBuilder Padding(EmailPadding padding) { _padding = padding; return this; }
    public SectionBuilder Padding(int all) { _padding = EmailPadding.All(all); return this; }

    public SectionBuilder Column(Action<ColumnBuilder> configure, double width = 1.0)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new ColumnBuilder(width);
        configure(builder);
        _columns.Add(builder.Build());
        return this;
    }

    internal EmailSection Build() => new()
    {
        BackgroundColor = _backgroundColor,
        Padding = _padding,
        Columns = [.._columns]
    };
}
