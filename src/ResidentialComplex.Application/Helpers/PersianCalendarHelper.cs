using System.Globalization;

namespace ResidentialComplex.Application.Helpers;

/// <summary>
/// Utility methods for working with the Persian (Solar Hijri) calendar.
/// </summary>
public static class PersianCalendarHelper
{
    private static readonly PersianCalendar PersianCalendar = new();

    private static readonly string[] PersianMonthNames =
    {
        "", "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
        "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"
    };

    /// <summary>Returns the current Persian year.</summary>
    public static int GetCurrentYear() => PersianCalendar.GetYear(DateTime.Now);

    /// <summary>Returns the current Persian month (1-12).</summary>
    public static int GetCurrentMonth() => PersianCalendar.GetMonth(DateTime.Now);

    /// <summary>Returns the current Persian day of the month.</summary>
    public static int GetCurrentDay() => PersianCalendar.GetDayOfMonth(DateTime.Now);

    /// <summary>Returns the Persian month name for a given month number (1-12).</summary>
    public static string GetMonthName(int month)
    {
        if (month < 1 || month > 12) return month.ToString();
        return PersianMonthNames[month];
    }

    /// <summary>Formats a Persian year/month as "ماه سال" e.g. "فروردین ۱۴۰۵".</summary>
    public static string FormatYearMonth(int year, int month) => $"{GetMonthName(month)} {year}";

    /// <summary>Formats a DateTime to Persian date string (yyyy/MM/dd).</summary>
    public static string ToPersianDateString(DateTime date)
    {
        var y = PersianCalendar.GetYear(date);
        var m = PersianCalendar.GetMonth(date);
        var d = PersianCalendar.GetDayOfMonth(date);
        return $"{y}/{m:D2}/{d:D2}";
    }

    /// <summary>Formats a DateTime to Persian date and time string.</summary>
    public static string ToPersianDateTimeString(DateTime date)
    {
        return $"{ToPersianDateString(date)} {date:HH:mm}";
    }

    /// <summary>Returns the Persian year for a given DateTime.</summary>
    public static int GetYear(DateTime date) => PersianCalendar.GetYear(date);

    /// <summary>Returns the Persian month for a given DateTime.</summary>
    public static int GetMonth(DateTime date) => PersianCalendar.GetMonth(date);
}
