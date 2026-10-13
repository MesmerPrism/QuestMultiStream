using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QuestMultiStream.App;

internal static class StereoCompositePreviewRenderer
{
    private const double TopCropRatio = 0.08;
    private const double BottomCropRatio = 0.98;
    private const double LeftEyeStartRatio = 0.16;
    private const double RightEyeStartRatio = 0.04;
    private const double EyeCropWidthRatio = 0.78;
    private const int BlackThreshold = 36;
    private const int MinimumCompositeWidth = 160;
    private const uint Srccopy = 0x00CC0020;

    public static BitmapSource? TryRender(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero || !GetClientRect(windowHandle, out var clientRect))
        {
            return null;
        }

        var width = Math.Max(1, clientRect.Right - clientRect.Left);
        var height = Math.Max(1, clientRect.Bottom - clientRect.Top);
        if (width < 400 || height < 240)
        {
            return null;
        }

        using var sourceBitmap = CaptureClientBitmap(windowHandle, width, height);
        return sourceBitmap is null
            ? null
            : CreateCompositeBitmapSource(sourceBitmap);
    }

    private static Bitmap? CaptureClientBitmap(IntPtr windowHandle, int width, int height)
    {
        using var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(bitmap);
        var destinationDc = graphics.GetHdc();
        var sourceDc = GetDC(windowHandle);

        try
        {
            if (sourceDc == IntPtr.Zero || !BitBlt(destinationDc, 0, 0, width, height, sourceDc, 0, 0, Srccopy))
            {
                return null;
            }
        }
        finally
        {
            if (sourceDc != IntPtr.Zero)
            {
                ReleaseDC(windowHandle, sourceDc);
            }

            graphics.ReleaseHdc(destinationDc);
        }

        return (Bitmap)bitmap.Clone();
    }

    private static BitmapSource? CreateCompositeBitmapSource(Bitmap sourceBitmap)
    {
        var sourceWidth = sourceBitmap.Width;
        var sourceHeight = sourceBitmap.Height;
        var halfWidth = sourceWidth / 2;
        if (halfWidth < 2 || sourceHeight < 2)
        {
            return null;
        }

        var cropTop = Math.Clamp((int)Math.Round(sourceHeight * TopCropRatio), 0, sourceHeight - 1);
        var cropBottom = Math.Clamp((int)Math.Round(sourceHeight * BottomCropRatio), cropTop + 1, sourceHeight);
        var cropHeight = cropBottom - cropTop;
        var cropWidth = Math.Clamp((int)Math.Round(halfWidth * EyeCropWidthRatio), 1, halfWidth);
        var leftStart = Math.Clamp((int)Math.Round(halfWidth * LeftEyeStartRatio), 0, Math.Max(0, halfWidth - cropWidth));
        var rightStart = halfWidth + Math.Clamp((int)Math.Round(halfWidth * RightEyeStartRatio), 0, Math.Max(0, halfWidth - cropWidth));

        var sourceRect = new Rectangle(0, 0, sourceWidth, sourceHeight);
        var sourceData = sourceBitmap.LockBits(sourceRect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);

        try
        {
            var sourceStride = Math.Abs(sourceData.Stride);
            var sourceBuffer = new byte[sourceStride * sourceHeight];
            Marshal.Copy(sourceData.Scan0, sourceBuffer, 0, sourceBuffer.Length);

            var horizontalShift = EstimateHorizontalShift(
                sourceBuffer,
                sourceStride,
                cropTop,
                cropHeight,
                leftStart,
                rightStart,
                cropWidth);
            var leftOffset = horizontalShift < 0 ? -horizontalShift : 0;
            var rightOffset = horizontalShift > 0 ? horizontalShift : 0;
            var outputWidth = cropWidth;

            var outputStride = outputWidth * 4;
            var outputBuffer = new byte[outputStride * cropHeight];

            for (var y = 0; y < cropHeight; y++)
            {
                var leftRowIndex = ((cropTop + y) * sourceStride) + (leftStart * 4);
                var rightRowIndex = ((cropTop + y) * sourceStride) + (rightStart * 4);
                var outputRowIndex = y * outputStride;

                for (var x = 0; x < outputWidth; x++)
                {
                    var outputIndex = outputRowIndex + (x * 4);
                    var leftX = x + leftOffset;
                    var rightX = x + rightOffset;
                    var leftValid = leftX >= 0 && leftX < cropWidth;
                    var rightValid = rightX >= 0 && rightX < cropWidth;

                    if (!leftValid && !rightValid)
                    {
                        outputBuffer[outputIndex + 3] = 255;
                        continue;
                    }

                    if (!leftValid)
                    {
                        var rightIndexOnly = rightRowIndex + (rightX * 4);
                        outputBuffer[outputIndex] = sourceBuffer[rightIndexOnly];
                        outputBuffer[outputIndex + 1] = sourceBuffer[rightIndexOnly + 1];
                        outputBuffer[outputIndex + 2] = sourceBuffer[rightIndexOnly + 2];
                        outputBuffer[outputIndex + 3] = 255;
                        continue;
                    }

                    if (!rightValid)
                    {
                        var leftIndexOnly = leftRowIndex + (leftX * 4);
                        outputBuffer[outputIndex] = sourceBuffer[leftIndexOnly];
                        outputBuffer[outputIndex + 1] = sourceBuffer[leftIndexOnly + 1];
                        outputBuffer[outputIndex + 2] = sourceBuffer[leftIndexOnly + 2];
                        outputBuffer[outputIndex + 3] = 255;
                        continue;
                    }

                    var leftIndex = leftRowIndex + (leftX * 4);
                    var rightIndex = rightRowIndex + (rightX * 4);

                    var leftBlue = sourceBuffer[leftIndex];
                    var leftGreen = sourceBuffer[leftIndex + 1];
                    var leftRed = sourceBuffer[leftIndex + 2];
                    var rightBlue = sourceBuffer[rightIndex];
                    var rightGreen = sourceBuffer[rightIndex + 1];
                    var rightRed = sourceBuffer[rightIndex + 2];

                    if (IsMostlyBlack(leftBlue, leftGreen, leftRed) && !IsMostlyBlack(rightBlue, rightGreen, rightRed))
                    {
                        outputBuffer[outputIndex] = rightBlue;
                        outputBuffer[outputIndex + 1] = rightGreen;
                        outputBuffer[outputIndex + 2] = rightRed;
                        outputBuffer[outputIndex + 3] = 255;
                        continue;
                    }

                    if (IsMostlyBlack(rightBlue, rightGreen, rightRed) && !IsMostlyBlack(leftBlue, leftGreen, leftRed))
                    {
                        outputBuffer[outputIndex] = leftBlue;
                        outputBuffer[outputIndex + 1] = leftGreen;
                        outputBuffer[outputIndex + 2] = leftRed;
                        outputBuffer[outputIndex + 3] = 255;
                        continue;
                    }

                    var blend = cropWidth == 1
                        ? 0.5
                        : x / (double)(cropWidth - 1);
                    blend = SmoothStep(blend);
                    var inverseBlend = 1.0 - blend;

                    outputBuffer[outputIndex] = (byte)Math.Clamp(Math.Round((leftBlue * inverseBlend) + (rightBlue * blend)), 0, 255);
                    outputBuffer[outputIndex + 1] = (byte)Math.Clamp(Math.Round((leftGreen * inverseBlend) + (rightGreen * blend)), 0, 255);
                    outputBuffer[outputIndex + 2] = (byte)Math.Clamp(Math.Round((leftRed * inverseBlend) + (rightRed * blend)), 0, 255);
                    outputBuffer[outputIndex + 3] = 255;
                }
            }

            var bitmapSource = BitmapSource.Create(
                outputWidth,
                cropHeight,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                outputBuffer,
                outputStride);
            bitmapSource.Freeze();
            return bitmapSource;
        }
        finally
        {
            sourceBitmap.UnlockBits(sourceData);
        }
    }

    private static bool IsMostlyBlack(byte blue, byte green, byte red)
        => blue + green + red < BlackThreshold;

    private static int EstimateHorizontalShift(
        byte[] sourceBuffer,
        int sourceStride,
        int cropTop,
        int cropHeight,
        int leftStart,
        int rightStart,
        int cropWidth)
    {
        var maxShift = Math.Min(160, Math.Max(24, cropWidth / 8));
        var sampleTop = Math.Clamp((int)Math.Round(cropHeight * 0.18), 0, cropHeight - 1);
        var sampleBottom = Math.Clamp((int)Math.Round(cropHeight * 0.82), sampleTop + 1, cropHeight);
        var rowStep = Math.Max(3, cropHeight / 48);
        var columnStep = Math.Max(3, cropWidth / 72);
        var bestShift = 0;
        var bestScore = double.MaxValue;

        for (var shift = -maxShift; shift <= maxShift; shift++)
        {
            var leftOffset = shift < 0 ? -shift : 0;
            var rightOffset = shift > 0 ? shift : 0;
            var overlapWidth = cropWidth - Math.Abs(shift);
            if (overlapWidth < MinimumCompositeWidth)
            {
                continue;
            }

            double totalDifference = 0;
            var sampleCount = 0;

            for (var y = sampleTop; y < sampleBottom; y += rowStep)
            {
                var leftRowIndex = ((cropTop + y) * sourceStride) + ((leftStart + leftOffset) * 4);
                var rightRowIndex = ((cropTop + y) * sourceStride) + ((rightStart + rightOffset) * 4);

                for (var x = 0; x < overlapWidth; x += columnStep)
                {
                    var leftIndex = leftRowIndex + (x * 4);
                    var rightIndex = rightRowIndex + (x * 4);

                    var leftBlue = sourceBuffer[leftIndex];
                    var leftGreen = sourceBuffer[leftIndex + 1];
                    var leftRed = sourceBuffer[leftIndex + 2];
                    var rightBlue = sourceBuffer[rightIndex];
                    var rightGreen = sourceBuffer[rightIndex + 1];
                    var rightRed = sourceBuffer[rightIndex + 2];

                    if (IsMostlyBlack(leftBlue, leftGreen, leftRed) || IsMostlyBlack(rightBlue, rightGreen, rightRed))
                    {
                        continue;
                    }

                    totalDifference += Math.Abs(GetLuminance(leftBlue, leftGreen, leftRed) - GetLuminance(rightBlue, rightGreen, rightRed));
                    sampleCount++;
                }
            }

            if (sampleCount < 24)
            {
                continue;
            }

            var averageDifference = totalDifference / sampleCount;
            if (averageDifference < bestScore)
            {
                bestScore = averageDifference;
                bestShift = shift;
            }
        }

        return bestShift;
    }

    private static double GetLuminance(byte blue, byte green, byte red)
        => (0.114d * blue) + (0.587d * green) + (0.299d * red);

    private static double SmoothStep(double value)
    {
        var clamped = Math.Clamp(value, 0d, 1d);
        return clamped * clamped * (3d - (2d * clamped));
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr windowHandle, IntPtr deviceContext);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr windowHandle, out NativeRect rect);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(
        IntPtr destinationDc,
        int x,
        int y,
        int width,
        int height,
        IntPtr sourceDc,
        int sourceX,
        int sourceY,
        uint rasterOperation);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
