namespace v2rayN.AutoSwitchCompanion;

public sealed class FloatingUsageController : IDisposable
{
    private readonly FloatingUsageWindow _window;
    private readonly V2rayNCompanionService _v2rayN;
    private readonly CloudflareAnalyticsClient _cloudflare = new();
    private readonly Action<string> _log;
    private readonly Action<CloudflareWorkerRule, WorkerUsage>? _usageUpdated;
    private readonly System.Windows.Forms.Timer _refreshTimer = new();
    private readonly System.Windows.Forms.Timer _settingsDebounceTimer = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private FloatingUsageState _lastState = FloatingUsageState.Loading;
    private string _lastCurrentGroup = string.Empty;
    private string _lastRuleQuerySignature = string.Empty;
    private FileSystemWatcher? _settingsWatcher;
    private bool _disposed;

    public FloatingUsageController(
        FloatingUsageWindow window,
        V2rayNCompanionService v2rayN,
        Action<string> log,
        Action<CloudflareWorkerRule, WorkerUsage>? usageUpdated = null)
    {
        _window = window;
        _v2rayN = v2rayN;
        _log = log;
        _usageUpdated = usageUpdated;
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _settingsDebounceTimer.Interval = 450;
        _settingsDebounceTimer.Tick += (_, _) =>
        {
            _settingsDebounceTimer.Stop();
            RefreshFromSettingsChange();
        };
    }

    public void Start()
    {
        var settings = SettingsStore.Load();
        ScheduleNextTick(settings);
        StartSettingsWatcher();
        _window.UpdateUsage(new FloatingUsageState
        {
            Message = UiText.For(settings.Language).Loading,
            Limit = CompanionSettings.CloudflareFreeDailyLimit
        });
        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (_disposed || !await _refreshLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            var settings = SettingsStore.Load();
            ScheduleNextTick(settings);
            var state = await BuildUsageStateAsync(settings);
            _lastState = state;
            if (!_window.IsDisposed)
            {
                _window.UpdateUsage(state);
            }
        }
        catch (Exception ex)
        {
            _log($"Floating usage refresh failed: {ex.Message}");
            if (!_window.IsDisposed)
            {
                _window.UpdateUsage(new FloatingUsageState
                {
                    Message = UiText.For(SettingsStore.Load().Language).QueryFailed,
                    IsError = true
                });
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void RefreshDisplayNameFromSettings()
    {
        if (_disposed || _window.IsDisposed)
        {
            return;
        }

        var settings = SettingsStore.Load();
        var currentRule = settings.Rules
            .Where(IsUsableRule)
            .FirstOrDefault(t => string.Equals(t.GroupName, _lastCurrentGroup, StringComparison.OrdinalIgnoreCase));

        if (currentRule == null || !_lastState.HasMatchingRule || _lastState.IsError)
        {
            _ = RefreshAsync();
            return;
        }

        UpdateDisplayName(currentRule.DisplayName);
    }

    public void RefreshFromSettingsChange()
    {
        if (_disposed || _window.IsDisposed)
        {
            return;
        }

        var settings = SettingsStore.Load();
        ScheduleNextTick(settings);
        var currentRule = settings.Rules
            .Where(IsUsableRule)
            .FirstOrDefault(t => string.Equals(t.GroupName, _lastCurrentGroup, StringComparison.OrdinalIgnoreCase));

        if (currentRule != null
            && _lastState.HasMatchingRule
            && !_lastState.IsError
            && string.Equals(BuildRuleQuerySignature(currentRule), _lastRuleQuerySignature, StringComparison.Ordinal))
        {
            UpdateDisplayName(currentRule.DisplayName);
            return;
        }

        _ = RefreshAsync();
    }

    public void Dispose()
    {
        _disposed = true;
        _refreshTimer.Stop();
        _refreshTimer.Dispose();
        _settingsDebounceTimer.Stop();
        _settingsDebounceTimer.Dispose();
        _settingsWatcher?.Dispose();
    }

    private async Task<FloatingUsageState> BuildUsageStateAsync(CompanionSettings settings)
    {
        var text = UiText.For(settings.Language);
        var selection = await _v2rayN.GetCurrentSelectionAsync();
        _lastCurrentGroup = selection.GroupName;
        if (string.IsNullOrWhiteSpace(selection.GroupName))
        {
            _lastRuleQuerySignature = string.Empty;
            return new FloatingUsageState
            {
                Message = text.NotMatchedGroup
            };
        }

        var currentRule = settings.Rules
            .Where(IsUsableRule)
            .FirstOrDefault(t => string.Equals(t.GroupName, selection.GroupName, StringComparison.OrdinalIgnoreCase));
        if (currentRule == null)
        {
            _lastRuleQuerySignature = string.Empty;
            return new FloatingUsageState
            {
                Message = text.NotMatchedGroup
            };
        }

        _lastRuleQuerySignature = BuildRuleQuerySignature(currentRule);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var usage = await _cloudflare.GetTodayUsageAsync(currentRule, cts.Token);
        _usageUpdated?.Invoke(currentRule, usage);
        return new FloatingUsageState
        {
            DisplayName = currentRule.DisplayName,
            ProfileName = selection.ProfileName,
            DelayDisplay = selection.DelayDisplay,
            SpeedDisplay = selection.SpeedDisplay,
            Requests = usage.Requests,
            Limit = CompanionSettings.CloudflareFreeDailyLimit,
            HasMatchingRule = true
        };
    }

    private void ScheduleNextTick(CompanionSettings settings)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, settings.DefaultCheckIntervalMinutes));
        var milliseconds = (int)Math.Clamp(interval.TotalMilliseconds, 1000, int.MaxValue);
        if (_refreshTimer.Interval != milliseconds)
        {
            _refreshTimer.Interval = milliseconds;
        }

        if (!_refreshTimer.Enabled)
        {
            _refreshTimer.Start();
        }
    }

