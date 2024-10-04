using ei8.Cortex.Library.Client.Out;
using ei8.Cortex.Library.Common;
using Microsoft.Extensions.Options;
using neurUL.Common.Domain.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ei8.Cortex.Coding.Persistence
{
    public class EnsembleRepository : IEnsembleRepository
    {
        private readonly INeuronQueryClient neuronQueryClient;
        private readonly IEnumerable<ExternalReference> externalReferences; 
        private readonly string cortexLibraryOutBaseUrl;
        private readonly int configQueryResultLimit;
        private readonly string appUserId;
        
        public EnsembleRepository(
            INeuronQueryClient neuronQueryClient, 
            IOptions<List<ExternalReference>> externalReferences,
            string cortexLibraryOutBaseUrl,
            int configQueryResultLimit,
            string appUserId
        )
        {
            AssertionConcern.AssertArgumentNotNull(neuronQueryClient, nameof(neuronQueryClient));
            AssertionConcern.AssertArgumentNotNull(externalReferences, nameof(externalReferences));
            AssertionConcern.AssertArgumentNotEmpty(cortexLibraryOutBaseUrl, "Parameter cannot be null or empty.", nameof(cortexLibraryOutBaseUrl));
            AssertionConcern.AssertArgumentRange(configQueryResultLimit, 0, int.MaxValue, nameof(configQueryResultLimit));
            AssertionConcern.AssertArgumentNotEmpty(appUserId, "Parameter cannot be null or empty.", nameof(appUserId));

            this.neuronQueryClient = neuronQueryClient;
            this.externalReferences = externalReferences.Value.ToArray();
            this.cortexLibraryOutBaseUrl = cortexLibraryOutBaseUrl;
            this.configQueryResultLimit = configQueryResultLimit;
            this.appUserId = appUserId;
        }

        public async Task<Ensemble> GetByQueryAsync(NeuronQuery query) =>
            await this.GetByQueryAsync(query, true);

        public async Task<Ensemble> GetByQueryAsync(NeuronQuery query, string userId) =>
            await this.GetByQueryAsync(query, userId, true);

        public async Task<Ensemble> GetByQueryAsync(NeuronQuery query, bool restrictQueryResultCount) =>
            await this.GetByQueryAsync(query, this.appUserId, restrictQueryResultCount);

        public async Task<Ensemble> GetByQueryAsync(NeuronQuery query, string userId, bool restrictQueryResultCount)
        {
            AssertionConcern.AssertArgumentNotNull(query, nameof(query));

            userId = userId ?? this.appUserId;

            var qr = await neuronQueryClient.GetNeuronsInternal(
                cortexLibraryOutBaseUrl,
                query,
                userId
            );

            // TODO: test if this works as expected
            AssertionConcern.AssertStateFalse(
                qr.Items.Any(i => i.Validation.RestrictionReasons.Any()),
                $"At least one query result is inaccessible to the specified userId '{userId}': " +
                $"'{string.Join("', '", qr.Items.SelectMany(i => i.Validation.RestrictionReasons))}'."
            );

            var queryResultLimit = restrictQueryResultCount ?
                this.configQueryResultLimit : int.MaxValue;

            AssertionConcern.AssertStateTrue(
                qr.Count <= queryResultLimit, 
                $"Query results cannot exceed {queryResultLimit} items. Query: {query.ToString()}"
            );

            return qr.ToEnsemble();
        }

        public async Task<IDictionary<string, Coding.Neuron>> GetExternalReferencesAsync(IEnumerable<string> keys) =>
            await this.GetExternalReferencesAsync(keys, this.appUserId);

        public async Task<IDictionary<string, Coding.Neuron>> GetExternalReferencesAsync(IEnumerable<string> keys, string userId)
        {
            AssertionConcern.AssertArgumentNotNull(keys, nameof(keys));
            AssertionConcern.AssertArgumentValid(k => k.Count() > 0, keys, "Specified 'keys' cannot be an empty array.", nameof(keys));

            userId = userId ?? this.appUserId;

            var exRefs = externalReferences.Where(er => keys.Contains(er.Key));
            var qr = await neuronQueryClient.GetNeuronsInternal(
                    this.cortexLibraryOutBaseUrl,
                    new NeuronQuery()
                    {
                        ExternalReferenceUrl = exRefs.Select(er => er.Url),
                        SortBy = SortByValue.NeuronCreationTimestamp,
                        SortOrder = SortOrderValue.Descending,
                        PageSize = exRefs.Count()
                    },
                    userId
                );
            AssertionConcern.AssertStateTrue(keys.Count() == qr.Count, "At least one local copy of a specified External Reference was not found.");
            var result = new Dictionary<string, Coding.Neuron>();

            foreach (var n in qr.Items)
            {
                Guid? r = null;
                if (Guid.TryParse(n?.Id, out Guid g))
                    r = g;
                result.Add(
                    exRefs.Single(er => er.Url == n.ExternalReferenceUrl).Key,
                    n.ToEnsemble()
                );
            }

            return result;
        }
    }
}