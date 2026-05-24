#nullable disable

namespace HotRepl.UnityCommands.Screenshots;

internal sealed class CapturedScreenshot
{
    public CapturedScreenshot(int width, int height, byte[] png)
    {
        Width = width;
        Height = height;
        Png = png;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Png { get; }
}
