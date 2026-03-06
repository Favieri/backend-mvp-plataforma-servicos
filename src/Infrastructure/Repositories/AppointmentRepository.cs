using Application.Abstractions;
using Dapper;
using Domain.Entities;
using Infrastructure.Data;

namespace Infrastructure.Repositories;

public sealed class AppointmentRepository(IConnectionFactory factory) : IAppointmentRepository
{
    private const string AppointmentSelect = """
        id AS "Id","professionalId" AS "ProfessionalId","clientId" AS "ClientId","serviceId" AS "ServiceId",
        "startsAt" AS "StartsAt","endsAt" AS "EndsAt",status AS "Status",location AS "Location",notes AS "Notes"
        """;

    public async Task<IReadOnlyList<Appointment>> GetByClientAsync(string clientId, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        var sql = $"select {AppointmentSelect} from \"Appointment\" where \"clientId\"=@clientId order by \"startsAt\" desc";
        return (await conn.QueryAsync<Appointment>(new CommandDefinition(sql, new { clientId }, cancellationToken: ct))).ToList();
    }

    public async Task<IReadOnlyList<object>> GetByProfessionalAsync(string professionalId, string? status, DateTime? from, DateTime? to, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        var where = "where \"professionalId\"=@professionalId";
        var p = new DynamicParameters();
        p.Add("professionalId", professionalId);

        if (!string.IsNullOrWhiteSpace(status)) { where += " and status=@status"; p.Add("status", status.ToUpperInvariant()); }
        if (from.HasValue) { where += " and \"startsAt\">=@from"; p.Add("from", from); }
        if (to.HasValue) { where += " and \"endsAt\"<=@to"; p.Add("to", to); }

        var sql = $"select {AppointmentSelect} from \"Appointment\" {where} order by \"startsAt\" asc";
        return (await conn.QueryAsync(new CommandDefinition(sql, p, cancellationToken: ct))).Cast<object>().ToList();
    }

    public async Task<bool> HasConflictAsync(string professionalId, DateTime startsAt, DateTime endsAt, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        // Overlap: NOT (endsAt <= startsAt OR startsAt >= endsAt)
        const string sql = """
            select count(1) from "Appointment"
            where "professionalId"=@professionalId
              and status in ('PENDING','CONFIRMED')
              and not ("endsAt" <= @startsAt or "startsAt" >= @endsAt)
            """;
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { professionalId, startsAt, endsAt }, cancellationToken: ct)) > 0;
    }

    public async Task<(int? SlotMinutes, bool? AllowInstantBooking)> GetProfessionalConfigAsync(string professionalId, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync(new CommandDefinition(
            "select \"slotMinutes\",\"allowInstantBooking\" from \"Professional\" where id=@professionalId",
            new { professionalId }, cancellationToken: ct));
        if (row is null) return (null, null);
        IDictionary<string, object?> d = row;
        var slotMinutes = d["slotMinutes"] is null ? (int?)null : Convert.ToInt32(d["slotMinutes"]);
        var allowInstant = d["allowInstantBooking"] is null ? (bool?)null : Convert.ToBoolean(d["allowInstantBooking"]);
        return (slotMinutes, allowInstant);
    }

    public async Task<bool> ProfessionalExistsAsync(string professionalId, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "select count(1) from \"Professional\" where id=@professionalId",
            new { professionalId }, cancellationToken: ct)) > 0;
    }

    public async Task<Appointment> CreateAsync(Appointment appointment, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        var sql = $"""
            insert into "Appointment"(id,"professionalId","clientId","serviceId","startsAt","endsAt",status,location,notes,"createdAt","updatedAt")
            values (gen_random_uuid()::text,@ProfessionalId,@ClientId,@ServiceId,@StartsAt,@EndsAt,@Status,@Location,@Notes,now(),now())
            returning {AppointmentSelect}
            """;
        return await conn.QuerySingleAsync<Appointment>(new CommandDefinition(sql, appointment, cancellationToken: ct));
    }

    public async Task<Appointment?> UpdateStatusAsync(string id, string status, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        var sql = $"update \"Appointment\" set status=@status,\"updatedAt\"=now() where id=@id returning {AppointmentSelect}";
        return await conn.QuerySingleOrDefaultAsync<Appointment>(new CommandDefinition(sql, new { id, status }, cancellationToken: ct));
    }

    public async Task<object?> GetAppointmentWithParticipantsAsync(string id, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        const string sql = """
            select a.id,a."professionalId",a."clientId",a."serviceId",a."startsAt",a."endsAt",a.status,a.location,a.notes,
                   prousr.email as "professionalEmail",prousr.name as "professionalName",
                   client.email as "clientEmail",client.name as "clientName",
                   s.name as "serviceName"
            from "Appointment" a
            join "Professional" pro on pro.id=a."professionalId"
            join "User" prousr on prousr.id=pro."userId"
            left join "User" client on client.id=a."clientId"
            left join "Service" s on s.id=a."serviceId"
            where a.id=@id
            """;
        return await conn.QuerySingleOrDefaultAsync(new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }
}
