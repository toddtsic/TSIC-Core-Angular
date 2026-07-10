using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.MyRoster;

namespace TSIC.API.Services.Reporting;

public interface IMyRosterPdfService
{
    /// <summary>
    /// Renders one team's roster listing (exactly the rows the caller already sees on their
    /// roster cards) to a landscape Letter PDF: player identity + contact and the two family
    /// contacts, nothing else. Fed an already-gated, already-team-scoped list, so it performs
    /// no data access and no authorization of its own — the caller owns both.
    /// </summary>
    ReportExportResult Generate(IReadOnlyList<MyRosterPlayerDto> players, string teamName);
}
