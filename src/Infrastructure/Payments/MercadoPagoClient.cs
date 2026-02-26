using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Payments;

public sealed class MercadoPagoClient(HttpClient httpClient, IConfiguration configuration) : IMercadoPagoClient
{
    private readonly string _accessToken = configuration["MercadoPago:AccessToken"] ?? string.Empty;
    private readonly string _baseUrl = configuration["MercadoPago:BaseUrl"] ?? "https://api.mercadopago.com";

    public async Task<(string PreferenceId, string InitPoint)> CreatePreferenceAsync(string orderId, int amountCents, string title, CancellationToken ct)
    {
        var payload = new
        {
            items = new[] { new { title, quantity = 1, unit_price = amountCents / 100m } },
            external_reference = orderId
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/checkout/preferences");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var resp = await httpClient.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return (doc.RootElement.GetProperty("id").GetString()!, doc.RootElement.GetProperty("init_point").GetString()!);
    }

    public async Task<string> GetPaymentStatusAsync(string paymentId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/v1/payments/{paymentId}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        var resp = await httpClient.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("status").GetString() ?? "pending";
    }
}
