using System.Text.Json.Nodes;
using Kiosk.Application.Abstractions;
using Kiosk.Application.Payments;
using Kiosk.Infrastructure.Payments;
using Kiosk.Infrastructure.Persistence;
using Kiosk.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Kiosk.UnitTests;

public class SlipTokenTests
{
    private static readonly FakeTimeProvider Time = new(new DateTimeOffset(2026, 10, 8, 4, 0, 0, TimeSpan.Zero));

    private static HmacSlipTokenService Service(string key = "+NjCCawqMZ2kIzdYjqC56VwEzD1lHaSIUGmVGRjQnNU=") =>
        new(Options.Create(new SlipOptions { SigningKey = key }), Time);

    [Fact]
    public void Round_trips_and_stays_short()
    {
        var id = Guid.NewGuid();
        var token = Service().Create(id, Time.GetUtcNow().AddMinutes(15));
        Assert.True(token.Length < 60, token);
        Assert.Equal(id, Service().Read(token));
    }

    [Fact]
    public void Tampered_token_is_rejected()
    {
        var token = Service().Create(Guid.NewGuid(), Time.GetUtcNow().AddMinutes(15));
        var tampered = (token[0] == 'A' ? 'B' : 'A') + token[1..];
        Assert.Null(Service().Read(tampered));
    }

    [Fact]
    public void Token_signed_with_another_key_is_rejected()
    {
        var other = Service(Convert.ToBase64String(new byte[32]));
        var token = other.Create(Guid.NewGuid(), Time.GetUtcNow().AddMinutes(15));
        Assert.Null(Service().Read(token));
    }

    [Fact]
    public void Expired_token_is_rejected()
    {
        var token = Service().Create(Guid.NewGuid(), Time.GetUtcNow().AddMinutes(-1));
        Assert.Null(Service().Read(token));
    }

    [Theory]
    [InlineData("+NjCCawqMZ2kIzdYjqC56VwEzD1lHaSIUGmVGRjQnNU=", true)] // 32 bytes
    [InlineData("c2hvcnQ=", false)] // "short"
    [InlineData("not base64!", false)]
    [InlineData("", false)]
    public void Signing_key_must_be_base64_of_at_least_32_bytes(string key, bool valid) =>
        Assert.Equal(valid, SlipOptions.IsValidKey(key));

    [Theory]
    [InlineData("")]
    [InlineData("A-101")]
    [InlineData("not.a-token")]
    public void Garbage_is_rejected(string input) => Assert.Null(Service().Read(input));
}

public class PayMongoWebhookParserTests
{
    private const string Secret = "whsk_test_secret";
    private static readonly PayMongoWebhookParser Parser = new(Options.Create(new PayMongoOptions { WebhookSecret = Secret }));

    private static string PaidBody(bool live = false) => """
        {"data":{"id":"evt_123","type":"event","attributes":{"type":"checkout_session.payment.paid","livemode":LIVE,
        "data":{"id":"cs_abc","type":"checkout_session","attributes":{"payments":[{"id":"pay_1","attributes":{"amount":28500,"status":"paid"}}]}}}}}
        """.Replace("LIVE", live ? "true" : "false");

    [Fact]
    public void Valid_test_signature_parses_the_paid_event()
    {
        var body = PaidBody();
        var evt = Parser.Parse(body, $"t=1700000000,te={PayMongoWebhookParser.Sign(body, 1700000000, Secret)},li=");

        Assert.NotNull(evt);
        Assert.Equal("evt_123", evt.EventId);
        Assert.Equal("cs_abc", evt.ProviderRef);
        Assert.Equal(285.00m, evt.Amount);
        Assert.True(evt.Succeeded);
    }

    [Fact]
    public void Live_event_must_match_the_li_signature()
    {
        var body = PaidBody(live: true);
        var sig = PayMongoWebhookParser.Sign(body, 1700000000, Secret);
        Assert.Throws<InvalidWebhookSignatureException>(() => Parser.Parse(body, $"t=1700000000,te={sig},li="));
        Assert.NotNull(Parser.Parse(body, $"t=1700000000,te=,li={sig}"));
    }

