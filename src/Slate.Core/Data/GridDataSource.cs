namespace Slate.Data;

/// <summary>What a server-mode grid asks for: the view state plus a row window.</summary>
public sealed record GridQuery
{
    public IReadOnlyList<GridSort> Sorts { get; init; } = [];
    public IReadOnlyList<GridFilter> Filters { get; init; } = [];
    public string QuickFilter { get; init; } = "";
    public int Offset { get; init; }
    public int Count { get; init; } = 100;

    public static GridQuery FromState(GridState state, int offset = 0, int count = 100) =>
        new() { Sorts = state.Sorts, Filters = state.Filters, QuickFilter = state.QuickFilter, Offset = offset, Count = count };

    /// <summary>Same query, different window.</summary>
    public GridQuery Window(int offset, int count) => this with { Offset = offset, Count = count };

    /// <summary>True when two queries describe the same result set (ignoring the window).</summary>
    public bool SameResultSet(GridQuery other) =>
        Sorts.SequenceEqual(other.Sorts) && Filters.SequenceEqual(other.Filters) && QuickFilter == other.QuickFilter;
}

/// <summary>A window of rows and the total matching count.</summary>
public sealed record GridResult<T>(IReadOnlyList<T> Items, int TotalCount);

/// <summary>Server-side data for a grid. Implementations must honour cancellation.</summary>
public interface IGridDataSource<T>
{
    Task<GridResult<T>> QueryAsync(GridQuery query, CancellationToken cancellationToken = default);
}

/// <summary>
/// An <see cref="IGridDataSource{T}"/> over an in-memory list, using the same filter/sort semantics as the client
/// pipeline. Optional simulated latency (for demos and tests).
/// </summary>
public sealed class InMemoryGridDataSource<T> : IGridDataSource<T>
{
    private readonly IReadOnlyList<T> _items;
    private readonly DataPipeline<T> _pipeline;
    private readonly TimeSpan _latency;
    private readonly TimeProvider _time;

    public InMemoryGridDataSource(IReadOnlyList<T> items, IReadOnlyList<GridColumn<T>> columns, TimeSpan latency = default, TimeProvider? timeProvider = null)
    {
        _items = items;
        _pipeline = new DataPipeline<T>(columns);
        _latency = latency;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Queries executed so far (for tests and diagnostics).</summary>
    public int QueryCount { get; private set; }

    public async Task<GridResult<T>> QueryAsync(GridQuery query, CancellationToken cancellationToken = default)
    {
        QueryCount++;
        if (_latency > TimeSpan.Zero)
            await Task.Delay(_latency, _time, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var state = new GridState { Sorts = query.Sorts, Filters = query.Filters, QuickFilter = query.QuickFilter };
        var predicate = _pipeline.BuildPredicate(state);
        var indices = new List<int>(_items.Count);
        for (var i = 0; i < _items.Count; i++)
            if (predicate is null || predicate(_items[i])) indices.Add(i);
        _pipeline.Sort(_items, indices, query.Sorts);

        var window = indices.Skip(Math.Max(0, query.Offset)).Take(Math.Max(0, query.Count)).Select(i => _items[i]).ToList();
        return new GridResult<T>(window, indices.Count);
    }
}

/// <summary>
/// Block cache for infinite scrolling over an <see cref="IGridDataSource{T}"/>. Loads fixed-size blocks on demand,
/// de-duplicates in-flight requests, and cancels everything (discarding late results) when the query changes.
/// </summary>
public sealed class GridRowCache<T>
{
    private readonly IGridDataSource<T> _source;
    private readonly Dictionary<int, T[]> _blocks = new();
    private readonly Dictionary<int, Task> _inFlight = new();
    private readonly object _gate = new();
    private CancellationTokenSource _cts = new();
    private GridQuery _query = new();
    private int _generation;

    public GridRowCache(IGridDataSource<T> source, int blockSize = 100)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(blockSize, 1);
        _source = source;
        BlockSize = blockSize;
    }

    public int BlockSize { get; }

    /// <summary>Total matching rows, once the first block has loaded.</summary>
    public int? TotalCount { get; private set; }

    public GridQuery Query => _query;

    /// <summary>Raised (possibly off the UI thread) when a block arrives or the cache resets.</summary>
    public event EventHandler? Changed;

    /// <summary>Error from the most recent failed load, if any.</summary>
    public Exception? LastError { get; private set; }

    /// <summary>Replaces the query: cancels in-flight loads and clears cached rows.</summary>
    public void SetQuery(GridQuery query)
    {
        lock (_gate)
        {
            _cts.Cancel();
            _cts = new CancellationTokenSource();
            _generation++;
            _blocks.Clear();
            _inFlight.Clear();
            _query = query;
            TotalCount = null;
            LastError = null;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool TryGet(int index, out T item)
    {
        lock (_gate)
        {
            if (_blocks.TryGetValue(index / BlockSize, out var block) && index % BlockSize < block.Length)
            {
                item = block[index % BlockSize];
                return true;
            }
        }
        item = default!;
        return false;
    }

    public bool IsLoading(int index)
    {
        lock (_gate) return _inFlight.ContainsKey(index / BlockSize);
    }

    public int LoadedBlockCount
    {
        get { lock (_gate) return _blocks.Count; }
    }

    /// <summary>Makes sure every block overlapping [first, first+count) is loaded or loading; completes when they are.</summary>
    public Task EnsureRangeAsync(int first, int count)
    {
        if (count <= 0) return Task.CompletedTask;
        var tasks = new List<Task>();
        lock (_gate)
        {
            var lastIndex = first + count - 1;
            if (TotalCount is { } total) lastIndex = Math.Min(lastIndex, total - 1);
            for (var b = Math.Max(0, first) / BlockSize; b <= lastIndex / BlockSize && lastIndex >= 0; b++)
            {
                if (_blocks.ContainsKey(b)) continue;
                if (!_inFlight.TryGetValue(b, out var t))
                {
                    t = LoadBlock(b, _generation, _cts.Token);
                    _inFlight[b] = t;
                }
                tasks.Add(t);
            }
        }
        return Task.WhenAll(tasks);
    }

    private async Task LoadBlock(int block, int generation, CancellationToken ct)
    {
        try
        {
            var result = await _source.QueryAsync(_query.Window(block * BlockSize, BlockSize), ct).ConfigureAwait(false);
            lock (_gate)
            {
                if (generation != _generation) return;
                _blocks[block] = result.Items.ToArray();
                TotalCount = result.TotalCount;
                _inFlight.Remove(block);
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Superseded by a newer query.
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                if (generation != _generation) return;
                _inFlight.Remove(block);
                LastError = ex;
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
