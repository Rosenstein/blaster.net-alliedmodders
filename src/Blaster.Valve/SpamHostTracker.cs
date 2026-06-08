// SPDX-License-Identifier: GPL-3.0-or-later
// Blaster (C) Copyright 2014 AlliedModders LLC

using System.Net;

namespace Blaster.Valve;

/// <summary>
/// Detects redirect/spam "farm" hosts during a master-server run and decides which servers to drop.
/// Two signals, both read from the (harder-to-spoof) GMS response, so no A2S query is needed:
/// <list type="bullet">
///   <item><b>Port farm:</b> an IP advertising more than <see cref="MaxServersPerHost"/> servers is one
///     operator stuffing the list with thousands of ports; the whole host is marked spam.</item>
///   <item><b>Impossible player count:</b> a single server reporting more than
///     <see cref="MaxRealisticPlayers"/> players is fake and dropped on its own — but its host is only
///     marked spam if it also crosses the port-farm threshold, so a lone faker on a shared hosting IP
///     doesn't take its legitimate neighbours down with it.</item>
/// </list>
/// Marked hosts are exposed via <see cref="SpamHosts"/> (for the consumer to prune any servers emitted
/// before the host crossed the threshold) and <see cref="TopSpamHosts"/> (for NOR fan-out feedback).
/// First-detected order ≈ largest-first, since the biggest farms dominate the earliest queries.
/// </summary>
internal sealed class SpamHostTracker
{
    private readonly int _maxServersPerHost;
    private readonly uint _maxRealisticPlayers;
    private readonly Dictionary<IPAddress, int> _counts = [];
    private readonly HashSet<IPAddress> _spam = [];
    private readonly List<IPAddress> _spamOrder = [];

    public SpamHostTracker(int maxServersPerHost, uint maxRealisticPlayers)
    {
        _maxServersPerHost = maxServersPerHost;
        _maxRealisticPlayers = maxRealisticPlayers;
    }

    /// <summary>The spam filter is disabled when the per-host cap is non-positive.</summary>
    public bool Enabled => _maxServersPerHost > 0;

    public IReadOnlyCollection<IPAddress> SpamHosts => _spam;

    public bool IsSpamHost(IPAddress host) => _spam.Contains(host);

    /// <summary>
    /// Records one server seen on <paramref name="host"/> and returns whether it should be kept
    /// (emitted). Returns false for a server that is itself fake (impossible count) or sits on a host
    /// that has been marked a spam farm. Every server counts toward the host's running total — including
    /// impossible-count ones — so a farm of inflated-count servers still trips the port-farm threshold.
    /// </summary>
    public bool Observe(IPAddress host, uint players)
    {
        if (!Enabled)
        {
            return true;
        }

        if (_spam.Contains(host))
        {
            return false;
        }

        var count = _counts.TryGetValue(host, out var c) ? c + 1 : 1;
        _counts[host] = count;

        if (count > _maxServersPerHost)
        {
            MarkSpam(host);
            return false;
        }

        // Real games are never this full; drop the individual server but leave the host alone unless it
        // also trips the port-farm threshold above.
        return players <= _maxRealisticPlayers;
    }

    private void MarkSpam(IPAddress host)
    {
        if (_spam.Add(host))
        {
            _spamOrder.Add(host);
        }
        _counts.Remove(host);
    }

    /// <summary>
    /// Up to <paramref name="max"/> spam hosts (largest-first) for a NOR fan-out exclusion, or an empty
    /// list when fewer than two are known — a NOR over a single <c>\gameaddr\</c> is not honoured by the
    /// master server and returns nothing.
    /// </summary>
    public IReadOnlyList<IPAddress> TopSpamHosts(int max) =>
        _spamOrder.Count < 2 ? [] : _spamOrder.GetRange(0, Math.Min(max, _spamOrder.Count));
}
