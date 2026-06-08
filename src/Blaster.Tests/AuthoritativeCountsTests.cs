// SPDX-License-Identifier: GPL-3.0-or-later
// Blaster (C) Copyright 2014 AlliedModders LLC

using System.Net;
using Blaster.Valve;
using Xunit;

namespace Blaster.Tests;

/// <summary>
/// Verifies that the GMS-reported player/bot counts override the (more easily spoofable) counts a server returns in
/// its own A2S_INFO reply.
/// </summary>
public class AuthoritativeCountsTests
{
    private static readonly IPEndPoint EndPoint = new(IPAddress.Parse("1.2.3.4"), 27015);

    [Fact]
    public void MasterServerEntry_OverwritesA2SPlayerBotAndMaxPlayerCounts()
    {
        // A server inflating its own counts over A2S_INFO.
        var info = new ServerInfo { Players = 64, Bots = 32, MaxPlayers = 100 };
        var entry = new MasterServerEntry(EndPoint, Players: 3, Bots: 1, MaxPlayers: 24);

        entry.ApplyAuthoritativeCounts(info);

        Assert.Equal(3, info.Players);
        Assert.Equal(1, info.Bots);
        Assert.Equal(24, info.MaxPlayers);
    }

    [Fact]
    public void FakeIpServer_OverwritesPlayerBotAndMaxPlayerCounts()
    {
        var info = new ServerInfo { Players = 99, Bots = 50, MaxPlayers = 100 };
        var fake = new FakeIpServer(EndPoint, AppId: 440, Players: 7, Bots: 2, MaxPlayers: 16);

        fake.ApplyAuthoritativeCounts(info);

        Assert.Equal(7, info.Players);
        Assert.Equal(2, info.Bots);
        Assert.Equal(16, info.MaxPlayers);
    }

    [Fact]
    public void Apply_ClampsGmsCountsToByteRange()
    {
        // GMS counts are 32-bit; A2S carries them as single bytes. A pathological/spoofed GMS value
        // must not overflow the byte field.
        var info = new ServerInfo { Players = 10, Bots = 5, MaxPlayers = 64 };
        var entry = new MasterServerEntry(EndPoint, Players: 1000, Bots: 300, MaxPlayers: 9000);

        entry.ApplyAuthoritativeCounts(info);

        Assert.Equal(byte.MaxValue, info.Players);
        Assert.Equal(byte.MaxValue, info.Bots);
        Assert.Equal(byte.MaxValue, info.MaxPlayers);
    }
}
