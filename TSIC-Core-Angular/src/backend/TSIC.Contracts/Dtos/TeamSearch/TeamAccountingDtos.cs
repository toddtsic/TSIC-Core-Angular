using TSIC.Contracts.Dtos.RegistrationSearch;

namespace TSIC.Contracts.Dtos.TeamSearch;

/// <summary>
/// Team detail with financials and accounting records.
/// Displayed in the team detail panel.
/// </summary>
public record TeamSearchDetailDto
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
    public string? ClubName { get; init; }
    public required string AgegroupName { get; init; }
    public string? DivName { get; init; }
    public string? LevelOfPlay { get; init; }
    public required bool Active { get; init; }

    // Financials
    public required decimal FeeBase { get; init; }
    public required decimal FeeProcessing { get; init; }
    public required decimal FeeTotal { get; init; }
    public required decimal PaidTotal { get; init; }
    public required decimal OwedTotal { get; init; }
    public string? TeamComments { get; init; }

    // Club rep info
    public Guid? ClubRepRegistrationId { get; init; }
    public string? ClubRepName { get; init; }
    public string? ClubRepEmail { get; init; }
    public string? ClubRepCellphone { get; init; }

    // Accounting records for this team
    public required List<AccountingRecordDto> AccountingRecords { get; init; }

    // Club-level summary (for scope selector: all club teams)
    public required List<ClubTeamSummaryDto> ClubTeamSummaries { get; init; }
}

/// <summary>
/// Summary of a single team within the club (for club-wide scope selector).
/// </summary>
public record ClubTeamSummaryDto
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
    public required string AgegroupName { get; init; }
    public required decimal FeeTotal { get; init; }
    public required decimal PaidTotal { get; init; }
    public required decimal OwedTotal { get; init; }
    public required bool Active { get; init; }
}

/// <summary>
/// Request to edit team properties.
/// </summary>
public record EditTeamRequest
{
    public string? TeamName { get; init; }
    public bool? Active { get; init; }
    public string? LevelOfPlay { get; init; }
    public string? TeamComments { get; init; }
}

/// <summary>
/// CC charge request — can target a single team or all club teams.
/// When TeamId is null, charges all active club teams with OwedTotal > 0.
/// </summary>
public record TeamCcChargeRequest
{
    public Guid? TeamId { get; init; }
    public required Guid ClubRepRegistrationId { get; init; }
    public required CreditCardInfo CreditCard { get; init; }
}

/// <summary>
/// CC charge response with per-team breakdown.
/// </summary>
public record TeamCcChargeResponse
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public List<TeamPaymentAllocation>? PerTeamResults { get; init; }
}

/// <summary>
/// Check or correction payment request — can target a single team or all club teams.
/// When TeamId is null, distributes across all active club teams.
/// </summary>
public record TeamCheckOrCorrectionRequest
{
    public Guid? TeamId { get; init; }
    public required Guid ClubRepRegistrationId { get; init; }
    public required decimal Amount { get; init; }
    public string? CheckNo { get; init; }
    public string? Comment { get; init; }
    public required string PaymentType { get; init; } // "Check" or "Correction"
}

/// <summary>
/// Check or correction response with per-team allocation breakdown.
/// </summary>
public record TeamCheckOrCorrectionResponse
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public List<TeamPaymentAllocation>? PerTeamAllocations { get; init; }
}

/// <summary>
/// Per-team allocation result — shown in UI for transparency.
/// </summary>
public record TeamPaymentAllocation
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
    public required decimal AllocatedAmount { get; init; }
    public required decimal ProcessingFeeReduction { get; init; }
    public required decimal NewOwedTotal { get; init; }
}
