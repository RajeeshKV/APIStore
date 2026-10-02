using KromicCommerce.Application.Abstractions.Sms;

namespace KromicCommerce.UnitTests.Infrastructure.Sms;

/// <summary>
/// An <see cref="ISmsOtpAuditSink"/> that discards everything.
/// </summary>
/// <remarks>
/// For provider tests whose subject is the wire contract rather than the audit trail. Using this
/// rather than a no-op lambda keeps those tests independent of the auditing abstraction, so a
/// change to the audit record cannot fail them for the wrong reason. Tests that do assert on
/// auditing capture <c>SmsOtpAudit</c> values into a list instead.
/// </remarks>
internal sealed class NullSmsOtpAuditSink : ISmsOtpAuditSink
{
    public void Record(in SmsOtpAudit audit)
    {
    }
}
