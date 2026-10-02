using Pragmatic.Internationalization.Attributes;

// The keys of translations/*.json as symbols. One declaration per module, because each module carries its
// own messages: Registry answers about companies and customers, Billing about invoices and payments.
[assembly: TranslationKeys(EmbedTranslations = false)]
