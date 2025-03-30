using System.Buffers;
using System.Runtime.InteropServices;

namespace PaddleOnWindowsML.Paddle
{
    partial class ImageClosedRegionDetector
    {
        public static System.Drawing.Rectangle[] GetClosedRegions(System.Drawing.Bitmap bitmap)
        {
            var builder = new ClosedRegionBuilder();
            using var bitmap2 = (System.Drawing.Bitmap)bitmap.Clone();
            bitmap2.ConvertFormat(System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var bitmapData = bitmap2.LockBits(
                new System.Drawing.Rectangle(0, 0, bitmap2.Width, bitmap2.Height),
                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var bitmap3 = new Bitmap(bitmapData.Scan0, bitmapData.Stride * bitmapData.Height, bitmapData.Width, bitmapData.Height);
                builder.Build(bitmap3);
            }
            finally
            {
                bitmap2.UnlockBits(bitmapData);
            }

            var rects = builder.GetBoundingBoxes();
            return rects.Select(rect => new System.Drawing.Rectangle(rect.Left, rect.Top, rect.Width, rect.Height))
                .ToArray();
        }

        private class ClosedRegionBuilder
        {
            private List<_Point[]> pointList;
            private List<_Rect> boundingBoxes;

            public ClosedRegionBuilder()
            {
                pointList = new List<_Point[]>();
                boundingBoxes = new List<_Rect>();
            }

            public void Build(Bitmap bmp)
            {
                // 查找轮廓
                pointList.Clear();
                boundingBoxes.Clear();

                List<_Point> contourList = new List<_Point>();

                int[][] map = ArrayPool<int[]>.Shared.Rent(bmp.Height);
                int[][] map2 = ArrayPool<int[]>.Shared.Rent(bmp.Height);

                try
                {
                    for (int i = 0; i < bmp.Height; i++)
                    {
                        map[i] = ArrayPool<int>.Shared.Rent(bmp.Width);
                        map2[i] = ArrayPool<int>.Shared.Rent(bmp.Width);
                        for (int j = 0; j < bmp.Width; j++)
                        {
                            map[i][j] = bmp.GetPixel(j, i).R > 0 ? 0 : 1;
                        }
                    }

                    using (var find = new FindContours())
                    {
                        find.MapLoader(map, bmp.Height, bmp.Width);
                        find.RasterScan();

                        find.CopyToMap(map2);
                    }

                    for (int y = 0; y < bmp.Height; y++)
                    {
                        for (int x = 0; x < bmp.Width; x++)
                        {
                            if (map2[y][x] == 0)
                            {
                                contourList.Clear();
                                FindContour(map2, x, y, bmp.Width, bmp.Height, contourList);
                                if (contourList.Count > 0)
                                {
                                    pointList.Add(contourList.ToArray());
                                }
                            }
                        }
                    }
                }
                finally
                {
                    for (int i = 0; i < bmp.Height; i++)
                    {
                        ArrayPool<int>.Shared.Return(map[i]);
                        ArrayPool<int>.Shared.Return(map2[i]);
                    }
                    ArrayPool<int[]>.Shared.Return(map);
                    ArrayPool<int[]>.Shared.Return(map2);
                }

                var allRects = new List<_Rect>();

                foreach (var contours in GetPoints())
                {
                    if (contours.Length > 0)
                    {
                        var left = int.MaxValue;
                        var top = int.MaxValue;
                        var right = int.MinValue;
                        var bottom = int.MinValue;

                        for (int i = 0; i < contours.Length; i++)
                        {
                            left = Math.Min(left, contours[i].X);
                            top = Math.Min(top, contours[i].Y);
                            right = Math.Max(right, contours[i].X);
                            bottom = Math.Max(bottom, contours[i].Y);
                        }

                        if (right - left > 1 && bottom - top > 1)
                        {
                            allRects.Add(new _Rect()
                            {
                                Left = left,
                                Top = top,
                                Right = right,
                                Bottom = bottom
                            });
                        }
                    }
                }

                var maxRectWidth = bmp.Width * 0.9;
                var maxRectHeight = bmp.Height * 0.9;

                for (int i = allRects.Count - 1; i >= 1; i--)
                {
                    var width1 = allRects[i].Right - allRects[i].Left;
                    var height1 = allRects[i].Bottom - allRects[i].Top;

                    if (width1 > maxRectWidth && height1 > maxRectHeight)
                    {
                        continue;
                    }

                    for (int j = i - 1; j >= 0; j--)
                    {
                        var width2 = allRects[j].Right - allRects[j].Left;
                        var height2 = allRects[j].Bottom - allRects[j].Top;

                        if (width2 > maxRectWidth && height2 > maxRectHeight)
                        {
                            continue;
                        }

                        bool flag = false;

                        if (Contains(allRects[i], allRects[j]))
                        {
                            allRects[j] = allRects[i];
                            flag = true;
                        }
                        else if (Contains(allRects[j], allRects[i]))
                        {
                            flag = true;
                        }

                        if (flag)
                        {
                            allRects.RemoveAt(i);
                            break;
                        }
                    }
                }

                boundingBoxes = allRects;

                bool Contains(_Rect rect1, _Rect rect2) =>
                    rect1.Left <= rect2.Left
                    && rect1.Top <= rect2.Top
                    && rect1.Right >= rect2.Right
                    && rect1.Bottom >= rect2.Bottom;
            }


            private void FindContour(int[][] map, int x, int y, int width, int height, List<_Point> contourList)
            {
                Stack<_Point> stack = new Stack<_Point>();
                stack.Push(new _Point(x, y));

                while (stack.Count > 0)
                {
                    _Point current = stack.Pop();
                    int cx = current.X;
                    int cy = current.Y;

                    if (map[cy][cx] == 0)
                    {
                        contourList.Add(current);
                        map[cy][cx] = -1;

                        if (cx > 0 && cy > 0) stack.Push(new _Point(cx - 1, cy - 1));

                        if (cy > 0) stack.Push(new _Point(cx, cy - 1));

                        if (cx < width - 1 && cy > 0) stack.Push(new _Point(cx + 1, cy - 1));


                        if (cx > 0) stack.Push(new _Point(cx - 1, cy));

                        if (cx < width - 1) stack.Push(new _Point(cx + 1, cy));


                        if (cx > 0 && cy < height - 1) stack.Push(new _Point(cx - 1, cy + 1));

                        if (cy < height - 1) stack.Push(new _Point(cx, cy + 1));

                        if (cx < width - 1 && cy < height - 1) stack.Push(new _Point(cx + 1, cy + 1));
                    }
                }
            }


            public List<_Point[]> GetPoints()
            {
                return pointList;
            }

            public List<_Rect> GetBoundingBoxes()
            {
                return boundingBoxes;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct _Color
        {
            public byte B;
            public byte G;
            public byte R;
            public byte A;

            public override string ToString()
            {
                return $"#{A:X2}{R:X2}{G:X2}{B:X2}";
            }
        }

        private struct _Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        private struct _Point
        {
            public int X;
            public int Y;

            public _Point(int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        private class Bitmap
        {
            private IntPtr data;
            private int dataLength;

            public Bitmap(IntPtr data, int dataLength, int width, int height)
            {
                this.data = data;
                this.dataLength = dataLength;
                Width = width;
                Height = height;
            }

            public int Width { get; }

            public int Height { get; }

            internal unsafe Span<byte> GetBytes() => new Span<byte>((void*)data, dataLength);

            internal unsafe Span<_Color> GetColors() => new Span<_Color>((void*)data, dataLength / 4);

            public _Color GetPixel(int x, int y) => GetColors()[y * Width + x];

            public void SetPixel(int x, int y, _Color color) => GetColors()[y * Width + x] = color;

        }
    }
}
