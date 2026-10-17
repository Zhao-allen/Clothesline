using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace ClotheslineSetup;

public partial class MainWindow : Window
{
    private enum State { Install, Uninstall, Done, UninstallDone }

    private readonly bool _uninstall;
    private State _state = State.Install;
    private Loc.Strings _s = Loc.For(Loc.Default);
    private readonly Button[] _langButtons;
    private bool _launchChecked = true;
    private string? _targetFolder;

    public MainWindow(bool uninstall)
    {
        _uninstall = uninstall;
        InitializeComponent();

        _langButtons = [LangEn, LangZh, LangEs, LangJa, LangFr];

        bool dark = IsDarkMode();
        ApplyTheme(dark);
        SetLanguage(InstallContext.Language);

        if (uninstall)
        {
            _state = State.Uninstall;
            using var key = Registry.CurrentUser.OpenSubKey(
                $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{Installer.AppName}");
            _targetFolder = key?.GetValue("InstallLocation") as string ?? Installer.DefaultTarget();
            ShowUninstall();
        }
        else
        {
            _targetFolder = Installer.DefaultTarget();
            PathBox.Text = _targetFolder;
        }
    }

    // MARK: theme

    private static bool IsDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            if (value is int i) return i == 0;
        }
        catch { }
        return true; // default to dark to match original installer
    }

    private void ApplyTheme(bool dark)
    {
        var t = dark ? Theme.Dark : Theme.Light;

        MainBorder.Background = t.WindowBackground;
        MainBorder.BorderBrush = t.WindowBorder;
        GlassBorder.Background = t.WindowBackground;
        HighlightBorder.Background = t.HighlightOverlay;

        TitleBarText.Foreground = t.TitleBarForeground;
        TitleBarBorder.Background = t.CheckRowBorder;
        CloseButton.Foreground = t.TitleBarForeground;
        CloseButton.MouseEnter += (_, _) => CloseButton.Background = t.ChromeButtonHoverBackground;
        CloseButton.MouseLeave += (_, _) => CloseButton.Background = Brushes.Transparent;

        BrandTitleText.Foreground = t.BrandTitle;
        BrandTaglineText.Foreground = t.BrandTagline;
        DividerBorder.Background = t.Divider;

        LanguageLabel.Foreground = t.SectionLabel;
        LocationLabel.Foreground = t.SectionLabel;

        WelcomeTitle.Foreground = t.Heading;
        WelcomeBody.Foreground = t.Body;
        HintCard.Background = t.HintBackground;
        HintCard.BorderBrush = t.HintBorder;
        HintIcon.Stroke = t.HintForeground;
        HintText.Foreground = t.HintForeground;

        PathFieldBorder.Background = t.PathFieldBackground;
        PathFieldBorder.BorderBrush = t.PathFieldBorder;
        PathBox.Foreground = t.PathFieldForeground;

        BrowseButton.Background = t.GhostButtonBackground;
        BrowseButton.Foreground = t.GhostButtonForeground;
        BrowseButton.BorderBrush = t.GhostButtonBackground;

        SecondaryButton.Background = t.GhostButtonBackground;
        SecondaryButton.Foreground = t.GhostButtonForeground;
        SecondaryButton.BorderBrush = t.GhostButtonBackground;

        PrimaryButton.Background = t.PrimaryButtonBackground;
        PrimaryButton.Foreground = t.PrimaryButtonForeground;
        PrimaryButton.BorderBrush = t.PrimaryButtonBackground;
        PrimaryIcon.Stroke = t.PrimaryButtonForeground;
        PrimaryLabel.Foreground = t.PrimaryButtonForeground;

        StatusText.Foreground = t.StatusForeground;
        ProgressBarTrack.Background = t.ProgressTrack;
        ProgressBarFill.Background = t.ProgressFill;

        ActionSeparator.Background = t.CheckRowBorder;

        OkBanner.Background = t.OkBannerBackground;
        OkBanner.BorderBrush = t.OkBannerBorder;
        OkHeading.Foreground = t.OkHeading;
        OkSubtext.Foreground = t.OkSubtext;
        DoneBodyText.Foreground = t.Body;

        LaunchCheckRow.Background = t.CheckRowBackground;
        LaunchCheckRow.BorderBrush = t.CheckRowBorder;
        LaunchCheckBox.Background = t.CheckBoxBackground;
        LaunchCheckMark.Stroke = t.PrimaryButtonForeground;
        LaunchPlayIcon.Stroke = new SolidColorBrush(dark ? ((LinearGradientBrush)Theme.Dark.PrimaryButtonBackground).GradientStops[0].Color : ((LinearGradientBrush)Theme.Light.PrimaryButtonBackground).GradientStops[0].Color);
        LaunchCheckText.Foreground = t.CheckRowForeground;

        UninstallTitle.Foreground = t.Heading;
        UninstallBody.Foreground = t.Body;
        UninstallPath.Foreground = t.BrandTagline;

        // Reset lang pill defaults
        foreach (var b in _langButtons)
        {
            b.Background = t.LangPillBackground;
            b.BorderBrush = t.LangPillBorder;
            b.Foreground = t.LangPillForeground;
        }
        UpdateLangPillState();
    }

    // MARK: language

    private void SetLanguage(string code)
    {
        InstallContext.Language = code;
        _s = Loc.For(code);

        TitleBarText.Text = $"{Installer.AppName} {Installer.Version}";
        BrandTaglineText.Text = _s.Tagline;
        LanguageLabel.Text = _s.Language.ToUpperInvariant();
        LocationLabel.Text = _s.Location;
        BrowseButton.Content = _s.Browse;
        WelcomeTitle.Text = _s.WelcomeTitle;
        WelcomeBody.Text = _s.WelcomeBody;
        HintText.Text = _s.Hint;

        if (_state == State.Install)
        {
            PrimaryLabel.Text = _s.Install;
            SecondaryButton.Content = _s.Cancel;
        }
        else if (_state == State.Uninstall)
        {
            PrimaryLabel.Text = _s.Uninstall;
            SecondaryButton.Content = _s.Cancel;
        }
        else if (_state == State.Done || _state == State.UninstallDone)
        {
            PrimaryLabel.Text = _s.Close;
        }

        if (_state == State.Done)
        {
            OkHeading.Text = _s.DoneTitle;
            OkSubtext.Text = _s.DoneBody;
            LaunchCheckText.Text = _s.Launch;
        }
        else if (_state == State.UninstallDone)
        {
            OkHeading.Text = Installer.AppName;
            OkSubtext.Text = _s.UninstallDone;
        }

        UninstallTitle.Text = _s.UninstallTitle;
        UninstallBody.Text = _s.UninstallBody;

        UpdateLangPillState();
    }

    private void LangButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string code)
            SetLanguage(code);
    }

    private void UpdateLangPillState()
    {
        var t = IsDarkMode() ? Theme.Dark : Theme.Light;
        for (int i = 0; i < _langButtons.Length; i++)
        {
            var b = _langButtons[i];
            bool active = Loc.Languages[i].Code == InstallContext.Language;
            b.Background = active ? t.LangPillActiveBackground : t.LangPillBackground;
            b.BorderBrush = active ? t.LangPillActiveBackground : t.LangPillBorder;
            b.Foreground = active ? t.LangPillActiveForeground : t.LangPillForeground;
        }
    }

    // MARK: actions

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = _s.Location };
        if (Directory.Exists(PathBox.Text))
            dlg.FolderName = PathBox.Text;
        else
            dlg.FolderName = Installer.DefaultTarget();

        if (dlg.ShowDialog() == true)
            PathBox.Text = dlg.FolderName;
    }

    private void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        switch (_state)
        {
            case State.Install:
                RunInstall();
                break;
            case State.Uninstall:
                RunUninstall();
                break;
            case State.Done:
                if (_launchChecked) Installer.Launch(_targetFolder!);
                InstallContext.FlushLog();
                Close();
                break;
            case State.UninstallDone:
                InstallContext.FlushLog();
                Close();
                break;
        }
    }

    private void SecondaryButton_Click(object sender, RoutedEventArgs e)
    {
        InstallContext.FlushLog();
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        InstallContext.FlushLog();
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    // MARK: install / uninstall engine

    private void RunInstall()
    {
        var folder = PathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(folder))
        {
            Fail(_s.BadPath, _s.BadPathBody);
            return;
        }

        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex)
        {
            InstallContext.Log($"cannot create {folder}: {ex.Message}");
            Fail(_s.Access, _s.AccessBody);
            return;
        }

        _targetFolder = folder;
        InstallContext.TargetFolder = folder;
        InstallContext.MakeDesktopShortcut = true;
        InstallContext.MakeStartMenuShortcut = true;

        SetBusy(_s.Installing);

        Task.Run(() =>
        {
            try
            {
                Installer.Install(folder,
                    report => Dispatcher.Invoke(() => StatusText.Text = report),
                    pct => Dispatcher.Invoke(() =>
                    {
                        double max = ProgressBarTrack.ActualWidth;
                        if (max <= 0) max = 648;
                        ProgressBarFill.Width = max * pct / 100.0;
                    }));
                Dispatcher.Invoke(ShowDone);
            }
            catch (Exception ex)
            {
                InstallContext.Log($"install failed: {ex}");
                Dispatcher.Invoke(() => Fail(_s.Access, ex.Message));
            }
        });
    }

    private void RunUninstall()
    {
        SetBusy(_s.Uninstalling);

        Task.Run(() =>
        {
            try
            {
                Installer.Uninstall(_targetFolder!, _ => { });
                Dispatcher.Invoke(ShowUninstallDone);
            }
            catch (Exception ex)
            {
                InstallContext.Log($"uninstall failed: {ex}");
                Dispatcher.Invoke(() => Fail(_s.Access, ex.Message));
            }
        });
    }

    // MARK: UI state transitions

    private void ShowDone()
    {
        _state = State.Done;
        InstallSections.Visibility = Visibility.Collapsed;
        UninstallSections.Visibility = Visibility.Collapsed;
        DoneSections.Visibility = Visibility.Visible;

        StatusText.Visibility = Visibility.Collapsed;
        ProgressBarTrack.Visibility = Visibility.Collapsed;

        OkHeading.Text = _s.DoneTitle;
        OkSubtext.Text = _s.DoneBody;
        DoneBodyText.Visibility = Visibility.Collapsed;

        LaunchCheckRow.Visibility = Visibility.Visible;
        LaunchCheckText.Text = _s.Launch;
        _launchChecked = true;
        LaunchCheckMark.Visibility = Visibility.Visible;

        SecondaryButton.Visibility = Visibility.Collapsed;
        PrimaryLabel.Text = _s.Close;
        PrimaryIcon.Visibility = Visibility.Collapsed;

        InstallContext.Log($"installed to {_targetFolder}");
    }

    private void ShowUninstall()
    {
        _state = State.Uninstall;
        InstallSections.Visibility = Visibility.Collapsed;
        UninstallSections.Visibility = Visibility.Visible;
        DoneSections.Visibility = Visibility.Collapsed;

        UninstallTitle.Text = _s.UninstallTitle;
        UninstallBody.Text = _s.UninstallBody;
        UninstallPath.Text = _targetFolder;

        PrimaryLabel.Text = _s.Uninstall;
        PrimaryIcon.Visibility = Visibility.Collapsed;
        SecondaryButton.Content = _s.Cancel;
    }

    private void ShowUninstallDone()
    {
        _state = State.UninstallDone;
        StatusText.Visibility = Visibility.Collapsed;
        ProgressBarTrack.Visibility = Visibility.Collapsed;

        InstallSections.Visibility = Visibility.Collapsed;
        UninstallSections.Visibility = Visibility.Collapsed;
        DoneSections.Visibility = Visibility.Visible;

        OkHeading.Text = Installer.AppName;
        OkSubtext.Text = _s.UninstallDone;
        LaunchCheckRow.Visibility = Visibility.Collapsed;

        SecondaryButton.Visibility = Visibility.Collapsed;
        PrimaryLabel.Text = _s.Close;
        PrimaryIcon.Visibility = Visibility.Collapsed;

        InstallContext.FlushLog();
    }

    private void LaunchCheckRow_Click(object sender, MouseButtonEventArgs e)
    {
        _launchChecked = !_launchChecked;
        LaunchCheckMark.Visibility = _launchChecked ? Visibility.Visible : Visibility.Collapsed;
    }

    // MARK: busy / error

    private void SetBusy(string status)
    {
        PrimaryButton.IsEnabled = false;
        SecondaryButton.IsEnabled = false;
        BrowseButton.IsEnabled = false;
        PathBox.IsEnabled = false;
        foreach (var b in _langButtons) b.IsEnabled = false;

        StatusText.Visibility = Visibility.Visible;
        ProgressBarTrack.Visibility = Visibility.Visible;
        ProgressBarFill.Width = 0;
        StatusText.Text = status;
    }

    private void Fail(string title, string body)
    {
        StatusText.Visibility = Visibility.Collapsed;
        ProgressBarTrack.Visibility = Visibility.Collapsed;
        PrimaryButton.IsEnabled = true;
        SecondaryButton.IsEnabled = true;
        BrowseButton.IsEnabled = true;
        PathBox.IsEnabled = true;
        foreach (var b in _langButtons) b.IsEnabled = true;

        MessageBox.Show(this, body, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
