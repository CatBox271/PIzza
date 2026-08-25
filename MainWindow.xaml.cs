using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
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
    private void Button_Click(object sender, RoutedEventArgs e)
    {

    }
}
