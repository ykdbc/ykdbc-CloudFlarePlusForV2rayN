using ServiceLib.Common;
using ServiceLib.Enums;
using ServiceLib.Handler;
using ServiceLib.Helper;
using ServiceLib.Manager;
using ServiceLib.Models.Configs;
using ServiceLib.Models.Entities;
using ServiceLib.Resx;
using ServiceLib.Services;

namespace v2rayN.AutoSwitchCompanion;

public sealed class V2rayNCompanionService
{
    private readonly record struct ProfileGroupInfo(string GroupId, string GroupName);

    private readonly Action<string> _log;
    private readonly SemaphoreSlim _serviceLock = new(1, 1);
    private bool _initialized;

    public V2rayNCompanionService(Action<string> log)
    {
        _log = log;
    }

    public string AppDirectory => V2rayNHost.HostDirectory;

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        await _serviceLock.WaitAsync();
        try
        {
            if (_initialized)
            {
                return;
            }

            if (!File.Exists(Path.Combine(AppDirectory, "v2rayN.exe")))
            {
                _log("Tip: v2rayN.exe was not found in the resolved host directory.");
            }

            if (!AppManager.Instance.InitApp())
            {
                throw new InvalidOperationException("Failed to load v2rayN configuration.");
            }

            AppManager.Instance.InitComponents();
            await ProfileExManager.Instance.Init();
            await CoreManager.Instance.Init(AppManager.Instance.Config, async (_, msg) =>
            {
                if (!string.IsNullOrWhiteSpace(msg))
                {
                    _log(msg);
                }
                await Task.CompletedTask;
            });

            _initialized = true;
        }
        finally
        {
            _serviceLock.Release();
        }
    }

    public async Task EnsureSpeedTestUrlAsync(string speedTestUrl)
    {
        await InitializeAsync();
        if (string.IsNullOrWhiteSpace(speedTestUrl))
        {
            return;
        }

        await _serviceLock.WaitAsync();
        try
        {
            var config = ReloadConfigFromDisk();
            config.SpeedTestItem.SpeedTestUrl = speedTestUrl.Trim();
            await ConfigHandler.SaveConfig(config);
            _log($"Speed test URL set to {config.SpeedTestItem.SpeedTestUrl}");
        }
        finally
        {
            _serviceLock.Release();
        }
    }

    public async Task<string> GetCurrentGroupNameAsync()
    {
        await InitializeAsync();
        return (await GetCurrentSelectionAsync()).GroupName;
    }

    public async Task<V2rayNSelectionSnapshot> GetCurrentSelectionAsync()
    {
        await InitializeAsync();
        await _serviceLock.WaitAsync();
        try
        {
            var config = ReloadConfigFromDisk();
            var active = await AppManager.Instance.GetProfileItem(config.IndexId);
            var subId = active?.Subid ?? config.SubIndexId ?? string.Empty;
            var sub = await SQLiteHelper.Instance.TableAsync<SubItem>().FirstOrDefaultAsync(t => t.Id == subId);
            var profileExs = await ReloadProfileExsAsync();
            var profileEx = profileExs.FirstOrDefault(t => string.Equals(t.IndexId, active?.IndexId ?? config.IndexId, StringComparison.OrdinalIgnoreCase));
            return new V2rayNSelectionSnapshot
            {
                GroupId = sub?.Id ?? subId,
                GroupName = sub?.Remarks ?? string.Empty,
                ProfileId = active?.IndexId ?? config.IndexId ?? string.Empty,
                ProfileName = active?.Remarks ?? string.Empty,
                ProfileIpInfo = profileEx?.IpInfo ?? string.Empty,
                Delay = profileEx?.Delay ?? 0,
                Speed = profileEx?.Speed ?? 0
            };
        }
        finally
        {
            _serviceLock.Release();
        }
    }

    public async Task<CandidateProfile?> SwitchToGroupByMixedTestAsync(string groupName, CompanionSettings settings, CancellationToken cancellationToken)
    {
        await InitializeAsync();
        await EnsureSpeedTestUrlAsync(settings.SpeedTestUrl);

        var sub = await SQLiteHelper.Instance.TableAsync<SubItem>()
            .FirstOrDefaultAsync(t => t.Remarks == groupName);
        if (sub == null)
        {
            _log($"Target subscription group not found: {groupName}");
            return null;
        }

        var profiles = await SQLiteHelper.Instance.TableAsync<ProfileItem>()
            .Where(t => t.Subid == sub.Id)
            .ToListAsync();
        profiles = profiles
            .Where(t => !string.IsNullOrWhiteSpace(t.IndexId))
            .OrderBy(t => t.Remarks)
            .ToList();

        if (profiles.Count == 0)
        {
            _log($"Target group has no profiles: {groupName}");
            return null;
        }

        _log($"Running mixed latency/speed test for group '{groupName}' with {profiles.Count} profiles.");
        if (!await RunMixedTestAsync(profiles, settings, cancellationToken))
        {
            return null;
        }

        var profileGroups = new Dictionary<string, ProfileGroupInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in profiles)
        {
            profileGroups.TryAdd(profile.IndexId, new ProfileGroupInfo(sub.Id, sub.Remarks));
        }

        var best = await SelectBestProfileAsync(profiles, profileGroups);
        if (best == null)
        {
            _log("No usable speed test result was found.");
            return null;
        }

        var config = ReloadConfigFromDisk();
        config.SubIndexId = best.GroupId;
        if (await ConfigHandler.SetDefaultServerIndex(config, best.IndexId) != 0)
        {
            _log("Failed to set default server.");
            return null;
        }

        _log($"Selected '{best.Remarks}' in group '{best.GroupName}' delay={best.Delay}ms speed={best.Speed} MB/s.");
        if (settings.AutoRestartV2rayN)
        {
            RestartV2rayN();
        }
        else
        {
            _log("Default server was saved. Reload v2rayN manually if it is already running.");
        }

        return best;
    }

    public async Task<CandidateProfile?> SwitchToBestProfileAcrossGroupsAsync(
        IReadOnlyCollection<string> groupNames,
        CompanionSettings settings,
        CancellationToken cancellationToken)
    {
        await InitializeAsync();
        await EnsureSpeedTestUrlAsync(settings.SpeedTestUrl);

        var requestedGroups = groupNames
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (requestedGroups.Count == 0)
        {
            _log("No target groups were provided for speed testing.");
            return null;
        }

        var subscriptions = await SQLiteHelper.Instance.TableAsync<SubItem>().ToListAsync();
        var profiles = new List<ProfileItem>();
        var profileGroups = new Dictionary<string, ProfileGroupInfo>(StringComparer.OrdinalIgnoreCase);
        var matchedGroups = new List<string>();

        foreach (var groupName in requestedGroups)
        {
            var sub = subscriptions.FirstOrDefault(t => string.Equals(t.Remarks, groupName, StringComparison.OrdinalIgnoreCase));
            if (sub == null)
            {
                _log($"Target subscription group not found: {groupName}");
                continue;
            }

            var groupProfiles = await SQLiteHelper.Instance.TableAsync<ProfileItem>()
                .Where(t => t.Subid == sub.Id)
                .ToListAsync();
            groupProfiles = groupProfiles
                .Where(t => !string.IsNullOrWhiteSpace(t.IndexId))
                .OrderBy(t => t.Remarks)
                .ToList();
            if (groupProfiles.Count == 0)
            {
                _log($"Target group has no profiles: {groupName}");
                continue;
            }

            matchedGroups.Add(sub.Remarks);
            foreach (var profile in groupProfiles)
            {
                if (profileGroups.ContainsKey(profile.IndexId))
                {
                    continue;
                }

                profiles.Add(profile);
                profileGroups[profile.IndexId] = new ProfileGroupInfo(sub.Id, sub.Remarks);
            }
        }

        if (profiles.Count == 0)
        {
            _log("No profiles were found in the eligible groups.");
            return null;
        }

        _log($"Running mixed latency/speed test for {matchedGroups.Count} groups with {profiles.Count} profiles.");
        if (!await RunMixedTestAsync(profiles, settings, cancellationToken))
        {
            return null;
        }

        var best = await SelectBestProfileAsync(profiles, profileGroups);
        if (best == null)
        {
            _log("No usable speed test result was found.");
            return null;
        }

        var config = ReloadConfigFromDisk();
        config.SubIndexId = best.GroupId;
        if (await ConfigHandler.SetDefaultServerIndex(config, best.IndexId) != 0)
        {
            _log("Failed to set default server.");
            return null;
        }

        _log($"Selected '{best.Remarks}' in group '{best.GroupName}' delay={best.Delay}ms speed={best.Speed} MB/s.");
        if (settings.AutoRestartV2rayN)
        {
            RestartV2rayN();
        }
        else
        {
            _log("Default server was saved. Reload v2rayN manually if it is already running.");
        }

        return best;
    }

    public async Task<V2rayNSelectionSnapshot?> SwitchToGroupWithoutSpeedTestAsync(string groupName)
    {
        await InitializeAsync();

        var sub = await SQLiteHelper.Instance.TableAsync<SubItem>()
            .FirstOrDefaultAsync(t => t.Remarks == groupName);
        if (sub == null)
        {
            _log($"Target subscription group not found: {groupName}");
            return null;
        }

        var profiles = await SQLiteHelper.Instance.TableAsync<ProfileItem>()
            .Where(t => t.Subid == sub.Id)
            .ToListAsync();
        var profile = profiles
            .Where(t => !string.IsNullOrWhiteSpace(t.IndexId))
            .OrderBy(t => t.Remarks)
            .FirstOrDefault();
        if (profile == null)
        {
            _log($"Target group has no profiles: {groupName}");
            return null;
        }

        var config = ReloadConfigFromDisk();
        config.SubIndexId = sub.Id;
        if (await ConfigHandler.SetDefaultServerIndex(config, profile.IndexId) != 0)
        {
            _log("Failed to set default server.");
            return null;
        }

        _log($"Simulated selection: group='{sub.Remarks}', profile='{profile.Remarks}'.");
        var profileExs = await ReloadProfileExsAsync();
        var profileEx = profileExs.FirstOrDefault(t => string.Equals(t.IndexId, profile.IndexId, StringComparison.OrdinalIgnoreCase));
        return new V2rayNSelectionSnapshot
        {
            GroupId = sub.Id,
            GroupName = sub.Remarks,
            ProfileId = profile.IndexId,
            ProfileName = profile.Remarks,
            ProfileIpInfo = profileEx?.IpInfo ?? string.Empty,
            Delay = profileEx?.Delay ?? 0,
            Speed = profileEx?.Speed ?? 0
        };
    }

    public async Task<bool> RestoreSelectionAsync(V2rayNSelectionSnapshot snapshot)
    {
        await InitializeAsync();
        if (string.IsNullOrWhiteSpace(snapshot.ProfileId))
        {
            return false;
        }

        var config = ReloadConfigFromDisk();
        config.SubIndexId = snapshot.GroupId;
        return await ConfigHandler.SetDefaultServerIndex(config, snapshot.ProfileId) == 0;
    }

    private static Config ReloadConfigFromDisk()
    {
        var latest = ConfigHandler.LoadConfig();
        if (latest == null)
        {
            return AppManager.Instance.Config;
        }

        var target = AppManager.Instance.Config;
        foreach (var property in typeof(Config).GetProperties())
        {
            if (property.CanRead && property.CanWrite)
            {
                property.SetValue(target, property.GetValue(latest));
            }
        }

        return target;
    }

    private static async Task<IEnumerable<ProfileExItem>> ReloadProfileExsAsync()
    {
        await ProfileExManager.Instance.Init();
        return await ProfileExManager.Instance.GetProfileExs();
    }

    private async Task<bool> RunMixedTestAsync(
        List<ProfileItem> profiles,
        CompanionSettings settings,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var speedtest = new SpeedtestService(AppManager.Instance.Config, async result =>
        {
            if (string.IsNullOrWhiteSpace(result.IndexId)
                && string.Equals(result.Delay, ResUI.SpeedtestingCompleted, StringComparison.Ordinal))
            {
                completion.TrySetResult();
            }

            await Task.CompletedTask;
        });

        speedtest.RunLoop(ESpeedActionType.Mixedtest, profiles);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(Math.Max(1, settings.SpeedTestTimeoutMinutes)));
        using (timeoutCts.Token.Register(() => completion.TrySetCanceled(timeoutCts.Token)))
        {
            try
            {
                await completion.Task;
            }
            catch (OperationCanceledException)
            {
                speedtest.ExitLoop();
                _log("Speed test timed out.");
                return false;
            }
        }

        return true;
    }

    private static async Task<CandidateProfile?> SelectBestProfileAsync(
        List<ProfileItem> profiles,
        IReadOnlyDictionary<string, ProfileGroupInfo> profileGroups)
    {
        var profileIds = profiles.Select(t => t.IndexId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var profileMap = new Dictionary<string, ProfileItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in profiles)
        {
            profileMap.TryAdd(profile.IndexId, profile);
        }

        var profileExs = await ReloadProfileExsAsync();

        var candidates = profileExs
            .Where(t => profileIds.Contains(t.IndexId) && t.Delay > 0)
            .Select(t =>
            {
                profileGroups.TryGetValue(t.IndexId, out var group);
                return new CandidateProfile
                {
                    IndexId = t.IndexId,
                    GroupId = group.GroupId ?? string.Empty,
                    GroupName = group.GroupName ?? string.Empty,
                    Remarks = profileMap.TryGetValue(t.IndexId, out var profile) ? profile.Remarks : t.IndexId,
                    Delay = t.Delay,
                    Speed = t.Speed
                };
            })
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        var withSpeed = candidates.Where(t => t.Speed > 0).ToList();
        var pool = withSpeed.Count > 0 ? withSpeed : candidates;
        return pool
            .OrderByDescending(t => t.Speed)
            .ThenBy(t => t.Delay)
            .FirstOrDefault();
    }

    private void RestartV2rayN()
    {
        var exePath = Path.Combine(AppDirectory, "v2rayN.exe");
        if (!File.Exists(exePath))
        {
            _log("v2rayN.exe not found in companion directory. Please reload v2rayN manually.");
            return;
        }

        try
        {
            var current = Environment.ProcessId;
            foreach (var process in Process.GetProcessesByName("v2rayN"))
            {
                if (process.Id == current)
                {
                    continue;
                }

                try
                {
                    var processDir = Path.GetDirectoryName(process.MainModule?.FileName ?? string.Empty);
                    if (!string.Equals(processDir, AppDirectory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    process.Kill(true);
                    process.WaitForExit(5000);
                }
                catch (Exception ex)
                {
                    _log($"Failed to stop v2rayN process {process.Id}: {ex.Message}");
                }
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = AppDirectory,
                UseShellExecute = true
            });
            _log("v2rayN restarted.");
        }
        catch (Exception ex)
        {
            _log($"Failed to restart v2rayN: {ex.Message}");
        }
    }
}
