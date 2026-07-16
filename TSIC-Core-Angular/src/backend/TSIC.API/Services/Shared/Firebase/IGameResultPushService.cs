namespace TSIC.API.Services.Shared.Firebase;

/// <summary>
/// Sends a game-result push notification to devices subscribed to either team of a game
/// (legacy parity: FirebaseService.PushGameResultsToSubscribedDevices).
/// Called from the score-write chokepoint; never throws — a push failure must not
/// fail the score write.
/// </summary>
public interface IGameResultPushService
{
    Task PushGameResultAsync(int gid, CancellationToken ct = default);
}
