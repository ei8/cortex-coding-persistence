using ei8.Cortex.IdentityAccess.Client.Out;
using ei8.Cortex.Library.Client.Out;
using ei8.Cortex.Library.Common;
using ei8.EventSourcing.Client;
using ei8.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nancy.TinyIoc;
using neurUL.Common.Domain.Model;
using neurUL.Common.Http;
using neurUL.Cortex.Domain.Model.Neurons;
using neurUL.Cortex.Port.Adapter.In.InProcess;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace ei8.Cortex.Coding.Persistence
{
    public class ExternalReferenceRepository : IExternalReferenceRepository
    {
        private struct GetResult
        {
            public QueryResult<Library.Common.Neuron> QueryResult;
            public IEnumerable<ExternalReference> Config;
            public IEnumerable<ExternalReference> Missing;
        }

        private readonly IHttpClientFactory httpClientFactory;
        private readonly INeuronQueryClient neuronQueryClient;
        private readonly string eventSourcingInBaseUrl;
        private readonly string eventSourcingOutBaseUrl;
        private readonly string cortexLibraryOutBaseUrl;
        private readonly string identityAccessOutBaseUrl;
        private readonly IOptions<List<ExternalReference>> externalReferences1;
        private readonly IEnumerable<ExternalReference> externalReferencesConfig;
        private readonly string appUserId;

        public static IExternalReferenceRepository CreateTransient(
            IHttpClientFactory httpClientFactory,
            string eventSourcingInBaseUrl,
            string eventSourcingOutBaseUrl,
            string cortexLibraryOutBaseUrl,
            string identityAccessOutBaseUrl,
            IOptions<List<ExternalReference>> externalReferencesConfig,
            string appUserId
        )
        {
            var rp = new RequestProvider();
            rp.SetHttpClientHandler(new HttpClientHandler());
            return new ExternalReferenceRepository(
                httpClientFactory,
                new HttpNeuronQueryClient(rp),
                eventSourcingInBaseUrl,
                eventSourcingOutBaseUrl,
                cortexLibraryOutBaseUrl,
                identityAccessOutBaseUrl,
                externalReferencesConfig,
                appUserId
            );
        }

        public ExternalReferenceRepository(
            IHttpClientFactory httpClientFactory,
            INeuronQueryClient neuronQueryClient,
            string eventSourcingInBaseUrl, 
            string eventSourcingOutBaseUrl, 
            string cortexLibraryOutBaseUrl,
            string identityAccessOutBaseUrl,
            IOptions<List<ExternalReference>> externalReferencesConfig,
            string appUserId
        )
        {
            AssertionConcern.AssertArgumentNotNull(httpClientFactory, nameof(httpClientFactory));
            AssertionConcern.AssertArgumentNotNull(neuronQueryClient, nameof(neuronQueryClient));
            var nullStringErrorMessage = "Parameter cannot be null or empty.";
            AssertionConcern.AssertArgumentNotEmpty(eventSourcingInBaseUrl, nullStringErrorMessage, nameof(eventSourcingInBaseUrl));
            AssertionConcern.AssertArgumentNotEmpty(eventSourcingOutBaseUrl, nullStringErrorMessage, nameof(eventSourcingOutBaseUrl));
            AssertionConcern.AssertArgumentNotEmpty(cortexLibraryOutBaseUrl, nullStringErrorMessage, nameof(cortexLibraryOutBaseUrl));
            AssertionConcern.AssertArgumentNotEmpty(identityAccessOutBaseUrl, nullStringErrorMessage, nameof(identityAccessOutBaseUrl));
            AssertionConcern.AssertArgumentNotNull(externalReferencesConfig, nameof(externalReferencesConfig));
            AssertionConcern.AssertArgumentNotEmpty(appUserId, nullStringErrorMessage, nameof(appUserId));

            this.httpClientFactory = httpClientFactory;
            this.neuronQueryClient = neuronQueryClient;
            this.eventSourcingInBaseUrl = eventSourcingInBaseUrl;
            this.eventSourcingOutBaseUrl = eventSourcingOutBaseUrl;
            this.cortexLibraryOutBaseUrl = cortexLibraryOutBaseUrl;
            this.identityAccessOutBaseUrl = identityAccessOutBaseUrl;
            this.externalReferencesConfig = externalReferencesConfig.Value.ToArray();
            this.appUserId = appUserId;
        }

        // TODO: specify region to save values
        public async Task Save(System.Collections.Generic.IEnumerable<ExternalReference> values)
        {
            var container = new TinyIoCContainer();
            container.Register(httpClientFactory);
            container.AddTransactions(this.eventSourcingInBaseUrl, this.eventSourcingOutBaseUrl);
            container.AddDataAdapters();
            container.AddRequestProvider();
            container.Register<IValidationClient, HttpValidationClient>();

            // validate
            var vc = container.Resolve<IValidationClient>();
            var validationResult = await vc.CreateNeuron(
                this.identityAccessOutBaseUrl,
                // use any id since we're only trying to retrieve the userNeuronId
                Guid.NewGuid(),
                // TODO: check if values can be saved to specified region
                null,
                appUserId
            );

            var transaction = container.Resolve<ITransaction>();
            await transaction.BeginAsync(validationResult.UserNeuronId);

            // This unusedAuthorId is unused because the Transaction object uses two eventstores.
            // The adapter methods use temporary eventstores whose authorIds,
            // which are set during Transaction.BeginAsync, are never persisted
            var unusedAuthorId = Guid.NewGuid();

            var na = container.Resolve<INeuronAdapter>();
            var ta = container.Resolve<Data.Tag.Port.Adapter.In.InProcess.IItemAdapter>();
            var era = container.Resolve<Data.ExternalReference.Port.Adapter.In.InProcess.IItemAdapter>();
            foreach (var m in values)
            {
                var nid = Guid.NewGuid();
                int expectedVersion = await transaction.InvokeAdapterAsync(
                    nid,
                    typeof(NeuronCreated).Assembly.GetEventTypes(),
                    async (ev) => await na.CreateNeuron(
                        nid,
                        unusedAuthorId
                    )
                );

                expectedVersion = await transaction.InvokeAdapterAsync(
                    nid,
                    typeof(ei8.Data.Tag.Domain.Model.TagChanged).Assembly.GetEventTypes(),
                    async (ev) => await ta.ChangeTag(
                        nid,
                        m.Key,
                        unusedAuthorId,
                        ev
                    ),
                    expectedVersion
                );

                expectedVersion = await transaction.InvokeAdapterAsync(
                    nid,
                    typeof(ei8.Data.ExternalReference.Domain.Model.UrlChanged).Assembly.GetEventTypes(),
                    async (ev) => await era.ChangeUrl(
                        nid,
                        m.Url,
                        unusedAuthorId,
                        ev
                    ),
                    expectedVersion
                );
            }

            await transaction.CommitAsync();
        }

        public async Task<IEnumerable<ExternalReference>> GetAllMissingAsync(IEnumerable<string> keys) => 
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

            result.Config = externalReferencesConfig.Where(er => keys.Contains(er.Key));
            ExternalReferenceRepository.ValidateRequiredItems(
                "At least one External Reference configuration was not found",
                keys,
                result.Config,
                (k, i) => i.Key == k,
                k => k
            );

            result.QueryResult = await neuronQueryClient.GetNeuronsInternal(
                    this.cortexLibraryOutBaseUrl,
                    new NeuronQuery()
                    {
                        ExternalReferenceUrl = result.Config.Select(er => er.Url),
                        SortBy = SortByValue.NeuronCreationTimestamp,
                        SortOrder = SortOrderValue.Descending,
                        PageSize = result.Config.Count()
                    },
                    this.appUserId
                );

            result.Missing = ExternalReferenceRepository.ValidateRequiredItems(
                "At least one local copy of required External References was not found",
                result.Config,
                result.QueryResult.Items,
                (k, i) => i.ExternalReferenceUrl == k.Url,
                k => k.Key,
                throwErrorIfMissing
            );

            return result;
        }

        public async Task<IDictionary<string, Neuron>> GetByKeysAsync(IEnumerable<string> keys)
        {
            var getResult = await this.GetByKeysCore(keys, true);
            var result = new Dictionary<string, Coding.Neuron>();

            foreach (var n in getResult.QueryResult.Items)
            {
                Guid? r = null;
                if (Guid.TryParse(n?.Id, out Guid g))
                    r = g;
                result.Add(
                    getResult.Config.Single(er => er.Url == n.ExternalReferenceUrl).Key,
                    n.ToEnsemble()
                );
            }

            return result;
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
