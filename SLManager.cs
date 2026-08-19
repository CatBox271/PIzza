using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace PiWpfUi;

/// <summary>
/// SLManager 桌面端移植版（原 Unity SLManager：泛型 JSON 存取）。
/// 适配 .NET 8 WPF：用 System.Text.Json，零 NuGet 依赖；
/// 持久化根目录 = exe 同目录 PersistentData（与项目现有习惯一致）。
/// </summary>
public static class SLManager
{
    private static string _persistentDataPath = "";

    /// <summary>
    /// 默认持久化根目录：exe 同目录 PersistentData（自动补建目录）
    /// </summary>
    public static string PersistentDataPath
    {
        get
        {
            if (_persistentDataPath == "")
            {
                _persistentDataPath = Path.Combine(AppContext.BaseDirectory, "PersistentData");
                Directory.CreateDirectory(_persistentDataPath);
            }
            return _persistentDataPath;
        }
    }

    /// <summary>
    /// 导出任意对象到 JSON 文件，返回完整路径；失败返回 null
    /// </summary>
    public static string? ExportToJson<T>(T data, string folderPath = "", string? fileName = null, bool prettyPrint = true)
    {
        try
        {
            if (data == null)
            {
                Console.WriteLine("[SLManager] 导出数据为空");
                return null;
            }

            var options = new JsonSerializerOptions { WriteIndented = prettyPrint };
            string json = JsonSerializer.Serialize(data, options);
            if (string.IsNullOrEmpty(json))
            {
                Console.WriteLine("[SLManager] JSON序列化失败");
                return null;
            }

            // 文件名由调用方带全（含 .json 后缀），SLManager 不再自动补
            string actualFileName = string.IsNullOrEmpty(fileName) ?
                GenerateDefaultFileName() : fileName;

            string basePath = PersistentDataPath;
            if (!string.IsNullOrEmpty(folderPath))
            {
                basePath = Path.Combine(basePath, folderPath);
            }

            if (!Directory.Exists(basePath))
            {
                Directory.CreateDirectory(basePath);
            }

            string filePath = Path.Combine(basePath, actualFileName);
            File.WriteAllText(filePath, json, new UTF8Encoding(false));

            Console.WriteLine($"[SLManager] ✓ 导出成功: {filePath}");
            return filePath;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[SLManager] 导出失败: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// 确认 PersistentData 下相对路径的父目录存在；文件已存在返回完整路径，否则空串
    /// </summary>
    public static string ConfirmPersistentDataPath(string file)
    {
        string root = PersistentDataPath;

        string file_dir = Path.GetDirectoryName(file) ?? "";//相对
        if (!string.IsNullOrEmpty(file_dir))
        {
            file_dir = Path.Combine(root, file_dir);//绝对
            if (!Directory.Exists(file_dir)) Directory.CreateDirectory(file_dir);
        }

        string file_path = Path.Combine(root, file);
        if (File.Exists(file_path)) return file_path;

        return string.Empty;
    }

    private static string GenerateDefaultFileName()
    {
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        return $"conversation_{timestamp}.json";
    }

    /// <summary>
    /// 从 JSON 文件导入对象；失败返回 default
    /// </summary>
    public static T? ImportFromJson<T>(string folderPath, string fileName) where T : new()
    {
        try
        {
            string basePath = Path.Combine(PersistentDataPath, folderPath, fileName);
            if (!File.Exists(basePath))
            {
                Console.WriteLine($"[SLManager] 文件不存在: {basePath}");
                return default;
            }

            string json = File.ReadAllText(basePath, new UTF8Encoding(false));
            if (string.IsNullOrEmpty(json))
            {
                Console.WriteLine("[SLManager] 文件为空");
                return default;
            }

            T? data = JsonSerializer.Deserialize<T>(json);
            if (data == null)
            {
                Console.WriteLine("[SLManager] 文件解析失败");
                return default;
            }

            Console.WriteLine($"[SLManager] ✓ {typeof(T).Name} 导入成功: {basePath}");
            return data;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[SLManager] 导入失败: {e.Message}");
            return default;
        }
    }
}
