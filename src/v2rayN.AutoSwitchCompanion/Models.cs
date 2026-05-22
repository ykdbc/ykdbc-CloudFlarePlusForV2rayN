using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace v2rayN.AutoSwitchCompanion;

public sealed class CompanionSettings
{
    public const string DefaultSpeedTestUrl = "https://cdn.cloudflare.steamstatic.com/steam/apps/256843155/movie_max.mp4";
    public const int CloudflareFreeDailyLimit = 100000;
    public const int FloatingUsageBlueBelowRequests = 50000;
    public const int FloatingUsageYellowFromRequests = 50001;
    public const int FloatingUsageRedFromRequests = 90000;
    public const int MinimumRemainingRequestsForSpeedTest = 10000;
    public const string LanguageChinese = "zh-CN";
    public const string LanguageEnglish = "en-US";

    public bool AutoRestartV2rayN { get; set; } = true;
    public int DefaultCheckIntervalMinutes { get; set; } = 1;
    public int SpeedTestTimeoutMinutes { get; set; } = 20;
    public string SpeedTestUrl { get; set; } = DefaultSpeedTestUrl;
    public string Language { get; set; } = LanguageChinese;
    public List<CloudflareWorkerRule> Rules { get; set; } = [];
}

public sealed class CloudflareWorkerRule : INotifyPropertyChanged
{
    private long? _currentRequests;
    private long? _remainingRequests;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string ApiToken { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string ScriptName { get; set; } = string.Empty;
    public int ThresholdRequests { get; set; } = 90000;

    [JsonIgnore]
    public string DisplayName => Name.Trim();

    [JsonIgnore]
    public long? CurrentRequests
    {
        get => _currentRequests;
        set
        {
            if (_currentRequests == value)
            {
                return;
            }

            _currentRequests = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentRequestsDisplay));
        }
    }

    [JsonIgnore]
    public long? RemainingRequests
    {
        get => _remainingRequests;
        set
        {
            if (_remainingRequests == value)
            {
                return;
            }

            _remainingRequests = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RemainingRequestsDisplay));
        }
    }

    [JsonIgnore]
    public string CurrentRequestsDisplay => CurrentRequests.HasValue ? CurrentRequests.Value.ToString("N0") : string.Empty;

    [JsonIgnore]
    public string RemainingRequestsDisplay => RemainingRequests.HasValue ? RemainingRequests.Value.ToString("N0") : string.Empty;

    public void ClearRuntimeUsage()
    {
        CurrentRequests = null;
        RemainingRequests = null;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class WorkerUsage
{
    public string RuleName { get; init; } = string.Empty;
    public long Requests { get; init; }
    public long Subrequests { get; init; }
    public long Errors { get; init; }
    public List<string> WorkerNames { get; init; } = [];
    public List<string> AccountTags { get; init; } = [];
    public DateTimeOffset UtcStart { get; init; }
    public DateTimeOffset UtcEnd { get; init; }

    [JsonIgnore]
    public long RemainingRequests => Math.Max(0, CompanionSettings.CloudflareFreeDailyLimit - Requests);
}

public sealed class FloatingUsageState
{
    public string DisplayName { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string ProfileName { get; init; } = string.Empty;
    public string DelayDisplay { get; init; } = string.Empty;
    public string SpeedDisplay { get; init; } = string.Empty;
    public long Requests { get; init; }
    public long Limit { get; init; } = CompanionSettings.CloudflareFreeDailyLimit;
    public bool HasMatchingRule { get; init; }
    public bool IsError { get; init; }

    [JsonIgnore]
    public long RemainingRequests => Math.Max(0, Limit - Requests);

    [JsonIgnore]
    public float Level => Limit <= 0 ? 0 : Math.Clamp((float)Requests / Limit, 0, 1);

    public static FloatingUsageState Loading { get; } = new()
    {
        Message = "正在查询",
        Limit = CompanionSettings.CloudflareFreeDailyLimit
    };
}

public sealed class CandidateProfile
{
    public string IndexId { get; init; } = string.Empty;
    public string GroupId { get; init; } = string.Empty;
    public string GroupName { get; init; } = string.Empty;
    public string Remarks { get; init; } = string.Empty;
    public int Delay { get; init; }
    public decimal Speed { get; init; }
}

public sealed class V2rayNSelectionSnapshot
{
    public string GroupId { get; init; } = string.Empty;
    public string GroupName { get; init; } = string.Empty;
    public string ProfileId { get; init; } = string.Empty;
    public string ProfileName { get; init; } = string.Empty;
    public int Delay { get; init; }
    public decimal Speed { get; init; }

    [JsonIgnore]
    public string DelayDisplay => Delay > 0 ? $"{Delay} ms" : string.Empty;

    [JsonIgnore]
    public string SpeedDisplay => Speed > 0 ? $"{Speed:N2} MB/s" : string.Empty;
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string SettingsPath => Path.Combine(V2rayNHost.HostDirectory, "autoswitch-companion.json");

    public static CompanionSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new CompanionSettings();
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return Normalize(JsonSerializer.Deserialize<CompanionSettings>(json, JsonOptions) ?? new CompanionSettings());
        }
        catch
        {
            return new CompanionSettings();
        }
    }

    public static void Save(CompanionSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }

    private static CompanionSettings Normalize(CompanionSettings settings)
    {
        if (settings.DefaultCheckIntervalMinutes < 1)
        {
            settings.DefaultCheckIntervalMinutes = 1;
        }

        if (!string.Equals(settings.Language, CompanionSettings.LanguageEnglish, StringComparison.OrdinalIgnoreCase))
        {
            settings.Language = CompanionSettings.LanguageChinese;
        }

        return settings;
    }
}

public sealed class SortableBindingList<T> : BindingList<T>
{
    public SortableBindingList()
    {
    }

    public SortableBindingList(IList<T> list) : base(list)
    {
    }
}
