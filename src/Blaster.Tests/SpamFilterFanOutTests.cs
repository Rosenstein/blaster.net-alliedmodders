// SPDX-License-Identifier: GPL-3.0-or-later
// Blaster (C) Copyright 2014 AlliedModders LLC

using System.Net;
using Blaster.Valve;
using Xunit;

namespace Blaster.Tests;

/// <summary>
/// End-to-end spam-filter behaviour driven through <see cref="MasterServerQuerier"/> against the
/// in-memory <see cref="SimulatedMasterServer"/>: farm hosts are flagged and excluded, impossible
/// player counts are dropped without condemning a shared host, and the filter can be disabled.
/// </summary>
public class SpamFilterFanOutTests
{
    private static async Task<(List<IPEndPoint> Delivered, MasterServerQuerier Querier)> RunAsync(
        IEnumerable<SimServer> population, int maxServersPerHost = ValveConstants.DefaultMaxServersPerHost)
    {
        var querier = new MasterServerQuerier(new SimulatedMasterServer(population)) { MaxServersPerHost = maxServersPerHost };
        querier.FilterAppIds((AppId)10);

        var delivered = new List<IPEndPoint>();
        await querier.QueryAsync(servers =>
        {
            delivered.AddRange(servers.Select(s => s.EndPoint));
            return Task.CompletedTask;
        });
        return (delivered, querier);
    }

    [Fact]
    public async Task FlagsFarmHost_LimitsStragglers_AndConsumerPruneCleansUp()
    {
        var population = new List<SimServer>();
        for (var p = 0; p < 150; p++) // one host, 150 ports -> over the default 100 cap
        {
            population.Add(SimServer.Empty(10, "5.5.5.5", 20000 + p, "FARM.RO", "de_dust2"));
        }
        for (var i = 0; i < 50; i++) // 50 legitimate hosts, one server each
        {
            population.Add(SimServer.Community(10, $"1.2.{i / 256}.{i % 256}", 27015, $"Real {i}", "de_dust2"));
        }

        var (delivered, querier) = await RunAsync(population);
        var farm = IPAddress.Parse("5.5.5.5");

        Assert.Contains(farm, querier.SpamHosts);
        // At most the threshold leaks before the host trips mid-stream; never all 150.
        Assert.True(delivered.Count(e => e.Address.Equals(farm)) <= querier.MaxServersPerHost);
        // The consumer-side prune (drop anything on a SpamHost) yields the clean set: 50 legit, no farm.
        var clean = delivered.Where(e => !querier.SpamHosts.Contains(e.Address)).ToList();
        Assert.Equal(50, clean.Count);
        Assert.DoesNotContain(clean, e => e.Address.Equals(farm));
    }

    [Fact]
    public async Task Disabled_DeliversFarmHostIntact()
    {
        var population = new List<SimServer>();
        for (var p = 0; p < 150; p++)
        {
            population.Add(SimServer.Empty(10, "5.5.5.5", 20000 + p, "FARM.RO", "de_dust2"));
        }

        var (delivered, querier) = await RunAsync(population, maxServersPerHost: 0);

        Assert.Equal(150, delivered.Count);
        Assert.Empty(querier.SpamHosts);
    }

    [Fact]
    public async Task NorFeedback_ExcludesFlaggedFarmsFromLaterQueries()
    {
        var population = new List<SimServer>();
        // Two farms, discovered early (front of the tier-1 sample), each over the cap.
        for (var p = 0; p < 200; p++)
        {
            population.Add(SimServer.Empty(10, "5.5.5.5", 20000 + p, "FARM.RO", "de_dust2"));
            population.Add(SimServer.Empty(10, "6.6.6.6", 20000 + p, "FARM.RO", "de_dust2"));
        }
        // Enough varied-map servers to blow past the cap and force map-tier fan-out (later queries).
        for (var i = 0; i < 11000; i++)
        {
            population.Add(SimServer.Empty(10, $"10.{i / 65536 % 256}.{i / 256 % 256}.{i % 256}", 27015 + i % 1000, $"S{i}", $"map_{i % 40}"));
        }

        var sim = new SimulatedMasterServer(population);
        var querier = new MasterServerQuerier(sim) { MaxServersPerHost = 100 };
        querier.FilterAppIds((AppId)10);
        await querier.QueryAsync(_ => Task.CompletedTask);

        Assert.Contains(IPAddress.Parse("5.5.5.5"), querier.SpamHosts);
        Assert.Contains(IPAddress.Parse("6.6.6.6"), querier.SpamHosts);
        // Once two farms are known, later queries must NOR both out (a NOR needs >= 2 distinct IPs).
        Assert.Contains(sim.Filters, f => f.Contains("\\gameaddr\\5.5.5.5") && f.Contains("\\gameaddr\\6.6.6.6"));
        // The tier-1 query, issued before any farm was known, must not carry the feedback NOR.
        Assert.DoesNotContain("\\gameaddr\\", sim.Filters[0]);
    }

    [Fact]
    public async Task FakeIpServers_AreExemptFromTheFilter_EvenWhenSharingAnAddress()
    {
        var population = new List<SimServer>();
        // SDR / fake-IP servers legitimately share one 169.254 address, well over the port-farm cap.
        for (var p = 0; p < 150; p++)
        {
            population.Add(SimServer.Empty(10, "169.254.1.1", 20000 + p, "SDR Server", "de_dust2"));
        }
        population.Add(SimServer.Community(10, "1.2.3.4", 27015, "Real", "de_dust2"));

        var querier = new MasterServerQuerier(new SimulatedMasterServer(population)) { MaxServersPerHost = 100 };
        querier.IncludeFakeIp = true;
        querier.FilterAppIds((AppId)10);

        var delivered = new List<IPEndPoint>();
        await querier.QueryAsync(servers =>
        {
            delivered.AddRange(servers.Select(s => s.EndPoint));
            return Task.CompletedTask;
        });

        // The shared fake-IP host is never flagged as a farm...
        Assert.Empty(querier.SpamHosts);
        // ...all of its servers are collected for QueryByFakeIP, none culled...
        Assert.Equal(150, querier.FakeIpServers.Count);
        Assert.All(querier.FakeIpServers, s => Assert.Equal("169.254.1.1", s.EndPoint.Address.ToString()));
        // ...and the ordinary server still comes through the normal A2S path.
        Assert.Contains(delivered, e => e.Address.ToString() == "1.2.3.4");
    }

    [Fact]
    public async Task DropsImpossiblePlayerCount_WithoutCondemningSharedHost()
    {
        var population = new List<SimServer>
        {
            SimServer.Community(10, "9.9.9.9", 27015, "Legit A", "de_dust2"),                  // 5 players
            new(10, "9.9.9.9", 27016, "Faker", "de_dust2", Linux: true, Players: 200, MaxPlayers: 32, Tags: ["cs"]),
            SimServer.Community(10, "9.9.9.9", 27017, "Legit B", "de_dust2"),                  // 5 players
        };

        var (delivered, querier) = await RunAsync(population);

        Assert.Equal(2, delivered.Count);                          // both legitimate neighbours kept
        Assert.DoesNotContain(delivered, e => e.Port == 27016);    // only the impossible-count server dropped
        Assert.Empty(querier.SpamHosts);                           // the shared host is not condemned
    }
}
