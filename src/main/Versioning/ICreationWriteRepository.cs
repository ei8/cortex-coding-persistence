using ei8.Cortex.Coding.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace ei8.Cortex.Coding.Persistence.Versioning
{
    /// <summary>
    /// Provides functionality for saving Creations.
    /// </summary>
    public interface ICreationWriteRepository
    {
        /// <summary>
        /// Saves the specified Creation value.
        /// </summary>
        /// <param name="value"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task Save(Creation value, CancellationToken token = default);
    }
}
