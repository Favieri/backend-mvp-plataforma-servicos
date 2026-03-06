using Application.Abstractions;
using Dapper;
using Domain.Entities;
using Infrastructure.Data;

namespace Infrastructure.Repositories;

public sealed class AvailabilityRepository(IConnectionFactory factory) : IAvailabilityRepository
{
    public async Task<IReadOnlyList<object>> GetByProfessionalAsync(string professionalId, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        var rows = await conn.QueryAsync(new CommandDefinition(
            "select id,\"professionalId\",weekday,\"startMinutes\",\"endMinutes\",active from \"ProfessionalAvailability\" where \"professionalId\"=@professionalId order by weekday asc, \"startMinutes\" asc",
            new { professionalId }, cancellationToken: ct));
        return rows.Cast<object>().ToList();
    }

    public async Task SaveAllAsync(string professionalId, IReadOnlyList<(int Weekday, int StartMinutes, int EndMinutes, bool Active)> rows, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync(new CommandDefinition(
            "delete from \"ProfessionalAvailability\" where \"professionalId\"=@professionalId",
            new { professionalId }, transaction: tx, cancellationToken: ct));

        foreach (var row in rows)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "insert into \"ProfessionalAvailability\"(id,\"professionalId\",weekday,\"startMinutes\",\"endMinutes\",active) values(gen_random_uuid()::text,@professionalId,@weekday,@startMinutes,@endMinutes,@active) on conflict do nothing",
                new { professionalId, weekday = row.Weekday % 7, startMinutes = row.StartMinutes, endMinutes = row.EndMinutes, active = row.Active },
                transaction: tx, cancellationToken: ct));
        }
        tx.Commit();
    }

    public async Task<IReadOnlyList<object>> GetBlocksAsync(string professionalId, DateTime from, DateTime to, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        var rows = await conn.QueryAsync(new CommandDefinition(
            "select id,\"professionalId\",\"startsAt\",\"endsAt\",reason,\"createdAt\" from \"ProfessionalBlock\" where \"professionalId\"=@professionalId and \"startsAt\">=@from and \"endsAt\"<=@to order by \"startsAt\" asc",
            new { professionalId, from, to }, cancellationToken: ct));
        return rows.Cast<object>().ToList();
    }

    public async Task<object> CreateBlockAsync(string professionalId, DateTime startsAt, DateTime endsAt, string? reason, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        const string sql = """
            insert into "ProfessionalBlock"(id,"professionalId","startsAt","endsAt",reason,"createdAt")
            values(gen_random_uuid()::text,@professionalId,@startsAt,@endsAt,@reason,now())
            returning id,"professionalId","startsAt","endsAt",reason,"createdAt"
            """;
        return await conn.QuerySingleAsync(new CommandDefinition(sql,
            new { professionalId, startsAt, endsAt, reason }, cancellationToken: ct));
    }

    public async Task<bool> ProfessionalExistsAsync(string professionalId, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "select count(1) from \"Professional\" where id=@professionalId",
            new { professionalId }, cancellationToken: ct)) > 0;
    }

    public async Task<object?> GetProfessionalSchedulingConfigAsync(string professionalId, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync(new CommandDefinition(
            "select id,\"slotMinutes\",\"leadTimeMinutes\",\"maxAdvanceDays\",\"allowInstantBooking\" from \"Professional\" where id=@professionalId",
            new { professionalId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ProfessionalAvailability>> GetAvailabilityForDayAsync(string professionalId, int weekday, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<ProfessionalAvailability>(new CommandDefinition(
            "select id AS \"Id\",\"professionalId\" AS \"ProfessionalId\",weekday AS \"Weekday\",\"startMinutes\" AS \"StartMinutes\",\"endMinutes\" AS \"EndMinutes\",active AS \"Active\" from \"ProfessionalAvailability\" where \"professionalId\"=@professionalId and active=true and weekday=@weekday order by \"startMinutes\" asc",
            new { professionalId, weekday }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<Appointment>> GetAppointmentsForDayAsync(string professionalId, DateTime dayStartUtc, DateTime dayEndUtc, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<Appointment>(new CommandDefinition(
            "select id AS \"Id\",\"professionalId\" AS \"ProfessionalId\",\"clientId\" AS \"ClientId\",\"serviceId\" AS \"ServiceId\",\"startsAt\" AS \"StartsAt\",\"endsAt\" AS \"EndsAt\",status AS \"Status\",location AS \"Location\",notes AS \"Notes\" from \"Appointment\" where \"professionalId\"=@professionalId and status in ('PENDING','CONFIRMED') and \"startsAt\" < @dayEndUtc and \"endsAt\" > @dayStartUtc",
            new { professionalId, dayStartUtc, dayEndUtc }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<ProfessionalBlock>> GetBlocksForDayAsync(string professionalId, DateTime dayStartUtc, DateTime dayEndUtc, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<ProfessionalBlock>(new CommandDefinition(
            "select id AS \"Id\",\"professionalId\" AS \"ProfessionalId\",\"startsAt\" AS \"StartsAt\",\"endsAt\" AS \"EndsAt\",reason AS \"Reason\",\"createdAt\" AS \"CreatedAt\" from \"ProfessionalBlock\" where \"professionalId\"=@professionalId and \"startsAt\" < @dayEndUtc and \"endsAt\" > @dayStartUtc",
            new { professionalId, dayStartUtc, dayEndUtc }, cancellationToken: ct));
        return rows.ToList();
    }
}
