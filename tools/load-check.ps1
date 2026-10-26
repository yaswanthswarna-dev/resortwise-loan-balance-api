<#
.SYNOPSIS
    Local load check for SC-006: a sustained rate per tenant to both tenants at the same time
    (default 10 requests/second each, 20 in total, for 60 seconds). Prints the p95 latency and
    the error count only, never response bodies (research R14).

.DESCRIPTION
    Run it against a locally running API (quickstart step 5). The API keys are read from
    environment variables and never printed. Pick the loan IDs locally; do not commit them.

      $env:RW_LOAD_KEY_A = '<key for a caller allowed tenant A>'
      $env:RW_LOAD_KEY_B = '<key for a caller allowed tenant B>'
      ./tools/load-check.ps1 -LoanIdA <id> -LoanIdB <id>

    Every request is a read-only GET of the balance summary.
#>
param(
    [Parameter(Mandatory = $true)][int]$LoanIdA,
    [Parameter(Mandatory = $true)][int]$LoanIdB,
    [string]$BaseUrl = 'http://localhost:8080',
    [string]$TenantA = 'tenant-a',
    [string]$TenantB = 'tenant-b',
    [int]$RatePerTenant = 10,
    [int]$DurationSeconds = 60,
    [double]$TargetP95Ms = 1000
)

$ErrorActionPreference = 'Stop'

$keyA = $env:RW_LOAD_KEY_A
$keyB = $env:RW_LOAD_KEY_B
if ([string]::IsNullOrEmpty($keyA) -or [string]::IsNullOrEmpty($keyB)) {
    throw 'Set RW_LOAD_KEY_A and RW_LOAD_KEY_B before running the load check.'
}

# C# 5 syntax, so it also compiles in Windows PowerShell 5.1.
Add-Type -ReferencedAssemblies System.Net.Http -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

public sealed class LoadCheckTarget
{
    public string Url;
    public string Tenant;
    public string Key;
}

public sealed class LoadCheckResult
{
    public int Requests;
    public int Errors;
    public double P95Ms;
}

public static class LoadCheckRunner
{
    public static LoadCheckResult Run(LoadCheckTarget[] targets, int ratePerTarget, int durationSeconds)
    {
        using (var client = new HttpClient())
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            var tasks = new List<Task<double>>();
            var interval = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / ratePerTarget);
            var ticks = ratePerTarget * durationSeconds;
            var clock = Stopwatch.StartNew();

            for (var i = 0; i < ticks; i++)
            {
                var due = TimeSpan.FromTicks(interval.Ticks * i);
                var wait = due - clock.Elapsed;
                if (wait > TimeSpan.Zero)
                {
                    Thread.Sleep(wait);
                }

                foreach (var target in targets)
                {
                    tasks.Add(SendAsync(client, target));
                }
            }

            Task.WaitAll(tasks.ToArray());

            var latencies = new List<double>();
            var errors = 0;
            foreach (var task in tasks)
            {
                if (task.Result < 0)
                {
                    errors++;
                }
                else
                {
                    latencies.Add(task.Result);
                }
            }

            latencies.Sort();
            var result = new LoadCheckResult();
            result.Requests = tasks.Count;
            result.Errors = errors;
            result.P95Ms = latencies.Count == 0
                ? double.NaN
                : latencies[Math.Max(0, (int)Math.Ceiling(latencies.Count * 0.95) - 1)];
            return result;
        }
    }

    // Returns the latency in milliseconds, or -1 for a non-200 response or a failure.
    private static async Task<double> SendAsync(HttpClient client, LoadCheckTarget target)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, target.Url))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Key);
                request.Headers.TryAddWithoutValidation("X-Tenant-Id", target.Tenant);
                using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead).ConfigureAwait(false))
                {
                    watch.Stop();
                    return (int)response.StatusCode == 200 ? watch.Elapsed.TotalMilliseconds : -1;
                }
            }
        }
        catch (Exception)
        {
            return -1;
        }
    }
}
'@

function New-Target([string]$tenant, [string]$key, [int]$loanId) {
    $target = New-Object LoadCheckTarget
    $target.Url = '{0}/v1/loans/{1}/balance-summary' -f $BaseUrl.TrimEnd('/'), $loanId
    $target.Tenant = $tenant
    $target.Key = $key
    return $target
}

$targets = @((New-Target $TenantA $keyA $LoanIdA), (New-Target $TenantB $keyB $LoanIdB))

Write-Host ("Load check: {0} requests/second per tenant, 2 tenants, {1} seconds" -f $RatePerTenant, $DurationSeconds)
$result = [LoadCheckRunner]::Run($targets, $RatePerTenant, $DurationSeconds)

$met = ($result.Errors -eq 0) -and ($result.P95Ms -lt $TargetP95Ms)
Write-Host ("Requests: {0}" -f $result.Requests)
Write-Host ("Errors:   {0}" -f $result.Errors)
Write-Host ("p95:      {0:N0} ms" -f $result.P95Ms)
Write-Host ("SC-006 target (p95 < {0:N0} ms, no errors): {1}" -f $TargetP95Ms, $(if ($met) { 'met' } else { 'NOT met' }))
if (-not $met) { exit 1 }
