// SPDX-License-Identifier: GPL-3.0-or-later
// Blaster (C) Copyright 2014 AlliedModders LLC

using Microsoft.Extensions.Logging;

namespace Blaster.Valve;

/// <summary>
/// Thread-safe progress reporter for long, otherwise-silent batches (e.g. the A2S / fake-IP query
/// passes). Logs <c>Queried N/Total ...</c> every <see cref="_logEvery"/> completions, at least once
/// per <see cref="_minInterval"/>, and once more on the final item — so the tool no longer appears to
/// hang while it works through thousands of servers.
/// </summary>
public sealed class ProgressLogger
{
    private readonly ILogger? _logger;
    private readonly int _total;
    private readonly string _noun;
    private readonly int _logEvery;
    private readonly long _minIntervalTicks;
    private int _done;
    private long _lastLogTicks;

    public ProgressLogger(
        ILogger? logger,
        int total,
        string noun = "servers",
        int logEvery = 500,
        TimeSpan? minInterval = null)
    {
        _logger = logger;
        _total = total;
        _noun = noun;
        _logEvery = Math.Max(1, logEvery);
        _minIntervalTicks = (minInterval ?? TimeSpan.FromSeconds(5)).Ticks;
        _lastLogTicks = DateTime.UtcNow.Ticks;
    }

    /// <summary>Records one completed item and logs progress when a threshold is crossed.</summary>
    public void Increment()
    {
        var done = Interlocked.Increment(ref _done);
        if (_logger is null || !_logger.IsEnabled(LogLevel.Information))
            return;

        var shouldLog = done == _total || done % _logEvery == 0;
        if (!shouldLog)
        {
            var now = DateTime.UtcNow.Ticks;
            var last = Interlocked.Read(ref _lastLogTicks);
            shouldLog = now - last >= _minIntervalTicks
                && Interlocked.CompareExchange(ref _lastLogTicks, now, last) == last;
        }

        if (!shouldLog)
            return;

        Interlocked.Exchange(ref _lastLogTicks, DateTime.UtcNow.Ticks);
        _logger.LogInformation("Queried {Done}/{Total} {Noun}", done, _total, _noun);
    }
}
