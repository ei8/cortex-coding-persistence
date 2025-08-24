using ei8.EventSourcing.Client;
using Nancy.Extensions;
using neurUL.Common.Domain.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ei8.Cortex.Coding.Persistence
{
    public abstract class MirrorRepositoryBase
    {
        private readonly ITransaction transaction;
        private readonly INetworkTransactionService networkTransactionService;

        protected MirrorRepositoryBase(
            ITransaction transaction,
            INetworkTransactionService networkTransactionService
        )
        {
            AssertionConcern.AssertArgumentNotNull(transaction, nameof(transaction));
            AssertionConcern.AssertArgumentNotNull(networkTransactionService, nameof(networkTransactionService));

            this.networkTransactionService = networkTransactionService;
            this.transaction = transaction;
        }

        public async Task<bool> Initialize(IEnumerable<string> keys)
        {
            var result = false;

            var missingMirrorsConfig = await this.GetAllMissingAsync(
                keys
            );

            if (missingMirrorsConfig.Any())
            {
                var newMirrors = missingMirrorsConfig.Select(mmc =>
                    Neuron.CreateTransient(
                        Guid.NewGuid(),
                        null,
                        mmc.Url,
                        null
                    )
                ).DistinctBy(nm => nm.MirrorUrl);

                await this.Save(newMirrors);

                result = true;
            }

            return result;
        }

        // TODO: specify region to save values
        public virtual async Task Save(IEnumerable<Neuron> values)
        {
            var network = new Network();

            foreach (var n in values)
                network.AddReplace(n);

            await this.networkTransactionService.SaveAsync(
                this.transaction,
                network
            );
        }

        public abstract Task<IEnumerable<MirrorConfig>> GetAllMissingAsync(IEnumerable<string> keys);
    }
}
