// SPDX-License-Identifier: GPL-3.0-or-later
// Blaster (C) Copyright 2014 AlliedModders LLC

using Blaster.Valve;
using System.Net.Sockets;
using Xunit.Abstractions;

namespace Blaster.Tests;

/// <summary>
/// Diagnostic: does the Steam GMS transport honour a NOR over <c>\gameaddr\</c>? This gates the design of
/// the dynamic spam filter — if NOR-gameaddr reliably excludes hosts, farm IPs can be fed back into the
/// fan-out filters; if not, exclusion must happen client-side after the fan-out.
///
/// Web API probing (docs/webapi_nor_probe.py) showed gameaddr-in-NOR is finicky there: a lone
/// \nor\1\gameaddr\X returns 0, while \nor\1 over other selectors works. This test measures the GMS
/// transport directly. It prints a matrix rather than asserting a single outcome; run with
/// <c>--logger "console;verbosity=detailed"</c> to see the numbers.
///
/// Integration-only: requires BLASTER_TEST_STEAM_USERNAME / _PASSWORD and a live Steam connection.
/// </summary>
public class MasterServerNorFilterTests : IDisposable
{
    private const uint CsAppId = (uint)AppId.CS; // 10
    private readonly string _steamUsername;
    private readonly string _steamPassword;
    private readonly LoggingFixture _loggingFixture = new();
    private readonly ITestOutputHelper _output;

    public MasterServerNorFilterTests(ITestOutputHelper output)
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
    public async Task NorGameAddr_BehaviorMatrix_OnSteamTransport()
    {
        var logger = _loggingFixture.CreateLogger<MasterServerQuerier>();
        var pool = new SteamConnectionPool(username: _steamUsername, password: _steamPassword, logger: logger);
        try
        {
            await pool.EnsureConnectedAsync();

            async Task<(int total, int onHost)> Run(string label, string[] nor, string[] and, params string[] hostsToCount)
            {
                var res = await pool.QueryWithFilterAsync(CsAppId, MasterServerQuerier.BuildFilter(CsAppId, nor, and));
                var hosts = hostsToCount.ToHashSet();
                var onHost = res.Count(r => hosts.Contains(r.EndPoint.Address.ToString()));
                _output.WriteLine($"{label,-34} -> total {res.Count,6}, on excluded host(s) {onHost,5}");
                return (res.Count, onHost);
            }

            // Discover the two busiest hosts currently in the CS list.
            var broad = await pool.QueryWithFilterAsync(CsAppId, MasterServerQuerier.BuildFilter(CsAppId, [], []));
            var busy = broad
                .Where(r => r.EndPoint.Address.AddressFamily == AddressFamily.InterNetwork)
                .GroupBy(r => r.EndPoint.Address.ToString())
                .Select(g => (Ip: g.Key, Count: g.Count()))
                .OrderByDescending(x => x.Count)
                .Take(2).ToList();
            Assert.True(busy.Count >= 2, "Need at least two distinct hosts to test multi-IP NOR.");
            var (a, b) = (busy[0].Ip, busy[1].Ip);
            _output.WriteLine($"Broad query: {broad.Count} servers. Busiest hosts: {a} ({busy[0].Count}), {b} ({busy[1].Count})\n");

            _output.WriteLine("--- sanity: single-element NOR on a non-gameaddr selector ---");
            await Run("nor1 gametype", ["\\gametype\\valve"], []);

            _output.WriteLine("\n--- the question: NOR over gameaddr ---");
            await Run("nor1 gameaddr A",            [$"\\gameaddr\\{a}"], [], a);
            await Run("nor2 gameaddr A,B (distinct)", [$"\\gameaddr\\{a}", $"\\gameaddr\\{b}"], [], a, b);
            await Run("nor2 gameaddr A,A (dup)",     [$"\\gameaddr\\{a}", $"\\gameaddr\\{a}"], [], a);

            _output.WriteLine("\n--- repeat the lone-gameaddr case 3x (consistency / flakiness) ---");
            for (var i = 0; i < 3; i++)
                await Run($"  nor1 gameaddr A run {i + 1}", [$"\\gameaddr\\{a}"], [], a);

            // Baseline so we can interpret: how many does the host actually have?
            _output.WriteLine("");
            await Run("baseline gameaddr A (positive)", [], [$"\\gameaddr\\{a}"], a);

            // Does the GMS itself report the impossible 255 / over-max counts for a farm host?
            _output.WriteLine("\n--- GMS-reported counts for busy host A (settles the '255' question) ---");
            var hostRecords = await pool.QueryWithFilterAsync(CsAppId, MasterServerQuerier.BuildFilter(CsAppId, [], [$"\\gameaddr\\{a}"]));
            var playersDist = hostRecords.GroupBy(r => r.Players).OrderByDescending(g => g.Count())
                .Select(g => $"players={g.Key}×{g.Count()}").Take(6);
            var maxDist = hostRecords.GroupBy(r => r.MaxPlayers).OrderByDescending(g => g.Count())
                .Select(g => $"max={g.Key}×{g.Count()}").Take(6);
            var over = hostRecords.Count(r => r.Players > r.MaxPlayers);
            var is255 = hostRecords.Count(r => r.Players == 255);
            var overCeiling = hostRecords.Count(r => r.Players > 32);
            _output.WriteLine($"  GMS players dist: {string.Join(", ", playersDist)}");
            _output.WriteLine($"  GMS max dist:     {string.Join(", ", maxDist)}");
            _output.WriteLine($"  players>max: {over}  players==255: {is255}  players>32: {overCeiling}  (of {hostRecords.Count})");

            // Sanity on the GMS-authoritative change itself: are GMS counts broadly populated, or mostly 0?
            var broadNonZero = broad.Count(r => r.Players > 0);
            var broadMaxNonZero = broad.Count(r => r.MaxPlayers > 0);
            _output.WriteLine($"\n--- broad sample GMS counts: {broadNonZero}/{broad.Count} have players>0, {broadMaxNonZero}/{broad.Count} have max>0 ---");

            // Definitive: \empty\1 returns NON-empty servers (they have players). If GMS still reports
            // players=0 for these, the GMS list response simply doesn't carry live player counts.
            var nonEmpty = await pool.QueryWithFilterAsync(CsAppId, MasterServerQuerier.BuildFilter(CsAppId, [], ["\\empty\\1"]));
            var nonEmptyWithPlayers = nonEmpty.Count(r => r.Players > 0);
            var nonEmptyBots = nonEmpty.Count(r => r.Bots > 0);
            _output.WriteLine($"--- \\empty\\1 (non-empty servers): {nonEmpty.Count} returned; {nonEmptyWithPlayers} report GMS players>0; {nonEmptyBots} report GMS bots>0 ---");
            foreach (var r in nonEmpty.Take(5))
                _output.WriteLine($"    {r.EndPoint}  players={r.Players} bots={r.Bots} max={r.MaxPlayers}  '{r.Name}'");

            Assert.True(broad.Count > 0); // keep the test green; the matrix above is the deliverable
        }
        finally
        {
            pool.Dispose();
        }
    }
}
