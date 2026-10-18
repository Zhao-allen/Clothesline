using System.Diagnostics;
using static ClotheslineSetup.Program;

namespace ClotheslineSetup;

/// <summary>
/// The installer's single window. Built rather than designed in the editor
/// because it ships as one self-contained exe with no satellite XAML.
///
/// The palette is the same one the application uses (see TrayPalette), so the
/// installer and the thing it installs read as one product.
///
/// Layout is positioned by hand rather than through FlowLayoutPanel: a flow
/// panel inside a percent-sized table cell scrolls instead of reflowing, which
/// left the finished state as a tall window with most of it empty.
/// </summary>
internal sealed class MainForm : Form
{
    private static readonly Color BgWindow = Color.FromArgb(0x1E, 0x1E, 0x1E);
    private static readonly Color BgCard = Color.FromArgb(0x2B, 0x2B, 0x2B);
    private static readonly Color BgField = Color.FromArgb(0x33, 0x33, 0x33);
    private static readonly Color FgPrimary = Color.White;
    private static readonly Color FgSecondary = Color.FromArgb(0xB4, 0xB4, 0xB4);
    private static readonly Color FgMuted = Color.FromArgb(0x8A, 0x8A, 0x8A);
    private static readonly Color Accent = Color.FromArgb(0x4C, 0xC2, 0xFF);
    private static readonly Color Stroke = Color.FromArgb(0x3D, 0x3D, 0x3D);

    private const int Side = 40;
    private const int HeaderH = 96;
    private const int FooterH = 68;

    private readonly bool _uninstall;
    private Loc.Strings _s = Loc.For(Loc.Default);

    private readonly Panel _body = new();
    private readonly List<Control> _installOnly = new();

    private readonly FlatButton[] _langButtons = new FlatButton[Loc.Languages.Length];
    private Label _welcomeTitle = null!;
    private Label _welcomeBody = null!;
    private Label _langLabel = null!;
    private Label _locationLabel = null!;
    private TextBox _pathBox = null!;
    private FlatButton _browse = null!;
    private CheckBox _desktopBox = null!;
    private CheckBox _startMenuBox = null!;
    private FlatButton _primary = null!;
    private FlatButton _secondary = null!;
    private Label _status = null!;
    private FlatProgressBar _bar = null!;
    private CheckBox _launchBox = null!;

    private int _cursor;
    private int _tallHeight;

    public int Result { get; private set; }

    public MainForm(bool uninstall = false)
    {
        _uninstall = uninstall;
        Text = uninstall ? Installer.AppName : $"{Installer.AppName} {Installer.Version}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = BgWindow;
        ForeColor = FgPrimary;
        Font = new Font("Segoe UI Variable Display", 9.5f);
        DoubleBuffered = true;
        ShowIcon = false;
        ClientSize = new Size(660, uninstall ? 330 : 476);

        Build();

        if (uninstall)
        {
            // Pre-fill from wherever the registry says it went, so an
            // uninstaller never has to guess.
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{Installer.AppName}");
            TargetFolder = key?.GetValue("InstallLocation") as string ?? Installer.DefaultTarget();
            ShowUninstall();
        }
        else
        {
            TargetFolder = Installer.DefaultTarget();
            _pathBox.Text = TargetFolder;
        }
    }

    // MARK: layout

