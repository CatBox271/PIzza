using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace PiWpfUi;

public partial class MainWindow
{
    // ===== 字段（会话管理）=====

    // 字典
    private Dictionary<string, string> SessionName = new();//读取

    // 对象
    private LastButtonMenuItem? LastButtonMenu = null;

    // 基础 string
    private string _SessionDir = "";
    private string SessionDir { 
        get
        {
            if (string.IsNullOrEmpty(_SessionDir))
            {
                _SessionDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".pi", "agent", "sessions");
            }
            return _SessionDir;    
        }
    }
    private string DefaultFolderName = "PIzza";
    private readonly string DefaultOfLastSessionPath = "NULL";   // 新建对话占位

    // PIzza 自己的对话文件目录（和 PI agent 的 session 文件分开）
    private string _PizzaSessionDir = "";
    private string PizzaSessionDir
    {
        get
        {
            if (string.IsNullOrEmpty(_PizzaSessionDir))
            {
                _PizzaSessionDir = Path.Combine(SLManager.PersistentDataPath, "PizzaSessions");
                Directory.CreateDirectory(_PizzaSessionDir);
            }
            return _PizzaSessionDir;
        }
    }

    // 新建一个 PIzza 自己的对话文件，返回完整路径
    public string CreatePizzaSessionFile()
    {
        Directory.CreateDirectory(PizzaSessionDir);
        string id = Guid.NewGuid().ToString("N");
        string path = Path.Combine(PizzaSessionDir, id + ".jsonl");
        if (!File.Exists(path)) File.WriteAllText(path, "");
        return path;
    }

    #region Session切换处理
    private void SessionList_SelectionChanged(object sender, SelectionChangedEventArgs e)//点击侧边栏〓〓〓〓〓〓〓〓〓〓〓〓〓〓〓〓〓〓〓〓〓〓〓
    {
        // 用户点击列表项时，这里会被调用
        int index = SessionList.SelectedIndex;
        if (index < 0) return;
        //判断切换对话
        if (Last.SessionPath == SessionUI[index].File) return;
        //保存当前TextBlock

        string last = Last.SessionPath;

        if (index == 0)
        {
            //新建
            ClearMessage();
            Last.SessionPath = DefaultOfLastSessionPath;
        }
        else
        {
            Last.SessionPath = SessionUI[index].File;
            //切换
            LoadPIMessage(Last.SessionPath);
            //记得在输入的时候切换agent
        }
        WhileChangeSessoin?.Invoke(last, Last.SessionPath);
        RebuildPagesForSession(Last.SessionPath);
        Last.SaveLastState();
    }

    #endregion

    #region 会话列表显示管理
    private async Task LoadSession()
    {
        ClearSessionUI();
        //加载名字对应
        LoadSessionNameDictionary();
        //读取 PIzza 自己的 Session 列表信息
        string dir = PizzaSessionDir;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);//创建
        string[] files = Directory.GetFiles(dir);
        foreach (string _file in files)
        {
            string name = FindSessionName(_file);
            if (string.IsNullOrEmpty(name))
            {
                //后期可以移动到一个叫error_session的文件夹里
                continue;
            }
            AddSessionUI(name, _file);
        }
    }
    private void ChangeSessionUIName(string file, string name)
    {
        //file一定对,找到它再替换(带上 File,不丢路径)
        for (int i = 0; i < SessionUI.Count; i++)
        {
            if (SessionUI[i].File == file)
            {
                SessionUI[i] = new SessionItem(name, file);
                break;
            }
        }

        string sessionId = GetSessionId(file);
        SetSessionName(sessionId, name);

        SaveSessionNameDictionary();//长期化
    }
    private void AddSessionUI(string name, string file)
    {
        SessionUI.Add(new SessionItem(name, file));
    }
    private void ClearSessionUI()
    {
        for (int i = SessionUI.Count - 1; i > 0; i--)
        {
            SessionUI.RemoveAt(i);
        }
    }
    private void RemoveSessionUI()
    {
        int index = SessionList.SelectedIndex;   // 当前选中行的索引
        SessionUI.RemoveAt(index);      // 删掉这行(File 在对象里,一起删了)
    }
    #endregion

    #region UI事件

    private class LastButtonMenuItem
    {
        private Button? button;
        private ContextMenu? menu;
        public TextBox? textBox;
        public LastButtonMenuItem(Button button)
        {
            this.button = button;
            this.menu = button.ContextMenu;
            // 按钮直接父级是 DockPanel；TextBox
            if (button.Parent is DockPanel panel)
            {
                foreach (var child in panel.Children)
                {
                    if (child is TextBox box) textBox = box;
                }
            }
        }

        public bool IsLastSelected()
        {
            if (menu != null) return menu.IsVisible;
            return false;
        }
    }
    private bool SessionMenuIsAvilible(out LastButtonMenuItem? item)
    {
        item = LastButtonMenu;
        return LastButtonMenu == null ? false : LastButtonMenu.IsLastSelected();
    }
    private void SessionMore_Click(object sender, RoutedEventArgs e)
    {
        Button btn = (Button)sender;          // 拿到被点的那个按钮
        btn.ContextMenu.PlacementTarget = btn;   // 菜单出现在按钮旁边
        btn.ContextMenu.IsOpen = true;           // 打开它
        //记录
        LastButtonMenu = new(btn);
    }
    private void SessionRenameSubmit(object sender, RoutedEventArgs e)
    {
        var box = (TextBox)sender;
        if (SessionMenuIsAvilible(out LastButtonMenuItem? item) || true)
        {
            if (item != null && item.textBox == box)
            {
                ChangeSessionUIName(((SessionItem)box.DataContext).File, box.Text);//text和textIput 有区别吗
                LastButtonMenu = null;
                // 退出编辑：隐藏输入框，切回带省略号的显示层
                box.IsReadOnly = true;
                box.Focusable = false;
            }
        }
    }
    private void SessionNameTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SessionRenameSubmit(sender, e);   // 复用保存逻辑
            // 焦点移走→触发LostFocus→但LastButtonMenu已清,不会重复保存
            ((TextBox)sender).MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        }
    }
    private void SessionRename_Click(object sender, RoutedEventArgs e)
    {
        var item = (MenuItem)sender;
        var sessionName = (SessionItem)item.DataContext;
        if (SessionMenuIsAvilible(out LastButtonMenuItem? lastItem) && lastItem != null && lastItem.textBox != null)
        {
            lastItem.textBox.IsReadOnly = false;
            lastItem.textBox.Focusable = true;
            lastItem.textBox.Focus();
            lastItem.textBox.SelectAll();
        }
    }
    #endregion

    #region 会话名字数据管理
    private void LoadSessionNameDictionary()
    {
        SessionName = SLManager.ImportFromJson<Dictionary<string, string>>("", "SessionName.json") ?? new();
    }

    private void SaveSessionNameDictionary()
    {
        SLManager.ExportToJson(SessionName, "", "SessionName.json");
    }

    private void SetSessionName(string sessionId, string name)
    {
        //先刷新UI

        //默认给的sessionId正确
        SessionName[sessionId] = name;
    }

    // 按短 sessionId 查 PI agent 自己的会话文件
    public string? FindPiSessionFilePath(string sid)
    {
        var dir = Path.Combine(SessionDir, DefaultFolderName);
        if (!Directory.Exists(dir)) return null;
        foreach (var file in Directory.GetFiles(dir))
        {
            if (GetSessionId(file) == sid) return file;
        }
        return null;
    }

    public string GetSessionId(string path)
    {
        //默认文件存在
        string file_name = Path.GetFileNameWithoutExtension(path);
        string session_id = file_name;
        if (file_name.Contains('_')) session_id = file_name.Split("_")[1];
        return session_id;
    }

    private string FindSessionName(string fullPath)
    {
        try
        {
            string sessionId = GetSessionId(fullPath);
            if (SessionName.ContainsKey(sessionId)) return SessionName[sessionId];
            //读取AI第一个回复
            string result = GetFirstUserAsk(fullPath);
            if (!string.IsNullOrEmpty(result)) return CutOver(result, 64);//怎么自动缩小适应UI
            //没有的话,用SessionID
            return sessionId;
        }
        catch
        {
            return string.Empty;
        }
    }

    private string CutOver(string s, int limit)
    {
        if (s.Length > limit)
        {
            return s[..limit];
        }
        return s;
    }

    private string GetFirstUserAsk(string fullPath)
    {
        foreach (string line in File.ReadLines(fullPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(line);
                JsonElement root = doc.RootElement;

                // 只要 type=message 的事件（其他事件跳过）
                if (root.GetProperty("type").GetString() != "message") continue;
                // 只要 assistant 角色
                if (root.GetProperty("message").GetProperty("role").GetString() != "user") continue;

                // content 数组里找第一个 text 块（新积木：EnumerateArray）
                foreach (var block in root.GetProperty("message").GetProperty("content").EnumerateArray())
                {
                    if (block.TryGetProperty("type", out var bt) && bt.GetString() == "text"
                        && block.TryGetProperty("text", out var txt))
                    {
                        return txt.GetString() ?? "";
                    }
                }
            }
            catch (JsonException) { }   // 不是不知道
        }
        return "";
    }
    #endregion
}
