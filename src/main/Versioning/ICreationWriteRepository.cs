using ei8.Cortex.Coding.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace ei8.Cortex.Coding.Persistence.Versioning
{
    public interface ICreationWriteRepository
    {
        Task Save(Creation value, CancellationToken token = default);
    }
}
