using System.Threading;
using System.Threading.Tasks;

namespace HotRepl.UnityCommands.Screenshots;

internal interface IUnityScreenshotCapturer
{
    ValueTask<UnityScreenshotCaptureResult> CaptureAsync(
        int superSize,
        CancellationToken cancellationToken
    );
}
