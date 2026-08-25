using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PiWpfUi
{
    public static class DeepseekRequest
    {
        public const string ApiKeyEnvVar = "DEEPSEEK_API_KEY";
        private const string BaseUrl = "https://api.deepseek.com";

        /// <summary>
        /// 从环境变量读取 DeepSeek API Key。找不到时抛异常，绝不返回空。
        /// </summary>
        public static string GetApiKey()
        {
            string? key = Environment.GetEnvironmentVariable(ApiKeyEnvVar, EnvironmentVariableTarget.Process)
                ?? Environment.GetEnvironmentVariable(ApiKeyEnvVar, EnvironmentVariableTarget.User)
                ?? Environment.GetEnvironmentVariable(ApiKeyEnvVar, EnvironmentVariableTarget.Machine);

            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException("未设置环境变量 " + ApiKeyEnvVar + "，请先配置 DeepSeek API Key。");
            }
            return key;
        }

        /// <summary>
        /// 全局公开函数：创建并配置好 DeepSeek API 的 HttpClient（Authorization、超时、地址）。
        /// </summary>
        public static HttpClient CreateClient()
        {
            var client = new HttpClient
            {
                BaseAddress = new Uri(BaseUrl),
                Timeout = TimeSpan.FromSeconds(120),
            };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GetApiKey());
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }

        /// <summary>
        /// 全局公开函数：发送一次非流式 chat/completions 请求，返回 assistant 文本。
        /// </summary>
        public static async Task<string?> ChatAsync(
            string model,
            IEnumerable<BasicMessage> messages,
            bool? thinking = null,
            string? reasoningEffort = null,
            int? maxTokens = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(model))
            {
                throw new ArgumentException("model 不能为空", nameof(model));
            }

            using var client = CreateClient();

            var body = new Dictionary<string, object?>
            {
                ["model"] = model,
                ["messages"] = messages.Select(m => new
                {
                    role = string.IsNullOrEmpty(m.Role) ? "user" : m.Role,
                    content = m.Text ?? "",
                }).ToArray(),
                ["stream"] = false,
            };

            if (thinking.HasValue)
            {
                body["thinking"] = new { type = thinking.Value ? "enabled" : "disabled" };
            }

            if (!string.IsNullOrWhiteSpace(reasoningEffort) && thinking != false)
            {
                body["reasoning_effort"] = reasoningEffort;
            }

            if (maxTokens.HasValue)
            {
                body["max_tokens"] = maxTokens.Value;
            }

            string json = JsonSerializer.Serialize(body);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await client.PostAsync("/chat/completions", content, cancellationToken).ConfigureAwait(false);
            string respText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException("DeepSeek API 返回 " + (int)response.StatusCode + " " + response.ReasonPhrase + "：" + respText);
            }

            using var doc = JsonDocument.Parse(respText);
            var root = doc.RootElement;
            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var msg = choices[0].GetProperty("message");
                if (msg.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                {
                    return c.GetString();
                }
            }
            return null;
        }
    }
}
