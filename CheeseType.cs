using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PiWpfUi
{
    #region 芝士类型
    public enum WaitType
    {
        Any,
        All,
        Assign,//在WaitDraft里指定必须等的项目
        OutProgram,
        OutHttp,
    }
    //工作类型
    public enum WorkType
    {
        UserMessage,
        MessageDisplay,
        Agent,
        AgentStream,
        InputCombiner,
        Text,
        Time,
        Clock,
        OutProgram,
        OutHttp,
        Merge,
        TestPopup,
        RegexReplace,
        RegexExtract,
        Contains,
        FileWriter,
        LLM,
        LLMStream,
    }

    public enum OutType
    {
        Any,//任何Output
        All,//等待所有Output
        OutProgram,
        OutHttp,
    }


    #region Para
    public enum DealParaType
    {
        InputSpawner,
        Addition,
        OutProgram,
        OutHttp,
    }

    public enum CheeseParaType
    {
        String,
        Int,
        Float,
        Bool,
        Select,
    }
    public class CheesePara : ObservableObject
    {
        private string _name = "";
        public string Name { get => _name; set => SetProperty(ref _name, value); }

        private string? _description = null;
        public string? Description { get => _description; set => SetProperty(ref _description, value); }//鼠标移动上去后显示

        private CheeseParaType _type;
        public CheeseParaType Type { get => _type; set => SetProperty(ref _type, value); }

        private string? _string = null;
        public string? String { get => _string; set => SetProperty(ref _string, value); }

        private int? _int = null;
        public int? Int { get => _int; set => SetProperty(ref _int, value); }

        private float? _float = null;
        public float? Float { get => _float; set => SetProperty(ref _float, value); }

        private bool? _bool = null;
        public bool? Bool { get => _bool; set => SetProperty(ref _bool, value); }

        private List<string>? _options = null;
        public List<string>? Options { get => _options; set => SetProperty(ref _options, value); }

        private List<DealParaType>? _paraDealer = null;
        public List<DealParaType>? ParaDealer { get => _paraDealer; set => SetProperty(ref _paraDealer, value); }//读取时自动加载添加处理方案
    }

    // 可通知的参数字典：Add/Remove/索引器赋值/Clear 时触发 Changed
    public class CheeseParaDictionary : Dictionary<string, CheesePara>
    {
        public event Action? Changed;

        public new void Add(string key, CheesePara value)
        {
            base.Add(key, value);
            Changed?.Invoke();
        }

        public new CheesePara? this[string key]
        {
            get => TryGetValue(key, out var value) ? value : null;
            set
            {
                var added = !TryGetValue(key, out var old);
                base[key] = value!;
                if (added || !ReferenceEquals(old, value)) Changed?.Invoke();
            }
        }

        public new bool TryAdd(string key, CheesePara value)
        {
            if (!base.TryAdd(key, value)) return false;
            Changed?.Invoke();
            return true;
        }

        public new bool Remove(string key)
        {
            if (!base.Remove(key)) return false;
            Changed?.Invoke();
            return true;
        }

        public new void Clear()
        {
            if (Count == 0) return;
            base.Clear();
            Changed?.Invoke();
        }
    }
    #endregion

    #endregion
}
