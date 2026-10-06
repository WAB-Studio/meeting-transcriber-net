using MeetingTranscriber.Presentation;

using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace MeetingTranscriber.App;

/// <summary>
/// The application's own drop-down: a pill carrying the chosen entry and a list that opens under
/// it, or over it when there is no room below, always inside the window.
/// </summary>
/// <remarks>
/// <para>
/// It replaces the platform's <c>ComboBox</c> everywhere, with exactly the surface the screens use
/// — <see cref="ItemsSource"/>, <see cref="SelectedIndex"/>, <see cref="PlaceholderText"/>,
/// <see cref="IsDropDownOpen"/>, <see cref="MaxDropDownHeight"/>, <see cref="SelectionChanged"/>
/// and <see cref="DropDownOpened"/> — so a screen changes the element's name and nothing else. A
/// <c>ComboBox</c>'s list is a window the platform places, and three batches of moving it were seen
/// on screen to be misplaced three times; this one is placed by <see cref="ListPlacement"/> against
/// the root of the window the pill is in, and a popup constrained to that root is placed in the
/// same root, which holds inside a <c>ContentDialog</c> too.
/// </para>
/// <para>
/// <see cref="SelectionChanged"/> is raised whenever <see cref="SelectedIndex"/> changes, whether
/// code or a person changed it. That is the platform's own rule and every <c>_filling</c> guard on
/// every screen is written against it. Setting <see cref="ItemsSource"/> empties the selection and
/// raises it when one stood.
/// </para>
/// <para>
/// A press on the pill while the list is open closes it and does not open it again. The press that
/// light-dismisses the list is the same press that reaches the pill, and in which order they arrive
/// is the platform's, so the pill asks both ways: whether the list was open when the press came, or
/// closed an instant before it. How long "an instant" is, and the rule as a whole, is
/// <see cref="PillPress.ClosesTheList"/>.
/// </para>
/// </remarks>
public sealed partial class DropDown : Control
{
    /// <summary>The index of the chosen entry, or −1 for none.</summary>
    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(
        nameof(SelectedIndex),
        typeof(int),
        typeof(DropDown),
        new PropertyMetadata(-1, OnSelectedIndexChanged));

