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
