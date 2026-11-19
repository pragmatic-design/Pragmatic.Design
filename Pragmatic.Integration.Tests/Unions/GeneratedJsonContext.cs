// The net11.0 build of this project generates its JSON context, as an AOT application does, so a union an operation answers with goes through
// what the generator wrote for it rather than through System.Text.Json's reflection (#838). Unions/ builds on net11.0 only.
[assembly: Pragmatic.Serialization.PragmaticGenerateJsonContext]
