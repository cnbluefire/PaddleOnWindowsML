using System.Buffers;

namespace PaddleOnWindowsML.Paddle
{
    partial class ImageClosedRegionDetector
    {
        // 从 Alexbeast-CN/findContours 项目翻译成 C# 代码
        // 项目地址: https://github.com/Alexbeast-CN/findContours
        private class FindContours : IDisposable
        {
            private bool disposedValue;

            private Memory<int[]> grid;
            private IMemoryOwner<int[]>? gridMemory;

            private int padSize = 1;
            private int rows;
            private int cols;
            private int LNBD = 1;
            private int NBD = 1;
            private (int x, int y) pwh;
            private List<string> boardType = new List<string> { " ", "in" };

            public FindContours()
            {
                Reset();
            }

            public void CopyToMap(int[][] map)
            {
                for (int i = 0; i < rows; i++)
                {
                    for (int j = 0; j < cols; j++)
                    {
                        map[i][j] = grid.Span[i][j];
                    }
                }
            }

            public void MapLoader(int[][] grid, int rows, int cols)
            {
                this.grid = grid;
                this.rows = rows;
                this.cols = cols;
                Reset();
            }

            private void Reset()
            {
                Pad(padSize);
                LNBD = 1;
                NBD = 1;
            }

            public void Pad(int padSize)
            {
                var grid = this.grid.Span;
                var newRows = rows + 2 * padSize;
                var newCols = cols + 2 * padSize;

                var owner = MemoryPool<int[]>.Shared.Rent(newRows);

                var gridPad = owner.Memory.Span;
                for (int i = 0; i < newRows; i++)
                {
                    gridPad[i] = ArrayPool<int>.Shared.Rent(newCols);
                    for (int j = 0; j < newCols; j++)
                    {
                        gridPad[i][j] = 0;
                    }
                }

                for (int i = 0; i < rows; i++)
                {
                    for (int j = 0; j < cols; j++)
                    {
                        gridPad[i + padSize][j + padSize] = grid[i][j];
                    }
                    if (this.gridMemory != null)
                    {
                        ArrayPool<int>.Shared.Return(grid[i]);
                    }
                }

                this.gridMemory?.Dispose();
                this.gridMemory = owner;
                this.grid = owner.Memory;
                rows = newRows;
                cols = newCols;
            }

            public void RmPad(int padSize)
            {
                var grid = this.grid.Span;
                var newRows = rows - 2 * padSize;
                var newCols = cols - 2 * padSize;

                var owner = MemoryPool<int[]>.Shared.Rent(newRows);

                var gridRm = owner.Memory.Span;
                for (int i = 0; i < newRows; i++)
                {
                    gridRm[i] = ArrayPool<int>.Shared.Rent(newCols);
                    for (int j = 0; j < newCols; j++)
                    {
                        gridRm[i][j] = 0;
                    }
                }

                for (int i = 0; i < newRows; i++)
                {
                    for (int j = 0; j < cols - 2 * padSize; j++)
                    {
                        gridRm[i][j] = grid[i + padSize][j + padSize];
                    }
                    if (this.gridMemory != null)
                    {
                        ArrayPool<int>.Shared.Return(grid[i]);
                    }
                }

                this.gridMemory?.Dispose();
                this.gridMemory = owner;
                this.grid = owner.Memory;
                rows = newRows;
                cols = newCols;
            }

            public (int x, int y) FindNeighbor((int x, int y) center, (int x, int y) start, bool clockwise = true)
            {
                var grid = this.grid.Span;

                int weight = 1;
                if (!clockwise) weight = -1;

                (int x, int y)[] neighbors = new[]
                {
                    (0, 0),
                    (0, 1),
                    (0, 2),
                    (1, 2),
                    (2, 2),
                    (2, 1),
                    (2, 0),
                    (1, 0)
                };

                int[,] index = new int[,]
                {
                    {0, 1, 2},
                    {7, 0, 3},
                    {6, 5, 4}
                };

                int startInd = index[start.Item1 - center.Item1 + 1, start.Item2 - center.Item2 + 1];

                for (int i = 1; i < neighbors.Length + 1; i++)
                {
                    int curInd = (startInd + i * weight + 8) % 8;
                    int x = center.Item1 + neighbors[curInd].Item1 - 1;
                    int y = center.Item2 + neighbors[curInd].Item2 - 1;
                    if (grid[x][y] != 0)
                    {
                        return (x, y);
                    }
                }
                return (-1, -1);
            }

            public void BoardFollow((int x, int y) center, (int x, int y) start, bool clockwise = true)
            {
                var grid = this.grid.Span;

                bool openPolygon = true;
                List<(int x, int y)> board = new List<(int x, int y)>();

                grid[center.Item1][center.Item2] = NBD;

                (int x, int y) newCenter = center;
                (int x, int y) neighbor = start;
                (int x, int y) newNeighbor = FindNeighbor(newCenter, neighbor, clockwise);

                while (!newNeighbor.Equals((-1, -1)))
                {
                    int x = newCenter.Item1;
                    int y = newCenter.Item2;
                    board.Add(newCenter);

                    if (newNeighbor.Equals(center))
                    {
                        foreach (var p in board)
                        {
                            grid[p.Item1][p.Item2] = NBD;
                        }
                        return;
                    }

                    neighbor = newCenter;
                    newCenter = newNeighbor;
                    newNeighbor = FindNeighbor(newCenter, neighbor, clockwise);
                }

                if (openPolygon)
                {
                    foreach (var p in board)
                    {
                        grid[p.Item1][p.Item2] = NBD;
                    }
                    return;
                }
            }

            public void RasterScan()
            {
                var grid = this.grid.Span;

                for (int i = 0; i < rows; i++)
                {
                    LNBD = 1;
                    for (int j = 0; j < cols; j++)
                    {
                        // Find the starting point of the border.
                        if (grid[i][j] == 1 && grid[i][j - 1] == 0)
                        {
                            LNBD = NBD;
                            NBD += 1;
                            Console.WriteLine("out " + NBD);
                            BoardFollow((i, j), (i, j - 1), false);
                            boardType.Add("out");
                        }
                        // Find the starting point of the hole.
                        else if (grid[i][j] == 1 && grid[i][j + 1] == 0)
                        {
                            LNBD = NBD;
                            NBD += 1;
                            Console.WriteLine("in " + NBD);
                            BoardFollow((i, j), (i, j + 1), true);
                            boardType.Add("in");
                        }
                    }
                }
                RmPad(padSize);
            }

            public void Dispose()
            {
                if (!disposedValue)
                {
                    gridMemory?.Dispose();
                    gridMemory = null;
                    grid = default;

                    disposedValue = true;
                }
            }
        }
    }
}

