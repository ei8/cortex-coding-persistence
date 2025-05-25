using ei8.Cortex.Library.Common;
using ei8.EventSourcing.Client;
using Microsoft.Extensions.Options;
using neurUL.Common.Domain.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ei8.Cortex.Coding.Persistence
{
    public class MirrorRepository : IMirrorRepository
    {
        private struct GetResult
        {
            public QueryResult QueryResult;
            public IEnumerable<MirrorConfig> Config;
            public IEnumerable<MirrorConfig> Missing;
        }

        private readonly INetworkRepository networkRepository;
        private readonly ITransaction transaction;
        private readonly INetworkTransactionService networkTransactionService;
        private readonly IEnumerable<MirrorConfig> mirrorConfigs;

        public MirrorRepository(
            INetworkRepository networkRepository,
            ITransaction transaction,
            INetworkTransactionService networkTransactionService,
            IOptions<List<MirrorConfig>> mirrorConfigs
        )
        {
            AssertionConcern.AssertArgumentNotNull(networkRepository, nameof(networkRepository));
            AssertionConcern.AssertArgumentNotNull(transaction, nameof(transaction));
            AssertionConcern.AssertArgumentNotNull(networkTransactionService, nameof(networkTransactionService));
            AssertionConcern.AssertArgumentNotNull(mirrorConfigs, nameof(mirrorConfigs));

            this.networkRepository = networkRepository;
            this.transaction = transaction;
            this.networkTransactionService = networkTransactionService;
            this.mirrorConfigs = mirrorConfigs.Value.ToArray();
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
                );

                await this.Save(newMirrors);

                result = true;
            }

            return result;
        }

        // TODO: specify region to save values
        public async Task Save(IEnumerable<Neuron> values)
        {
            var network = new Network();

            foreach (var n in values)
                network.AddReplace(n);

            await this.networkTransactionService.SaveAsync(
                this.transaction,
                network
            );
        }

        public async Task<IEnumerable<MirrorConfig>> GetAllMissingAsync(IEnumerable<string> keys) => 
            (await this.GetByKeysCore(keys, false)).Missing;

        private async Task<GetResult> GetByKeysCore(IEnumerable<string> keys, bool throwErrorIfMissing)
        {
            var result = new GetResult();

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

            result.Config = mirrorConfigs.Where(er => keys.Contains(er.Key));
            MirrorRepository.ValidateRequiredItems(
                "At least one Mirror configuration was not found",
                keys,
                result.Config,
                (k, i) => i.Key == k,
                k => k
            );

            result.QueryResult = await this.networkRepository.GetByQueryAsync(
                new NeuronQuery()
                {
                    ExternalReferenceUrl = result.Config.Select(er => er.Url),
                    SortBy = SortByValue.NeuronCreationTimestamp,
                    SortOrder = SortOrderValue.Descending,
                    PageSize = result.Config.Count()
                },
                false
            );

            result.Missing = MirrorRepository.ValidateRequiredItems(
                "At least one local copy of required Mirrors was not found",
                result.Config,
                result.QueryResult.Network.GetItems<Neuron>(),
                (k, i) => i.MirrorUrl == k.Url,
                k => k.Key,
                throwErrorIfMissing
            );

            return result;
        }

        public async Task<IDictionary<string, Neuron>> GetByKeysAsync(IEnumerable<string> keys, bool throwErrorIfMissing = true)
        {
            var getResult = await this.GetByKeysCore(keys, throwErrorIfMissing);

            return getResult.QueryResult.Network
                .GetItems<Neuron>()
                .ToDictionary(
                    n => getResult.Config.Single(mc => mc.Url == n.MirrorUrl).Key
                );
        }

        private static IEnumerable<TKey> ValidateRequiredItems<TKey, TItem>(
            string errorMessage,
            IEnumerable<TKey> keys,
            IEnumerable<TItem> items,
            Func<TKey, TItem, bool> equalityComparer,
            Func<TKey, string> keyConverter,
            bool throwErrorIfMissingAny = true
        )
        {
            var unmatchedKeys = keys.Where(k => !items.Any(er => equalityComparer(k, er)));

            AssertionConcern.AssertStateTrue(
                !throwErrorIfMissingAny || !unmatchedKeys.Any(),
                $"{errorMessage}: '{string.Join("', '", unmatchedKeys.Select(uk => keyConverter(uk)))}'"
            );

            return unmatchedKeys;
        }
    }
}
