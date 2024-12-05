using ei8.Cortex.Library.Client.Out;
using ei8.Cortex.Library.Common;
using neurUL.Common.Domain.Model;
using System.Linq;
using System.Threading.Tasks;

namespace ei8.Cortex.Coding.Persistence
{
    public class EnsembleRepository : IEnsembleRepository
    {
        private readonly INeuronQueryClient neuronQueryClient;
        private readonly string cortexLibraryOutBaseUrl;
        private readonly int configQueryResultLimit;
        private readonly string appUserId;

        public EnsembleRepository(
            INeuronQueryClient neuronQueryClient,
            string cortexLibraryOutBaseUrl,
            int configQueryResultLimit,
            string appUserId
        )
        {
            AssertionConcern.AssertArgumentNotNull(neuronQueryClient, nameof(neuronQueryClient));
            AssertionConcern.AssertArgumentNotEmpty(cortexLibraryOutBaseUrl, "Parameter cannot be null or empty.", nameof(cortexLibraryOutBaseUrl));
            AssertionConcern.AssertArgumentRange(configQueryResultLimit, 0, int.MaxValue, nameof(configQueryResultLimit));
            AssertionConcern.AssertArgumentNotEmpty(appUserId, "Parameter cannot be null or empty.", nameof(appUserId));

            this.neuronQueryClient = neuronQueryClient;
            this.cortexLibraryOutBaseUrl = cortexLibraryOutBaseUrl;
            this.configQueryResultLimit = configQueryResultLimit;
            this.appUserId = appUserId;
        }

        public async Task<QueryResult> GetByQueryAsync(NeuronQuery query) =>
            await this.GetByQueryAsync(query, true);

        public async Task<QueryResult> GetByQueryAsync(NeuronQuery query, string userId) =>
            await this.GetByQueryAsync(query, userId, true);

        public async Task<QueryResult> GetByQueryAsync(NeuronQuery query, bool restrictQueryResultCount) =>
            await this.GetByQueryAsync(query, this.appUserId, restrictQueryResultCount);

        public async Task<QueryResult> GetByQueryAsync(NeuronQuery query, string userId, bool restrictQueryResultCount)
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

            return new QueryResult(qr.ToEnsemble(), qr.UserNeuronId);
        }
    }
}