using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace PiWpfUi;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// 骨架文件：字段集中声明 + 构造 + 初始化绑定。
/// 其他职责拆在 partial 分部文件里：
///   MainWindow.Message.cs — 消息加载 / 输入发送 / 流式刷新 / 滚动
///   MainWindow.Session.cs — 会话列表 / 重命名 / 会话名持久化
///   MainWindow.Agent.cs   — Pi RPC 进程管理 / 事件流解析
///   MainWindow.Pages.cs   — 页面管理 (MonoPage / PageCache)
///   Models.cs             — 纯数据类 + LastState(上次会话状态)
///   持久化统一走 SLManager（路径确认 / JSON 存取）
/// </summary>
public partial class MainWindow : Window
{
    // ===== 字段（全局/初始化用，其余按职责散在各 partial 顶部）=====

    // 集合类
    ObservableCollection<SessionItem> SessionUI = new() { new("新建对话") };//左侧栏会话列表
    ObservableCollection<BasicMessage> MessageUI = new();//对话列表UI

    public static MainWindow Instance;
    // 委托
    public Action<string, string>? WhileChangeSessoin;

    // 对象
    LastState Last = new();//上次会话状态（持久化，重开回到上次）

    public MainWindow()
    {
        InitializeComponent();
        BindCS();
        BindUI();

        LoadPIMessage(Last.SessionPath, true);//初始加载

        _ = LoadSession();
    }

    private void BindUI()
    {
        //准备UI
        SessionList.ItemsSource = SessionUI;//左侧栏会话列表
        MessageList.ItemsSource = MessageUI;
        PageTabBar.ItemsSource = PageUI;//顶部tab栏页面
        CheeseCanvas.ItemsSource = CheeseUI;//PIzza积木画布
        LLMConfigList.ItemsSource = LLMMessageUI;//LLM配置页消息列表
        ConnectionLines.ItemsSource = ConnectionUI;//PIzza连线层
        var templateItems = new List<object> { "__HEADER__" };
        templateItems.AddRange(CheeseTemplate.Display);
        CheeseTemplateList.ItemsSource = templateItems;
        State();
    }

    private void BindCS()
    {
        Instance = this;
        MessageManager.Instance = new();
        Last = LastState.LoadLastState();
        LoadSessionNameDictionary();
        LoadSessionChatInput();
        LoadPageCache();
        LoadPizzaGraphs();
        RebuildPagesForSession(Last.SessionPath);//初始化当前session的页面
        WhileChangeSessoin += ChangeSessionChatInput;
    }
    private static async Task MessageLog(string error)
    {
        MessageBox.Show(error);
    }

    public static void LogError(string error)
    { 
        Debug.WriteLine(error);
        _ = MessageLog(error);
    }

    private void CheeseMoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not BaseCheese cheese) return;
        
        string sessionPath = cheese.Bread?.sessionPath ?? Last.SessionPath;
        var sb = new StringBuilder();
        sb.AppendLine("输入口内容：");
        sb.AppendLine(BuildPortSummary(cheese.Input));
        sb.AppendLine();
        sb.AppendLine("输出口内容：");
        sb.AppendLine(BuildPortSummary(cheese.Output));
        sb.AppendLine();
        sb.AppendLine("类型：");
        sb.AppendLine($"Wait: {cheese.WaitType} | Work: {cheese.WorkType} | Out: {cheese.OutType}");
        sb.AppendLine();
        sb.AppendLine("Draft 内容：");
        sb.AppendLine(BuildDraftSummary(cheese.WorkDraft));

        string remark = "";
        if (cheese.Parameter.TryGetValue("remark", out var remarkPara) && remarkPara?.Type == CheeseParaType.String)
            remark = remarkPara.String ?? "";

        string pageTitle = string.IsNullOrWhiteSpace(remark) ? cheese.Name + " 后台" : remark + " 后台";
        OpenTextPage(sessionPath, pageTitle, sb.ToString());
    }

    private static string BuildPortSummary(CheesePortDictionary ports)
    {
        if (ports == null || ports.Count == 0) return "（无端口）";
        var sb = new StringBuilder();
        foreach (var kv in ports)
        {
            var cache = kv.Value?.Cache ?? new List<string>();
            var cacheParts = new List<string>();
            foreach (var item in cache) cacheParts.Add(PrettyJsonOrRaw(item));
            sb.AppendLine($"{kv.Key}: {(cacheParts.Count == 0 ? "（空）" : string.Join(" | ", cacheParts))}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string BuildDraftSummary(Dictionary<string, List<string>> drafts)
    {
        if (drafts == null || drafts.Count == 0) return "（无 Draft）";
        var sb = new StringBuilder();
        foreach (var kv in drafts)
        {
            var list = kv.Value ?? new List<string>();
            var listParts = new List<string>();
            foreach (var item in list) listParts.Add(PrettyJsonOrRaw(item));
            sb.AppendLine($"{kv.Key}: {(listParts.Count == 0 ? "（空）" : string.Join(" | ", listParts))}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string PrettyJsonOrRaw(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        try
        {
            using var doc = JsonDocument.Parse(text);
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            return JsonSerializer.Serialize(doc.RootElement, options);
        }
        catch
        {
            return text;
        }
    }
}
