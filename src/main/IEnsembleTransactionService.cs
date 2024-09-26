using ei8.EventSourcing.Client;
using System;
using System.Threading.Tasks;

namespace ei8.Cortex.Coding.Persistence
{
    public interface IEnsembleTransactionService
    {
        Task SaveAsync(
            ITransaction transaction,
            Ensemble ensemble,
            Guid authorId
        );
    }
}
