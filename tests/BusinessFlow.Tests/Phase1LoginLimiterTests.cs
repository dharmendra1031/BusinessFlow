using BusinessFlow.Api.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class Phase1LoginLimiterTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task Shared_office_allows_distinct_accounts_but_one_account_is_limited_across_ips()
    {
        var limiter = new LoginAttemptLimiter(new ConfigurationBuilder().Build(), new Clock());
        for (var account = 0; account < 100; account++)
            Assert.True(await limiter.CheckAsync($"staff{account}@test.local", "office"));
        for (var attempt = 0; attempt < 5; attempt++)
            Assert.True(await limiter.CheckAsync(attempt % 2 == 0 ? " Person@Test.Local " : "person@test.local", "ip" + attempt));
        Assert.False(await limiter.CheckAsync("PERSON@TEST.LOCAL", "another-ip"));
    }

    [Fact]
    public async Task Concurrent_account_attempts_are_bounded_and_expire()
    {
        var clock = new Clock();
        var limiter = new LoginAttemptLimiter(new ConfigurationBuilder().Build(), clock);
        var results = await Task.WhenAll(Enumerable.Range(0, 40).Select(index => Task.Run(async () =>
            await limiter.CheckAsync("missing@test.local", "ip" + index))));
        Assert.Equal(5, results.Count(allowed => allowed));
        clock.Now = clock.Now.AddMinutes(1);
        Assert.True(await limiter.CheckAsync("missing@test.local", "new-ip"));
    }

    [Fact]
    public async Task Aggregate_ip_limit_and_key_capacity_cannot_be_evaded_by_new_accounts()
    {
        var clock = new Clock();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:LoginIpRequestsPerMinute"] = "3", ["RateLimiting:LoginMaxTrackedKeys"] = "8"
        }).Build();
        var limiter = new LoginAttemptLimiter(config, clock);
        for (var index = 0; index < 3; index++) Assert.True(await limiter.CheckAsync("a" + index, "office"));
        Assert.False(await limiter.CheckAsync("other", "office"));
        Assert.True(await limiter.CheckAsync("new-person", "new-ip"));
        Assert.True(await limiter.CheckAsync("last-person", "last-ip"));
        Assert.False(await limiter.CheckAsync("overflow", "overflow-ip"));
        clock.Now = clock.Now.AddMinutes(1);
        Assert.True(await limiter.CheckAsync("overflow", "overflow-ip"));
    }
}
