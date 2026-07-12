using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Infrastructure.Data;
using Infrastructure.Email;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace UnitTests;

public sealed class SesEmailServiceTests : IDisposable
{
    private readonly Mock<IAmazonSimpleEmailServiceV2> _sesClient = new();
    private readonly Mock<IConnectionFactory> _connectionFactory = new();
    private readonly SesEmailService _sut;

    public SesEmailServiceTests()
    {
        _sut = new SesEmailService(_sesClient.Object, _connectionFactory.Object, Mock.Of<ILogger<SesEmailService>>());
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("EMAIL_ENABLED", null);
        Environment.SetEnvironmentVariable("EMAIL_FROM", null);
    }

    [Fact]
    public async Task SendAsync_WhenEmailDisabled_DoesNotCallSesClient()
    {
        Environment.SetEnvironmentVariable("EMAIL_ENABLED", "false");

        await _sut.SendAsync("cliente@example.com", "Assunto", "<p>Corpo</p>");

        _sesClient.Verify(c => c.SendEmailAsync(It.IsAny<SendEmailRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendAsync_BuildsRequestWithFromToSubjectAndContent_AndCallsSesClient()
    {
        Environment.SetEnvironmentVariable("EMAIL_FROM", "Jobeasy <naoresponda@jobeasy.com.br>");
        SendEmailRequest? captured = null;
        _sesClient
            .Setup(c => c.SendEmailAsync(It.IsAny<SendEmailRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SendEmailRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new SendEmailResponse { MessageId = "msg-123" });

        await _sut.SendAsync("cliente@example.com", "Assunto de teste", "<p>Corpo</p>", text: "Corpo texto");

        Assert.NotNull(captured);
        Assert.Equal("Jobeasy <naoresponda@jobeasy.com.br>", captured!.FromEmailAddress);
        Assert.Equal(["cliente@example.com"], captured.Destination.ToAddresses);
        Assert.Equal("Assunto de teste", captured.Content.Simple.Subject.Data);
        Assert.Equal("<p>Corpo</p>", captured.Content.Simple.Body.Html.Data);
        Assert.Equal("Corpo texto", captured.Content.Simple.Body.Text.Data);
    }

    [Fact]
    public async Task SendAsync_WithoutDedupeKey_NeverTouchesConnectionFactory()
    {
        _sesClient
            .Setup(c => c.SendEmailAsync(It.IsAny<SendEmailRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendEmailResponse { MessageId = "msg-123" });

        await _sut.SendAsync("cliente@example.com", "Assunto", "<p>Corpo</p>");

        _connectionFactory.Verify(f => f.CreateOpenConnectionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendAsync_WhenSesClientThrows_DoesNotPropagate()
    {
        _sesClient
            .Setup(c => c.SendEmailAsync(It.IsAny<SendEmailRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonSimpleEmailServiceV2Exception("mail rejected"));

        var exception = await Record.ExceptionAsync(() =>
            _sut.SendAsync("cliente@example.com", "Assunto", "<p>Corpo</p>"));

        Assert.Null(exception);
    }

    [Fact]
    public async Task SendPasswordResetAsync_SendsExpectedSubject()
    {
        SendEmailRequest? captured = null;
        _sesClient
            .Setup(c => c.SendEmailAsync(It.IsAny<SendEmailRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SendEmailRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new SendEmailResponse { MessageId = "msg-123" });

        await _sut.SendPasswordResetAsync("cliente@example.com", "Maria", "https://jobeasy.com.br/reset?token=abc");

        Assert.NotNull(captured);
        Assert.Equal("Redefinir sua senha na Jobeasy", captured!.Content.Simple.Subject.Data);
        Assert.Contains("Maria", captured.Content.Simple.Body.Html.Data);
    }
}
