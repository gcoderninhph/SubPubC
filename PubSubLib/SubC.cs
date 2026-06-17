namespace PubSubLib;

internal static class SubC
{
    public static string[] GetAllGridCellsInRange(Vector2 position, float range, float gridSize)
    {
        var cells = new List<string>();
        int minX = (int)Math.Floor((position.x - range) / gridSize);
        int maxX = (int)Math.Floor((position.x + range) / gridSize);
        int minY = (int)Math.Floor((position.y - range) / gridSize);
        int maxY = (int)Math.Floor((position.y + range) / gridSize);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                cells.Add($"{x}:{y}");
            }
        }

        return cells.ToArray();
    }

    public static string GetGridCellByPosition(Vector2 position, float gridSize)
    {
        int cellX = (int)Math.Floor(position.x / gridSize);
        int cellY = (int)Math.Floor(position.y / gridSize);
        return $"{cellX}:{cellY}";
    }
}
