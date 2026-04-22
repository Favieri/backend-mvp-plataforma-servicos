using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public sealed class MercadoPagoService : IMercadoPagoService
{
    private readonly HttpClient _client;
    private readonly ILogger<MercadoPagoService> _logger;

    private readonly string _appId;
    private readonly string _platformAccessToken;
    private readonly string _frontendBaseUrl;
    private readonly string _apiBaseUrl;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public MercadoPagoService(
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<MercadoPagoService> logger)
    {
        _client = httpClientFactory.CreateClient("mercadopago");
        _logger = logger;
        _appId = config["MercadoPago__AppId"] ?? config["MercadoPago:AppId"] ?? "";
        _platformAccessToken = config["MercadoPago__AccessToken"] ?? config["MercadoPago:AccessToken"] ?? "";
        _frontendBaseUrl = config["MercadoPago__FrontendBaseUrl"] ?? config["MercadoPago:FrontendBaseUrl"] ?? "";
        _apiBaseUrl = config["MercadoPago__ApiBaseUrl"] ?? config["MercadoPago:ApiBaseUrl"] ?? "";
    }

    public async Task<MpPreferenceResult> CreatePreferenceAsync(
        CreatePreferenceRequest request,
        string professionalAccessToken,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var expiresAt = now.AddMinutes(30);

        var payload = new
        {
            items = new[]
            {
                new
                {
                    id = request.OrderId,
                    title = $"{request.ServiceName} — Jobeasy",
                    description = "Serviço profissional via Jobeasy",
                    category_id = "services",
                    quantity = 1,
                    currency_id = "BRL",
                    unit_price = request.AmountCents / 100.0
                }
            },
            payer = new
            {
                email = request.PayerEmail ?? "cliente@jobeasy.com.br"
            },
            marketplace = _appId,
            marketplace_fee = request.PlatformFeeCents / 100.0,
            back_urls = new
            {
                success = request.BackUrlSuccess,
                failure = request.BackUrlFailure,
                pending = request.BackUrlPending
            },
            auto_return = "approved",
            notification_url = request.NotificationUrl,
            external_reference = request.OrderId,
            expires = true,
            expiration_date_from = now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz"),
            expiration_date_to = expiresAt.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz"),
            payment_methods = new
            {
                excluded_payment_types = new[] { new { id = "ticket" } },
                installments = request.MaxInstallments,
                default_installments = 1
            },
            statement_descriptor = "JOBEASY"
        };

        using var response = await SendWithRetryAsync(
            () =>
            {
                var msg = new HttpRequestMessage(HttpMethod.Post, "/checkout/preferences");
                msg.Headers.Add("Authorization", $"Bearer {professionalAccessToken}");
                msg.Headers.Add("X-Idempotency-Key", request.OrderId);
                msg.Content = JsonContent.Create(payload, options: _jsonOptions);
                return msg;
            },
            ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("[MpService] CreatePreference failed. OrderId={OrderId} Status={Status} Body={Body}",
                request.OrderId, response.StatusCode, body);
            throw new MpGatewayException($"MP preference creation failed: {response.StatusCode}");
        }

        var result = await response.Content.ReadFromJsonAsync<MpPreferenceApiResponse>(ct)
            ?? throw new MpGatewayException("Empty response from MP preferences endpoint");

        _logger.LogInformation("[MpService] Preference created. OrderId={OrderId} PreferenceId={PreferenceId}",
            request.OrderId, result.Id);

        return new MpPreferenceResult(
            PreferenceId: result.Id,
            CheckoutUrl: $"https://www.mercadopago.com.br/checkout/v1/redirect?pref_id={result.Id}",
            SandboxUrl: $"https://sandbox.mercadopago.com.br/checkout/v1/redirect?pref_id={result.Id}",
            ExpiresAt: expiresAt
        );
    }

    public async Task<MpPaymentDetails?> GetPaymentDetailsAsync(string mpPaymentId, CancellationToken ct)
    {
        using var response = await SendWithRetryAsync(
            () =>
            {
                var msg = new HttpRequestMessage(HttpMethod.Get, $"/v1/payments/{mpPaymentId}");
                msg.Headers.Add("Authorization", $"Bearer {_platformAccessToken}");
                return msg;
            },
            ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("[MpService] GetPaymentDetails failed. MpPaymentId={Id} Status={Status}",
                mpPaymentId, response.StatusCode);
            return null;
        }

        var result = await response.Content.ReadFromJsonAsync<MpPaymentApiResponse>(ct);
        if (result is null) return null;

        return new MpPaymentDetails(
            MpPaymentId: result.Id.ToString(),
            Status: result.Status ?? "unknown",
            PaymentTypeId: result.PaymentTypeId,
            TransactionAmountCents: (int)Math.Round(result.TransactionAmount * 100),
            DateApproved: result.DateApproved,
            TransactionNetAmountCents: result.TransactionNetAmount.HasValue
                ? (int)Math.Round(result.TransactionNetAmount.Value * 100)
                : null
        );
    }

    public async Task<bool> RefundPaymentAsync(string mpPaymentId, CancellationToken ct)
    {
        using var response = await SendWithRetryAsync(
            () =>
            {
                var msg = new HttpRequestMessage(HttpMethod.Post, $"/v1/payments/{mpPaymentId}/refunds");
                msg.Headers.Add("Authorization", $"Bearer {_platformAccessToken}");
                msg.Content = JsonContent.Create(new { }, options: _jsonOptions);
                return msg;
            },
            ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("[MpService] RefundPayment failed. MpPaymentId={Id} Status={Status}",
                mpPaymentId, response.StatusCode);
            return false;
        }

        return true;
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken ct)
    {
        int[] delaySeconds = [2, 4, 8];
        const int maxAttempts = 3;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (attempt > 0)
            {
                _logger.LogWarning("[MpService] Retrying MP request (attempt {Attempt}/{Max})", attempt + 1, maxAttempts);
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds[attempt - 1]), ct);
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            var request = requestFactory();
            HttpResponseMessage response;
            try
            {
                response = await _client.SendAsync(request, cts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning("[MpService] Request timed out on attempt {Attempt}", attempt + 1);
                if (attempt == maxAttempts - 1) throw new MpGatewayException("MP API timed out after 3 attempts");
                continue;
            }

            if ((int)response.StatusCode < 500) return response;

            _logger.LogWarning("[MpService] MP returned {Status} on attempt {Attempt}", response.StatusCode, attempt + 1);
            response.Dispose();
        }

        throw new MpGatewayException("MP API returned 5xx after 3 attempts");
    }

    // ─── Internal response models ──────────────────────────────────────────────

    private sealed class MpPreferenceApiResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = "";
    }

    private sealed class MpPaymentApiResponse
    {
        [JsonPropertyName("id")]
        public long Id { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("payment_type_id")]
        public string? PaymentTypeId { get; init; }

        [JsonPropertyName("transaction_amount")]
        public double TransactionAmount { get; init; }

        [JsonPropertyName("transaction_net_amount")]
        public double? TransactionNetAmount { get; init; }

        [JsonPropertyName("date_approved")]
        public DateTime? DateApproved { get; init; }
    }
}

public sealed class MpGatewayException : Exception
{
    public MpGatewayException(string message) : base(message) { }
}
