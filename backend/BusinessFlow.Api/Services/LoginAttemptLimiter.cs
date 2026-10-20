namespace BusinessFlow.Api.Services;

public sealed class LoginAttemptLimiter
{
    private sealed class Window(DateTimeOffset expires)
    {
        public DateTimeOffset Expires { get; } = expires;
        public int Attempts { get; set; }
    }

    private readonly object gate = new();
    private readonly Dictionary<string, Window> windows = new(StringComparer.Ordinal);
    private readonly TimeProvider clock;
    private readonly int accountLimit;
    private readonly int ipLimit;
    private readonly int capacity;
    private DateTimeOffset nextCleanup;

    public LoginAttemptLimiter(IConfiguration configuration, TimeProvider clock)
    {
        this.clock = clock;
        accountLimit = configuration.GetValue<int?>("RateLimiting:LoginAccountRequestsPerMinute")
            ?? configuration.GetValue<int?>("RateLimiting:LoginRequestsPerMinute") ?? 5;
        ipLimit = configuration.GetValue<int?>("RateLimiting:LoginIpRequestsPerMinute") ?? 300;
        capacity = configuration.GetValue<int?>("RateLimiting:LoginMaxTrackedKeys") ?? 10000;
        if (accountLimit is < 1 or > 50 || ipLimit is < 1 or > 10000 || capacity is < 2 or > 100000)
            throw new InvalidOperationException("Configure valid account/IP login limits and tracked-key capacity.");
    }

    public Task<bool> CheckAsync(string account, string remoteIp, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (account.Length > 256 || remoteIp.Length > 128) return Task.FromResult(false);
        var accountKey = "account:" + account.Trim().ToUpperInvariant();
        var ipKey = "ip:" + remoteIp;
        lock (gate)
        {
            var now = clock.GetUtcNow();
            if (now >= nextCleanup)
            {
                foreach (var key in windows.Where(entry => entry.Value.Expires <= now).Select(entry => entry.Key).ToArray())
                    windows.Remove(key);
                nextCleanup = now.AddSeconds(10);
            }
            if (windows.TryGetValue(accountKey, out var accountWindow) && accountWindow.Expires <= now)
                windows.Remove(accountKey);
            if (windows.TryGetValue(ipKey, out var ipWindow) && ipWindow.Expires <= now)
                windows.Remove(ipKey);
            var needed = (windows.ContainsKey(accountKey) ? 0 : 1) + (windows.ContainsKey(ipKey) ? 0 : 1);
            if (windows.Count + needed > capacity) return Task.FromResult(false);
            if (!windows.TryGetValue(ipKey, out ipWindow)) windows[ipKey] = ipWindow = new Window(now.AddMinutes(1));
            if (++ipWindow.Attempts > ipLimit) return Task.FromResult(false);
            if (!windows.TryGetValue(accountKey, out accountWindow)) windows[accountKey] = accountWindow = new Window(now.AddMinutes(1));
            return Task.FromResult(++accountWindow.Attempts <= accountLimit);
        }
    }
}
