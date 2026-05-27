using System.ComponentModel;

namespace v2rayN.AutoSwitchCompanion;

public sealed class AutoSwitchForm : Form
{
    private const int VisibleRuleRows = 4;
    private const int RuleRowHeight = 28;
    private const int RuleHeaderHeight = 30;
    private const int RuleGridChromeHeight = 3;

    private readonly Label _speedTestUrlLabel = new();
    private readonly Label _checkIntervalLabel = new();
    private readonly Label _timeoutLabel = new();
    private readonly TextBox _speedTestUrlBox = new();
    private readonly NumericUpDown _checkIntervalBox = new();
    private readonly NumericUpDown _timeoutBox = new();
    private readonly CheckBox _autoRestartBox = new();
    private readonly GroupBox _passwallGroupBox = new();
    private readonly CheckBox _passwallEnabledBox = new();
    private readonly Label _passwallHostLabel = new();
    private readonly Label _passwallPortLabel = new();
    private readonly Label _passwallUserLabel = new();
    private readonly Label _passwallPasswordLabel = new();
    private readonly Label _passwallPrivateKeyLabel = new();
    private readonly TextBox _passwallHostBox = new();
    private readonly NumericUpDown _passwallPortBox = new();
    private readonly TextBox _passwallUserBox = new();
    private readonly TextBox _passwallPasswordBox = new();
    private readonly TextBox _passwallPrivateKeyBox = new();
    private readonly CheckBox _passwallRestartBox = new();
    private readonly CheckBox _passwallSwitchUdpBox = new();
    private readonly Button _testPasswallButton = new();
    private readonly DataGridView _rulesGrid = new();
    private readonly TextBox _logBox = new();
    private readonly Button _startButton = new();
    private readonly Button _stopButton = new();
    private readonly Button _checkNowButton = new();
    private readonly Button _saveButton = new();
    private readonly Button _addButton = new();
    private readonly Button _removeButton = new();
    private readonly Button _openConfigButton = new();
    private readonly CheckBox _languageSwitch = new();
    private readonly Label _currentProfileLabel = new();
    private readonly NotifyIcon _notifyIcon = new();
    private readonly ContextMenuStrip _trayMenu = new();
    private readonly ToolStripMenuItem _refreshMenuItem = new();
    private readonly ToolStripMenuItem _switchGroupMenuItem = new();
    private readonly ToolStripMenuItem _toggleFloatingMenuItem = new();
    private readonly ToolStripMenuItem _aboutMenuItem = new();
    private readonly ToolStripMenuItem _exitMenuItem = new();
    private readonly System.Windows.Forms.Timer _ruleUsageTimer = new();
    private readonly SemaphoreSlim _ruleUsageRefreshLock = new(1, 1);
    private readonly BindingList<CloudflareWorkerRule> _rules;
    private readonly V2rayNCompanionService _v2rayN;
    private readonly PasswallSshService _passwall;
    private readonly AutoSwitchOrchestrator _orchestrator;
    private readonly CloudflareAnalyticsClient _cloudflare = new();
    private readonly FloatingUsageWindow _floatingWindow;
    private readonly FloatingUsageController _floatingController;
    private readonly Icon _appIcon;
    private CompanionSettings _settings;
    private UiText _text;
    private CancellationTokenSource? _monitorCts;
    private TextBox? _activeNameEditBox;
    private DateTimeOffset _lastFloatingManualRefresh = DateTimeOffset.MinValue;
    private bool _started;
    private bool _controlsLoaded;
    private bool _allowClose;
    private bool _loadingLanguageSwitch;

