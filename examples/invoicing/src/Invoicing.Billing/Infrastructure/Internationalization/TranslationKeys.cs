using Pragmatic.Internationalization.Attributes;

// The keys of translations/*.json as symbols — T.Invoice.Document.Total, TKeys.Error.Overpayment.Detail —
// so no message key in this module is a string a typo can break, and a key one language has and the other
// lacks is a build warning (PRAG1802) instead of a blank on a customer's invoice. Keys, not the texts: the
// host reads the texts at run time, in the language the answer is owed in.
[assembly: TranslationKeys(EmbedTranslations = false)]
