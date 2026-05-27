using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
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
    public PasswallSshSettings PasswallSsh { get; set; } = new();
    public List<CloudflareWorkerRule> Rules { get; set; } = [];
}

public sealed class PasswallSshSettings
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    public string UserName { get; set; } = "root";
    public string Password { get; set; } = string.Empty;
    public string PrivateKeyPath { get; set; } = string.Empty;
    public string UrlTestArgument { get; set; } = "urltest_node";
    public bool RestartAfterSwitch { get; set; } = true;
    public bool SwitchUdpWithTcp { get; set; }
    public int CommandTimeoutSeconds { get; set; } = 120;

    [JsonIgnore]
    public bool HasCredentials => !string.IsNullOrWhiteSpace(Password)
        || !string.IsNullOrWhiteSpace(PrivateKeyPath);

    [JsonIgnore]
    public bool CanConnect => !string.IsNullOrWhiteSpace(Host)
        && Port > 0
        && !string.IsNullOrWhiteSpace(UserName)
        && HasCredentials;

    [JsonIgnore]
    public bool IsUsable => Enabled && CanConnect;
}

public sealed class CloudflareWorkerRule : INotifyPropertyChanged
{
    private long? _currentRequests;
    private long? _remainingRequests;
    private string _apiToken = string.Empty;
    private bool _apiTokenVerified;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string PasswallGroup { get; set; } = string.Empty;
    public string ApiToken
    {
        get => _apiToken;
        set
        {
            var next = value?.Trim() ?? string.Empty;
            if (string.Equals(_apiToken, next, StringComparison.Ordinal))
            {
                return;
            }

            _apiToken = next;
            _apiTokenVerified = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ApiTokenDisplay));
            OnPropertyChanged(nameof(ApiTokenVerified));
        }
    }

    public bool ApiTokenVerified
    {
        get => _apiTokenVerified;
        set
        {
            if (_apiTokenVerified == value)
            {
                return;
            }

            _apiTokenVerified = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ApiTokenDisplay));
        }
    }

    public string AccountId { get; set; } = string.Empty;
    public string ScriptName { get; set; } = string.Empty;
    public int ThresholdRequests { get; set; } = 90000;

    [JsonIgnore]
    public string DisplayName => Name.Trim();

    [JsonIgnore]
    public string ApiTokenDisplay
    {
        get => ApiTokenVerified && !string.IsNullOrWhiteSpace(ApiToken)
            ? MaskToken(ApiToken)
            : ApiToken;
        set
        {
            if (ApiTokenVerified)
            {
                return;
            }

            ApiToken = value;
        }
    }

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

    public bool MarkApiTokenVerified()
    {
        if (string.IsNullOrWhiteSpace(ApiToken) || ApiTokenVerified)
        {
            return false;
        }

        ApiTokenVerified = true;
        return true;
    }

    public void ClearRuntimeUsage()
    {
        CurrentRequests = null;
        RemainingRequests = null;
    }

    private static string MaskToken(string token)
    {
        var trimmed = token.Trim();
        if (trimmed.Length <= 8)
        {
            return "****";
        }

        return $"{trimmed[..4]}...{trimmed[^4..]}";
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
    public string IpInfoPrefixDisplay { get; init; } = string.Empty;
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
    public string ProfileIpInfo { get; init; } = string.Empty;
    public int Delay { get; init; }
    public decimal Speed { get; init; }

    [JsonIgnore]
    public string IpInfoPrefixDisplay => ExtractIpInfoPrefix(ProfileIpInfo);

    [JsonIgnore]
    public string DelayDisplay => Delay > 0 ? $"{Delay} ms" : string.Empty;

    [JsonIgnore]
    public string SpeedDisplay => Speed > 0 ? $"{Speed:N2} MB/s" : string.Empty;

    private static string ExtractIpInfoPrefix(string ipInfo)
    {
        var value = ipInfo.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var open = value.IndexOf('(');
        if (open >= 0)
        {
            var close = value.IndexOf(')', open + 1);
            if (close > open + 1)
            {
                var countryCode = value[(open + 1)..close].Trim();
                return countryCode.Length <= 2
                    ? countryCode
                    : countryCode[..2];
            }
        }

        var text = new string(value.Where(char.IsLetterOrDigit).Take(2).ToArray());
        if (!string.IsNullOrEmpty(text))
        {
            return text;
        }

        return value.Length <= 2 ? value : value[..2];
    }
}

