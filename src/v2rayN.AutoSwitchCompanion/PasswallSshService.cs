using Renci.SshNet;
using Renci.SshNet.Common;
using System.Text;

namespace v2rayN.AutoSwitchCompanion;

public sealed class PasswallSshService
{
    private readonly Action<string> _log;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public PasswallSshService(Action<string> log)
    {
        _log = log;
    }

    public async Task<IReadOnlyList<string>> ListGroupsAsync(PasswallSshSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.CanConnect)
        {
            return [];
        }

        var output = await RunCommandAsync(settings, PasswallCommands.ListGroups, cancellationToken);
        return output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task TestConnectionAsync(PasswallSshSettings settings, CancellationToken cancellationToken)
    {
        var output = await RunCommandAsync(settings, PasswallCommands.Probe, cancellationToken);
        var firstLine = output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "connected";
        _log($"Passwall SSH test OK: {firstLine}");
    }

    public async Task<string> TestGroupUrlTestAsync(
        PasswallSshSettings settings,
        string passwallGroup,
        CancellationToken cancellationToken)
    {
        if (!settings.CanConnect)
        {
            throw new InvalidOperationException("Passwall SSH is not configured.");
        }

        if (string.IsNullOrWhiteSpace(passwallGroup))
        {
            throw new InvalidOperationException("passwallgroup is empty.");
        }

        var best = await FindBestNodeAsync(settings, passwallGroup.Trim(), cancellationToken);
        if (best == null)
        {
            return $"Passwall group '{passwallGroup.Trim()}': no usable URL test result.";
        }

        return $"Passwall group '{passwallGroup.Trim()}': best '{best.Value.Remarks}' ({best.Value.NodeId}), delay={best.Value.Milliseconds:N0} ms.";
    }

