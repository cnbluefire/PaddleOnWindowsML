using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.AI.MachineLearning;

namespace PaddleOnWindowsML.Paddle
{
    internal class PaddleDetector : IDisposable
    {
        private const int InputBitmapSize = 960;
        private static PixelFormat pixelFormat = PixelFormat.Format24bppRgb;

        private bool disposedValue;
        private PaddlePredictor predictor;

        public PaddleDetector()
        {
            var streamReference = ModelLoader.GetDetectModelStream();
            predictor = new PaddlePredictor(streamReference!);
        }

        public async Task<Rectangle[]?> RunAsync(Bitmap src, PaddleDevice device, CancellationToken cancellationToken)
        {
            if (src == null)
            {
                throw new ArgumentException("src size should not be 0, wrong input picture provided?");
            }

            if (src.PixelFormat != PixelFormat.Format24bppRgb && src.PixelFormat != PixelFormat.Indexed)
            {
                throw new NotSupportedException($"{nameof(src)} channel must be 3 or 1, provided {src.PixelFormat}.");
            }

            var padded = ResizeAndPadding(src, out var resizedSize);
            float[] normalized;
            try
            {
                normalized = Normalize(padded);
            }
            finally
            {
                padded.Dispose();
            }

            var input = new PaddleDetectInput()
            {
                images = TensorFloat.CreateFromArray(new OnnxShapes([1, 3, InputBitmapSize, InputBitmapSize]), normalized)
            };
            PaddleDetectOutput? output = null;

            try
            {
                using var session = predictor.CreateSession(device);
                output = await session.RunAsync(input, cancellationToken);
                using var outputBitmap = GetOutput(output, resizedSize);
                if (outputBitmap != null)
                {
                    var rects = ImageClosedRegionDetector.GetClosedRegions(outputBitmap);
                    var scaleRatio = 1.0 * src.Width / resizedSize.Width;


                    for (int i = 0; i < rects.Length; i++)
                    {
                        var paddingLeft = rects[i].Height;
                        var paddingTop = rects[i].Height;
                        var paddingRight = rects[i].Height;
                        var paddingBottom = rects[i].Height;

                        var left = (int)Math.Ceiling((rects[i].Left - paddingLeft) * scaleRatio);
                        var top = (int)Math.Ceiling((rects[i].Top - paddingTop) * scaleRatio);
                        var right = (int)Math.Ceiling((rects[i].Right + paddingRight) * scaleRatio);
                        var bottom = (int)Math.Ceiling((rects[i].Bottom + paddingBottom) * scaleRatio);

                        left = Math.Clamp(left, 0, src.Width);
                        top = Math.Clamp(top, 0, src.Height);
                        right = Math.Clamp(right, left, src.Width);
                        bottom = Math.Clamp(bottom, top, src.Height);

                        rects[i] = new Rectangle(left, top, right - left, bottom - top);
                    }

                    return rects;
                }

                return null;
            }
            finally
            {
                input.images.Dispose();
                output?.outputs?.Dispose();
            }
        }

        private unsafe Bitmap? GetOutput(PaddleDetectOutput output, Size resizedSize)
        {
            if (output?.outputs == null) return null;

            float[] data = output.outputs.GetAsVectorView().ToArray();
            long[] shape = output.outputs.Shape.ToArray();

            var height = (int)shape[2];
            var width = (int)shape[3];

            var bitmap = new Bitmap(resizedSize.Width, resizedSize.Height, pixelFormat);
            var bitmapData = bitmap.LockBits(new Rectangle(default, resizedSize), ImageLockMode.WriteOnly, pixelFormat);
            try
            {
                var sourceStride = width * 3;
                var span = MemoryMarshal.CreateSpan(ref Unsafe.AsRef<byte>((void*)bitmapData.Scan0), bitmapData.Stride * bitmapData.Height);
                for (int i = 0; i < span.Length / 3; i++)
                {
                    var v = data[i / bitmapData.Stride * sourceStride + i % bitmapData.Stride];
                    var v2 = (byte)Math.Clamp(v * 255, 0, 255);
                    span[i * 3] = v2;
                    span[i * 3 + 1] = v2;
                    span[i * 3 + 2] = v2;
                }
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }

            return bitmap;
        }



        private Bitmap ResizeAndPadding(Bitmap src, out Size resizedSize)
        {
            var longEdge = Math.Max(src.Width, src.Height);
            var scaleRate = 1.0 * InputBitmapSize / longEdge;

            var newWidth = (int)Math.Ceiling(src.Width * scaleRate);
            var newHeight = (int)Math.Ceiling(src.Height * scaleRate);
            resizedSize = new Size(newWidth, newHeight);
            var bitmap = new Bitmap(InputBitmapSize, InputBitmapSize, pixelFormat);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Black);
                g.DrawImage(src, 0, 0, newWidth, newHeight);
            }
            return bitmap;
        }

        private unsafe float[] Normalize(Bitmap bitmap)
        {
            float[] scales = new[] { 1 / 0.229f, 1 / 0.224f, 1 / 0.225f };
            float[] means = new[] { 0.485f, 0.456f, 0.406f };

            var size = bitmap.Width * bitmap.Height;
            float[] bgr = new float[bitmap.Width * bitmap.Height * 3];

            var bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, pixelFormat);
            try
            {
                var span = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef<byte>((void*)bitmapData.Scan0), bitmapData.Stride * bitmapData.Height);
                Span<float> bgrSpan = bgr;

                for (int i = 0; i < bgrSpan.Length; i++)
                {
                    // 0->r 1->g 2->b
                    var p = i / (bitmapData.Width * bitmapData.Height);

                    var lineIndex = i % (bitmapData.Width * bitmapData.Height) / bitmapData.Width;
                    var pixelIndex = i % (bitmapData.Width * bitmapData.Height) % bitmapData.Width;
                    var v = span[lineIndex * bitmapData.Stride + pixelIndex * 3 + p];
                    bgr[i] = (v * 1.0f / 255) * scales[i % 3] + (0.0f - means[i % 3]) * scales[i % 3];
                }
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }

            return bgr;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    predictor.Dispose();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

    }
}
