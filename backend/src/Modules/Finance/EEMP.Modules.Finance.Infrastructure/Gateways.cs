using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using EEMP.Modules.Finance.Application;
using Microsoft.Extensions.Options;

namespace EEMP.Modules.Finance.Infrastructure;

public sealed class FinanceGatewayOptions
{
    public const string SectionName = "Finance:Gateways";
    public FakeGatewayOptions Fake { get; set; } = new();
    public ZarinpalOptions Zarinpal { get; set; } = new();
}

public sealed class FakeGatewayOptions
{
    /// <summary>Development/Testing only. Never enable in production.</summary>
    public bool Enabled { get; set; }
    public string PaymentPageBaseUrl { get; set; } = "https://fake-gateway.invalid/pay/";
}

public sealed class ZarinpalOptions
{
    public bool Enabled { get; set; }
    /// <summary>Merchant id; supplied only through configuration/environment (Finance__Gateways__Zarinpal__MerchantId).</summary>
    public string? MerchantId { get; set; }
    public bool Sandbox { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 15;
}

public sealed class FinancePaymentOptions
{
    public const string SectionName = "Finance:Payments";
    /// <summary>Public base URL of this API used in provider callback URLs (else the request's scheme and host).</summary>
    public string? CallbackBaseUrl { get; set; }
    /// <summary>Frontend page the payer returns to after the callback (?paymentId=&amp;status=). JSON is returned when not set.</summary>
    public string? ReturnUrl { get; set; }
}

/// <summary>
/// In-memory fake provider for development and tests. "Paying" happens when the callback arrives with Status=OK; tests may
/// override what the provider reports (<see cref="Tamper"/>, <see cref="FailNext"/>) to exercise amount checks and failures.
/// </summary>
public sealed class FakePaymentGateway(IOptions<FinanceGatewayOptions> options) : IPaymentGateway
{
    private readonly ConcurrentDictionary<string, decimal> _requested = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, decimal> _tampered = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _fail = new(StringComparer.Ordinal);
    private int _verifyCalls;

    public string Name => "fake";
    public bool Enabled => options.Value.Fake.Enabled;
    public int VerifyCalls => _verifyCalls;

    public Task<GatewayRequestResult> RequestAsync(GatewayRequest request, CancellationToken ct)
    {
        var authority = "FAKE" + Guid.NewGuid().ToString("N")[..24].ToUpperInvariant();
        _requested[authority] = request.Amount;
        return Task.FromResult(new GatewayRequestResult(true, authority, null));
    }

    public string PaymentUrl(string authority) => options.Value.Fake.PaymentPageBaseUrl + authority;

    public Task<GatewayVerifyResult> VerifyAsync(string authority, decimal amount, string currency, CancellationToken ct)
    {
        Interlocked.Increment(ref _verifyCalls);
        if (!_requested.TryGetValue(authority, out var requested) || _fail.ContainsKey(authority))
            return Task.FromResult(new GatewayVerifyResult(false, null, null, "fake_declined"));
        var paid = _tampered.TryGetValue(authority, out var t) ? t : requested;
        return Task.FromResult(new GatewayVerifyResult(true, "REF" + authority[4..14], paid, null));
    }

    public void Tamper(string authority, decimal reportedAmount) => _tampered[authority] = reportedAmount;
    public void FailNext(string authority) => _fail[authority] = true;
}

/// <summary>
/// Zarinpal v4 adapter (DL-066): payment/request.json → authority, StartPay/{authority}, payment/verify.json with the stored amount.
/// Codes 100 (verified) and 101 (already verified) are success. Disabled unless configured; amounts are sent in Rials (IRR).
/// </summary>
public sealed class ZarinpalGateway(HttpClient http, IOptions<FinanceGatewayOptions> options) : IPaymentGateway
{
    private ZarinpalOptions O => options.Value.Zarinpal;
    private string Host => O.Sandbox ? "https://sandbox.zarinpal.com" : "https://payment.zarinpal.com";

    public string Name => "zarinpal";
    public bool Enabled => O.Enabled && !string.IsNullOrWhiteSpace(O.MerchantId);

    public async Task<GatewayRequestResult> RequestAsync(GatewayRequest request, CancellationToken ct)
    {
        if (request.Currency is not ("IRR" or "IRT")) return new GatewayRequestResult(false, null, "unsupported_currency");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(O.TimeoutSeconds));
        var body = new
        {
            merchant_id = O.MerchantId, amount = (long)request.Amount, currency = request.Currency, callback_url = request.CallbackUrl,
            description = request.Description, metadata = new { order_id = request.PaymentId.ToString("N") },
        };
        using var res = await http.PostAsJsonAsync($"{Host}/pg/v4/payment/request.json", body, cts.Token);
        var parsed = await res.Content.ReadFromJsonAsync<ZarinpalResponse>(cts.Token);
        return parsed?.Data is { Code: 100, Authority: { Length: > 0 } a }
            ? new GatewayRequestResult(true, a, null)
            : new GatewayRequestResult(false, null, $"zarinpal_{parsed?.Data?.Code.ToString(CultureInfo.InvariantCulture) ?? ((int)res.StatusCode).ToString(CultureInfo.InvariantCulture)}");
    }

    public string PaymentUrl(string authority) => $"{Host}/pg/StartPay/{Uri.EscapeDataString(authority)}";

    public async Task<GatewayVerifyResult> VerifyAsync(string authority, decimal amount, string currency, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(O.TimeoutSeconds));
        var body = new { merchant_id = O.MerchantId, amount = (long)amount, authority };
        using var res = await http.PostAsJsonAsync($"{Host}/pg/v4/payment/verify.json", body, cts.Token);
        var parsed = await res.Content.ReadFromJsonAsync<ZarinpalResponse>(cts.Token);
        // Zarinpal verifies the amount we send against what was paid; it does not echo an amount, so PaidAmount stays null.
        return parsed?.Data is { Code: 100 or 101 } d
            ? new GatewayVerifyResult(true, d.RefId?.ToString(CultureInfo.InvariantCulture), null, null)
            : new GatewayVerifyResult(false, null, null, $"zarinpal_{parsed?.Data?.Code.ToString(CultureInfo.InvariantCulture) ?? "error"}");
    }

    private sealed class ZarinpalResponse
    {
        [JsonPropertyName("data")] public ZarinpalData? Data { get; set; }
    }

    private sealed class ZarinpalData
    {
        [JsonPropertyName("code")] public int Code { get; set; }
        [JsonPropertyName("authority")] public string? Authority { get; set; }
        [JsonPropertyName("ref_id")] public long? RefId { get; set; }
    }
}
