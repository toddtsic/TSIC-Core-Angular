namespace TSIC.Domain.Adults;

/// <summary>
/// Whether a job's adult coach form collects a USA Lacrosse membership number, and whether it blocks.
///
/// <para>Encoded as a pipe token on <c>Jobs.RegformName_Coach</c> (<c>StaffSTEPS|USLAX-R</c>), mirroring
/// the pipe encoding <c>Jobs.CoreRegformPlayer</c> already uses for the player side
/// (<c>PP27|BYGRADYEAR</c>). The token makes the capability ORTHOGONAL to the AC profile, which is what
/// the adult form model always claimed it was — previously it was baked into WHICH legacy form name you
/// picked, so AC3 + USLax was simply unrepresentable.</para>
///
/// <para>Lives in Domain so the Contracts DTOs and the API services share one definition.</para>
/// </summary>
public enum AdultUsLaxMode
{
    /// <summary>No USA Lacrosse field on the form. The default when no token is present.</summary>
    None = 0,

    /// <summary>
    /// The field is shown but optional. A supplied number is still hard-validated at submit
    /// (an inactive or non-coach membership is rejected); leaving it blank never blocks registration.
    /// </summary>
    Optional = 1,

    /// <summary>
    /// The field is shown and required — the coach cannot complete registration without an active
    /// USA Lacrosse coach membership. This is the legacy StaffLaxValidate behaviour.
    /// </summary>
    Required = 2,
}
