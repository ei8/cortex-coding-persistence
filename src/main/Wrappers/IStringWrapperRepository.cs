using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using System;

namespace ei8.Cortex.Coding.Persistence.Wrappers
{
    public interface IStringWrapperRepository
    {
        Task<IEnumerable<StringWrapper>> GetByIds(
            IEnumerable<Guid> ids,
            string userId,
            CancellationToken token = default
        );

        Task Save(StringWrapper message, CancellationToken token = default);
    }
}
