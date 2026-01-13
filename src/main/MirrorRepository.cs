using ei8.Cortex.Coding.Mirrors;
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
    /// <summary>
    /// Represents a MirrorRepository.
    /// </summary>
    public class MirrorRepository : MirrorRepositoryBase, IMirrorRepository
    {
        // TODO:1 transfer to ei8.Cortex.Coding and use as return value of all Get methods
        //public class GetResult<T>
        //{
        //    public GetResult(Guid userNeuronId, T value)
        //    {
        //        this.UserNeuronId = userNeuronId;
        //        this.Value = value;
        //    }
        //    public Guid UserNeuronId { get; private set; }
        //    public T Value { get; private set; }
        //}

        private struct GetResultCore
        {
            public QueryResult QueryResult;
            public IEnumerable<MirrorConfig> Config;
            public IEnumerable<MirrorConfig> Missing;
        }

        private readonly INetworkRepository networkRepository;
        private readonly IEnumerable<MirrorConfig> mirrorConfigs;

        /// <summary>
        /// Constructs a MIrrorRepository.
        /// </summary>
        /// <param name="networkRepository"></param>
        /// <param name="transaction"></param>
        /// <param name="networkTransactionService"></param>
        /// <param name="mirrorConfigs"></param>
        public MirrorRepository(
            INetworkRepository networkRepository,
            ITransaction transaction,
            INetworkTransactionService networkTransactionService,
            IOptions<List<MirrorConfig>> mirrorConfigs
        ) : base(
            transaction, 
            networkTransactionService
        )
        {
            AssertionConcern.AssertArgumentNotNull(networkRepository, nameof(networkRepository));
            AssertionConcern.AssertArgumentNotNull(mirrorConfigs, nameof(mirrorConfigs));

            this.networkRepository = networkRepository;
            this.mirrorConfigs = mirrorConfigs.Value.ToArray();
        }

        /// <summary>
        /// Gets the MirrorConfigs matching the specified keys that are not found in Persistence.
        /// </summary>
        /// <param name="keys"></param>
        /// <returns></returns>
        public override async Task<IEnumerable<MirrorConfig>> GetAllMissingAsync(IEnumerable<string> keys) => 
            (await this.GetByKeysCore(keys, false)).Missing;

        private async Task<GetResultCore> GetByKeysCore(IEnumerable<string> keys, bool throwErrorIfMissing)
        {
            var result = new GetResultCore();

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

            result.Config = mirrorConfigs.Where(mc => mc.Keys.Any(mck => keys.Contains(mck)));
            MirrorRepository.ValidateRequiredItems(
                "At least one Mirror configuration was not found",
                keys,
                result.Config,
                (k, i) => i.Keys.Contains(k),
                k => k
            );

            result.QueryResult = await this.networkRepository.GetByQueryAsync(
                new NeuronQuery()
                {
                    ExternalReferenceUrl = result.Config.Select(er => er.Url).Distinct(),
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
                k => string.Join(",", k.Keys),
                throwErrorIfMissing
            );

            return result;
        }

        /// <summary>
        /// Gets Mirror neurons by their Keys.
        /// </summary>
        /// <param name="keys"></param>
        /// <param name="throwErrorIfMissing"></param>
        /// <returns></returns>
        public async Task<IDictionary<string, Neuron>> GetByKeysAsync(
            IEnumerable<string> keys, 
            bool throwErrorIfMissing = true
        )
        {
            var getResult = await this.GetByKeysCore(keys, throwErrorIfMissing);

            return MirrorRepository.GetKeyMirrorDictionary(
                keys,
                getResult.QueryResult.Network.GetItems<Neuron>(),
                this.mirrorConfigs
            );
        }

        internal static IDictionary<string, Neuron> GetKeyMirrorDictionary(
            IEnumerable<string> keys,
            IEnumerable<Neuron> foundMirrors,
            IEnumerable<MirrorConfig> mirrorConfigs
        )
        {
            // return specified keys
            var kvps = keys.Where(
                    // ... that are 
                    kp => mirrorConfigs.Any(
                        // ... contained in the mirror configuration
                        mc => mc.Keys.Contains(kp) &&
                            // and whose configured url matches the url in any found Mirrors
                            foundMirrors.Any(fm => fm.MirrorUrl == mc.Url)
                    )
                )
                // convert the matching keys
                .Select(
                    // ... into key value pairs
                    kp => new KeyValuePair<string, Neuron>(
                        // ... using the key as the key
                        kp,
                        // ... and the foundMirror as the value
                        foundMirrors.Single(
                            // ...where the found mirror
                            fm => mirrorConfigs.Single(
                                // ...matches a mirror configuration
                                mc => mc.Keys.Contains(kp)
                                // ... whose url matches the url of the found mirror
                            ).Url == fm.MirrorUrl
                        )
                    )
                );
            // convert the key value pairs into a dictionary
            return kvps.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        internal static IEnumerable<TKey> ValidateRequiredItems<TKey, TItem>(
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
