using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace MeetingTranscriber.App;

/// <summary>
/// What a screen reader, and the UI probe, see of a <see cref="DropDown"/>: a combo box that
/// expands, selects and reads its value.
/// </summary>
/// <remarks>
/// <para>
/// The entries of a popup constrained to the window's root are children of the window root in
/// UI Automation, not of the thing that opened them. While the list is open this hands them over:
/// its one child is the list, whose own peers are the entries, which is what lets <c>choose</c>
/// find them <em>under</em> the picker the way it found them under a <c>ComboBox</c>. Closed, it has
/// no children, as a <c>ComboBox</c>'s closed list had none.
/// </para>
/// <para>
/// The <c>AutomationId</c> a caller sets is the base class's and is not overridden here:
/// <c>ChannelStrip.Identity</c> and <c>OneOfThese.Build</c> address a pill by it.
/// </para>
/// </remarks>
internal sealed class DropDownAutomationPeer(DropDown owner)
    : FrameworkElementAutomationPeer(owner), IExpandCollapseProvider, ISelectionProvider, IValueProvider
{
    private readonly DropDown _drop = owner;

    /// <inheritdoc />
    public ExpandCollapseState ExpandCollapseState =>
        _drop.IsDropDownOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    /// <inheritdoc />
    public bool CanSelectMultiple => false;

    /// <inheritdoc />
    public bool IsSelectionRequired => false;

    /// <inheritdoc />
    public bool IsReadOnly => true;

    /// <inheritdoc />
    public string Value => _drop.SelectedText ?? string.Empty;

    /// <inheritdoc />
    public void Expand()
    {
        Enabled();
        _drop.IsDropDownOpen = true;
    }

    /// <inheritdoc />
    public void Collapse()
    {
        Enabled();
        _drop.IsDropDownOpen = false;
    }

    /// <inheritdoc />
    public IRawElementProviderSimple[] GetSelection()
    {
        if (_drop.IsDropDownOpen
            && _drop.Chosen is { } container
            && CreatePeerForElement(container) is { } peer)
        {
            return [ProviderFromPeer(peer)];
        }

        return [];
    }

    /// <inheritdoc />
    public void SetValue(string value) => throw new ElementNotEnabledException();

    /// <summary>Tells a client the list opened or closed.</summary>
    internal void Moved(bool wasOpen, bool isOpen)
    {
        // The children are the list while it is open and none while it is not, so a client that
        // read them last time is told to read them again.
        InvalidatePeer();

        RaisePropertyChangedEvent(
            ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
            wasOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed,
            isOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed);
    }

    /// <summary>Tells a client the chosen entry changed.</summary>
    internal void ValueMoved(string? before, string? after) =>
        RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, before ?? string.Empty, after ?? string.Empty);

    /// <inheritdoc />
    protected override string GetClassNameCore() => nameof(DropDown);

    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ComboBox;

    /// <inheritdoc />
    protected override object GetPatternCore(PatternInterface patternInterface) =>
        patternInterface is PatternInterface.ExpandCollapse or PatternInterface.Selection or PatternInterface.Value
            ? this
            : base.GetPatternCore(patternInterface);

    /// <inheritdoc />
    protected override IList<AutomationPeer> GetChildrenCore() =>
        _drop.IsDropDownOpen && _drop.TheList is { } list && CreatePeerForElement(list) is { } peer
            ? [peer]
            : [];

    private void Enabled()
    {
        if (!_drop.IsEnabled)
        {
            throw new ElementNotEnabledException();
        }
    }
}
