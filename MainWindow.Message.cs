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
