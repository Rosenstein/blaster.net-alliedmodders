// SPDX-License-Identifier: GPL-3.0-or-later
// Blaster (C) Copyright 2014 AlliedModders LLC

namespace Blaster.Valve;

public static class ValveConstants
{
    public const int MaxPacketSize = 1400;

    // OOB request packet types
    public const byte A2S_INFO = 0x54;
    public const byte A2S_RULES = 0x56;

    // Official versions of the A2S_INFO reply
    public const byte S2A_INFO_GOLDSRC = 0x6d;
    public const byte S2A_INFO_SOURCE = 0x49;

    // Other OOB response packet types
    public const byte S2C_CHALLENGE = 0x41;
    public const byte S2A_PLAYER = 0x44;
    public const byte S2A_RULES = 0x45;

    // Steam's limit per query
    public const uint MaxServersPerQuery = 10000;

    // A host (IP) advertising more than this many servers is treated as a redirect/spam farm: its
    // servers are dropped and the host is excluded from further fan-out. <= 0 disables the spam filter.
    public const int DefaultMaxServersPerHost = 100;

    // A server reporting a GMS player count above this is treated as fake. No real Valve game is
    // realistically this populated: most cap at 64 (+ SourceTV/replay), TF2 ~100, GMod ~128. SDK mods
    // can raise the engine limit toward 255 but are never realistically this full.
    public const uint MaxRealisticPlayers = 130;

    // Upper bound on how many spam-host \gameaddr\ conditions are packed into one NOR when feeding
    // discovered farms back into the fan-out, to keep the filter string within the master server's
    // length limit. (A NOR over gameaddr needs >= 2 distinct IPs to be honoured at all.)
    public const int MaxSpamNorHosts = 24;

    // Retry policy for the rate-limited Steam GMS master-server queries. A timeout or transient
    // error is retried rather than silently dropping a whole fan-out bucket.
    public const int MasterQueryMaxAttempts = 3;
    public const int MasterQueryRetryBaseDelayMs = 1000;

    // Steam Web API documented limit: 200 requests / 5 minutes == one request per 1.5s. The Web API
    // transport throttles to this interval and, if it still receives a 429, backs off (honouring
    // Retry-After) up to WebApiMaxRateLimitWaits times before giving up on a query.
    public const int WebApiMinIntervalMs = 1500;
    public const int WebApiMaxRateLimitWaits = 5;
}
