namespace PubSubLib;

public readonly struct UnitKey : IEquatable<UnitKey>
{
    public readonly long Id;
    public readonly string Type;

    public UnitKey(long id, string type)
    {
        Id = id;
        Type = type;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Id, Type);
    }

    public override bool Equals(object? obj)
    {
        return obj is UnitKey other && Equals(other);
    }

    public bool Equals(UnitKey other)
    {
        return Id == other.Id && Type == other.Type;
    }

    public static bool operator ==(UnitKey left, UnitKey right) => left.Equals(right);
    public static bool operator !=(UnitKey left, UnitKey right) => !left.Equals(right);

    public override string ToString()
    {
        return $"{Type}:{Id}";
    }
}
