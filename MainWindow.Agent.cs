using System.Data;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using static System.Net.Mime.MediaTypeNames;

namespace PiWpfUi;

public class StreamMessage
{
    public Type? type;
    public string? role;
    public string? text;
    public string? tool_id;
    public string? tool_name;
    public string? tool_exec;
    public string? tool_result;
    public bool? isError;

    public enum Type
    {
        Settled,        // 这一轮结束，调用方退出循环
        MessageStart,   // role + text
        ThinkingDelta,  // delta
        MessageDelta,   // delta
        ToolCallEnd,    // id + name + arguments
        ToolResult,     // id + name + tool_result + isError
        Error,
    }
}

public partial class MainWindow
{
    // ===== 字段（Pi Agent 管理）=====
    public Dictionary<string, PiRpcClient?> PIAgent = new();//sessionId
    private string DefaultPIAgentKeyName = "creating";

    private void Test()
    {
        MessageBox.Show(
            Directory.GetCurrentDirectory() + "\n" +
            AppContext.BaseDirectory + "\n" +
            Environment.CurrentDirectory
            );
    }

    public async Task<PiRpcClient?> GetPIAgentClient(string sessionId)
    {
        Debug.WriteLine(sessionId);
        if (PIAgent.ContainsKey(sessionId) && PIAgent[sessionId] != null) return PIAgent[sessionId];
        return await RunPi(sessionId);
    }

    public async Task<PiRpcClient?> RunPi(string? sessionId = null)
    {
        //如果sessionId ==
        //对NULL拦截
        var isNewSession = sessionId == null || sessionId == DefaultOfLastSessionPath;
        if (sessionId == DefaultOfLastSessionPath) sessionId = null;
        //启动 Pi 期间提示（后台线程，切回 UI）
        State("PI启动中");
        var client = new PiRpcClient();
        client.Start(Path.Combine(SessionDir, DefaultFolderName), sessionId);
        //创建Graph流程
        if (isNewSession) EnsureDefaultPizzaGraph(DefaultOfLastSessionPath);
        PIAgent[DefaultPIAgentKeyName] = client;
        return client;
    }

