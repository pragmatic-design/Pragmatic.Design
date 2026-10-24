using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Pragmatic.Persistence.Sql.Execution;

/// <summary>A cursor token that is not what it claims: tampered with, from another key or purpose, truncated, or not of the shape the caller asked for.</summary>
public sealed class SqlCursorException(string message) : Exception(message);
