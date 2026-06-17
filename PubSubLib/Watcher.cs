namespace PubSubLib;

internal class Watcher
{
    public long Id { get; }
    public Vector2 Position { get; set; }
    public float Radius { get; set; }

    private HashSet<string> _cells = new();

    public string[] Cells => _cells.ToArray();

    public Watcher(long id, Vector2 position, float radius)
    {
        Id = id;
        Position = position;
        Radius = radius;
    }

    public void AddCells(IEnumerable<string> cellIds)
    {
        foreach (var id in cellIds)
            _cells.Add(id);
    }

    public void RemoveCells(IEnumerable<string> cellIds)
    {
        foreach (var id in cellIds)
            _cells.Remove(id);
    }
}
