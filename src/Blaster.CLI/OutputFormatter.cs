// SPDX-License-Identifier: GPL-3.0-or-later
// Blaster (C) Copyright 2014 AlliedModders LLC

using System.Text;
using System.Text.Json;
using Blaster.Valve;

namespace Blaster.CLI;

/// <summary>
/// Formats query results as JSON in various output modes, streamed straight to the output with
/// <see cref="Utf8JsonWriter"/> so large result sets don't materialise an entire node tree and string.
/// </summary>
public class OutputFormatter
{
    private static readonly JsonWriterOptions WriterOptions = new() { Indented = true };

    /// <summary>
    /// Writes the results to <paramref name="output"/> in the given format ("list", "map", or "lines").
    /// </summary>
    public void Write(List<QueryResult> results, string format, Stream output)
    {
        switch (format.ToLowerInvariant())
        {
            case "list":
                WriteList(results, output);
                break;
            case "map":
                WriteMap(results, output);
                break;
            case "lines":
                WriteLines(results, output);
                break;
            default:
                throw new ArgumentException($"Unknown format: {format}");
        }
    }

    /// <summary>
    /// Convenience overload that returns the formatted output as a string (used by tests / small sets).
    /// </summary>
    public string Format(List<QueryResult> results, string format)
    {
        using var stream = new MemoryStream();
        Write(results, format, stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteList(List<QueryResult> results, Stream output)
    {
        using var writer = new Utf8JsonWriter(output, WriterOptions);
        writer.WriteStartArray();
        foreach (var result in results)
        {
            WriteResult(writer, result);
        }
        writer.WriteEndArray();
        writer.Flush();
    }

    private static void WriteMap(List<QueryResult> results, Stream output)
    {
        using var writer = new Utf8JsonWriter(output, WriterOptions);
        writer.WriteStartObject();
        for (var i = 0; i < results.Count; i++)
        {
            writer.WritePropertyName(results[i].Server ?? $"error_{i}");
            WriteResult(writer, results[i]);
        }
        writer.WriteEndObject();
        writer.Flush();
    }

    private static void WriteLines(List<QueryResult> results, Stream output)
    {
        // One compact JSON object per line (JSONL).
        using var writer = new Utf8JsonWriter(output);
        foreach (var result in results)
        {
            writer.Reset(output);
            WriteResult(writer, result);
            writer.Flush();
            output.WriteByte((byte)'\n');
        }
    }

    private static void WriteResult(Utf8JsonWriter writer, QueryResult result)
    {
        writer.WriteStartObject();

        if (result.Server != null)
            writer.WriteString("server", result.Server);

        if (result.AppId != 0)
            writer.WriteNumber("appid", result.AppId);

        if (result.Info != null)
        {
            writer.WritePropertyName("info");
            WriteServerInfo(writer, result.Info);
        }

        if (result.InfoError != null)
            writer.WriteString("info_error", result.InfoError);

        if (result.Rules is { Count: > 0 })
        {
            writer.WriteStartObject("rules");
            foreach (var (key, value) in result.Rules)
            {
                writer.WriteString(key, value);
            }
            writer.WriteEndObject();
        }

        if (result.RulesError != null)
            writer.WriteString("rules_error", result.RulesError);

        if (result.Error != null)
            writer.WriteString("error", result.Error);

        writer.WriteEndObject();
    }

    private static void WriteServerInfo(Utf8JsonWriter writer, ServerInfo info)
    {
        writer.WriteStartObject();

        writer.WriteNumber("protocol", info.Protocol);

        if (info.Name != null)
            writer.WriteString("name", info.Name);
        if (info.MapName != null)
            writer.WriteString("map", info.MapName);
        if (info.Folder != null)
            writer.WriteString("folder", info.Folder);
        if (info.Game != null)
            writer.WriteString("game", info.Game);

        writer.WriteNumber("appid", (int)(info.Ext?.AppId ?? AppId.Unknown));
        writer.WriteNumber("players", info.Players);
        writer.WriteNumber("max_players", info.MaxPlayers);
        writer.WriteNumber("bots", info.Bots);

        writer.WriteString("type", info.Type switch
        {
            ServerType.Dedicated => "d",
            ServerType.Listen => "l",
            ServerType.HLTV => "p",
            _ => "u",
        });

        writer.WriteString("os", info.OS switch
        {
            ServerOS.Linux => "l",
            ServerOS.Windows => "w",
            _ => "u",
        });

        writer.WriteNumber("visibility", info.Visibility);
        writer.WriteNumber("vac", info.Vac);

        if (info.Ext?.GameVersion != null)
            writer.WriteString("version", info.Ext.GameVersion);

        if (info.Mod != null)
        {
            writer.WriteStartObject("mod");
            writer.WriteString("url", info.Mod.Url);
            writer.WriteString("download_url", info.Mod.DwlUrl);
            writer.WriteNumber("version", info.Mod.Version);
            writer.WriteNumber("size", info.Mod.Size);
            writer.WriteNumber("type", info.Mod.Type);
            writer.WriteNumber("dll", info.Mod.Dll);
            writer.WriteEndObject();
        }

        if (info.TheShip != null)
        {
            writer.WriteStartObject("the_ship");
            writer.WriteNumber("mode", info.TheShip.Mode);
            writer.WriteNumber("witnesses", info.TheShip.Witnesses);
            writer.WriteNumber("duration", info.TheShip.Duration);
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }
}
