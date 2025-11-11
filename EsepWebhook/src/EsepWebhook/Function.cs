using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace EsepWebhook
{
    public class Function
    {
        private static readonly HttpClient Http = new HttpClient();

        public async Task<APIGatewayProxyResponse> FunctionHandler(APIGatewayProxyRequest request, ILambdaContext context)
        {
            try
            {
                if (string.Equals(request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                    return new APIGatewayProxyResponse { StatusCode = 200, Body = "OK" };

                var raw = request.Body ?? "";
                if (string.IsNullOrWhiteSpace(raw))
                    return new APIGatewayProxyResponse { StatusCode = 400, Body = "Empty body" };

                var payload = JObject.Parse(raw);
                var issueUrl = payload["issue"]?["html_url"]?.ToString();

                var messageText = !string.IsNullOrWhiteSpace(issueUrl)
                    ? $"New GitHub issue created: {issueUrl}"
                    : $"Received GitHub webhook payload:\n```{Truncate(raw, 800)}```";

                var slackUrl = Environment.GetEnvironmentVariable("SLACK_URL");
                if (string.IsNullOrWhiteSpace(slackUrl))
                    return new APIGatewayProxyResponse { StatusCode = 500, Body = "SLACK_URL not set" };

                var body = JsonConvert.SerializeObject(new { text = messageText });
                var resp = await Http.PostAsync(slackUrl, new StringContent(body, Encoding.UTF8, "application/json"));
                var ok = (int)resp.StatusCode >= 200 && (int)resp.StatusCode < 300;

                return new APIGatewayProxyResponse
                {
                    StatusCode = ok ? 200 : 502,
                    Body = ok ? "Posted to Slack" : $"Slack post failed: {(int)resp.StatusCode}"
                };
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex.ToString());
                return new APIGatewayProxyResponse { StatusCode = 500, Body = "Error: " + ex.Message };
            }
        }

        private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}
