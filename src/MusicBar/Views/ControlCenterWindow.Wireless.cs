using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MusicBar.Infrastructure;
using MusicBar.Services;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace MusicBar.Views;

/// <summary>
/// The control center's Wi-Fi and Bluetooth lists. A click opens a row in place (connect, type
/// the key, pair, remove); a right-click offers the same as a menu, as in Windows' own panel.
/// </summary>
public partial class ControlCenterWindow
{
    static readonly Brush Warning = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0xA8, 0x96)));

    readonly WifiNetworks _wifi = new();
    IReadOnlyList<WifiNetwork> _networks = [];
    bool _wifiScanning;
    Button? _wifiRefresh;
    // The network whose row is open, and what has been chosen or typed there.
    string? _wifiOpen;
    bool _wifiAskKey;
    bool _wifiAuto = true;
    bool _wifiBusy;
    string _wifiKey = "";
    string? _wifiNote;

    BluetoothDevices? _bt;
    // The device list changes in bursts while the radio looks around; redraw at most this often.
    readonly DispatcherTimer _btRender = new() { Interval = TimeSpan.FromMilliseconds(350) };
    string? _btOpen;
    string? _btBusy, _btBusyText;
    string? _btNote;
    readonly Dictionary<string, bool> _btAudio = [];
    PairingPrompt? _btPrompt;
    TaskCompletionSource<(bool, string?)>? _btAnswer;
    string _btPin = "";

    /// <summary>The Wi-Fi or Bluetooth list closes: stop looking for devices, drop what was half typed.</summary>
    void LeaveWireless()
    {
        _bt?.Stop();
        _btRender.Stop();
        _btAnswer?.TrySetResult((false, null));
        _btAnswer = null;
        _btPrompt = null;
        _wifiOpen = _btOpen = null;
        _wifiKey = _btPin = "";
        _wifiNote = _btNote = null;
    }

    // ------------------------------------------------------------------ Wi-Fi

    async void ShowWifi()
    {
        int token = _sideToken;
        SideHeader("Wi-Fi");
        var radio = await QuickActions.RadioAsync(RadioKind.WiFi);
        if (token != _sideToken) return;
        if (radio == null)
        {
            SideMessage(Loc.T("CC_WifiNoAdapter"));
            return;
        }
        if (radio.State != RadioState.On)
        {
            RadioOff("Wi-Fi", Loc.T("CC_WifiOff"), radio, SideKind.WiFi, Loc.T("CC_WifiMore"), "ms-settings:network-wifi");
            return;
        }
        var status = await _wifi.OpenAsync();
        if (token != _sideToken) return;
        if (status != WifiStatus.Ready)
        {
            SideMessage(Loc.T(status == WifiStatus.Denied ? "CC_WifiDenied" : "CC_WifiNoAdapter"));
            SideLink(Loc.T("CC_WifiMore"), "ms-settings:network-wifi");
            return;
        }
        // What Windows saw last shows at once; a fresh scan takes a few seconds.
        _networks = await _wifi.ListAsync(scan: false);
        if (token != _sideToken) return;
        LoadWifi(scan: true);
    }

    async void LoadWifi(bool scan)
    {
        int token = _sideToken;
        _wifiScanning = scan;
        RenderWifi();
        var networks = await _wifi.ListAsync(scan);
        if (token != _sideToken) return;
        _networks = networks;
        _wifiScanning = false;
        // Someone typing a key keeps their field; the fresh list shows with the next step.
        if (_wifiAskKey && _wifiOpen != null)
        {
            if (_wifiRefresh != null) _wifiRefresh.IsEnabled = true;
            return;
        }
        RenderWifi();
    }

    void RenderWifi()
    {
        if (_side != SideKind.WiFi) return;
        double offset = SideScroll.VerticalOffset;
        SideHeader("Wi-Fi");
        _wifiRefresh = SideTool("", Loc.T("CC_LookAgain"), () => LoadWifi(scan: true));
        _wifiRefresh.IsEnabled = !_wifiScanning;
        if (_wifi.Blocked)
        {
            // Windows won't list networks to MusicBar (location is off), but its own list is exempt.
            Log.Info("Wi-Fi list blocked (location off): opening Windows' Wi-Fi list");
            SetSide(SideKind.None, animate: false);
            OpenUri("ms-availablenetworks:");
            return;
        }
        if (_networks.Count == 0) SideMessage(Loc.T(_wifiScanning ? "CC_WifiSearching" : "CC_WifiNone"));
        foreach (var network in _networks)
        {
            var n = network;
            SideRow(WifiGlyph(n.Bars), n.Ssid, WifiState(n), n.Connected, () => ToggleWifi(n), () => WifiMenu(n));
            if (_wifiOpen == n.Ssid) SideList.Children.Add(WifiDetail(n));
        }
        SideLink(Loc.T("CC_WifiMore"), "ms-settings:network-wifi");
        SideScroll.ScrollToVerticalOffset(offset);
    }

    static string WifiGlyph(int bars) => bars switch
    {
        >= 4 => "",
        3 => "",
        2 => "",
        _ => "",
    };

    static string WifiState(WifiNetwork n) => Loc.T(n.Connected
        ? n.Security == WifiSecurity.Open ? "CC_WifiConnected" : "CC_WifiConnectedSecured"
        : n.Security == WifiSecurity.Open ? "CC_WifiOpen" : "CC_WifiSecured");

    void ToggleWifi(WifiNetwork n)
    {
        if (_wifiBusy) return;
        if (_wifiOpen == n.Ssid)
        {
            _wifiOpen = null;
            RenderWifi();
            return;
        }
        ExpandWifi(n);
    }

    /// <summary>Opens the network's row: a key field when Windows doesn't know the network yet.</summary>
    void ExpandWifi(WifiNetwork n)
    {
        _wifiOpen = n.Ssid;
        _wifiNote = null;
        _wifiKey = "";
        _wifiAuto = n.AutoConnect;
        _wifiAskKey = !n.Connected && !n.Saved && n.Security != WifiSecurity.Open;
        RenderWifi();
    }

    FrameworkElement WifiDetail(WifiNetwork n)
    {
        var panel = new StackPanel { Margin = new Thickness(38, 0, 8, 10) };
        if (n.Connected)
        {
            panel.Children.Add(Buttons(Soft(Loc.T("CC_Disconnect"), () => LeaveWifi())));
            return panel;
        }
        if (_wifiBusy)
        {
            panel.Children.Add(Note(Loc.T("CC_Joining")));
            return panel;
        }

        // A company network signs in with an account (802.1X): Windows' own list walks through that.
        if (_wifiAskKey && n.Security == WifiSecurity.Enterprise)
        {
            panel.Children.Add(Note(Loc.T("CC_WifiEnterprise")));
            panel.Children.Add(Buttons(Accent(Loc.T("CC_WifiWindowsList"), () => OpenUri("ms-availablenetworks:"))));
            return panel;
        }

        var connect = Accent(Loc.T("CC_Connect"), () => JoinWifi(n));
        if (_wifiAskKey)
        {
            panel.Children.Add(SecretField(Loc.T("CC_WifiKeyHint"), _wifiKey, t =>
            {
                _wifiKey = t;
                connect.IsEnabled = t.Length > 0;
            }, () => JoinWifi(n), out var key));
            connect.IsEnabled = _wifiKey.Length > 0;
            FocusLater(key);
        }

        var auto = new CheckBox { Style = (Style)FindResource("Check"), Content = Loc.T("CC_WifiAuto"), IsChecked = _wifiAuto, Margin = new Thickness(0, 10, 0, 0) };
        auto.Click += (_, _) => _wifiAuto = auto.IsChecked == true;
        panel.Children.Add(auto);
        if (_wifiNote != null) panel.Children.Add(Note(_wifiNote, warn: true));
        panel.Children.Add(_wifiAskKey ? Buttons(Soft(Loc.T("CC_Cancel"), () => ToggleWifi(n)), connect) : Buttons(connect));
        return panel;
    }

    async void JoinWifi(WifiNetwork n)
    {
        if (_wifiBusy || (_wifiAskKey && _wifiKey.Length == 0)) return;
        _wifiBusy = true;
        _wifiNote = null;
        RenderWifi();
        var result = await _wifi.ConnectAsync(n, _wifiAuto, _wifiAskKey ? _wifiKey : null);
        _wifiBusy = false;
        if (_side != SideKind.WiFi) return;
        switch (result)
        {
            case WifiResult.Connected:
                _wifiOpen = null;
                _wifiAskKey = false;
                _wifiKey = "";
                break;
            case WifiResult.WrongKey when n.Security != WifiSecurity.Open:
                // A typo, or a saved key that changed on the router: ask (again).
                _wifiAskKey = true;
                _wifiKey = "";
                _wifiNote = Loc.T("CC_WifiWrongKey");
                break;
            default:
                _wifiNote = Loc.T("CC_WifiFailed", n.Ssid);
                break;
        }
        LoadWifi(scan: false);
    }

    void LeaveWifi()
    {
        _wifi.Disconnect();
        _wifiOpen = null;
        LoadWifi(scan: false);
    }

    ContextMenu WifiMenu(WifiNetwork n)
    {
        var menu = NewMenu();
        if (n.Connected)
        {
            menu.Items.Add(Item(Loc.T("CC_Disconnect"), "", LeaveWifi));
        }
        else
        {
            menu.Items.Add(Item(Loc.T("CC_Connect"), "", () =>
            {
                ExpandWifi(n);
                // Known (or open) networks join right away; new ones wait for their key.
                if (!_wifiAskKey) JoinWifi(n);
            }));
        }
        if (n.Saved)
        {
            menu.Items.Add(Item(Loc.T("CC_WifiForget"), "", () =>
            {
                _wifi.Forget(n);
                if (_wifiOpen == n.Ssid) _wifiOpen = null;
                LoadWifi(scan: false);
            }));
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Loc.T("CC_WifiSettings"), "", () => OpenUri("ms-settings:network-wifi")));
        return menu;
    }

    // ------------------------------------------------------------------ Bluetooth

    async void ShowBluetooth()
    {
        int token = _sideToken;
        SideHeader("Bluetooth");
        var radio = await QuickActions.RadioAsync(RadioKind.Bluetooth);
        if (token != _sideToken) return;
        if (radio == null || radio.State != RadioState.On)
        {
            _bt?.Stop();
            if (radio == null) SideMessage(Loc.T("CC_BtNoAdapter"));
            else RadioOff("Bluetooth", Loc.T("CC_BtOff"), radio, SideKind.Bluetooth, Loc.T("CC_BtMore"), "ms-settings:bluetooth");
            return;
        }
        if (_bt == null)
        {
            _bt = new BluetoothDevices(Dispatcher);
            _bt.Changed += () =>
            {
                if (_side == SideKind.Bluetooth && !_btRender.IsEnabled) _btRender.Start();
            };
            _btRender.Tick += (_, _) =>
            {
                _btRender.Stop();
                // A PIN being typed keeps its field until the step is answered.
                if (_btPrompt == null) RenderBluetooth();
            };
        }
        _bt.Start();
        RenderBluetooth();
    }

    void RenderBluetooth()
    {
        if (_side != SideKind.Bluetooth || _bt == null) return;
        double offset = SideScroll.VerticalOffset;
        SideHeader("Bluetooth");
        var items = _bt.Items();
        var mine = items.Where(i => i.Paired).ToList();
        if (mine.Count > 0)
        {
            SideSubhead(Loc.T("CC_BtMine"));
            foreach (var item in mine) BluetoothRow(item);
        }
        SideSubhead(Loc.T("CC_BtNearby"));
        foreach (var item in items.Where(i => !i.Paired)) BluetoothRow(item);
        // The radio keeps looking while this list is open.
        if (items.All(i => i.Paired)) SideMessage(Loc.T("CC_BtSearching"));
        SideLink(Loc.T("CC_BtMore"), "ms-settings:bluetooth");
        SideScroll.ScrollToVerticalOffset(offset);
    }

    void BluetoothRow(BluetoothItem item)
    {
        string detail = _btBusy == item.Id && _btBusyText != null ? _btBusyText
            : item is { Connected: true, Battery: int battery } ? Loc.T("CC_BtConnectedBattery", battery)
            : Loc.T(item.Connected ? "CC_BtConnected" : item.Paired ? "CC_BtPaired" : "CC_BtReady");
        SideRow(BluetoothGlyph(item.Kind), item.Name, detail, item.Connected, () => ToggleBluetooth(item), () => BluetoothMenu(item));
        if (_btOpen == item.Id) SideList.Children.Add(BluetoothDetail(item));
    }

    static string BluetoothGlyph(BluetoothKind kind) => kind switch
    {
        BluetoothKind.Audio => "",
        BluetoothKind.Keyboard => "",
        BluetoothKind.Mouse => "",
        BluetoothKind.Phone => "",
        BluetoothKind.Computer => "",
        _ => "",
    };

    void ToggleBluetooth(BluetoothItem item)
    {
        if (_btBusy != null) return;
        _btOpen = _btOpen == item.Id ? null : item.Id;
        _btNote = null;
        if (_btOpen != null && item.Paired && !_btAudio.ContainsKey(item.Id)) ProbeAudio(item);
        RenderBluetooth();
    }

    /// <summary>Only audio devices can be connected from here; find out once per device.</summary>
    async void ProbeAudio(BluetoothItem item)
    {
        _btAudio[item.Id] = await BluetoothDevices.CanConnectAsync(item);
        RenderBluetooth();
    }

    FrameworkElement BluetoothDetail(BluetoothItem item)
    {
        var panel = new StackPanel { Margin = new Thickness(38, 0, 8, 10) };
        if (_btBusy == item.Id)
        {
            panel.Children.Add(_btPrompt is { } prompt ? PairingPanel(item, prompt) : Note(_btBusyText ?? ""));
            return panel;
        }
        if (_btNote != null) panel.Children.Add(Note(_btNote, warn: true));
        if (!item.Paired)
        {
            panel.Children.Add(Buttons(Accent(Loc.T("CC_BtPair"), () => PairBluetooth(item))));
            return panel;
        }
        var remove = Soft(Loc.T("CC_BtRemove"), () => RemoveBluetooth(item));
        if (!_btAudio.TryGetValue(item.Id, out bool audio))
        {
            panel.Children.Add(Buttons(remove));
        }
        else if (audio)
        {
            var link = item.Connected
                ? Soft(Loc.T("CC_Disconnect"), () => LinkBluetooth(item, false))
                : Accent(Loc.T("CC_Connect"), () => LinkBluetooth(item, true));
            panel.Children.Add(Buttons(remove, link));
        }
        else
        {
            if (!item.Connected) panel.Children.Add(Note(Loc.T("CC_BtNoLink")));
            panel.Children.Add(Buttons(remove));
        }
        return panel;
    }

    FrameworkElement PairingPanel(BluetoothItem item, PairingPrompt prompt)
    {
        var panel = new StackPanel();
        switch (prompt.Kind)
        {
            case DevicePairingKinds.DisplayPin:
                panel.Children.Add(Note(Loc.T("CC_BtPinShow", item.Name)));
                panel.Children.Add(PinText(prompt.Pin));
                break;
            case DevicePairingKinds.ConfirmPinMatch:
                panel.Children.Add(Note(Loc.T("CC_BtPinMatch", item.Name)));
                panel.Children.Add(PinText(prompt.Pin));
                panel.Children.Add(Buttons(Soft(Loc.T("CC_BtNo"), () => AnswerPairing(false)), Accent(Loc.T("CC_BtYes"), () => AnswerPairing(true))));
                break;
            case DevicePairingKinds.ProvidePin:
                panel.Children.Add(Note(Loc.T("CC_BtPinEnter", item.Name)));
                panel.Children.Add(TextField("PIN", _btPin, t => _btPin = t, () => AnswerPairing(true), out var box));
                panel.Children.Add(Buttons(Soft(Loc.T("CC_Cancel"), () => AnswerPairing(false)), Accent(Loc.T("CC_Connect"), () => AnswerPairing(true))));
                FocusLater(box);
                break;
        }
        return panel;
    }

    static TextBlock PinText(string? pin) => new()
    {
        Text = pin ?? "", FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 4),
        Typography = { NumeralAlignment = FontNumeralAlignment.Tabular },
    };

    void AnswerPairing(bool accept)
    {
        var answer = _btAnswer;
        _btAnswer = null;
        _btPrompt = null;
        answer?.TrySetResult((accept, _btPin));
        RenderBluetooth();
    }

    async void PairBluetooth(BluetoothItem item)
    {
        if (_btBusy != null || _bt == null) return;
        _btOpen = item.Id;
        _btBusy = item.Id;
        _btBusyText = Loc.T("CC_BtPairing");
        _btNote = null;
        RenderBluetooth();
        bool ok = await _bt.PairAsync(item.Id, prompt =>
        {
            _btPrompt = prompt;
            _btPin = "";
            var answer = new TaskCompletionSource<(bool, string?)>();
            _btAnswer = answer;
            // A PIN shown here is typed on the device; Windows waits for that, not for a click.
            if (prompt.Kind == DevicePairingKinds.DisplayPin) answer.TrySetResult((true, null));
            RenderBluetooth();
            return answer.Task;
        });
        _btPrompt = null;
        _btAnswer = null;
        _btBusy = _btBusyText = null;
        if (_side != SideKind.Bluetooth) return;
        if (ok) _btOpen = null;
        else _btNote = Loc.T("CC_BtPairFailed", item.Name);
        RenderBluetooth();
    }

    async void RemoveBluetooth(BluetoothItem item)
    {
        if (_btBusy != null || _bt == null) return;
        _btOpen = item.Id;
        _btBusy = item.Id;
        _btBusyText = Loc.T("CC_BtRemoving");
        _btNote = null;
        RenderBluetooth();
        bool ok = await _bt.UnpairAsync(item.Id);
        _btBusy = _btBusyText = null;
        if (ok)
        {
            _btOpen = null;
            _btAudio.Remove(item.Id);
        }
        else
        {
            _btNote = Loc.T("CC_BtRemoveFailed", item.Name);
        }
        RenderBluetooth();
    }

    async void LinkBluetooth(BluetoothItem item, bool connect)
    {
        if (_btBusy != null) return;
        _btOpen = item.Id;
        _btBusy = item.Id;
        _btBusyText = Loc.T(connect ? "CC_Joining" : "CC_BtDisconnecting");
        _btNote = null;
        RenderBluetooth();
        bool ok = await BluetoothDevices.SetConnectedAsync(item, connect);
        // The driver takes the request at once; the link itself shows up a few seconds later.
        bool done = false;
        for (int i = 0; ok && i < 48 && _side == SideKind.Bluetooth; i++)
        {
            await Task.Delay(250);
            if (_bt?.Items().FirstOrDefault(d => d.Id == item.Id)?.Connected == connect)
            {
                done = true;
                break;
            }
        }
        _btBusy = _btBusyText = null;
        if (!done) _btNote = Loc.T("CC_BtLinkFailed");
        RenderBluetooth();
    }

    ContextMenu BluetoothMenu(BluetoothItem item)
    {
        var menu = NewMenu();
        if (item.Paired)
        {
            bool audio = _btAudio.TryGetValue(item.Id, out var known) ? known : item.Kind == BluetoothKind.Audio;
            if (audio)
            {
                menu.Items.Add(item.Connected
                    ? Item(Loc.T("CC_Disconnect"), "", () => LinkBluetooth(item, false))
                    : Item(Loc.T("CC_Connect"), "", () => LinkBluetooth(item, true)));
            }
            menu.Items.Add(Item(Loc.T("CC_BtRemove"), "", () => RemoveBluetooth(item)));
        }
        else
        {
            menu.Items.Add(Item(Loc.T("CC_BtPair"), "", () => PairBluetooth(item)));
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Loc.T("CC_BtSettings"), "", () => OpenUri("ms-settings:bluetooth")));
        return menu;
    }

    // ------------------------------------------------------------------ building blocks

    /// <summary>The radio is off: say so, offer to turn it on, and point at the Windows page.</summary>
    void RadioOff(string title, string text, Radio radio, SideKind kind, string linkText, string uri)
    {
        SideHeader(title);
        SideMessage(text);
        var on = Accent(Loc.T("CC_TurnOn"), async () =>
        {
            await radio.SetStateAsync(RadioState.On);
            foreach (var paint in _tilePainters) paint();
            if (_side == kind) SetSide(kind, animate: false);
        });
        on.HorizontalAlignment = HorizontalAlignment.Left;
        on.Margin = new Thickness(10, 2, 0, 8);
        SideList.Children.Add(on);
        SideLink(linkText, uri);
    }

    Button SideTool(string glyph, string tip, Action click)
    {
        var tool = new Button
        {
            Style = (Style)FindResource("TextButton"), ToolTip = tip, Padding = new Thickness(7, 4, 7, 4),
            Content = new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 12 },
        };
        tool.IsEnabledChanged += (_, _) => tool.Opacity = tool.IsEnabled ? 1 : 0.35;
        tool.Click += (_, _) => click();
        SideTools.Children.Add(tool);
        return tool;
    }

    void SideSubhead(string text) => SideList.Children.Add(new TextBlock
    {
        Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Opacity = 0.55, Margin = new Thickness(10, 6, 10, 4),
    });

    void SideLink(string text, string uri)
    {
        var link = new Button { Style = (Style)FindResource("TextButton"), Content = text, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 4, 0, 0) };
        link.Click += (_, _) => OpenUri(uri);
        SideFooter.Children.Add(link);
    }

    void OpenUri(string uri)
    {
        Dismiss();
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"Opening {uri} failed", ex);
        }
    }

    TextBlock Note(string text, bool warn = false)
    {
        var note = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 2), Opacity = warn ? 1 : 0.7 };
        if (warn) note.Foreground = Warning;
        return note;
    }

    Button Accent(string text, Action click) => ActionButton("AccentButton", text, click);

    Button Soft(string text, Action click) => ActionButton("SoftButton", text, click);

    Button ActionButton(string style, string text, Action click)
    {
        var button = new Button { Style = (Style)FindResource(style), Content = text };
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>A row's buttons, at its right edge, the main one last.</summary>
    static StackPanel Buttons(params Button[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        foreach (var b in buttons)
        {
            if (row.Children.Count > 0) b.Margin = new Thickness(6, 0, 0, 0);
            row.Children.Add(b);
        }
        return row;
    }

    FrameworkElement TextField(string hint, string value, Action<string> changed, Action submit, out TextBox box)
    {
        var field = new TextBox { Style = (Style)FindResource("Field"), Text = value };
        var placeholder = Hint(hint, value.Length == 0);
        field.TextChanged += (_, _) =>
        {
            changed(field.Text);
            placeholder.Visibility = field.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        };
        field.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            submit();
            e.Handled = true;
        };
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        grid.Children.Add(field);
        grid.Children.Add(placeholder);
        box = field;
        return grid;
    }

    /// <summary>A key field with a hint, and an eye to see what was typed.</summary>
    FrameworkElement SecretField(string hint, string value, Action<string> changed, Action submit, out Control focus)
    {
        var hidden = new PasswordBox { Style = (Style)FindResource("KeyField"), Password = value, Padding = new Thickness(6, 7, 34, 7) };
        var shown = new TextBox { Style = (Style)FindResource("Field"), Text = value, Padding = new Thickness(6, 7, 34, 7), Visibility = Visibility.Collapsed };
        var placeholder = Hint(hint, value.Length == 0);
        void Typed(string text)
        {
            changed(text);
            placeholder.Visibility = text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        hidden.PasswordChanged += (_, _) => Typed(hidden.Password);
        shown.TextChanged += (_, _) => Typed(shown.Text);
        KeyEventHandler enter = (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            submit();
            e.Handled = true;
        };
        hidden.KeyDown += enter;
        shown.KeyDown += enter;

        var eye = new Button
        {
            Style = (Style)FindResource("TextButton"), ToolTip = Loc.T("CC_WifiShowKey"), Padding = new Thickness(7, 4, 7, 4),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 3, 0),
            Content = new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 13 },
        };
        eye.Click += (_, _) =>
        {
            bool show = shown.Visibility != Visibility.Visible;
            if (show) shown.Text = hidden.Password;
            else hidden.Password = shown.Text;
            shown.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            hidden.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
            if (show) shown.CaretIndex = shown.Text.Length;
            FocusLater(show ? shown : hidden);
        };

        var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        grid.Children.Add(hidden);
        grid.Children.Add(shown);
        grid.Children.Add(placeholder);
        grid.Children.Add(eye);
        focus = hidden;
        return grid;
    }

    static TextBlock Hint(string text, bool visible) => new()
    {
        Text = text, FontSize = 13, Opacity = 0.45, IsHitTestVisible = false, Margin = new Thickness(9, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center, Visibility = visible ? Visibility.Visible : Visibility.Collapsed,
    };

    void FocusLater(Control control) => Dispatcher.BeginInvoke(() =>
    {
        control.Focus();
        Keyboard.Focus(control);
    }, DispatcherPriority.Input);

    static ContextMenu NewMenu() => new() { Style = Application.Current?.TryFindResource("BarContextMenu") as Style };

    static MenuItem Item(string text, string glyph, Action click)
    {
        var item = new MenuItem { Header = text, Icon = new TextBlock { Text = glyph } };
        item.Click += (_, _) => click();
        return item;
    }
}
