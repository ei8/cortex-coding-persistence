using ei8.Cortex.Coding.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace ei8.Cortex.Coding.Persistence.Versioning
{
    public interface ISnapshotWriteRepository
    {
        Task Save(Snapshot value, CancellationToken token = default);
    }
}
