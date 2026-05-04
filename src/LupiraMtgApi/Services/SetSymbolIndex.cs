using LupiraMtgApi.Data;
using Microsoft.EntityFrameworkCore;

namespace LupiraMtgApi.Services;

/// <summary>
/// In-memory perceptual-hash index over all set-symbol silhouettes.
/// BK-tree, mirrors PHashIndex but keyed by SetCode rather than printing id.
/// </summary>
public sealed class SetSymbolIndex
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<SetSymbolIndex> logger;
    private readonly SemaphoreSlim rebuildLock = new(1, 1);
    private volatile BkTreeNode? root;
    private int count;

    public SetSymbolIndex(IServiceScopeFactory scopeFactory, ILogger<SetSymbolIndex> logger)
    {
        this.scopeFactory = scopeFactory;
        this.logger = logger;
    }

    public int Count => this.count;

    public bool IsLoaded => this.root is not null;

    public async Task RebuildAsync(CancellationToken ct)
    {
        await this.rebuildLock.WaitAsync(ct);
        try
        {
            await using var scope = this.scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LupiraMtgDbContext>();

            var rows = await db.Sets
                .AsNoTracking()
                .Where(s => s.IconPHash != null)
                .Select(s => new { s.Code, Hash = s.IconPHash!.Value })
                .ToListAsync(ct);

            BkTreeNode? newRoot = null;
            foreach (var row in rows)
            {
                if (newRoot is null)
                {
                    newRoot = new BkTreeNode(row.Hash, row.Code);
                }
                else
                {
                    Insert(newRoot, row.Hash, row.Code);
                }
            }

            this.root = newRoot;
            this.count = rows.Count;
            this.logger.LogInformation("SetSymbolIndex rebuilt with {Count} entries", rows.Count);
        }
        finally
        {
            this.rebuildLock.Release();
        }
    }

    public IReadOnlyList<SetSymbolHit> Search(long queryHash, int maxHamming)
    {
        var results = new List<SetSymbolHit>();
        if (this.root is { } r)
        {
            SearchInternal(r, queryHash, maxHamming, results);
            results.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        }

        return results;
    }

    private static void Insert(BkTreeNode node, long hash, string setCode)
    {
        var current = node;
        while (true)
        {
            var d = HammingDistance(current.Hash, hash);
            if (d == 0)
            {
                current.AddSetCode(setCode);
                return;
            }

            if (current.TryGetChild(d, out var child))
            {
                current = child;
            }
            else
            {
                current.AddChild(d, new BkTreeNode(hash, setCode));
                return;
            }
        }
    }

    private static void SearchInternal(BkTreeNode node, long query, int tol, List<SetSymbolHit> results)
    {
        var d = HammingDistance(node.Hash, query);
        if (d <= tol)
        {
            foreach (var code in node.SetCodes)
            {
                results.Add(new SetSymbolHit(code, d));
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
        var xor = unchecked((ulong)a) ^ unchecked((ulong)b);
        return System.Numerics.BitOperations.PopCount(xor);
    }

    public readonly record struct SetSymbolHit(string SetCode, int Distance);

    private sealed class BkTreeNode
    {
        public BkTreeNode(long hash, string firstSetCode)
        {
            this.Hash = hash;
            this.SetCodes = new List<string> { firstSetCode };
            this.Children = new Dictionary<int, BkTreeNode>(4);
        }

        public long Hash { get; }

        public List<string> SetCodes { get; }

        public Dictionary<int, BkTreeNode> Children { get; }

        public void AddSetCode(string setCode)
        {
            this.SetCodes.Add(setCode);
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
