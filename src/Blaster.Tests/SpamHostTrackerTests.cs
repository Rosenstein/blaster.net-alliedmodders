// SPDX-License-Identifier: GPL-3.0-or-later
// Blaster (C) Copyright 2014 AlliedModders LLC

using System.Net;
using Blaster.Valve;
using Xunit;

namespace Blaster.Tests;

/// <summary>
/// Unit tests for <see cref="SpamHostTracker"/>: the port-farm threshold, the impossible-player-count
/// rule, and the bounded, ordered host list used for NOR fan-out feedback.
/// </summary>
public class SpamHostTrackerTests
{
    private static IPAddress Ip(string s) => IPAddress.Parse(s);

    [Fact]
    public void Disabled_KeepsEverything()
    {
        var t = new SpamHostTracker(maxServersPerHost: 0, maxRealisticPlayers: 130);

        Assert.False(t.Enabled);
        for (var i = 0; i < 5000; i++)
        {
            Assert.True(t.Observe(Ip("1.2.3.4"), players: 254));
        }
        Assert.Empty(t.SpamHosts);
    }

    [Fact]
    public void UnderThreshold_AllKept_HostNotSpam()
    {
        var t = new SpamHostTracker(100, 130);

        for (var i = 0; i < 100; i++)
        {
            Assert.True(t.Observe(Ip("5.5.5.5"), 10));
        }
        Assert.False(t.IsSpamHost(Ip("5.5.5.5")));
        Assert.Empty(t.SpamHosts);
    }

    [Fact]
    public void OverThreshold_MarksHostSpam_AndDropsTheRest()
    {
        var t = new SpamHostTracker(100, 130);

        for (var i = 0; i < 100; i++)
        {
            Assert.True(t.Observe(Ip("5.5.5.5"), 10));   // 1..100 kept
        }
        Assert.False(t.Observe(Ip("5.5.5.5"), 10));      // 101st crosses -> dropped, host flagged
        Assert.True(t.IsSpamHost(Ip("5.5.5.5")));
        Assert.False(t.Observe(Ip("5.5.5.5"), 10));      // everything after is dropped
    }

    [Fact]
    public void ImpossibleCount_DropsServer_ButSparesSmallHost()
    {
        var t = new SpamHostTracker(100, 130);

        Assert.True(t.Observe(Ip("9.9.9.9"), 10));    // legit neighbour kept
        Assert.False(t.Observe(Ip("9.9.9.9"), 200));  // impossible count -> this server dropped
        Assert.False(t.IsSpamHost(Ip("9.9.9.9")));    // but the shared host is not condemned
        Assert.True(t.Observe(Ip("9.9.9.9"), 10));    // another legit neighbour still kept
    }

    [Fact]
    public void ImpossibleCounts_StillCountTowardPortFarm()
    {
        var t = new SpamHostTracker(100, 130);

        for (var i = 0; i < 101; i++)
        {
            t.Observe(Ip("7.7.7.7"), 254); // a farm whose every port fakes an impossible count
        }
        Assert.True(t.IsSpamHost(Ip("7.7.7.7")));
    }

    [Fact]
    public void TopSpamHosts_EmptyWhenFewerThanTwo()
    {
        var t = new SpamHostTracker(maxServersPerHost: 1, maxRealisticPlayers: 130);

        t.Observe(Ip("1.1.1.1"), 0);
        t.Observe(Ip("1.1.1.1"), 0); // crosses threshold of 1 -> spam

        Assert.Single(t.SpamHosts);
        Assert.Empty(t.TopSpamHosts(10)); // a single-IP gameaddr NOR is not honoured, so none offered
    }

    [Fact]
    public void TopSpamHosts_FirstDetectedOrder_BoundedByMax()
    {
        var t = new SpamHostTracker(maxServersPerHost: 1, maxRealisticPlayers: 130);

        foreach (var ip in new[] { "1.1.1.1", "2.2.2.2", "3.3.3.3" })
        {
            t.Observe(Ip(ip), 0);
            t.Observe(Ip(ip), 0); // each crosses, flagged in this order
        }

        Assert.Equal([Ip("1.1.1.1"), Ip("2.2.2.2")], t.TopSpamHosts(2));
    }
}
