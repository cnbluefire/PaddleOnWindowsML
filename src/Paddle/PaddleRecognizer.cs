using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using Windows.AI.MachineLearning;

namespace PaddleOnWindowsML.Paddle
{
    internal class PaddleRecognizer : IDisposable
    {
        private bool disposedValue;

        private PaddlePredictor predictor;

        private static PixelFormat pixelFormat = PixelFormat.Format24bppRgb;

        private string[] dict;

        public PaddleRecognizer()
        {
            predictor = new PaddlePredictor(ModelLoader.GetRecognizeModelStream()!);

            using (var stream = ModelLoader.GetLabelsDictStream())
            using (var reader = new StreamReader(stream!))
            {
                dict = reader.ReadToEnd().Replace("\r", "").Split("\n");
            }
        }

        public async Task<PaddleRecognizerResult[]?> RunAsync(Bitmap src, System.Drawing.Rectangle[] rects, PaddleDevice device, CancellationToken cancellationToken)
        {
            if (src == null)
            {
                throw new ArgumentException("src size should not be 0, wrong input picture provided?");
            }

            if (src.PixelFormat != PixelFormat.Format24bppRgb && src.PixelFormat != PixelFormat.Indexed)
            {
                throw new NotSupportedException($"{nameof(src)} channel must be 3 or 1, provided {src.PixelFormat}.");
            }

            int modelWidth = (int)predictor.InputShapes[3];
            int modelHeight = (int)predictor.InputShapes[2];
            int maxWidth = (int)Math.Ceiling(rects.Max(rect => 1.0 * rect.Width / rect.Height * modelHeight));

            var inputSize = new Size(maxWidth, modelHeight);

            var sampleDataLength = inputSize.Width * inputSize.Height * 3;
            var data = new float[sampleDataLength * rects.Length];

            for (int i = 0; i < rects.Length; i++)
            {
                var pixels = ResizeAndPadding(src, rects[i], inputSize);
                var normalized = Normalize(pixels, inputSize);
                Array.Copy(normalized, 0, data, i * sampleDataLength, sampleDataLength);
            }

            var input = new PaddleDetectInput()
            {
                images = TensorFloat.CreateFromArray(new OnnxShapes([rects.Length, 3, inputSize.Height, inputSize.Width]), data)
            };
            PaddleDetectOutput? output = null;

            try
            {
                using var session = predictor.CreateSession(device);
                output = await session.RunAsync(input, cancellationToken);

                if (output?.outputs != null)
                {
                    int sampleCount = (int)output.outputs.Shape[0];
                    int labelCount = (int)output.outputs.Shape[2];
                    int charCount = (int)output.outputs.Shape[1];

                    var outputData = output.outputs.GetAsVectorView().ToArray();

                    var results = new PaddleRecognizerResult[sampleCount];

                    for (int i = 0; i < results.Length; i++)
                    {
                        StringBuilder sb = new();
                        int lastIndex = 0;
                        float score = 0;

                        for (int n = 0; n < charCount; ++n)
                        {
                            var span = outputData.AsSpan((n + i * charCount) * labelCount, labelCount);
                            int[] maxIdx = new int[2];
                            MinMaxIndex(span, out double _, out double maxVal, null, maxIdx);

                            if (maxIdx[1] > 0 && (!(n > 0 && maxIdx[1] == lastIndex)) && maxVal > 0.001)
                            {
                                score += (float)maxVal;
                                sb.Append(GetLabelByIndex(maxIdx[1], dict));
                            }
                            lastIndex = maxIdx[1];
                        }

                        results[i] = new PaddleRecognizerResult(sb.ToString(), score / sb.Length, rects[i]);
                    }

                    return results;
                }

                return null;
            }
            finally
            {
                input.images.Dispose();
                output?.outputs?.Dispose();
            }
        }

        private static void MinMaxIndex(Span<float> span, out double minValue, out double maxValue, int[]? minIdx, int[]? maxIdx)
        {
            minValue = double.MaxValue;
            maxValue = double.MinValue;

            for (int i = 0; i < span.Length; i++)
            {
                if (span[i] > maxValue)
                {
                    maxValue = span[i];
                    if (maxIdx != null && maxIdx.Length > 1) maxIdx[1] = i;
                }
                if (span[i] < minValue)
                {
                    minValue = span[i];
                    if (minIdx != null && minIdx.Length > 1) minIdx[1] = i;
                }
            }
        }

        private unsafe byte[] ResizeAndPadding(Bitmap src, Rectangle rect, Size inputSize)
        {
            var newWidth = inputSize.Width;
            var newHeight = (int)Math.Ceiling(1.0 * inputSize.Width / rect.Width * rect.Height);

            if (newHeight > inputSize.Height)
            {
                newHeight = inputSize.Height;
                newWidth = (int)Math.Ceiling(1.0 * inputSize.Height / rect.Height * rect.Width);
            }

            var resizedSize = new Size(newWidth, newHeight);
            using var bitmap = new Bitmap(inputSize.Width, inputSize.Height, pixelFormat);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Gray);
                g.DrawImage(src, new Rectangle(0, 0, newWidth, newHeight), rect, GraphicsUnit.Pixel);
            }

            //bitmap.Save(Path.Combine("C:\\Users\\blue-fire\\Downloads\\test-imgs", $"{Guid.NewGuid().ToString("N")[0..8]}.png"));

            var bitmapData = bitmap.LockBits(new Rectangle(default, inputSize), ImageLockMode.ReadOnly, bitmap.PixelFormat);
            try
            {
                var span = new Span<byte>((void*)bitmapData.Scan0, bitmapData.Stride * bitmapData.Height);
                var result = new byte[bitmapData.Width * bitmapData.Height * 3];
                var linePixels = bitmapData.Width * 3;

                for (int i = 0; i < bitmapData.Height; i++)
                {
                    span.Slice(i * bitmapData.Stride, linePixels).CopyTo(result.AsSpan(i * linePixels, linePixels));
                }
                return result;
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }
        }

        private unsafe float[] Normalize(byte[] bytes, Size inputSize)
        {
            float[] scales = new[] { 1 / 0.229f, 1 / 0.224f, 1 / 0.225f };
            float[] means = new[] { 0.485f, 0.456f, 0.406f };

            var size = inputSize.Width * inputSize.Height;
            float[] bgr = new float[inputSize.Width * inputSize.Height * 3];

            Span<byte> span = bytes;
            Span<float> bgrSpan = bgr;

            for (int i = 0; i < bgrSpan.Length; i++)
            {
                // 0->r 1->g 2->b
                var p = i / (inputSize.Width * inputSize.Height);

                var lineIndex = i % (inputSize.Width * inputSize.Height) / inputSize.Width;
                var pixelIndex = i % (inputSize.Width * inputSize.Height) % inputSize.Width;
                var v = span[lineIndex * inputSize.Width * 3 + pixelIndex * 3 + p];
                bgr[i] = (v * 1.0f / 255) * scales[i % 3] + (0.0f - means[i % 3]) * scales[i % 3];
            }

            return bgr;
        }

        public static string GetLabelByIndex(int i, IReadOnlyList<string> labels)
        {
            if (i > 0 && i <= labels.Count) return labels[i - 1];
            else if (i == labels.Count + 1) return " ";
            Throw(i, labels.Count);

            return string.Empty;

            void Throw(int idx, int count)
            {
                throw new Exception($"Unable to GetLabelByIndex: index {idx} out of range {count}, OCR model or labels not matched?");
            }
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


        public record class PaddleRecognizerResult(string Text, double Score, Rectangle BoundingBox);
    }
}
