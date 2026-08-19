using System.Text.Json;

namespace PiWpfUi;

public class SessionItem
{
    public string Name { get; set; } = "";
    public string File { get; set; } = "";
    public SessionItem() { }
    public SessionItem(string name, string file = "")
    {
        Name = name;
        File = file;
    }
}

public class ToolItem
{
    public string tool_id { get; set; } = "";
    public string tool_name { get; set; } = "";
    public string? tool_exec { get; set; } = "";
    public string? tool_result { get; set; } = "";
    public bool? isError { get; set; } = false;
}

/// <summary>
/// 上次会话状态持久化：记住关闭前在看哪个会话
/// </summary>
public class LastState
{
    public string SessionPath { get; set; } = "NULL";

    public static LastState LoadLastState()
    {
        return SLManager.ImportFromJson<LastState>("", "LastState.json") ?? new();
    }

    public void SaveLastState()
    {
        SLManager.ExportToJson(this, "", "LastState.json");
    }
}

public class BasicMessageItem
{
    public string Text { get; set; } = "";
    public string Reasoning { get; set; } = "";
    public string Role { get; set; } = "system";
    public List<ToolItem>? Tools { get; set; } = new();

    public int MessageIndex = -1;
    public BasicMessageItem() { }
    public BasicMessageItem(string role, string text, string reasoning = "")
    {
        Text = text;
        Role = role;
        Reasoning = reasoning;
    }

    public BasicMessageItem(MonoMessage mono, int index)
    {
        Role = mono.role;
        Text = mono.text ?? "";
        Reasoning = mono.reasoning ?? "";
        MessageIndex = index;

        if (mono.tools != null && mono.tools.Count > 0)
        {
            Tools = new List<ToolItem>();
            foreach (var t in mono.tools)
            {
                Tools.Add(new ToolItem
                {
                    tool_id = t.tool_id,
                    tool_name = t.tool_name,
                    tool_exec = t.tool_exec,
                    tool_result = t.tool_result,
                    isError = t.isError
                });
            }
        }
    }
}