    private void Build()
    {
        // Header.
        var header = new Panel { Location = new Point(0, 0), Size = new Size(ClientSize.Width, HeaderH), BackColor = BgWindow };
        header.Paint += (_, e) =>
        {
            using var pen = new Pen(Stroke);
            e.Graphics.DrawLine(pen, Side, HeaderH - 1, ClientSize.Width - Side, HeaderH - 1);
        };
        header.Controls.Add(new Label
        {
            Text = Installer.AppName,
            Font = new Font("Segoe UI Variable Display", 21f, FontStyle.Bold),
            ForeColor = FgPrimary,
            AutoSize = true,
            Location = new Point(Side, 22),
        });
        header.Controls.Add(new Label
        {
            Text = _s.Tagline,
            Font = new Font("Segoe UI Variable Display", 10f),
            ForeColor = FgSecondary,
            AutoSize = true,
            Location = new Point(Side + 2, 60),
        });
        Controls.Add(header);

        // Body.
        _body.Location = new Point(0, HeaderH);
        _body.Size = new Size(ClientSize.Width, ClientSize.Height - HeaderH - FooterH);
        _body.BackColor = BgWindow;
        Controls.Add(_body);
        BuildBody();

        // Footer.
        var footer = new Panel
        {
            Location = new Point(0, ClientSize.Height - FooterH),
            Size = new Size(ClientSize.Width, FooterH),
            BackColor = BgWindow,
        };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Stroke);
            e.Graphics.DrawLine(pen, Side, 0, ClientSize.Width - Side, 0);
        };

        _status = new Label
        {
            Location = new Point(Side, 14),
            Size = new Size(ClientSize.Width - Side * 2, 20),
            ForeColor = FgSecondary,
            Font = new Font("Segoe UI", 9f),
            Visible = false,
        };
        footer.Controls.Add(_status);

        _bar = new FlatProgressBar
        {
            Location = new Point(Side, 36),
            Size = new Size(ClientSize.Width - Side * 2, 6),
            Visible = false,
        };
        footer.Controls.Add(_bar);

        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
            Location = new Point(ClientSize.Width - Side - 250, 20),
        };

        _secondary = new FlatButton
        {
            Text = _s.Cancel,
            Width = 104,
            Height = 36,
            Margin = new Padding(0, 0, 10, 0),
        };
        _secondary.Click += (_, _) => Close();
        row.Controls.Add(_secondary);

        _primary = new FlatButton
        {
            Text = _s.Install,
            Width = 128,
            Height = 36,
            Primary = true,
        };
        _primary.Click += Primary_Click;
        row.Controls.Add(_primary);

        footer.Controls.Add(row);
        Controls.Add(footer);
    }

    /// <summary>
    /// Stacks a control down the body panel. Returns the control so call sites
    /// can keep configuring it, and records install-only controls so the
    /// finished state can hide them in one go.
    /// </summary>
    private T Stack<T>(T control, int gap = 0, bool installOnly = false) where T : Control
    {
        control.Location = new Point(Side, _cursor + gap);
        _cursor += gap + control.Height;
        if (installOnly) _installOnly.Add(control);
        _body.Controls.Add(control);
        return control;
    }

    private void BuildBody()
    {
        _cursor = 14;

        _langLabel = Stack(new Label
        {
            Text = _s.Language,
            Font = new Font("Segoe UI Variable Display", 9.5f, FontStyle.Bold),
            ForeColor = FgMuted,
            AutoSize = true,
            Height = 18,
        }, installOnly: true);

        var langRow = new Panel
        {
            Location = new Point(Side, _cursor + 8),
            Size = new Size(ClientSize.Width - Side * 2, 34),
        };
        _installOnly.Add(langRow);
        int bw = (langRow.Width - 4 * 8) / Loc.Languages.Length;
        for (int i = 0; i < Loc.Languages.Length; i++)
        {
            var (code, native) = Loc.Languages[i];
            var b = new FlatButton
            {
                Text = native,
                Size = new Size(bw, 32),
                Location = new Point(i * (bw + 8), 0),
                Tag = code,
            };
            b.Click += (_, _) => SetLanguage(code);
            _langButtons[i] = b;
            langRow.Controls.Add(b);
        }
        _body.Controls.Add(langRow);
        _cursor += 8 + 34 + 22;

        _welcomeTitle = Stack(new Label
        {
            Text = _s.WelcomeTitle,
            Font = new Font("Segoe UI Variable Display", 14f, FontStyle.Bold),
            ForeColor = FgPrimary,
            AutoSize = false,
            Size = new Size(ClientSize.Width - Side * 2, 24),
        });

        // The body text is the only control whose height depends on the
        // language, so it is measured rather than fixed.
        _welcomeBody = Stack(new Label
        {
            Text = _s.WelcomeBody,
            Font = new Font("Segoe UI Variable Display", 10f),
            ForeColor = FgSecondary,
            AutoSize = false,
            Size = new Size(ClientSize.Width - Side * 2, 118),
        }, gap: 8);

        _cursor += 20;

        _locationLabel = Stack(new Label
        {
            Text = _s.Location,
            Font = new Font("Segoe UI Variable Display", 9.5f, FontStyle.Bold),
            ForeColor = FgMuted,
            AutoSize = true,
            Height = 18,
        }, installOnly: true);

        var locRow = new Panel
        {
            Location = new Point(Side, _cursor + 6),
            Size = new Size(ClientSize.Width - Side * 2, 34),
        };
        _installOnly.Add(locRow);

        _pathBox = new TextBox
        {
            Location = new Point(0, 3),
            Size = new Size(locRow.Width - 100, 28),
            BackColor = BgField,
            ForeColor = FgPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9.5f),
        };
        locRow.Controls.Add(_pathBox);

        _browse = new FlatButton
        {
            Text = _s.Browse,
            Size = new Size(92, 28),
            Location = new Point(locRow.Width - 92, 3),
        };
        _browse.Click += Browse_Click;
        locRow.Controls.Add(_browse);

        _body.Controls.Add(locRow);
        _cursor += 6 + 34 + 12;

        _desktopBox = Stack(new CheckBox
        {
            Text = _s.Desktop,
            ForeColor = FgPrimary,
            AutoSize = true,
            Height = 22,
            Checked = true,
            FlatStyle = FlatStyle.Flat,
        }, gap: 2, installOnly: true);

        _startMenuBox = Stack(new CheckBox
        {
            Text = _s.StartMenu,
            ForeColor = FgPrimary,
            AutoSize = true,
            Height = 22,
            Checked = true,
            FlatStyle = FlatStyle.Flat,
        }, gap: 2, installOnly: true);

        // The launch checkbox only exists once something has been installed,
        // and sits directly under the finished message so it reads as part of
        // the same block rather than floating at the bottom of the window.
        _launchBox = Stack(new CheckBox
        {
            Text = _s.Launch,
            ForeColor = FgPrimary,
            AutoSize = true,
            Height = 22,
            Checked = true,
            FlatStyle = FlatStyle.Flat,
        }, gap: 18);

        _tallHeight = ClientSize.Height;
    }

    // MARK: language

    private void SetLanguage(string code)
    {
        Language = code;
        _s = Loc.For(code);

        for (int i = 0; i < _langButtons.Length; i++)
            _langButtons[i].Selected = Loc.Languages[i].Code == code;

        _langLabel.Text = _s.Language;
        _welcomeTitle.Text = _s.WelcomeTitle;
        _welcomeBody.Text = _s.WelcomeBody;
        _locationLabel.Text = _s.Location;
        _browse.Text = _s.Browse;
        _desktopBox.Text = _s.Desktop;
        _startMenuBox.Text = _s.StartMenu;
        _launchBox.Text = _s.Launch;

        if (_uninstall)
        {
            _primary.Text = _s.Uninstall;
            _secondary.Text = _s.Cancel;
        }
        else if (_primary.Enabled)
        {
            _primary.Text = _s.Install;
            _secondary.Text = _s.Cancel;
        }
    }

    // MARK: actions

    private void Browse_Click(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = _s.Location,
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_pathBox.Text) ? _pathBox.Text : Installer.DefaultTarget(),
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            _pathBox.Text = dlg.SelectedPath;
    }

    private void Primary_Click(object? sender, EventArgs e)
    {
        if (_uninstall)
        {
            RunUninstall();
            return;
        }

        var folder = _pathBox.Text.Trim();
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
            Program.Log($"cannot create {folder}: {ex.Message}");
            Fail(_s.Access, _s.AccessBody);
            return;
        }

        TargetFolder = folder;
        MakeDesktopShortcut = _desktopBox.Checked;
        MakeStartMenuShortcut = _startMenuBox.Checked;

        SetBusy(_s.Installing);

        // Off the UI thread: 67 MB of extraction takes a few seconds, and a
        // form that stops repainting looks hung.
        Task.Run(() =>
        {
            try
            {
                Installer.Install(folder,
                    report => Invoke(() => _status.Text = report),
                    pct => Invoke(() => _bar.Value = Math.Min(100, pct)));
                Invoke(ShowDone);
            }
            catch (Exception ex)
            {
                Program.Log($"install failed: {ex}");
                Invoke(() => Fail(_s.Access, ex.Message));
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
                Installer.Uninstall(TargetFolder!, _ => { });
                Invoke(ShowUninstallDone);
            }
            catch (Exception ex)
            {
                Program.Log($"uninstall failed: {ex}");
                Invoke(() => Fail(_s.Access, ex.Message));
            }
        });
    }

    private void SetBusy(string status)
    {
        _primary.Enabled = false;
        _secondary.Enabled = false;
        _browse.Enabled = false;
        _pathBox.Enabled = false;
        _desktopBox.Enabled = false;
        _startMenuBox.Enabled = false;
        foreach (var b in _langButtons) b.Enabled = false;

        _status.Visible = true;
        _bar.Visible = true;
        _bar.Value = 0;
        _status.Text = status;
    }

    private void ShowDone()
    {
        _status.Visible = false;
        _bar.Visible = false;
        _primary.Enabled = true;
        _secondary.Visible = false;

        // The install choices are gone, so hide them and pull the launch
        // checkbox up to sit under the finished message. Leaving them in place
        // and shrinking only the window left a tall empty gap.
        foreach (var c in _installOnly) c.Visible = false;
        _launchBox.Visible = true;
        _launchBox.Location = new Point(Side, _welcomeBody.Bottom + 26);

        _welcomeTitle.Text = _s.DoneTitle;
        _welcomeBody.Text = _s.DoneBody;

        _primary.Text = _s.Close;
        _primary.Click -= Primary_Click;
        _primary.Click += (_, _) =>
        {
            if (_launchBox.Checked)
                Installer.Launch(TargetFolder!);
            Program.FlushLog();
            Close();
        };

        // Shrink to fit what is actually left: a full-height window holding
        // four lines of text is mostly void.
        int needed = _launchBox.Bottom + FooterH + 24;
        if (needed < _tallHeight) ClientSize = new Size(ClientSize.Width, needed);

        Program.Log($"installed to {TargetFolder}");
    }

    private void ShowUninstall()
    {
        _s = Loc.For(Language);
        _welcomeTitle.Text = _s.UninstallTitle;
        _welcomeBody.Text = _s.UninstallBody;
        _primary.Text = _s.Uninstall;
        _secondary.Text = _s.Cancel;

        foreach (var c in _installOnly) c.Visible = false;

        // The path is shown read-only, because guessing it is how an
        // uninstaller deletes the wrong folder.
        var path = new Label
        {
            Text = TargetFolder,
            Font = new Font("Segoe UI", 9f),
            ForeColor = FgMuted,
            AutoSize = false,
            Size = new Size(ClientSize.Width - Side * 2, 18),
            Location = new Point(Side, _welcomeBody.Bottom + 22),
        };
        _body.Controls.Add(path);

        _launchBox.Visible = false;
        int needed = path.Bottom + FooterH + 24;
        if (needed < ClientSize.Height) ClientSize = new Size(ClientSize.Width, needed);
    }

    private void ShowUninstallDone()
    {
        _status.Visible = false;
        _bar.Visible = false;
        _welcomeTitle.Text = Installer.AppName;
        _welcomeBody.Text = _s.UninstallDone;
        _primary.Text = _s.Close;
        _primary.Enabled = true;
        _secondary.Visible = false;
        Program.FlushLog();
    }

    private void Fail(string title, string body)
    {
        _status.Visible = false;
        _bar.Visible = false;
        _primary.Enabled = true;
        _secondary.Enabled = true;
        MessageBox.Show(this, body, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        Result = 1;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Program.FlushLog();
        base.OnFormClosed(e);
    }
}
