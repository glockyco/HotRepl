using System.Threading;
using System.Threading.Tasks;

namespace HotRepl.UnityCommands.Screenshots;

internal sealed class UnsupportedUnityScreenshotCapturer : IUnityScreenshotCapturer
{
    public static readonly UnsupportedUnityScreenshotCapturer Instance = new();

    private UnsupportedUnityScreenshotCapturer() { }

    public ValueTask<UnityScreenshotCaptureResult> CaptureAsync(
        int superSize,
        CancellationToken cancellationToken
    ) => new(UnityScreenshotCaptureResult.Failed(UnityScreenshotFailureKind.CaptureUnsupported));
}
