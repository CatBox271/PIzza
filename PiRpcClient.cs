using System;
using System.Diagnostics;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;


namespace PiWpfUi
{
    /// <summary>
    /// Pi 的 RPC 桥接客户端:
    /// 启动 pi 子进程,通过管道与它通信(你发命令,它回事件流)。
    /// 事件流是 JSONL 格式:每行一个 JSON 事件。
    /// </summary>
    public sealed class PiRpcClient
    {
        public Process? process;// pi 子进程

        //仅仅只是启动
        public bool Start(string session_dir,string? session_id)
        {
            //配置
            string arguments = $"--mode rpc --provider deepseek --model deepseek-v4-flash --session-dir \"{session_dir}\"";
            if (!string.IsNullOrEmpty(session_id))
            {
                arguments += $" --session-id \"{session_id}\"";//在假设文件管理完美的情况下，不会出现想打开却创建
            }
            process = new Process()
            {
                StartInfo = new ProcessStartInfo()
                {
                    FileName = "pi.cmd",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    StandardInputEncoding = new UTF8Encoding(false),   // 不是 Encoding.UTF8!
                    StandardOutputEncoding = new UTF8Encoding(false),
                    WindowStyle = ProcessWindowStyle.Hidden,
                },
            };
            string? key = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
            if(!string.IsNullOrEmpty(key)) process.StartInfo.Environment["DEEPSEEK_API_KEY"] = key;
            return process.Start();
        }
    }
}