public static class SettingsStore
{
    private const string EncryptedFormat = "v2rayN.AutoSwitchCompanion.Settings.DPAPI.v1";

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
            var fileContent = File.ReadAllText(SettingsPath);
            var encrypted = TryReadEncryptedPayload(fileContent, out var json);
            var settings = Normalize(JsonSerializer.Deserialize<CompanionSettings>(json, JsonOptions) ?? new CompanionSettings());
            if (!encrypted)
            {
                BackupPlainSettingsBeforeEncryption();
                Save(settings);
            }

            return settings;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load companion settings from {SettingsPath}: {ex.Message}", ex);
        }
    }

    public static void Save(CompanionSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var payload = ProtectedSettingsPayload.Create(json);
        var encryptedJson = JsonSerializer.Serialize(payload, JsonOptions);
        File.WriteAllText(SettingsPath, encryptedJson);
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

        settings.PasswallSsh ??= new PasswallSshSettings();
        if (settings.PasswallSsh.Port < 1 || settings.PasswallSsh.Port > 65535)
        {
            settings.PasswallSsh.Port = 22;
        }

        if (settings.PasswallSsh.CommandTimeoutSeconds < 10)
        {
            settings.PasswallSsh.CommandTimeoutSeconds = 120;
        }

        if (string.IsNullOrWhiteSpace(settings.PasswallSsh.UserName))
        {
            settings.PasswallSsh.UserName = "root";
        }

        if (string.IsNullOrWhiteSpace(settings.PasswallSsh.UrlTestArgument))
        {
            settings.PasswallSsh.UrlTestArgument = "urltest_node";
        }

        return settings;
    }

    private static void BackupPlainSettingsBeforeEncryption()
    {
        var backupPath = $"{SettingsPath}.plain-before-encrypt-{DateTime.Now:yyyyMMddHHmmss}.bak";
        File.Copy(SettingsPath, backupPath, overwrite: false);
    }

    private static bool TryReadEncryptedPayload(string fileContent, out string json)
    {
        using var doc = JsonDocument.Parse(fileContent);
        if (!TryGetPropertyIgnoreCase(doc.RootElement, "format", out var formatElement)
            || !string.Equals(formatElement.GetString(), EncryptedFormat, StringComparison.Ordinal))
        {
            json = fileContent;
            return false;
        }

        if (!TryGetPropertyIgnoreCase(doc.RootElement, "protectedData", out var protectedDataElement))
        {
            throw new InvalidOperationException("Protected settings payload is missing.");
        }

        json = SettingsProtector.Unprotect(protectedDataElement.GetString() ?? string.Empty);
        return true;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private sealed class ProtectedSettingsPayload
    {
        public string Format { get; init; } = EncryptedFormat;
        public string Scope { get; init; } = "CurrentUser";
        public string ProtectedData { get; init; } = string.Empty;

        public static ProtectedSettingsPayload Create(string json)
        {
            return new ProtectedSettingsPayload
            {
                ProtectedData = SettingsProtector.Protect(json)
            };
        }
    }
}

internal static class SettingsProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CloudFlarePlusForV2rayN.AutoSwitchCompanion.Settings.v1");

    public static string Protect(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    public static string Unprotect(string protectedValue)
    {
        var protectedBytes = Convert.FromBase64String(protectedValue);
        var bytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
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
