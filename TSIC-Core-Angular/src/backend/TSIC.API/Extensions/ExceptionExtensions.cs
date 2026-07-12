using System.Text;

namespace TSIC.API.Extensions;

public static class ExceptionExtensions
{
    /// <summary>
    /// The message, plus every inner exception's message, joined.
    ///
    /// <para>Exists because the messages that matter are almost never on the outer exception. EF's
    /// <c>DbUpdateException.Message</c> is the literal string "An error occurred while saving the entity
    /// changes. See the inner exception for details." — and if that is what you put in an operator's
    /// email or a result DTO, you have told them nothing. The actual cause (a CHECK constraint, a
    /// truncation, a deadlock) is one level down, in the SqlException.</para>
    ///
    /// <para>Use this anywhere an exception message is going to reach a human who cannot open a
    /// debugger: digest emails, background-job results, API error payloads.</para>
    /// </summary>
    public static string Flatten(this Exception ex)
    {
        var sb = new StringBuilder();
        for (Exception? e = ex; e != null; e = e.InnerException)
        {
            if (sb.Length > 0) sb.Append(" --> ");
            sb.Append(e.Message);
        }
        return sb.ToString();
    }
}
