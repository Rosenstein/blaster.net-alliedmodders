// SPDX-License-Identifier: GPL-3.0-or-later
// Blaster (C) Copyright 2014 AlliedModders LLC

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Blaster.CLI;
using Blaster.Valve;
using Xunit;

namespace Blaster.Tests;

/// <summary>
/// Verifies the streamed JSON output for each format mode is well-formed and carries the expected fields.
/// </summary>
public class OutputFormatterTests
{
    private static List<QueryResult> Sample() =>
    [
        new QueryResult
        {
            Server = "1.2.3.4:27015",
            AppId = 10,
            Info = new ServerInfo
            {
                Name = "Alpha",
                MapName = "de_dust2",
                Folder = "cstrike",
                Players = 5,
                MaxPlayers = 32,
                Bots = 1,
                Type = ServerType.Dedicated,
                OS = ServerOS.Linux,
                Ext = new ExtendedInfo { AppId = (AppId)10, GameVersion = "1.0" },
            },
        },
        new QueryResult
        {
            Server = "5.6.7.8:27016",
            AppId = 10,
            InfoError = "timeout",
            Rules = new Dictionary<string, string> { ["sv_gravity"] = "800", ["mp_timelimit"] = "30" },
        },
    ];

    [Fact]
    public void List_ProducesJsonArrayWithExpectedFields()
    {
        var json = new OutputFormatter().Format(Sample(), "list");
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(2, doc.RootElement.GetArrayLength());

        var first = doc.RootElement[0];
        Assert.Equal("1.2.3.4:27015", first.GetProperty("server").GetString());
        Assert.Equal(10, first.GetProperty("appid").GetInt32());

        var info = first.GetProperty("info");
        Assert.Equal("de_dust2", info.GetProperty("map").GetString());
        Assert.Equal("d", info.GetProperty("type").GetString());
        Assert.Equal("l", info.GetProperty("os").GetString());
        Assert.Equal(5, info.GetProperty("players").GetInt32());

        Assert.Equal("timeout", doc.RootElement[1].GetProperty("info_error").GetString());
    }

    [Fact]
    public void Rules_AreEmittedAsObjectWhenPopulated()
    {
        var json = new OutputFormatter().Format(Sample(), "list");
        using var doc = JsonDocument.Parse(json);

        // First server has no rules -> no "rules" property.
        Assert.False(doc.RootElement[0].TryGetProperty("rules", out _));

        // Second server carries rules -> emitted as a key/value object.
        var rules = doc.RootElement[1].GetProperty("rules");
        Assert.Equal(JsonValueKind.Object, rules.ValueKind);
        Assert.Equal("800", rules.GetProperty("sv_gravity").GetString());
        Assert.Equal("30", rules.GetProperty("mp_timelimit").GetString());
    }

    [Fact]
    public void Map_KeysByServerAddress()
    {
        var json = new OutputFormatter().Format(Sample(), "map");
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        Assert.True(doc.RootElement.TryGetProperty("1.2.3.4:27015", out var entry));
        Assert.Equal(10, entry.GetProperty("appid").GetInt32());
    }

    [Fact]
    public void Lines_IsOneCompactJsonObjectPerLine()
    {
        var json = new OutputFormatter().Format(Sample(), "lines");
        var lines = json.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);
        foreach (var line in lines)
        {
            Assert.DoesNotContain('\n', line); // compact, single line
            using var doc = JsonDocument.Parse(line);
            Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        }
    }

    [Fact]
    public void Write_ToStream_MatchesFormatString()
    {
        var formatter = new OutputFormatter();
        var asString = formatter.Format(Sample(), "list");

        using var stream = new MemoryStream();
        formatter.Write(Sample(), "list", stream);

        Assert.Equal(asString, Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public void UnknownFormat_Throws()
    {
        Assert.Throws<ArgumentException>(() => new OutputFormatter().Format(Sample(), "bogus"));
    }
}
