using ei8.Cortex.Coding.Versioning;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ei8.Cortex.Coding.Persistence.Versioning
{
    /// <summary>
    /// Provides functionality for retrieving Creations.
    /// </summary>
    public interface ICreationReadRepository
    {
        /// <summary>
        /// Gets Creations using the specified Subject Id.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task<IEnumerable<Creation>> GetBySubjectId(Guid id, CancellationToken token = default);
    }
}
