// ArgEcho.cs —— 测试辅助程序：把自己收到的命令行参数写入
// %TEMP%\openchoose-argecho.txt，用于验证 PPTChooser 是否按
// “xxx.exe "文件.pptx"” 的约定启动外部程序。
//
// 编译： csc /nologo /target:exe /out:ArgEcho.exe ArgEcho.cs
using System;
using System.IO;
using System.Text;

internal static class ArgEcho
{
    private static void Main(string[] args)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("count=" + args.Length);
        for (int i = 0; i < args.Length; i++)
        {
            builder.AppendLine("arg[" + i + "]=" + args[i]);
        }

        builder.AppendLine("exists=" + (args.Length > 0 && File.Exists(args[0])));

        string target = Path.Combine(Path.GetTempPath(), "openchoose-argecho.txt");
        File.WriteAllText(target, builder.ToString(), Encoding.UTF8);
    }
}
