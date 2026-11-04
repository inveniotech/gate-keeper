// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Engine;

/// <summary>Partitions work between processor-bounded workers that own their result lists.</summary>
internal static class BoundedWorkers
{
    internal static async Task<IReadOnlyList<TResult>> MapAsync<T, TResult>(
        IReadOnlyList<T> input,
        Func<T, CancellationToken, Task<TResult>> process,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var count = Math.Min(Environment.ProcessorCount, input.Count);
        var workers = new Task<List<TResult>>[count];
        for (var index = 0; index < count; index++)
        {
            var start = index;
            workers[index] = Task.Run(async () =>
            {
                var results = new List<TResult>();
                for (var offset = start; offset < input.Count; offset += count)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    results.Add(await process(input[offset], cancellationToken).ConfigureAwait(false));
                }

                return results;
            }, cancellationToken);
        }

        var batches = await Task.WhenAll(workers).ConfigureAwait(false);
        var merged = new List<TResult>(input.Count);
        foreach (var batch in batches)
        {
            merged.AddRange(batch);
        }

        return merged;
    }
}
