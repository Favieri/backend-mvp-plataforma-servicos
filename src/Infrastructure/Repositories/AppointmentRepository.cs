using Application.Abstractions;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class AppointmentRepository(AppDbContext ctx) : IAppointmentRepository
{
    public async Task<IReadOnlyList<Appointment>> GetByClientAsync(string clientId, CancellationToken ct)
        => await ctx.Appointments
            .AsNoTracking()
            .Where(a => a.ClientId == clientId)
            .OrderByDescending(a => a.StartsAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<object>> GetByProfessionalAsync(
        string professionalId, string? status, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var query = ctx.Appointments
            .AsNoTracking()
            .Where(a => a.ProfessionalId == professionalId);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(a => a.Status == status.ToUpperInvariant());

        if (from.HasValue)
            query = query.Where(a => a.StartsAt >= from.Value);

        if (to.HasValue)
            query = query.Where(a => a.EndsAt <= to.Value);

        var rows = await query
            .OrderBy(a => a.StartsAt)
            .Select(a => new
            {
                id = a.Id,
                professionalId = a.ProfessionalId,
                clientId = a.ClientId,
                serviceId = a.ServiceId,
                startsAt = a.StartsAt,
                endsAt = a.EndsAt,
                status = a.Status,
                location = a.Location,
                notes = a.Notes
            })
            .ToListAsync(ct);

        return rows.Cast<object>().ToList();
    }

    public async Task<bool> HasConflictAsync(
        string professionalId, DateTime startsAt, DateTime endsAt, CancellationToken ct)
        => await ctx.Appointments
            .AsNoTracking()
            .Where(a =>
                a.ProfessionalId == professionalId
                && (a.Status == "PENDING" || a.Status == "CONFIRMED")
                && !(a.EndsAt <= startsAt || a.StartsAt >= endsAt))
            .AnyAsync(ct);

    public async Task<(int? SlotMinutes, bool? AllowInstantBooking)> GetProfessionalConfigAsync(
        string professionalId, CancellationToken ct)
    {
        var row = await ctx.Professionals
            .AsNoTracking()
            .Where(p => p.Id == professionalId)
            .Select(p => new { p.SlotMinutes, p.AllowInstantBooking })
            .FirstOrDefaultAsync(ct);

        return row is null ? (null, null) : (row.SlotMinutes, row.AllowInstantBooking);
    }

    public async Task<bool> ProfessionalExistsAsync(string professionalId, CancellationToken ct)
        => await ctx.Professionals.AsNoTracking().AnyAsync(p => p.Id == professionalId, ct);

    public async Task<Appointment> CreateAsync(Appointment appointment, CancellationToken ct)
    {
        var entity = new Appointment(
            Id: Guid.NewGuid().ToString(),
            ProfessionalId: appointment.ProfessionalId,
            ClientId: appointment.ClientId,
            ServiceId: appointment.ServiceId,
            StartsAt: appointment.StartsAt,
            EndsAt: appointment.EndsAt,
            Status: appointment.Status,
            Location: appointment.Location,
            Notes: appointment.Notes);

        ctx.Appointments.Add(entity);
        await ctx.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<Appointment?> UpdateStatusAsync(string id, string status, CancellationToken ct)
    {
        await ctx.Appointments
            .Where(a => a.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, status), ct);

        return await ctx.Appointments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
    }

    public async Task<object?> GetAppointmentWithParticipantsAsync(string id, CancellationToken ct)
    {
        var result = await (
            from a in ctx.Appointments.AsNoTracking()
            join pro in ctx.Professionals.AsNoTracking() on a.ProfessionalId equals pro.Id
            join proUsr in ctx.Users.AsNoTracking() on pro.UserId equals proUsr.Id
            join svc in ctx.Services.AsNoTracking() on a.ServiceId equals svc.Id into svcJoin
            from svc in svcJoin.DefaultIfEmpty()
            where a.Id == id
            select new
            {
                id = a.Id,
                professionalId = a.ProfessionalId,
                clientId = a.ClientId,
                serviceId = a.ServiceId,
                startsAt = a.StartsAt,
                endsAt = a.EndsAt,
                status = a.Status,
                location = a.Location,
                notes = a.Notes,
                professionalEmail = proUsr.Email,
                professionalName = proUsr.Name,
                serviceName = svc != null ? svc.Name : null
            }
        ).FirstOrDefaultAsync(ct);

        if (result is null) return null;

        // Load client info separately (nullable)
        if (result.clientId is null)
            return new { result.id, result.professionalId, result.clientId, result.serviceId, result.startsAt, result.endsAt, result.status, result.location, result.notes, result.professionalEmail, result.professionalName, clientEmail = (string?)null, clientName = (string?)null, result.serviceName };

        var client = await ctx.Users
            .AsNoTracking()
            .Where(u => u.Id == result.clientId)
            .Select(u => new { u.Email, u.Name })
            .FirstOrDefaultAsync(ct);

        return new
        {
            result.id,
            result.professionalId,
            result.clientId,
            result.serviceId,
            result.startsAt,
            result.endsAt,
            result.status,
            result.location,
            result.notes,
            result.professionalEmail,
            result.professionalName,
            clientEmail = client?.Email,
            clientName = client?.Name,
            result.serviceName
        };
    }
}
