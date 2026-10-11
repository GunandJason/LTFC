namespace BetterClickPPT.MockShow;

/// <summary>
/// 模拟“全屏放映窗口”的小工具，用于自动化测试：
/// 窗口类名默认为 screenClass（与 PowerPoint 放映窗口一致），
/// 会把收到的鼠标点击与按键记录到日志文件，便于断言“点击是否被增强器拦截 / 是否收到下一页按键”。
/// </summary>
internal sealed class MockShowOptions
{
    public string? LogPath { get; private set; }

    public bool FullScreen { get; private set; }

    public string ClassName { get; private set; } = "screenClass";

    public string Title { get; private set; } = "PowerPoint Slide Show - [mock]";

    public int Seconds { get; private set; } = 10;

    public int X { get; private set; } = 80;

    public int Y { get; private set; } = 80;

    public int Width { get; private set; } = 900;

    public int Height { get; private set; } = 600;

    public static MockShowOptions Parse(string[] args)
    {
        var options = new MockShowOptions();

        for (var i = 0; i < args.Length; i++)
        {
            string? Take()
                => i + 1 < args.Length ? args[++i] : null;

            switch (args[i])
            {
                case "--log":
                    options.LogPath = Take();
                    break;
                case "--fullscreen":
                    options.FullScreen = true;
                    break;
                case "--class":
                    options.ClassName = Take() ?? options.ClassName;
                    break;
                case "--title":
                    options.Title = Take() ?? options.Title;
                    break;
                case "--seconds":
                    options.Seconds = int.TryParse(Take(), out var seconds) ? seconds : options.Seconds;
                    break;
                case "--x":
                    options.X = int.TryParse(Take(), out var x) ? x : options.X;
                    break;
                case "--y":
                    options.Y = int.TryParse(Take(), out var y) ? y : options.Y;
                    break;
                case "--width":
                    options.Width = int.TryParse(Take(), out var width) ? width : options.Width;
                    break;
                case "--height":
                    options.Height = int.TryParse(Take(), out var height) ? height : options.Height;
                    break;
            }
        }

        return options;
    }
}
