using Dapper;
using Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Email;

internal static class EmailDedupeGuard
{
    public static async Task<bool> TryInsertAsync(
        IConnectionFactory factory, string to, string subject, string html, string? text, string dedupeKey, ILogger logger, CancellationToken ct)
    {
        try
        {
            using var conn = await factory.CreateOpenConnectionAsync(ct);
            var affected = await conn.ExecuteAsync(new CommandDefinition(
                """
                insert into "EmailJob"(id,"to",subject,html,text,status,"dedupeKey","createdAt")
                values(gen_random_uuid()::text,@to,@subject,@html,@text,'pending',@dedupeKey,now())
                on conflict ("dedupeKey") do nothing
                """,
                new { to, subject, html, text, dedupeKey }, cancellationToken: ct));
            return affected > 0;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[EMAIL] Failed to check/insert dedupeKey={Key}, proceeding with send", dedupeKey);
            return true;
        }
    }
}
