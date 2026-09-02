namespace MeetingTranscriber.Presentation;

/// <summary>
/// What a screen calls one of this machine's audio endpoints in a list somebody picks from.
/// </summary>
/// <remarks>
/// <para>
/// The device's name is the maker's — it reads the same in both languages and is never
/// translated. Everything the application puts around that name is not: <em>default</em> is a
/// word this application chose to add, and a screen that added it itself would be saying one
/// word of English to somebody reading in Spanish. So the line is an entry of
/// <see cref="UiTexts"/> with the name inside it, and the only thing decided here is which entry
/// an endpoint gets.
/// </para>
/// <para>
/// It takes the two facts rather than the device, because the device is
/// <c>MeetingTranscriber.Audio</c>'s and this project references nothing: the catalogue owes
/// nothing to WASAPI, and the reason it is provable at all is that a test can load it without a
/// packaged host. The audio domain does not know what language a window is being read in and is
/// not given a way to find out.
/// </para>
/// </remarks>
public static class DeviceLines
{
    /// <summary>
    /// The line a picker shows for an endpoint: a <see cref="TextLine"/> because the two answers
    /// are different kinds of thing — one is a sentence with a value in it and the other is the
    /// value on its own — and that is the one distinction this type carries.
    /// </summary>
    /// <remarks>
    /// It refuses nothing. A name Windows came back blank about would draw an empty row, and an
    /// empty row is something somebody can see and pick around; a throw here lands in
    /// <c>FillThePickers</c>, which the recorder's constructor and the device-change callback both
    /// run, so the window would not open at all. The recorder already settled that trade in as
    /// many words on <c>MainWindow.Ask</c> — a window that will not start is worse than one that
    /// opens with an empty picker.
    /// </remarks>
    /// <param name="name">What the maker called it, as Windows reports it.</param>
    /// <param name="isDefault">Whether Windows uses this one when nothing else was asked for.</param>
    public static TextLine Of(string name, bool isDefault) =>
        isDefault ? TextLine.Says(UiTexts.TheDeviceWindowsUsesByDefault, name) : TextLine.Data(name);
}
