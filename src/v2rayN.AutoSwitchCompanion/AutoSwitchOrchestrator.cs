namespace v2rayN.AutoSwitchCompanion;

public sealed class AutoSwitchOrchestrator
{
    private readonly CloudflareAnalyticsClient _cloudflare = new();
    private readonly V2rayNCompanionService _v2rayN;
    private readonly PasswallSshService _passwall;
    private readonly Action<string> _log;
    private readonly Action<CloudflareWorkerRule, WorkerUsage>? _usageUpdated;
    private readonly SemaphoreSlim _runLock = new(1, 1);

    public AutoSwitchOrchestrator(
        V2rayNCompanionService v2rayN,
        PasswallSshService passwall,
        Action<string> log,
        Action<CloudflareWorkerRule, WorkerUsage>? usageUpdated = null)
    {
        _v2rayN = v2rayN;
        _passwall = passwall;
        _log = log;
        _usageUpdated = usageUpdated;
    }

    public Task RunOnceAsync(CompanionSettings settings, CancellationToken cancellationToken)
    {
        return RunPeriodicCheckAsync(settings, cancellationToken);
    }

    public Task RunStartupSelectionAsync(CompanionSettings settings, CancellationToken cancellationToken)
    {
        return RunAsync(settings, cancellationToken, isStartupSelection: true);
    }

    public Task RunPeriodicCheckAsync(CompanionSettings settings, CancellationToken cancellationToken)
    {
        return RunAsync(settings, cancellationToken, isStartupSelection: false);
    }

    private async Task RunAsync(CompanionSettings settings, CancellationToken cancellationToken, bool isStartupSelection)
    {
        if (!await _runLock.WaitAsync(0, cancellationToken))
        {
            _log("A check is already running.");
            return;
        }

        try
        {
            await _v2rayN.InitializeAsync();
            await _v2rayN.EnsureSpeedTestUrlAsync(settings.SpeedTestUrl);

            var enabledRules = settings.Rules.Where(IsUsableRule).ToList();
            if (enabledRules.Count == 0)
            {
                _log("No enabled Cloudflare/group rules are configured.");
                return;
            }

            string currentGroup = string.Empty;
            try
            {
                currentGroup = await _v2rayN.GetCurrentGroupNameAsync();
                if (!string.IsNullOrWhiteSpace(currentGroup))
                {
                    _log($"Current active group: {currentGroup}");
                }
            }
            catch (Exception ex)
            {
                _log($"Current v2rayN group could not be detected: {ex.Message}");
            }

            var usageMap = new Dictionary<CloudflareWorkerRule, WorkerUsage>();
            foreach (var rule in enabledRules)
            {
                try
                {
                    var usage = await _cloudflare.GetTodayUsageAsync(rule, cancellationToken);
                    usageMap[rule] = usage;
                    _usageUpdated?.Invoke(rule, usage);
                    _log($"{rule.GroupName}: used {usage.Requests:N0}/{CompanionSettings.CloudflareFreeDailyLimit:N0}, remaining {usage.RemainingRequests:N0}.");
                }
                catch (Exception ex)
                {
                    _log($"{rule.GroupName}: Cloudflare usage query failed: {ex.Message}");
                }
            }

            if (!isStartupSelection)
            {
                await RunPeriodicDecisionAsync(currentGroup, enabledRules, usageMap, settings, cancellationToken);
                return;
            }

            var eligibleRules = GetEligibleRules(enabledRules, usageMap);
            if (eligibleRules.Count == 0)
            {
                _log($"No group is below its threshold and has more than {CompanionSettings.MinimumRemainingRequestsForSpeedTest:N0} remaining requests.");
                return;
            }

            var eligibleGroups = eligibleRules
                .Select(t => t.GroupName.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
            _log($"Startup initialization: testing all profiles in groups below threshold and with remaining > {CompanionSettings.MinimumRemainingRequestsForSpeedTest:N0}: {string.Join(", ", eligibleGroups)}");
            var selected = await _v2rayN.SwitchToBestProfileAcrossGroupsAsync(eligibleGroups, settings, cancellationToken);
            await SyncPasswallForSelectedProfileAsync(settings, eligibleRules, selected, cancellationToken);
        }
        finally
        {
            _runLock.Release();
        }
    }

    private async Task RunPeriodicDecisionAsync(
        string currentGroup,
        List<CloudflareWorkerRule> enabledRules,
        Dictionary<CloudflareWorkerRule, WorkerUsage> usageMap,
        CompanionSettings settings,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currentGroup))
        {
            _log("Current v2rayN group could not be detected. Periodic check will not switch.");
            return;
        }

