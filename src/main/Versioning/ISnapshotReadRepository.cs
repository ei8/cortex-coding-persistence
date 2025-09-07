using ei8.Cortex.Coding.Versioning;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ei8.Cortex.Coding.Persistence.Versioning
{
    public interface ISnapshotReadRepository
    {
        Task<IEnumerable<Snapshot>> GetByIds(IEnumerable<Guid> ids, CancellationToken token = default);
    }
}
