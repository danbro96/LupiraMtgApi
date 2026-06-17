using LupiraMtgApi.Catalog.Data;
using Microsoft.EntityFrameworkCore;

namespace LupiraMtgApi.Recognition.Infrastructure.Imaging;

/// <summary>
/// In-memory perceptual-hash index over all card art crops with a known pHash.
/// Backed by a BK-tree for O(log n) average-case Hamming-distance queries.
/// </summary>
public sealed class PHashIndex
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PHashIndex> _logger;
    private readonly SemaphoreSlim _rebuildLock = new(1, 1);
    private volatile BkTreeNode? _root;
    private int _count;

    public PHashIndex(IServiceScopeFactory scopeFactory, ILogger<PHashIndex> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public int Count => _count;

    public bool IsLoaded => _root is not null;

    public async Task RebuildAsync(CancellationToken ct)
    {
        await _rebuildLock.WaitAsync(ct);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LupiraMtgDbContext>();

            var rows = await db.CardPrintings
                .AsNoTracking()
                .Where(p => p.ArtPHash != null)
                .Select(p => new { p.Id, Hash = p.ArtPHash!.Value })
                .ToListAsync(ct);

            BkTreeNode? newRoot = null;
            foreach (var row in rows)
            {
                if (newRoot is null)
                {
                    newRoot = new BkTreeNode(row.Hash, row.Id);
                }
                else
                {
                    Insert(newRoot, row.Hash, row.Id);
                }
            }

            _root = newRoot;
            _count = rows.Count;
            _logger.LogInformation("PHashIndex rebuilt with {Count} entries", rows.Count);
        }
        finally
        {
            _rebuildLock.Release();
        }
    }

    public IReadOnlyList<PHashHit> Search(long queryHash, int maxHamming)
    {
        var results = new List<PHashHit>();
        if (_root is { } r)
        {
            SearchInternal(r, queryHash, maxHamming, results);
            results.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        }

        return results;
    }

    private static void Insert(BkTreeNode node, long hash, string printingId)
    {
        var current = node;
        while (true)
        {
            var d = HammingDistance(current.Hash, hash);
            if (d == 0)
            {
                // Duplicate hash — different printing IDs can share an art hash
                // (reprints with identical art). Fold into a single node, append the ID.
                current.AddPrinting(printingId);
                return;
            }

            if (current.TryGetChild(d, out var child))
            {
                current = child;
            }
            else
            {
                current.AddChild(d, new BkTreeNode(hash, printingId));
                return;
            }
        }
    }

    private static void SearchInternal(BkTreeNode node, long query, int tol, List<PHashHit> results)
    {
        var d = HammingDistance(node.Hash, query);
        if (d <= tol)
        {
            foreach (var pid in node.PrintingIds)
            {
                results.Add(new PHashHit(pid, d));
            }
        }

        var lo = d - tol;
        var hi = d + tol;
        foreach (var (childDistance, child) in node.Children)
        {
            if (childDistance >= lo && childDistance <= hi)
            {
                SearchInternal(child, query, tol, results);
            }
        }
    }

    private static int HammingDistance(long a, long b)
    {
        var xor = unchecked((ulong) a) ^ unchecked((ulong) b);
        return System.Numerics.BitOperations.PopCount(xor);
    }

    public readonly record struct PHashHit(string PrintingId, int Distance);

    private sealed class BkTreeNode
    {
        public BkTreeNode(long hash, string firstPrintingId)
        {
            this.Hash = hash;
            this.PrintingIds = new List<string> { firstPrintingId };
            this.Children = new Dictionary<int, BkTreeNode>(4);
        }

        public long Hash { get; }

        public List<string> PrintingIds { get; }

        public Dictionary<int, BkTreeNode> Children { get; }

        public void AddPrinting(string printingId)
        {
            this.PrintingIds.Add(printingId);
        }

        public bool TryGetChild(int distance, out BkTreeNode child)
        {
            return this.Children.TryGetValue(distance, out child!);
        }

        public void AddChild(int distance, BkTreeNode child)
        {
            this.Children[distance] = child;
        }
    }
}
