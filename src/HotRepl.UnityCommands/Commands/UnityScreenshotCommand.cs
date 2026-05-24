#nullable disable

using System;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Control;
using HotRepl.UnityCommands.Models;
using HotRepl.UnityCommands.Screenshots;

namespace HotRepl.UnityCommands.Commands;

/// <summary>Captures the current frame as a PNG command artifact.</summary>
[ControlCommand(UnityCommandCatalogNames.ScreenshotCapture, Kind = ControlCommandKind.Job)]
[ControlCommandArtifact("screenshot", ContentType = "image/png", Required = true)]
public sealed class UnityScreenshotCommand
    : IControlCommandHandler<UnityScreenshotArgs, UnityScreenshotResult>
{
    private readonly IUnityScreenshotCapturer _capturer;

    /// <summary>Creates a screenshot command without a loader coroutine host.</summary>
    public UnityScreenshotCommand()
        : this(UnsupportedUnityScreenshotCapturer.Instance) { }

    internal UnityScreenshotCommand(IUnityScreenshotCapturer capturer)
    {
        _capturer = capturer ?? throw new ArgumentNullException(nameof(capturer));
    }

    /// <inheritdoc />
    public string Name => UnityCommandCatalogMetadata.ScreenshotCapture.Name;

    /// <inheritdoc />
    public int Version => 1;

    /// <inheritdoc />
    public ControlCommandKind Kind => UnityCommandCatalogMetadata.ScreenshotCapture.Kind;

    /// <inheritdoc />
    public bool MutatesState => UnityCommandCatalogMetadata.ScreenshotCapture.MutatesState;

    /// <inheritdoc />
    public async ValueTask<ControlCommandResult<UnityScreenshotResult>> ExecuteAsync(
        ControlCommandContext<UnityScreenshotResult> context,
        UnityScreenshotArgs args,
        CancellationToken cancellationToken
    )
    {
        var capture = await _capturer
            .CaptureAsync(Math.Max(1, args.SuperSize), cancellationToken)
            .ConfigureAwait(true);
        if (!capture.Succeeded)
        {
            return Failure(context, capture.FailureKind);
        }

        var screenshot = capture.Screenshot;
        var artifact = await context
            .Artifacts.AttachBytesAsync(
                "screenshot",
                screenshot.Png,
                "image/png",
                cancellationToken
            )
            .ConfigureAwait(true);

        return context.Ok(
            new UnityScreenshotResult { Width = screenshot.Width, Height = screenshot.Height },
            "screenshot",
            artifact
        );
    }

    private static ControlCommandResult<UnityScreenshotResult> Failure(
        ControlCommandContext<UnityScreenshotResult> context,
        UnityScreenshotFailureKind failureKind
    ) =>
        failureKind switch
        {
            UnityScreenshotFailureKind.PngEncodingUnsupported => context.PreconditionFailed(
                "pngEncodingUnsupported",
                "Unity PNG encoding is not available in this runtime."
            ),
            UnityScreenshotFailureKind.SuperSizeUnsupported => context.PreconditionFailed(
                "screenshotSuperSizeUnsupported",
                "This Unity runtime does not expose supersampled screenshot capture; retry with superSize = 1."
            ),
            _ => context.PreconditionFailed(
                "screenshotUnsupported",
                "A loader coroutine host is required for end-of-frame screenshot capture."
            ),
        };
}
