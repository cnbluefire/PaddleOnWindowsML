using PaddleOnWindowsML.Paddle;
using System.CommandLine;
using System.Diagnostics;
using System.Drawing;
using WinRT;

namespace PaddleOnWindowsML
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var imageSourceOption = new Option<ImageSource?>("--source", () => null)
            {
                IsRequired = false
            };

            var inputFileOption = new Option<FileInfo?>("--input", () => null)
            {
                IsRequired = false
            };

            var deviceOption = new Option<PaddleDevice>("--device", () => PaddleDevice.Cpu)
            {
                IsRequired = true
            };

            var command = new Command("PaddleOnWindowsML", "Deploy paddle ocr on Windows.ML")
            {
                imageSourceOption,
                inputFileOption,
                deviceOption
            };

            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, a) => cts.Cancel();

            command.SetHandler(async (imageSource, inputFile, device) =>
            {
                Bitmap? inputBitmap = null;
                if (imageSource is ImageSource.Screen || (imageSource is not ImageSource.InputFile && inputFile == null))
                {
                    inputBitmap = ScreenHelper.CaptureScreen();
                }
                else if (inputFile == null || !inputFile.Exists)
                {
                    throw new FileNotFoundException();
                }
                else
                {
                    using var bitmap = Image.FromFile(inputFile.FullName);
                    inputBitmap = new Bitmap(bitmap.Width, bitmap.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                    using (var g = Graphics.FromImage(inputBitmap))
                    {
                        g.DrawImageUnscaled(bitmap, 0, 0);
                    }
                }

                await RunAsync(inputBitmap, device, cts.Token);
            }, imageSourceOption, inputFileOption, deviceOption);

            await command.InvokeAsync(args);
        }

        public enum ImageSource
        {
            Screen,
            InputFile
        }

        private static async Task RunAsync(Bitmap inputBitmap, PaddleDevice device, CancellationToken cancellationToken)
        {
            using var detector = new PaddleDetector();
            var rects = await detector.RunAsync(inputBitmap, device, default);

            if (rects != null)
            {
                var rects2 = rects
                    .Where(c => c.Width > 4 && c.Height > 4)
                    .Chunk(8)
                    .ToArray();
                var recognizer = new PaddleRecognizer();

                var tasks = new List<Task<PaddleRecognizer.PaddleRecognizerResult[]?>>();
                var list = new List<PaddleRecognizer.PaddleRecognizerResult>();

                for (int i = 0; i < rects2.Length; i++)
                {
                    tasks.Add(recognizer.RunAsync(inputBitmap, rects2[i], device, cancellationToken));
                }

                await Task.WhenAll(tasks);

                for (int i = 0; i < tasks.Count; i++)
                {
                    var result = tasks[i].Result;
                    if (result != null)
                    {
                        list.AddRange(result);
                        for (int j = 0; j < result.Length; j++)
                        {
                            Console.WriteLine(result[j].Text);
                        }
                    }
                }
            }
        }
    }
}
