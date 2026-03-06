using Application.Abstractions;
using Dapper;
using Infrastructure.Data;

namespace Infrastructure.Repositories;

public sealed class ConversationRepository(IConnectionFactory factory) : IConversationRepository
{
    public async Task<string?> ResolveProfessionalUserIdAsync(string professionalIdOrUserId, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        // Check if it's a Professional.id (returns userId) or already a User.id
        var userId = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "select \"userId\" from \"Professional\" where id=@id",
            new { id = professionalIdOrUserId }, cancellationToken: ct));
        if (userId is not null) return userId;
        // Check if it's a User.id directly
        var exists = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "select count(1) from \"User\" where id=@id",
            new { id = professionalIdOrUserId }, cancellationToken: ct));
        return exists > 0 ? professionalIdOrUserId : null;
    }

    public async Task<IReadOnlyList<object>> GetByParticipantAsync(string? clientId, string? professionalId, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        var where = "where 1=1";
        var p = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(clientId)) { where += " and c.\"clientId\"=@clientId"; p.Add("clientId", clientId); }
        if (!string.IsNullOrWhiteSpace(professionalId)) { where += " and c.\"professionalId\"=@professionalId"; p.Add("professionalId", professionalId); }

        var sql = $"""
            select c.id,c."orderId",c."clientId",c."professionalId",c."createdAt",
                   c."clientLastReadAt",c."professionalLastReadAt",
                   client.id as "cid",client.name as "cname",client.email as "cemail",client.phone as "cphone",
                   pro.id as "pid",pro.name as "pname",pro.email as "pemail",pro.phone as "pphone",
                   m.id as "mid",m.text as "mtext",m."sentAt" as "msentat",m."senderId" as "msenderid"
            from "Conversation" c
            join "User" client on client.id=c."clientId"
            join "User" pro on pro.id=c."professionalId"
            left join lateral (
                select id,text,"sentAt","senderId" from "Message" where "conversationId"=c.id order by "sentAt" desc limit 1
            ) m on true
            {where}
            order by coalesce(m."sentAt", c."createdAt") desc
            """;

        var rows = await conn.QueryAsync(new CommandDefinition(sql, p, cancellationToken: ct));
        return rows.Select(r =>
        {
            IDictionary<string, object?> d = r;
            return (object)new
            {
                id = d["id"],
                orderId = d["orderId"],
                clientId = d["clientId"],
                professionalId = d["professionalId"],
                createdAt = d["createdAt"],
                clientLastReadAt = d["clientLastReadAt"],
                professionalLastReadAt = d["professionalLastReadAt"],
                client = new { id = d["cid"], name = d["cname"], email = d["cemail"], phone = d["cphone"] },
                professional = new { id = d["pid"], name = d["pname"], email = d["pemail"], phone = d["pphone"] },
                lastMessage = d["mid"] is null ? null : new { id = d["mid"], text = d["mtext"], sentAt = d["msentat"], senderId = d["msenderid"] }
            };
        }).ToList();
    }

    public async Task<object?> GetOrCreateAsync(string clientId, string professionalUserId, string? orderId, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);

        // Try to find existing conversation
        dynamic? existing = null;
        if (!string.IsNullOrWhiteSpace(orderId))
        {
            existing = await conn.QuerySingleOrDefaultAsync(new CommandDefinition(
                "select id,\"orderId\",\"clientId\",\"professionalId\",\"createdAt\",\"clientLastReadAt\",\"professionalLastReadAt\" from \"Conversation\" where \"orderId\"=@orderId",
                new { orderId }, cancellationToken: ct));
        }

        if (existing is null)
        {
            existing = await conn.QuerySingleOrDefaultAsync(new CommandDefinition(
                "select id,\"orderId\",\"clientId\",\"professionalId\",\"createdAt\",\"clientLastReadAt\",\"professionalLastReadAt\" from \"Conversation\" where \"clientId\"=@clientId and \"professionalId\"=@professionalUserId limit 1",
                new { clientId, professionalUserId }, cancellationToken: ct));
        }

        if (existing is null)
        {
            const string insertSql = """
                insert into "Conversation"(id,"orderId","clientId","professionalId","createdAt")
                values(gen_random_uuid()::text,@orderId,@clientId,@professionalUserId,now())
                returning id,"orderId","clientId","professionalId","createdAt","clientLastReadAt","professionalLastReadAt"
                """;
            existing = await conn.QuerySingleAsync(new CommandDefinition(insertSql,
                new { orderId, clientId, professionalUserId }, cancellationToken: ct));
        }
        else if (existing.orderId is null && !string.IsNullOrWhiteSpace(orderId))
        {
            // Try to link orderId
            try
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    "update \"Conversation\" set \"orderId\"=@orderId where id=@id",
                    new { orderId, id = existing.id }, cancellationToken: ct));
                existing.orderId = orderId;
            }
            catch { /* ignore unique conflict */ }
        }

        return existing;
    }

    public async Task<IReadOnlyList<object>> GetMessagesAsync(string conversationId, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        var rows = await conn.QueryAsync(new CommandDefinition(
            """
            select m.id,m."conversationId",m."senderId",m.text,m."sentAt",
                   u.id as "uid",u.name as "uname"
            from "Message" m join "User" u on u.id=m."senderId"
            where m."conversationId"=@conversationId order by m."sentAt" asc
            """,
            new { conversationId }, cancellationToken: ct));
        return rows.Select(r =>
        {
            IDictionary<string, object?> d = r;
            return (object)new
            {
                id = d["id"],
                conversationId = d["conversationId"],
                senderId = d["senderId"],
                text = d["text"],
                sentAt = d["sentAt"],
                sender = new { id = d["uid"], name = d["uname"] }
            };
        }).ToList();
    }

    public async Task<object> CreateMessageAsync(string conversationId, string senderId, string text, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        const string sql = """
            insert into "Message"(id,"conversationId","senderId",text,"sentAt")
            values(gen_random_uuid()::text,@conversationId,@senderId,@text,now())
            returning id,"conversationId","senderId",text,"sentAt"
            """;
        var msg = await conn.QuerySingleAsync(new CommandDefinition(sql,
            new { conversationId, senderId, text }, cancellationToken: ct));

        // Attach sender info as flat fields on the ExpandoObject
        var sender = await conn.QuerySingleOrDefaultAsync(new CommandDefinition(
            "select id,name,email from \"User\" where id=@senderId",
            new { senderId }, cancellationToken: ct));

        IDictionary<string, object?> msgDict = msg;
        IDictionary<string, object?> senderDict = sender;
        msgDict["senderName"] = senderDict?["name"];
        msgDict["senderEmail"] = senderDict?["email"];
        return msg;
    }

    public async Task<object?> GetConversationForReadAsync(string conversationId, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        const string sql = """
            select c.id,c."clientId",c."professionalId",c."clientLastReadAt",c."professionalLastReadAt",
                   client.email as "clientEmail",client.name as "clientName",
                   pro.email as "professionalEmail",pro.name as "professionalName"
            from "Conversation" c
            join "User" client on client.id=c."clientId"
            join "User" pro on pro.id=c."professionalId"
            where c.id=@conversationId
            """;
        return await conn.QuerySingleOrDefaultAsync(new CommandDefinition(sql, new { conversationId }, cancellationToken: ct));
    }

    public async Task MarkReadAsync(string conversationId, bool isClient, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        var col = isClient ? "\"clientLastReadAt\"" : "\"professionalLastReadAt\"";
        await conn.ExecuteAsync(new CommandDefinition(
            $"update \"Conversation\" set {col}=now() where id=@conversationId",
            new { conversationId }, cancellationToken: ct));
    }
}
