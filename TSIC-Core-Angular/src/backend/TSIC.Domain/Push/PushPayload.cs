namespace TSIC.Domain.Push;

/// <summary>
/// The closed set of push shapes this server can send. One variant per kind of push, each
/// owning the keys only it uses -- there is no all-purpose bag of strings, and no caller can
/// invent a new key without adding a variant here.
///
/// WHY A UNION AND NOT A DICTIONARY (Todd, 2026-09-21): the data map is a wire contract the
/// mobile clients fork on. When every sender hands FCM a free-form dictionary, the only thing
/// keeping a payload readable is the discipline of whoever wrote the sender, and the client
/// has no way to know what keys a push carries until it inspects them. A union makes the shape
/// a compile-time fact: <see cref="Type"/> is derived from the variant rather than typed in by
/// hand, so a payload cannot claim to be one thing and carry another's fields.
///
/// <see cref="Type"/> is the discriminant the client switches on. Clients ignore values they
/// do not recognise, so a new variant is safe to ship ahead of the app that reads it.
///
/// ISOLATION: this is the NEW push path. The live senders -- TeamManagementService,
/// PushNotificationService and GameResultPushService -- still go through
/// IFirebasePushService.SendToDevicesAsync with their own dictionaries and are deliberately
/// untouched. Nothing here changes what today's TSIC-Events or TSIC-Teams installs receive.
/// Migrating them onto this union is a later, separate decision.
/// </summary>
public abstract record PushPayload
{
    /// <summary>
    /// The discriminant, written to the FCM data map as "type". Derived from the variant --
    /// never settable, so it cannot drift from the payload it labels.
    /// </summary>
    public abstract string Type { get; }

    /// <summary>
    /// Collapse key. FCM keeps only the most recent undelivered message per key per device,
    /// which is what turns twenty messages in a minute into one banner. Null means no
    /// collapsing -- every message is delivered separately.
    /// </summary>
    public abstract string? CollapseKey { get; }

    /// <summary>Android notification channel. Null falls back to the app's default channel.</summary>
    public virtual string? AndroidChannelId => null;

    /// <summary>
    /// How long FCM should keep trying before dropping the push. Null uses the FCM default
    /// (4 weeks), which is wrong for anything time-sensitive.
    /// </summary>
    public virtual TimeSpan? TimeToLive => null;

    /// <summary>
    /// The variant's own fields, flattened for the FCM data map. The ONE place a wire key is
    /// spelled -- do not build data maps at the call site.
    /// </summary>
    protected abstract void WriteFields(IDictionary<string, string> data);

    /// <summary>
    /// The complete data map, "type" first. Sealed: a variant contributes fields, never the
    /// discriminant, so no variant can omit or overwrite it.
    /// </summary>
    public IReadOnlyDictionary<string, string> ToData()
    {
        var data = new Dictionary<string, string>(StringComparer.Ordinal) { ["type"] = Type };
        WriteFields(data);
        data["type"] = Type;   // belt and braces: a variant writing "type" cannot win
        return data;
    }
}

/// <summary>
/// A team chat message. One thread per team, so <see cref="TeamId"/> is the thread key.
/// </summary>
public sealed record ChatPushPayload : PushPayload
{
    public override string Type => "chat";

    /// <summary>
    /// The registration this push is FOR, not the author's. A parent with two children on the
    /// same team receives a push against the registration they may not currently be signed in
    /// as; the client re-selects before routing.
    /// </summary>
    public required Guid RegId { get; init; }

    /// <summary>The team whose thread to open.</summary>
    public required Guid TeamId { get; init; }

    /// <summary>
    /// The message's PERMANENT thread position (teamchat.Messages.Seq), so the client can tell
    /// whether it already holds this message.
    ///
    /// NOT a catch-up cursor. The cursor is a LastTouchSeq, and while both draw from the same
    /// sequence -- so Seq is never greater than the row's LastTouchSeq -- they are not the same
    /// number and must never be compared for equality.
    /// </summary>
    public required long Seq { get; init; }

    /// <summary>
    /// Collapsed per team: a burst in one thread lands as one banner. Per team and not per job,
    /// because two teams talking at once are two conversations.
    /// </summary>
    public override string? CollapseKey => $"chat_{TeamId}";

    public override string? AndroidChannelId => "chat";

    /// <summary>
    /// Six hours. A chat message that could not be delivered for six hours is not worth waking
    /// a phone for -- the client catches up from the cursor on next open regardless.
    /// </summary>
    public override TimeSpan? TimeToLive => TimeSpan.FromHours(6);

    protected override void WriteFields(IDictionary<string, string> data)
    {
        data["regId"] = RegId.ToString();
        data["teamId"] = TeamId.ToString();
        data["seq"] = Seq.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// A director/staff alert. Declared so the union is complete and the "alert" spelling lives in
/// one place; the live alert sender is NOT routed through it yet (see <see cref="PushPayload"/>).
/// </summary>
public sealed record AlertPushPayload : PushPayload
{
    public override string Type => "alert";

    public required string JobName { get; init; }

    public Guid? TeamId { get; init; }

    /// <summary>Alerts do not collapse -- two alerts are two things somebody needs to read.</summary>
    public override string? CollapseKey => null;

    protected override void WriteFields(IDictionary<string, string> data)
    {
        data["jobName"] = JobName;
        if (TeamId is { } t) data["teamId"] = t.ToString();
    }
}

/// <summary>
/// A finished game's score. Declared for completeness; GameResultPushService is untouched and
/// still builds its own map. The seven keys below are exactly the ones the shipped TSIC-Events
/// handler reads -- do not add to them without an app release that reads the addition.
/// </summary>
public sealed record GameResultPushPayload : PushPayload
{
    public override string Type => "gameResult";

    public required string JobName { get; init; }
    public required string AgegroupName { get; init; }
    public required string DivName { get; init; }
    public required string FirstTeam { get; init; }
    public required string FirstScore { get; init; }
    public required string SecondTeam { get; init; }
    public required string SecondScore { get; init; }

    public override string? CollapseKey => null;

    protected override void WriteFields(IDictionary<string, string> data)
    {
        data["jobName"] = JobName;
        data["agegroupName"] = AgegroupName;
        data["divName"] = DivName;
        data["firstTeam"] = FirstTeam;
        data["firstScore"] = FirstScore;
        data["secondTeam"] = SecondTeam;
        data["secondScore"] = SecondScore;
    }
}
