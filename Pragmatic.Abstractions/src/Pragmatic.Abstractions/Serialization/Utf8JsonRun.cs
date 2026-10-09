using System.Buffers;
using System.Buffers.Text;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Pragmatic.Serialization;

/// <summary>
///     A run of JSON whose bytes no encoder can change — names already encoded, numbers, booleans, Guids, dates, and
///     objects and arrays made only of those — formatted as <see cref="Utf8JsonWriter" /> would write it, for a
///     generated writer to hand over in one <see cref="Utf8JsonWriter.WriteRawValue(ReadOnlySpan{byte}, bool)" />.
/// </summary>
/// <remarks>
///     <para>
///         Called by generated code, not meant to be called by hand. A writer call does a state check, a buffer
///         request and a separator decision for every token; on a document made mostly of small objects of numbers
///         that is most of the time spent (<c>Pragmatic.Endpoints/BENCHMARK-RESULTS.md</c>). A run makes the same
///         decisions in a few instructions each, and the writer pays its own once per run.
///     </para>
///     <para>
///         Separators follow the writer's rule: a comma before a name or a value that follows a value or a
///         closed container. A run that starts with a name, written where the writer expects one, needs a writer
///         that skips validation, as a generated response's does.
///     </para>
///     <para>
///         <see cref="Start" /> takes the thread's scratch buffer and <see cref="Dispose" /> gives it back, so a run
///         neither clears a stack buffer nor rents one: measured on <c>citm_catalog.json</c>, with a stack buffer
///         cleared per run and a pooled one for every run past it, the generated writer was 280 μs against 228 for the
///         same runs written by hand in the same benchmark run; with the scratch it is 252 against 271. A run
///         started while another is open on the thread finds no scratch and gets a buffer of its own; a run that
///         outgrows the scratch moves
///         to a pooled one, which <see cref="Dispose" /> returns. A run abandoned by an exception leaves both to the
///         collector, and the thread's next run allocates a new scratch.
///     </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public ref struct Utf8JsonRun
{
    // Every run of the measured documents fits; a larger one moves to the pool.
    private const int ScratchSize = 16 * 1024;

    [ThreadStatic] private static byte[]? t_scratch;

    // Longest of each value: a signed 64-bit integer, a decimal, a quoted Guid, a quoted round-trip date with offset.
    private const int MaxInteger = 20;
    private const int MaxDecimal = 31;
    private const int MaxGuid = 38;
    private const int MaxDate = 35;

    // -1.7976931348623157E+308, and room to spare.
    private const int MaxFloatingPoint = 32;

    private Span<byte> _buffer;
    private byte[]? _scratch;
    private byte[]? _rented;
    private int _length;
    private bool _separate;

    private Utf8JsonRun(byte[] scratch)
    {
        _scratch = scratch;
        _buffer = scratch;
    }

    /// <summary>A run in the thread's scratch buffer, or in a new one when another run holds it.</summary>
    public static Utf8JsonRun Start()
    {
        var scratch = t_scratch ?? new byte[ScratchSize];
        t_scratch = null;
        return new Utf8JsonRun(scratch);
    }

    /// <summary>What the run holds.</summary>
    public readonly ReadOnlySpan<byte> Written => _buffer[.._length];

    /// <summary>Whether nothing was written: every member of the run was left out.</summary>
    public readonly bool IsEmpty => _length == 0;

    /// <summary><c>"name":</c>, from a name encoded once: what a run writes for a property name.</summary>
    public static byte[] Name(JsonEncodedText name)
    {
        var encoded = name.EncodedUtf8Bytes;
        var bytes = new byte[encoded.Length + 3];
        bytes[0] = (byte)'"';
        encoded.CopyTo(bytes.AsSpan(1));
        bytes[^2] = (byte)'"';
        bytes[^1] = (byte)':';
        return bytes;
    }

    /// <summary>A property name, as <see cref="Name" /> encoded it.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PropertyName(ReadOnlySpan<byte> name)
    {
        var target = Reserve(name.Length + 1);
        var at = Separator(target);
        name.CopyTo(target[at..]);
        _length += at + name.Length;
        _separate = false;
    }

    /// <summary>A dictionary key that is a signed number: its invariant digits, as a property name.</summary>
    public void PropertyName(long key)
    {
        var target = Reserve(MaxInteger + 4);
        var at = Separator(target);
        target[at++] = (byte)'"';
        Utf8Formatter.TryFormat(key, target[at..], out var count);
        at += count;
        target[at++] = (byte)'"';
        target[at++] = (byte)':';
        _length += at;
        _separate = false;
    }

    /// <summary>A dictionary key that is an unsigned number: its invariant digits, as a property name.</summary>
    public void PropertyName(ulong key)
    {
        var target = Reserve(MaxInteger + 4);
        var at = Separator(target);
        target[at++] = (byte)'"';
        Utf8Formatter.TryFormat(key, target[at..], out var count);
        at += count;
        target[at++] = (byte)'"';
        target[at++] = (byte)':';
        _length += at;
        _separate = false;
    }

    /// <summary><c>{</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void StartObject() => Open((byte)'{');

    /// <summary><c>}</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EndObject() => Close((byte)'}');

    /// <summary><c>[</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void StartArray() => Open((byte)'[');

    /// <summary><c>]</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EndArray() => Close((byte)']');

    /// <summary><c>null</c>.</summary>
    public void NullValue() => Literal("null"u8);

    /// <summary><c>true</c> or <c>false</c>.</summary>
    public void BooleanValue(bool value) => Literal(value ? "true"u8 : "false"u8);

    /// <summary>An integer, in its invariant digits.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void NumberValue(int value)
    {
        var target = Reserve(MaxInteger + 1);
        var at = Separator(target);
        Utf8Formatter.TryFormat(value, target[at..], out var count);
        Value(at + count);
    }

    /// <summary>An integer, in its invariant digits.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void NumberValue(long value)
    {
        var target = Reserve(MaxInteger + 1);
        var at = Separator(target);
        Utf8Formatter.TryFormat(value, target[at..], out var count);
        Value(at + count);
    }

    /// <summary>An integer, in its invariant digits.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void NumberValue(uint value)
    {
        var target = Reserve(MaxInteger + 1);
        var at = Separator(target);
        Utf8Formatter.TryFormat(value, target[at..], out var count);
        Value(at + count);
    }

    /// <summary>An integer, in its invariant digits.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void NumberValue(ulong value)
    {
        var target = Reserve(MaxInteger + 1);
        var at = Separator(target);
        Utf8Formatter.TryFormat(value, target[at..], out var count);
        Value(at + count);
    }

    /// <summary>A double, in its shortest round-trip invariant form, as the writer writes one.</summary>
    /// <exception cref="ArgumentException">The value is not finite, which the writer refuses too.</exception>
    public void NumberValue(double value)
    {
        if (!double.IsFinite(value))
            throw NotFinite(value.ToString(CultureInfo.InvariantCulture));

        var target = Reserve(MaxFloatingPoint + 1);
        var at = Separator(target);
        value.TryFormat(target[at..], out var count, provider: CultureInfo.InvariantCulture);
        Value(at + count);
    }

    /// <summary>A float, in its shortest round-trip invariant form, as the writer writes one.</summary>
    /// <exception cref="ArgumentException">The value is not finite, which the writer refuses too.</exception>
    public void NumberValue(float value)
    {
        if (!float.IsFinite(value))
            throw NotFinite(value.ToString(CultureInfo.InvariantCulture));

        var target = Reserve(MaxFloatingPoint + 1);
        var at = Separator(target);
        value.TryFormat(target[at..], out var count, provider: CultureInfo.InvariantCulture);
        Value(at + count);
    }

    /// <summary>A decimal, in the general format, as the writer writes one.</summary>
    public void NumberValue(decimal value)
    {
        var target = Reserve(MaxDecimal + 1);
        var at = Separator(target);
        Utf8Formatter.TryFormat(value, target[at..], out var count);
        Value(at + count);
    }

    /// <summary>A Guid, in the <c>D</c> format, as the writer writes one: none of its characters is escaped.</summary>
    public void StringValue(Guid value)
    {
        var target = Reserve(MaxGuid + 1);
        var at = Separator(target);
        target[at++] = (byte)'"';
        Utf8Formatter.TryFormat(value, target[at..], out var count);
        at += count;
        target[at++] = (byte)'"';
        Value(at);
    }

    /// <summary>A date, as the writer writes one: the round-trip format with the fraction trimmed.</summary>
    public void StringValue(DateTime value)
    {
        var target = Reserve(MaxDate + 1);
        var at = Separator(target);
        target[at++] = (byte)'"';
        Utf8Formatter.TryFormat(value, target[at..], out var count, 'O');
        at += Trimmed(target.Slice(at, count));
        target[at++] = (byte)'"';
        Value(at);
    }

    /// <summary>A date with its offset, as the writer writes one: the round-trip format with the fraction trimmed.</summary>
    public void StringValue(DateTimeOffset value)
    {
        var target = Reserve(MaxDate + 1);
        var at = Separator(target);
        target[at++] = (byte)'"';
        Utf8Formatter.TryFormat(value, target[at..], out var count, 'O');
        at += Trimmed(target.Slice(at, count));
        target[at++] = (byte)'"';
        Value(at);
    }

    /// <summary>Gives the scratch buffer back to the thread, and returns the pooled one if the run moved to one.</summary>
    public void Dispose()
    {
        _buffer = default;
        _length = 0;
        if (_rented is { } rented)
        {
            _rented = null;
            ArrayPool<byte>.Shared.Return(rented);
        }

        if (_scratch is { } scratch)
        {
            _scratch = null;
            t_scratch = scratch;
        }
    }

    // JSON has no literal for these, and the host's options do not allow the named ones: the writer throws here too,
    // and the response fails the same way.
    private static ArgumentException NotFinite(string value)
        => new($"{value} is not a finite number, and JSON has no literal for it.");

    // The round-trip format writes seven digits of fraction after the seconds; the writer drops its trailing zeros,
    // and the dot when nothing is left. Whatever follows them (Z, an offset, nothing) moves up.
    private static int Trimmed(Span<byte> formatted)
    {
        const int dot = 19;
        const int fractionEnd = 27;

        var last = fractionEnd - 1;
        while (last > dot && formatted[last] == (byte)'0')
            last--;

        var keep = last == dot ? dot : last + 1;
        formatted[fractionEnd..].CopyTo(formatted[keep..]);
        return keep + (formatted.Length - fractionEnd);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Open(byte token)
    {
        var target = Reserve(2);
        var at = Separator(target);
        target[at] = token;
        _length += at + 1;
        _separate = false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Close(byte token)
    {
        var target = Reserve(1);
        target[0] = token;
        _length++;
        _separate = true;
    }

    private void Literal(ReadOnlySpan<byte> literal)
    {
        var target = Reserve(literal.Length + 1);
        var at = Separator(target);
        literal.CopyTo(target[at..]);
        Value(at + literal.Length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly int Separator(Span<byte> target)
    {
        if (!_separate)
            return 0;

        target[0] = (byte)',';
        return 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Value(int written)
    {
        _length += written;
        _separate = true;
    }

    /// <summary>The free part of the buffer, at least <paramref name="size" /> bytes of it.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Span<byte> Reserve(int size)
    {
        if (_buffer.Length - _length < size)
            Grow(size);

        return _buffer[_length..];
    }

    private void Grow(int size)
    {
        var next = ArrayPool<byte>.Shared.Rent(Math.Max(_buffer.Length * 2, _length + size));
        _buffer[.._length].CopyTo(next);
        if (_rented is { } previous)
            ArrayPool<byte>.Shared.Return(previous);

        _rented = next;
        _buffer = next;
    }
}
