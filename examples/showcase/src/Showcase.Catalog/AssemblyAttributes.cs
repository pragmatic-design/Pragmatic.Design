using Pragmatic.Internationalization.Attributes;

// ByFile = false → hierarchy from JSON keys (T.Property.Title, T.Errors.NotFound)
// EmbedTranslations = true → values baked in at compile time, no runtime file loading
[assembly: TranslationKeys(ClassName = "T", EmbedTranslations = true, ByFile = false)]
