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
    private readonly bool _isSandbox;

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
        _isSandbox = config.GetValue<bool>("MercadoPago__IsSandbox")
                  || config.GetValue<bool>("MercadoPago:IsSandbox");

        if (_isSandbox)
            _logger.LogWarning("[MpService] SANDBOX MODE ATIVO. Pagamentos reais não serão processados.");
    }

    public async Task<MpPreferenceResult> CreatePreferenceAsync(
        CreatePreferenceRequest request,
        string professionalAccessToken,
        CancellationToken ct)
    {
        // MP processa datas em São Paulo (UTC-3) — usar DateTimeOffset com offset fixo
        var spOffset    = TimeSpan.FromHours(-3);
        var nowSp       = DateTimeOffset.UtcNow.ToOffset(spOffset);
        var expiresAtSp = nowSp.AddMinutes(60);

        static string FormatMpDate(DateTimeOffset d) =>
            d.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz");

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
                email = _isSandbox ? "test@testuser.com" : (request.PayerEmail ?? "cliente@jobeasy.com.br")
            },
            marketplace = $"MP-MKT-{_appId}",
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
            expiration_date_from = FormatMpDate(nowSp),
            expiration_date_to   = FormatMpDate(expiresAtSp),
            payment_methods = new
            {
                excluded_payment_types = new[] { new { id = "ticket" } },
                installments = request.MaxInstallments,
                default_installments = 1
            },
            statement_descriptor = "JOBEASY"
        };

        _logger.LogDebug(
            "[MpService] Creating preference. OrderId={OrderId} Amount={Amount} Fee={Fee} PayerEmail={Email} Marketplace={Marketplace}",
            request.OrderId,
            request.AmountCents / 100.0,
            request.PlatformFeeCents / 100.0,
            request.PayerEmail,
            $"MP-MKT-{_appId}");

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

        var checkoutUrl = result.InitPoint
            ?? $"https://www.mercadopago.com.br/checkout/v1/redirect?pref_id={result.Id}";

        var sandboxUrl = result.SandboxInitPoint
            ?? $"https://sandbox.mercadopago.com.br/checkout/v1/redirect?pref_id={result.Id}";

        _logger.LogInformation(
            "[MpService] Preference created. OrderId={OrderId} PreferenceId={PreferenceId} IsSandbox={IsSandbox}",
            request.OrderId, result.Id, _isSandbox);

        return new MpPreferenceResult(
            PreferenceId: result.Id,
            CheckoutUrl: checkoutUrl,
            SandboxUrl: sandboxUrl,
            ExpiresAt: expiresAtSp.UtcDateTime,
            IsSandbox: _isSandbox
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

        var gatewayFeeCents = result.FeeDetails?
            .Where(f => string.Equals(f.Type, "mercadopago_fee", StringComparison.OrdinalIgnoreCase))
            .Sum(f => (int)Math.Round(f.Amount * 100));

        var pixCode = result.PointOfInteraction?.TransactionData?.QrCode;
        var pixQrBase64 = result.PointOfInteraction?.TransactionData?.QrCodeBase64;

        return new MpPaymentDetails(
            MpPaymentId: result.Id.ToString(),
            Status: result.Status ?? "unknown",
            PaymentTypeId: result.PaymentTypeId,
            TransactionAmountCents: (int)Math.Round(result.TransactionAmount * 100),
            DateApproved: result.DateApproved,
            TransactionNetAmountCents: result.TransactionNetAmount.HasValue
                ? (int)Math.Round(result.TransactionNetAmount.Value * 100)
                : null,
            StatusDetail: result.StatusDetail,
            ExternalReference: result.ExternalReference,
            MarketplaceFeeCents: (int)Math.Round(result.MarketplaceFee * 100),
            MpGatewayFeeCents: gatewayFeeCents,
            PixCode: pixCode,
            PixQrCodeBase64: pixQrBase64,
            PixExpiresAt: result.DateOfExpiration
        );
    }

    public async Task<MpRefundResult> RefundPaymentAsync(string mpPaymentId, int? amountCents, CancellationToken ct)
    {
        using var response = await SendWithRetryAsync(
            () =>
            {
                var msg = new HttpRequestMessage(HttpMethod.Post, $"/v1/payments/{mpPaymentId}/refunds");
                msg.Headers.Add("Authorization", $"Bearer {_platformAccessToken}");
                // Omit amount for total refund; include for partial
                var body = amountCents.HasValue
                    ? (object)new { amount = amountCents.Value / 100.0 }
                    : new { };
                msg.Content = JsonContent.Create(body, options: _jsonOptions);
                return msg;
            },
            ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("[MpService] RefundPayment failed. MpPaymentId={Id} Status={Status} Body={Body}",
                mpPaymentId, response.StatusCode, body);

            var errorCode = (int)response.StatusCode == 422 ? "refund_window_expired" : "gateway_error";
            return new MpRefundResult(Success: false, RefundId: null, ErrorCode: errorCode);
        }

        var result = await response.Content.ReadFromJsonAsync<MpRefundApiResponse>(ct);
        var refundId = result?.Id.ToString();

        _logger.LogInformation("[MpService] Refund created. MpPaymentId={Id} RefundId={RefundId} AmountCents={Amount}",
            mpPaymentId, refundId, amountCents);

        return new MpRefundResult(Success: true, RefundId: refundId, ErrorCode: null);
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

        [JsonPropertyName("init_point")]
        public string? InitPoint { get; init; }

        [JsonPropertyName("sandbox_init_point")]
        public string? SandboxInitPoint { get; init; }
    }

    private sealed class MpPaymentApiResponse
    {
        [JsonPropertyName("id")]
        public long Id { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("status_detail")]
        public string? StatusDetail { get; init; }

        [JsonPropertyName("payment_type_id")]
        public string? PaymentTypeId { get; init; }

        [JsonPropertyName("external_reference")]
        public string? ExternalReference { get; init; }

        [JsonPropertyName("transaction_amount")]
        public double TransactionAmount { get; init; }

        [JsonPropertyName("transaction_net_amount")]
        public double? TransactionNetAmount { get; init; }

        [JsonPropertyName("marketplace_fee")]
        public double MarketplaceFee { get; init; }

        [JsonPropertyName("fee_details")]
        public MpFeeDetail[]? FeeDetails { get; init; }

        [JsonPropertyName("date_approved")]
        public DateTime? DateApproved { get; init; }

        [JsonPropertyName("date_of_expiration")]
        public DateTime? DateOfExpiration { get; init; }

        [JsonPropertyName("point_of_interaction")]
        public MpPointOfInteraction? PointOfInteraction { get; init; }
    }

    private sealed class MpFeeDetail
    {
        [JsonPropertyName("type")]
        public string? Type { get; init; }

        [JsonPropertyName("amount")]
        public double Amount { get; init; }
    }

    private sealed class MpPointOfInteraction
    {
        [JsonPropertyName("transaction_data")]
        public MpTransactionData? TransactionData { get; init; }
    }

    private sealed class MpTransactionData
    {
        [JsonPropertyName("qr_code")]
        public string? QrCode { get; init; }

        [JsonPropertyName("qr_code_base64")]
        public string? QrCodeBase64 { get; init; }
    }

    private sealed class MpRefundApiResponse
    {
        [JsonPropertyName("id")]
        public long Id { get; init; }
    }
}

public sealed class MpGatewayException : Exception
{
    public MpGatewayException(string message) : base(message) { }
}