    public async Task<bool> SwitchToBestNodeAsync(
        PasswallSshSettings settings,
        string passwallGroup,
        CancellationToken cancellationToken)
    {
        if (!settings.IsUsable)
        {
            _log("Passwall SSH is not configured; skipping Passwall sync.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(passwallGroup))
        {
            _log("No passwallgroup is configured for the selected rule; skipping Passwall sync.");
            return false;
        }

        var group = passwallGroup.Trim();
        var best = await FindBestNodeAsync(settings, group, cancellationToken);
        if (best == null)
        {
            _log($"Passwall group '{group}' has no usable URL test result.");
            return false;
        }

        var command = BuildSwitchCommand(settings, best.Value.NodeId);
        await RunCommandAsync(settings, command, cancellationToken);
        _log($"Passwall group '{group}' switched to '{best.Value.Remarks}' ({best.Value.NodeId}), delay={best.Value.Milliseconds:N0} ms.");
        return true;
    }

    private async Task<PasswallNodeTestResult?> FindBestNodeAsync(
        PasswallSshSettings settings,
        string group,
        CancellationToken cancellationToken)
    {
        var command = PasswallCommands.UrlTestGroup
            .Replace("__GROUP_BASE64__", ShellQuote(ToBase64(group)), StringComparison.Ordinal)
            .Replace("__URL_ARG__", ShellQuote(settings.UrlTestArgument), StringComparison.Ordinal);
        var output = await RunCommandAsync(settings, command, cancellationToken);
        var candidates = new List<PasswallNodeTestResult>();
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('\t', 3);
            if (parts.Length < 2
                || !decimal.TryParse(parts[0], System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var ms)
                || ms <= 0)
            {
                continue;
            }

            candidates.Add(new PasswallNodeTestResult(parts[1], parts.Length > 2 ? parts[2] : parts[1], ms));
        }

        return candidates
            .OrderBy(t => t.Milliseconds)
            .FirstOrDefault();
    }

    private async Task<string> RunCommandAsync(PasswallSshSettings settings, string command, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var client = new SshClient(CreateConnectionInfo(settings));
                client.Connect();
                try
                {
                    using var commandHandle = client.CreateCommand(command);
                    commandHandle.CommandTimeout = TimeSpan.FromSeconds(Math.Max(10, settings.CommandTimeoutSeconds));
                    var output = commandHandle.Execute();
                    if (commandHandle.ExitStatus != 0)
                    {
                        var error = string.IsNullOrWhiteSpace(commandHandle.Error)
                            ? output
                            : commandHandle.Error;
                        throw new InvalidOperationException($"Passwall SSH command failed ({commandHandle.ExitStatus}): {error.Trim()}");
                    }

                    return output ?? string.Empty;
                }
                finally
                {
                    client.Disconnect();
                }
            }, cancellationToken);
        }
        catch (SshAuthenticationException ex)
        {
            throw new InvalidOperationException("Passwall SSH authentication failed.", ex);
        }
        catch (SshConnectionException ex)
        {
            throw new InvalidOperationException($"Passwall SSH connection failed: {ex.Message}", ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static ConnectionInfo CreateConnectionInfo(PasswallSshSettings settings)
    {
        var methods = new List<AuthenticationMethod>();
        if (!string.IsNullOrWhiteSpace(settings.PrivateKeyPath))
        {
            methods.Add(new PrivateKeyAuthenticationMethod(settings.UserName.Trim(), new PrivateKeyFile(settings.PrivateKeyPath.Trim())));
        }

        if (!string.IsNullOrWhiteSpace(settings.Password))
        {
            methods.Add(new PasswordAuthenticationMethod(settings.UserName.Trim(), settings.Password));
        }

        if (methods.Count == 0)
        {
            throw new InvalidOperationException("Passwall SSH credentials are missing.");
        }

        return new ConnectionInfo(settings.Host.Trim(), settings.Port, settings.UserName.Trim(), methods.ToArray())
        {
            Timeout = TimeSpan.FromSeconds(Math.Max(10, settings.CommandTimeoutSeconds))
        };
    }

    private static string BuildSwitchCommand(PasswallSshSettings settings, string nodeId)
    {
        var quotedNode = ShellQuote(nodeId);
        var udpLine = settings.SwitchUdpWithTcp
            ? "\nuci set passwall.@global[0].udp_node=$node"
            : string.Empty;
        var restartLine = settings.RestartAfterSwitch
            ? "\n/etc/init.d/passwall restart >/dev/null 2>&1"
            : string.Empty;

        return $$"""
node={{quotedNode}}
uci set passwall.@global[0].tcp_node=$node
{{udpLine}}
uci commit passwall
{{restartLine}}
echo OK
""";
    }

    private static string ShellQuote(string value)
    {
        return "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
    }

    private static string ToBase64(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    }

    private readonly record struct PasswallNodeTestResult(string NodeId, string Remarks, decimal Milliseconds);

    private static class PasswallCommands
    {
        public const string Probe = "uname -n; cat /etc/openwrt_release 2>/dev/null | sed -n '1,3p'; test -x /usr/share/passwall/test.sh && echo passwall-ok";

        public const string ListGroups = """
lua - <<'LUA'
local uci=require('luci.model.uci').cursor()
local groups={}
uci:foreach('passwall','nodes',function(s)
  local group=s.group or 'default'
  groups[group]=true
end)
for group,_ in pairs(groups) do print(group) end
LUA
""";

        public const string UrlTestGroup = """
group=$(printf '%s' __GROUP_BASE64__ | base64 -d)
url_arg=__URL_ARG__
lua - "$group" <<'LUA' | while IFS="$(printf '\t')" read -r id remarks; do
local uci=require('luci.model.uci').cursor()
local target=arg[1]
uci:foreach('passwall','nodes',function(s)
  local group=s.group or 'default'
  if group == target and s['.name'] and s.protocol ~= '_shunt' then
    print((s['.name'] or '') .. '\t' .. (s.remarks or s['.name'] or ''))
  end
end)
LUA
  [ -n "$id" ] || continue
  result=$(/usr/share/passwall/test.sh url_test_node "$id" "$url_arg")
  code="${result%%:*}"
  seconds="${result#*:}"
  case "$code" in 200|204) ;; *) continue ;; esac
  ms=$(awk -v s="$seconds" 'BEGIN { if (s > 0) printf "%.2f", s * 1000 }')
  [ -n "$ms" ] || continue
  printf '%s\t%s\t%s\n' "$ms" "$id" "$remarks"
done
""";
    }
}
