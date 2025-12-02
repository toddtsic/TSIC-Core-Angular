using TSIC.API.Dtos;

namespace TSIC.API.Services;

/// <summary>
/// Validates player form submission data against job metadata schema.
/// Reusable for any registration type that uses PlayerProfileMetadataJson schema.
/// </summary>
public interface IPlayerFormValidationService
{
    /// <summary>
    /// Validates all players' form values against the job's metadata schema.
    /// Returns list of validation errors (empty if all valid).
    /// </summary>
    /// <param name="metadataJson">Job's PlayerProfileMetadataJson containing field schemas</param>
    /// <param name="selections">Team selections with FormValues for each player</param>
    /// <returns>List of validation errors</returns>
    List<PreSubmitValidationErrorDto> ValidatePlayerFormValues(string? metadataJson, List<PreSubmitTeamSelectionDto> selections);
}
