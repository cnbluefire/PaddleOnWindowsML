using Windows.Storage.Streams;

namespace PaddleOnWindowsML.Paddle
{
    internal static class ModelLoader
    {
        private static Stream? GetAssemblyResourceStream(string name)
        {
            var assemblyName = typeof(ModelLoader).Assembly.GetName().Name;
            var stream = typeof(ModelLoader).Assembly.GetManifestResourceStream($"{assemblyName}.Assets.Models.{name}");
            return stream;
        }

        private static IRandomAccessStreamReference? GetAssemblyResourceStreamReference(string name)
        {
            using var stream = GetAssemblyResourceStream(name);
            if (stream != null)
            {
                var ms = new InMemoryRandomAccessStream();
                var wrapper = ms.AsStreamForWrite();
                stream.CopyTo(wrapper);
                wrapper.Flush();
                ms.Seek(0);
                return RandomAccessStreamReference.CreateFromStream(ms);
            }
            return null;
        }

        public static IRandomAccessStreamReference? GetDetectModelStream() => GetAssemblyResourceStreamReference("ch_PP-OCRv4_det_infer.onnx");

        public static IRandomAccessStreamReference? GetRecognizeModelStream() => GetAssemblyResourceStreamReference("ch_PP-OCRv4_rec.onnx");

        public static Stream? GetLabelsDictStream() => GetAssemblyResourceStream("ppocr_keys_v1.txt");
    }
}
