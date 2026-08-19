using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PiWpfUi;

public partial class MainWindow
{
    // ===== 字段（消息/输入/刷新）=====

    // 字典
    public Dictionary<string, string> SessionChatInput = new();//每个会话的输入框草稿

    // 对象
    private ScrollViewer? scrollViewer = null;

    #region 消息管理

    void ClearMessage()
    {
        MessageUI.Clear();
        MessageUI.Add(new BasicMessageItem("Spacer", ""));   // 顶部占位
        MessageUI.Add(new BasicMessageItem("Spacer", ""));   // 底部占位
    }

    #region Pi事件上UI线程处理
    private void UiMessageStart(string file_path, string role, string text)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => UiMessageStart(file_path, role, text));
            return;
        }

        State(); // Pi 已就绪，清掉「启动中」

        var MC = MessageManager.Instance.MessagesCache;
        if (!MC.TryGetValue(file_path, out var list))
        {
            list = MessageManager.Instance.GetMessages(file_path, true) ?? new List<MonoMessage>();
            MC[file_path] = list;
        }

        var msg = new MonoMessage() { role = role };
        if (role == "user") msg.ApplyTextBuilder(text);
        list.Add(msg);

        if (file_path != Last.SessionPath) return;

        MessageUI.Insert(MessageUI.Count - 1, new BasicMessageItem(msg, list.Count - 1));
        ScrollToLatest();
    }

    private void UiMessageDelta(string file_path, string delta, bool reasoning)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => UiMessageDelta(file_path, delta, reasoning));
            return;
        }

        var MC = MessageManager.Instance.MessagesCache;
        if (!MC.TryGetValue(file_path, out var list) || list.Count == 0) return;

        var msg = list[^1];
        if (reasoning) msg.ApplyReasoningBuilder(delta);
        else
        {
            msg.ApplyTextBuilder(delta);
            // 流式增量直接写回 PIzza 自己的会话文件（只更新最后一条 streaming 行）
            SavePizzaConversationStreaming(file_path, msg.role, msg.text ?? "");
        }

        if (file_path != Last.SessionPath) return;

        int msgIndex = list.Count - 1;
        if (MessageUI.Count >= 3 && MessageUI[^2].MessageIndex == msgIndex)
            MessageUI[^2] = new BasicMessageItem(msg, msgIndex);
        else
            MessageUI.Insert(MessageUI.Count - 1, new BasicMessageItem(msg, msgIndex));

        ScrollToLatest();
    }

    private void UiToolCallEnd(string file_path, string toolId, string toolName, string toolArgs)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => UiToolCallEnd(file_path, toolId, toolName, toolArgs));
            return;
        }

        var MC = MessageManager.Instance.MessagesCache;
        if (!MC.TryGetValue(file_path, out var list) || list.Count == 0) return;

        var msg = list[^1];
        msg.AddTool(toolId, toolName, toolArgs, null, null);

        if (file_path != Last.SessionPath) return;

        int msgIndex = list.Count - 1;
        if (MessageUI.Count >= 3 && MessageUI[^2].MessageIndex == msgIndex)
            MessageUI[^2] = new BasicMessageItem(msg, msgIndex);
        else
            MessageUI.Insert(MessageUI.Count - 1, new BasicMessageItem(msg, msgIndex));

        ScrollToLatest();
    }
    #endregion

    #region PI本地读取
    private void LoadPIMessage(string file, bool reload = false)
    {
        var list = MessageManager.Instance.GetMessages(file, reload);
        ClearMessage();
        if (list == null) return;
        for (int i = 0; i < list.Count; i++)
        {
            var mono = list[i];
            MessageUI.Insert(MessageUI.Count - 1, new(mono, i));
        }

        ScrollToLatest(true);
    }
    #endregion

    #region 输入管理

    private void LoadSessionChatInput()
    {
        SessionChatInput = SLManager.ImportFromJson<Dictionary<string, string>>("", "SessionChatInput.json") ?? new();
    }

    private void SaveSessionChatInput()
    {
        SLManager.ExportToJson<Dictionary<string, string>>(SessionChatInput, "", "SessionChatInput.json");
    }

    public void ChangeSessionChatInput(string last_file_path, string file_path)
    {
        //先保存再更新
        if (!string.IsNullOrEmpty(ChatInput.Text)) SessionChatInput[last_file_path] = ChatInput.Text;
        if (SessionChatInput.ContainsKey(file_path)) ChatInput.Text = SessionChatInput[file_path];
        else ChatInput.Text = "";
        SaveSessionChatInput();
    }

    public string FlashSessionChatInput(string text)
    {
        string final = ChatInput.Text;
        SessionChatInput[Last.SessionPath] = ChatInput.Text = text;
        SaveSessionChatInput();
        return final;
    }

    private void ChatInputTextBox_KeyDown(object sender, KeyEventArgs e)//按下输入框
    {
        if (e.Key == Key.Enter)
        {
            if ((Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) != 0)
            {
                //换行
                ChatInput.AppendText("\n");
                e.Handled = true;
            }
            else
            {
                //发送
                e.Handled = true;
                SendChatInput();
            }
        }
    }

    public void SendChatInput()//输入管理输入管理输入管理输入管理输入管理输入管理输入管理输入管理输入管理输入管理输入管理输入管理输入管理输入管理输入管理输入管理
    {
        string input = FlashSessionChatInput("");

        // 新建对话：先创建一个 PIzza 自己的会话文件，再接入 Cheese
        if (Last.SessionPath == DefaultOfLastSessionPath)
        {
            string newPath = CreatePizzaSessionFile();
            string oldPath = Last.SessionPath;
            Last.SessionPath = newPath;
            EnsureDefaultPizzaGraph(newPath);
            SessionUI.Add(new SessionItem("新对话", newPath));
            Last.SaveLastState();
            WhileChangeSessoin?.Invoke(oldPath, newPath);
            RebuildPagesForSession(newPath);
        }

        string sessionId = GetSessionId(Last.SessionPath);
        string filePath = Last.SessionPath;

        PizzaGraphs[sessionId].UserInput(input);

        //现在接入Cheese
        return;
    }
    #endregion
    #endregion

    // 给 MessageDisplay Cheese 用：直接把接收到的内容显示到当前聊天区
    public void DisplayCheeseMessage(string text)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => DisplayCheeseMessage(text));
            return;
        }

        MessageUI.Insert(MessageUI.Count - 1, new BasicMessageItem("assistant", text));
        ScrollToLatest();
    }

    // 流式更新最后一条消息：删除旧 streaming 行，再写入当前累计文本
    public void SavePizzaConversationStreaming(string? filePath, string role, string text)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath) || filePath == DefaultOfLastSessionPath) return;
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var lines = File.Exists(filePath) ? File.ReadAllLines(filePath).ToList() : new List<string>();
            if (lines.Count > 0)
            {
                try
                {
                    using var lastDoc = JsonDocument.Parse(lines[^1]);
                    var root = lastDoc.RootElement;
                    if (root.TryGetProperty("streaming", out var streaming) && streaming.GetBoolean()
                        && root.GetProperty("message").GetProperty("role").GetString() == role)
                    {
                        lines.RemoveAt(lines.Count - 1);
                    }
                }
                catch { }
            }

            var message = new
            {
                type = "message",
                timestamp = DateTime.Now.ToString("o"),
                streaming = true,
                message = new
                {
                    role = role,
                    content = new[]
                    {
                        new { type = "text", text = text }
                    }
                }
            };
            lines.Add(JsonSerializer.Serialize(message));
            File.WriteAllLines(filePath, lines);
        }
        catch
        {
            // 流式保存失败不影响显示
        }
    }

    // 把 Cheese 产生的一条对话写入 PIzza 自己的会话文件（JSONL）
    public void SavePizzaConversation(string? filePath, string role, string text)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath) || filePath == DefaultOfLastSessionPath) return;
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var message = new
            {
                type = "message",
                timestamp = DateTime.Now.ToString("o"),
                message = new
                {
                    role = role,
                    content = new[]
                    {
                        new { type = "text", text = text }
                    }
                }
            };
            File.AppendAllText(filePath, JsonSerializer.Serialize(message) + Environment.NewLine);
        }
        catch
        {
            // 保存失败不影响显示
        }
    }

    // ===== Cheese 流式消息处理（WorkMessageDisplay 专用） =====

    /// <summary>
    /// 逐行处理 stream_result 里的原始 PI JSON：分析 + 落盘；UI 刷新是副作用，仅当前会话做。
    /// </summary>
    public void HandleCheeseStreamLine(string? filePath, string line)
    {
        var sm = MainWindow.AnalysisPIStream(line);
        if (sm == null || sm.type == null) return;

        switch (sm.type.Value)
        {
            case StreamMessage.Type.MessageStart:
                if (sm.role == "user" || sm.role == "assistant")
                {
                    CheeseStreamMessageStart(filePath, sm.role, sm.text ?? "");
                }
                break;

            case StreamMessage.Type.ThinkingDelta:
                CheeseStreamThinkingDelta(filePath, sm.text ?? "");
                break;

            case StreamMessage.Type.MessageDelta:
                CheeseStreamMessageDelta(filePath, sm.text ?? "");
                break;

            case StreamMessage.Type.ToolCallEnd:
                CheeseStreamToolCallEnd(filePath, sm.tool_id ?? "", sm.tool_name ?? "", sm.tool_exec ?? "");
                break;

            case StreamMessage.Type.ToolResult:
                CheeseStreamToolResult(filePath, sm.tool_id ?? "", sm.tool_name ?? "", sm.tool_result ?? "", sm.isError);
                break;

            case StreamMessage.Type.Settled:
            case StreamMessage.Type.Error:
            default:
                // agent_settled 不会送到这里；Error / 非 JSON 忽略
                break;
        }
    }

    private List<MonoMessage>? GetOrCreateCheeseMessages(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || filePath == DefaultOfLastSessionPath) return null;

        var cache = MessageManager.Instance.MessagesCache;
        if (!cache.TryGetValue(filePath, out var list) || list == null)
        {
            list = MessageManager.Instance.GetMessages(filePath, true) ?? new List<MonoMessage>();
            cache[filePath] = list;
        }
        return list;
    }

    private void CheeseStreamMessageStart(string? filePath, string role, string text)
    {
        var list = GetOrCreateCheeseMessages(filePath);
        if (list == null) return;

        var msg = new MonoMessage { role = role };
        msg.ApplyTextBuilder(text);
        list.Add(msg);

        if (role == "assistant")
        {
            AppendPizzaConversationStreaming(filePath, msg);
        }
        else
        {
            AppendPizzaConversationMessage(filePath, msg);
        }
        DisplayCheeseStreamMessageStart(filePath, msg, list.Count - 1);
    }

    private void CheeseStreamThinkingDelta(string? filePath, string delta)
    {
        var list = GetOrCreateCheeseMessages(filePath);
        if (list == null || list.Count == 0) return;

        var msg = list[^1];
        msg.ApplyReasoningBuilder(delta);

        UpdatePizzaConversationStreaming(filePath, msg);
        DisplayCheeseStreamMessage(filePath, msg, list.Count - 1);
    }

    private void CheeseStreamMessageDelta(string? filePath, string delta)
    {
        var list = GetOrCreateCheeseMessages(filePath);
        if (list == null || list.Count == 0) return;

        var msg = list[^1];
        msg.ApplyTextBuilder(delta);

        UpdatePizzaConversationStreaming(filePath, msg);
        DisplayCheeseStreamMessage(filePath, msg, list.Count - 1);
    }

    private void CheeseStreamToolCallEnd(string? filePath, string toolId, string toolName, string toolExec)
    {
        var list = GetOrCreateCheeseMessages(filePath);
        if (list == null || list.Count == 0) return;

        var msg = list[^1];
        msg.AddTool(toolId, toolName, toolExec, null, null);

        UpdatePizzaConversationStreaming(filePath, msg);
        DisplayCheeseStreamMessage(filePath, msg, list.Count - 1);
    }

    private void CheeseStreamToolResult(string? filePath, string toolId, string toolName, string toolResult, bool? isError)
    {
        var list = GetOrCreateCheeseMessages(filePath);

        int msgIndex = -1;
        MonoMessage? target = null;
        if (list != null)
        {
            msgIndex = FindCheeseToolMessageIndex(list, toolId);
            if (msgIndex >= 0)
            {
                target = list[msgIndex];
                if (target.tools != null)
                {
                    foreach (var tool in target.tools)
                    {
                        if (tool.tool_id == toolId)
                        {
                            tool.tool_result = toolResult;
                            tool.isError = isError;
                            break;
                        }
                    }
                }
            }
        }

        SavePizzaConversationToolResult(filePath, toolId, toolName, toolResult, isError);

        if (target != null && msgIndex >= 0)
        {
            DisplayCheeseStreamMessage(filePath, target, msgIndex);
        }
    }

    private int FindCheeseToolMessageIndex(List<MonoMessage> list, string toolId)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var tools = list[i].tools;
            if (tools == null) continue;
            foreach (var tool in tools)
            {
                if (tool.tool_id == toolId) return i;
            }
        }
        return -1;
    }

    // MessageStart 时：追加一条新的流式行
    private void AppendPizzaConversationStreaming(string? filePath, MonoMessage msg)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath) || filePath == DefaultOfLastSessionPath) return;
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            File.AppendAllText(filePath, SerializePizzaMessage(msg, streaming: true) + Environment.NewLine);
        }
        catch
        {
            // 保存失败不影响后续
        }
    }

    // 非流式角色：追加一条最终消息行
    private void AppendPizzaConversationMessage(string? filePath, MonoMessage msg)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath) || filePath == DefaultOfLastSessionPath) return;
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            File.AppendAllText(filePath, SerializePizzaMessage(msg, streaming: false) + Environment.NewLine);
        }
        catch
        {
            // 保存失败不影响显示
        }
    }
    // deltas / toolCall 时：原地更新最后一条流式行
    private void UpdatePizzaConversationStreaming(string? filePath, MonoMessage msg)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath) || filePath == DefaultOfLastSessionPath) return;
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var lines = File.Exists(filePath) ? File.ReadAllLines(filePath).ToList() : new List<string>();

            int target = -1;
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                if (IsStreamingMessageLine(lines[i]))
                {
                    target = i;
                    break;
                }
            }

            string newLine = SerializePizzaMessage(msg, streaming: true);
            if (target >= 0) lines[target] = newLine;
            else lines.Add(newLine);

            File.WriteAllLines(filePath, lines);
        }
        catch
        {
            // 保存失败不影响后续
        }
    }

    private bool IsStreamingMessageLine(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            return root.TryGetProperty("type", out var type) && type.GetString() == "message"
                && root.TryGetProperty("streaming", out var streaming)
                && streaming.ValueKind == JsonValueKind.True;
        }
        catch
        {
            return false;
        }
    }

    private void SavePizzaConversationToolResult(string? filePath, string toolId, string toolName, string toolResult, bool? isError)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath) || filePath == DefaultOfLastSessionPath) return;
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            File.AppendAllText(filePath, SerializeToolResultMessage(toolId, toolName, toolResult, isError) + Environment.NewLine);
        }
        catch
        {
            // 保存失败不影响显示
        }
    }

    private string SerializePizzaMessage(MonoMessage msg, bool streaming)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "message");
            writer.WriteString("timestamp", DateTime.Now.ToString("o"));
            if (streaming) writer.WriteBoolean("streaming", true);

            writer.WriteStartObject("message");
            writer.WriteString("role", string.IsNullOrWhiteSpace(msg.role) ? "assistant" : msg.role);

            writer.WriteStartArray("content");
            if (!string.IsNullOrWhiteSpace(msg.reasoning))
            {
                writer.WriteStartObject();
                writer.WriteString("type", "thinking");
                writer.WriteString("thinking", msg.reasoning);
                writer.WriteEndObject();
            }

            if (msg.text != null)
            {
                writer.WriteStartObject();
                writer.WriteString("type", "text");
                writer.WriteString("text", msg.text);
                writer.WriteEndObject();
            }

            if (msg.tools != null)
            {
                foreach (var tool in msg.tools)
                {
                    writer.WriteStartObject();
                    writer.WriteString("type", "toolCall");
                    writer.WriteString("id", tool.tool_id ?? "");
                    writer.WriteString("name", tool.tool_name ?? "");
                    writer.WritePropertyName("arguments");
                    WriteJsonValue(writer, tool.tool_exec);
                    writer.WriteEndObject();
                }
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.Flush();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private void WriteJsonValue(Utf8JsonWriter writer, string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            writer.WriteStringValue("");
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            doc.RootElement.WriteTo(writer);
        }
        catch
        {
            writer.WriteStringValue(rawJson);
        }
    }

    private string SerializeToolResultMessage(string toolId, string toolName, string toolResult, bool? isError)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "message");
            writer.WriteString("timestamp", DateTime.Now.ToString("o"));

            writer.WriteStartObject("message");
            writer.WriteString("role", "toolResult");
            writer.WriteString("toolCallId", toolId);
            writer.WriteString("toolName", toolName);
            writer.WriteStartArray("content");
            writer.WriteStartObject();
            writer.WriteString("type", "text");
            writer.WriteString("text", toolResult);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteBoolean("isError", isError ?? false);
            writer.WriteEndObject();

            writer.WriteEndObject();
            writer.Flush();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private void DisplayCheeseStreamMessageStart(string? filePath, MonoMessage msg, int msgIndex)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => DisplayCheeseStreamMessageStart(filePath, msg, msgIndex));
            return;
        }

        if (filePath != Last.SessionPath) return;

        MessageUI.Insert(MessageUI.Count - 1, new BasicMessageItem(msg, msgIndex));
        ScrollToLatest();
    }

    private void DisplayCheeseStreamMessage(string? filePath, MonoMessage msg, int msgIndex)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => DisplayCheeseStreamMessage(filePath, msg, msgIndex));
            return;
        }

        if (filePath != Last.SessionPath) return;

        if (MessageUI.Count >= 3 && MessageUI[^2].MessageIndex == msgIndex)
        {
            MessageUI[^2] = new BasicMessageItem(msg, msgIndex);
            ScrollToLatest();
            return;
        }

        for (int i = 1; i < MessageUI.Count - 1; i++)
        {
            if (MessageUI[i].MessageIndex == msgIndex)
            {
                MessageUI[i] = new BasicMessageItem(msg, msgIndex);
                ScrollToLatest();
                return;
            }
        }

        MessageUI.Insert(MessageUI.Count - 1, new BasicMessageItem(msg, msgIndex));
        ScrollToLatest();
    }

    #region 当前页面刷新
    private void ScrollToLatest(bool force = false)
    {
        if (MessageUI.Count == 0) return;
        if (force)
        {
            MessageList.ScrollIntoView(MessageUI[^1]);
            return;
        }
        if (scrollViewer == null) scrollViewer = FindScrollViewer(MessageList);
        if (scrollViewer == null) return;
        if (scrollViewer.Height - scrollViewer.VerticalOffset > 500) return;
        MessageList.ScrollIntoView(MessageUI[^1]);
    }
    private ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        // 深度优先遍历视觉树
        if (root is ScrollViewer sv) return sv;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            var found = FindScrollViewer(child);
            if (found != null) return found;
        }
        return null;
    }

    #endregion

    #region 工具展开（更多按钮 → text 页）
    private void ToolExec_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).DataContext is ToolItem tool)
        {
            OpenTextPage(Last.SessionPath, "工具命令", tool.tool_exec ?? "");
        }
    }

    private void ToolResult_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).DataContext is ToolItem tool)
        {
            OpenTextPage(Last.SessionPath, "工具结果", tool.tool_result ?? "");
        }
    }
    #endregion
}
