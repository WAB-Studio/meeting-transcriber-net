using MeetingTranscriber.Recording;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace MeetingTranscriber.App;

/// <summary>
/// One channel of the recording: the picker that chooses its source, and the meter of what that
/// source is bringing in, drawn as one thing. <c>docs/design.md</c> §Where it goes is what it is
/// built from — the meter is pinned to the control that chooses its source.
/// </summary>
/// <remarks>
/// One control and two channels, so it knows it is a channel and never which. Every word on it and
/// everything its picker offers come from the window: the two are the contract's own — channel 0 is
/// the loopback and channel 1 is the microphone — and a control that decided which it was would be
/// a second place that has to agree with <c>CapturedAudio</c>.
/// </remarks>
public sealed partial class ChannelStrip : UserControl
{
    /// <summary>
    /// Raised when somebody picks something in this strip's picker — never when the window is the
    /// one putting the answers in, which is what <see cref="Offer"/> is careful about.
    /// </summary>
    public event EventHandler<int>? Chose;

    /// <summary>True while <see cref="Offer"/> is filling the picker, so refilling is not a pick.</summary>
    private bool _filling;

    public ChannelStrip() => InitializeComponent();

    /// <summary>
    /// What a test or a tool finds this strip's three elements by. It stays the same in every
    /// language, which is why it is not a text from the catalogue and why it is not set beside the
    /// words.
    /// </summary>
    /// <remarks>
    /// Set from outside and once, because this is one control used twice: its own <c>x:Name</c>s
    /// are the same on both strips, and two elements answering to one id is a probe that cannot say
    /// which picker it just chose in. The picker keeps the name it has always had, which is what
    /// ISC-158.1's recorded evidence and every walk written against this screen ask for; the chip
    /// and the role take that name and a suffix.
    /// </remarks>
    public string Identity
    {
        get => AutomationProperties.GetAutomationId(Picker);
        set
        {
            AutomationProperties.SetAutomationId(Picker, value);
            AutomationProperties.SetAutomationId(Chip, value + "Channel");
            AutomationProperties.SetAutomationId(Role, value + "Role");
        }
    }

    /// <summary>Whether anything can be chosen in the picker.</summary>
    public bool PickerIsLive
    {
        get => Picker.IsEnabled;
        set => Picker.IsEnabled = value;
    }

    /// <summary>What the meter is showing, in the words the window worded them in.</summary>
    public string LoudnessSaid
    {
        get => Meter.LoudnessSaid;
        set => Meter.LoudnessSaid = value;
    }

    /// <summary>What the window wrote about the loudest moment, or nothing.</summary>
    public string LoudestSoFarSaid
    {
        get => Meter.LoudestSoFarSaid;
        set => Meter.LoudestSoFarSaid = value;
    }

    /// <summary>Says which channel this is: the chip, the role beside it, and what the picker chooses.</summary>
    /// <param name="channel">The channel's number, as the mono chip the artboards draw.</param>
    /// <param name="role">What this channel is for, in words: the others, or you.</param>
    /// <param name="picker">What the picker chooses, for somebody who cannot see the two texts.</param>
    /// <remarks>
    /// One call and not three properties, because a strip described in part is a strip that looks
    /// finished: set two of the three and the missing one is silently empty, and one of them —
    /// <paramref name="role"/> — is also the only thing that names the bar to a screen reader, so
    /// forgetting it leaves the one element here carrying a value announcing nothing at all.
    /// </remarks>
    public void Describe(string channel, string role, string picker)
    {
        Chip.Text = channel;
        Role.Text = role;
        Meter.ChannelName = role;

        // The pill carries no header — that is what lets it stand at the control rank's 34 — so
        // this is the only thing naming it, and it is what the empty pill says as well: a picker
        // with nothing chosen and nothing beside it is a box somebody has to guess at.
        AutomationProperties.SetName(Picker, picker);
        Picker.PlaceholderText = picker;
    }

    /// <summary>What the picker offers, and which of them is the answer.</summary>
    /// <param name="offered">Every source this channel could be pointed at.</param>
    /// <param name="chosen">Which of them is chosen, or <c>-1</c> when none is.</param>
    public void Offer(IReadOnlyList<string> offered, int chosen)
    {
        ArgumentNullException.ThrowIfNull(offered);

        _filling = true;
        try
        {
            Picker.ItemsSource = offered;
            Picker.SelectedIndex = chosen;
        }
        finally
        {
            _filling = false;
        }
    }

    /// <summary>
    /// Draws <paramref name="reading"/>, or the bar with nothing on it, and hands back the loudest
    /// this source has reached for the window to word.
    /// </summary>
    /// <remarks>
    /// The peak comes back from the call that moves it rather than off a property beside it. Read
    /// separately it is a two-step whose order only a comment carries: drawing is what moves the
    /// peak, so a window that worded it first would print the moment before this one, for ever,
    /// with nothing on screen that looked wrong.
    /// </remarks>
    public float? Show(ChannelReading? reading)
    {
        Meter.Show(reading);
        return Meter.LoudestSoFar;
    }

    /// <summary>Forgets the loudest moment, which a new meeting's meter has none of.</summary>
    public void ForgetTheLoudestMoment() => Meter.ForgetTheLoudestMoment();

    private void OnChosen(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || Picker.SelectedIndex < 0)
        {
            return;
        }

        Chose?.Invoke(this, Picker.SelectedIndex);
    }
}