    [Fact]
    public void Modified_body_fails_verification()
    {
        var body = PaidBody();
        var sig = PayMongoWebhookParser.Sign(body, 1700000000, Secret);
        Assert.Throws<InvalidWebhookSignatureException>(() =>
            Parser.Parse(body.Replace("28500", "1"), $"t=1700000000,te={sig}"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("te=abc")]
    [InlineData("t=1700000000,te=deadbeef")]
    public void Missing_or_wrong_signature_is_rejected(string? header) =>
        Assert.Throws<InvalidWebhookSignatureException>(() => Parser.Parse(PaidBody(), header));

    [Fact]
    public void Unhandled_event_types_return_null()
    {
        var body = PaidBody().Replace("checkout_session.payment.paid", "source.chargeable");
        Assert.Null(Parser.Parse(body, $"t=1,te={PayMongoWebhookParser.Sign(body, 1, Secret)}"));
    }

    private static string PaymentBody(string type, string intent = "\"payment_intent_id\":\"pi_abc\",") => """
        {"data":{"id":"evt_456","type":"event","attributes":{"type":"TYPE","livemode":false,
        "data":{"id":"pay_2","type":"payment","attributes":{"amount":14900,"status":"x",INTENT"failed_code":"card_declined","failed_message":"Card declined"}}}}}
        """.Replace("TYPE", type).Replace("INTENT", intent);

    private static PaymentEvent? ParseSigned(string body) =>
        Parser.Parse(body, $"t=1,te={PayMongoWebhookParser.Sign(body, 1, Secret)}");

    [Fact]
    public void Payment_paid_is_matched_by_its_payment_intent()
    {
        var evt = ParseSigned(PaymentBody("payment.paid"));

        Assert.NotNull(evt);
        Assert.Equal("pi_abc", evt.ProviderRef);
        Assert.Equal(149.00m, evt.Amount);
        Assert.True(evt.Succeeded);
        Assert.Null(evt.FailureReason);
    }

    [Fact]
    public void Payment_failed_carries_the_reason()
    {
        var evt = ParseSigned(PaymentBody("payment.failed"));

        Assert.NotNull(evt);
        Assert.False(evt.Succeeded);
        Assert.Equal("Card declined", evt.FailureReason);
    }

    [Fact]
    public void A_payment_without_an_intent_is_left_to_its_checkout_session_event() =>
        Assert.Null(ParseSigned(PaymentBody("payment.paid", intent: "")));
}

public class PayMongoIntentTests
{
    private static IntentResult Parse(string attributes) =>
        PayMongoGateway.ParseIntent(JsonNode.Parse("""{"id":"pi_1","attributes":{"amount":18900,ATTRS}}""".Replace("ATTRS", attributes)));

    [Fact]
    public void Succeeded_intent_is_paid_with_its_amount()
    {
        var result = Parse("\"status\":\"succeeded\"");
        Assert.Equal((IntentStatus.Succeeded, 189.00m), (result.Status, result.Amount));
    }

    [Fact]
    public void Awaiting_next_action_hands_over_the_3ds_page()
    {
        var result = Parse("""
            "status":"awaiting_next_action","next_action":{"type":"redirect","redirect":{"url":"https://bank.test/3ds","return_url":"x"}}
            """);
        Assert.Equal((IntentStatus.NeedsAction, "https://bank.test/3ds"), (result.Status, result.ActionUrl));
    }

    [Fact]
    public void A_failed_attempt_is_a_failure_with_its_message()
    {
        var result = Parse("""
            "status":"awaiting_payment_method","last_payment_error":{"failed_code":"insufficient_funds","failed_message":"Insufficient funds"}
            """);
        Assert.Equal((IntentStatus.Failed, "Insufficient funds"), (result.Status, result.FailureReason));
    }

    [Fact]
    public void A_fresh_intent_is_still_pending() =>
        Assert.Equal(IntentStatus.Pending, Parse("\"status\":\"awaiting_payment_method\",\"last_payment_error\":null").Status);

    [Theory]
    [InlineData("iVBORw0KGgo", "data:image/png;base64,iVBORw0KGgo")]
    [InlineData("data:image/png;base64,iVBORw0KGgo", "data:image/png;base64,iVBORw0KGgo")]
    [InlineData("https://cdn.paymongo.test/qr.png", "https://cdn.paymongo.test/qr.png")]
    public void QR_image_is_always_an_img_source(string given, string expected) =>
        Assert.Equal(expected, PayMongoGateway.AsImageSource(given));
}

public class PostgresConnectionStringTests
{
    [Fact]
    public void Neon_url_is_converted_to_npgsql_format()
    {
        var result = new Npgsql.NpgsqlConnectionStringBuilder(PostgresConnectionString.Normalize(
            "postgresql://neondb_owner:p%40ss:w0rd@ep-x-pooler.c-4.ap-southeast-1.aws.neon.tech/neondb?sslmode=require&channel_binding=require"));

        Assert.Equal("ep-x-pooler.c-4.ap-southeast-1.aws.neon.tech", result.Host);
        Assert.Equal(5432, result.Port);
        Assert.Equal("neondb", result.Database);
        Assert.Equal("neondb_owner", result.Username);
        Assert.Equal("p@ss:w0rd", result.Password);
        Assert.Equal(Npgsql.SslMode.Require, result.SslMode);
        Assert.Equal(Npgsql.ChannelBinding.Require, result.ChannelBinding);
    }

    [Fact]
    public void Url_port_and_verify_full_are_kept()
    {
        var result = new Npgsql.NpgsqlConnectionStringBuilder(
            PostgresConnectionString.Normalize("postgres://u:p@db.example.com:6543/app?sslmode=verify-full"));
        Assert.Equal(6543, result.Port);
        Assert.Equal(Npgsql.SslMode.VerifyFull, result.SslMode);
    }

    [Fact]
    public void Keyword_format_passes_through_unchanged()
    {
        const string cs = "Host=localhost;Port=5433;Database=kiosk;Username=kiosk;Password=kiosk";
        Assert.Equal(cs, PostgresConnectionString.Normalize(cs));
    }
}
