using Microsoft.UI.Xaml.Data;

using Duration = MeetingTranscriber.Domain.Time.Duration;

namespace MeetingTranscriber.App;

/// <summary>
/// What the player's track says when the thumb is held: the time it stands at, as the clock beside
/// the track writes it.
/// </summary>
/// <remarks>
/// The slider's own tooltip would print its value, and the track's value is the recording's offset
/// in milliseconds — a number nobody reads a meeting by, and the reason the thumb said
/// <c>1360000</c> over an hour-long recording.
/// </remarks>
public sealed class TrackTime : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        ScreenNumbers.Long(Duration.FromMilliseconds((long)Math.Max(0, System.Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture))));

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("A track's time is read, never typed.");
}
