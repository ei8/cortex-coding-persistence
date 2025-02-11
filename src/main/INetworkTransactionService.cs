using ei8.EventSourcing.Client;
using System.Threading.Tasks;

namespace ei8.Cortex.Coding.Persistence
{
    public interface INetworkTransactionService
    {
        Task SaveAsync(
            ITransaction transaction,
            Network Network
        );
    }
}
