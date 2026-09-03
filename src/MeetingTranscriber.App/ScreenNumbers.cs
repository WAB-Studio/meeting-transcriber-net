using System.Globalization;

using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.App;

/// <summary>
/// How every screen writes the machine's own numbers about a meeting: when it was, how long it
/// ran, and where in it something was said.
/// </summary>
/// <remarks>
/// <para>
/// Not words. Nothing here goes through <c>UiTexts</c> and nothing here changes with the language,
/// because these are data — a reader compares them to each other rather than reading them as a
/// sentence, and a date that moved between the two languages would be one more thing to hold in
/// your head while doing it.
/// </para>
/// <para>
/// One place because two screens now write them and a third will. A meeting reading
/// <c>1:12:04</c> on the list and <c>72:04</c> on its own screen is the same number said twice,
/// and somebody would have to work out that it is.
/// </para>
/// </remarks>
internal static class ScreenNumbers
{
    /// <summary>
    /// When the meeting was and how long it ran, as one line.
    /// </summary>
    /// <remarks>
    /// The length comes after it where there is one — a meeting still being recorded, and one
    /// whose recording never finished, have none.
    /// </remarks>
    public static string When(Meeting meeting)
    {
        ArgumentNullException.ThrowIfNull(meeting);

        return meeting.Duration is { } length
            ? Beside(At(meeting.StartedAt), Long(length))
            : At(meeting.StartedAt);
    }

    /// <summary>
    /// Several facts about one thing, as the one line of data they are read as.
    /// </summary>
    /// <remarks>
    /// The separator is here and in no screen, which is what makes it one separator: three screens
    /// each spelling their own is three lines that look almost alike. It is also the only reason
    /// this method exists — a punctuation mark typed into a screen is a literal on a line a person
    /// reads, and <c>ScreenTextsTests</c> is right to refuse it there.
    /// </remarks>
    public static string Beside(params string[] facts) => string.Join(" · ", facts);

    /// <summary>
    /// One thing inside another inside another — a path down the classification tree, root first.
    /// </summary>
    /// <remarks>
    /// A second separator and not <see cref="Beside"/>, because it says something else: those are
    /// facts standing next to each other and these are one thing containing the next, so
    /// <em>TechSed › Soporte › ticket #4312</em> reads as a place and <em>TechSed · Soporte ·
    /// ticket #4312</em> would read as three of them. It is here for the reason that one is: a
    /// punctuation mark typed into a screen is a literal on a line a person reads.
    /// </remarks>
    public static string Inside(params string[] path) => string.Join(" › ", path);

    /// <summary>
    /// When something was, to the minute and never to the second: what tells two meetings apart on
    /// a list is which one it was, not how far into a minute it started.
    /// </summary>
    public static string At(UtcTimestamp moment) => moment.Value.ToLocalTime()
        .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// What time of day it was, with no date on it, for a moment inside something the reader is
    /// already in the middle of.
    /// </summary>
    /// <remarks>
    /// The date is what tells two meetings apart on a list and there is nothing to tell apart
    /// inside one: somebody watching a recording being made and reading that a device was cut off
    /// knows what day it is, and a date in that sentence is a fact they are asked to skip over to
    /// reach the one they need. The same minute and the same clock as <see cref="At"/>, which is
    /// why the two are here together and not one of them typed into a screen.
    /// </remarks>
    public static string TimeOfDay(UtcTimestamp moment) => moment.Value.ToLocalTime()
        .ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// The year something began, which is the whole of what an affiliation says about when.
    /// </summary>
    /// <remarks>
    /// A year and not a date. A period on a person is read against another person's and against the
    /// meeting's own — <em>did this hold when this happened</em> — and the month and the day would
    /// be two facts nobody compares standing in front of the one they do. It is here rather than in
    /// <c>UiTexts</c> because it is the same number in either language; the sentence it goes inside
    /// is the catalogue's, and that sentence is text rather than mono for the reason
    /// <c>docs/design.md</c> §Type gives — a number that is part of a sentence is text.
    /// </remarks>
    public static string Year(UtcTimestamp moment) =>
        moment.Value.ToLocalTime().ToString("yyyy", CultureInfo.InvariantCulture);

    /// <summary>
    /// The instant a year begins, as the person typing it means it.
    /// </summary>
    /// <remarks>
    /// The exact inverse of <see cref="Year"/>, and it is here beside it because that is the whole
    /// of what makes it right. <see cref="Year"/> reads an instant in whatever zone the reader is
    /// in, so a start written as midnight UTC comes back a year early for everybody west of it: on
    /// this machine, typing 2024 and being told <em>desde 2023</em>, permanently and with no screen
    /// that can correct it. Midnight local is the answer that round trips, and it is also what
    /// somebody means when they write a year down.
    /// </remarks>
    public static UtcTimestamp TheStartOfTheYear(int year) =>
        UtcTimestamp.From(new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Local));

    /// <summary>
    /// How long something ran, or how far into a meeting something is.
    /// </summary>
    /// <remarks>
    /// One format for both, and hours are always there. A player counting <c>04:47</c> beside a
    /// citation reading <c>1:04:47</c> would be two clocks on one screen, and the hour is the
    /// difference between the two.
    /// </remarks>
    public static string Long(Duration length) =>
        length.ToTimeSpan().ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture);
}
