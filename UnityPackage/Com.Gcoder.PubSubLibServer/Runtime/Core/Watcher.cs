using System.Collections.Generic;
using System.Linq;

namespace PubSubLib
{
    internal class Watcher
    {
        public long Id { get; }
        public Vector2 Position { get; set; }
        public float Radius { get; set; }

        private HashSet<string> _cells = new();
        private HashSet<string> _knownTypes = new();

        public string[] Cells => _cells.ToArray();
        public string[] KnownTypes => _knownTypes.ToArray();

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

        public void RegisterKnownType(string type)
        {
            _knownTypes.Add(type);
        }
    }
}