        var currentRule = enabledRules.FirstOrDefault(t => string.Equals(t.GroupName, currentGroup, StringComparison.OrdinalIgnoreCase));
        if (currentRule == null)
        {
            _log("No Cloudflare rule matches the current v2rayN group. Periodic check will not switch.");
            return;
        }

        if (!usageMap.TryGetValue(currentRule, out var currentUsage))
        {
            _log("Cannot decide whether to switch because the current group's Cloudflare usage was not returned.");
            return;
        }

        if (currentUsage.Requests <= currentRule.ThresholdRequests)
        {
            _log($"Current group usage {currentUsage.Requests:N0} is not above threshold {currentRule.ThresholdRequests:N0}. No speed test or switch.");
            return;
        }

        var eligibleRules = GetEligibleRules(enabledRules, usageMap);
        if (eligibleRules.Count == 0)
        {
            _log($"Current group usage {currentUsage.Requests:N0} is above threshold {currentRule.ThresholdRequests:N0}, but no group is below its threshold and has more than {CompanionSettings.MinimumRemainingRequestsForSpeedTest:N0} remaining requests.");
            return;
        }

        var eligibleGroups = eligibleRules
            .Select(t => t.GroupName.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _log($"Current group usage {currentUsage.Requests:N0} is above threshold {currentRule.ThresholdRequests:N0}. Re-selecting from groups below threshold and with remaining > {CompanionSettings.MinimumRemainingRequestsForSpeedTest:N0}: {string.Join(", ", eligibleGroups)}");
        var selected = await _v2rayN.SwitchToBestProfileAcrossGroupsAsync(eligibleGroups, settings, cancellationToken);
        await SyncPasswallForSelectedProfileAsync(settings, eligibleRules, selected, cancellationToken);
    }

    private async Task SyncPasswallForSelectedProfileAsync(
        CompanionSettings settings,
        List<CloudflareWorkerRule> eligibleRules,
        CandidateProfile? selected,
        CancellationToken cancellationToken)
    {
        if (selected == null)
        {
            return;
        }

        if (!settings.PasswallSsh.Enabled)
        {
            return;
        }

        var selectedRule = eligibleRules.FirstOrDefault(t =>
            string.Equals(t.GroupName, selected.GroupName, StringComparison.OrdinalIgnoreCase));
        if (selectedRule == null)
        {
            _log($"Passwall sync skipped: no rule matched selected v2rayN group '{selected.GroupName}'.");
            return;
        }

        try
        {
            await _passwall.SwitchToBestNodeAsync(settings.PasswallSsh, selectedRule.PasswallGroup, cancellationToken);
        }
        catch (Exception ex)
        {
            _log($"Passwall sync failed: {ex.Message}");
        }
    }

    private static List<CloudflareWorkerRule> GetEligibleRules(
        List<CloudflareWorkerRule> enabledRules,
        Dictionary<CloudflareWorkerRule, WorkerUsage> usageMap)
    {
        return enabledRules
            .Where(t => usageMap.TryGetValue(t, out var usage)
                && usage.Requests <= t.ThresholdRequests
                && usage.RemainingRequests > CompanionSettings.MinimumRemainingRequestsForSpeedTest)
            .GroupBy(t => t.GroupName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(t => t.First())
            .OrderBy(t => t.GroupName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsUsableRule(CloudflareWorkerRule rule)
    {
        return rule.Enabled
            && !string.IsNullOrWhiteSpace(rule.GroupName)
            && !string.IsNullOrWhiteSpace(rule.ApiToken);
    }
}
