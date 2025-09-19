using ei8.Cortex.Coding.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace ei8.Cortex.Coding.Persistence.Versioning
{
    /// <summary>
    /// Provides functionality for saving Snapshots.
    /// </summary>
    public interface ISnapshotWriteRepository
    {
        /// <summary>
        /// Saves the specified Snapshot value.
        /// </summary>
        /// <param name="value"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task Save(Snapshot value, CancellationToken token = default);
    }
}
