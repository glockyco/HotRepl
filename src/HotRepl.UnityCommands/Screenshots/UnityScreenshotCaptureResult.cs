#nullable disable

namespace HotRepl.UnityCommands.Screenshots;

internal sealed class UnityScreenshotCaptureResult
{
    private UnityScreenshotCaptureResult(
        CapturedScreenshot screenshot,
        UnityScreenshotFailureKind failureKind
    )
    {
        Screenshot = screenshot;
        FailureKind = failureKind;
    }

    public bool Succeeded => FailureKind == UnityScreenshotFailureKind.None;

    public CapturedScreenshot Screenshot { get; }

    public UnityScreenshotFailureKind FailureKind { get; }

    public static UnityScreenshotCaptureResult Success(CapturedScreenshot screenshot) =>
        new(screenshot, UnityScreenshotFailureKind.None);

    public static UnityScreenshotCaptureResult Failed(UnityScreenshotFailureKind failureKind) =>
        new(null, failureKind);
}
