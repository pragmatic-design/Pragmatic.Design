using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Pragmatic.Persistence.Sql.Planning;

/// <summary>Writes the parameter values of one call into the command: the arguments in the slots, the constants, and the paging.</summary>
public delegate void SqlBind<T>(in SqlQuery<T> query, DbParameterCollection parameters, SqlDialect dialect);
