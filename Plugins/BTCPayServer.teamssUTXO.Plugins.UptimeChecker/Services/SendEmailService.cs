using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Plugins.Emails.Services;
using BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Models;
using MimeKit;

namespace BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Services;

/// <summary>
/// Sends alert e-mails on status transitions (up → down, down → up).
/// </summary>
public class SendEmailService
{
    private readonly EmailSenderFactory _emailSenderFactory;

    public SendEmailService(EmailSenderFactory emailSenderFactory)
    {
        _emailSenderFactory = emailSenderFactory;
    }

    /// <summary>
    /// Sends a "service is DOWN" alert
    /// </summary>
    public async Task SendMailDownAsync(UptimeCheck check, UptimeCheckResult result)
    {
        if (check.NotificationEmails == null || check.NotificationEmails.Count == 0)
            return;

        var sender = await _emailSenderFactory.GetEmailSender();

        var subject = $"[DOWN] {check.Url} is unreachable";
        var body = BuildDownBody(check, result);

        foreach (var email in check.NotificationEmails)
        {
            if (string.IsNullOrWhiteSpace(email))
                continue;

            var mailbox = new MailboxAddress(email, email);
            sender.SendEmail(mailbox, subject, body);
        }
    }

    /// <summary>
    /// Sends a "service is back UP" alert
    /// </summary>
    public async Task SendMailUpAsync(UptimeCheck check, UptimeCheckResult result)
    {
        if (check.NotificationEmails == null || check.NotificationEmails.Count == 0)
            return;

        var sender = await _emailSenderFactory.GetEmailSender();

        var subject = $"[RECOVERED] {check.Url} is back online";
        var body = BuildUpBody(check, result);

        foreach (var email in check.NotificationEmails)
        {
            if (string.IsNullOrWhiteSpace(email))
                continue;

            var mailbox = new MailboxAddress(email, email);
            sender.SendEmail(mailbox, subject, body);
        }
    }

    private static string BuildDownBody(UptimeCheck check, UptimeCheckResult result)
    {
        var lines = new List<string>
        {
            $"<p>The following service has been detected as <strong>DOWN</strong>.</p>",
            $"<table>",
            $"  <tr><td><strong>URL</strong></td><td>{check.Url}</td></tr>",
            $"  <tr><td><strong>Checked at</strong></td><td>{result.CheckedAt:u}</td></tr>"
        };

        if (result.HttpStatusCode.HasValue)
            lines.Add($"  <tr><td><strong>HTTP status</strong></td><td>{result.HttpStatusCode.Value}</td></tr>");

        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            lines.Add($"  <tr><td><strong>Error</strong></td><td>{System.Net.WebUtility.HtmlEncode(result.ErrorMessage)}</td></tr>");

        lines.Add("</table>");
        lines.Add("<p>No further alert will be sent until the service recovers.</p>");

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildUpBody(UptimeCheck check, UptimeCheckResult result)
    {
        var lines = new List<string>
        {
            $"<p>The following service has <strong>recovered</strong> and is back online.</p>",
            $"<table>",
            $"  <tr><td><strong>URL</strong></td><td>{check.Url}</td></tr>",
            $"  <tr><td><strong>Recovered at</strong></td><td>{result.CheckedAt:u}</td></tr>"
        };

        if (result.HttpStatusCode.HasValue)
            lines.Add($"  <tr><td><strong>HTTP status</strong></td><td>{result.HttpStatusCode.Value}</td></tr>");

        lines.Add("</table>");

        return string.Join(Environment.NewLine, lines);
    }
}
