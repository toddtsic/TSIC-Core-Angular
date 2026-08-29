namespace TSIC.Contracts.Dtos.Reference;

/// <summary>
/// One row of reference.States, shaped for a &lt;select&gt;.
///
/// This is the ONE source of state/province options for every address form in the app.
/// Before it existed the frontend carried two divergent hardcoded arrays — one missing
/// Washington DC (420 accounts) and every territory, the other missing the Canadian
/// provinces — so a DC family could not complete a required State field at all.
/// Legacy served the same table via IRegistrationService.SelectListItems_States.
/// </summary>
public record StateOptionDto
{
    /// <summary>reference.States.StateID — the 2-char code that is stored on the account.</summary>
    public required string Value { get; init; }

    /// <summary>reference.States.State — the full display name.</summary>
    public required string Label { get; init; }
}
