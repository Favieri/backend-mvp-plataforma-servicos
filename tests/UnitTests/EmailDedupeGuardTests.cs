using Infrastructure.Data;
using Infrastructure.Email;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace UnitTests;

public sealed class EmailDedupeGuardTests
{
    [Fact]
    public async Task TryInsertAsync_WhenConnectionFactoryThrows_ReturnsTrue_FailOpen()
    {
        var factory = new Mock<IConnectionFactory>();
        factory
            .Setup(f => f.CreateOpenConnectionAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unavailable"));

        var result = await EmailDedupeGuard.TryInsertAsync(
            factory.Object, "cliente@example.com", "Assunto", "<p>Corpo</p>", null, "dedupe-key-1", Mock.Of<ILogger>(), CancellationToken.None);

        Assert.True(result);
    }
}
