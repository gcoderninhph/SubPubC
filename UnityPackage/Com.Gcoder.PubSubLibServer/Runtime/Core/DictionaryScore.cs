using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

namespace Gcoder.Collections
{

/// <summary>
/// DictionaryScore&lt;TKey, TValue&gt; — cấu trúc dữ liệu giống Redis ZSET.
///
/// Kết hợp:
///   • Skip List  — lưu (score, key) theo thứ tự tăng dần, O(log n) insert/remove/range
///   • Hash Map   — tra cứu score theo key trong O(1)
///
/// Độ phức tạp:
///   Add / Remove              — O(log n) trung bình
///   GetScore                  — O(1)
///   GetRank                   — O(log n)
///   RangeByScore / Rank       — O(log n + k), k = số phần tử trả về
///   CountByScore              — O(log n)
///   RemoveRangeByScore / Rank — O(log n + k)
///   Count                     — O(1)
///   Clear                     — O(1) đối với SkipList, O(N) theo Hash Map bên dưới
///
/// ⚠️ Class này KHÔNG thread-safe. Nếu cần truy cập đồng thời từ nhiều thread,
/// hãy sử dụng lock bên ngoài.
/// </summary>
internal class DictionaryScore<TKey, TValue> : ISortedDictionary<TKey, TValue>
    where TKey : notnull, IComparable<TKey>
    where TValue : IComparable<TValue>
{
    // ─── Skip List internals ────────────────────────────────────────────────

    private const int MaxLevel = 32;

    private const double Probability = 0.25; // Redis dùng p = 0.25

    // ─── Shared buffers (Tránh GC Allocation trong Hot Path) ────────────────
    private readonly SkipNode[] _sharedUpdate = new SkipNode[MaxLevel];
    private readonly int[] _sharedRank = new int[MaxLevel];

    private readonly Random _rng = new Random();

    private sealed class SkipNode
    {
        public TKey Key;
        public TValue Score;
        public readonly SkipNode[] Forward; // con trỏ tiến theo từng tầng
        public readonly int[] Span; // khoảng cách tới node tiếp theo (dùng cho GetRank)

        public SkipNode(TKey key, TValue score, int level)
        {
            Key = key;
            Score = score;
            Forward = new SkipNode[level];
            Span = new int[level];
        }
    }

    private readonly SkipNode _head; // sentinel HEAD
    private int _level = 1; // tầng cao nhất đang dùng
    private int _count = 0;

    // ─── Hash Map: key → score (O(1) lookup) ────────────────────────────────
    private readonly Dictionary<TKey, TValue> _dict;
    private readonly IComparer<TValue> _scoreComparer;

    // ────────────────────────────────────────────────────────────────────────

    public DictionaryScore(IComparer<TValue>? scoreComparer = null)
    {
        _scoreComparer = scoreComparer ?? Comparer<TValue>.Default;
        _dict = new Dictionary<TKey, TValue>();
        _head = new SkipNode(default!, default!, MaxLevel);
        // span = 0, forward = null theo mặc định
    }

    /// <summary>Số phần tử hiện có.</summary>
    public int Count => _count;

    /// <summary>
    /// Lấy danh sách các Key có trong tập hợp (Không đảm bảo thứ tự điểm số).
    /// Tốc độ truy xuất cực nhanh O(1).
    /// </summary>
    public IEnumerable<TKey> Keys => _dict.Keys;

    /// <summary>
    /// Lấy danh sách các Key được sắp xếp theo thứ tự điểm số (Score) tăng dần.
    /// Tốc độ truy xuất O(N).
    /// </summary>
    public IEnumerable<TKey> OrderedKeys
    {
        get
        {
            var cur = _head.Forward[0];
            while (cur != null)
            {
                yield return cur.Key;
                cur = cur.Forward[0];
            }
        }
    }

    // ─── Clear ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Xoá toàn bộ dữ liệu trong cấu trúc.
    /// </summary>
    public void Clear()
    {
        _dict.Clear();
        _count = 0;
        _level = 1;

        // Reset lại Sentinel HEAD của Skip List
        for (int i = 0; i < MaxLevel; i++)
        {
            _head.Forward[i] = null!;
            _head.Span[i] = 0;
        }
    }

    // ─── Random level (giống Redis: mỗi tầng có xác suất p = 0.25) ─────────
    private int RandomLevel()
    {
        int lv = 1;
        while (lv < MaxLevel && _rng.NextDouble() < Probability)
            lv++;
        return lv;
    }

    // ─── So sánh (score, key) — đảm bảo unique position trong skip list ───
    private int Compare(TValue scoreA, TKey keyA, TValue scoreB, TKey keyB)
    {
        int cmp = _scoreComparer.Compare(scoreA, scoreB);
        if (cmp != 0) return cmp;
        // Khi score bằng nhau, tie-break bằng key (Redis dùng lexicographic)
        return Comparer<TKey>.Default.Compare(keyA, keyB);
    }

    // ─── Add / Update ───────────────────────────────────────────────────────

    /// <summary>
    /// Thêm hoặc cập nhật phần tử.
    /// Nếu key đã tồn tại và score mới khác, xoá node cũ rồi chèn lại.
    /// </summary>
    /// <returns>true nếu là key mới, false nếu là update.</returns>
    public bool Add(TKey key, TValue score)
    {
        if (_dict.TryGetValue(key, out var oldScore))
        {
            if (_scoreComparer.Compare(oldScore, score) == 0)
                return false; // score không đổi — không làm gì
            RemoveFromSkipList(key, oldScore);
            _dict[key] = score;
            InsertIntoSkipList(key, score);
            return false; // update — không phải key mới
        }

        _dict[key] = score;
        InsertIntoSkipList(key, score);
        return true; // key mới
    }

    private void InsertIntoSkipList(TKey key, TValue score)
    {
        var update = _sharedUpdate; // Dùng lại mảng
        var rank = _sharedRank; // Dùng lại mảng

        var cur = _head;
        for (int i = _level - 1; i >= 0; i--)
        {
            rank[i] = (i == _level - 1) ? 0 : rank[i + 1];
            while (cur.Forward[i] != null &&
                   Compare(cur.Forward[i].Score, cur.Forward[i].Key, score, key) < 0)
            {
                rank[i] += cur.Span[i];
                cur = cur.Forward[i];
            }

            update[i] = cur;
        }

        int newLevel = RandomLevel();
        if (newLevel > _level)
        {
            for (int i = _level; i < newLevel; i++)
            {
                rank[i] = 0;
                update[i] = _head;
                update[i].Span[i] = _count;
            }

            _level = newLevel;
        }

        var node = new SkipNode(key, score, newLevel);
        for (int i = 0; i < newLevel; i++)
        {
            node.Forward[i] = update[i].Forward[i];
            update[i].Forward[i] = node;

            // cập nhật span
            node.Span[i] = update[i].Span[i] - (rank[0] - rank[i]);
            update[i].Span[i] = (rank[0] - rank[i]) + 1;
        }

        // tầng cao hơn newLevel: chỉ tăng span
        for (int i = newLevel; i < _level; i++)
            update[i].Span[i]++;

        _count++;

        // Xoá tham chiếu để tránh Memory Leak (Zombie references)
        Array.Clear(update, 0, MaxLevel);
    }

    // ─── Remove ─────────────────────────────────────────────────────────────

    /// <summary>Xoá phần tử theo key. Trả về false nếu key không tồn tại.</summary>
    public bool Remove(TKey key)
    {
        if (!_dict.TryGetValue(key, out var score))
            return false;

        _dict.Remove(key);
        RemoveFromSkipList(key, score);
        return true;
    }

    private void RemoveFromSkipList(TKey key, TValue score)
    {
        var update = _sharedUpdate;
        var cur = _head;

        for (int i = _level - 1; i >= 0; i--)
        {
            while (cur.Forward[i] != null &&
                   Compare(cur.Forward[i].Score, cur.Forward[i].Key, score, key) < 0)
                cur = cur.Forward[i];
            update[i] = cur;
        }

        var target = cur.Forward[0];
        if (target == null ||
            _scoreComparer.Compare(target.Score, score) != 0 ||
            !EqualityComparer<TKey>.Default.Equals(target.Key, key))
            return; // không tìm thấy

        for (int i = 0; i < _level; i++)
        {
            if (update[i].Forward[i] != target)
            {
                update[i].Span[i]--;
                continue;
            }

            update[i].Span[i] += target.Span[i] - 1;
            update[i].Forward[i] = target.Forward[i];
        }

        while (_level > 1 && _head.Forward[_level - 1] == null)
            _level--;

        _count--;
        Array.Clear(update, 0, MaxLevel);
    }

    // ─── Lookup ─────────────────────────────────────────────────────────────

    /// <summary>Lấy score của key. Throws KeyNotFoundException nếu không có.</summary>
    public TValue GetScore(TKey key) => _dict[key];

    /// <summary>Thử lấy score. Trả về false nếu key không tồn tại.</summary>
    public bool TryGetScore(TKey key, out TValue score) => _dict.TryGetValue(key, out score!);

    /// <summary>Kiểm tra key có tồn tại không.</summary>
    public bool ContainsKey(TKey key) => _dict.ContainsKey(key);

    // ─── Rank (0-based, thứ hạng từ score thấp đến cao) ────────────────────

    /// <summary>
    /// Trả về rank 0-based của key (0 = score nhỏ nhất).
    /// Trả về -1 nếu key không tồn tại.
    /// </summary>
    public int GetRank(TKey key)
    {
        if (!_dict.TryGetValue(key, out var score)) return -1;

        int rank = 0;
        var cur = _head;
        for (int i = _level - 1; i >= 0; i--)
        {
            while (cur.Forward[i] != null &&
                   Compare(cur.Forward[i].Score, cur.Forward[i].Key, score, key) <= 0)
            {
                rank += cur.Span[i];
                cur = cur.Forward[i];
            }
        }

        return rank - 1; // span tính từ 1
    }

    /// <summary>Rank từ cuối (0 = score lớn nhất).</summary>
    public int GetReverseRank(TKey key)
    {
        int r = GetRank(key);
        return r < 0 ? -1 : _count - 1 - r;
    }

    // ─── Min / Max ──────────────────────────────────────────────────────────

    /// <summary>
    /// Trả về entry có score thấp nhất (tương đương ZRANGE key 0 0).
    /// Throws InvalidOperationException nếu tập rỗng.
    /// </summary>
    public (TKey Key, TValue Score) Min()
    {
        if (_count == 0) throw new InvalidOperationException("Set is empty.");
        var node = _head.Forward[0]; // node đầu tiên ở tầng 0 — luôn là nhỏ nhất
        return (node.Key, node.Score);
    }

    /// <summary>
    /// Trả về entry có score cao nhất (tương đương ZRANGE key -1 -1).
    /// Throws InvalidOperationException nếu tập rỗng.
    /// </summary>
    public (TKey Key, TValue Score) Max()
    {
        if (_count == 0) throw new InvalidOperationException("Set is empty.");

        // Đi từ tầng cao nhất, nhảy xa nhất có thể đến cuối
        var cur = _head;
        for (int i = _level - 1; i >= 0; i--)
            while (cur.Forward[i] != null)
                cur = cur.Forward[i]; // nhảy hết về phía phải ở tầng i

        return (cur.Key, cur.Score);
    }

    /// <summary>Thử lấy Min — trả về false nếu tập rỗng (không throw).</summary>
    public bool TryGetMin(out TKey key, out TValue score)
    {
        if (_count == 0)
        {
            key = default!;
            score = default!;
            return false;
        }

        (key, score) = Min();
        return true;
    }

    /// <summary>Thử lấy Max — trả về false nếu tập rỗng (không throw).</summary>
    public bool TryGetMax(out TKey key, out TValue score)
    {
        if (_count == 0)
        {
            key = default!;
            score = default!;
            return false;
        }

        (key, score) = Max();
        return true;
    }

    // ─── Range by Rank ──────────────────────────────────────────────────────

    /// <summary>
    /// Lấy các phần tử theo khoảng rank [startRank, stopRank] (inclusive, 0-based).
    /// Tương đương ZRANGE key start stop.
    /// </summary>
    public IEnumerable<(TKey Key, TValue Score)> RangeByRank(int startRank, int stopRank)
    {
        if (startRank < 0) startRank = Math.Max(0, _count + startRank);
        if (stopRank < 0) stopRank = Math.Max(0, _count + stopRank);
        stopRank = Math.Min(stopRank, _count - 1);
        if (startRank > stopRank) yield break;

        // Nhảy tới node ở vị trí startRank
        int traversed = 0;
        var cur = _head;
        for (int i = _level - 1; i >= 0; i--)
            while (cur.Forward[i] != null && traversed + cur.Span[i] <= startRank)
            {
                traversed += cur.Span[i];
                cur = cur.Forward[i];
            }

        cur = cur.Forward[0];
        for (int pos = startRank; pos <= stopRank && cur != null; pos++, cur = cur.Forward[0])
            yield return (cur.Key, cur.Score);
    }

    // ─── Range by Score ─────────────────────────────────────────────────────

    /// <summary>
    /// Lấy các phần tử trong khoảng score [minScore, maxScore] (inclusive).
    /// Tương đương ZRANGEBYSCORE key min max.
    /// </summary>
    public IEnumerable<(TKey Key, TValue Score)> RangeByScore(
        TValue minScore, TValue maxScore,
        bool minExclusive = false, bool maxExclusive = false)
    {
        var cur = _head;
        // Nhảy đến node đầu tiên >= minScore
        for (int i = _level - 1; i >= 0; i--)
            while (cur.Forward[i] != null)
            {
                int cmp = _scoreComparer.Compare(cur.Forward[i].Score, minScore);
                if (cmp < 0 || (minExclusive && cmp == 0))
                    cur = cur.Forward[i];
                else
                    break;
            }

        cur = cur.Forward[0];
        while (cur != null)
        {
            int cmp = _scoreComparer.Compare(cur.Score, maxScore);
            if (cmp > 0 || (maxExclusive && cmp == 0)) yield break;
            yield return (cur.Key, cur.Score);
            cur = cur.Forward[0];
        }
    }

    // ─── Count by Score ─────────────────────────────────────────────────────

    /// <summary>Đếm số phần tử trong khoảng score [min, max]. O(log n).</summary>
    public int CountByScore(TValue minScore, TValue maxScore)
    {
        if (_count == 0 || _scoreComparer.Compare(minScore, maxScore) > 0)
            return 0;

        // rank (0-based) của phần tử đầu tiên có score >= minScore
        int firstRank = FindRankOfFirstGe(minScore);
        if (firstRank >= _count) return 0;

        // rank (0-based) của phần tử cuối cùng có score <= maxScore
        int lastRank = FindRankOfLastLe(maxScore);
        if (lastRank < 0) return 0;

        return firstRank <= lastRank ? lastRank - firstRank + 1 : 0;
    }

    /// <summary>Rank (0-based) của phần tử đầu tiên có score ≥ minScore. Trả _count nếu không có.</summary>
    private int FindRankOfFirstGe(TValue score)
    {
        int rank = 0;
        var cur = _head;
        for (int i = _level - 1; i >= 0; i--)
            while (cur.Forward[i] != null &&
                   _scoreComparer.Compare(cur.Forward[i].Score, score) < 0)
            {
                rank += cur.Span[i];
                cur = cur.Forward[i];
            }

        // cur.Forward[0] là phần tử đầu tiên có score >= minScore (hoặc null)
        return rank; // 0-based rank
    }

    /// <summary>Rank (0-based) của phần tử cuối cùng có score ≤ maxScore. Trả -1 nếu không có.</summary>
    private int FindRankOfLastLe(TValue score)
    {
        int rank = 0;
        var cur = _head;
        for (int i = _level - 1; i >= 0; i--)
            while (cur.Forward[i] != null &&
                   _scoreComparer.Compare(cur.Forward[i].Score, score) <= 0)
            {
                rank += cur.Span[i];
                cur = cur.Forward[i];
            }

        return rank - 1; // 0-based rank, -1 nếu không tìm thấy
    }

    // ─── Remove by Score / Rank ─────────────────────────────────────────────

    /// <summary>Xoá tất cả phần tử trong khoảng score [min, max]. Trả về số đã xoá. O(log n + k).</summary>
    public int RemoveRangeByScore(TValue minScore, TValue maxScore)
    {
        if (_count == 0 || _scoreComparer.Compare(minScore, maxScore) > 0)
            return 0;

        // Tìm update[] — vị trí ngay trước phần tử đầu tiên >= minScore
        var update = _sharedUpdate;
        var cur = _head;
        for (int i = _level - 1; i >= 0; i--)
        {
            while (cur.Forward[i] != null &&
                   _scoreComparer.Compare(cur.Forward[i].Score, minScore) < 0)
                cur = cur.Forward[i];
            update[i] = cur;
        }

        // Xoá batch: duyệt tầng 0, gỡ từng node khỏi tất cả tầng
        int removed = 0;
        var node = update[0].Forward[0];
        while (node != null && _scoreComparer.Compare(node.Score, maxScore) <= 0)
        {
            var next = node.Forward[0];

            for (int i = 0; i < _level; i++)
            {
                if (update[i].Forward[i] == node)
                {
                    update[i].Span[i] += node.Span[i] - 1;
                    update[i].Forward[i] = node.Forward[i];
                }
                else
                {
                    update[i].Span[i]--;
                }
            }

            _dict.Remove(node.Key);
            _count--;
            removed++;
            node = next;
        }

        while (_level > 1 && _head.Forward[_level - 1] == null)
            _level--;

        Array.Clear(update, 0, MaxLevel);

        return removed;
    }

    /// <summary>Xoá các phần tử theo khoảng rank [start, stop] (inclusive, 0-based). Trả về số đã xoá. O(log n + k).</summary>
    public int RemoveRangeByRank(int startRank, int stopRank)
    {
        if (startRank < 0) startRank = Math.Max(0, _count + startRank);
        if (stopRank < 0) stopRank = Math.Max(0, _count + stopRank);
        stopRank = Math.Min(stopRank, _count - 1);
        if (startRank > stopRank) return 0;

        // Tìm update[] — vị trí ngay trước node tại startRank
        var update = _sharedUpdate;
        int traversed = 0;
        var cur = _head;
        for (int i = _level - 1; i >= 0; i--)
        {
            while (cur.Forward[i] != null && traversed + cur.Span[i] <= startRank)
            {
                traversed += cur.Span[i];
                cur = cur.Forward[i];
            }

            update[i] = cur;
        }

        // Xoá batch
        int removed = 0;
        var node = update[0].Forward[0];
        for (int pos = startRank; pos <= stopRank && node != null; pos++)
        {
            var next = node.Forward[0];

            for (int i = 0; i < _level; i++)
            {
                if (update[i].Forward[i] == node)
                {
                    update[i].Span[i] += node.Span[i] - 1;
                    update[i].Forward[i] = node.Forward[i];
                }
                else
                {
                    update[i].Span[i]--;
                }
            }

            _dict.Remove(node.Key);
            _count--;
            removed++;
            node = next;
        }

        while (_level > 1 && _head.Forward[_level - 1] == null)
            _level--;

        Array.Clear(update, 0, MaxLevel);

        return removed;
    }

    // ─── Enumerate (toàn bộ, theo thứ tự score tăng dần) ───────────────────

    public IEnumerable<(TKey Key, TValue Score)> GetAll()
    {
        var cur = _head.Forward[0];
        while (cur != null)
        {
            yield return (cur.Key, cur.Score);
            cur = cur.Forward[0];
        }
    }

    // ─── Debug / ToString ───────────────────────────────────────────────────

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"DictionaryScore (count={_count}, levels={_level})");
        foreach (var (key, score) in GetAll())
            sb.AppendLine($"  [{score}] {key}");
        return sb.ToString();
    }
}
}
