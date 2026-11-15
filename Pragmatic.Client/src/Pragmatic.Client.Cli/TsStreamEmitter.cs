using System.Text;

namespace Pragmatic.Client.Cli;

/// <summary>
///     The parts of <c>client.ts</c> an <c>[Sse]</c> endpoint needs: the event type a stream yields, the
///     <c>ApiStreamError</c> it throws, and the reader that turns a <c>text/event-stream</c> body into events.
/// </summary>
/// <remarks>
///     <para>
///         A stream is an <c>AsyncGenerator</c> of the event type, not an <c>ApiResult</c>: its values arrive
///         one at a time over one response, and a failure can arrive after some of them. So a failure is
///         thrown — the response refused, or the server's terminal <c>event: error</c> — as an
///         <c>ApiStreamError</c> carrying the same <c>ApiError</c> the request-response methods return.
///     </para>
///     <para>
///         ⚠️ The reader follows the format <c>SseStreamResult</c> writes: one <c>data:</c> line of JSON per
///         event, a blank line between events, <c>: hb</c> comments as keep-alive, and a terminal
///         <c>event: error</c>. <c>ApiStreamError</c> is declared in <c>client.ts</c> rather than
///         <c>errors.ts</c> so the file's other imports stay type-only.
///     </para>
/// </remarks>
internal static class TsStreamEmitter
{
    /// <summary>
    ///     The event type, from the manifest's response type: <c>IAsyncEnumerable&lt;Result&lt;T, E&gt;&gt;</c>,
    ///     <c>IAsyncEnumerable&lt;T&gt;</c> and the bare item type are all <c>T</c>.
    /// </summary>
    public static string? ItemClrType(string? clrType)
    {
        if (clrType is null)
            return null;

        var type = clrType.Replace("global::", "").Trim();
        foreach (var wrapper in new[] { "System.Collections.Generic.IAsyncEnumerable<", "IAsyncEnumerable<" })
        {
            if (type.StartsWith(wrapper, StringComparison.Ordinal) && type.EndsWith(">", StringComparison.Ordinal))
            {
                type = type[wrapper.Length..^1].Trim();
                break;
            }
        }

        foreach (var result in new[] { "Pragmatic.Result.Result<", "Result<" })
        {
            if (type.StartsWith(result, StringComparison.Ordinal) && type.EndsWith(">", StringComparison.Ordinal))
                return FirstTypeArgument(type[result.Length..^1]);
        }

        return type;
    }

    /// <summary>The first argument of a generic argument list, split at its top-level comma.</summary>
    private static string FirstTypeArgument(string arguments)
    {
        var depth = 0;
        for (var i = 0; i < arguments.Length; i++)
        {
            switch (arguments[i])
            {
                case '<': depth++; break;
                case '>': depth--; break;
                case ',' when depth == 0: return arguments[..i].Trim();
            }
        }

        return arguments.Trim();
    }

    /// <summary>The <c>ApiStreamError</c> class, emitted once when the module has a stream.</summary>
    public static void AppendStreamError(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("/** Thrown by a stream method: the response was refused, or the server ended the stream with an error. */");
        sb.AppendLine("export class ApiStreamError extends Error {");
        sb.AppendLine("  readonly error: ApiError;");
        sb.AppendLine("  constructor(error: ApiError) {");
        sb.AppendLine("    super(error.title ?? error.code);");
        sb.AppendLine("    this.error = error;");
        sb.AppendLine("  }");
        sb.AppendLine("}");
    }

    /// <summary>The private reader every stream method returns through.</summary>
    public static void AppendStreamReader(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("  private async *stream<TItem>(method: string, url: string, body?: unknown): AsyncGenerator<TItem, void, undefined> {");
        sb.AppendLine("    const doFetch = this.options?.fetchImpl ?? fetch;");
        sb.AppendLine("    const response = await doFetch(this.baseUrl + url, {");
        sb.AppendLine("      method,");
        sb.AppendLine("      headers: { accept: 'text/event-stream', 'content-type': 'application/json', ...this.options?.headers },");
        sb.AppendLine("      body: body === undefined ? undefined : JSON.stringify(body),");
        sb.AppendLine("    });");
        sb.AppendLine("    if (!response.ok || response.body === null) {");
        sb.AppendLine("      const text = await response.text();");
        sb.AppendLine("      const problem = (text.length > 0 ? JSON.parse(text) : {}) as Partial<ApiError>;");
        sb.AppendLine("      throw new ApiStreamError({ code: problem.code ?? 'unknown', title: problem.title, detail: problem.detail, status: response.status });");
        sb.AppendLine("    }");
        sb.AppendLine("    const reader = response.body.pipeThrough(new TextDecoderStream()).getReader();");
        sb.AppendLine("    let buffer = '';");
        sb.AppendLine("    try {");
        sb.AppendLine("      for (;;) {");
        sb.AppendLine("        const { value, done } = await reader.read();");
        sb.AppendLine("        if (done) return;");
        sb.AppendLine("        buffer += value.replace(/\\r\\n/g, '\\n');");
        sb.AppendLine("        let end: number;");
        sb.AppendLine("        while ((end = buffer.indexOf('\\n\\n')) >= 0) {");
        sb.AppendLine("          const frame = buffer.slice(0, end);");
        sb.AppendLine("          buffer = buffer.slice(end + 2);");
        sb.AppendLine("          let event = 'message';");
        sb.AppendLine("          const data: string[] = [];");
        sb.AppendLine("          for (const line of frame.split('\\n')) {");
        sb.AppendLine("            if (line.startsWith('event:')) event = line.slice(6).trim();");
        sb.AppendLine("            else if (line.startsWith('data:')) data.push(line.slice(5).replace(/^ /, ''));");
        sb.AppendLine("          }");
        sb.AppendLine("          // A frame with no data is a keep-alive comment (': hb').");
        sb.AppendLine("          if (data.length === 0) continue;");
        sb.AppendLine("          const payload = JSON.parse(data.join('\\n'));");
        sb.AppendLine("          if (event === 'error') {");
        sb.AppendLine("            const problem = payload as Partial<ApiError>;");
        sb.AppendLine("            throw new ApiStreamError({ code: problem.code ?? 'unknown', title: problem.title, detail: problem.detail, status: problem.status ?? response.status });");
        sb.AppendLine("          }");
        sb.AppendLine("          yield payload as TItem;");
        sb.AppendLine("        }");
        sb.AppendLine("      }");
        sb.AppendLine("    } finally {");
        sb.AppendLine("      await reader.cancel();");
        sb.AppendLine("    }");
        sb.AppendLine("  }");
    }
}
