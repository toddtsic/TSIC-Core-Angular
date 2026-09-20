namespace TSIC.Contracts.Repositories;

/// <summary>
/// Ids of <c>invites.InviteKinds</c>. Seeded by <c>scripts/27-create-invites-schema.sql</c>;
/// these values are the contract between that script and this code.
/// </summary>
public enum InviteKind
{
    PlayerRegistration = 1,
    ClubRepRegistration = 2,
    SchedulePreview = 3
}

/// <summary>
/// Ids of <c>invites.InvitationOutcomes</c> — how the SEND went, nothing about what the
/// recipient later did. Seeded by the same script.
/// </summary>
public enum InvitationOutcome
{
    Sent = 1,
    Failed = 2,
    OptedOut = 3
}

/// <summary>
/// Write side of invitation tracking. Records WHO an invitation went to and how the send went —
/// and nothing else, ever.
///
/// Whether an invitation was taken up is NOT stored and is never written back here. It is inferred
/// live by the search query, from what currently exists in the target event (see
/// <c>IRegistrationRepository</c>'s invite filter). That is what keeps the two tables free of every
/// staleness case: a reused pending registration, a replaced registration, a club that drops a team,
/// a rep who unregisters. There is deliberately no "mark accepted" method on this interface, and
/// adding one would reintroduce the bug class the design exists to avoid.
/// </summary>
public interface IInvitationRepository
{
    /// <summary>
    /// Records one click of Send: a single <c>invites.Invitations</c> row plus one
    /// <c>invites.InvitationRegistrations</c> row per invited person. Returns the new InvitationId.
    ///
    /// A reminder is a fresh call, not an update — attempts are the row count, and each send's links
    /// keep their own expiry, so nothing here is ever revised except a later failure flip (see
    /// <see cref="MarkOutcomeAsync"/>).
    /// </summary>
    /// <param name="recipients">
    /// Source registration id + the outcome known at send time (OptedOut for suppressed recipients,
    /// otherwise Sent). Duplicate registration ids are collapsed — the composite key is
    /// (SourceRegistrationId, InvitationId), so a registration can appear only once per send.
    /// </param>
    Task<Guid> RecordSendAsync(
        Guid sourceJobId,
        Guid targetJobId,
        InviteKind kind,
        string subject,
        string bodyTemplate,
        DateTime expiresAt,
        string sentByUserId,
        IReadOnlyCollection<(Guid RegistrationId, InvitationOutcome Outcome)> recipients,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Flips the outcome of specific recipients of ONE send — used once, after the batch engine
    /// drains, to mark the recipients whose delivery failed. Rows not named are left alone.
    /// No-op when <paramref name="registrationIds"/> is empty.
    /// </summary>
    Task MarkOutcomeAsync(
        Guid invitationId,
        IReadOnlyCollection<Guid> registrationIds,
        InvitationOutcome outcome,
        CancellationToken cancellationToken = default);
}
