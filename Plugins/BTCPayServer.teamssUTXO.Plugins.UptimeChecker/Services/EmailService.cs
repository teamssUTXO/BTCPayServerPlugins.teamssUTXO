using System.Threading.Tasks;

namespace BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Services;

public class EmailService
{
    // service qui va envoyer les mails

    public EmailService() {}

    public async Task<bool> sendMailDown()
    {
        // méthode qui envoie un mail lorqu'un service est down
        return true;
    }

    public async Task<bool> sendMailUp()
    {
        // méthode qui envoie un mail lorsqu'un service est up (après un down)
        return true;
    }
}