    private void UpdateDisplayName(string displayName)
    {
        _lastState = new FloatingUsageState
        {
            DisplayName = displayName,
            ProfileName = _lastState.ProfileName,
            DelayDisplay = _lastState.DelayDisplay,
            SpeedDisplay = _lastState.SpeedDisplay,
            Requests = _lastState.Requests,
            Limit = _lastState.Limit,
            HasMatchingRule = true
        };
        _window.UpdateUsage(_lastState);
    }

    private void StartSettingsWatcher()
    {
        var settingsPath = SettingsStore.SettingsPath;
        var directory = Path.GetDirectoryName(settingsPath);
        var fileName = Path.GetFileName(settingsPath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        _settingsWatcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        _settingsWatcher.Changed += (_, _) => DebounceSettingsRefresh();
        _settingsWatcher.Created += (_, _) => DebounceSettingsRefresh();
        _settingsWatcher.Renamed += (_, _) => DebounceSettingsRefresh();
        _settingsWatcher.EnableRaisingEvents = true;
    }

    private void DebounceSettingsRefresh()
    {
        if (_disposed || _window.IsDisposed || !_window.IsHandleCreated)
        {
            return;
        }

        try
        {
            _window.BeginInvoke(() =>
            {
                if (_disposed || _window.IsDisposed)
                {
                    return;
                }

                _settingsDebounceTimer.Stop();
                _settingsDebounceTimer.Start();
            });
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static bool IsUsableRule(CloudflareWorkerRule rule)
    {
        return rule.Enabled
            && !string.IsNullOrWhiteSpace(rule.GroupName)
            && !string.IsNullOrWhiteSpace(rule.ApiToken);
    }

    private static string BuildRuleQuerySignature(CloudflareWorkerRule rule)
    {
        return string.Join('\u001f',
            rule.Enabled,
            rule.GroupName.Trim(),
            rule.ApiToken.Trim(),
            rule.AccountId.Trim(),
            rule.ScriptName.Trim(),
            rule.ThresholdRequests);
    }
}
