namespace Gcoder.Collections;

internal interface ISortedDictionary<TKey, TValue>
    where TKey : notnull, IComparable<TKey>
    where TValue : IComparable<TValue>
{
    int Count { get; }
    IEnumerable<TKey> Keys { get; }
    IEnumerable<TKey> OrderedKeys { get; }
    void Clear();
    bool Add(TKey key, TValue score);
    bool Remove(TKey key);
    TValue GetScore(TKey key);
    bool TryGetScore(TKey key, out TValue score);
    bool ContainsKey(TKey key);
    int GetRank(TKey key);
    int GetReverseRank(TKey key);
    (TKey Key, TValue Score) Min();
    (TKey Key, TValue Score) Max();
    bool TryGetMin(out TKey key, out TValue score);
    bool TryGetMax(out TKey key, out TValue score);
    IEnumerable<(TKey Key, TValue Score)> RangeByRank(int startRank, int stopRank);
    IEnumerable<(TKey Key, TValue Score)> RangeByScore(
        TValue minScore, TValue maxScore, bool minExclusive = false, bool maxExclusive = false);
    int CountByScore(TValue minScore, TValue maxScore);
    int RemoveRangeByScore(TValue minScore, TValue maxScore);
    int RemoveRangeByRank(int startRank, int stopRank);
    IEnumerable<(TKey Key, TValue Score)> GetAll();
}
