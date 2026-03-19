using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Plugins.Emails;
using BTCPayServer.Plugins.Emails.Services;
using BTCPayServer.Services;
using BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Models;
using MimeKit;

namespace BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Services;

/// <summary>
/// Sends alert e-mails on status transitions (up → down, down → up).
/// </summary>
public class SendEmailService
{
    private readonly EmailSenderFactory _emailSenderFactory;
    private readonly ISettingsAccessor<ServerSettings> _serverSettings;

    // BTCPay Server logo
    private const string BtcPayLogoSvg =
        "<svg xmlns='http://www.w3.org/2000/svg' width='24' height='24' viewBox='0 0 24 24' fill='none' aria-label='BTCPay Server'>" +
        "<g fill='#51b13e' transform='matrix(0.27159594,0,0,0.27159594,5.6093989,0.60189406)'>" +
        "<path d='M 5.206,83.433 A 4.86,4.86 0 0 1 0.347,78.572 V 5.431 a 4.86,4.86 0 1 1 9.719,0 v 73.141 a 4.861,4.861 0 0 1 -4.86,4.861'/>" +
        "<path d='M 5.209,83.433 A 4.862,4.862 0 0 1 3.123,74.18 L 32.43,60.274 2.323,38.093 a 4.861,4.861 0 0 1 5.766,-7.826 l 36.647,26.999 a 4.864,4.864 0 0 1 -0.799,8.306 L 7.289,82.964 a 4.866,4.866 0 0 1 -2.08,0.469'/>" +
        "<path d='M 5.211,54.684 A 4.86,4.86 0 0 1 2.324,45.91 L 32.43,23.73 3.123,9.821 A 4.861,4.861 0 0 1 7.289,1.037 l 36.648,17.394 a 4.86,4.86 0 0 1 0.799,8.305 l -36.647,27 a 4.844,4.844 0 0 1 -2.878,0.948'/>" +
        "<path d='M 10.066,31.725 V 52.278 L 24.01,42.006 Z'/>" +
        "<path d='M 10.066,5.431 A 4.861,4.861 0 0 0 5.206,0.57 4.86,4.86 0 0 0 0.347,5.431 v 61.165 h 9.72 V 5.431 Z'/>" +
        "</g></svg>";

    public SendEmailService(EmailSenderFactory emailSenderFactory, ISettingsAccessor<ServerSettings> serverSettings)
    {
        _emailSenderFactory = emailSenderFactory;
        _serverSettings = serverSettings;
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

        foreach (var email in check.NotificationEmails)
        {
            if (string.IsNullOrWhiteSpace(email))
                continue;

            var body = BuildDownBody(result, email);
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

        foreach (var email in check.NotificationEmails)
        {
            if (string.IsNullOrWhiteSpace(email))
                continue;

            var body = BuildUpBody(result, email);
            var mailbox = new MailboxAddress(email, email);
            sender.SendEmail(mailbox, subject, body);
        }
    }

    private string BuildDownBody(UptimeCheckResult result, string recipientEmail)
    {
        var rows = new List<string>
        {
            $"  <tr><td style='padding:4px 12px 4px 0;font-weight:bold'>URL</td><td style='padding:4px 0'>{System.Net.WebUtility.HtmlEncode(result.Url)}</td></tr>",
            $"  <tr><td style='padding:4px 12px 4px 0;font-weight:bold'>Checked at</td><td style='padding:4px 0'>{result.CheckedAt:u}</td></tr>"
        };

        if (result.HttpStatusCode.HasValue)
            rows.Add($"  <tr><td style='padding:4px 12px 4px 0;font-weight:bold'>HTTP status</td><td style='padding:4px 0'>{result.HttpStatusCode.Value}</td></tr>");

        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            rows.Add($"  <tr><td style='padding:4px 12px 4px 0;font-weight:bold'>Error</td><td style='padding:4px 0'>{System.Net.WebUtility.HtmlEncode(result.ErrorMessage)}</td></tr>");

        var content =
            $"<p>The following service has been detected as <strong style='color:#c0392b'>DOWN</strong>.</p>" +
            $"<table style='border-collapse:collapse;margin:12px 0'>{string.Join(Environment.NewLine, rows)}</table>" +
            $"<p style='color:#555;font-size:0.9em'>No further alert will be sent until the service recovers.</p>";

        return EmailsPlugin.CreateEmailBody(content + BuildFooter(recipientEmail));
    }

    private string BuildUpBody(UptimeCheckResult result, string recipientEmail)
    {
        var rows = new List<string>
        {
            $"  <tr><td style='padding:4px 12px 4px 0;font-weight:bold'>URL</td><td style='padding:4px 0'>{System.Net.WebUtility.HtmlEncode(result.Url)}</td></tr>",
            $"  <tr><td style='padding:4px 12px 4px 0;font-weight:bold'>Recovered at</td><td style='padding:4px 0'>{result.CheckedAt:u}</td></tr>"
        };

        if (result.HttpStatusCode.HasValue)
            rows.Add($"  <tr><td style='padding:4px 12px 4px 0;font-weight:bold'>HTTP status</td><td style='padding:4px 0'>{result.HttpStatusCode.Value}</td></tr>");

        var content =
            $"<p>The following service has <strong style='color:#27ae60'>recovered</strong> and is back online.</p>" +
            $"<table style='border-collapse:collapse;margin:12px 0'>{string.Join(Environment.NewLine, rows)}</table>";

        return EmailsPlugin.CreateEmailBody(content + BuildFooter(recipientEmail));
    }

    private string BuildFooter(string recipientEmail)
    {
        var settings = _serverSettings.Settings;
        var serverName = settings.ServerName ?? "BTCPay Server";
        var baseUrl = settings.BaseUrl;

        var dashboardLink = string.IsNullOrWhiteSpace(baseUrl)
            ? null
            : $"{baseUrl.TrimEnd('/')}/";

        var dashboardAnchor = dashboardLink is not null
            ? $"<a href='{dashboardLink}' style='color:#51b13e'>BTCPay Server dashboard</a>"
            : "your BTCPay Server dashboard";

        return
            $"<hr style='margin:24px 0;border:none;border-top:1px solid #e0e0e0'/>" +
            $"<table style='width:100%'><tr>" +
            $"<td style='vertical-align:middle;padding-right:12px'>{BtcPayLogoSvg}</td>" +
            $"<td style='vertical-align:middle;font-size:0.8em;color:#777;line-height:1.5'>" +
            $"You are receiving this alert because <strong>{System.Net.WebUtility.HtmlEncode(recipientEmail)}</strong> " +
            $"is registered as a notification recipient on <strong>{System.Net.WebUtility.HtmlEncode(serverName)}</strong>.<br/>" +
            $"To manage your alerts, visit {dashboardAnchor}." +
            $"</td>" +
            $"</tr></table>";
    }
}
