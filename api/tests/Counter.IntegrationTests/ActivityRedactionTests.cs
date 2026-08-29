namespace Counter.IntegrationTests;

using Counter.Api.Services;
using Counter.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// No credential this process holds ever reaches a stored record.
/// </summary>
/// <remarks>
/// The realistic version of this failure is not an attack. It is somebody with a
/// password on the clipboard, a search box focused, and a habit of pressing
/// paste — and from there the password is in the audit table, in a backup of it,
/// and in front of whoever reviews the trail.
///
/// The audit write is the last place it can be caught, because it is the only
/// place in this application that stores what a person typed.
/// </remarks>
/// <param name="fixture">The started services.</param>
[Collection(CounterCollection.Name)]
public sealed class ActivityRedactionTests(CounterFixture fixture)
{
    private readonly CounterFixture _fixture = fixture;

    [Fact]
    public async Task A_password_typed_into_the_search_box_is_not_stored()
    {
        string secret = ErpPasswordInUse();

        using HttpClient browser = _fixture.Application.CreateClient();

        // Searched for, not sent to the ERP as a credential. This is a person
        // pasting into the wrong window, which is the case that actually happens.
        await browser.GetAsync($"/api/v1/parts?q={Uri.EscapeDataString(secret)}");

        IReadOnlyList<string> targets = await ReadTargetsAsync();

        Assert.NotEmpty(targets);
        Assert.DoesNotContain(targets, target => target.Contains(secret, StringComparison.Ordinal));
        Assert.Contains(targets, target => target.Contains(SecretRedactor.Marker, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_password_embedded_in_a_longer_search_is_still_removed()
    {
        // A partial match is the case a naive equality check misses, and it is
        // the more likely one: a paste lands beside whatever was already typed.
        string secret = ErpPasswordInUse();
        string typed = $"breaker {secret} 100a";

        using HttpClient browser = _fixture.Application.CreateClient();
        await browser.GetAsync($"/api/v1/parts?q={Uri.EscapeDataString(typed)}");

        IReadOnlyList<string> targets = await ReadTargetsAsync();

        Assert.DoesNotContain(targets, target => target.Contains(secret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task No_stored_row_anywhere_carries_a_configured_secret()
    {
        // The sweep. The two tests above cover the path anyone thought of; this
        // one covers every row the whole suite produced, including from paths
        // nobody thought about.
        IConfiguration configuration =
            _fixture.Application.Services.GetRequiredService<IConfiguration>();

        string[] secrets =
        [
            configuration["Erp:DatabasePassword"] ?? string.Empty,
            configuration.GetConnectionString("Counter") ?? string.Empty,
        ];

        IDbContextFactory<CounterContext> contexts = _fixture.Application.Services
            .GetRequiredService<IDbContextFactory<CounterContext>>();

        await using CounterContext context = await contexts.CreateDbContextAsync();

        List<ActivityRow> rows = await context.Activity.ToListAsync();

        Assert.NotEmpty(rows);

        foreach (ActivityRow row in rows)
        {
            foreach (string secret in secrets.Where(value => value.Length >= 4))
            {
                Assert.DoesNotContain(secret, row.TargetKey, StringComparison.Ordinal);
                Assert.DoesNotContain(secret, row.DatabaseLogin, StringComparison.Ordinal);
                Assert.DoesNotContain(secret, row.DisplayName, StringComparison.Ordinal);
                Assert.DoesNotContain(secret, row.Action, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void The_redactor_knows_about_at_least_one_secret()
    {
        // Without this, every assertion above would pass against a redactor that
        // had been configured with nothing and did nothing.
        SecretRedactor redactor = _fixture.Application.Services.GetRequiredService<SecretRedactor>();

        Assert.True(
            redactor.HasSecrets,
            "The redactor holds no secrets, so the tests above prove nothing.");
    }

    /// <summary>
    /// The ERP password this instance is configured with.
    /// </summary>
    /// <remarks>
    /// Read back from the running application's own configuration rather than
    /// written down here, so the test cannot drift into checking for a password
    /// nothing uses — which would pass forever while the real one leaked.
    /// </remarks>
    private string ErpPasswordInUse()
    {
        IConfiguration configuration =
            _fixture.Application.Services.GetRequiredService<IConfiguration>();

        return configuration["Erp:DatabasePassword"]
            ?? throw new InvalidOperationException(
                "No ERP password is configured, so this test would prove nothing.");
    }

    /// <summary>Read every stored target, waiting for writes still in flight.</summary>
    private async Task<IReadOnlyList<string>> ReadTargetsAsync()
    {
        IDbContextFactory<CounterContext> contexts = _fixture.Application.Services
            .GetRequiredService<IDbContextFactory<CounterContext>>();

        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (true)
        {
            await using CounterContext context = await contexts.CreateDbContextAsync();

            List<string> targets = await context.Activity
                .Where(row => row.TargetKey != string.Empty)
                .Select(row => row.TargetKey)
                .ToListAsync();

            if (targets.Count > 0 || DateTime.UtcNow > deadline)
            {
                return targets;
            }

            await Task.Delay(100);
        }
    }
}
