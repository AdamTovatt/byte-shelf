using System.Net.Http.Json;
using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Serialization;
using ByteShelf.Server.Email.Models;
using ByteShelf.Server.Email.Services.PostmarkModels;

namespace ByteShelf.Server.Email.Services
{
    /// <summary>
    /// Sends mail through Postmark's REST API (one endpoint, <c>POST /email</c>, so no SDK), with the
    /// named <see cref="HttpClient"/> <see cref="HttpClientName"/>. A failure, a timeout included, is
    /// answered as <see cref="EmailSendResult.IsSuccess"/> false and logged; only the caller's own
    /// cancellation is thrown.
    /// </summary>
    public sealed class PostmarkEmailSender : IEmailSender
    {
        /// <summary>The named client for Postmark, its base address Postmark's API.</summary>
        public const string HttpClientName = "Postmark";

        /// <summary>Postmark's API, which <see cref="HttpClientName"/> is based at.</summary>
        public static readonly Uri ApiBaseAddress = new Uri("https://api.postmarkapp.com/");

        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            // Postmark's names are PascalCase, as the models spell them.
            PropertyNamingPolicy = null,

            // WhenWritingNull rather than WhenWritingDefault: the latter would drop TrackOpens = false, and
            // an absent tracking field is the dashboard's setting rather than ours.
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _apiKey;
        private readonly string _from;
        private readonly ILogger<PostmarkEmailSender> _logger;

        public PostmarkEmailSender(IHttpClientFactory httpClientFactory, PostmarkEmail settings, ILogger<PostmarkEmailSender> logger)
        {
            _httpClientFactory = httpClientFactory;
            _apiKey = settings.ApiKey;
            // Quoted, so a name holding a comma, a quote or angle brackets stays one name.
            _from = new MailAddress(settings.From.Address, settings.From.Name).ToString();
            _logger = logger;
        }

        public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            PostmarkSendRequest request = new PostmarkSendRequest
            {
                From = _from,
                To = message.To,
                Subject = message.Subject,
                HtmlBody = message.HtmlBody,
                TextBody = message.TextBody,
                Tag = message.Tag,
                TrackOpens = false,
                TrackLinks = PostmarkSendRequest.NoLinkTracking,
            };

            try
            {
                HttpClient httpClient = _httpClientFactory.CreateClient(HttpClientName);
                using HttpRequestMessage httpRequest = new HttpRequestMessage(HttpMethod.Post, "email");

                // Per request rather than on the client's default headers, since the named client is shared.
                httpRequest.Headers.Add("X-Postmark-Server-Token", _apiKey);
                httpRequest.Headers.Add("Accept", "application/json");
                httpRequest.Content = JsonContent.Create(request, options: SerializerOptions);

                using HttpResponseMessage response = await httpClient.SendAsync(httpRequest, cancellationToken);
                PostmarkSendResponse? answer = await response.Content.ReadFromJsonAsync<PostmarkSendResponse>(SerializerOptions, cancellationToken);

                if (answer == null)
                {
                    _logger.LogError("Postmark answered nothing when sending an email to {To}.", message.To);
                    return Failed("Postmark returned an empty response.");
                }

                if (!response.IsSuccessStatusCode || answer.ErrorCode != 0)
                {
                    _logger.LogError(
                        "Postmark answered HTTP {StatusCode} with error code {ErrorCode} when sending an email to {To}: {Message}",
                        (int)response.StatusCode,
                        answer.ErrorCode,
                        message.To,
                        answer.Message);
                    return Failed(answer.Message ?? $"Postmark answered HTTP {(int)response.StatusCode}.");
                }

                _logger.LogInformation("Email sent to {To} through Postmark (message id {MessageId}).", message.To, answer.MessageID);
                return new EmailSendResult { IsSuccess = true, MessageId = answer.MessageID };
            }
            catch (HttpRequestException exception)
            {
                _logger.LogError(exception, "Could not reach Postmark to send an email to {To}.", message.To);
                return Failed(exception.Message);
            }
            catch (JsonException exception)
            {
                _logger.LogError(exception, "Could not read Postmark's answer when sending an email to {To}.", message.To);
                return Failed("Failed to parse Postmark's response.");
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                // The client's own timeout, not the caller's cancellation.
                _logger.LogError(exception, "Postmark did not answer in time when sending an email to {To}.", message.To);
                return Failed("Postmark did not answer in time.");
            }
        }

        private static EmailSendResult Failed(string error)
        {
            return new EmailSendResult { IsSuccess = false, ErrorMessage = error };
        }
    }
}
