using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace Pragmatic.Persistence.Sql.Planning;

/// <summary>
///     Parameters that only hold values: what a plan binds into before an IN list expands its statement. A provider's own parameter may
///     refuse a list as its value (ODP.NET takes arrays only as array binding), so the lists are read from here, never from the provider's.
/// </summary>
internal sealed class SqlProbeParameters : DbParameterCollection
{
    private readonly List<SqlProbeParameter> _items = [];

    public SqlProbeParameters(int count)
    {
        for (var i = 0; i < count; i++)
            _items.Add(new SqlProbeParameter());
    }

    public override int Count => _items.Count;

    public override object SyncRoot => _items;

    public override int Add(object value)
    {
        _items.Add((SqlProbeParameter)value);
        return _items.Count - 1;
    }

    public override void AddRange(Array values)
    {
        foreach (var value in values)
            Add(value);
    }

    public override void Clear() => _items.Clear();

    public override bool Contains(object value) => value is SqlProbeParameter parameter && _items.Contains(parameter);

    public override bool Contains(string value) => _items.Exists(p => p.ParameterName == value);

    public override void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);

    public override IEnumerator GetEnumerator() => _items.GetEnumerator();

    public override int IndexOf(object value) => value is SqlProbeParameter parameter ? _items.IndexOf(parameter) : -1;

    public override int IndexOf(string parameterName) => _items.FindIndex(p => p.ParameterName == parameterName);

    public override void Insert(int index, object value) => _items.Insert(index, (SqlProbeParameter)value);

    public override void Remove(object value) => _items.Remove((SqlProbeParameter)value);

    public override void RemoveAt(int index) => _items.RemoveAt(index);

    public override void RemoveAt(string parameterName) => _items.RemoveAt(IndexOf(parameterName));

    protected override DbParameter GetParameter(int index) => _items[index];

    protected override DbParameter GetParameter(string parameterName) => _items[IndexOf(parameterName)];

    protected override void SetParameter(int index, DbParameter value) => _items[index] = (SqlProbeParameter)value;

    protected override void SetParameter(string parameterName, DbParameter value) => _items[IndexOf(parameterName)] = (SqlProbeParameter)value;

    private sealed class SqlProbeParameter : DbParameter
    {
        public override DbType DbType { get; set; }

        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;

        public override bool IsNullable { get; set; } = true;

        [AllowNull]
        public override string ParameterName { get; set; } = "";

        [AllowNull]
        public override string SourceColumn { get; set; } = "";

        public override bool SourceColumnNullMapping { get; set; }

        public override object? Value { get; set; }

        public override int Size { get; set; }

        public override void ResetDbType() => DbType = DbType.Object;
    }
}
