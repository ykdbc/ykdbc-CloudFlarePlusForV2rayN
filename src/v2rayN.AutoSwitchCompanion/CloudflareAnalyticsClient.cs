using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace v2rayN.AutoSwitchCompanion;

public sealed class CloudflareAnalyticsClient
{
    private const string QueryWorkersByAccount = """
        query GetWorkersAnalytics($accountTag: string, $datetimeStart: string, $datetimeEnd: string) {
          viewer {
            accounts(filter: { accountTag: $accountTag }) {
              accountTag
              workersInvocationsAdaptive(limit: 10000, filter: {
                datetime_geq: $datetimeStart,
                datetime_lt: $datetimeEnd
              }) {
                sum { requests subrequests errors }
                dimensions { scriptName }
              }
            }
          }
        }
        """;

    private const string QueryWorkersOverviewByAccount = """
        query GetWorkersOverview($accountTag: string, $datetimeStart: string, $datetimeEnd: string) {
          viewer {
            accounts(filter: { accountTag: $accountTag }) {
              accountTag
              workersOverviewRequestsAdaptiveGroups(limit: 10000, filter: {
                datetime_geq: $datetimeStart,
                datetime_lt: $datetimeEnd
              }) {
                count
                dimensions { status usageModel }
              }
            }
          }
        }
        """;

