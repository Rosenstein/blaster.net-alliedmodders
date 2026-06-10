// SPDX-License-Identifier: GPL-3.0-or-later
// Blaster (C) Copyright 2014 AlliedModders LLC

using Blaster.CLI;
using Blaster.Valve;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Blaster.Tests;

/// <summary>
/// Regression guard for the CLI worker's rules pass. <see cref="CliServerQuerier"/> plumbs a
/// <c>skipRules</c> flag end-to-end, but for the entire .NET port the worker never actually issued the
/// A2S_RULES query — so <c>QueryResult.Rules</c> was always null and <c>--no-rules</c> made no difference.
/// These tests run the real master fan-out + A2S pass and assert rules are populated by default, and
/// absent when skipped. If the <c>QueryRules</c> call is dropped again, the first test fails.
///
/// Integration-only: requires BLASTER_TEST_STEAM_USERNAME / _PASSWORD and a live Steam connection.
/// Heavy: queries a full appid's worth of servers (same cost as the master-server integration tests).
/// </summary>
public class CliRulesIntegrationTests
{
    private const int AppId = (int)Blaster.Valve.AppId.CSS;
    private readonly string _steamUsername;
    private readonly string _steamPassword;
    private readonly ITestOutputHelper _output;

    public CliRulesIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _steamUsername = GetRequiredEnvironmentVariable("BLASTER_TEST_STEAM_USERNAME");
        _steamPassword = GetRequiredEnvironmentVariable("BLASTER_TEST_STEAM_PASSWORD");
    }

    private CliServerQuerier CreateQuerier() => new(
        maxConcurrency: 50,
        transport: MasterServerTransport.Steam,
        steamUsername: _steamUsername,
        steamPassword: _steamPassword,
        webApiKey: null,
        includeFakeIp: false,
        loggerFactory: NullLoggerFactory.Instance);

    [Trait("Category", "Integration")]
    [Fact]
    public async Task QueryServers_ByDefault_PopulatesRules()
    {
        var results = await CreateQuerier().QueryServersAsync([AppId]);

        // Plenty of servers don't answer A2S_RULES, so don't require every result — just that the rules
        // pass ran and succeeded somewhere. Before the fix this was zero across the board.
        var withRules = results.Count(r => r.Rules is { Count: > 0 });
        _output.WriteLine($"CSS: {results.Count} results, {withRules} with rules populated.");

        Assert.True(withRules > 0,
            "No server returned rules — the CLI worker is not issuing A2S_RULES (regression of the rules pass).");
    }

    [Trait("Category", "Integration")]
    [Fact]
    public async Task QueryServers_WithSkipRules_OmitsRules()
    {
        var results = await CreateQuerier().QueryServersAsync([AppId], skipRules: true);

        Assert.All(results, r => Assert.Null(r.Rules));
    }

    private static string GetRequiredEnvironmentVariable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Environment variable '{name}' is required for this integration test.");
        return value;
    }
}
