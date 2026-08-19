using System;
using System.Collections.Generic;
using System.DirectoryServices.ActiveDirectory;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.TextFormatting;
using System.Windows.Shapes;
using static PiWpfUi.MonoMessage;

namespace PiWpfUi
{
    public class MonoMessage()
    {
        public string time_stamp = ""; //json:timestamp 外层事件行时间(ISO字符串)
        public string role = "";       //json:role user/assistant/toolResult


        #region Content内容
        public string? text = "";       //json:content[] text块的 text
        public string? reasoning = "";  //json:content[] thinking块的 thinking

        public StringBuilder text_builder = new();
        public StringBuilder reasoning_builder = new();

        public bool thinking_finish = true;
        public bool text_finish = true;

        public void ApplyTextBuilder(string delta,bool clear = false)
        {
            text_builder.Append(delta);
            text = text_builder.ToString();
            if(clear) text_builder.Clear();
        }
        public void ApplyReasoningBuilder(string delta, bool clear = false)
        {
            reasoning_builder.Append(delta);
            reasoning = reasoning_builder.ToString();
            if(clear) reasoning_builder.Clear();
        }

        #region Tool
        public class ToolInfo
        {
            public string tool_id = "";   //json:toolCallId
            public string tool_name = "";      //json:toolName
            public string? tool_exec = "";   //json:content[] toolCall块的 arguments(assistant)
            public string? tool_result = "";    //json:content[] text块的 text(toolResult消息)//显示的时候向上查询更新UI
            public bool? isError = false;        //json:isError
            public ToolInfo() { }

            public ToolInfo(string tool_id, string tool_name, string? tool_exec, string? tool_result, bool? isError)
            {
                this.tool_id = tool_id;
                this.tool_name = tool_name;
                this.tool_exec = tool_exec;
                this.tool_result = tool_result;
                this.isError = isError;
            }
        }

        public List<ToolInfo>? tools;

        #endregion
        #endregion

        #region 模型信息
        public string? api = "";            //json:api
        public string? provider = "";       //json:provider
        public string? model = "";          //json:model
        public long? total_token = 0;       //json:usage.totalTokens
        #endregion

        public string? stopReason = "";         //json:stopReason stop/toolUse/error

        public void AddTool(string tool_id,string tool_name,string? tool_exec,string? tool_result,bool? isError)
        { 
            if(tools == null) tools = new();
            tools.Add(new ToolInfo(tool_id, tool_name, tool_exec, tool_result, isError));
        }
    }

    internal class MessageManager
    {
        public static MessageManager Instance { get; set; } = new();

        public Dictionary<string, List<MonoMessage>> MessagesCache = new();//string 是FilePath

        public void ForceAdd(string file_path, MonoMessage message)
        {
            if (!MessagesCache.ContainsKey(file_path) || MessagesCache[file_path] == null)
            {
                MessagesCache[file_path] = GetMessages(file_path, true) ?? new();
            }
            MessagesCache[file_path].Add(message);
        }

        public List<MonoMessage>? GetMessages(string file_path,bool force_refresh)
        {
            if (force_refresh || !MessagesCache.ContainsKey(file_path))
            {
                if (!File.Exists(file_path)) return null;
                string[] json = File.ReadAllLines(file_path);
                //解析
                List<MonoMessage> monoMessages = new List<MonoMessage>();
                foreach (string line in json)
                {
                    var mono_result = GetMessage(line,out var tool_result);
                    if (null != mono_result) monoMessages.Add(mono_result);
                    if (tool_result != null)
                    {
                        for (int i = monoMessages.Count - 1; i > -1; i--)
                        {
                            var find = monoMessages[i];
                            if (find.tools != null && find.tools.Count > 0)
                            {
                                foreach (var aim in find.tools)
                                {
                                    if (tool_result.tool_id == aim.tool_id)
                                    {
                                        aim.tool_result = tool_result.tool_result;
                                        aim.isError = tool_result.isError;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
                MessagesCache[file_path] = monoMessages;
            }
            if (!MessagesCache.ContainsKey(file_path)) return null;
            return MessagesCache[file_path];
        }

        public MonoMessage? GetMessage(string mono_json,out ToolInfo? toolResults)
        {
            toolResults = null;
            MonoMessage ms = new();
            try
            {
                JsonDocument document = JsonDocument.Parse(mono_json);
                var root = document.RootElement;
                if (root.GetProperty("type").GetString() != "message") return null;
                //new
                ms = new();
                //timestamp
                ms.time_stamp = root.GetProperty("timestamp").GetString() ?? "";
                //解析message
                var rm = root.GetProperty("message");
                //role
                ms.role = rm.GetProperty("role").GetString() ?? "";
                if (ms.role == "toolResult")
                {
                    ms.AddTool(
                        rm.GetProperty("toolCallId").GetString() ?? "",   // tool_id   ← json:toolCallId
                        rm.GetProperty("toolName").GetString() ?? "",     // tool_name ← json:toolName
                        null,                                              // tool_exec  结果消息里没有,填 null
                        rm.GetProperty("content").EnumerateArray().First().GetProperty("text").GetString(),// tool_result
                        rm.GetProperty("isError").GetBoolean()             // isError  ← json:isError
                        );
                    toolResults = ms.tools!.Count > 0 ? ms.tools[0] : null;
                    return ms;
                }
                //解析Content
                var content = rm.GetProperty("content");//这里获得的是[{},{}],怎么继续解析
                foreach (var block in content.EnumerateArray())
                {
                    switch (block.GetProperty("type").GetString())
                    {
                        case "text":
                            if (ms.role == "toolResult" && ms.tools != null && ms.tools.Count > 0)
                            {
                                ms.tools[ms.tools.Count - 1].tool_result = block.GetProperty("text").GetString();
                            }
                            else
                            {
                                ms.text = block.GetProperty("text").GetString();
                            }
                            break;
                        case "thinking":
                            ms.reasoning = block.GetProperty("thinking").GetString();
                            break;
                        case "toolCall":
                            if (ms.tools == null) ms.tools = new();
                            ms.AddTool(
                                block.GetProperty("id").GetString() ?? "",          // tool_id   ← json:id
                                block.GetProperty("name").GetString() ?? "",        // tool_name ← json:name
                                block.GetProperty("arguments").ToString(),          // tool_exec ← json:arguments 直接转 string
                                null,   // tool_result  结果还没回来,填 null
                                null    // isError      结果还没回来,填 null
                                );
                            break;
                    }
                }
                return ms;
            }
            catch
            {
                
                return null;
            }
        }

    }
}
