using TSIC.Contracts.Dtos;

namespace TSIC.Contracts.Services;

/// <summary>
/// QuickLinks resolution (public landing) + SuperUser editor operations.
/// </summary>
public interface IQuickLinksService
{
    /// <summary>
    /// Resolve a job's quicklinks for the public landing hero: the full active
    /// catalog (Option-A baseline) with per-job label/sort/enabled overrides applied.
    /// The client decides final visibility from the pulse + each row's Enabled.
    /// </summary>
    Task<List<QuickLinkResolvedDto>> ResolveForJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>Editor model for a chosen job: one row per active catalog LinkType.</summary>
    Task<QuickLinkEditorModelDto?> GetEditorModelAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>Batch upsert/delete the job's JobQuickLink override rows.</summary>
    Task SaveAsync(Guid jobId, SaveQuickLinksRequest request, string userId, CancellationToken ct = default);
}
