using Domain.Entities;
using Domain.Enums;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IntegrationTests.Repositories;

public sealed class ProposalRepositoryTests : RepositoryTestBase
{
    private readonly ProposalRepository _repo;

    public ProposalRepositoryTests()
    {
        _repo = new ProposalRepository(Ctx);
        SeedFixtures().GetAwaiter().GetResult();
    }

    private async Task SeedFixtures()
    {
        Ctx.Users.Add(new User("client1", "Client", "client@test.com", null, "CLIENT", null, DateTime.UtcNow));
        Ctx.Services.Add(new Service("svc1", "Pintura", null, DateTime.UtcNow));
        await Ctx.SaveChangesAsync();
    }

    private static Proposal BuildProposal(string id = "prop1") => Proposal.Create(
        id: id,
        professionalId: "pro1",
        clientId: "client1",
        serviceId: "svc1",
        scope: "Pintar sala",
        priceTotalCents: 50000,
        validUntil: DateTime.UtcNow.AddDays(3));

    [Fact]
    public async Task CreateAsync_PersistsProposal()
    {
        var proposal = await _repo.CreateAsync(BuildProposal(), CancellationToken.None);

        Assert.NotNull(proposal.Id);
        Assert.Equal("client1", proposal.ClientId);
        Assert.Equal("pro1", proposal.ProfessionalId);
        Assert.Equal(ProposalStatus.Draft, proposal.Status);
        Assert.Equal(50000, proposal.PriceTotalCents);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsProposal_WhenExists()
    {
        var proposal = await _repo.CreateAsync(BuildProposal("p2"), CancellationToken.None);
        var found = await _repo.GetByIdAsync(proposal.Id, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(proposal.Id, found!.Id);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotExists()
    {
        var found = await _repo.GetByIdAsync("nonexistent", CancellationToken.None);
        Assert.Null(found);
    }

    [Fact]
    public async Task SendAsync_ChangesStatusToSent()
    {
        var proposal = await _repo.CreateAsync(BuildProposal("p3"), CancellationToken.None);
        var ok = await _repo.SendAsync(proposal.Id, CancellationToken.None);

        Assert.True(ok);
        Ctx.ChangeTracker.Clear();
        var updated = await Ctx.Proposals.AsNoTracking().FirstAsync(p => p.Id == proposal.Id);
        Assert.Equal(ProposalStatus.Sent, updated.Status);
    }

    [Fact]
    public async Task AcceptAsync_ChangesStatusToAccepted_AndSetsOrderId()
    {
        var proposal = await _repo.CreateAsync(BuildProposal("p4"), CancellationToken.None);
        await _repo.SendAsync(proposal.Id, CancellationToken.None);

        var ok = await _repo.AcceptAsync(proposal.Id, "order-abc", CancellationToken.None);

        Assert.True(ok);
        Ctx.ChangeTracker.Clear();
        var updated = await Ctx.Proposals.AsNoTracking().FirstAsync(p => p.Id == proposal.Id);
        Assert.Equal(ProposalStatus.Accepted, updated.Status);
        Assert.Equal("order-abc", updated.OrderId);
    }

    [Fact]
    public async Task RejectAsync_ChangesStatusToRejected()
    {
        var proposal = await _repo.CreateAsync(BuildProposal("p5"), CancellationToken.None);
        await _repo.SendAsync(proposal.Id, CancellationToken.None);

        var ok = await _repo.RejectAsync(proposal.Id, "Preço alto", CancellationToken.None);

        Assert.True(ok);
        Ctx.ChangeTracker.Clear();
        var updated = await Ctx.Proposals.AsNoTracking().FirstAsync(p => p.Id == proposal.Id);
        Assert.Equal(ProposalStatus.Rejected, updated.Status);
        Assert.Equal("Preço alto", updated.RejectionReason);
    }

    [Fact]
    public async Task GetByConversationAsync_ReturnsMatchingProposals()
    {
        var p = BuildProposal("p6");
        // We can't set ConversationId via factory, so use reflection for test
        var prop = Proposal.Create("p6", "pro1", "client1", "svc1", "Scope", 10000,
            DateTime.UtcNow.AddDays(2), conversationId: "conv1");
        await _repo.CreateAsync(prop, CancellationToken.None);
        await _repo.CreateAsync(BuildProposal("p7"), CancellationToken.None);

        var result = await _repo.GetByConversationAsync("conv1", CancellationToken.None);
        Assert.Single(result);
        Assert.Equal("p6", result[0].Id);
    }

    [Fact]
    public async Task ExpireOverdueAsync_ExpiresExpiredProposals()
    {
        var expiredProposal = Proposal.Create("p8", "pro1", "client1", "svc1", "Scope", 10000,
            DateTime.UtcNow.AddDays(-1)); // already past valid_until
        await _repo.CreateAsync(expiredProposal, CancellationToken.None);
        await _repo.SendAsync(expiredProposal.Id, CancellationToken.None);

        var count = await _repo.ExpireOverdueAsync(DateTime.UtcNow, CancellationToken.None);
        Assert.Equal(1, count);

        Ctx.ChangeTracker.Clear();
        var updated = await Ctx.Proposals.AsNoTracking().FirstAsync(p => p.Id == "p8");
        Assert.Equal(ProposalStatus.Expired, updated.Status);
    }

    [Fact]
    public async Task GetMineAsync_ReturnsProposalsForClient()
    {
        await _repo.CreateAsync(BuildProposal("pm1"), CancellationToken.None);
        await _repo.CreateAsync(
            Proposal.Create("pm2", "pro99", "other-client", "svc1", "Scope", 5000, DateTime.UtcNow.AddDays(1)),
            CancellationToken.None);

        var result = await _repo.GetMineAsync("client1", "client", CancellationToken.None);
        Assert.Single(result);
    }
}