    /// <summary>What the pill says while nothing is chosen.</summary>
    public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
        nameof(PlaceholderText),
        typeof(string),
        typeof(DropDown),
        new PropertyMetadata(null, OnAppearanceChanged));

    /// <summary>Whether the list is open.</summary>
    public static readonly DependencyProperty IsDropDownOpenProperty = DependencyProperty.Register(
        nameof(IsDropDownOpen),
        typeof(bool),
        typeof(DropDown),
        new PropertyMetadata(false, OnOpenChanged));

    /// <summary>The tallest the open list gets before it scrolls.</summary>
    public static readonly DependencyProperty MaxDropDownHeightProperty = DependencyProperty.Register(
        nameof(MaxDropDownHeight),
        typeof(double),
        typeof(DropDown),
        new PropertyMetadata((8 * 34d) + 10));

    private IReadOnlyList<string>? _items;
    private Entry[] _entries = [];

    private Border? _pill;
    private TextBlock? _value;
    private Popup? _popup;
    private Border? _listBorder;
    private ListView? _list;

    private bool _isOpen;
    private bool _syncing;
    private bool _over;
    private bool _pressed;
    private bool _openAtPress;
    private bool _refocus;

    // Nothing until a press light-dismisses the list. It was `long.MinValue` for "never", and
    // `Environment.TickCount64 - long.MinValue` overflows to a negative number below the window, so
    // every press on a fresh pill was read as the one that had just dismissed the list and the pill
    // never opened under a pointer.
    private long? _dismissedAt;
    private XamlRoot? _watching;

    /// <summary>Makes the drop-down. Its look is the style a screen gives it, and none is given by default.</summary>
    public DropDown()
    {
        IsTabStop = true;

        // Eight entries, the list's own 4 above and below and its 1px rule either side: the ceiling
        // a list has when a screen does not say another. Read off the control height so the two
        // never drift; the metadata default is the same number for a height that is not found.
        if (Application.Current?.Resources.TryGetValue("ControlHeight", out var height) == true
            && height is double rank)
        {
            MaxDropDownHeight = (8 * rank) + 10;
        }

        IsEnabledChanged += (_, _) => UpdateStates();

        // A control that leaves the tree while its list is open — a panel rebuilt under it — takes
        // the list with it.
        Unloaded += (_, _) => IsDropDownOpen = false;
    }

    /// <summary>Raised whenever <see cref="SelectedIndex"/> changes, by code or by a person.</summary>
    public event SelectionChangedEventHandler? SelectionChanged;

    /// <summary>Raised when the list has opened.</summary>
    public event EventHandler<object>? DropDownOpened;

    /// <summary>
    /// What the list offers. Set from code only; setting it empties the selection, and raises
    /// <see cref="SelectionChanged"/> when one stood.
    /// </summary>
    public IReadOnlyList<string>? ItemsSource
    {
        get => _items;
        set
        {
            SelectedIndex = -1;

            _items = value;
            _entries = value is null ? [] : [.. value.Select((text, index) => new Entry(index, text))];

            if (_list is not null)
            {
                _list.ItemsSource = _entries;
            }

            Refresh();

            // Set while the list is open — the recorder's pickers read the machine again as they open
            // — so what is on screen is the new list, placed for its new size.
            if (_isOpen && XamlRoot?.Content is FrameworkElement root)
            {
                _listBorder?.UpdateLayout();
                Place(root);
            }
        }
    }

    /// <summary>The index of the chosen entry, or −1 for none.</summary>
    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    /// <summary>What the pill says while nothing is chosen.</summary>
    public string? PlaceholderText
    {
        get => (string?)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    /// <summary>Whether the list is open.</summary>
    public bool IsDropDownOpen
    {
        get => (bool)GetValue(IsDropDownOpenProperty);
        set => SetValue(IsDropDownOpenProperty, value);
    }

    /// <summary>The tallest the open list gets before it scrolls: eight entries by default.</summary>
    public double MaxDropDownHeight
    {
        get => (double)GetValue(MaxDropDownHeightProperty);
        set => SetValue(MaxDropDownHeightProperty, value);
    }

    /// <summary>The words of the chosen entry, or nothing.</summary>
    internal string? SelectedText => At(SelectedIndex);

    /// <summary>The open list, which is where the entries are, for the peer to hand over.</summary>
    internal UIElement? TheList => _list;

    /// <summary>What the open list drew for the chosen entry, if it drew one.</summary>
    internal UIElement? Chosen => SelectedIndex >= 0 ? _list?.ContainerFromIndex(SelectedIndex) as UIElement : null;

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        Unhook();

        _pill = GetTemplateChild("Pill") as Border;
        _value = GetTemplateChild("Value") as TextBlock;
        _popup = GetTemplateChild("Popup") as Popup;
        _listBorder = GetTemplateChild("ListBorder") as Border;
        _list = GetTemplateChild("List") as ListView;

        if (_pill is not null)
        {
            _pill.PointerEntered += OnPillEntered;
            _pill.PointerExited += OnPillExited;
            _pill.PointerPressed += OnPillPressed;
            _pill.PointerReleased += OnPillReleased;
            _pill.PointerCanceled += OnPillReleased;
            _pill.PointerCaptureLost += OnPillReleased;
            _pill.Tapped += OnPillTapped;
        }

        if (_popup is not null)
        {
            _popup.Closed += OnPopupClosed;
        }

        if (_list is not null)
        {
            _list.ItemsSource = _entries;
            _list.ItemClick += OnItemClicked;
            _list.SelectionChanged += OnListSelectionChanged;
            _list.KeyDown += OnListKeyDown;
        }

        Refresh();
        UpdateStates();
    }

    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer() => new DropDownAutomationPeer(this);

    /// <inheritdoc />
    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        base.OnKeyDown(e);

        // Keys typed inside the open list bubble up through the popup to here, and the list has
        // already answered them.
        if (e.Handled || !ReferenceEquals(e.OriginalSource, this) || !IsEnabled)
        {
            return;
        }

        var altDown = e.Key == VirtualKey.Down
            && InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).HasFlag(CoreVirtualKeyStates.Down);

        if (e.Key is VirtualKey.F4 || altDown)
        {
            Toggle();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape && IsDropDownOpen)
        {
            _refocus = true;
            IsDropDownOpen = false;
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnKeyUp(KeyRoutedEventArgs e)
    {
        base.OnKeyUp(e);

        // Space and Enter act on release, and the list takes the keyboard after the press: opened on
        // the press, the release would land on the list's first entry and pick it.
        if (!e.Handled && ReferenceEquals(e.OriginalSource, this) && IsEnabled
            && e.Key is VirtualKey.Space or VirtualKey.Enter)
        {
            Toggle();
            e.Handled = true;
        }
    }

    private void Toggle()
    {
        _refocus = IsDropDownOpen;
        IsDropDownOpen = !IsDropDownOpen;
    }

    /// <summary>What stands at <paramref name="index"/>, or nothing when that is not an entry.</summary>
    internal string? At(int index) => index >= 0 && index < _entries.Length ? _entries[index].Text : null;

    private static void OnSelectedIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var drop = (DropDown)d;
        var before = drop.At((int)e.OldValue);
        var after = drop.At((int)e.NewValue);

        drop.Refresh();

        if (FrameworkElementAutomationPeer.FromElement(drop) is DropDownAutomationPeer peer)
        {
            peer.ValueMoved(before, after);
        }

        drop.SelectionChanged?.Invoke(
            drop,
            new SelectionChangedEventArgs(
                before is null ? Array.Empty<object>() : [before],
                after is null ? Array.Empty<object>() : [after]));
    }

    private static void OnAppearanceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((DropDown)d).Refresh();

    private static void OnOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var drop = (DropDown)d;

        if ((bool)e.NewValue)
        {
            if (!drop.TryOpen())
            {
                drop.IsDropDownOpen = false;
                return;
            }

            drop._isOpen = true;
            drop.Announce(false, true);
        }
        else if (drop._isOpen)
        {
            drop._isOpen = false;
            drop.Close();
            drop.Announce(true, false);
        }
    }

    private void Announce(bool was, bool now)
    {
        if (FrameworkElementAutomationPeer.FromElement(this) is DropDownAutomationPeer peer)
        {
            peer.Moved(was, now);
        }
    }

    /// <summary>The pill says what is chosen, or the placeholder in tertiary ink.</summary>
    private void Refresh()
    {
        var chosen = At(SelectedIndex);

        if (_value is not null)
        {
            _value.Text = chosen ?? PlaceholderText ?? string.Empty;
        }

        VisualStateManager.GoToState(this, chosen is null ? "Placeholder" : "HasValue", false);
    }

    private void UpdateStates()
    {
        var state = !IsEnabled ? "Disabled" : _pressed ? "Pressed" : _over ? "PointerOver" : "Normal";
        VisualStateManager.GoToState(this, state, false);
    }

    private void OnPillEntered(object sender, PointerRoutedEventArgs e)
    {
        _over = true;
        UpdateStates();
    }

    private void OnPillExited(object sender, PointerRoutedEventArgs e)
    {
        _over = false;
        _pressed = false;
        UpdateStates();
    }

    private void OnPillPressed(object sender, PointerRoutedEventArgs e)
    {
        _pressed = true;
        _openAtPress = PillPress.ClosesTheList(IsDropDownOpen, _dismissedAt, Environment.TickCount64);
        UpdateStates();
    }

    private void OnPillReleased(object sender, PointerRoutedEventArgs e)
    {
        _pressed = false;
        UpdateStates();
    }

    private void OnPillTapped(object sender, TappedRoutedEventArgs e)
    {
        if (!IsEnabled)
        {
            return;
        }

        Focus(FocusState.Pointer);

        // A press on the pill while the list is open closes it, and the light-dismiss of that same
        // press must not be read as a fresh one that opens it again.
        _refocus = _openAtPress;
        IsDropDownOpen = !_openAtPress;
        _openAtPress = false;

        // One dismissal swallows at most the one press it came with.
        _dismissedAt = null;
    }

    /// <summary>
    /// Opens the list: told its theme, placed by <see cref="ListPlacement"/> against the root of the
    /// window, and scrolled to what is chosen.
    /// </summary>
    private bool TryOpen()
    {
        if (_popup is null
            || _listBorder is null
            || _list is null
            || _pill is null
            || XamlRoot?.Content is not FrameworkElement root)
        {
            return false;
        }

        // First, while nothing is on screen: a handler may offer something else in place of what
        // was offered — the recorder's pickers read the machine again as they open — and the list
        // is measured and placed for what is there afterwards.
        DropDownOpened?.Invoke(this, EventArgs.Empty);

        if (_entries.Length == 0)
        {
            return false;
        }

        _syncing = true;
        _list.SelectedIndex = SelectedIndex;
        _syncing = false;

        // A popup is not in the tree its pill is in, so it does not take the pill's theme unless
        // told it.
        _listBorder.RequestedTheme = ActualTheme;

        _popup.IsOpen = true;

        // The list is only in the live tree now, so only now has it a template to measure. Everything
        // here runs in the one turn the press was handled in: nothing is drawn between.
        _listBorder.UpdateLayout();
        Place(root);

        _watching = XamlRoot;
        _watching.Changed += OnRootChanged;

        // The chosen entry first, and the keyboard on the list so the arrows and Enter act on it.
        if (SelectedIndex >= 0 && SelectedIndex < _entries.Length)
        {
            _list.ScrollIntoView(_entries[SelectedIndex], ScrollIntoViewAlignment.Leading);
        }

        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (IsDropDownOpen)
            {
                _list?.Focus(FocusState.Programmatic);
            }
        });

        return true;
    }

    private void Place(FrameworkElement root)
    {
        if (_pill is null || _popup is null || _listBorder is null || XamlRoot is null)
        {
            return;
        }

        var at = Corner(root);
        var size = XamlRoot.Size;

        _listBorder.Width = double.NaN;
        _listBorder.Height = double.NaN;
        _listBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var wanted = _listBorder.DesiredSize;

        var place = ListPlacement.For(
            new ListAnchor(at.X, at.Y, _pill.ActualWidth, _pill.ActualHeight),
            size.Width,
            size.Height,
            wanted.Width,
            wanted.Height,
            MaxDropDownHeight);

        _listBorder.Width = place.Width;
        _listBorder.Height = place.Height;

        // The popup's offsets are from where the popup itself sits, which is the pill's own corner:
        // the template puts the popup in the same cell as the pill, at its top left.
        _popup.HorizontalOffset = place.Left - at.X;
        _popup.VerticalOffset = place.Top - at.Y;
    }

    /// <summary>
    /// The pill's corner in the coordinates of the window's root. A pill in a dialog is in the
    /// dialog's own layer, which has no common ancestor with the root to transform to, and there the
    /// window's own coordinates are the root's.
    /// </summary>
    private Point Corner(FrameworkElement root)
    {
        try
        {
            return _pill!.TransformToVisual(root).TransformPoint(new Point(0, 0));
        }
        catch (ArgumentException)
        {
            return _pill!.TransformToVisual(null).TransformPoint(new Point(0, 0));
        }
    }

    private void Close()
    {
        if (_watching is not null)
        {
            _watching.Changed -= OnRootChanged;
            _watching = null;
        }

        if (_popup is { IsOpen: true })
        {
            _popup.IsOpen = false;
        }

        // The keyboard goes back to the pill, unless the list was closed by a press on something
        // else that should keep it.
        if (_refocus)
        {
            _refocus = false;
            Focus(FocusState.Programmatic);
        }
    }

    private void OnPopupClosed(object? sender, object e)
    {
        // Closed from outside: a press elsewhere light-dismissed it.
        if (IsDropDownOpen)
        {
            _dismissedAt = Environment.TickCount64;
            IsDropDownOpen = false;
        }
    }

    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => IsDropDownOpen = false;

    private void OnItemClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Entry clicked)
        {
            Pick(clicked.Index);
        }
    }

    private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // A pick made through UI Automation's Select, which is no click.
        if (!_syncing && e.AddedItems.Count > 0 && e.AddedItems[0] is Entry picked)
        {
            Pick(picked.Index);
        }
    }

    private void OnListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            _refocus = true;
            IsDropDownOpen = false;
            e.Handled = true;
        }
    }

    private void Pick(int index)
    {
        // A click arrives as both the item's click and the list's selection change, and the second
        // finds the list already closed.
        if (!IsDropDownOpen)
        {
            return;
        }

        // Closed first: a screen answers a pick by drawing itself again from inside the handler, and
        // that takes this pill off the tree, so what is left to do here has to be done by then.
        _refocus = true;
        IsDropDownOpen = false;
        SelectedIndex = index;
    }

    private void Unhook()
    {
        if (_pill is not null)
        {
            _pill.PointerEntered -= OnPillEntered;
            _pill.PointerExited -= OnPillExited;
            _pill.PointerPressed -= OnPillPressed;
            _pill.PointerReleased -= OnPillReleased;
            _pill.PointerCanceled -= OnPillReleased;
            _pill.PointerCaptureLost -= OnPillReleased;
            _pill.Tapped -= OnPillTapped;
        }

        if (_popup is not null)
        {
            _popup.Closed -= OnPopupClosed;
        }

        if (_list is not null)
        {
            _list.ItemClick -= OnItemClicked;
            _list.SelectionChanged -= OnListSelectionChanged;
            _list.KeyDown -= OnListKeyDown;
        }

        _refocus = false;
        IsDropDownOpen = false;
    }

    /// <summary>
    /// One entry of the list. A class of its own and not the string, so that two entries saying the
    /// same words are still two entries: a program is listed by its window's title, and two windows
    /// can have the same one.
    /// </summary>
    private sealed record Entry(int Index, string Text)
    {
        public override string ToString() => Text;
    }
}
