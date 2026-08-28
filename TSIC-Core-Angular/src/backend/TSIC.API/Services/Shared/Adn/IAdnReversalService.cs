using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Shared.Adn;

/// <summary>
/// The ONE place that reverses a card charge at Authorize.Net.
///
/// <para>
/// Reversing a charge is not one operation but two, and picking the wrong one silently fails or
/// double-reverses: an unsettled charge must be VOIDED (full amount only), a settled one must be
/// REFUNDED (partial allowed). The choice is made from the gateway's own report of the original
/// transaction's status, never from anything the caller believes.
/// </para>
///
/// <para>
/// This service owns that decision, the amount validation, and the gateway wording of any failure.
/// It deliberately does NOT touch the database: where the reversal is booked — a registration
/// ledger row, a team's totals, a store cart batch — differs per caller and stays with the caller.
/// </para>
/// </summary>
public interface IAdnReversalService
{
    /// <summary>
    /// Void or refund a charge, whichever its settlement state requires. Never throws for a
    /// gateway refusal; inspect <see cref="AdnReversalResult.Success"/>.
    /// </summary>
    Task<AdnReversalResult> ReverseAsync(AdnReversalRequest request, CancellationToken ct = default);

    /// <summary>
    /// The standard note recorded against a voided charge. Shared so every ledger says the same
    /// thing, in the same words, about the same event — a void reads very differently from a
    /// refund to whoever finds it later, and the distinction must survive in the record.
    /// </summary>
    static string BuildVoidNote(decimal reversedAmount, string transactionId, string? reason)
    {
        var note = $"VOIDED {DateTime.Now:g} — CC was not yet settled at Authorize.Net, so the "
            + $"original ${reversedAmount:F2} charge was VOIDED (not refunded). "
            + $"ADN void tx {transactionId}.";

        return string.IsNullOrWhiteSpace(reason) ? note : $"{note} Reason: {reason}";
    }

    /// <summary>Append a note to an existing comment without losing what was already there.</summary>
    static string AppendNote(string? existingComment, string note) =>
        string.IsNullOrWhiteSpace(existingComment) ? note : $"{existingComment} | {note}";
}
