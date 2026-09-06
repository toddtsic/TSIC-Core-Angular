namespace TSIC.Contracts.Dtos.Usage;

/// <summary>
/// The client facet for the usage-analysis page: which app clients actually have rows
/// in the current scope, window, bots and event lens. The page offers a client only if
/// it is here -- a dropdown of clients with no data would let the stamp name a client
/// that contributed nothing.
///
/// Names are the log's own (logs.AppClients): tsic-web, tsic-teams, tsic-events,
/// unknown. "unknown" is real data -- the App Store Events build sends no client tag --
/// and stays offered when present.
/// </summary>
public record UsageClientsDto
{
    public required int WindowDays { get; init; }

    public required bool BotsExcluded { get; init; }

    /// <summary>Live events the facet covers -- the resolved scope after any event lens.</summary>
    public required int JobCount { get; init; }

    /// <summary>Clients with at least one row, busiest first.</summary>
    public required List<UsageClientFacetDto> Clients { get; init; }

    /// <summary>False when TSICLogs is not configured on this server -- a missing source, not zero traffic.</summary>
    public required bool UsageLoggingAvailable { get; init; }
}

public record UsageClientFacetDto
{
    /// <summary>logs.AppClients key; 0 = unknown.</summary>
    public required int AppClientId { get; init; }

    public required string AppClientName { get; init; }

    /// <summary>Rows from this client in the window, bots included or not per the query.</summary>
    public required int Requests { get; init; }
}
