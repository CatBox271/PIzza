using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Runtime;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;

namespace PiWpfUi;

public class MonoPage: ObservableObject
{
    public string type { get; set; } = "";//page/text/picture/pizza
    public string? file_path { get; set; }//picture地址（暂未用）
    public string? text { get; set; }//text内容
    public string? sessionId { get; set; }//在pizaa下代表索引
    public string title { get; set; } = "";//tab标题
    public long index { get; set; } = -1;//page:-1拉到最新
    public bool main { get; set; } = false;
    private bool _focus = false;
    [JsonIgnore]
    public bool focus { get => _focus; set => SetProperty(ref _focus, value); }//当前被选中
    public MonoPage() { }
    public static MonoPage SetMainPage(string sessionId)
    {
        return new MonoPage() { type = "page", main = true, sessionId = sessionId, title = "对话" };
    }
}

public partial class MainWindow
{
    // ===== 字段（页面管理）=====

    // 字典
    Dictionary<string, List<MonoPage>> PageCache = new();//string:sessionID -> 该session的页面列表

    // 集合
    ObservableCollection<MonoPage> PageUI = new();//Tab栏数据源：当前session的页面
    ObservableCollection<BaseCheese> CheeseUI = new();//PIzza画布积木列表

    // 对象
    MonoPage? CurrentPage = null;//当前显示的页面

    #region 页面管理
    // 切 session 时重建：主 page + 该 session 的 text 页
    public void RebuildPagesForSession(string sessionId)
    {
        PageUI.Clear();
        bool has_main = false;
        if (PageCache.TryGetValue(sessionId, out var list))
        {
            foreach (var p in list)
            {
                PageUI.Add(p);
                if(p.main) has_main = true;
            }
        }
        if (!has_main)
        {
            var main = MonoPage.SetMainPage(sessionId);
            SetPage(sessionId, main, 0);        
        }
        SwitchPage();
    }

    // 点"更多按钮"开一个 text 页
    public void OpenTextPage(string sessionId, string title, string content)
    {
        var page = new MonoPage { type = "text", sessionId = sessionId, title = title, text = content };
        OpenPage(sessionId, page);
    }
    public void OpenPIzzaText(string sessionId, string title)
    {
        var page = new MonoPage { type = "pizza", sessionId = sessionId, title = title};
        OpenPage(sessionId, page);
    }
    private List<BaseCheese> BuildDefaultCheeseList(double baseX)
    {
        var user = CloneTemplate(CheeseTemplate.UserMessage, baseX, 80);
        var agent = CloneTemplate(CheeseTemplate.StreamAgent, baseX + 220, 80);
        var display = CloneTemplate(CheeseTemplate.MessageDisplay, baseX + 440, 80);

        user.Output["content"].link = new List<CheesePortLink> { new CheesePortLink { TargetId = agent.Id, TargetPort = "content" } };
        agent.Output["stream"].link = new List<CheesePortLink> { new CheesePortLink { TargetId = display.Id, TargetPort = "stream_result" } };

        return new List<BaseCheese> { user, agent, display };
    }

    private static BaseCheese CloneTemplate(BaseCheese template, double x, double y)
    {
        var json = JsonSerializer.Serialize(template);
        var clone = JsonSerializer.Deserialize<BaseCheese>(json) ?? throw new InvalidOperationException("克隆模板失败");
        clone.Id = Guid.NewGuid().ToString("N")[..8];
        clone.X = x;
        clone.Y = y;
        return clone;
    }

    private void AddDefaultCheesePreset()
    {
        if (CurrentPage?.type != "pizza") return;
        var sc = GetOrCreateCurrentSessionCheese();
        var baseX = 60 + CheeseUI.Count * 40;
        foreach (var cheese in BuildDefaultCheeseList(baseX))
        {
            CheeseUI.Add(cheese);
            sc.AddCheese(cheese);
        }
    }

    public void AddTestCheese_Click(object sender, RoutedEventArgs e)
    {
        PushUndo();//撤销用
        AddDefaultCheesePreset();
        RebuildConnections();
        UpdateAllConnectionEndpoints();
        SaveCurrentPizzaGraph();//长期化
    }
    public void OpenPage(string sessionId, MonoPage page)
    {
        SetPage(sessionId, page);
        SwitchPage(page);
    }
    public void SetPage(string sessionId, MonoPage page,int idx = -1)
    {
        if (idx < 0) SyncAdd(sessionId, page);
        else SyncInsert(sessionId, idx, page);
        SavePageCache();
    }

