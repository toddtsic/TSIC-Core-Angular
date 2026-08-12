namespace TSIC.Domain.Constants;

public static class TeamConstants
{
    /// <summary>
    /// Anchor team for store walk-up registrations, living under the "Dropped Teams"
    /// graveyard agegroup. A walk-up buyer isn't on a real team, but Registrations
    /// requires a team to hang the purchase on — this inactive, invisible team is that
    /// hook. Resolved by name (TeamRepository.GetStoreMerchTeamIdAsync); minted at job
    /// clone (JobCloneResetRules.CreateStoreMerchTeam). Legacy used the identical name,
    /// so the shared DB's semantics agree across stacks.
    /// </summary>
    public const string StoreMerch = "Store Merch";
}
