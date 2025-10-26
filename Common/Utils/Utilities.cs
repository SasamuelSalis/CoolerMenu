using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CoolerMenu.Common.Utils;

#nullable enable

public static class Utilities
{
    #region RenderTargets

    /// <summary>
    /// Reinitializes <paramref name="target"/> if needed.
    /// </summary>
    public static void ReintializeTarget(
        ref RenderTarget2D? target,
        GraphicsDevice device,
        int width,
        int height,
        bool mipMap = false,
        SurfaceFormat preferredFormat = SurfaceFormat.Color,
        DepthFormat preferredDepthFormat = DepthFormat.None,
        int preferredMultiSampleCount = 0,
        RenderTargetUsage usage = RenderTargetUsage.PreserveContents)
    {
        if (target is null ||
            target.IsDisposed ||
            target.Width != width ||
            target.Height != height)
        {
            target?.Dispose();
            target = new(device,
                width,
                height,
                mipMap,
                preferredFormat,
                preferredDepthFormat,
                preferredMultiSampleCount,
                usage);
        }
    }

    #endregion

    public static float Saturate(float value) =>
        MathHelper.Clamp(value, 0f, 1f);
}
