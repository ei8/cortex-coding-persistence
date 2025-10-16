using ei8.Cortex.Coding.Wrappers;
using System.Threading;
using System.Threading.Tasks;

namespace ei8.Cortex.Coding.Persistence.Wrappers
{
    /// <summary>
    /// Provides functionality for writing StringWrappers.
    /// </summary>
    public interface IStringWrapperWriteRepository
    {
        /// <summary>
        /// Saves the specified StringWrapper value.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task Save(
            StringWrapper value, 
            CancellationToken token = default
        );
    }
}
