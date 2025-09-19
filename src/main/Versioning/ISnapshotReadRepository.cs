using ei8.Cortex.Coding.Versioning;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ei8.Cortex.Coding.Persistence.Versioning
{
    /// <summary>
    /// Provides functionality for retrieving Snapshots.
    /// </summary>
    public interface ISnapshotReadRepository
    {
        /// <summary>
        /// Gets Snapshots using the specified IDs.
        /// </summary>
        /// <param name="ids"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task<IEnumerable<Snapshot>> GetByIds(IEnumerable<Guid> ids, CancellationToken token = default);
    }
}
