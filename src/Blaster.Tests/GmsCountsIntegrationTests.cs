// SPDX-License-Identifier: GPL-3.0-or-later
// Blaster (C) Copyright 2014 AlliedModders LLC

using Blaster.Valve;
using Xunit.Abstractions;

namespace Blaster.Tests;

/// <summary>
/// Regression guard for the GMS live-count source. The CM (GMS) server-list response carries the live
/// player count in <c>auth_players</c> (protobuf field 3); the <c>players</c> field (field 8) is always 0.
/// <see cref="GameServersHandler"/> must read <c>auth_players</c>. If it regresses to <c>players</c>, every
/// server reports 0 and this test fails. (The Web API exposes the same value under the name <c>players</c>.)
///
/// Integration-only: requires BLASTER_TEST_STEAM_USERNAME / _PASSWORD and a live Steam connection.
/// </summary>
public class GmsCountsIntegrationTests : IDisposable
{
    private readonly string _steamUsername;
    private readonly string _steamPassword;
    private readonly LoggingFixture _loggingFixture = new();
    private readonly ITestOutputHelper _output;

    public GmsCountsIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _steamUsername = GetRequiredEnvironmentVariable("BLASTER_TEST_STEAM_USERNAME");
        _steamPassword = GetRequiredEnvironmentVariable("BLASTER_TEST_STEAM_PASSWORD");
    }

    public void Dispose() => _loggingFixture.Dispose();

    private static string GetRequiredEnvironmentVariable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Environment variable '{name}' is required for this integration test.");
        return value;
    }

    [Trait("Category", "Integration")]
    [Fact]
    public async Task GmsReportsLivePlayerCounts_FromAuthPlayers()
    {
        var logger = _loggingFixture.CreateLogger<MasterServerQuerier>();
        var pool = new SteamConnectionPool(username: _steamUsername, password: _steamPassword, logger: logger);
        try
        {
            await pool.EnsureConnectedAsync();

            // Mix of less-faked games (TF2/GMod) and fake-heavy ones (CS/CSS) for contrast.
            (string Name, AppId Id)[] games =
            [
                ("TF2", AppId.TF2), ("GarrysMod", AppId.GarrysMod), ("CS", AppId.CS), ("CSS", AppId.CSS),
            ];

            var totalWithPlayers = 0;
            foreach (var (name, id) in games)
            {
                var recs = await pool.QueryWithFilterAsync((uint)id, MasterServerQuerier.BuildFilter((uint)id, [], []));
                if (recs.Count == 0) { _output.WriteLine($"{name,-10} ({(uint)id}): no servers returned"); continue; }
                var withPlayers = recs.Count(r => r.Players > 0);
                totalWithPlayers += withPlayers;
                _output.WriteLine($"{name,-10} ({(uint)id}): {recs.Count,6} servers | players>0 (auth_players): {withPlayers,5} (sum {recs.Sum(r => (long)r.Players)})");
                foreach (var r in recs.OrderByDescending(r => r.Players).Take(3))
                    _output.WriteLine($"    players={r.Players,3} bots={r.Bots,3} max={r.MaxPlayers,3}  {r.EndPoint}  '{r.Name}'");
            }

            // If the handler reads the wrong field (players, always 0) this is 0 across all games.
            Assert.True(totalWithPlayers > 0,
                "GMS reported 0 live players across all sampled games — handler is likely reading 'players' instead of 'auth_players'.");
        }
        finally
        {
            pool.Dispose();
        }
    }
}
