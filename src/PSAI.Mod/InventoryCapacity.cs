using System;

namespace PSAI
{
    internal static class InventoryCapacity
    {
        internal static int CountUsableCells(int width, int height, Func<int, int, byte> cell)
        {
            var total = 0;
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    // Original inventory masks use zero for usable cells and one for walls.
                    if (cell(x, y) == 0) total++;
            return total;
        }
    }
}
