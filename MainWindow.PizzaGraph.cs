using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.Xml;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using static System.Net.Mime.MediaTypeNames;

namespace PiWpfUi
{
    public class CheesePortLink
    {
        public string TargetId { get; set; } = "";
        public string TargetPort { get; set; } = "";
    }

    public class CheeseConnection : ObservableObject
    {
        public BaseCheese Source { get; }
        public string SourcePort { get; }
        public BaseCheese Target { get; }
        public string TargetPort { get; }

        private double _x1;
        public double X1 { get => _x1; set => SetProperty(ref _x1, value); }
        private double _y1;
        public double Y1 { get => _y1; set => SetProperty(ref _y1, value); }
        private double _x2;
        public double X2 { get => _x2; set => SetProperty(ref _x2, value); }
        private double _y2;
        public double Y2 { get => _y2; set => SetProperty(ref _y2, value); }
        private double _opacity = 1.0;
        public double Opacity { get => _opacity; set => SetProperty(ref _opacity, value); }

        public CheeseConnection(BaseCheese source, string sourcePort, BaseCheese target, string targetPort)
        {
            Source = source;
            SourcePort = sourcePort;
            Target = target;
            TargetPort = targetPort;
        }
    }

    public class CheesePort : ObservableObject
    {
        private bool _visiblity = true;
        public bool visiblity { get => _visiblity; set => SetProperty(ref _visiblity, value); }
        public string color { get; set; } = "Cyan";
        public CheesePort()
        { 
        
        }
        public CheesePort(bool input)
        {
            color = input ? "Red" : "Green";
        }
        public CheesePort(string color)
        {
            this.color = color;
        }
        public List<string> Cache { get; set; } = new();//需要持久化
        public List<CheesePortLink>? link { get; set; }//Input不填
    }

    // 可通知的端口字典：Add/Remove/索引器赋值/Clear 时通知 WPF，动态生成/删除端口会立即刷新 UI
    public class CheesePortDictionary : Dictionary<string, CheesePort>, INotifyCollectionChanged
    {
        public event NotifyCollectionChangedEventHandler? CollectionChanged;

        private int IndexOfKey(string key)
        {
            var index = 0;
            foreach (var k in Keys)
            {
                if (k == key) return index;
                index++;
            }
            return -1;
        }

