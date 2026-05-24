namespace v2rayN.AutoSwitchCompanion;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        if (TryRedirectToTaskbarExecutable(args))
        {
            return 0;
        }

        WindowsShellIdentity.Apply();

        if (!V2rayNHost.TryInitialize(out var error))
        {
            MessageBox.Show(error, "v2rayN Auto Switch Companion", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        if (args.Any(t => string.Equals(t, "--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            return await RunSelfTestAsync();
        }

        if (args.Any(t => string.Equals(t, "--cloudflare-test", StringComparison.OrdinalIgnoreCase)))
        {
            return await RunCloudflareTestAsync();
        }

        if (args.Any(t => string.Equals(t, "--simulate-switch", StringComparison.OrdinalIgnoreCase)))
        {
            return await RunSwitchSimulationAsync(args);
        }

        using var singleInstance = new Mutex(true, @"Local\v2rayN.AutoSwitchCompanion", out var createdNew);
        if (!createdNew)
        {
            return 0;
        }

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            Application.Run(new AutoSwitchForm());
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, WindowsShellIdentity.ProductTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 2;
        }
    }

    private static bool TryRedirectToTaskbarExecutable(string[] args)
    {
        if (args.Length > 0)
        {
            return false;
        }

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath)
            || !string.Equals(Path.GetFileName(processPath), "v2rayN.AutoSwitchCompanion.exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var taskbarPath = Path.Combine(Path.GetDirectoryName(processPath) ?? AppContext.BaseDirectory, "CloudFlarePlusForV2rayN.exe");
        if (!File.Exists(taskbarPath))
        {
            return false;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = taskbarPath,
            WorkingDirectory = Path.GetDirectoryName(taskbarPath) ?? AppContext.BaseDirectory,
            UseShellExecute = true
        });
        return true;
    }

    private static async Task<int> RunSelfTestAsync()
    {
        var logPath = Path.Combine(V2rayNHost.HostDirectory, "autoswitch-companion.selftest.log");
        var lines = new List<string>
        {
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Self-test started.",
            $"Host directory: {V2rayNHost.HostDirectory}"
        };

        try
        {
            var service = new V2rayNCompanionService(message => lines.Add(message));
            await service.InitializeAsync();
            await service.EnsureSpeedTestUrlAsync(CompanionSettings.DefaultSpeedTestUrl);
            var selection = await service.GetCurrentSelectionAsync();
            lines.Add($"Current group: {(string.IsNullOrWhiteSpace(selection.GroupName) ? "<none>" : selection.GroupName)}");
            lines.Add($"Current profile: {(string.IsNullOrWhiteSpace(selection.ProfileName) ? "<none>" : selection.ProfileName)}, delay={selection.DelayDisplay}, speed={selection.SpeedDisplay}");
            lines.Add("Self-test completed successfully.");
            await File.WriteAllLinesAsync(logPath, lines);
            return 0;
        }
        catch (Exception ex)
        {
            lines.Add(ex.ToString());
            await File.WriteAllLinesAsync(logPath, lines);
            return 2;
        }
    }

    private static async Task<int> RunCloudflareTestAsync()
    {
        var logPath = Path.Combine(V2rayNHost.HostDirectory, "autoswitch-companion.cloudflare-test.log");
        var lines = new List<string>
        {
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Cloudflare test started.",
            $"Settings: {SettingsStore.SettingsPath}"
        };

        try
        {
            var settings = SettingsStore.Load();
            var rules = settings.Rules
                .Where(t => t.Enabled
                    && !string.IsNullOrWhiteSpace(t.GroupName)
                    && !string.IsNullOrWhiteSpace(t.ApiToken))
                .ToList();

            if (rules.Count == 0)
            {
                lines.Add("No usable Cloudflare rules found.");
                await File.WriteAllLinesAsync(logPath, lines);
                return 3;
            }

            var client = new CloudflareAnalyticsClient();
            var hasFailure = false;
            var verifiedTokenChanged = false;
            foreach (var rule in rules)
            {
                try
                {
                    var usage = await client.GetTodayUsageAsync(rule, CancellationToken.None);
                    verifiedTokenChanged |= rule.MarkApiTokenVerified();
                    lines.Add($"{rule.GroupName}: used {usage.Requests:N0}/{CompanionSettings.CloudflareFreeDailyLimit:N0}, remaining {usage.RemainingRequests:N0}, configured threshold {rule.ThresholdRequests:N0}, subrequests={usage.Subrequests:N0}, errors={usage.Errors:N0}");
                }
                catch (Exception ex)
                {
                    hasFailure = true;
                    lines.Add($"{rule.GroupName}: Cloudflare usage query failed: {ex.Message}");
                }
            }

            if (verifiedTokenChanged)
            {
                SettingsStore.Save(settings);
            }

            lines.Add(hasFailure
                ? "Cloudflare test completed with failures."
                : "Cloudflare test completed successfully.");
            await File.WriteAllLinesAsync(logPath, lines);
            return hasFailure ? 4 : 0;
        }
        catch (Exception ex)
        {
            lines.Add(ex.ToString());
            await File.WriteAllLinesAsync(logPath, lines);
            return 2;
        }
    }

    private static async Task<int> RunSwitchSimulationAsync(string[] args)
    {
        var logPath = Path.Combine(V2rayNHost.HostDirectory, "autoswitch-companion.switch-simulation.log");
        var lines = new List<string>
        {
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Switch simulation started.",
            $"Host directory: {V2rayNHost.HostDirectory}"
        };

        var configPath = Path.Combine(V2rayNHost.HostDirectory, "guiConfigs", "guiNConfig.json");
        var backupPath = $"{configPath}.autoswitch-sim-{DateTime.Now:yyyyMMddHHmmss}.bak";

        try
        {
            var settings = SettingsStore.Load();
            var service = new V2rayNCompanionService(message => lines.Add(message));
            await service.InitializeAsync();

            var before = await service.GetCurrentSelectionAsync();
            lines.Add($"Before: group='{before.GroupName}' profile='{before.ProfileName}' groupId={before.GroupId} profileId={before.ProfileId}");

            var targetGroup = GetArgumentValue(args, "--target-group")
                ?? settings.Rules
                    .Where(t => t.Enabled && !string.IsNullOrWhiteSpace(t.GroupName))
                    .Select(t => t.GroupName)
                    .FirstOrDefault(t => !string.Equals(t, before.GroupName, StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(targetGroup))
            {
                lines.Add("No target group found for simulation.");
                await File.WriteAllLinesAsync(logPath, lines);
                return 3;
            }

            File.Copy(configPath, backupPath, overwrite: true);
            lines.Add($"Backup: {backupPath}");
            lines.Add($"Target group: {targetGroup}");

            var simulated = await service.SwitchToGroupWithoutSpeedTestAsync(targetGroup);
            if (simulated == null)
            {
                lines.Add("Simulation failed before writing target selection.");
                await File.WriteAllLinesAsync(logPath, lines);
                return 4;
            }

            var after = await service.GetCurrentSelectionAsync();
            lines.Add($"After switch write: group='{after.GroupName}' profile='{after.ProfileName}' groupId={after.GroupId} profileId={after.ProfileId}");

            var switched = string.Equals(after.GroupName, targetGroup, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(after.ProfileId, before.ProfileId, StringComparison.OrdinalIgnoreCase);
            lines.Add($"Switch write verified: {switched}");

            var restored = await service.RestoreSelectionAsync(before);
            var restoredSelection = await service.GetCurrentSelectionAsync();
            lines.Add($"Restored via ServiceLib: {restored}");
            lines.Add($"After restore: group='{restoredSelection.GroupName}' profile='{restoredSelection.ProfileName}' groupId={restoredSelection.GroupId} profileId={restoredSelection.ProfileId}");

            if (!string.Equals(restoredSelection.ProfileId, before.ProfileId, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(backupPath, configPath, overwrite: true);
                lines.Add("Restore fallback: copied backup config back.");
            }

            lines.Add("Switch simulation completed.");
            await File.WriteAllLinesAsync(logPath, lines);
            return switched && restored ? 0 : 5;
        }
        catch (Exception ex)
        {
            lines.Add(ex.ToString());
            try
            {
                if (File.Exists(backupPath))
                {
                    File.Copy(backupPath, configPath, overwrite: true);
                    lines.Add("Exception fallback: copied backup config back.");
                }
            }
            catch (Exception restoreEx)
            {
                lines.Add($"Exception fallback restore failed: {restoreEx}");
            }

            await File.WriteAllLinesAsync(logPath, lines);
            return 2;
        }
    }

    private static string? GetArgumentValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