    private const string QueryAllAccessibleAccounts = """
        query GetWorkersAnalytics($datetimeStart: string, $datetimeEnd: string) {
          viewer {
            accounts {
              accountTag
              workersInvocationsAdaptive(limit: 10000, filter: {
                datetime_geq: $datetimeStart,
                datetime_lt: $datetimeEnd
              }) {
                sum { requests subrequests errors }
                dimensions { scriptName }
              }
            }
          }
        }
        """;

    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri("https://api.cloudflare.com/client/v4/")
    };

    public async Task<WorkerUsage> GetTodayUsageAsync(CloudflareWorkerRule rule, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rule.ApiToken))
        {
            throw new InvalidOperationException("Cloudflare API token is empty.");
        }

        var utcStart = DateTimeOffset.UtcNow.Date;
        var utcEnd = DateTimeOffset.UtcNow;
        var variables = new
        {
            datetimeStart = utcStart.ToString("yyyy-MM-ddTHH:mm:ss.000Z"),
            datetimeEnd = utcEnd.ToString("yyyy-MM-ddTHH:mm:ss.000Z")
        };

        var accountTags = await ResolveAccountTagsAsync(rule, cancellationToken);
        if (accountTags.Count == 0)
        {
            return await QueryUsageAsync(rule, QueryAllAccessibleAccounts, variables, utcStart, utcEnd, cancellationToken);
        }

        WorkerUsage? mergedUsage = null;
        var errors = new List<string>();
        foreach (var accountTag in accountTags)
        {
            var scopedVariables = new
            {
                accountTag,
                variables.datetimeStart,
                variables.datetimeEnd
            };

            try
            {
                var usage = await QueryUsageAsync(rule, QueryWorkersByAccount, scopedVariables, utcStart, utcEnd, cancellationToken);
                if (usage.Requests == 0 && usage.WorkerNames.Count == 0)
                {
                    usage = await QueryOverviewUsageAsync(rule, scopedVariables, utcStart, utcEnd, cancellationToken);
                }

                mergedUsage = MergeUsage(mergedUsage, usage);
            }
            catch (InvalidOperationException ex)
            {
                errors.Add($"{accountTag}: {ex.Message}");
            }
        }

        if (mergedUsage is not null)
        {
            return mergedUsage;
        }

        throw new InvalidOperationException("Cloudflare usage query failed for all discovered accounts: " + string.Join("; ", errors));
    }

    private static async Task<WorkerUsage> QueryOverviewUsageAsync(
        CloudflareWorkerRule rule,
        object variables,
        DateTimeOffset utcStart,
        DateTimeOffset utcEnd,
        CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(new
        {
            query = QueryWorkersOverviewByAccount,
            variables
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "graphql");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rule.ApiToken.Trim());
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await Http.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Cloudflare API returned {(int)response.StatusCode}: {response.ReasonPhrase}");
        }

        using var doc = JsonDocument.Parse(content);
        if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
        {
            var message = errors[0].GetProperty("message").GetString() ?? "Cloudflare GraphQL error.";
            throw new InvalidOperationException(message);
        }

        var rows = doc.RootElement
            .GetProperty("data")
            .GetProperty("viewer")
            .GetProperty("accounts");
        if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Cloudflare token returned no accessible account analytics.");
        }

        long requests = 0;
        var accountTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var account in rows.EnumerateArray())
        {
            var accountTag = account.TryGetProperty("accountTag", out var accountTagElement)
                ? accountTagElement.GetString()
                : string.Empty;
            if (!string.IsNullOrWhiteSpace(accountTag))
            {
                accountTags.Add(accountTag);
            }

            var overviewRows = account.GetProperty("workersOverviewRequestsAdaptiveGroups");
            foreach (var row in overviewRows.EnumerateArray())
            {
                requests += row.GetProperty("count").GetInt64();
            }
        }

        return new WorkerUsage
        {
            RuleName = rule.DisplayName,
            Requests = requests,
            Subrequests = 0,
            Errors = 0,
            WorkerNames = ["<workers-overview>"],
            AccountTags = accountTags.Order(StringComparer.OrdinalIgnoreCase).ToList(),
            UtcStart = utcStart,
            UtcEnd = utcEnd
        };
    }

    private static async Task<List<string>> ResolveAccountTagsAsync(CloudflareWorkerRule rule, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(rule.AccountId))
        {
            return [rule.AccountId.Trim()];
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "zones?per_page=50");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rule.ApiToken.Trim());

        using var response = await Http.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        using var doc = JsonDocument.Parse(content);
        if (!doc.RootElement.TryGetProperty("result", out var zones) || zones.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var accountTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var zone in zones.EnumerateArray())
        {
            if (!zone.TryGetProperty("account", out var account)
                || !account.TryGetProperty("id", out var idElement))
            {
                continue;
            }

            var accountId = idElement.GetString();
            if (!string.IsNullOrWhiteSpace(accountId))
            {
                accountTags.Add(accountId);
            }
        }

        return accountTags.Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static async Task<WorkerUsage> QueryUsageAsync(
        CloudflareWorkerRule rule,
        string query,
        object variables,
        DateTimeOffset utcStart,
        DateTimeOffset utcEnd,
        CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(new { query, variables });

        using var request = new HttpRequestMessage(HttpMethod.Post, "graphql");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rule.ApiToken.Trim());
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await Http.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Cloudflare API returned {(int)response.StatusCode}: {response.ReasonPhrase}");
        }

        using var doc = JsonDocument.Parse(content);
        if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
        {
            var message = errors[0].GetProperty("message").GetString() ?? "Cloudflare GraphQL error.";
            if (message.Contains("not authorized", StringComparison.OrdinalIgnoreCase)
                || message.Contains("authorization denied", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Cloudflare token lacks Workers analytics read permission for this account.");
            }

            throw new InvalidOperationException(message);
        }

        var rows = doc.RootElement
            .GetProperty("data")
            .GetProperty("viewer")
            .GetProperty("accounts");
        if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Cloudflare token returned no accessible account analytics.");
        }

        long requests = 0;
        long subrequests = 0;
        long errorsCount = 0;
        var seenScripts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var accountTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var account in rows.EnumerateArray())
        {
            var accountTag = account.TryGetProperty("accountTag", out var accountTagElement)
                ? accountTagElement.GetString()
                : string.Empty;
            if (!string.IsNullOrWhiteSpace(accountTag))
            {
                accountTags.Add(accountTag);
            }

            var workerRows = account.GetProperty("workersInvocationsAdaptive");
            foreach (var row in workerRows.EnumerateArray())
            {
                var scriptName = row.GetProperty("dimensions").GetProperty("scriptName").GetString() ?? string.Empty;
                seenScripts.Add(string.IsNullOrWhiteSpace(scriptName) ? "<unknown>" : scriptName);
                var sum = row.GetProperty("sum");
                requests += sum.GetProperty("requests").GetInt64();
                subrequests += sum.GetProperty("subrequests").GetInt64();
                errorsCount += sum.GetProperty("errors").GetInt64();
            }
        }

        return new WorkerUsage
        {
            RuleName = rule.DisplayName,
            Requests = requests,
            Subrequests = subrequests,
            Errors = errorsCount,
            WorkerNames = seenScripts.Order(StringComparer.OrdinalIgnoreCase).ToList(),
            AccountTags = accountTags.Order(StringComparer.OrdinalIgnoreCase).ToList(),
            UtcStart = utcStart,
            UtcEnd = utcEnd
        };
    }

    private static WorkerUsage MergeUsage(WorkerUsage? current, WorkerUsage next)
    {
        if (current is null)
        {
            return next;
        }

        return new WorkerUsage
        {
            RuleName = current.RuleName,
            Requests = current.Requests + next.Requests,
            Subrequests = current.Subrequests + next.Subrequests,
            Errors = current.Errors + next.Errors,
            WorkerNames = current.WorkerNames.Concat(next.WorkerNames).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList(),
            AccountTags = current.AccountTags.Concat(next.AccountTags).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList(),
            UtcStart = current.UtcStart,
            UtcEnd = current.UtcEnd
        };
    }
}
