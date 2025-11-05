using ei8.Cortex.Coding.Model.Wrappers;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ei8.Cortex.Coding.Persistence.Wrappers
{
    /// <summary>
    /// Provides functionality for retrieving StringWrappers.
    /// </summary>
    public interface IStringWrapperReadRepository
    {
        /// <summary>
        /// Gets StringWrappers using the specified IDs.
        /// </summary>
        /// <param name="ids"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task<IEnumerable<StringWrapper>> GetByIds(
            IEnumerable<Guid> ids,
            CancellationToken token = default
        );
    }
}
