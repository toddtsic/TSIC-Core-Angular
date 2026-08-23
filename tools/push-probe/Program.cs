using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;

// ---------------------------------------------------------------------------------------------
// PushProbe - run one FCM registration token against BOTH TSIC Firebase projects.
//
// TSIC-Events (tsic-events, sender 871529174390) and TSIC-Teams (tsic-teams, sender 330996497478)
// are separate projects. A registration token is scoped to the project that minted it, so the
// wrong credential answers SenderIdMismatch and the push reaches nobody. This tells you which
// project owns a token - which is the question behind almost every "the push didn't arrive".
//
// DRY RUN IS THE DEFAULT. A dry run reaches Google and is fully validated against the
// credential, but is never delivered to the handset, so it is safe against a stranger's device.
// Pass --send to actually deliver.
//
//   dotnet run -- <token>                    validate against both senders, deliver nothing
//   dotnet run -- <token> --send             deliver for real
//   dotnet run -- <token> --send --body="x"  custom notification text
//   dotnet run -- <token> --creds=<dir>      credential directory override
//
// Exit codes: 0 exactly one sender accepted (the healthy answer) | 1 usage error
//             2 no sender accepted (dead token, or neither credential works)
//             3 both senders accepted (should be impossible - investigate)
// ---------------------------------------------------------------------------------------------

var token = args.FirstOrDefault(a => !a.StartsWith("--"));
if (string.IsNullOrWhiteSpace(token))
{
    Console.Error.WriteLine("usage: dotnet run -- <fcm-token> [--send] [--body=text] [--creds=dir]");
    return 1;
}

var dryRun = !args.Contains("--send");
var body = Arg("--body=")
           ?? (dryRun ? "Validation only - you should not see this." : "Test push from TSIC-Core-Angular.");

// Defaults to the API project, where both credential files live. They are gitignored - present
// on the box, never in source - so this path is a machine fact, not a repo fact.
var credDir = Arg("--creds=")
              ?? Path.Combine(AppContext.BaseDirectory,
                     "..", "..", "..", "..", "..", "TSIC-Core-Angular", "src", "backend", "TSIC.API");

Console.WriteLine($"mode   : {(dryRun ? "DRY RUN (validated by Google, delivered to nobody)" : "*** REAL SEND ***")}");
Console.WriteLine($"token  : {token[..Math.Min(24, token.Length)]}... ({token.Length} chars)");
Console.WriteLine($"creds  : {Path.GetFullPath(credDir)}");
Console.WriteLine();

var senders = new (string Label, string File, string AppName)[]
{
    ("TSIC-Events (tsic-events)", "FirebaseAuth_TSICEvents.json", "PROBE_EVENTS"),
    ("TSIC-Teams  (tsic-teams)",  "FirebaseAuth_TSICTeams.json",  "PROBE_TEAMS")
};

var accepted = 0;

foreach (var (label, file, appName) in senders)
{
    var path = Path.Combine(credDir, file);
    if (!File.Exists(path))
    {
        Console.WriteLine($"{label,-28} SKIPPED      credential not found: {path}");
        continue;
    }

#pragma warning disable CS0618 // FirebaseAdmin.AppOptions.Credential still types as GoogleCredential.
    var cred = GoogleCredential.FromFile(path)
        .CreateScoped("https://www.googleapis.com/auth/firebase.messaging");
#pragma warning restore CS0618

    var app = FirebaseApp.GetInstance(appName)
              ?? FirebaseApp.Create(new AppOptions { Credential = cred }, appName);

    var message = new Message
    {
        Token = token,
        Notification = new Notification { Title = "TSIC push probe", Body = body },
        Apns = new ApnsConfig { Aps = new Aps { Sound = "default" } },
        Android = new AndroidConfig { Notification = new AndroidNotification { Sound = "default" } }
    };

    try
    {
        // SendEachAsync reports per-message failures on the BatchResponse rather than throwing -
        // the same shape the production path reads, so this probe fails the way prod fails.
        var resp = await FirebaseMessaging.GetMessaging(app).SendEachAsync([message], dryRun);
        var r = resp.Responses[0];

        if (r.IsSuccess)
        {
            accepted++;
            Console.WriteLine($"{label,-28} ACCEPTED     messageId={r.MessageId}");
        }
        else
        {
            var code = r.Exception?.MessagingErrorCode?.ToString() ?? "Unknown";
            Console.WriteLine($"{label,-28} REJECTED     {code} - {r.Exception?.Message}");
        }
    }
    catch (Exception ex)
    {
        // A throw here is credential-level, not token-level: bad key, revoked service account,
        // clock skew. Worth distinguishing from a per-message rejection.
        Console.WriteLine($"{label,-28} SENDER ERROR {ex.GetType().Name}: {ex.Message}");
    }
}

Console.WriteLine();
switch (accepted)
{
    case 1:
        Console.WriteLine("One accepted, one rejected - healthy. The accepting project owns this token,");
        Console.WriteLine("and that is the app the job must resolve to for a push to land.");
        if (!dryRun)
        {
            Console.WriteLine();
            Console.WriteLine("Accepted means FCM took it, not that the handset showed it. If nothing arrives");
            Console.WriteLine("on a development build, suspect the APNs sandbox before suspecting the token.");
        }
        return 0;
    case 0:
        Console.WriteLine("Nobody accepted it. Either the token is dead (app uninstalled, token rotated)");
        Console.WriteLine("or neither credential is usable - a SENDER ERROR line above tells them apart.");
        return 2;
    default:
        Console.WriteLine("BOTH projects accepted the same token. That should not be possible; do not");
        Console.WriteLine("route anything on this result until it is understood.");
        return 3;
}

string? Arg(string prefix) =>
    args.FirstOrDefault(a => a.StartsWith(prefix))?[prefix.Length..];