    private List<MonoPage>? Resync(string sessionId)
    {
        List<MonoPage>? back = null;
        int cache_count = 0;
        if (PageCache.ContainsKey(sessionId))
        {
            cache_count = (back = PageCache[sessionId]).Count;
        }
        if (PageUI.Count == cache_count) return back;
        //以PageUI为准进行同步
        if (back == null) back = PageCache[sessionId] = new();
        back.Clear();
        bool has_main = false;
        for (int i = 0; i < PageUI.Count; i++)
        {
            var page = PageUI[i];
            if (page.main)
            {
                if (has_main)
                {
                    PageUI.RemoveAt(i);
                    i--;
                    continue;
                }
                if (i > 0)
                {
                    back.Insert(0, page);
                    PageUI.Remove(page);
                    PageUI.Insert(0, page);
                }
                else
                {
                    back.Add(page);
                }
                has_main = true;
                continue;
            }
            back.Add(page);
        }

        return back;
    }

    private void SyncAdd(string sessionId, MonoPage page)
    {
        Resync(sessionId);
        PageUI.Add(page);
        if (!PageCache.TryGetValue(sessionId, out _)) PageCache[sessionId] = new();
        PageCache[sessionId].Add(page);
    }
    private void SyncInsert(string sessionId,int idx, MonoPage page)
    {
        Resync(sessionId);
        PageUI.Insert(idx, page);
        if (!PageCache.TryGetValue(sessionId, out _)) PageCache[sessionId] = new();
        PageCache[sessionId].Insert(idx, page);
    }
    private void SyncRemove(string sessionId, MonoPage page)
    {
        Resync(sessionId);
        PageUI.Remove(page);
        if (!PageCache.TryGetValue(sessionId, out _)) return;
        PageCache[sessionId].Remove(page);
    }

    public void ClosePage(string sessionId, MonoPage page)
    {
        int idx = PageUI.IndexOf(page);
        if (idx <= 0) return;
        if (page == CurrentPage)
        {
            if (PageUI.Count > idx + 1) SwitchPage(PageUI[idx + 1]);
            else SwitchPage(PageUI[idx - 1]);
        }
        SyncRemove(sessionId, page);
        SavePageCache();
    }

    // 切换当前页面
    public void SwitchPage(MonoPage? page = null)
    {
        if (page == null)
        {
            foreach (var item in PageUI)
            {
                if (item.main)
                { 
                    page = item;
                    break;
                }
            }
        }
        if (page == null) return;

        PageTabBar.SelectedItem = null;

        // 离开旧pizza页时先保存
        SaveCurrentPizzaGraph();

        CurrentPage = page;
        PageUIVisiableSwitch(page.type);
        switch (page.type)
        {
            case "text":
                TextPageView.Text = page.text ?? "";
                break;
            case "pizza":
                LoadCheeseUI(page.sessionId ?? Last.SessionPath);
                break;
        }

        int idx = PageUI.IndexOf(page);

        TitleFocus(PageUI, idx);
    }
    private Visibility Visiable(bool vis)
    { 
        return vis ? Visibility.Visible : Visibility.Collapsed;
    }
    private void PageUIVisiableSwitch(string type)
    {
        MessageList.Visibility = Visiable(type == "page");
        TextPageView.Visibility = Visiable(type == "text");
        PIzzaPage.Visibility = Visiable(type == "pizza");
    }

    public void PageClose_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not MonoPage page) return;
        if (page.main)
        {
            //改为弹出当前session的流程拼图页面
            OpenPIzzaText(Last.SessionPath, "PIzza流程");
            return;
        }
        ClosePage(Last.SessionPath, page);
    }

    private void PageTab_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PageTabBar.SelectedItem is MonoPage page)
        {
            SwitchPage(page);
        }
    }

    public static void TitleFocus(IList<MonoPage> pages, int index = 0)
    {
        foreach (var page in pages)
        {
            page.focus = false;
        }
        pages[index].focus = true;
    }
    #region SL
    public void LoadPageCache()
    {
        PageCache = SLManager.ImportFromJson<Dictionary<string, List<MonoPage>>>("", "PageInfo.json") ?? new();
    }

    public void SavePageCache()
    {
        SLManager.ExportToJson(PageCache, "", "PageInfo.json");
    }
    #endregion
    #endregion
}