    public async Task TestGetAllInfo(PiRpcClient client, string file_path)
    {
        string? line;
        while (true)
        {
            line = await client.process!.StandardOutput.ReadLineAsync();
            if (line is null) break;   // 进程退出/管道关闭 → EOF, 没得读了, 退出任务防死循环

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                string root_type = root.GetProperty("type").GetString() ?? "";
                switch (root_type)
                {
                    case "agent_settled":
                        return;   // 这一轮 PI 已经讲完
                    case "message_start":
                    {
                        var ms = root.GetProperty("message");
                        string role = ms.GetProperty("role").GetString() ?? "";
                        string text = "";

                        if (role == "user" && ms.TryGetProperty("content", out var content))
                        {
                            foreach (var block in content.EnumerateArray())
                            {
                                if (block.TryGetProperty("type", out var bt) && bt.GetString() == "text"
                                    && block.TryGetProperty("text", out var txt))
                                {
                                    text = txt.GetString() ?? "";
                                    break;
                                }
                            }
                        }

                        UiMessageStart(file_path, role, text);
                        break;
                    }

                    case "message_update":
                    {
                        var ms_event = root.GetProperty("assistantMessageEvent");
                        string ms_type = ms_event.GetProperty("type").GetString() ?? "";

                        switch (ms_type)
                        {
                            case "thinking_delta":
                            {
                                string delta = ms_event.GetProperty("delta").GetString() ?? "";
                                UiMessageDelta(file_path, delta, true);
                                break;
                            }
                            case "text_delta":
                            {
                                string delta = ms_event.GetProperty("delta").GetString() ?? "";
                                UiMessageDelta(file_path, delta, false);
                                break;
                            }
                            case "toolcall_end":
                            {
                                var tool_call = ms_event.GetProperty("toolCall");
                                string tool_id = tool_call.GetProperty("id").GetString() ?? "";
                                string tool_name = tool_call.GetProperty("name").GetString() ?? "";
                                string tool_exec = tool_call.GetProperty("arguments").ToString();

                                UiToolCallEnd(file_path, tool_id, tool_name, tool_exec);
                                break;
                            }
                        }
                        break;
                    }
                }
            }
            catch (JsonException) { }   // 个别行不是 JSON，跳过
        }
    }

    public static bool AnalysisPISettled(string? line)
    {
        if (line is null) return true;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var root_type)) return false;
            string type = root_type.GetString() ?? "";
            if (type == "agent_settled") return true;
        }
        catch (JsonException)
        {
            return false;   // 个别行不是 JSON，跳过
        }
        catch (Exception)
        {
            return true;
        }
        return false;
    }

    public static StreamMessage? AnalysisPIStream(string? json)
    {
        if (json is null) return new StreamMessage() { type = StreamMessage.Type.Error };
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string root_type = root.GetProperty("type").GetString() ?? "";
            switch (root_type)
            {
                case "agent_settled":
                    return new StreamMessage() { type = StreamMessage.Type.Settled };
                case "message_start":
                    {
                        var ms = root.GetProperty("message");
                        string role = ms.GetProperty("role").GetString() ?? "";

                        switch (role)
                        {
                            case "toolResult":
                            {
                                string tool_id = ms.TryGetProperty("toolCallId", out var tcid) ? tcid.GetString() ?? "" : "";
                                string tool_name = ms.TryGetProperty("toolName", out var tn) ? tn.GetString() ?? "" : "";
                                string tool_result = "";

                                if (ms.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var block in content.EnumerateArray())
                                    {
                                        if (block.TryGetProperty("type", out var bt) && bt.GetString() == "text"
                                            && block.TryGetProperty("text", out var txt))
                                        {
                                            tool_result = txt.GetString() ?? "";
                                            break;
                                        }
                                    }
                                }

                                bool? isError = null;
                                if (ms.TryGetProperty("isError", out var err)
                                    && (err.ValueKind == JsonValueKind.True || err.ValueKind == JsonValueKind.False))
                                {
                                    isError = err.GetBoolean();
                                }

                                return new StreamMessage()
                                {
                                    type = StreamMessage.Type.ToolResult,
                                    tool_id = tool_id,
                                    tool_name = tool_name,
                                    tool_result = tool_result,
                                    isError = isError,
                                };
                            }

                            case "user":
                            {
                                string text = "";
                                if (ms.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var block in content.EnumerateArray())
                                    {
                                        if (block.TryGetProperty("type", out var bt) && bt.GetString() == "text"
                                            && block.TryGetProperty("text", out var txt))
                                        {
                                            text = txt.GetString() ?? "";
                                            break;
                                        }
                                    }
                                }

                                return new StreamMessage()
                                {
                                    type = StreamMessage.Type.MessageStart,
                                    role = role,
                                    text = text,
                                };
                            }

                            default:
                                return new StreamMessage()
                                {
                                    type = StreamMessage.Type.MessageStart,
                                    role = role,
                                };
                        }
                    }

                case "message_update":
                    {
                        var ms_event = root.GetProperty("assistantMessageEvent");
                        string ms_type = ms_event.GetProperty("type").GetString() ?? "";

                        switch (ms_type)
                        {
                            case "thinking_delta":
                                {
                                    string delta = ms_event.GetProperty("delta").GetString() ?? "";
                                    return new StreamMessage() { type = StreamMessage.Type.ThinkingDelta, text = delta };
                                }
                            case "text_delta":
                                {
                                    string delta = ms_event.GetProperty("delta").GetString() ?? "";
                                    return new StreamMessage() { type = StreamMessage.Type.MessageDelta, text = delta };
                                }
                            case "toolcall_end":
                                {
                                    var tool_call = ms_event.GetProperty("toolCall");
                                    string tool_id = tool_call.GetProperty("id").GetString() ?? "";
                                    string tool_name = tool_call.GetProperty("name").GetString() ?? "";
                                    string tool_exec = tool_call.GetProperty("arguments").ToString();

                                    return new StreamMessage() { type = StreamMessage.Type.ToolCallEnd, tool_id = tool_id, tool_name = tool_name , tool_exec = tool_exec };
                                }
                        }
                        break;
                    }
            }
        }
        catch (JsonException)
        {
            return null;   // 个别行不是 JSON，跳过
        }
        catch (Exception)
        {
            return new StreamMessage() { type = StreamMessage.Type.Error };
        }
        return null;
    }

    /// <summary>
    /// 把用户输入包成 pi 的 prompt 命令
    /// </summary>
    public string GetProcessExecChatInput(string input)
    {
        return JsonSerializer.Serialize(new { type = "prompt", message = input });
    }
}
