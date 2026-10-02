using Pragmatic.Internationalization.Attributes;

// The keys of translations/*.json as symbols — T.Validation.LeaveRequest.EndsBeforeItStarts — so no
// message key in the module is a string a typo can break. Keys, not the texts: the host reads the texts
// at run time, in the caller's language, and the same file serves both.
[assembly: TranslationKeys(EmbedTranslations = false)]
