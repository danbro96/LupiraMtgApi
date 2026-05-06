using LupiraMtgApi.Data;
using Microsoft.EntityFrameworkCore;

namespace LupiraMtgApi.Services.Imaging;

/// <summary>
/// In-memory perceptual-hash index over the full card image (Scryfall image_uris.normal),
/// parallel to <see cref="PHashIndex"/>. Captures frame + name + art + text together;
/// more discriminative than art-only but more sensitive to lighting/foil variance, so
/// the scan path uses a wider hamming cutoff (FullCardPHashMaxHamming, default 16).
/// Backed by a BK-tree mirroring PHashIndex's structure.
/// </summary>
public sealed class FullCardPHashIndex
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FullCardPHashIndex> _logger;
    private readonly SemaphoreSlim _rebuildLock = new(1, 1);
    private volatile BkTreeNode? _root;
    private int _count;

    public FullCardPHashIndex(IServiceScopeFactory scopeFactory, ILogger<FullCardPHashIndex> logger)
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
                .Where(p => p.FullCardPHash != null)
                .Select(p => new { p.Id, Hash = p.FullCardPHash!.Value })
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
            _logger.LogInformation("FullCardPHashIndex rebuilt with {Count} entries", rows.Count);
        }
        finally
        {
            _rebuildLock.Release();
        }
    }

    public IReadOnlyList<PHashIndex.PHashHit> Search(long queryHash, int maxHamming)
    {
        var results = new List<PHashIndex.PHashHit>();
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

    private static void SearchInternal(BkTreeNode node, long query, int tol, List<PHashIndex.PHashHit> results)
    {
        var d = HammingDistance(node.Hash, query);
        if (d <= tol)
        {
            foreach (var pid in node.PrintingIds)
            {
                results.Add(new PHashIndex.PHashHit(pid, d));
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
