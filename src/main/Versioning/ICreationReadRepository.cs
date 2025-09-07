using ei8.Cortex.Coding.Versioning;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ei8.Cortex.Coding.Persistence.Versioning
{
    public interface ICreationReadRepository
    {
        Task<IEnumerable<Creation>> GetBySubjectId(Guid id, CancellationToken token = default);
    }
}
