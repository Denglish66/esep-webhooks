using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

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
                    return Ok("OK");

                var headers = request.Headers ?? new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                headers.TryGetValue("X-GitHub-Event", out var ghEvent);

                var raw = request.Body ?? string.Empty;
                if (string.IsNullOrWhiteSpace(raw))
                    return BadRequest("Empty body");

                var payload = JObject.Parse(raw);

                if (string.Equals(ghEvent, "ping", StringComparison.OrdinalIgnoreCase))
                {
                    await PostToSlack($" GitHub webhook ping received for `{payload["repository"]?["full_name"] ?? "unknown"}`");
                    return Ok("Ping acknowledged");
                }

                if (string.Equals(ghEvent, "issues", StringComparison.OrdinalIgnoreCase))
                {
                    var action = payload["action"]?.ToString();
                    if (string.Equals(action, "opened", StringComparison.OrdinalIgnoreCase))
                    {
                        var repo = payload["repository"]?["full_name"]?.ToString() ?? "(unknown repo)";
                        var title = payload["issue"]?["title"]?.ToString() ?? "(no title)";
                        var author = payload["issue"]?["user"]?["login"]?.ToString() ?? "(unknown)";
                        var url = payload["issue"]?["html_url"]?.ToString() ?? "(no issue url)";

                        var text = $":bell: *GitHub Issue Created* in `{repo}`\n" +
                                   $"*Title:* {title}\n" +
                                   $"*Author:* {author}\n" +
                                   $"*Link:* {url}";

                        await PostToSlack(text);
                        return Ok("Posted issue");
                    }

                    return Ok($"Ignored issues event action={action}");
                }

                await PostToSlack($"(Ignored) Received GitHub event `{ghEvent}`.\n```{Truncate(raw, 700)}```");
                return Ok("Ignored non-target event");
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex.ToString());
                return new APIGatewayProxyResponse { StatusCode = 500, Body = "Error: " + ex.Message };
            }
        }

        private static async Task PostToSlack(string message)
        {
            var slackUrl = Environment.GetEnvironmentVariable("SLACK_URL");
            if (string.IsNullOrWhiteSpace(slackUrl))
                throw new InvalidOperationException("SLACK_URL not set");

            var body = JsonConvert.SerializeObject(new { text = message });
            var resp = await Http.PostAsync(slackUrl, new StringContent(body, Encoding.UTF8, "application/json"));
            resp.EnsureSuccessStatusCode();
        }

        private static APIGatewayProxyResponse Ok(string body) =>
            new APIGatewayProxyResponse { StatusCode = 200, Body = body };

        private static APIGatewayProxyResponse BadRequest(string body) =>
            new APIGatewayProxyResponse { StatusCode = 400, Body = body };

        private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}

