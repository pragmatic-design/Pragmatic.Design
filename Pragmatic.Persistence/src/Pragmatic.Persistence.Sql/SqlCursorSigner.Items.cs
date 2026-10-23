namespace Pragmatic.Persistence.Sql;

/// <summary>The items of a cursor token: a tag byte per value, then the value in its binary form; read back as the type the caller asks for.</summary>
public sealed partial class SqlCursorSigner
{
    private enum Tag : byte
    {
        Null = 0,
        Guid = 1,
        String = 2,
        Boolean = 3,
        Int32 = 4,
        Int64 = 5,
        Decimal = 6,
        Double = 7,
        DateTimeOffset = 8,
        DateTime = 9,
        DateOnly = 10,
        TimeOnly = 11,
        Bytes = 12,
        Int16 = 13,
        Byte = 14,
        Single = 15,
        Enum = 16,
    }

    private static void Item(BinaryWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.Write((byte)Tag.Null);
                break;
            case Guid guid:
                writer.Write((byte)Tag.Guid);
                writer.Write(guid.ToByteArray());
                break;
            case string text:
                writer.Write((byte)Tag.String);
                writer.Write(text);
                break;
            case bool flag:
                writer.Write((byte)Tag.Boolean);
                writer.Write(flag);
                break;
            case int int32:
                writer.Write((byte)Tag.Int32);
                writer.Write(int32);
                break;
            case long int64:
                writer.Write((byte)Tag.Int64);
                writer.Write(int64);
                break;
            case decimal number:
                writer.Write((byte)Tag.Decimal);
                writer.Write(number);
                break;
            case double floating:
                writer.Write((byte)Tag.Double);
                writer.Write(floating);
                break;
            case DateTimeOffset at:
                writer.Write((byte)Tag.DateTimeOffset);
                writer.Write(at.Ticks);
                writer.Write((short)at.Offset.TotalMinutes);
                break;
            case DateTime instant:
                writer.Write((byte)Tag.DateTime);
                writer.Write(instant.ToBinary());
                break;
            case DateOnly day:
                writer.Write((byte)Tag.DateOnly);
                writer.Write(day.DayNumber);
                break;
            case TimeOnly time:
                writer.Write((byte)Tag.TimeOnly);
                writer.Write(time.Ticks);
                break;
            case byte[] bytes:
                writer.Write((byte)Tag.Bytes);
                writer.Write(bytes.Length);
                writer.Write(bytes);
                break;
            case short int16:
                writer.Write((byte)Tag.Int16);
                writer.Write(int16);
                break;
            case byte octet:
                writer.Write((byte)Tag.Byte);
                writer.Write(octet);
                break;
            case float single:
                writer.Write((byte)Tag.Single);
                writer.Write(single);
                break;
            case Enum member:
                writer.Write((byte)Tag.Enum);
                writer.Write(Convert.ToInt64(member, System.Globalization.CultureInfo.InvariantCulture));
                break;
            default:
                throw new ArgumentException($"A cursor value of type {value.GetType().Name} is not written into a token: the keys of a cursor are Guid, string, bool, the integers, decimal, double, float, DateTimeOffset, DateTime, DateOnly, TimeOnly, byte[] or an enum.");
        }
    }

    /// <summary>The <paramref name="position" />-th item as <typeparamref name="T" />: a null for a nullable type, the enum from its number, the value when its tag is the type's.</summary>
    private static T Item<T>(BinaryReader reader, int position)
    {
        var expected = typeof(T);
        var underlying = Nullable.GetUnderlyingType(expected) ?? expected;
        Tag tag;
        object? value;
        try
        {
            tag = (Tag)reader.ReadByte();
            value = tag switch
            {
                Tag.Null => null,
                Tag.Guid => new Guid(reader.ReadBytes(16)),
                Tag.String => reader.ReadString(),
                Tag.Boolean => reader.ReadBoolean(),
                Tag.Int32 => reader.ReadInt32(),
                Tag.Int64 => reader.ReadInt64(),
                Tag.Decimal => reader.ReadDecimal(),
                Tag.Double => reader.ReadDouble(),
                Tag.DateTimeOffset => new DateTimeOffset(reader.ReadInt64(), TimeSpan.FromMinutes(reader.ReadInt16())),
                Tag.DateTime => DateTime.FromBinary(reader.ReadInt64()),
                Tag.DateOnly => DateOnly.FromDayNumber(reader.ReadInt32()),
                Tag.TimeOnly => new TimeOnly(reader.ReadInt64()),
                Tag.Bytes => reader.ReadBytes(reader.ReadInt32()),
                Tag.Int16 => reader.ReadInt16(),
                Tag.Byte => reader.ReadByte(),
                Tag.Single => reader.ReadSingle(),
                Tag.Enum => reader.ReadInt64(),
                _ => throw new SqlCursorException($"Value {position} of the cursor token has an unknown tag ({(byte)tag})."),
            };
        }
        catch (EndOfStreamException)
        {
            throw new SqlCursorException("The cursor token ends before its values do.");
        }

        if (value is null)
        {
            if (expected.IsValueType && underlying == expected)
                throw new SqlCursorException($"Value {position} of the cursor token is null; {expected.Name} does not take one.");
            return default!;
        }

        if (tag == Tag.Enum)
        {
            if (!underlying.IsEnum)
                throw new SqlCursorException($"Value {position} of the cursor token is an enum; {underlying.Name} expected.");
            return (T)Enum.ToObject(underlying, (long)value);
        }

        if (value is T typed && !underlying.IsEnum)
            return typed;
        throw new SqlCursorException($"Value {position} of the cursor token is a {value.GetType().Name}; {underlying.Name} expected.");
    }
}