    public AutoSwitchForm()
    {
        _settings = SettingsStore.Load();
        _text = UiText.For(_settings.Language);
        _rules = new BindingList<CloudflareWorkerRule>(_settings.Rules);
        _appIcon = AppIconProvider.Load();
        _v2rayN = new V2rayNCompanionService(Log);
        _passwall = new PasswallSshService(Log);
        _orchestrator = new AutoSwitchOrchestrator(_v2rayN, _passwall, Log, UpdateRuleUsage);
        _floatingWindow = new FloatingUsageWindow();
        _floatingController = new FloatingUsageController(_floatingWindow, _v2rayN, Log, UpdateRuleUsage);

        Icon = _appIcon;
        Text = WindowsShellIdentity.ProductTitle;
        Width = 1180;
        Height = 820;
        MinimumSize = new Size(1040, 700);
        StartPosition = FormStartPosition.CenterScreen;

        BuildLayout();
        ConfigureTrayIcon();
        LoadSettingsToControls();
        ApplyLocalization();
        _floatingWindow.ManualRefreshRequested += async (_, _) => await ManualRefreshFromFloatingWindowAsync();
        _ruleUsageTimer.Tick += async (_, _) => await RefreshRuleUsageColumnsAsync();
        _controlsLoaded = true;
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(10)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, RulesGridHeight()));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var settingsPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 6,
            AutoSize = true
        };
        settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _speedTestUrlBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _speedTestUrlBox.Width = 520;
        _checkIntervalBox.Minimum = 1;
        _checkIntervalBox.Maximum = 1440;
        _timeoutBox.Minimum = 1;
        _timeoutBox.Maximum = 180;
        _autoRestartBox.AutoSize = true;
        _autoRestartBox.Margin = new Padding(0, 10, 12, 6);

        settingsPanel.Controls.Add(_speedTestUrlLabel, 0, 0);
        settingsPanel.Controls.Add(_speedTestUrlBox, 1, 0);
        settingsPanel.Controls.Add(_checkIntervalLabel, 2, 0);
        settingsPanel.Controls.Add(_checkIntervalBox, 3, 0);
        settingsPanel.Controls.Add(_timeoutLabel, 4, 0);
        settingsPanel.Controls.Add(_timeoutBox, 5, 0);

        ConfigureGrid();
        ConfigurePasswallPanel();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = false,
            AutoSize = true
        };

        ConfigureButton(_addButton, (_, _) => AddRule());
        ConfigureButton(_removeButton, (_, _) => RemoveSelectedRule());
        ConfigureButton(_saveButton, (_, _) => SaveFromControls());
        ConfigureButton(_checkNowButton, async (_, _) => await RunOnceFromUiAsync());
        ConfigureButton(_startButton, async (_, _) => await StartMonitorAsync());
        ConfigureButton(_stopButton, (_, _) => StopMonitor());
        ConfigureButton(_openConfigButton, (_, _) => OpenConfigFolder());
        _stopButton.Enabled = false;

        buttons.Controls.AddRange([
            _addButton,
            _removeButton,
            _saveButton,
            _checkNowButton,
            _startButton,
            _stopButton,
            _openConfigButton,
            _autoRestartBox
        ]);

        var statusRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            MinimumSize = new Size(0, 36),
            AutoSize = true
        };
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        statusRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        _currentProfileLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _currentProfileLabel.AutoSize = false;
        _currentProfileLabel.AutoEllipsis = true;
        _currentProfileLabel.TextAlign = ContentAlignment.MiddleLeft;
        _currentProfileLabel.Height = 28;
        _currentProfileLabel.Margin = new Padding(0, 6, 18, 0);
        _languageSwitch.Anchor = AnchorStyles.Right;
        _languageSwitch.AutoSize = true;
        _languageSwitch.Margin = new Padding(0, 6, 0, 0);
        _languageSwitch.CheckedChanged += LanguageSwitchCheckedChanged;
        statusRow.Controls.Add(_currentProfileLabel, 0, 0);
        statusRow.Controls.Add(_languageSwitch, 1, 0);

        _logBox.Dock = DockStyle.Fill;
        _logBox.Multiline = true;
        _logBox.ScrollBars = ScrollBars.Vertical;
        _logBox.ReadOnly = true;
        _logBox.Font = new Font(FontFamily.GenericMonospace, 9);

        root.Controls.Add(settingsPanel, 0, 0);
        root.Controls.Add(_passwallGroupBox, 0, 1);
        root.Controls.Add(_rulesGrid, 0, 2);
        root.Controls.Add(buttons, 0, 3);
        root.Controls.Add(_logBox, 0, 4);
        root.Controls.Add(statusRow, 0, 5);
        Controls.Add(root);
    }

    private void ConfigurePasswallPanel()
    {
        _passwallGroupBox.Dock = DockStyle.Top;
        _passwallGroupBox.AutoSize = true;
        _passwallGroupBox.Padding = new Padding(8);

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 10
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _passwallEnabledBox.AutoSize = true;
        _passwallEnabledBox.Margin = new Padding(0, 8, 12, 4);
        _passwallRestartBox.AutoSize = true;
        _passwallRestartBox.Margin = new Padding(8, 8, 12, 4);
        _passwallSwitchUdpBox.AutoSize = true;
        _passwallSwitchUdpBox.Margin = new Padding(8, 8, 12, 4);
        _passwallHostBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _passwallUserBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _passwallPasswordBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _passwallPrivateKeyBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _passwallPasswordBox.UseSystemPasswordChar = true;
        _passwallPortBox.Minimum = 1;
        _passwallPortBox.Maximum = 65535;
        _passwallPortBox.Width = 70;
        ConfigureButton(_testPasswallButton, async (_, _) => await TestPasswallFromUiAsync());

        panel.Controls.Add(_passwallEnabledBox, 0, 0);
        panel.SetColumnSpan(_passwallEnabledBox, 2);
        panel.Controls.Add(_passwallHostLabel, 2, 0);
        panel.Controls.Add(_passwallHostBox, 3, 0);
        panel.SetColumnSpan(_passwallHostBox, 3);
        panel.Controls.Add(_passwallPortLabel, 6, 0);
        panel.Controls.Add(_passwallPortBox, 7, 0);
        panel.Controls.Add(_testPasswallButton, 9, 0);

        panel.Controls.Add(_passwallUserLabel, 0, 1);
        panel.Controls.Add(_passwallUserBox, 1, 1);
        panel.Controls.Add(_passwallPasswordLabel, 2, 1);
        panel.Controls.Add(_passwallPasswordBox, 3, 1);
        panel.SetColumnSpan(_passwallPasswordBox, 3);
        panel.Controls.Add(_passwallPrivateKeyLabel, 6, 1);
        panel.Controls.Add(_passwallPrivateKeyBox, 7, 1);
        panel.SetColumnSpan(_passwallPrivateKeyBox, 2);

        panel.Controls.Add(_passwallRestartBox, 0, 2);
        panel.SetColumnSpan(_passwallRestartBox, 3);
        panel.Controls.Add(_passwallSwitchUdpBox, 3, 2);
        panel.SetColumnSpan(_passwallSwitchUdpBox, 3);

        _passwallGroupBox.Controls.Add(panel);
    }

    private void ConfigureGrid()
    {
        _rulesGrid.Dock = DockStyle.Fill;
        _rulesGrid.Height = RulesGridHeight();
        _rulesGrid.MinimumSize = new Size(0, RulesGridHeight());
        _rulesGrid.MaximumSize = new Size(int.MaxValue, RulesGridHeight());
        _rulesGrid.AutoGenerateColumns = false;
        _rulesGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _rulesGrid.ScrollBars = ScrollBars.Vertical;
        _rulesGrid.RowHeadersVisible = false;
        _rulesGrid.BackgroundColor = SystemColors.Window;
        _rulesGrid.BorderStyle = BorderStyle.FixedSingle;
        _rulesGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _rulesGrid.ColumnHeadersHeight = RuleHeaderHeight;
        _rulesGrid.RowTemplate.Height = RuleRowHeight;
        _rulesGrid.AllowUserToResizeRows = false;
        _rulesGrid.AllowUserToAddRows = false;
        _rulesGrid.AllowUserToDeleteRows = true;
        _rulesGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _rulesGrid.MultiSelect = false;
        _rulesGrid.ClipboardCopyMode = DataGridViewClipboardCopyMode.Disable;
        _rulesGrid.DataSource = _rules;
        _rulesGrid.CellValueChanged += (_, e) => SaveAndRefreshFloatingUsage(e.ColumnIndex);
        _rulesGrid.CellBeginEdit += RulesGridCellBeginEdit;
        _rulesGrid.CellFormatting += RulesGridCellFormatting;
        _rulesGrid.EditingControlShowing += RulesGridEditingControlShowing;
        _rulesGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_rulesGrid.IsCurrentCellDirty
                && _rulesGrid.CurrentCell?.OwningColumn is DataGridViewCheckBoxColumn)
            {
                _rulesGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        _rulesGrid.UserDeletedRow += (_, _) => SaveAndRefreshFloatingUsage();

        _rulesGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            DataPropertyName = nameof(CloudflareWorkerRule.Enabled),
            Name = nameof(CloudflareWorkerRule.Enabled),
            MinimumWidth = 42,
            FillWeight = 40
        });
        _rulesGrid.Columns.Add(TextColumn(nameof(CloudflareWorkerRule.Name), 90, 70));
        _rulesGrid.Columns.Add(TextColumn(nameof(CloudflareWorkerRule.GroupName), 105, 80));
        _rulesGrid.Columns.Add(TextColumn(nameof(CloudflareWorkerRule.PasswallGroup), 105, 80));
        _rulesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(CloudflareWorkerRule.ApiTokenDisplay),
            Name = nameof(CloudflareWorkerRule.ApiToken),
            MinimumWidth = 130,
            FillWeight = 190
        });
        _rulesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(CloudflareWorkerRule.ThresholdRequests),
            Name = nameof(CloudflareWorkerRule.ThresholdRequests),
            MinimumWidth = 68,
            FillWeight = 78
        });
        _rulesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(CloudflareWorkerRule.CurrentRequestsDisplay),
            Name = nameof(CloudflareWorkerRule.CurrentRequestsDisplay),
            ReadOnly = true,
            MinimumWidth = 78,
            FillWeight = 90
        });
        _rulesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(CloudflareWorkerRule.RemainingRequestsDisplay),
            Name = nameof(CloudflareWorkerRule.RemainingRequestsDisplay),
            ReadOnly = true,
            MinimumWidth = 78,
            FillWeight = 90
        });
    }

    private static int RulesGridHeight()
    {
        return RuleHeaderHeight + (RuleRowHeight * VisibleRuleRows) + RuleGridChromeHeight;
    }

    private static DataGridViewTextBoxColumn TextColumn(string propertyName, float fillWeight, int minimumWidth)
    {
        return new DataGridViewTextBoxColumn
        {
            DataPropertyName = propertyName,
            Name = propertyName,
            MinimumWidth = minimumWidth,
            FillWeight = fillWeight
        };
    }

    private void ConfigureTrayIcon()
    {
        _refreshMenuItem.Click += async (_, _) => await ManualRefreshAsync();
        _switchGroupMenuItem.DropDownOpening += (_, _) => RebuildSwitchGroupMenu();
        _toggleFloatingMenuItem.Click += (_, _) => ToggleFloatingWindow();
        _aboutMenuItem.Click += (_, _) => MessageBox.Show(_text.AboutText, _text.About, MessageBoxButtons.OK, MessageBoxIcon.Information);
        _exitMenuItem.Click += (_, _) =>
        {
            _allowClose = true;
            Close();
        };

        _trayMenu.Items.AddRange([
            _refreshMenuItem,
            _switchGroupMenuItem,
            _toggleFloatingMenuItem,
            _aboutMenuItem,
            _exitMenuItem
        ]);

        _notifyIcon.Icon = _appIcon;
        _notifyIcon.ContextMenuStrip = _trayMenu;
        _notifyIcon.Visible = true;
        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    private void ConfigureButton(Button button, EventHandler handler)
    {
        button.AutoSize = true;
        button.Margin = new Padding(0, 6, 8, 6);
        button.Click += handler;
    }

    private void LoadSettingsToControls()
    {
        _speedTestUrlBox.Text = string.IsNullOrWhiteSpace(_settings.SpeedTestUrl)
            ? CompanionSettings.DefaultSpeedTestUrl
            : _settings.SpeedTestUrl;
        _checkIntervalBox.Value = Math.Clamp(_settings.DefaultCheckIntervalMinutes, 1, 1440);
        _timeoutBox.Value = Math.Clamp(_settings.SpeedTestTimeoutMinutes, 1, 180);
        _autoRestartBox.Checked = _settings.AutoRestartV2rayN;
        _passwallEnabledBox.Checked = _settings.PasswallSsh.Enabled;
        _passwallHostBox.Text = _settings.PasswallSsh.Host;
        _passwallPortBox.Value = Math.Clamp(_settings.PasswallSsh.Port, 1, 65535);
        _passwallUserBox.Text = string.IsNullOrWhiteSpace(_settings.PasswallSsh.UserName)
            ? "root"
            : _settings.PasswallSsh.UserName;
        _passwallPasswordBox.Text = _settings.PasswallSsh.Password;
        _passwallPrivateKeyBox.Text = _settings.PasswallSsh.PrivateKeyPath;
        _passwallRestartBox.Checked = _settings.PasswallSsh.RestartAfterSwitch;
        _passwallSwitchUdpBox.Checked = _settings.PasswallSsh.SwitchUdpWithTcp;
        _loadingLanguageSwitch = true;
        _languageSwitch.Checked = string.Equals(_settings.Language, CompanionSettings.LanguageEnglish, StringComparison.OrdinalIgnoreCase);
        _loadingLanguageSwitch = false;
    }

    private void ApplyLocalization()
    {
        _text = UiText.For(_settings.Language);
        Text = WindowsShellIdentity.ProductTitle;
        _notifyIcon.Text = WindowsShellIdentity.ProductTitle;
        _speedTestUrlLabel.Text = _text.SpeedTestUrl;
        _checkIntervalLabel.Text = _text.CheckMinutes;
        _timeoutLabel.Text = _text.TestTimeoutMinutes;
        _autoRestartBox.Text = _text.RestartV2rayNAfterSwitch;
        _passwallGroupBox.Text = _text.PasswallSsh;
        _passwallEnabledBox.Text = _text.PasswallEnabled;
        _passwallHostLabel.Text = _text.PasswallHost;
        _passwallPortLabel.Text = _text.PasswallPort;
        _passwallUserLabel.Text = _text.PasswallUser;
        _passwallPasswordLabel.Text = _text.PasswallPassword;
        _passwallPrivateKeyLabel.Text = _text.PasswallPrivateKey;
        _passwallRestartBox.Text = _text.PasswallRestartAfterSwitch;
        _passwallSwitchUdpBox.Text = _text.PasswallSwitchUdpWithTcp;
        _testPasswallButton.Text = _text.TestPasswall;
        _addButton.Text = _text.AddRule;
        _removeButton.Text = _text.RemoveRule;
        _saveButton.Text = _text.Save;
        _checkNowButton.Text = _text.CheckNow;
        _startButton.Text = _text.StartMonitor;
        _stopButton.Text = _text.Stop;
        _openConfigButton.Text = _text.OpenConfigFolder;
        _languageSwitch.Text = _text.LanguageSwitch;
        _refreshMenuItem.Text = _text.Refresh;
        _switchGroupMenuItem.Text = _text.SwitchGroup;
        _aboutMenuItem.Text = _text.About;
        _exitMenuItem.Text = _text.Exit;
        UpdateFloatingMenuText();

        SetColumnHeader(nameof(CloudflareWorkerRule.Enabled), _text.Enabled);
        SetColumnHeader(nameof(CloudflareWorkerRule.Name), _text.Name);
        SetColumnHeader(nameof(CloudflareWorkerRule.GroupName), _text.GroupName);
        SetColumnHeader(nameof(CloudflareWorkerRule.PasswallGroup), _text.PasswallGroup);
        SetColumnHeader(nameof(CloudflareWorkerRule.ApiToken), _text.ApiToken);
        SetColumnHeader(nameof(CloudflareWorkerRule.ThresholdRequests), _text.Threshold);
        SetColumnHeader(nameof(CloudflareWorkerRule.CurrentRequestsDisplay), _text.CurrentUsage);
        SetColumnHeader(nameof(CloudflareWorkerRule.RemainingRequestsDisplay), _text.RemainingUsage);
    }

    private void SetColumnHeader(string name, string text)
    {
        var column = _rulesGrid.Columns[name];
        if (column != null)
        {
            column.HeaderText = text;
        }
    }

    private void LanguageSwitchCheckedChanged(object? sender, EventArgs e)
    {
        if (!_controlsLoaded || _loadingLanguageSwitch)
        {
            return;
        }

        _settings.Language = _languageSwitch.Checked
            ? CompanionSettings.LanguageEnglish
            : CompanionSettings.LanguageChinese;
        SettingsStore.Save(_settings);
        ApplyLocalization();
        _floatingController.RefreshFromSettingsChange();
    }

    private void RulesGridEditingControlShowing(object? sender, DataGridViewEditingControlShowingEventArgs e)
    {
        if (_activeNameEditBox != null)
        {
            _activeNameEditBox.TextChanged -= NameEditBoxTextChanged;
            _activeNameEditBox = null;
        }

        var column = _rulesGrid.CurrentCell?.OwningColumn;
        if (column?.DataPropertyName != nameof(CloudflareWorkerRule.Name)
            || e.Control is not TextBox textBox)
        {
            return;
        }

        _activeNameEditBox = textBox;
        _activeNameEditBox.TextChanged += NameEditBoxTextChanged;
    }

    private void RulesGridCellBeginEdit(object? sender, DataGridViewCellCancelEventArgs e)
    {
        if (e.RowIndex < 0
            || _rulesGrid.Columns[e.ColumnIndex].Name != nameof(CloudflareWorkerRule.ApiToken)
            || _rulesGrid.Rows[e.RowIndex].DataBoundItem is not CloudflareWorkerRule rule)
        {
            return;
        }

        if (rule.ApiTokenVerified)
        {
            e.Cancel = true;
        }
    }

    private void RulesGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0
            || _rulesGrid.Columns[e.ColumnIndex].Name != nameof(CloudflareWorkerRule.ApiToken)
            || _rulesGrid.Rows[e.RowIndex].DataBoundItem is not CloudflareWorkerRule rule
            || !rule.ApiTokenVerified)
        {
            return;
        }

        e.CellStyle.ForeColor = SystemColors.GrayText;
        e.CellStyle.SelectionForeColor = SystemColors.GrayText;
    }

    private void NameEditBoxTextChanged(object? sender, EventArgs e)
    {
        if (!_controlsLoaded
            || sender is not TextBox textBox
            || _rulesGrid.CurrentRow?.DataBoundItem is not CloudflareWorkerRule rule)
        {
            return;
        }

        rule.Name = textBox.Text;
        _settings.Rules = _rules.ToList();
        SettingsStore.Save(_settings);
        _floatingController.RefreshDisplayNameFromSettings();
    }

    private void SaveFromControls()
    {
        SaveFromControls(refreshFloatingUsage: true);
    }

    private void SaveFromControls(bool refreshFloatingUsage)
    {
        _rulesGrid.EndEdit();
        _settings.SpeedTestUrl = _speedTestUrlBox.Text.Trim();
        _settings.DefaultCheckIntervalMinutes = (int)_checkIntervalBox.Value;
        _settings.SpeedTestTimeoutMinutes = (int)_timeoutBox.Value;
        _settings.AutoRestartV2rayN = _autoRestartBox.Checked;
        _settings.PasswallSsh.Enabled = _passwallEnabledBox.Checked;
        _settings.PasswallSsh.Host = _passwallHostBox.Text.Trim();
        _settings.PasswallSsh.Port = (int)_passwallPortBox.Value;
        _settings.PasswallSsh.UserName = _passwallUserBox.Text.Trim();
        _settings.PasswallSsh.Password = _passwallPasswordBox.Text;
        _settings.PasswallSsh.PrivateKeyPath = _passwallPrivateKeyBox.Text.Trim();
        _settings.PasswallSsh.RestartAfterSwitch = _passwallRestartBox.Checked;
        _settings.PasswallSsh.SwitchUdpWithTcp = _passwallSwitchUdpBox.Checked;
        _settings.Language = _languageSwitch.Checked
            ? CompanionSettings.LanguageEnglish
            : CompanionSettings.LanguageChinese;
        _settings.Rules = _rules.ToList();
        SettingsStore.Save(_settings);
        UpdateRuleUsageTimerInterval();
        RebuildSwitchGroupMenu();
        Log($"{_text.SettingsSaved}: {SettingsStore.SettingsPath}");
        if (refreshFloatingUsage)
        {
            _ = _floatingController.RefreshAsync();
        }
    }

    private void AddRule()
    {
        var rule = new CloudflareWorkerRule
        {
            Name = $"worker-{_rules.Count + 1}",
            ThresholdRequests = 90000
        };
        _rules.Add(rule);
        SaveAndRefreshFloatingUsage();
        SelectRule(rule, beginEdit: true);
    }

    private void RemoveSelectedRule()
    {
        if (_rulesGrid.CurrentRow?.DataBoundItem is CloudflareWorkerRule rule)
        {
            _rulesGrid.EndEdit();
            if (!ConfirmRemoveRule(rule))
            {
                return;
            }

            var rowIndex = _rules.IndexOf(rule);
            _rules.Remove(rule);
            SaveAndRefreshFloatingUsage();
            if (_rules.Count > 0)
            {
                SelectRule(_rules[Math.Clamp(rowIndex, 0, _rules.Count - 1)], beginEdit: false);
            }
        }
    }

    private bool ConfirmRemoveRule(CloudflareWorkerRule rule)
    {
        var displayName = string.IsNullOrWhiteSpace(rule.DisplayName) ? "-" : rule.DisplayName;
        var groupName = string.IsNullOrWhiteSpace(rule.GroupName) ? "-" : rule.GroupName.Trim();
        var message = string.Format(_text.DeleteRuleConfirmMessageFormat, displayName, groupName);
        return MessageBox.Show(
            this,
            message,
            _text.DeleteRuleConfirmTitle,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }

    private void SaveAndRefreshFloatingUsage(int changedColumnIndex = -1)
    {
        if (!_controlsLoaded || !IsHandleCreated || Disposing || IsDisposed)
        {
            return;
        }

        SaveFromControls(refreshFloatingUsage: false);
        if (changedColumnIndex >= 0
            && _rulesGrid.Columns[changedColumnIndex].DataPropertyName == nameof(CloudflareWorkerRule.Name))
        {
            _floatingController.RefreshDisplayNameFromSettings();
            return;
        }

        _ = _floatingController.RefreshAsync();
    }

    private void SelectRule(CloudflareWorkerRule rule, bool beginEdit)
    {
        var rowIndex = _rules.IndexOf(rule);
        if (rowIndex < 0 || rowIndex >= _rulesGrid.Rows.Count)
        {
            return;
        }

        var column = _rulesGrid.Columns[nameof(CloudflareWorkerRule.Name)];
        if (column == null)
        {
            return;
        }

        _rulesGrid.Focus();
        _rulesGrid.ClearSelection();
        _rulesGrid.CurrentCell = _rulesGrid.Rows[rowIndex].Cells[column.Index];
        _rulesGrid.Rows[rowIndex].Selected = true;
        _rulesGrid.FirstDisplayedScrollingRowIndex = rowIndex;
        if (beginEdit)
        {
            _rulesGrid.BeginEdit(selectAll: true);
        }
    }

    private async Task RunOnceFromUiAsync()
    {
        SaveFromControls();
        using var cts = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            await _orchestrator.RunOnceAsync(_settings, cts.Token);
            await UpdateCurrentSelectionStatusAsync();
        }
        catch (Exception ex)
        {
            Log(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task TestPasswallFromUiAsync()
    {
        SaveFromControls(refreshFloatingUsage: false);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(10, _settings.PasswallSsh.CommandTimeoutSeconds)));
        _testPasswallButton.Enabled = false;
        try
        {
            await _passwall.TestConnectionAsync(_settings.PasswallSsh, cts.Token);
            var groups = await _passwall.ListGroupsAsync(_settings.PasswallSsh, cts.Token);
            Log($"Passwall groups: {(groups.Count == 0 ? "<none>" : string.Join(", ", groups))}");
        }
        catch (Exception ex)
        {
            Log(ex.Message);
        }
        finally
        {
            _testPasswallButton.Enabled = true;
        }
    }

    private async Task StartMonitorAsync()
    {
        if (_monitorCts != null)
        {
            return;
        }

        SaveFromControls();
        _monitorCts = new CancellationTokenSource();
        _startButton.Enabled = false;
        _stopButton.Enabled = true;
        Log(_text.MonitorStarted);

        try
        {
            await _orchestrator.RunStartupSelectionAsync(_settings, _monitorCts.Token);
            await UpdateCurrentSelectionStatusAsync();

            while (!_monitorCts.IsCancellationRequested)
            {
                var interval = TimeSpan.FromMinutes(Math.Max(1, _settings.DefaultCheckIntervalMinutes));
                await Task.Delay(interval, _monitorCts.Token);
                await _orchestrator.RunPeriodicCheckAsync(_settings, _monitorCts.Token);
                await UpdateCurrentSelectionStatusAsync();
            }
        }
        catch (OperationCanceledException)
        {
            Log(_text.MonitorStopped);
        }
        catch (Exception ex)
        {
            Log(ex.Message);
        }
        finally
        {
            _monitorCts?.Dispose();
            _monitorCts = null;
            _startButton.Enabled = true;
            _stopButton.Enabled = false;
        }
    }

    private void StopMonitor()
    {
        _monitorCts?.Cancel();
    }

    private static void OpenConfigFolder()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = AppContext.BaseDirectory,
            UseShellExecute = true
        });
    }

    private void SetBusy(bool busy)
    {
        _checkNowButton.Enabled = !busy;
        _saveButton.Enabled = !busy;
        _rulesGrid.Enabled = !busy;
    }

    private async Task ManualRefreshAsync()
    {
        SaveFromControls(refreshFloatingUsage: false);
        await _floatingController.RefreshAsync();
        await RefreshRuleUsageColumnsAsync();
        await UpdateCurrentSelectionStatusAsync();
    }

    private async Task ManualRefreshFromFloatingWindowAsync()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastFloatingManualRefresh < TimeSpan.FromMinutes(1))
        {
            Log(_text.FloatingRefreshCooldown);
            return;
        }

        _lastFloatingManualRefresh = now;
        await ManualRefreshAsync();
    }

    private async Task RefreshRuleUsageColumnsAsync()
    {
        if (!await _ruleUsageRefreshLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            var rules = _rules
                .Where(IsUsableRule)
                .ToList();
            if (rules.Count == 0)
            {
                return;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            foreach (var rule in rules)
            {
                try
                {
                    var usage = await _cloudflare.GetTodayUsageAsync(rule, cts.Token);
                    UpdateRuleUsage(rule, usage);
                }
                catch (Exception ex)
                {
                    rule.ClearRuntimeUsage();
                    Log($"{rule.GroupName}: {ex.Message}");
                }
            }
        }
        finally
        {
            _ruleUsageRefreshLock.Release();
        }
    }

    private void UpdateRuleUsage(CloudflareWorkerRule sourceRule, WorkerUsage usage)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => UpdateRuleUsage(sourceRule, usage));
            return;
        }

        var rule = _rules.FirstOrDefault(t =>
            string.Equals(t.GroupName, sourceRule.GroupName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(t.ApiToken, sourceRule.ApiToken, StringComparison.Ordinal));
        if (rule == null)
        {
            return;
        }

        rule.CurrentRequests = usage.Requests;
        rule.RemainingRequests = usage.RemainingRequests;
        if (rule.MarkApiTokenVerified())
        {
            _settings.Rules = _rules.ToList();
            SettingsStore.Save(_settings);
            _rulesGrid.InvalidateRow(_rules.IndexOf(rule));
        }
    }

    private async Task UpdateCurrentSelectionStatusAsync()
    {
        try
        {
            var selection = await _v2rayN.GetCurrentSelectionAsync();
            SetCurrentSelectionStatus(selection);
        }
        catch (Exception ex)
        {
            _currentProfileLabel.Text = $"{_text.CurrentSelection}: {_text.QueryFailed} ({ex.Message})";
        }
    }

    private void SetCurrentSelectionStatus(V2rayNSelectionSnapshot selection)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetCurrentSelectionStatus(selection));
            return;
        }

        var delay = string.IsNullOrWhiteSpace(selection.DelayDisplay) ? "-" : selection.DelayDisplay;
        var speed = string.IsNullOrWhiteSpace(selection.SpeedDisplay) ? "-" : selection.SpeedDisplay;
        _currentProfileLabel.Text = $"{_text.CurrentSelection}: {selection.GroupName} / {selection.ProfileName}  {_text.Delay}: {delay}  {_text.Speed}: {speed}";
    }

    private void RebuildSwitchGroupMenu()
    {
        _switchGroupMenuItem.DropDownItems.Clear();
        var groups = _rules
            .Where(t => t.Enabled && !string.IsNullOrWhiteSpace(t.GroupName))
            .Select(t => t.GroupName.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (groups.Count == 0)
        {
            var empty = new ToolStripMenuItem(_text.NoConfiguredGroup)
            {
                Enabled = false
            };
            _switchGroupMenuItem.DropDownItems.Add(empty);
            return;
        }

        foreach (var group in groups)
        {
            var item = new ToolStripMenuItem(group);
            item.Click += async (_, _) => await SwitchGroupFromTrayAsync(group);
            _switchGroupMenuItem.DropDownItems.Add(item);
        }
    }

    private async Task SwitchGroupFromTrayAsync(string groupName)
    {
        SaveFromControls(refreshFloatingUsage: false);
        try
        {
            var selection = await _v2rayN.SwitchToGroupWithoutSpeedTestAsync(groupName);
            if (selection != null)
            {
                SetCurrentSelectionStatus(selection);
                await SyncPasswallForGroupAsync(groupName);
                await _floatingController.RefreshAsync();
            }
        }
        catch (Exception ex)
        {
            Log(ex.Message);
        }
    }

    private async Task SyncPasswallForGroupAsync(string groupName)
    {
        if (!_settings.PasswallSsh.Enabled)
        {
            return;
        }

        var rule = _rules.FirstOrDefault(t =>
            t.Enabled && string.Equals(t.GroupName, groupName, StringComparison.OrdinalIgnoreCase));
        if (rule == null)
        {
            Log($"Passwall sync skipped: no rule matched v2rayN group '{groupName}'.");
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(10, _settings.PasswallSsh.CommandTimeoutSeconds)));
            await _passwall.SwitchToBestNodeAsync(_settings.PasswallSsh, rule.PasswallGroup, cts.Token);
        }
        catch (Exception ex)
        {
            Log($"Passwall sync failed: {ex.Message}");
        }
    }

    private void ToggleFloatingWindow()
    {
        SetFloatingVisible(!_floatingWindow.Visible);
    }

    private void SetFloatingVisible(bool visible)
    {
        if (visible)
        {
            _floatingWindow.Show();
            _floatingWindow.TopMost = true;
        }
        else
        {
            _floatingWindow.Hide();
        }

        UpdateFloatingMenuText();
    }

    private void UpdateFloatingMenuText()
    {
        _toggleFloatingMenuItem.Text = _floatingWindow.Visible
            ? _text.HideFloating
            : _text.ShowFloating;
    }

    private void UpdateRuleUsageTimerInterval()
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, _settings.DefaultCheckIntervalMinutes));
        var milliseconds = (int)Math.Clamp(interval.TotalMilliseconds, 1000, int.MaxValue);
        if (_ruleUsageTimer.Interval != milliseconds)
        {
            _ruleUsageTimer.Interval = milliseconds;
        }
    }

    private void ShowMainWindow()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void HideToTray()
    {
        ShowInTaskbar = false;
        Hide();
    }

    private void Log(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => Log(message));
            return;
        }

        _logBox.AppendText($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_started)
        {
            return;
        }

        _started = true;
        SetFloatingVisible(true);
        _floatingController.Start();
        UpdateRuleUsageTimerInterval();
        _ruleUsageTimer.Start();
        _ = RefreshRuleUsageColumnsAsync();
        _ = UpdateCurrentSelectionStatusAsync();
        _ = StartMonitorAsync();
        HideToTray();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_started && WindowState == FormWindowState.Minimized)
        {
            HideToTray();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        if (_activeNameEditBox != null)
        {
            _activeNameEditBox.TextChanged -= NameEditBoxTextChanged;
            _activeNameEditBox = null;
        }

        _monitorCts?.Cancel();
        SaveFromControls(refreshFloatingUsage: false);
        _ruleUsageTimer.Stop();
        _ruleUsageTimer.Dispose();
        _floatingController.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _trayMenu.Dispose();
        if (!_floatingWindow.IsDisposed)
        {
            _floatingWindow.Close();
        }

        _appIcon.Dispose();
        base.OnFormClosing(e);
    }

    private static bool IsUsableRule(CloudflareWorkerRule rule)
    {
        return rule.Enabled
            && !string.IsNullOrWhiteSpace(rule.GroupName)
            && !string.IsNullOrWhiteSpace(rule.ApiToken);
    }
}
