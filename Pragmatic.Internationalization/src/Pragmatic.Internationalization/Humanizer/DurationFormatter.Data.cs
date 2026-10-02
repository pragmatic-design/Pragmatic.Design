using System.Collections.Frozen;

namespace Pragmatic.Internationalization.Humanizer;

/// <summary>
///     Per-language suffix / word tables consumed by <see cref="DurationFormatter"/>.
/// </summary>
public sealed partial class DurationFormatter
{
    // Languages that require plural rules for "few" form in long format
    private static readonly FrozenSet<string> SlavicLanguages =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ru", "pl", "cs", "sk", "uk", "hr", "sr", "bs" }
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, DurationSuffixes> SShortSuffixes =
        new Dictionary<string, DurationSuffixes>(StringComparer.OrdinalIgnoreCase)
        {
        ["en"] = new DurationSuffixes("d", "h", "m", "s", "ms"),
        ["it"] = new DurationSuffixes("g", "h", "m", "s", "ms"),
        ["de"] = new DurationSuffixes("T", "Std", "Min", "Sek", "ms"),
        ["fr"] = new DurationSuffixes("j", "h", "min", "s", "ms"),
        ["es"] = new DurationSuffixes("d", "h", "min", "s", "ms"),
        ["pt"] = new DurationSuffixes("d", "h", "min", "s", "ms"),
        ["ru"] = new DurationSuffixes("д", "ч", "мин", "с", "мс"),
        ["zh"] = new DurationSuffixes("天", "小时", "分", "秒", "毫秒"),
        ["ja"] = new DurationSuffixes("日", "時間", "分", "秒", "ミリ秒"),
        ["ko"] = new DurationSuffixes("일", "시간", "분", "초", "밀리초"),
        ["ar"] = new DurationSuffixes("ي", "س", "د", "ث", "مل"),
        ["nl"] = new DurationSuffixes("d", "u", "m", "s", "ms"),
        ["pl"] = new DurationSuffixes("d", "godz", "min", "s", "ms"),
        ["sv"] = new DurationSuffixes("d", "tim", "min", "sek", "ms"),
        ["no"] = new DurationSuffixes("d", "t", "min", "sek", "ms"),
        ["da"] = new DurationSuffixes("d", "t", "min", "sek", "ms"),
        ["fi"] = new DurationSuffixes("pv", "t", "min", "s", "ms")
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, DurationWords> SLongSuffixes =
        new Dictionary<string, DurationWords>(StringComparer.OrdinalIgnoreCase)
        {
        ["en"] = new DurationWords(
            ("day", "days", "days"), ("hour", "hours", "hours"), ("minute", "minutes", "minutes"),
            ("second", "seconds", "seconds"), ("millisecond", "milliseconds", "milliseconds")),
        ["it"] = new DurationWords(
            ("giorno", "giorni", "giorni"), ("ora", "ore", "ore"), ("minuto", "minuti", "minuti"),
            ("secondo", "secondi", "secondi"), ("millisecondo", "millisecondi", "millisecondi")),
        ["de"] = new DurationWords(
            ("Tag", "Tage", "Tage"), ("Stunde", "Stunden", "Stunden"), ("Minute", "Minuten", "Minuten"),
            ("Sekunde", "Sekunden", "Sekunden"), ("Millisekunde", "Millisekunden", "Millisekunden")),
        ["fr"] = new DurationWords(
            ("jour", "jours", "jours"), ("heure", "heures", "heures"), ("minute", "minutes", "minutes"),
            ("seconde", "secondes", "secondes"), ("milliseconde", "millisecondes", "millisecondes")),
        ["es"] = new DurationWords(
            ("día", "días", "días"), ("hora", "horas", "horas"), ("minuto", "minutos", "minutos"),
            ("segundo", "segundos", "segundos"), ("milisegundo", "milisegundos", "milisegundos")),
        ["pt"] = new DurationWords(
            ("dia", "dias", "dias"), ("hora", "horas", "horas"), ("minuto", "minutos", "minutos"),
            ("segundo", "segundos", "segundos"), ("milissegundo", "milissegundos", "milissegundos")),
        ["ru"] = new DurationWords(
            ("день", "дней", "дня"), ("час", "часов", "часа"), ("минута", "минут", "минуты"),
            ("секунда", "секунд", "секунды"), ("миллисекунда", "миллисекунд", "миллисекунды")),
        ["nl"] = new DurationWords(
            ("dag", "dagen", "dagen"), ("uur", "uren", "uren"), ("minuut", "minuten", "minuten"),
            ("seconde", "seconden", "seconden"), ("milliseconde", "milliseconden", "milliseconden")),
        ["pl"] = new DurationWords(
            ("dzień", "dni", "dni"), ("godzina", "godzin", "godziny"), ("minuta", "minut", "minuty"),
            ("sekunda", "sekund", "sekundy"), ("milisekunda", "milisekund", "milisekundy"))
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
}
