using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace PiWpfUi;

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

    private async Task TestGetPIOutput(PiRpcClient client)
    {
        StringBuilder reply = new();
        string? line;
        while (true)
        {
            line = await client.process!.StandardOutput.ReadLineAsync();
            if (line is null) break;
            if (line.Contains("agent_settled")) break;   // 这轮讲完了,停

            try
            {
                using var doc = JsonDocument.Parse(line);   // 把这一行 JSON 解析成对象
                var root = doc.RootElement;
                if (root.GetProperty("type").GetString() == "message_update")
                {
                    var ae = root.GetProperty("assistantMessageEvent");
                    if (ae.GetProperty("type").GetString() == "text_delta")
                    {
                        reply.Append(ae.GetProperty("delta").GetString());   // 只拼 AI 说出口的话
                    }
                }
            }
            catch (JsonException) { }   // 个别行不是 JSON,跳过
        }
        Debug.WriteLine(reply);
        MessageBox.Show(reply.ToString());
    }

    /// <summary>
    /// 把用户输入包成 pi 的 prompt 命令
    /// </summary>
    public string GetProcessExecChatInput(string input)
    {
        return JsonSerializer.Serialize(new { type = "prompt", message = input });
    }
}
