using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Vcm.App.Localization;
using Vcm.Core;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Vcm.App.UI;

/// <summary>
/// Shows the current shortcut as key caps with an edit button; editing opens the "Activation shortcut" dialog.
/// </summary>
internal sealed class ShortcutControl : ContentControl
{
    public static readonly DependencyProperty HotkeyProperty = DependencyProperty.Register(
        nameof(Hotkey), typeof(HotkeySettings), typeof(ShortcutControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((ShortcutControl)d).UpdateKeys()));

    public static readonly DependencyProperty DefaultHotkeyProperty = DependencyProperty.Register(
        nameof(DefaultHotkey), typeof(HotkeySettings), typeof(ShortcutControl));

    private readonly KeyCaps _keys = new();

    public ShortcutControl()
    {
        var edit = new SymbolIcon { Symbol = SymbolRegular.Edit24, FontSize = 14, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(_keys);
        content.Children.Add(edit);

        var button = new Button
        {
            Content = content,
            Padding = new Thickness(4, 3, 8, 3),
            ToolTip = Loc.Get("EditShortcut"),
            Appearance = ControlAppearance.Transparent,
        };
        button.Click += async (_, _) => await EditAsync();
        Content = button;
    }

    public HotkeySettings? Hotkey
    {
        get => (HotkeySettings?)GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }

    public HotkeySettings? DefaultHotkey
    {
        get => (HotkeySettings?)GetValue(DefaultHotkeyProperty);
        set => SetValue(DefaultHotkeyProperty, value);
    }

    private void UpdateKeys() => _keys.ItemsSource = Hotkey?.GetKeyList() ?? [];

    private async Task EditAsync()
    {
        if (Window.GetWindow(this) is not SettingsWindow window)
        {
            return;
        }

        var viewModel = window.ViewModel;
        HotkeySettings? candidate = Hotkey;

        var keys = new KeyCaps { KeySize = 16, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 16) };
        var invalid = new InfoBar { Title = Loc.Get("InvalidShortcutTitle"), Message = Loc.Get("InvalidShortcutMessage"), Severity = InfoBarSeverity.Error, IsClosable = false };
        var altGr = new InfoBar { Title = Loc.Get("AltGrWarningTitle"), Message = Loc.Get("AltGrWarningMessage"), Severity = InfoBarSeverity.Warning, IsClosable = false };
        var conflict = new InfoBar { Title = Loc.Get("ConflictWarningTitle"), Message = Loc.Get("ConflictWarningMessage"), Severity = InfoBarSeverity.Informational, IsClosable = false };

        var panel = new StackPanel { MinWidth = 400 };
        panel.Children.Add(new TextBlock { Text = Loc.Get("ShortcutDialogDescription"), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(keys);
        panel.Children.Add(invalid);
        panel.Children.Add(altGr);
        panel.Children.Add(conflict);

        var dialog = new ContentDialog(window.DialogHost)
        {
            Title = Loc.Get("ShortcutDialogTitle"),
            Content = panel,
            PrimaryButtonText = Loc.Get("Save"),
            SecondaryButtonText = Loc.Get("Reset"),
            CloseButtonText = Loc.Get("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        void Show(HotkeySettings? hotkey, bool partial)
        {
            keys.ItemsSource = hotkey?.GetKeyList() ?? [];
            bool valid = hotkey is { IsValid: true };
            invalid.IsOpen = !partial && hotkey is not null && !valid;
            altGr.IsOpen = valid && hotkey!.Ctrl && hotkey.Alt;
            conflict.IsOpen = valid && !hotkey!.SameCombination(Hotkey) && !viewModel.IsHotkeyAvailable(hotkey);
            dialog.IsPrimaryButtonEnabled = valid;
        }

        viewModel.SuspendHotkeys();
        using var recorder = new KeyboardRecorder();
        recorder.ModifiersChanged += h => Dispatcher.BeginInvoke(() => Show(h, partial: true));
        recorder.Recorded += h => Dispatcher.BeginInvoke(() =>
        {
            candidate = h;
            Show(h, partial: false);
        });
        recorder.Committed += save => Dispatcher.BeginInvoke(() =>
        {
            if (!save || candidate is { IsValid: true })
            {
                dialog.Hide(save ? ContentDialogResult.Primary : ContentDialogResult.None);
            }
        });

        Show(candidate, partial: false);
        ContentDialogResult result;
        try
        {
            result = await dialog.ShowAsync();
        }
        finally
        {
            recorder.Dispose();
            viewModel.ResumeHotkeys();
        }

        if (result == ContentDialogResult.Primary && candidate is { IsValid: true })
        {
            Hotkey = candidate;
        }
        else if (result == ContentDialogResult.Secondary && DefaultHotkey is not null)
        {
            Hotkey = DefaultHotkey;
        }
    }
}
