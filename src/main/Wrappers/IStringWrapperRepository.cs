using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using System;
using ei8.Cortex.Coding.Wrappers;

namespace ei8.Cortex.Coding.Persistence.Wrappers
{
    /// <summary>
    /// Provides functionality for retrieving StringWrappers.
    /// </summary>
    public interface IStringWrapperRepository
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

        /// <summary>
        /// Saves the specified StringWrapper value.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task Save(StringWrapper value, CancellationToken token = default);
    }
}
