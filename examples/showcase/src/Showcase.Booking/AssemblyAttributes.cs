using Pragmatic.Internationalization.Attributes;

// ByFile = false → hierarchy from JSON keys (T.Reservation.Title, T.Errors.ReservationNotFound)
// EmbedTranslations = true → values baked in at compile time, no runtime file loading
[assembly: TranslationKeys(ClassName = "T", EmbedTranslations = true, ByFile = false)]

// The templates embedded here (the guest's mails): every host that includes this module registers them.
[assembly: Pragmatic.Documents.Markup.PdxTemplates<Showcase.Booking.BookingModule>]
