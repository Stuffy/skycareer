using System.Reflection;
using System.Windows.Threading;
using SkyCareer.Application.Logging;

namespace SkyCareer.App;

/// <summary>
/// Works around a WebView2 bug that leaves the UI black. BlazorWebView renders through <c>WebView2CompositionControl</c>,
/// whose <c>GraphicsItemD3DImage</c> rebuilds its Direct3D devices after a display change (MSFS switching to or from full
/// screen, resolution or HDR). Its retry loop only catches <c>COMException</c>, but creating the D3D9 device can fail with
/// E_INVALIDARG, which arrives as <see cref="ArgumentException"/> (seen 2026-11-04, WebView2 SDK 1.0.4258.31): the
/// exception reaches the dispatcher, no retry runs, and the control stays "display change pending", so it never paints
/// again. This retries the same private recovery until the display has settled.
/// </summary>
internal static class WebViewDisplayRecovery
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(1);
    private const int MaxRetries = 120;

    private static object? _control;
    private static DispatcherTimer? _timer;

    /// <summary>The WebView2 control the BlazorWebView created (from <c>BlazorWebViewInitialized</c>).</summary>
    public static void Attach(object webView)
    {
        _control = webView;
        // A newer WebView2 SDK may rename the internals (or fix the bug); the log then says the workaround is inactive.
        if (Resolve() is null) AppLog.Warn("App", $"WebView2 display recovery unavailable for {webView.GetType().FullName}");
    }

    /// <summary>Whether the exception is the failed recovery after a display change.</summary>
    public static bool IsDisplayChangeFailure(Exception ex) =>
        ex.StackTrace?.Contains("GraphicsItemD3DImage.RecoverAfterDisplayChange", StringComparison.Ordinal) == true;

    /// <summary>Retries the recovery on the UI thread until it succeeds (at most two minutes).</summary>
    public static void Recover(Dispatcher dispatcher, Exception cause)
    {
        if (_timer is not null) return;
        if (Resolve() is not { } recover)
        {
            AppLog.Warn("App", "WebView2 lost its display after a display change and could not be recovered (internals not found)", cause);
            return;
        }
        AppLog.Warn("App", $"WebView2 could not recover after a display change ({cause.Message}); retrying");
        int attempt = 0;
        _timer = new DispatcherTimer(RetryInterval, DispatcherPriority.Background, (_, _) =>
        {
            attempt++;
            try
            {
                recover();
                Stop();
                AppLog.Info("App", $"WebView2 display recovered after {attempt} attempt(s)");
            }
            catch (TargetInvocationException ex) when (attempt < MaxRetries)
            {
                if (attempt % 10 == 0) AppLog.Info("App", $"WebView2 display recovery attempt {attempt} failed: {ex.InnerException?.Message}");
            }
            catch (Exception ex)
            {
                Stop();
                AppLog.Warn("App", $"WebView2 display recovery gave up after {attempt} attempt(s)", ex.InnerException ?? ex);
            }
        }, dispatcher);
        _timer.Start();
    }

    private static void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    private static Action? Resolve()
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        var image = _control?.GetType().GetField("_d3dImage", Private)?.GetValue(_control);
        var method = image?.GetType().GetMethod("RecoverAfterDisplayChange", Private, Type.EmptyTypes);
        return image is null || method is null ? null : () => method.Invoke(image, null);
    }
}
