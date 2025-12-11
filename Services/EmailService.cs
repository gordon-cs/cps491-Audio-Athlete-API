using Mailjet.Client;
using Mailjet.Client.Resources;
using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;

namespace AudioAthleteApi.Services
{
    public class EmailService
    {
        private readonly string _apiKey;
        private readonly string _secretKey;
        private readonly string _fromEmail;
        private readonly string _fromName;

        public EmailService(string apiKey, string secretKey, string fromEmail, string fromName)
        {
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            _secretKey = secretKey ?? throw new ArgumentNullException(nameof(secretKey));
            _fromEmail = fromEmail ?? throw new ArgumentNullException(nameof(fromEmail));
            _fromName = fromName ?? throw new ArgumentNullException(nameof(fromName));
        }

        public async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            if (string.IsNullOrWhiteSpace(toEmail))
                throw new ArgumentNullException(nameof(toEmail));
            if (string.IsNullOrWhiteSpace(subject))
                throw new ArgumentNullException(nameof(subject));
            if (string.IsNullOrWhiteSpace(body))
                throw new ArgumentNullException(nameof(body));

            try
            {
                var client = new MailjetClient(_apiKey, _secretKey);

                var request = new MailjetRequest
                {
                    Resource = Send.Resource
                }
                .Property(Send.FromEmail, _fromEmail)
                .Property(Send.FromName, _fromName)
                .Property(Send.Subject, subject)
                .Property(Send.TextPart, body)
                .Property(Send.HtmlPart, body)
                .Property(Send.Recipients, new JArray {
                    new JObject { { "Email", toEmail } }
                });

                var response = await client.PostAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Mailjet error: StatusCode={response.StatusCode}");
                    Console.WriteLine($"ErrorInfo: {response.GetErrorInfo()}");
                    Console.WriteLine($"ErrorMessage: {response.GetErrorMessage()}");
                }
                else
                {
                    Console.WriteLine($"Email sent successfully to {toEmail}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Email sending failed: {ex.Message}");
            }
        }
    }
}
