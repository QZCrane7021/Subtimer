using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using FFmpeg.AutoGen;

namespace Subtimer;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 1. 优先配置 FFmpeg 的路径
        SetupFFmpegPath();

        // 2. 🌟 核心升级：为 BASS 音频库配置全系统自适应重定向
        SetupBassResolver();

        // 3. 启动 Avalonia 界面
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void SetupFFmpegPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            ffmpeg.RootPath = Directory.Exists("/opt/homebrew/lib") ? "/opt/homebrew/lib" : "/usr/local/lib";
        }
        // ... 其他平台的 FFmpeg 路径保持你之前的配置即可 ...
    }

    /// <summary>
    /// 安排全系统的 BASS 动态库精准加载与拦截
    /// </summary>
    private static void SetupBassResolver()
    {
        // 拦截 ManagedBass 程序集对 "bass" 动态库的底层请求
        NativeLibrary.SetDllImportResolver(typeof(ManagedBass.Bass).Assembly, (libraryName, assembly, searchPath) =>
        {
            // 只有当底层尝试加载名为 "bass" 的库时才介入处理
            if (libraryName.Equals("bass", StringComparison.OrdinalIgnoreCase))
            {
                string baseDir = AppContext.BaseDirectory;
                string rid = "";
                string actualFileName = "";

                // 1. 判定当前运行的操作系统，并映射到对应的规范目录与文件名
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    rid = "win-x64";
                    actualFileName = "bass.dll";
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    // Mac 平台支持判断：目前主流为 arm64，也可根据 RuntimeInformation.ProcessArchitecture 分流
                    rid = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
                    actualFileName = "libbass.dylib"; 
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    rid = "linux-x64";
                    actualFileName = "libbass.so";
                }

                // 2. 拼接出你在 .csproj 中指定的标准运行路径
                string libraryPath = Path.Combine(baseDir, "runtimes", rid, "native", actualFileName);

                // 3. 检查文件是否存在，存在则实施最高优先级硬加载
                if (File.Exists(libraryPath))
                {
                    return NativeLibrary.Load(libraryPath);
                }

                // 🌟 Mac 专属兜底：防止有些版本的 BASS 库没有 "lib" 前缀
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    string fallbackMacPath = Path.Combine(baseDir, "runtimes", rid, "native", "bass.dylib");
                    if (File.Exists(fallbackMacPath))
                    {
                        return NativeLibrary.Load(fallbackMacPath);
                    }
                }
            }

            // 返回 IntPtr.Zero 代表未命中，交还给系统默认机制处理（比如留空未装载的系统）
            return IntPtr.Zero; 
        });
    }
}