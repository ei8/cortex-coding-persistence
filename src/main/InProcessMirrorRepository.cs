using ei8.EventSourcing.Client;
using Microsoft.Extensions.Options;
using neurUL.Common.Domain.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ei8.Cortex.Coding.Persistence
{
    public class InProcessMirrorRepository : MirrorRepositoryBase, IMirrorRepository
    {
        private readonly IEnumerable<MirrorConfig> mirrorConfigs;
        private readonly INetworkTransactionData networkTransactionData;

        public InProcessMirrorRepository(
            ITransaction transaction,
            INetworkTransactionService networkTransactionService,
            INetworkTransactionData networkTransactionData,
            IOptions<List<MirrorConfig>> mirrorConfigs
        ) : base(
            transaction, 
            networkTransactionService
        )
        {
            AssertionConcern.AssertArgumentNotNull(mirrorConfigs, nameof(mirrorConfigs));

            this.mirrorConfigs = mirrorConfigs.Value.ToArray();
            this.networkTransactionData = networkTransactionData;
        }

        public override Task<IEnumerable<MirrorConfig>> GetAllMissingAsync(IEnumerable<string> keys)
        {
            AssertionConcern.AssertArgumentNotNull(keys, nameof(keys));
            AssertionConcern.AssertArgumentValid(
                k => k.Count() > 0,
                keys,
                "Specified 'keys' cannot be an empty array.",
                nameof(keys)
            );
            AssertionConcern.AssertArgumentValid(
                k => !k.Any(s => string.IsNullOrWhiteSpace(s)),
                keys,
                "Specified 'keys' cannot contain an empty string.",
                nameof(keys)
            );

            // get configs matching specified keys
            var configs = mirrorConfigs.Where(mc => mc.Keys.Any(mck => keys.Contains(mck)));
            MirrorRepository.ValidateRequiredItems(
                "At least one Mirror configuration was not found",
                keys,
                configs,
                (k, i) => i.Keys.Contains(k),
                k => k
            );

            // get neurons not matching configs
            var missing = MirrorRepository.ValidateRequiredItems(
                "At least one local copy of required Mirrors was not found",
                configs,
                Enumerable.Empty<Neuron>(),
                (k, i) => i.MirrorUrl == k.Url,
                k => string.Join(",", k.Keys),
                false
            );

            return Task.FromResult(missing);
        }

        public Task<IDictionary<string, Neuron>> GetByKeysAsync(
            IEnumerable<string> keys, 
            bool throwErrorIfMissing
        )
        {
            var foundMirrors = this.networkTransactionData.SavedTransientNeurons.Where(
                im => this.mirrorConfigs.Any(
                    mc => mc.Url == im.MirrorUrl && mc.Keys.Any(mck => keys.Contains(mck))
                )
            );
            
            return Task.FromResult(
                MirrorRepository.GetKeyMirrorDictionary(
                    keys, 
                    foundMirrors, 
                    this.mirrorConfigs
                )
            );
        }
    }
}
