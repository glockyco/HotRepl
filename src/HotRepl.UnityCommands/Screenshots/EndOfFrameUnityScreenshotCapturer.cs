#nullable disable

using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace HotRepl.UnityCommands.Screenshots;

internal sealed class EndOfFrameUnityScreenshotCapturer : IUnityScreenshotCapturer
{
    private static readonly Lazy<MethodInfo> CaptureScreenshotAsTextureMethod = new(
        ResolveCaptureScreenshotAsTexture
    );

    private readonly Action<IEnumerator> _startCoroutine;

    public EndOfFrameUnityScreenshotCapturer(Action<IEnumerator> startCoroutine)
    {
        _startCoroutine = startCoroutine ?? throw new ArgumentNullException(nameof(startCoroutine));
    }

    public ValueTask<UnityScreenshotCaptureResult> CaptureAsync(
        int superSize,
        CancellationToken cancellationToken
    )
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new ValueTask<UnityScreenshotCaptureResult>(
                Task.FromCanceled<UnityScreenshotCaptureResult>(cancellationToken)
            );
        }

        var source = new TaskCompletionSource<UnityScreenshotCaptureResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var cancellation = cancellationToken.CanBeCanceled
            ? cancellationToken.Register(CancelCapture, source)
            : default;

        try
        {
            _startCoroutine(CaptureAfterEndOfFrame(Math.Max(1, superSize), source, cancellation));
        }
        catch (Exception ex)
        {
            cancellation.Dispose();
            source.TrySetException(ex);
        }

        return new ValueTask<UnityScreenshotCaptureResult>(source.Task);
    }

    private static void CancelCapture(object state)
    {
        var source = (TaskCompletionSource<UnityScreenshotCaptureResult>)state;
        source.TrySetCanceled();
    }

    private static IEnumerator CaptureAfterEndOfFrame(
        int superSize,
        TaskCompletionSource<UnityScreenshotCaptureResult> source,
        CancellationTokenRegistration cancellation
    )
    {
        yield return new UnityEngine.WaitForEndOfFrame();

        if (source.Task.IsCompleted)
        {
            cancellation.Dispose();
            yield break;
        }

        UnityEngine.Texture2D texture = null;
        try
        {
            texture = CaptureScreenshotAsTexture(superSize);
            if (texture is null)
            {
                if (superSize > 1)
                {
                    source.TrySetResult(
                        UnityScreenshotCaptureResult.Failed(
                            UnityScreenshotFailureKind.SuperSizeUnsupported
                        )
                    );
                    yield break;
                }

                texture = CaptureReadPixels();
            }
            var png = UnityPngEncoder.Encode(texture);
            source.TrySetResult(
                png is null
                    ? UnityScreenshotCaptureResult.Failed(
                        UnityScreenshotFailureKind.PngEncodingUnsupported
                    )
                    : UnityScreenshotCaptureResult.Success(
                        new CapturedScreenshot(texture.width, texture.height, png)
                    )
            );
        }
        catch (Exception ex)
        {
            source.TrySetException(ex);
        }
        finally
        {
            if (texture != null)
            {
                UnityEngine.Object.Destroy(texture);
            }

            cancellation.Dispose();
        }
    }

    private static UnityEngine.Texture2D CaptureScreenshotAsTexture(int superSize)
    {
        var method = CaptureScreenshotAsTextureMethod.Value;
        return method?.Invoke(null, new object[] { superSize }) as UnityEngine.Texture2D;
    }

    private static UnityEngine.Texture2D CaptureReadPixels()
    {
        var width = Math.Max(1, UnityEngine.Screen.width);
        var height = Math.Max(1, UnityEngine.Screen.height);
        var texture = new UnityEngine.Texture2D(
            width,
            height,
            UnityEngine.TextureFormat.RGB24,
            false
        );
        texture.ReadPixels(new UnityEngine.Rect(0, 0, width, height), 0, 0);
        texture.Apply();
        return texture;
    }

    private static MethodInfo ResolveCaptureScreenshotAsTexture()
    {
        var screenCapture =
            Type.GetType("UnityEngine.ScreenCapture, UnityEngine.ScreenCaptureModule")
            ?? Type.GetType("UnityEngine.ScreenCapture, UnityEngine");
        return screenCapture?.GetMethod(
            "CaptureScreenshotAsTexture",
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[] { typeof(int) },
            null
        );
    }
}
