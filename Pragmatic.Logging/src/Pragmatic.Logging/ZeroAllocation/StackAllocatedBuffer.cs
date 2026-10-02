using System.Buffers;
using System.Runtime.CompilerServices;

namespace Pragmatic.Logging.ZeroAllocation;

/// <summary>
/// Delegate for operations on spans that avoids closure allocations.
/// </summary>
/// <param name="span">The span to operate on</param>
public delegate void SpanAction<T>(Span<T> span);

/// <summary>
/// Interface for span callback operations to work around C# 12 limitations with Func{Span{T}, TResult}.
/// </summary>
/// <typeparam name="TResult">The result type</typeparam>
/// <typeparam name="TState">The state parameter type</typeparam>
public interface ISpanCallback<out TResult, in TState>
{
    /// <summary>
    /// Executes the callback with the provided span and state.
    /// </summary>
    /// <param name="span">The span to operate on</param>
    /// <param name="state">The state parameter</param>
    /// <returns>The result</returns>
    TResult Execute(Span<char> span, TState state);
}

/// <summary>
/// Provides utilities for working with stack-allocated buffers and array pools.
/// </summary>
public static class StackAllocatedBuffer
{
    /// <summary>
    /// The threshold above which to use array pool instead of stack allocation.
    /// </summary>
    public const int StackAllocThreshold = 1024;

    /// <summary>
    /// Executes an action with either a stack-allocated buffer or a rented array.
    /// </summary>
    /// <param name="length">The required buffer length</param>
    /// <param name="action">The action to execute with the buffer</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WithBuffer(int length, SpanAction<char> action)
    {
        if (length <= StackAllocThreshold)
        {
            Span<char> buffer = stackalloc char[length];
            action(buffer);
        }
        else
        {
            char[] rented = ArrayPool<char>.Shared.Rent(length);
            try
            {
                action(rented.AsSpan(0, length));
            }
            finally
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }
    }

    /// <summary>
    /// Executes a function with either a stack-allocated buffer or a rented array using a callback interface.
    /// </summary>
    /// <typeparam name="T">The return type</typeparam>
    /// <typeparam name="TState">The state parameter type</typeparam>
    /// <param name="length">The required buffer length</param>
    /// <param name="state">The state to pass to the callback</param>
    /// <param name="callback">The callback to execute with the buffer</param>
    /// <returns>The result of the function</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T WithBuffer<T, TState>(int length, TState state, ISpanCallback<T, TState> callback)
    {
        if (length <= StackAllocThreshold)
        {
            Span<char> buffer = stackalloc char[length];
            return callback.Execute(buffer, state);
        }
        else
        {
            char[] rented = ArrayPool<char>.Shared.Rent(length);
            try
            {
                return callback.Execute(rented.AsSpan(0, length), state);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }
    }

}