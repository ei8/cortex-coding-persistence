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
        private IEnumerable<ExternalReference> externalReferences;

        public EnsembleRepository(INeuronQueryClient neuronQueryClient, IOptions<List<ExternalReference>> externalReferences)
        {
            AssertionConcern.AssertArgumentNotNull(neuronQueryClient, nameof(neuronQueryClient));
            AssertionConcern.AssertArgumentNotNull(externalReferences, nameof(externalReferences));

            this.neuronQueryClient = neuronQueryClient;
            this.externalReferences = externalReferences.Value.ToArray();
        }

        public async Task<Ensemble> GetByQueryAsync(string userId, NeuronQuery query, string cortexLibraryOutBaseUrl, int queryResultLimit)
        {
            AssertionConcern.AssertArgumentNotEmpty(userId, "Specified 'userId' cannot be null or empty.", nameof(userId));
            AssertionConcern.AssertArgumentNotNull(query, nameof(query));

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

            AssertionConcern.AssertStateTrue(
                qr.Count < queryResultLimit, 
                $"Query results cannot exceed {queryResultLimit} items. Query: {query.ToString()}"
            );

            return qr.ToEnsemble();
        }

        public async Task<IDictionary<string, Coding.Neuron>> GetExternalReferencesAsync(string userId, string cortexLibraryOutBaseUrl, params string[] keys)
        {
            AssertionConcern.AssertArgumentNotEmpty(userId, "Specified 'userId' cannot be null or empty.", nameof(userId));
            AssertionConcern.AssertArgumentNotNull(keys, nameof(keys));
            AssertionConcern.AssertArgumentValid(k => k.Length > 0, keys, "Specified array cannot be an empty array.", nameof(keys));

            var exRefs = externalReferences.Where(er => keys.Contains(er.Key));
            var qr = await neuronQueryClient.GetNeuronsInternal(
                    cortexLibraryOutBaseUrl + "/",
                    new NeuronQuery()
                    {
                        ExternalReferenceUrl = exRefs.Select(er => er.Url),
                        SortBy = SortByValue.NeuronCreationTimestamp,
                        SortOrder = SortOrderValue.Descending,
                        PageSize = exRefs.Count()
                    },
                    userId
                );
            AssertionConcern.AssertStateTrue(keys.Length == qr.Count, "At least one local copy of a specified External Reference was not found.");
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