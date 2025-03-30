using System.Collections;

namespace PaddleOnWindowsML.Paddle
{
    [WinRT.GeneratedWinRTExposedType]
    public partial class OnnxShapes : IEnumerable<long>
    {
        private readonly IEnumerable<long> shapes;

        public OnnxShapes(IEnumerable<long> shapes)
        {
            this.shapes = shapes;
        }

        public IEnumerator<long> GetEnumerator()
        {
            return shapes.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
