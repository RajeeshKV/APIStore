using KromicCommerce.Application.Abstractions.Auth;
using KromicCommerce.Application.Abstractions.Sms;
using KromicCommerce.Application.Features.Auth.SendOtp;
using KromicCommerce.Application.Options;
using KromicCommerce.Domain.Sms;
using KromicCommerce.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KromicCommerce.IntegrationTests.Sms;

/// <summary>
/// Proves the resend mutex actually holds under real concurrency in PostgreSQL.
///
/// The unit test in <c>SendOtpHandlerTests</c> models the UNIQUE index with an in-memory stand-in.
/// That proves the handler uses the claim; this proves the claim works: a genuine UNIQUE
/// constraint under genuine concurrent INSERTs. If the index or the ON CONFLICT clause regresses,
/// this fails even while the unit tests stay green.
///
/// Skipped without Docker; see <see cref="IntegrationTestBase"/>.
/// </summary>
[Collection("Database")]
public sealed class OtpResendRaceTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private const string PhoneNumber = "+919876543210";

    /// <summary>
    /// Concurrent resends are a normal customer behaviour, not an exotic one: a double tap on
    /// "resend", or a client retrying a request whose response was lost. Each one costs money and
    /// leaves the customer with several live codes.
    /// </summary>
    [SkippableTheory]
    [InlineData(2)]
    [InlineData(6)]
    public async Task Simultaneous_resends_deliver_exactly_one_code(int concurrentRequests)
    {
        var provider = new CountingSmsProvider();

        // A separate context per request, because in production each request is served by its own
        // scoped context and its own connection. Sharing one context would serialise on the change
        // tracker and quietly hide the race.
        var attempts = Enumerable.Range(0, concurrentRequests).Select(_ => Task.Run(async () =>
        {
            await using var ctx = Db.CreateDbContext();
            var handler = new SendOtpHandler(
                ctx,
                NewOtpService(),
                new StubFactory(provider),
                Policy(),
                NullLogger<SendOtpHandler>.Instance);

            return await handler.Handle(
                new SendOtpCommand(PhoneNumber, OtpPurpose.PhoneVerification, Guid.NewGuid()),
                CancellationToken.None);
        })).ToArray();

        var results = await Task.WhenAll(attempts);

        // The single most important assertion: one SMS, not N.
        provider.SendCount.Should().Be(
            1,
            "the (PhoneNumber, Purpose) claim must serialise simultaneous resends into one send");

        // And exactly one code was persisted, so the cooldown and verification logic see one code.
        await using var verify = Db.CreateDbContext();
        var stored = await verify.OtpRequests
            .Where(o => o.PhoneNumber == PhoneNumber)
            .ToListAsync();
        stored.Should().ContainSingle();

        // Losing the race is a cooldown, never a hard failure: the customer is told to wait, not
        // that the code could not be sent.
        results.Should().ContainSingle(r => r.IsSuccess);
        results.Skip(1).Should().OnlyContain(r => r.Error.Code == "OTP_COOLDOWN");
    }

    /// <summary>
    /// The claim is a mutex, not a cooldown record. It must not stop a legitimate later resend,
    /// and it must not leave the customer locked out if the winning request crashed before its
    /// release, since a claim left behind is reclaimed once stale.
    /// </summary>
    [SkippableFact]
    public async Task A_claim_is_released_so_the_next_resend_proceeds()
    {
        var provider = new CountingSmsProvider();

        await using (var first = Db.CreateDbContext())
        {
            await first.TryAcquireOtpSendClaimAsync(
                PhoneNumber, OtpPurpose.PhoneVerification, DateTime.UtcNow.AddMinutes(-5));
        }

        // While the claim is held, a second request is turned away.
        await using (var blocked = Db.CreateDbContext())
        {
            var acquired = await blocked.TryAcquireOtpSendClaimAsync(
                PhoneNumber, OtpPurpose.PhoneVerification, DateTime.UtcNow.AddMinutes(-5));
            acquired.Should().BeFalse();
        }

        // Released, so it can be taken again straight away.
        await using (var release = Db.CreateDbContext())
        {
            await release.ReleaseOtpSendClaimAsync(
                PhoneNumber, OtpPurpose.PhoneVerification, CancellationToken.None);
        }

        await using var next = Db.CreateDbContext();
        var reacquired = await next.TryAcquireOtpSendClaimAsync(
            PhoneNumber, OtpPurpose.PhoneVerification, DateTime.UtcNow.AddMinutes(-5));
        reacquired.Should().BeTrue();
    }

    /// <summary>
    /// A request that dies mid-send leaves its claim behind. Once it is older than the staleness
    /// window the next resend must succeed anyway, so a crash cannot wedge a customer's OTP flow
    /// until they give up.
    /// </summary>
    [SkippableFact]
    public async Task A_claim_abandoned_by_a_crash_is_reclaimed_once_stale()
    {
        var phone = "+919876500001";

        await using (var crashed = Db.CreateDbContext())
        {
            (await crashed.TryAcquireOtpSendClaimAsync(
                phone, OtpPurpose.PhoneVerification, DateTime.UtcNow.AddMinutes(-5))).Should().BeTrue();
        }

        // No release, simulating a process that died inside the send.

        // A fresh claim is refused: within the staleness window the winner may still be sending.
        await using (var tooSoon = Db.CreateDbContext())
        {
            (await tooSoon.TryAcquireOtpSendClaimAsync(
                phone, OtpPurpose.PhoneVerification, DateTime.UtcNow.AddMinutes(-5)))
                .Should().BeFalse();
        }

        // Past the window, a claim nobody will ever release stops blocking.
        await using (var later = Db.CreateDbContext())
        {
            (await later.TryAcquireOtpSendClaimAsync(
                phone,
                OtpPurpose.PhoneVerification,
                DateTime.UtcNow.AddHours(-1)))
                .Should().BeTrue();
        }
    }

    /// <summary>A different purpose is a different claim, so registration and login never block.</summary>
    [SkippableFact]
    public async Task Claims_are_scoped_per_purpose()
    {
        var phone = "+919876500002";

        await using var ctx = Db.CreateDbContext();
        (await ctx.TryAcquireOtpSendClaimAsync(
            phone, OtpPurpose.PhoneVerification, DateTime.UtcNow.AddMinutes(-5))).Should().BeTrue();

        (await ctx.TryAcquireOtpSendClaimAsync(
            phone, OtpPurpose.PasswordReset, DateTime.UtcNow.AddMinutes(-5))).Should().BeTrue();

        await ctx.ReleaseOtpSendClaimAsync(phone, OtpPurpose.PhoneVerification, CancellationToken.None);
    }

    private static IOptions<SmsPolicyOptions> Policy()
        => Options.Create(new SmsPolicyOptions
        {
            ExpiryMinutes = 10,
            ResendCooldownSeconds = 60,
            MaxAttempts = 5,
            Length = SmsOtpDefaults.Length
        });

    private static IOtpService NewOtpService() => new StubOtpService();

    private sealed class StubOtpService : IOtpService
    {
        private int _counter;

        public string GenerateOtp(int length)
        {
            // Distinct codes, so a duplicate send would be visible in the stored hashes.
            var value = Interlocked.Increment(ref _counter).ToString();
            return value.PadLeft(length, '0')[..length];
        }

        public string HashOtp(string otp) => $"hash:{otp}";

        public bool VerifyOtp(string submittedOtp, string storedHash)
            => storedHash == HashOtp(submittedOtp);
    }

    private sealed class CountingSmsProvider : ISmsProvider
    {
        private int _sends;

        public string ProviderName => "Counting";

        public SmsProviderKind Kind => SmsProviderKind.Twilio;

        public bool IsOperational => true;

        public int SendCount => Volatile.Read(ref _sends);

        public Task<SmsSendResult> SendOtpAsync(
            string phoneNumber, string otp, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _sends);
            return Task.FromResult(new SmsSendResult(true, "msg-1", null, null, false));
        }
    }

    private sealed class StubFactory(ISmsProvider provider) : ISmsProviderFactory
    {
        public Task<SmsProviderStatus> GetStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new SmsProviderStatus(
                Enabled: true,
                Provider: SmsProviderKind.Twilio,
                IsConfigured: true,
                MissingSettings: []));

        public Task<ISmsProvider> CreateAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(provider);
    }
}