        public new void Add(string key, CheesePort value)
        {
            var index = Count;
            base.Add(key, value);
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Add, new KeyValuePair<string, CheesePort>(key, value), index));
        }

        public new CheesePort this[string key]
        {
            get => base[key];
            set
            {
                var exists = TryGetValue(key, out var old);
                var index = exists ? IndexOfKey(key) : Count;
                base[key] = value;
                if (exists)
                {
                    CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
                        NotifyCollectionChangedAction.Replace,
                        new KeyValuePair<string, CheesePort>(key, value),
                        new KeyValuePair<string, CheesePort>(key, old!),
                        index));
                }
                else
                {
                    CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
                        NotifyCollectionChangedAction.Add, new KeyValuePair<string, CheesePort>(key, value), index));
                }
            }
        }

        public new bool Remove(string key)
        {
            var index = IndexOfKey(key);
            if (index < 0 || !TryGetValue(key, out var old) || !base.Remove(key)) return false;
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Remove, new KeyValuePair<string, CheesePort>(key, old), index));
            return true;
        }

        public new bool Remove(string key, out CheesePort? value)
        {
            var index = IndexOfKey(key);
            if (!base.Remove(key, out value)) return false;
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Remove, new KeyValuePair<string, CheesePort>(key, value!), index));
            return true;
        }

        public new void Clear()
        {
            if (Count == 0) return;
            base.Clear();
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }

    public class BaseCheese : ObservableObject
    {
        private string _name = "";
        public string Name { get => _name; set => SetProperty(ref _name, value); }
        private string _id = Guid.NewGuid().ToString("N")[..8];
        public string Id { get => _id; set => SetProperty(ref _id, value); }//短GUID，本图内足够唯一
        private double _x;
        public double X { get => _x; set => SetProperty(ref _x, value); }
        private double _y;
        public double Y { get => _y; set => SetProperty(ref _y, value); }
        //所有数据皆引用，Dictionary仅用于拆分输出
        public CheesePortDictionary Output { get; set; } = new();//string,输出标记如x，y，z：CheesePort.link →目标id+目标口
        public CheesePortDictionary Input { get; set; } = new();//String入口 ：CheesePort.resultCache 已接收的信息
        //要怎么做个实际执行的内容
        public WaitType WaitType { get; set; }//等待类型
        public Dictionary<string, List<string>> WaitDraft { get; set; } = new();//等待草稿,也相当于参数
        public WorkType WorkType { get; set; }//工作类型
        public Dictionary<string, List<string>> WorkDraft { get; set; } = new();//工作草稿,也相当于参数
        public OutType OutType { get; set; }
        // LLM 运行锁：endurable 模式下一次只跑一个请求（本地私有字段，不序列化）
        private bool _llmRunning = false;

        // 输入端口数据变化时的回调（PortAdd 且 input == true 时触发）
        [JsonIgnore]
        public Action? WhileImportChange { get; set; }

        // 所属面包（不持久化保存）
        [JsonIgnore]
        public PizzaBread? Bread { get; set; }

        // 面包需要加热时，每个芝士挂载的回调
        private Action<List<string>>? _whileBreadNeedHeat;
        [JsonIgnore]
        public Action<List<string>>? WhileBreadNeedHeat
        {
            get => _whileBreadNeedHeat;
            set
            {
                if (value == null)
                {
                    _whileBreadNeedHeat = null;
                }
                else
                {
                    _whileBreadNeedHeat = list =>
                    {
                        // 自动把自己的 Id 放到列表最前面
                        list.Insert(0, Id);
                        value(list);
                    };
                }
            }
        }

        public BaseCheese()
        {
            // 只挂回调；Parameter 的对象初始化器在构造函数之后才执行，这里扫描会扫到空集合
            WhileImportChange += _DoWait;
            AttachParameterDictionary(_parameter);
        }

        public bool GetTruePortCache(bool input, string port, out List<string>? cache)
        {
            if (!GetPortCache(input, port, out cache) || cache!.Count == 0) return false;
            return true;
        }

        #region port
        /// <summary>
        ///true cache一定不为null;
        /// </summary>
        /// <param name="input"></param>
        /// <param name="port">key</param>
        /// <param name="cache">List<string></param>
        /// <returns></returns>
        public bool GetPortCache(bool input, string port, out List<string>? cache)
        {
            cache = null;
            if (!(input ? Input : Output).TryGetValue(port, out var cheesePort)) return false;
            if (!GetPortCache(cheesePort, out cache)) return false;

            return true;
        }
        /// <summary>
        /// true cache一定不为null;
        /// </summary>
        /// <param name="cheesePort">CheesePort</param>
        /// <param name="cache">List<string></param>
        /// <returns></returns>
        public bool GetPortCache(CheesePort cheesePort, out List<string> cache)
        {
            cache = cheesePort.Cache;
            if (cache == null) return false;
            return true;
        }

        public void PortAdd(bool input, string port, List<string> items)
        {
            if (!GetPortCache(input, port, out var cache)) return;
            cache!.AddRange(items);
            if (input) WhileImportChange?.Invoke();
        }

        public void SetPort(bool input, string port, string item)
        {
            if (!GetPortCache(input, port, out var cache)) return;
            cache!.Add(item);
            if (input) WhileImportChange?.Invoke();
        }

        public void PortClear(bool input, string port)
        {
            //指定clear
            if (!GetPortCache(input, port, out var cache)) return;
            cache?.Clear();
        }

        public void PortClear()
        {
            //全部clear
            foreach (var item in Input)
            {
                if (item.Value == null) continue;
                var cache = item.Value.Cache;
                if (cache != null) cache.Clear();
            }
        }
        #endregion

        #region draft
        //在写外部新建Cheese的时候一定要告知不能用default做端口名
        //原生的方法经历不要新建，为了给外部的留
        private const string default_work_key = "default";

        public void AddWorkDraft(string add, string key = default_work_key)
        {
            //默认放在
            if (!WorkDraft.ContainsKey(key) || WorkDraft[key] == null)
            {
                WorkDraft[key] = new();
            }
            WorkDraft[key].Add(add);
        }
        public void AddWorkDraft(List<string> add, string key = default_work_key)
        {
            //默认放在
            if (!WorkDraft.ContainsKey(key) || WorkDraft[key] == null)
            {
                WorkDraft[key] = new();
            }
            WorkDraft[key].AddRange(add);
        }
        private bool MoveWorkDraft(string to, string from = default_work_key)
        {
            if (GetWorkDraft(out var draft, from) && draft != null)
            {
                AddWorkDraft(draft, to);
                WorkDraftClear(from);
                return true;
            }
            return false;
        }
        private void WorkDraftClear(string key = default_work_key)
        {
            WorkDraft[key]?.Clear();
        }
        /// <summary>
        /// draft非空
        /// </summary>
        private bool GetWorkDraft(out List<string>? draft, string key = default_work_key)
        {
            if (!WorkDraft.TryGetValue(key, out draft))
            {
                draft = null;
            }
            if (draft == null || draft.Count == 0) return false;
            bool judge = false;
            for (int i = draft.Count - 1; i > -1; i--)
            {
                var item = draft[i];
                if (string.IsNullOrEmpty(item))
                {
                    draft.RemoveAt(i);
                }
                else
                {
                    judge = true;
                }
            }
            return judge;
        }
        #endregion 

        #region Wait
        private void _DoWait() => DoWait();

        public bool DoWait()
        {
            bool judge = false;
            try
            {
                judge = WaitType switch
                {
                    WaitType.Any => WaitAny(),
                    WaitType.All => WaitAll(),
                    WaitType.Assign => WaitAssign(),
                    _ => false
                };
            }
            catch(Exception e)
            {
                MainWindow.LogError(e.ToString());
            }

            if (judge) DoWork();
            return judge;
        }

        //have any thing
        private bool InputKeyHaveAnyThing(string key)
        {
            if (Input.TryGetValue(key, out var port) && port?.Cache != null && ListHaveAnyThing(port.Cache))
            {
                return true;
            }
            return false;
        }

        //have any thing (Output 对称版)
        private bool OutputKeyHaveAnyThing(string key)
        {
            if (Output.TryGetValue(key, out var port) && port?.Cache != null && ListHaveAnyThing(port.Cache))
            {
                return true;
            }
            return false;
        }

        private bool ListHaveAnyThing(List<string> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                bool blank = string.IsNullOrEmpty(list[i]);
                if (blank)
                {
                    //主动清空
                    list.RemoveAt(i);
                    i--;
                }
                else
                {
                    return true;
                }
            }
            return false;
        }

        private bool WaitAny()
        {
            foreach (string key in Input.Keys)
            {
                if (InputKeyHaveAnyThing(key)) return true;
            }
            return false;
        }
        private bool WaitAll()
        {
            if (Input.Keys.Count == 0) return false;

            foreach (string key in Input.Keys)
            {
                if (!InputKeyHaveAnyThing(key)) return false;
            }

            return true;
        }
        //WaitDraft的KeyValue ANY就直接过、所有的ALL都有过，最后把All和Any的结果单独与门计算，Value没写默认All

        private bool WaitAssign()
        {
            if (!WaitDraft.TryGetValue("any", out var any)) any = new List<string>();
            if (!WaitDraft.TryGetValue("all", out var all)) all = new List<string>();

            bool all_judge = false;
            bool any_judge = false;
            if (all.Count + any.Count != 0)
            {
                if (all.Count == 0) all_judge = true;
                if (any.Count == 0) any_judge = true;
            }

            if (all.Count != 0)
            {
                all_judge = true;
                foreach (var key in all)
                {
                    //必须指定的每个都有
                    if (!InputKeyHaveAnyThing(key))
                    {
                        all_judge = false;
                        break;
                    }
                }
            }

            foreach (var key in any)
            {
                if (InputKeyHaveAnyThing(key))
                {
                    any_judge = true;
                    break;
                }
            }

            return all_judge && any_judge;
        }
        #endregion

        #region Work
        public bool DoWork()
        {
            Debug.WriteLine(Name);
            //暂时不管执行错误的情况
            bool get_result = false;
            try
            {
                get_result = WorkType switch
                {
                    WorkType.UserMessage => WorkUserMessage(),
                    WorkType.AgentStream => WorkAgentStream(),
                    WorkType.MessageDisplay => WorkMessageDisplay(),
                    WorkType.Text => WorkText(),
                    WorkType.Time => WorkTime(),
                    WorkType.Merge => WorkMerge(),
                    WorkType.TestPopup => WorkTestPopup(),
                    WorkType.RegexReplace => WorkRegexReplace(),
                    WorkType.RegexExtract => WorkRegexExtract(),
                    WorkType.Contains => WorkContains(),
                    WorkType.FileWriter => WorkFileWriter(),
                    WorkType.LLM=>WorkLLM(),
                    _ => false,
                };

                if (get_result) DoOut();
            }
            catch(Exception e)
            {
                MainWindow.LogError(e.ToString());
            }
            return get_result;
        }
        private bool WorkLLM()
        {
            //根据WorkDraft里存的内容运行
            //有一个text输入。reset输入

            if (GetTruePortCache(true, "reset", out List<string>? resets))
            {
                if (resets!.Contains("true"))
                {
                    //重置
                    PortClear(true, "reset");
                    WorkDraftClear("messages");
                }
            }

            if (!GetTruePortCache(true, "text", out var texts) || texts!.Count == 0) return false;

            string text = texts[0];
            texts.RemoveAt(0);
            bool endurable = GetPara("endurable", CheeseParaType.Bool, out CheesePara? para) && (para!.Bool ?? false);

            if (endurable)
            {
                //一次只能跑一个 LLM 请求
                if (_llmRunning) return false;
                _llmRunning = true;

                List<BasicMessage> messages = new();
                if (!LLMLoad("messages", ref messages))
                {
                    if (!LLMLoad(default_work_key, ref messages)) messages.Clear();
                }
                messages.Add(new BasicMessage("user", text));
                _ = WorkStartLLMClient(messages);
                return true;
            }

            //一次性对话：不维护历史
            List<BasicMessage> oneShot = new();
            if (!LLMLoad(default_work_key, ref oneShot)) oneShot.Clear();
            oneShot.Add(new BasicMessage("user", text));
            _ = WorkStartLLMClient(oneShot);
            return true;
        }
        private bool LLMLoad(string key,ref List<BasicMessage> messages)
        {
            if (GetWorkDraft(out var jsons, key))
            {
                foreach (var json in jsons)
                {
                    try
                    {
                        var ms = JsonSerializer.Deserialize<BasicMessage>(json);
                        if (ms != null) messages.Add(ms);
                    }
                    catch
                    {
                        MainWindow.LogError("LLMCheese读取时遭遇异常");
                        continue;
                    }
                }
                return true;
            }
            else
            {
                return false;
            }
        }

        private async Task WorkStartLLMClient(List<BasicMessage> messages)
        {
            try
            {
                //LLM请求的参数是放在WorkDraft里的
                if (!GetPara("model", CheeseParaType.String, out CheesePara? modelPara) || string.IsNullOrWhiteSpace(modelPara!.String))
                {
                    MainWindow.LogError("LLM未配置模型：Parameter[\"model\"] 不能为空");
                    return;
                }

                    string model = modelPara.String!;

                //默认只有deepseekAPI
                bool? thinking = null;
                if (GetPara("thinking", CheeseParaType.Bool, out CheesePara? thinkingPara))
                    thinking = thinkingPara!.Bool;

                string? reasoningEffort = null;
                if (GetPara("reasoning_effort", CheeseParaType.Select, out CheesePara? effortPara))
                    reasoningEffort = string.IsNullOrWhiteSpace(effortPara!.String) ? null : effortPara.String;

                int? maxTokens = null;
                if (GetPara("max_tokens", CheeseParaType.Int, out CheesePara? tokenPara) && tokenPara!.Int > 0)
                    maxTokens = tokenPara.Int;

                string? content = await DeepseekRequest.ChatAsync(model, messages, thinking, reasoningEffort, maxTokens);

                if (!string.IsNullOrEmpty(content))
                {
                    SetPort(false, "output", content);

                    //endurable：把 assistant 结果追加到 messages 历史
                    if (GetPara("endurable", CheeseParaType.Bool, out CheesePara? para) && (para!.Bool ?? false))
                    {
                        AddWorkDraft(JsonSerializer.Serialize(new BasicMessage("assistant", content)), "messages");
                    }

                    DoOut();
                }
            }
            catch (Exception e)
            {
                MainWindow.LogError(e.ToString());
            }
            finally
            {
                _llmRunning = false;
            }
        }
        private bool WorkUserMessage()
        {
            if(!GetTruePortCache(true, "user_input", out var result)) return false;
            PortAdd(false, "content", result!);
            PortClear(true, "user_input");
            return true;
        }

        private bool WorkText()
        {
            if (!GetPara("text", CheeseParaType.String, out var text)) return false;
            if (!GetPara("addition", CheeseParaType.Bool, out var para) || !(para!.Bool ?? false))
            {
                PortClear(true, "input");
            }

            SetPort(false, "output", text?.String ?? "");
            return true;
        }

        private bool WorkTime()
        {
            // 没有开启 Addition 时，输入只作为触发，用后清掉；开启时交给 DoAddition 统一追加
            if (!GetPara("addition", CheeseParaType.Bool, out var para) || !(para!.Bool ?? false))
            {
                PortClear(true, "入口");
            }

            SetPort(false, "出口", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            return true;
        }

        private int GetOccurrence(string key = "occurrence")
        {
            if (Parameter.TryGetValue(key, out var p) && p?.Type == CheeseParaType.Int && p.Int.HasValue) return p.Int.Value;
            return 0;
        }

        private static string ReplaceNthLiteral(string input, string pattern, string replacement, int occurrence)
        {
            int index = -1;
            for (int i = 0; i < occurrence; i++)
            {
                index = input.IndexOf(pattern, index + 1, StringComparison.Ordinal);
                if (index < 0) return input;
            }
            return input.Remove(index, pattern.Length).Insert(index, replacement);
        }

        private static string ReplaceNthRegex(string input, string pattern, string replacement, int occurrence)
        {
            var matches = Regex.Matches(input, pattern);
            if (occurrence <= 0 || occurrence > matches.Count) return input;
            var m = matches[occurrence - 1];
            return input.Remove(m.Index, m.Length).Insert(m.Index, replacement);
        }

        private static List<string> ExtractBetween(string input, string start, string end)
        {
            var results = new List<string>();
            if (string.IsNullOrEmpty(start) || string.IsNullOrEmpty(end)) return results;

            int pos = 0;
            while (pos <= input.Length)
            {
                int s = input.IndexOf(start, pos, StringComparison.Ordinal);
                if (s < 0) break;
                int contentStart = s + start.Length;
                int e = input.IndexOf(end, contentStart, StringComparison.Ordinal);
                if (e < 0) break;
                results.Add(input.Substring(contentStart, e - contentStart));
                pos = e + end.Length;
            }
            return results;
        }

        private bool WorkContains()
        {
            if (!GetTruePortCache(true, "input", out var input)) return false;
            if (!GetPara("keyword", CheeseParaType.String, out var keyword) || string.IsNullOrEmpty(keyword!.String))
            {
                PortClear(true, "input");
                return false;
            }

            bool regexMode = false;
            if (Parameter.TryGetValue("regex_mode", out var rm) && rm?.Type == CheeseParaType.Bool && rm.Bool.HasValue)
            {
                regexMode = rm.Bool.Value;
            }

            var output = new List<string>();
            foreach (var item in input!)
            {
                bool ok;
                try
                {
                    if (regexMode) ok = Regex.IsMatch(item, keyword.String!);
                    else ok = item.Contains(keyword.String!, StringComparison.Ordinal);
                }
                catch (Exception e)
                {
                    MessageBox.Show(e.ToString());
                    PortClear(true, "input");
                    return false;
                }
                output.Add(ok ? "true" : "false");
            }

            PortClear(true, "input");
            PortAdd(false, "result", output);
            return true;
        }

        private bool WorkRegexReplace()
        {
            if (!GetTruePortCache(true, "input", out var input)) return false;
            if (!GetPara("search", CheeseParaType.String, out var search) || string.IsNullOrEmpty(search!.String))
            {
                PortClear(true, "input");
                return false;
            }

            string replacement = "";
            if (Parameter.TryGetValue("replace", out var rp) && rp?.Type == CheeseParaType.String && rp.String != null)
            {
                replacement = rp.String;
            }

            bool regexMode = false;
            if (Parameter.TryGetValue("regex_mode", out var rm) && rm?.Type == CheeseParaType.Bool && rm.Bool.HasValue)
            {
                regexMode = rm.Bool.Value;
            }

            int occurrence = GetOccurrence();

            var output = new List<string>();
            foreach (var item in input!)
            {
                if (string.IsNullOrEmpty(item))
                {
                    output.Add(item);
                    continue;
                }

                try
                {
                    if (occurrence <= 0)
                    {
                        if (regexMode) output.Add(Regex.Replace(item, search.String!, m => replacement));
                        else output.Add(item.Replace(search.String!, replacement));
                    }
                    else
                    {
                        if (regexMode) output.Add(ReplaceNthRegex(item, search.String!, replacement, occurrence));
                        else output.Add(ReplaceNthLiteral(item, search.String!, replacement, occurrence));
                    }
                }
                catch (Exception e)
                {
                    MessageBox.Show(e.ToString());
                    PortClear(true, "input");
                    return false;
                }
            }

            PortClear(true, "input");
            PortAdd(false, "output", output);
            return true;
        }

        private bool WorkRegexExtract()
        {
            if (!GetTruePortCache(true, "input", out var input)) return false;

            bool regexMode = false;
            if (Parameter.TryGetValue("regex_mode", out var rm) && rm?.Type == CheeseParaType.Bool && rm.Bool.HasValue)
            {
                regexMode = rm.Bool.Value;
            }

            int occurrence = GetOccurrence();
            bool outputList = true;
            if (Parameter.TryGetValue("output_list", out var ol) && ol?.Type == CheeseParaType.Bool && ol.Bool.HasValue)
            {
                outputList = ol.Bool.Value;
            }

            var output = new List<string>();
            if (regexMode)
            {
                if (!GetPara("start", CheeseParaType.String, out var start) || string.IsNullOrEmpty(start!.String))
                {
                    PortClear(true, "input");
                    return false;
                }
                if (!GetPara("end", CheeseParaType.Int, out var end) || !end!.Int.HasValue)
                {
                    PortClear(true, "input");
                    return false;
                }
                int groupIndex = end.Int.Value;

                foreach (var item in input!)
                {
                    if (string.IsNullOrEmpty(item)) continue;

                    try
                    {
                        var matches = Regex.Matches(item, start.String!);
                        var selected = new List<string>();
                        for (int i = 0; i < matches.Count; i++)
                        {
                            if (occurrence > 0 && i + 1 != occurrence) continue;
                            if (groupIndex >= matches[i].Groups.Count) continue;
                            selected.Add(matches[i].Groups[groupIndex].Value);
                        }

                        if (selected.Count == 0) continue;
                        if (outputList) output.AddRange(selected);
                        else output.Add(string.Concat(selected));
                    }
                    catch (Exception e)
                    {
                        MessageBox.Show(e.ToString());
                        PortClear(true, "input");
                        return false;
                    }
                }
            }
            else
            {
                if (!GetPara("start", CheeseParaType.String, out var start) || string.IsNullOrEmpty(start!.String))
                {
                    PortClear(true, "input");
                    return false;
                }
                if (!GetPara("end", CheeseParaType.String, out var end) || string.IsNullOrEmpty(end!.String))
                {
                    PortClear(true, "input");
                    return false;
                }

                foreach (var item in input!)
                {
                    if (string.IsNullOrEmpty(item))
                    {
                        output.Add(item);
                        continue;
                    }

                    var matches = ExtractBetween(item, start.String!, end.String!);
                    if (occurrence > 0)
                    {
                        matches = matches.Skip(occurrence - 1).Take(1).ToList();
                    }

                    if (matches.Count == 0) continue;

                    if (outputList) output.AddRange(matches);
                    else output.Add(string.Concat(matches));
                }
            }

            PortClear(true, "input");
            PortAdd(false, "output", output);
            return true;
        }

        private bool WorkFileWriter()
        {
            if (!GetTruePortCache(true, "input", out var input)) return false;
            if (!GetPara("path", CheeseParaType.String, out var path) || string.IsNullOrWhiteSpace(path!.String))
            {
                PortClear(true, "input");
                return false;
            }

            bool append = false;
            if (Parameter.TryGetValue("append", out var ap) && ap?.Type == CheeseParaType.Bool && ap.Bool.HasValue)
            {
                append = ap.Bool.Value;
            }

            try
            {
                var dir = Path.GetDirectoryName(path.String);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                if (append) File.AppendAllLines(path.String!, input!);
                else File.WriteAllLines(path.String!, input!);

                PortClear(true, "input");
                return true;
            }
            catch (Exception e)
            {
                MessageBox.Show(e.ToString());
                return false;
            }
        }
        /// <summary>
        /// 合并，使用
        /// </summary>
        /// <returns></returns>
        private bool WorkMerge()
        {
            // 1. content 先进入缓冲
            if (GetTruePortCache(true, "content", out var content))
            {
                AddWorkDraft(content!);
                PortClear(true, "content");
            }

            // 2. finish 信号触发输出
            if (GetTruePortCache(true, "finish", out var finish))
            {
                PortClear(true, "finish");

                string separator = "";
                if (GetPara("separator", CheeseParaType.String, out CheesePara? sp))
                {
                    separator = sp!.String ?? "";
                }
                if (GetWorkDraft(out var buffer) && buffer != null && buffer.Count > 0)
                {
                    SetPort(false, "combined", string.Join(separator, buffer));
                    WorkDraftClear();
                }
            }

            return true;
        }

        private bool WorkTestPopup()
        {
            if (!GetTruePortCache(true, "input", out var input)) return false;

            string text = string.Join(Environment.NewLine, input!);
            PortClear(true, "input");

            var main = MainWindow.Instance;
            if (main != null && !main.Dispatcher.CheckAccess())
            {
                main.Dispatcher.BeginInvoke(() => MessageBox.Show(main, text, "测试弹窗"));
            }
            else if (main != null)
            {
                MessageBox.Show(main, text, "测试弹窗");
            }
            else
            {
                MessageBox.Show(text, "测试弹窗");
            }

            return true;
        }

        private bool WorkMessageDisplay()
        {
            var main = MainWindow.Instance;
            if (main == null) return false;

            bool worked = false;

            // 流式口：原始 PI JSON 行，逐行分析、落盘、按条件刷 UI
            if (GetTruePortCache(true, "stream_result", out var stream))
            {
                foreach (var line in stream!)
                {
                    main.HandleCheeseStreamLine(Bread?.sessionPath, line);
                }
                PortClear(true, "stream_result");
                worked = true;
            }

            // 非流式口：保持原样不动
            if (GetTruePortCache(true, "result", out var result))
            {
                foreach (var item in result!)
                {
                    main.SavePizzaConversation(Bread?.sessionPath, "assistant", item);
                    main.DisplayCheeseMessage(item);
                }
                PortClear(true, "result");
                worked = true;
            }

            // 用户输入口：把文本作为 user 消息显示（并写入 PIzza 会话文件）
            if (GetTruePortCache(true, "用户输入", out var userInput))
            {
                foreach (var item in userInput!)
                {
                    main.SavePizzaConversation(Bread?.sessionPath, "user", item);
                    main.DisplayUserMessage(item);
                }
                PortClear(true, "用户输入");
                worked = true;
            }

            return worked;
        }

        //一直循环到结束
        private bool WorkAgentStream()
        {
            if (!GetTruePortCache(true, "content", out var result)) return false;
            if (GetWorkDraft(out var draft) && draft!.Count != 0)
            {
                //还需要实际看一下有没有PI的后台，如果没有就放行
                if (!MainWindow.Instance.PIAgent.TryGetValue(MainWindow.Instance.GetSessionId(Bread!.sessionPath), out var client) || client == null)
                {
                    draft = new();
                }
                else
                {
                    //这里其实要个处理完这个处理下个的
                    return false;
                }
            }
            AddWorkDraft(result!);
            PortClear(true,"content");

            _ = AgentStreamAsync();
            return true;
        }
        public async Task AgentStreamAsync()
        {
            //等下帮我在需要的地方加Dispatch
            MainWindow main = MainWindow.Instance;
            if (!GetWorkDraft(out List<string>? draft) || draft == null || draft.Count == 0) return;

            StringBuilder builder = new();
            foreach (string item in draft)
            {
                builder.AppendLine(item);
            }
            var client = await main.GetPIAgentClient(main.GetSessionId(Bread!.sessionPath));
            if (client == null)
            {
                MessageBox.Show("启动PIAgent失败");
                return;
            }
            try
            {
                await client.process!.StandardInput.WriteLineAsync(main.GetProcessExecChatInput(builder.ToString()));

                string? line;
                while (true)
                {
                    line = await client.process!.StandardOutput.ReadLineAsync();
                    if (MainWindow.AnalysisPISettled(line)) break;
                    SetPort(false, "stream", line!);//AnalysisPISettled后不可能为null
                    DoOut();   // 触发下游加热/传递
                }
                SetPort(false, "finish", "true");
                DoOut();

                WorkDraftClear();
            }
            catch (Exception e)
            {
                MessageBox.Show(e.ToString());

                SetPort(false, "finish", "false");
                DoOut();

                WorkDraftClear();
            }
        }
        #endregion

        #region Addition
        // 通用 para：Addition。输出时，把刚刚接收到的内容一起追加到输出缓存
        private void DoAddition()
        {
            try
            {
                if (!GetPara("addition", CheeseParaType.Bool, out var para) || !(para!.Bool ?? false)) return;

                bool front = true;
                if (Parameter.TryGetValue("addition_fore_back", out var fb) && fb != null)
                {
                    switch (fb.Type)
                    {
                        case CheeseParaType.Bool:
                            front = fb.Bool ?? true;
                            break;
                        default:
                            front = true;
                            break;
                    }
                }

                foreach (var input in Input.Values)
                {
                    if (input?.Cache == null || input.Cache.Count == 0) continue;
                    foreach (var output in Output.Values)
                    {
                        if (output?.Cache == null) continue;
                        if (front) output.Cache.InsertRange(0, input.Cache);
                        else output.Cache.AddRange(input.Cache);
                    }
                    input.Cache.Clear();
                }
            }
            catch (Exception e)
            {
                MainWindow.LogError(e.ToString());
            }
        }
        #endregion

        #region Out

        private void DoOut()
        {
            DoAddition();
            List<string>? idx = null;
            try
            {
                idx = OutType switch
                {
                    OutType.Any => OutAny(),
                    OutType.All => OutAll(),
                    OutType.OutProgram => OutProgram(),
                    OutType.OutHttp => OutHttp(),
                    _ => null,
                };
            }
            catch(Exception e)
            {
                MainWindow.LogError(e.ToString());
            }
            if (idx == null || idx.Count == 0) return;

            WhileBreadNeedHeat?.Invoke(idx);
        }


        private List<string> GetAllOutCheeseId()
        {
            List<string> ids = new();
            foreach (var output in Output.Values)
            {
                if (output?.link == null) continue;
                foreach (var link in output.link)
                {
                    if (!string.IsNullOrEmpty(link.TargetId))
                        ids.Add(link.TargetId);
                }
            }
            return ids;
        }

        private List<string>? OutAny()
        {
            Debug.WriteLine(Name);
            foreach (var key in Output.Keys)
            {
                if (OutputKeyHaveAnyThing(key))
                {
                    return GetAllOutCheeseId();
                }
            }
            return null;
        }
        private List<string>? OutAll()
        {
            if (Output.Count == 0) return null;

            var ids = new List<string>();
            foreach (var output in Output.Values)
            {
                if (output?.Cache == null || !ListHaveAnyThing(output.Cache))
                {
                    return null;
                }

                if (output.link == null) continue;
                foreach (var link in output.link)
                {
                    if (!string.IsNullOrEmpty(link.TargetId))
                        ids.Add(link.TargetId);
                }
            }
            return ids.Count > 0 ? ids : null;
        }
        private List<string>? OutProgram() { return null; }
        private List<string>? OutHttp() { return null; }
        #endregion

        //选项后期可以优化为缓存结果，因为运行中不会被改变

        #region Para
        public void DoPara()
        {
            if (WorkType == WorkType.RegexExtract) UpdateRegexExtractParamUI();
            if (WorkType == WorkType.LLM) UpdateLLMParamUI();
            foreach (var type in DealParas)
            {
                _ = type switch
                {
                    DealParaType.InputSpawner => InputSpawner(),
                    _ => false,
                };
            }
        }
        private bool InputSpawner()
        {
            if (!GetPara("input_count", CheeseParaType.Int, out CheesePara? para)) return false;
            int count = para!.Int ?? 0;
            //暴力执行
            int delta = Input.Count - count;
            if (delta > 0)
            {
                for (; delta > 0; delta--)
                {
                    string key = Input.Keys.ToList()[^1];
                    CheesePort item = Input[key];
                    Input.Remove(key);
                }
            }
            else if (delta < 0)
            {
                for (; delta < 0; delta++)
                {
                    StringBuilder builder = new("接口");
                    for (int num = 1; true; num++)
                    {
                        builder.Append(num);
                        if (Input.ContainsKey(builder.ToString()))
                        {
                            int cost = num.ToString().Length;
                            builder.Remove(builder.Length - cost, cost);
                            continue;
                        }
                        Input.Add(builder.ToString(), new CheesePort(true));
                        break;
                    }
                }
            }
            return true;
        }

        #region 参数
        public List<DealParaType> DealParas { get; set; } = new();

        private CheeseParaDictionary _parameter = new();
        public CheeseParaDictionary Parameter
        {
            get => _parameter;
            set
            {
                if (ReferenceEquals(_parameter, value)) return;
                DetachParameterDictionary(_parameter);
                _parameter = value ?? new CheeseParaDictionary();
                AttachParameterDictionary(_parameter);
                ScanDealParas();
            }
        }

        /// <summary>清空并重新扫描 ParaDealer：只登记执行方案，不执行</summary>
        public void ScanDealParas()
        {
            DealParas.Clear();
            foreach (var para in Parameter.Values)
            {
                if (para?.ParaDealer == null) continue;
                foreach (var item in para.ParaDealer)
                {
                    if (!DealParas.Contains(item)) DealParas.Add(item);
                }
            }
        }
        private void AttachParameterDictionary(CheeseParaDictionary parameter)
        {
            parameter.Changed -= OnParameterDictionaryChanged;
            parameter.Changed += OnParameterDictionaryChanged;
            foreach (var para in parameter.Values) AttachParameter(para);
        }

        private void DetachParameterDictionary(CheeseParaDictionary parameter)
        {
            parameter.Changed -= OnParameterDictionaryChanged;
            foreach (var para in parameter.Values) DetachParameter(para);
        }

        private void AttachParameter(CheesePara? para)
        {
            if (para == null) return;
            para.PropertyChanged -= OnCheeseParaChanged;
            para.PropertyChanged += OnCheeseParaChanged;
        }

        private void DetachParameter(CheesePara? para)
        {
            if (para != null) para.PropertyChanged -= OnCheeseParaChanged;
        }

        // Parameter 字典本身增删项：清空重扫，不执行
        private void OnParameterDictionaryChanged()
        {
            foreach (var para in Parameter.Values) AttachParameter(para);
            ScanDealParas();
        }

        // 某个 CheesePara 的值变了：执行一遍
        private void OnCheeseParaChanged(object? sender, PropertyChangedEventArgs e)
        {
            // 已从 Parameter 移除的旧参数不再响应（避免残留订阅误触发）
            if (sender is CheesePara para && !Parameter.ContainsValue(para)) return;
            if (e.PropertyName == nameof(CheesePara.ParaDealer)) ScanDealParas();
            DoPara();
        }
        /// <summary>
        /// para 不可能为空
        /// </summary>
        private bool GetPara(string key, CheeseParaType type, out CheesePara? para)
        {
            para = null;
            if (!Parameter.TryGetValue(key, out para) || para == null) return false;
            if (para.Type != type) return false;
            return true;
        }

        // 正则提取：根据 regex_mode 动态切换 start/end 的显示名与 end 类型
        private void UpdateRegexExtractParamUI()
        {
            if (WorkType != WorkType.RegexExtract) return;

            bool regexMode = false;
            if (Parameter.TryGetValue("regex_mode", out var rm) && rm?.Type == CheeseParaType.Bool && rm.Bool.HasValue)
            {
                regexMode = rm.Bool.Value;
            }

            if (Parameter.TryGetValue("start", out var start))
            {
                start.Name = regexMode ? "正则表达式" : "开始";
            }
            if (Parameter.TryGetValue("end", out var end))
            {
                end.Name = regexMode ? "第几个提取项" : "结束";
                end.Type = regexMode ? CheeseParaType.Int : CheeseParaType.String;
            }
        }

        // LLM：只有勾选上下文持久化才显示 reset 口；取消时去掉挂到 reset 口的连接
        private void UpdateLLMParamUI()
        {
            if (WorkType != WorkType.LLM) return;

            bool endurable = GetPara("endurable", CheeseParaType.Bool, out var para) && (para!.Bool ?? false);

            if (Input.TryGetValue("reset", out var resetPort))
            {
                resetPort.visiblity = endurable;
            }

            if (!endurable)
            {
                MainWindow.Instance?.RemoveIncomingLinkPublic(this, "reset");
            }
        }
        #endregion

        #endregion
    }

    public class CheeseTemplate
    {
        #region para
        public readonly static CheesePara Addition = new()
        {
            Name = "输入附带",
            Description = "输出时，将这个Cheese刚刚接受到的内容一起传递下去",
            Type = CheeseParaType.Bool,
            Bool = true,
            ParaDealer = new() { DealParaType.Addition },
        };
        public readonly static CheesePara AdditionFB = new()
        {
            Name = "附带在前",
            Description = " Ture时input的内容在前 ，False在后",
            Type = CheeseParaType.Bool,
            Bool = true,
        };
        public readonly static CheesePara MergeSeparator = new()
        {
            Name = "拼接符",
            Description = "合并芝士输出时使用的拼接字符串",
            Type = CheeseParaType.String,
            String = "",
        };

        public readonly static CheesePara RegexMode = new()
        {
            Name = "正则模式",
            Description = "true 时按正则表达式处理；false 时按字面字符串处理",
            Type = CheeseParaType.Bool,
            Bool = false,
        };
        public readonly static CheesePara RegexSearch = new()
        {
            Name = "检索",
            Description = "正则表达式，匹配要检索的内容",
            Type = CheeseParaType.String,
            String = "",
        };
        public readonly static CheesePara RegexReplacement = new()
        {
            Name = "替换为",
            Description = "要替换成的文本，字面替换",
            Type = CheeseParaType.String,
            String = "",
        };
        public readonly static CheesePara RegexOccurrence = new()
        {
            Name = "第几项",
            Description = "替换/提取第几个匹配；<=0 表示全部，默认0",
            Type = CheeseParaType.Int,
            Int = 0,
        };
        public readonly static CheesePara ExtractStart = new()
        {
            Name = "开始",
            Description = "开始字符串（字面匹配；提取时从第一个开始串之后取，不处理嵌套）",
            Type = CheeseParaType.String,
            String = "",
        };
        public readonly static CheesePara ExtractEnd = new()
        {
            Name = "结束",
            Description = "结束字符串（字面匹配；取第一个结束串之前的内容，不处理嵌套）",
            Type = CheeseParaType.String,
            String = "",
        };
        public readonly static CheesePara ExtractOutputList = new()
        {
            Name = "输出为列表",
            Description = "true 每个结果作为独立列表项；false 多个结果合并为一个字符串",
            Type = CheeseParaType.Bool,
            Bool = true,
        };
        public readonly static CheesePara ContainsKeyword = new()
        {
            Name = "包含内容",
            Description = "要判断是否包含的字符串或正则表达式",
            Type = CheeseParaType.String,
            String = "",
        };

        public readonly static CheesePara FileWriterPath = new()
        {
            Name = "写入地址",
            Description = "要写入的文件完整路径",
            Type = CheeseParaType.String,
            String = "",
        };
        public readonly static CheesePara FileWriterAppend = new()
        {
            Name = "追加写入",
            Description = "true 追加；false 覆盖",
            Type = CheeseParaType.Bool,
            Bool = false,
        };

        public readonly static CheesePara TextContent = new()
        {
            Name = "文本内容",
            Description = "要输出的固定文本",
            Type = CheeseParaType.String,
            String = "",
        };
        /// <summary>
        /// clone parameter
        /// </summary>
        public readonly static CheesePara LLMModel = new()
        {
            Name = "模型",
            Description = "DeepSeek 模型名",
            Type = CheeseParaType.String,
            String = "",
        };
        public readonly static CheesePara LLMEndurable = new()
        {
            Name = "上下文持久化",
            Description = "true 维护历史消息，false 一次性对话",
            Type = CheeseParaType.Bool,
            Bool = false,
        };
        public readonly static CheesePara LLMRemark = new()
        {
            Name = "备注",
            Description = "显示名称，如：代码生成",
            Type = CheeseParaType.String,
            String = "",
        };
        public readonly static CheesePara LLMThinking = new()
        {
            Name = "思考模式",
            Description = "true 启用思考；false 关闭思考",
            Type = CheeseParaType.Bool,
            Bool = true,
        };
        public readonly static CheesePara LLMReasoningEffort = new()
        {
            Name = "思考强度",
            Description = "low / high / max；关闭思考时不生效",
            Type = CheeseParaType.Select,
            String = "high",
            Options = new List<string> { "low", "high", "max" },
        };
        public readonly static CheesePara LLMMaxTokens = new()
        {
            Name = "MaxToken",
            Description = "最大输出 token 数",
            Type = CheeseParaType.Int,
            Int = 4096,
        };



        public static CheesePara CP(CheesePara template)
        {
            var json = JsonSerializer.Serialize(template);
            var clone = JsonSerializer.Deserialize<CheesePara>(json) ?? throw new InvalidOperationException("克隆模板失败");
            return clone;
        }
        #endregion
        #region Cheese
        public readonly static BaseCheese UserMessage = new() {
            Name = "用户输入",
            Input = new() { ["user_input"] = new CheesePort { visiblity = false } },
            Output = new() { ["content"] = new CheesePort(false) },
            WaitType = WaitType.Any,
            WorkType = WorkType.UserMessage,
            OutType = OutType.Any,
        };
        public readonly static BaseCheese StreamAgent = new() { 
            Name = "流式输出Agent",
            Input = new() { ["content"] = new CheesePort (true) },
            Output = new() { ["stream"] = new CheesePort (false), ["finish"] = new CheesePort (false) },//finish输出bool:true/false
            WaitType = WaitType.Any,
            WorkType = WorkType.AgentStream,
            OutType = OutType.Any,
        };
        public readonly static BaseCheese MessageDisplay = new() {
            Name = "消息显示",
            Input = new() { ["result"] = new CheesePort (true), ["stream_result"] = new CheesePort (true), ["用户输入"] = new CheesePort (true) },
            WaitType = WaitType.Any,
            WorkType = WorkType.MessageDisplay 
        };
        public readonly static BaseCheese InputCombiner = new() {
            Name = "输入合并器",
            Output = new() { ["conbined"] = new CheesePort(true) },
            WaitType = WaitType.All,
            WorkType = WorkType.InputCombiner,
            OutType = OutType.Any,
            Parameter = new()
            {
                ["input_count"] = new (){
                    Name = "接口数量",
                    Type = CheeseParaType.Int,
                    Int = 2,
                    ParaDealer = new(){ DealParaType.InputSpawner }
                }
            }
        };
        public readonly static BaseCheese SystemTime = new()
        {
            Name = "时间",
            Input = new() { ["入口"] = new CheesePort(true)},
            Output = new() { ["出口"] = new CheesePort(false)},
            WaitType = WaitType.Any,
            WorkType = WorkType.Time,
            OutType = OutType.Any,
            Parameter = new()
            {
                ["addition"] = CP(Addition),
                ["addition_fore_back"] = CP(AdditionFB),
            }
        };
        public readonly static BaseCheese Merge = new()
        {
            Name = "合并芝士",
            Input = new() { ["content"] = new CheesePort(true), ["finish"] = new CheesePort(true) },
            Output = new() { ["combined"] = new CheesePort(false) },
            WaitType = WaitType.Any,
            WorkType = WorkType.Merge,
            OutType = OutType.Any,
            Parameter = new()
            {
                ["separator"] = CP(MergeSeparator),
            }
        };
        public readonly static BaseCheese TestPopup = new()
        {
            Name = "测试弹窗",
            Input = new() { ["input"] = new CheesePort(true) },
            WaitType = WaitType.Any,
            WorkType = WorkType.TestPopup,
            OutType = OutType.Any,
        };
        public readonly static BaseCheese RegexReplace = new()
        {
            Name = "正则替换",
            Input = new() { ["input"] = new CheesePort(true) },
            Output = new() { ["output"] = new CheesePort(false) },
            WaitType = WaitType.Any,
            WorkType = WorkType.RegexReplace,
            OutType = OutType.Any,
            Parameter = new()
            {
                ["search"] = CP(RegexSearch),
                ["replace"] = CP(RegexReplacement),
                ["occurrence"] = CP(RegexOccurrence),
                ["regex_mode"] = CP(RegexMode),
            }
        };
        public readonly static BaseCheese RegexExtract = new()
        {
            Name = "正则提取",
            Input = new() { ["input"] = new CheesePort(true) },
            Output = new() { ["output"] = new CheesePort(false) },
            WaitType = WaitType.Any,
            WorkType = WorkType.RegexExtract,
            OutType = OutType.Any,
            Parameter = new()
            {
                ["start"] = CP(ExtractStart),
                ["end"] = CP(ExtractEnd),
                ["occurrence"] = CP(RegexOccurrence),
                ["output_list"] = CP(ExtractOutputList),
                ["regex_mode"] = CP(RegexMode),
            }
        };
        public readonly static BaseCheese Contains = new()
        {
            Name = "包含",
            Input = new() { ["input"] = new CheesePort(true) },
            Output = new() { ["result"] = new CheesePort(false) },
            WaitType = WaitType.Any,
            WorkType = WorkType.Contains,
            OutType = OutType.Any,
            Parameter = new()
            {
                ["keyword"] = CP(ContainsKeyword),
                ["regex_mode"] = CP(RegexMode),
            }
        };
        public readonly static BaseCheese Text = new()
        {
            Name = "文本",
            Input = new() { ["input"] = new CheesePort(true) },
            Output = new() { ["output"] = new CheesePort(false) },
            WaitType = WaitType.Any,
            WorkType = WorkType.Text,
            OutType = OutType.Any,
            Parameter = new()
            {
                ["text"] = CP(TextContent),
                ["addition"] = CP(Addition),
                ["addition_fore_back"] = CP(AdditionFB),
            }
        };
        public readonly static BaseCheese FileWriter = new()
        {
            Name = "写入文件",
            Input = new() { ["input"] = new CheesePort(true) },
            WaitType = WaitType.Any,
            WorkType = WorkType.FileWriter,
            OutType = OutType.Any,
            Parameter = new()
            {
                ["path"] = CP(FileWriterPath),
                ["append"] = CP(FileWriterAppend),
            }
        };
        public readonly static BaseCheese LLM = new()
        {
            Name = "LLM请求",
            Input = new() { ["text"] = new CheesePort(true), ["reset"] = new CheesePort(true) },
            Output = new() { ["output"] = new CheesePort(true) },
            WaitType = WaitType.Any,
            WorkType = WorkType.LLM,
            OutType = OutType.Any,
            Parameter = new()
            {
                ["model"] = CP(LLMModel),
                ["endurable"] = CP(LLMEndurable),
                ["remark"] = CP(LLMRemark),
                ["thinking"] = CP(LLMThinking),
                ["reasoning_effort"] = CP(LLMReasoningEffort),
                ["max_tokens"] = CP(LLMMaxTokens),
            }
        };

        /// <summary>
        /// clone cheese：模板本体不要直接给 UI，避免被改动污染
        /// </summary>
        public static BaseCheese Clone(BaseCheese template)
        {
            var json = JsonSerializer.Serialize(template);
            var clone = JsonSerializer.Deserialize<BaseCheese>(json) ?? throw new InvalidOperationException("克隆模板失败");
            return clone;
        }

        // 必须在所有模板字段之后初始化，否则静态初始化顺序会导致字段还是 null，Display 里全是空
        public static List<BaseCheese> Display = new()
        {
            Clone(UserMessage),
            Clone(StreamAgent),
            Clone(MessageDisplay),
            Clone(InputCombiner),
            Clone(SystemTime),
            Clone(Merge),
            Clone(TestPopup),
            Clone(RegexReplace),
            Clone(RegexExtract),
            Clone(Contains),
            Clone(Text),
            Clone(FileWriter),
            Clone(LLM),
        };
        #endregion 
    }

    public class PizzaBread
    {
        public PizzaBread() { }

        public PizzaBread(string sessionId, IEnumerable<BaseCheese> cheeses)
        {
            this.sessionPath = sessionId;
            ReplaceCheeses(cheeses);
        }

        private List<BaseCheese> _cheeses = new();
        public string sessionPath { get; set; } = "";
        public List<BaseCheese> Cheeses
        {
            get => _cheeses;
            set
            {
                _cheeses = value ?? new();
                RebuildIndex();
            }
        }

        // 按 Id 快速查找的索引，加载/设置 Cheeses 时自动重建
        [JsonIgnore]
        public Dictionary<string, BaseCheese> CheeseIndex { get; private set; } = new();

        // 按 WorkType 分组的索引，加载/设置 Cheeses 时自动重建
        [JsonIgnore]
        public Dictionary<WorkType, List<BaseCheese>> WorkTypeIndex { get; private set; } = new();

        public void RebuildIndex()
        {
            CheeseIndex.Clear();
            WorkTypeIndex.Clear();
            foreach (var cheese in _cheeses)
            {
                if (cheese == null) continue;
                CheeseIndex[cheese.Id] = cheese;

                if (!WorkTypeIndex.TryGetValue(cheese.WorkType, out var list))
                {
                    list = new List<BaseCheese>();
                    WorkTypeIndex[cheese.WorkType] = list;
                }
                list.Add(cheese);

                // 设置所属 Bread
                cheese.Bread = this;

                // 加载时清空旧挂载，再重新挂载加热函数
                cheese.WhileBreadNeedHeat = null;
                cheese.WhileBreadNeedHeat += OnBreadNeedHeat;

                // 对象此时已经完整：先扫描参数方案，再执行一遍（InputSpawner 按差值建口）
                cheese.ScanDealParas();
                cheese.DoPara();
            }
        }

        private void OnBreadNeedHeat(List<string> ids)
        {
            PortDelive(CheeseIndex[ids[0]]);
            // 需要激活的一批对象 ID
            for (int i = 1; i < ids.Count; i++)
            {
                if (CheeseIndex.TryGetValue(ids[i], out var cheese))
                {
                    cheese.DoWait();
                }
            }
        }

        [JsonIgnore] public Stack<List<BaseCheese>> UndoStack { get; set; } = new();
        [JsonIgnore] public Stack<List<BaseCheese>> RedoStack { get; set; } = new();

        #region 添加/移除/替换

        // 供外部使用的添加/移除/替换工具，统一维护 Cheeses 和 CheeseIndex
        public void AddCheese(BaseCheese cheese)
        {
            if (cheese == null) return;
            cheese.Bread = this;
            _cheeses.Add(cheese);
            CheeseIndex[cheese.Id] = cheese;

            if (!WorkTypeIndex.TryGetValue(cheese.WorkType, out var list))
            {
                list = new List<BaseCheese>();
                WorkTypeIndex[cheese.WorkType] = list;
            }
            list.Add(cheese);

            // 对象已经完整：扫描参数方案并执行一次
            cheese.ScanDealParas();
            cheese.DoPara();
        }

        public bool RemoveCheese(string id)
        {
            if (string.IsNullOrEmpty(id) || !CheeseIndex.TryGetValue(id, out var cheese)) return false;
            _cheeses.Remove(cheese);
            CheeseIndex.Remove(id);
            RemoveFromWorkTypeIndex(cheese);
            return true;
        }

        public bool RemoveCheese(BaseCheese cheese)
        {
            if (cheese == null || !CheeseIndex.TryGetValue(cheese.Id, out var existing)) return false;
            if (!ReferenceEquals(existing, cheese)) return false;
            _cheeses.Remove(cheese);
            CheeseIndex.Remove(cheese.Id);
            RemoveFromWorkTypeIndex(cheese);
            return true;
        }

        public void ReplaceCheeses(IEnumerable<BaseCheese> cheeses)
        {
            _cheeses = cheeses?.ToList() ?? new List<BaseCheese>();
            RebuildIndex();
        }

        public void ClearCheeses()
        {
            _cheeses.Clear();
            CheeseIndex.Clear();
            WorkTypeIndex.Clear();
        }

        private void RemoveFromWorkTypeIndex(BaseCheese cheese)
        {
            if (!WorkTypeIndex.TryGetValue(cheese.WorkType, out var list)) return;
            list.Remove(cheese);
            if (list.Count == 0) WorkTypeIndex.Remove(cheese.WorkType);
        }
        #endregion

        //手动delivery
        public void PortDelive(BaseCheese from, bool from_input, string port_from, BaseCheese to, bool to_input, string port_to, bool clear = true)
        {
            if (!from.GetPortCache(from_input, port_from, out var from_cache)) return;
            if (!to.GetPortCache(to_input, port_to, out var to_cache) || to_cache == null) return;
            //开始运输
            to_cache!.AddRange(from_cache!);
            //结束
            if(clear) from.PortClear(from_input, port_from);
        }

        public void PortDelive(BaseCheese cheese)
        {
            //传递所有可传递的
            foreach (var item in cheese.Output)
            {
                string port = item.Key;
                if (!cheese.GetPortCache(false, port, out var cache)) continue;

                var links = item.Value.link;
                if (links == null || links.Count == 0) continue;

                foreach (CheesePortLink link in links)
                {
                    string CheeseId = link.TargetId;
                    string PortId = link.TargetPort;
                    if (string.IsNullOrEmpty(CheeseId) || string.IsNullOrEmpty(PortId)) continue;
                    if (!FindCheese(CheeseId, out BaseCheese aim_cheese)) continue;

                    PortDelive(cheese, false, port, aim_cheese, true, PortId ,false);
                }
                cheese.PortClear(false, port);
            }
        }

        public bool FindCheese(string id, out BaseCheese cheese)
        {
            if (CheeseIndex.TryGetValue(id, out var found))
            {
                cheese = found;
                return true;
            }
            cheese = null!;
            return false;
        }

        public void UserInput(string content)
        {
            if (!WorkTypeIndex.TryGetValue(WorkType.UserMessage, out var cheeselist))
            {
                MessageBox.Show("当前面饼上没有检测用户输入的芝士");
                return;
            }
            foreach (var cheese in cheeselist)
            {
                cheese.SetPort(true, "user_input", content);
            }
        }
        public void AddInputTo(BaseCheese cheese, string port, List<string> items)
        {
            
        }
    }

    public partial class MainWindow
    {
        // ===== 字段（PIzza积木持久化）=====

        // 字典：sessionId -> 该 session 的积木列表（模仿 SessionName 的存取方式）
        private Dictionary<string, PizzaBread> PizzaGraphs = new();

        #region 烹饪PIzza



        #endregion

        #region UI参数

        // 连线
        private readonly ObservableCollection<CheeseConnection> ConnectionUI = new();
        private const double PortSnapRadius = 14;

        private bool _isConnecting = false;
        private BaseCheese? _connectingSource = null;
        private string _connectingSourcePort = "";

        // 重连/断连
        private BaseCheese? _rewireTargetCheese = null;
        private string _rewireTargetPort = "";
        private BaseCheese? _rewireOldSourceCheese = null;
        private string _rewireOldSourcePort = "";
        private CheeseConnection? _rewireOldConnection = null;

        // 撤销/重做（按当前 session 存在 SessionCheese 里）
        private List<BaseCheese>? _dragSnapshot = null;
        private double _dragOriginalX;
        private double _dragOriginalY;

        // 拖拽状态
        private bool _isCheeseDragging = false;
        private BaseCheese? _draggingCheese = null;
        private Point _cheeseDragStart;
        private bool _isCheeseOverDeleteZone = false;

        #endregion
        #region PIzza积木数据管理
        private void LoadPizzaGraphs()
        {
            PizzaGraphs = new();

            if (Directory.Exists(PizzaSessionDir))
            {
                foreach (var dir in Directory.GetDirectories(PizzaSessionDir))
                {
                    string sid = Path.GetFileName(dir);
                    string graphFile = Path.Combine(dir, PizzaGraphFileName);
                    if (!File.Exists(graphFile)) continue;

                    try
                    {
                        var bread = JsonSerializer.Deserialize<PizzaBread>(File.ReadAllText(graphFile));
                        if (bread == null) continue;
                        bread.sessionPath = GetPizzaConversationFilePath(sid);
                        PizzaGraphs[sid] = bread;
                    }
                    catch
                    {
                        // 单个会话图损坏不影响其他会话加载
                    }
                }
            }

            if (PizzaGraphs.Count == 0)
            {
                ResetPizzaGraphsToDefault();
                return;
            }

            NormalizePizzaGraphs();
            EnsureLLMParameterDefaults();
            EnsureMessageDisplayDefaults();
        }

        // 修复已加载的图：null 的 PizzaBread 补默认图
        private void NormalizePizzaGraphs()
        {
            bool changed = false;
            foreach (var sid in PizzaGraphs.Keys.ToList())
            {
                if (PizzaGraphs[sid] != null) continue;
                PizzaGraphs[sid] = new PizzaBread(GetPizzaConversationFilePath(sid), BuildDefaultCheeseList(60));
                changed = true;
            }
            if (changed) SavePizzaGraphs();
        }


        // 老 LLM 芝士补齐新增参数：备注/思考模式/思考强度/MaxToken
        private void EnsureLLMParameterDefaults()
        {
            bool changed = false;
            foreach (var bread in PizzaGraphs.Values)
            {
                if (bread?.Cheeses == null) continue;
                foreach (var cheese in bread.Cheeses)
                {
                    if (cheese.WorkType != WorkType.LLM) continue;
                    if (!cheese.Parameter.ContainsKey("remark"))
                    {
                        cheese.Parameter["remark"] = CheeseTemplate.CP(CheeseTemplate.LLMRemark);
                        changed = true;
                    }
                    if (!cheese.Parameter.ContainsKey("thinking"))
                    {
                        cheese.Parameter["thinking"] = CheeseTemplate.CP(CheeseTemplate.LLMThinking);
                        changed = true;
                    }
                    if (!cheese.Parameter.ContainsKey("reasoning_effort"))
                    {
                        cheese.Parameter["reasoning_effort"] = CheeseTemplate.CP(CheeseTemplate.LLMReasoningEffort);
                        changed = true;
                    }
                    if (!cheese.Parameter.ContainsKey("max_tokens"))
                    {
                        cheese.Parameter["max_tokens"] = CheeseTemplate.CP(CheeseTemplate.LLMMaxTokens);
                        changed = true;
                    }
                }
            }
            if (changed) SavePizzaGraphs();
        }

        // 老 MessageDisplay 芝士补齐新增端口：用户输入
        private void EnsureMessageDisplayDefaults()
        {
            bool changed = false;
            foreach (var bread in PizzaGraphs.Values)
            {
                if (bread?.Cheeses == null) continue;
                foreach (var cheese in bread.Cheeses)
                {
                    if (cheese.WorkType != WorkType.MessageDisplay) continue;
                    if (!cheese.Input.ContainsKey("用户输入"))
                    {
                        cheese.Input["用户输入"] = new CheesePort(true);
                        changed = true;
                    }
                }
            }
            if (changed) SavePizzaGraphs();
        }
        // 没有任何会话图时：为所有已有会话 + LastPath 重建默认图
        private void ResetPizzaGraphsToDefault()
        {
            var reset = new Dictionary<string, PizzaBread>();

            if (Directory.Exists(PizzaSessionDir))
            {
                foreach (var dir in Directory.GetDirectories(PizzaSessionDir))
                {
                    string sid = Path.GetFileName(dir);
                    string conv = Path.Combine(dir, PizzaConversationFileName);
                    if (!File.Exists(conv)) continue;
                    reset[sid] = new PizzaBread(conv, BuildDefaultCheeseList(60));
                }
            }

            // LastPath 可能是新建占位 NULL，也要有一份默认图，避免页面全空
            string lastSid = GetSessionId(Last.SessionPath);
            if (!string.IsNullOrEmpty(lastSid) && !reset.ContainsKey(lastSid))
            {
                string conv = lastSid == DefaultOfLastSessionPath ? DefaultOfLastSessionPath : GetPizzaConversationFilePath(lastSid);
                reset[lastSid] = new PizzaBread(conv, BuildDefaultCheeseList(60));
            }

            PizzaGraphs = reset;
            SavePizzaGraphs();
        }

        private string FindSessionFilePath(string sid)
        {
            if (string.IsNullOrEmpty(sid) || sid == DefaultOfLastSessionPath) return sid;
            return GetPizzaConversationFilePath(sid);
        }

        private void SavePizzaGraphs()
        {
            foreach (var sid in PizzaGraphs.Keys.ToList())
            {
                SavePizzaGraph(sid, PizzaGraphs[sid]);
            }
        }

        private void SavePizzaGraph(string sid, PizzaBread? bread)
        {
            if (bread == null) return;
            if (string.IsNullOrEmpty(sid) || sid == DefaultOfLastSessionPath) return;

            bread.sessionPath = GetPizzaConversationFilePath(sid);
            string dir = Path.Combine(PizzaSessionDir, sid);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, PizzaGraphFileName), JsonSerializer.Serialize(bread, new JsonSerializerOptions { WriteIndented = true }));
        }

        // 当前 pizza 页所属 session 的 SessionCheese；没有就补一个
        // 只读获取当前 pizza 页所属 session 的 PizzaBread；不创建，避免副作用
        private PizzaBread? CurrentSessionCheese()
        {
            if (CurrentPage?.type != "pizza") return null;
            string filePath = CurrentPage.sessionId ?? Last.SessionPath;
            string sessionId = GetSessionId(filePath);
            return PizzaGraphs.TryGetValue(sessionId, out var sc) ? sc : null;
        }

        // 需要往当前 session 里加芝士时调用；没有就建一个空面包（持久化由 SaveCurrentPizzaGraph 负责）
        private PizzaBread GetOrCreateCurrentSessionCheese()
        {
            string filePath = CurrentPage?.sessionId ?? Last.SessionPath;
            string sessionId = GetSessionId(filePath);
            if (!PizzaGraphs.TryGetValue(sessionId, out var sc))
            {
                string conv = sessionId == DefaultOfLastSessionPath ? DefaultOfLastSessionPath : GetPizzaConversationFilePath(sessionId);
                sc = new PizzaBread(conv, new List<BaseCheese>());
                PizzaGraphs[sessionId] = sc;
            }
            return sc;
        }

        // 把当前 CheeseUI 写回当前 pizza 页所属 session，并落盘到该 session 文件夹
        private void SaveCurrentPizzaGraph()
        {
            if (CurrentPage?.type != "pizza") return;
            string filePath = CurrentPage.sessionId ?? Last.SessionPath;
            string sessionId = GetSessionId(filePath);
            if (!PizzaGraphs.TryGetValue(sessionId, out var sc))
            {
                string conv = sessionId == DefaultOfLastSessionPath ? DefaultOfLastSessionPath : GetPizzaConversationFilePath(sessionId);
                sc = new PizzaBread(conv, new List<BaseCheese>());
                PizzaGraphs[sessionId] = sc;
            }
            sc.ReplaceCheeses(CheeseUI);
            SavePizzaGraph(sessionId, sc);
        }
        // ---------- 撤销/重做/重置 ----------
        private void PushUndo()
        {
            var sc = CurrentSessionCheese();
            if (sc == null) return;
            sc.UndoStack.Push(CloneCheeseList());
            sc.RedoStack.Clear();
        }

        private List<BaseCheese> CloneCheeseList()
        {
            var json = JsonSerializer.Serialize(CheeseUI.ToList());
            return JsonSerializer.Deserialize<List<BaseCheese>>(json) ?? new List<BaseCheese>();
        }

        private void LoadCheeseList(List<BaseCheese> list)
        {
            CheeseUI.Clear();
            foreach (var cheese in list)
            {
                CheeseUI.Add(cheese);
            }
            RebuildConnections();
            UpdateAllConnectionEndpoints();
        }

        private void ResetPizzaGraph_Click(object sender, RoutedEventArgs e)
        {
            PushUndo();
            CheeseUI.Clear();
            AddDefaultCheesePreset();
            RebuildConnections();
            UpdateAllConnectionEndpoints();
            SaveCurrentPizzaGraph();
        }

        private void UndoPizzaGraph_Click(object sender, RoutedEventArgs e)
        {
            var sc = CurrentSessionCheese();
            if (sc == null || sc.UndoStack.Count == 0) return;
            sc.RedoStack.Push(CloneCheeseList());
            LoadCheeseList(sc.UndoStack.Pop());
            SaveCurrentPizzaGraph();
        }

        private void RedoPizzaGraph_Click(object sender, RoutedEventArgs e)
        {
            var sc = CurrentSessionCheese();
            if (sc == null || sc.RedoStack.Count == 0) return;
            sc.UndoStack.Push(CloneCheeseList());
            LoadCheeseList(sc.RedoStack.Pop());
            SaveCurrentPizzaGraph();
        }

        // 打开/切换到 pizza 页时，把该 session 的积木装进 CheeseUI
        private void LoadCheeseUI(string sessionId)
        {
            sessionId = GetSessionId(sessionId);
            CheeseUI.Clear();
            if (PizzaGraphs.TryGetValue(sessionId, out var sc))
            {
                foreach (var cheese in sc.Cheeses)
                {
                    CheeseUI.Add(cheese);
                }
                sc.UndoStack.Clear();
                sc.RedoStack.Clear();
            }
            RebuildConnections();
            UpdateAllConnectionEndpoints();
        }

        // 新建会话（RunPi 里 session_id == null）时，立刻生成默认测试积木并存盘
        public void EnsureDefaultPizzaGraph(string sessionId)
        {
            string key = GetSessionId(sessionId);
            if (PizzaGraphs.ContainsKey(key)) return;
            string conv = key == DefaultOfLastSessionPath ? DefaultOfLastSessionPath : GetPizzaConversationFilePath(key);
            PizzaGraphs[key] = new PizzaBread(conv, BuildDefaultCheeseList(60));
            SavePizzaGraph(key, PizzaGraphs[key]);
        }

        // 从 CheeseUI 的 Output 字典重建连线（连接关系存在节点自身）
        private void RebuildConnections()
        {
            ConnectionUI.Clear();
            foreach (var cheese in CheeseUI)
            {
                foreach (var kv in cheese.Output)
                {
                    if (kv.Value?.link == null) continue;
                    foreach (var link in kv.Value.link)
                    {
                        var target = CheeseUI.FirstOrDefault(c => c.Id == link.TargetId);
                        if (target == null) continue;
                        var conn = new CheeseConnection(cheese, kv.Key, target, link.TargetPort);
                        ConnectionUI.Add(conn);
                        BindConnection(conn);
                    }
                }
            }
        }

        private void BindConnection(CheeseConnection conn)
        {
            conn.Source.PropertyChanged += OnCheeseForConnectionChanged;
            conn.Target.PropertyChanged += OnCheeseForConnectionChanged;
        }

        private void OnCheeseForConnectionChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is not (nameof(BaseCheese.X) or nameof(BaseCheese.Y))) return;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateAllConnectionEndpoints));
        }

        private void UpdateAllConnectionEndpoints()
        {
            CheeseCanvas.UpdateLayout();
            foreach (var conn in ConnectionUI)
            {
                UpdateConnectionEndpoints(conn);
            }
        }

        private void UpdateConnectionEndpoints(CheeseConnection conn)
        {
            var srcPort = FindPortBorder(conn.Source, conn.SourcePort, isOutput: true);
            var tgtPort = FindPortBorder(conn.Target, conn.TargetPort, isOutput: false);
            if (srcPort != null)
            {
                var p = srcPort.TranslatePoint(new Point(srcPort.ActualWidth / 2, srcPort.ActualHeight / 2), ConnectionLayer);
                conn.X1 = p.X; conn.Y1 = p.Y;
            }
            if (tgtPort != null)
            {
                var p = tgtPort.TranslatePoint(new Point(tgtPort.ActualWidth / 2, tgtPort.ActualHeight / 2), ConnectionLayer);
                conn.X2 = p.X; conn.Y2 = p.Y;
            }
        }

        private Border? FindPortBorder(BaseCheese cheese, string portKey, bool isOutput)
        {
            if (CheeseCanvas.ItemContainerGenerator.ContainerFromItem(cheese) is not DependencyObject container) return null;
            return FindPortInVisualTree(container, portKey, isOutput);
        }

        private static Border? FindPortInVisualTree(DependencyObject root, string portKey, bool isOutput)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is Border b && b.Tag is string key && key == portKey && IsPortColor(b, isOutput))
                {
                    return b;
                }
                var found = FindPortInVisualTree(child, portKey, isOutput);
                if (found != null) return found;
            }
            return null;
        }

        private static bool IsPortColor(Border b, bool isOutput)
        {
            var want = isOutput ? Colors.Green : Colors.Red;
            return b.Background is SolidColorBrush brush && brush.Color == want;
        }

        private static BaseCheese? FindVisualParentCheese(DependencyObject? d)
        {
            for (var cur = d; cur != null; cur = VisualTreeHelper.GetParent(cur))
            {
                if (cur is FrameworkElement fe && fe.DataContext is BaseCheese cheese) return cheese;
            }
            return null;
        }

        private Border? HitTestPort(Point pos, bool isOutput)
        {
            var hit = VisualTreeHelper.HitTest(CheeseCanvas, pos);
            if (hit?.VisualHit is DependencyObject d)
            {
                for (var cur = d; cur != null; cur = VisualTreeHelper.GetParent(cur))
                {
                    if (cur is Border b && b.Tag is string && IsPortColor(b, isOutput)) return b;
                }
            }
            return null;
        }

        private Border? FindNearestPort(Point pos, bool isOutput)
        {
            Border? best = null;
            var bestDistance = PortSnapRadius;
            foreach (var cheese in CheeseUI)
            {
                IEnumerable<string> portKeys = isOutput ? cheese.Output.Keys : cheese.Input.Keys;
                foreach (var portKey in portKeys)
                {
                    var port = FindPortBorder(cheese, portKey, isOutput);
                    if (port == null) continue;
                    var center = port.TranslatePoint(new Point(port.ActualWidth / 2, port.ActualHeight / 2), ConnectionLayer);
                    var dx = center.X - pos.X;
                    var dy = center.Y - pos.Y;
                    var distance = Math.Sqrt(dx * dx + dy * dy);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = port;
                    }
                }
            }
            return best;
        }

        private Border? HitTestInputPort(Point pos)
        {
            return HitTestPort(pos, isOutput: false) ?? FindNearestPort(pos, isOutput: false);
        }


        private void ShowPortHover(Border port)
        {
            var center = port.TranslatePoint(new Point(port.ActualWidth / 2, port.ActualHeight / 2), ConnectionLayer);
            Canvas.SetLeft(PortHoverHighlight, center.X - PortHoverHighlight.Width / 2);
            Canvas.SetTop(PortHoverHighlight, center.Y - PortHoverHighlight.Height / 2);
            PortHoverHighlight.Visibility = Visibility.Visible;
        }

        private void HidePortHover()
        {
            PortHoverHighlight.Visibility = Visibility.Collapsed;
        }

        private void UpdatePortHover(Point pos)
        {
            var port = _isConnecting ? HitTestInputPort(pos) : null;
            if (port != null) ShowPortHover(port);
            else HidePortHover();
        }

        private void UpdatePortDrag(Point pos)
        {
            TempConnectionLine.X2 = pos.X;
            TempConnectionLine.Y2 = pos.Y;
            UpdatePortHover(pos);
        }

        private void StartPortDrag(Border port, Point start)
        {
            TempConnectionLine.X1 = TempConnectionLine.X2 = start.X;
            TempConnectionLine.Y1 = TempConnectionLine.Y2 = start.Y;
            TempConnectionLine.Visibility = Visibility.Visible;
            port.CaptureMouse();
        }

        private void RefreshConnections()
        {
            RebuildConnections();
            UpdateAllConnectionEndpoints();
            SaveCurrentPizzaGraph();
        }

        private void ClearDragState()
        {
            _isConnecting = false;
            _connectingSource = null;
            _connectingSourcePort = "";
            _rewireTargetCheese = null;
            _rewireTargetPort = "";
            _rewireOldSourceCheese = null;
            _rewireOldSourcePort = "";
            if (_rewireOldConnection != null)
            {
                _rewireOldConnection.Opacity = 1;
                _rewireOldConnection = null;
            }
        }

        // 参数编辑器里按回车（Enter）立即提交绑定，并且提交后不再停留在输入框
        private void ParameterEditor_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key is not (Key.Enter or Key.Return) || sender is not TextBox textBox) return;
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

            // 提交后直接清掉键盘焦点，不再停留在输入框
            Keyboard.ClearFocus();

            e.Handled = true;
        }

        // 参数值成功写回 CheesePara 后保存当前 Pizza 图
        private void ParameterEditor_SourceUpdated(object sender, DataTransferEventArgs e)
        {
            SaveCurrentPizzaGraph();
        }

        // 右侧积木区模板卡片：点击后克隆到当前画布
        private void CheeseTemplateDisplay_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not BaseCheese template) return;
            AddCheeseFromTemplate(template);
            e.Handled = true;
        }

        private void AddCheeseFromTemplate(BaseCheese template)
        {
            if (CurrentPage?.type != "pizza") return;
            var sc = GetOrCreateCurrentSessionCheese();

            PushUndo();

            var index = CheeseUI.Count;
            var x = 60 + (index % 5) * 220;
            var y = 80 + (index / 5) * 160;
            var cheese = CloneTemplate(template, x, y);

            sc.AddCheese(cheese);
            CheeseUI.Add(cheese);
            RebuildConnections();
            UpdateAllConnectionEndpoints();
            SaveCurrentPizzaGraph();
        }

        private (BaseCheese Source, string SourcePort)? FindIncomingLink(BaseCheese target, string targetPort)
        {
            foreach (var cheese in CheeseUI)
            {
                foreach (var kv in cheese.Output)
                {
                    if (kv.Value?.link?.Any(l => l.TargetId == target.Id && l.TargetPort == targetPort) == true)
                        return (cheese, kv.Key);
                }
            }
            return null;
        }

        private void RemoveIncomingLink(BaseCheese target, string targetPort)
        {
            var incoming = FindIncomingLink(target, targetPort);
            if (incoming is not { } old) return;
            if (old.Source.Output.TryGetValue(old.SourcePort, out var port) && port?.link != null)
            {
                port.link.RemoveAll(l => l.TargetId == target.Id && l.TargetPort == targetPort);
            }

        }

        public void RemoveIncomingLinkPublic(BaseCheese target, string targetPort)
        {
            if (FindIncomingLink(target, targetPort) == null) return;
            RemoveIncomingLink(target, targetPort);
            RefreshConnections();
        }

        private void AddLink(BaseCheese source, string sourcePort, BaseCheese target, string targetPort)
        {
            if (!source.Output.TryGetValue(sourcePort, out var port) || port == null)
            {
                port = new CheesePort (false);
                source.Output[sourcePort] = port;
            }
            port.link ??= new List<CheesePortLink>();
            if (!port.link.Any(l => l.TargetId == target.Id && l.TargetPort == targetPort))
                port.link.Add(new CheesePortLink { TargetId = target.Id, TargetPort = targetPort });
        }

        private void AddConnection(BaseCheese source, string sourcePort, BaseCheese target, string targetPort)
        {
            if (source.Output.TryGetValue(sourcePort, out var existing) &&
                existing?.link?.Any(l => l.TargetId == target.Id && l.TargetPort == targetPort) == true) return;

            PushUndo();
            AddLink(source, sourcePort, target, targetPort);
            RefreshConnections();
        }
        private bool IsPointInsideCheeseDeleteZone(Point pos)
        {
            return pos.X >= 0 && pos.Y >= 0 &&
                   pos.X <= CheeseDeleteZone.ActualWidth &&
                   pos.Y <= CheeseDeleteZone.ActualHeight;
        }

        private void UpdateCheeseDeleteZoneVisual(bool inside)
        {
            if (_isCheeseOverDeleteZone == inside) return;
            _isCheeseOverDeleteZone = inside;

            CheeseDeleteZone.Background = inside
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x8A))
                : new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0xA0));
            CheeseDeleteHint.Text = inside ? "松开删除" : "拖到这里删除";
            CheeseDeleteHint.Foreground = inside
                ? Brushes.White
                : new SolidColorBrush(Color.FromRgb(0x88, 0x44, 0x44));
        }

        private void RemoveLinksToCheese(BaseCheese removed)
        {
            foreach (var cheese in CheeseUI)
            {
                if (ReferenceEquals(cheese, removed)) continue;
                foreach (var kv in cheese.Output)
                {
                    kv.Value?.link?.RemoveAll(l => l.TargetId == removed.Id);
                }
            }
        }

        private void CheeseNameBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not BaseCheese cheese) return;
            _isCheeseDragging = true;
            _draggingCheese = cheese;
            _cheeseDragStart = e.GetPosition(CheeseCanvas);
            _dragSnapshot = CloneCheeseList();
            _dragOriginalX = cheese.X;
            _dragOriginalY = cheese.Y;
            UpdateCheeseDeleteZoneVisual(false);
            fe.CaptureMouse();
            e.Handled = true;
        }

        private void CheeseNameBar_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isCheeseDragging || _draggingCheese == null) return;
            var pos = e.GetPosition(CheeseCanvas);
            _draggingCheese.X += pos.X - _cheeseDragStart.X;
            _draggingCheese.Y += pos.Y - _cheeseDragStart.Y;
            _cheeseDragStart = pos;

            var deletePos = e.GetPosition(CheeseDeleteZone);
            UpdateCheeseDeleteZoneVisual(IsPointInsideCheeseDeleteZone(deletePos));
        }

        private void CheeseNameBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isCheeseDragging) return;
            if (sender is FrameworkElement fe) fe.ReleaseMouseCapture();

            var cheese = _draggingCheese;
            var snapshot = _dragSnapshot;
            var moved = cheese != null &&
                        (cheese.X != _dragOriginalX || cheese.Y != _dragOriginalY);
            var overDeleteZone = IsPointInsideCheeseDeleteZone(e.GetPosition(CheeseDeleteZone));

            _dragSnapshot = null;
            _isCheeseDragging = false;
            _draggingCheese = null;
            UpdateCheeseDeleteZoneVisual(false);

            if (overDeleteZone && cheese != null)
            {
                var sc = CurrentSessionCheese();
                if (sc != null && snapshot != null)
                {
                    sc.UndoStack.Push(snapshot);
                    sc.RedoStack.Clear();
                    sc.RemoveCheese(cheese);
                }
                CheeseUI.Remove(cheese);
                RemoveLinksToCheese(cheese);
                RebuildConnections();
                UpdateAllConnectionEndpoints();
                SaveCurrentPizzaGraph();
                return;
            }

            if (moved && snapshot != null)
            {
                var sc = CurrentSessionCheese();
                if (sc != null)
                {
                    sc.UndoStack.Push(snapshot);
                    sc.RedoStack.Clear();
                }
            }
            SaveCurrentPizzaGraph();
        }

        #endregion

        #region 连线交互
        private void OutputPort_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border port || port.Tag is not string sourcePort) return;
            var source = FindVisualParentCheese(port);
            if (source == null) return;

            _isConnecting = true;
            _connectingSource = source;
            _connectingSourcePort = sourcePort;
            _rewireTargetCheese = null;
            _rewireTargetPort = "";
            _rewireOldSourceCheese = null;
            _rewireOldSourcePort = "";
            _rewireOldConnection = null;

            var p = port.TranslatePoint(new Point(port.ActualWidth / 2, port.ActualHeight / 2), ConnectionLayer);
            StartPortDrag(port, p);
            e.Handled = true;
        }

        private void Window_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isConnecting) return;
            UpdatePortDrag(e.GetPosition(ConnectionLayer));
        }

        private void OutputPort_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isConnecting) return;
            UpdatePortDrag(e.GetPosition(ConnectionLayer));
        }

        private void InputPort_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isConnecting || _rewireTargetCheese == null) return;
            UpdatePortDrag(e.GetPosition(ConnectionLayer));
        }

        private void OutputPort_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isConnecting || _rewireTargetCheese != null) return;

            if (sender is Border port) port.ReleaseMouseCapture();
            var pos = e.GetPosition(ConnectionLayer);
            TempConnectionLine.Visibility = Visibility.Collapsed;
            HidePortHover();

            var source = _connectingSource;
            var sourcePort = _connectingSourcePort;
            ClearDragState();

            var targetPortBorder = HitTestInputPort(pos);
            if (source != null && targetPortBorder?.Tag is string targetPort)
            {
                var target = FindVisualParentCheese(targetPortBorder);
                if (target != null && target != source)
                {
                    AddConnection(source, sourcePort, target, targetPort);
                }
            }

            e.Handled = true;
        }

        private void InputPort_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border port || port.Tag is not string targetPort) return;
            var target = FindVisualParentCheese(port);
            if (target == null) return;

            var incoming = FindIncomingLink(target, targetPort);
            BaseCheese? oldSource = null;
            string oldSourcePort = "";
            CheeseConnection? oldConnection = null;

            var portCenter = port.TranslatePoint(new Point(port.ActualWidth / 2, port.ActualHeight / 2), ConnectionLayer);
            var start = portCenter;

            if (incoming is { } old)
            {
                oldSource = old.Source;
                oldSourcePort = old.SourcePort;
                oldConnection = ConnectionUI.FirstOrDefault(c =>
                    c.Source == old.Source && c.SourcePort == old.SourcePort &&
                    c.Target == target && c.TargetPort == targetPort);
                if (oldConnection != null) oldConnection.Opacity = 0;

                var oldPort = FindPortBorder(old.Source, old.SourcePort, isOutput: true);
                if (oldPort != null)
                {
                    start = oldPort.TranslatePoint(new Point(oldPort.ActualWidth / 2, oldPort.ActualHeight / 2), ConnectionLayer);
                }
            }

            _isConnecting = true;
            _connectingSource = null;
            _connectingSourcePort = "";
            _rewireTargetCheese = target;
            _rewireTargetPort = targetPort;
            _rewireOldSourceCheese = oldSource;
            _rewireOldSourcePort = oldSourcePort;
            _rewireOldConnection = oldConnection;

            StartPortDrag(port, start);
            UpdatePortHover(portCenter);
            e.Handled = true;
        }

        private void InputPort_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isConnecting || _rewireTargetCheese == null) return;

            if (sender is Border port) port.ReleaseMouseCapture();
            var pos = e.GetPosition(ConnectionLayer);
            TempConnectionLine.Visibility = Visibility.Collapsed;
            HidePortHover();

            var target = _rewireTargetCheese;
            var targetPort = _rewireTargetPort;
            var oldSource = _rewireOldSourceCheese;
            var oldSourcePort = _rewireOldSourcePort;
            ClearDragState();

            if (target == null) return;

            var inputPortBorder = HitTestInputPort(pos);
            if (inputPortBorder?.Tag is string newTargetPort)
            {
                var newTarget = FindVisualParentCheese(inputPortBorder);
                var isSamePort = newTarget == target && newTargetPort == targetPort;
                if (newTarget != null && !isSamePort && oldSource != null)
                {
                    PushUndo();
                    RemoveIncomingLink(target, targetPort);
                    AddLink(oldSource, oldSourcePort, newTarget, newTargetPort);
                    RefreshConnections();
                }
            }
            else if (oldSource != null)
            {
                PushUndo();
                RemoveIncomingLink(target, targetPort);
                RefreshConnections();
            }

            e.Handled = true;
        }
        #endregion
    }
}
