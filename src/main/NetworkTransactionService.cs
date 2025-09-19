using ei8.EventSourcing.Client;
using neurUL.Cortex.Domain.Model.Neurons;
using neurUL.Cortex.Port.Adapter.In.InProcess;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace ei8.Cortex.Coding.Persistence
{
    public class NetworkTransactionService : INetworkTransactionService
    {
        private readonly INeuronAdapter neuronAdapter;
        private readonly ITerminalAdapter terminalAdapter;
        private readonly Data.Tag.Port.Adapter.In.InProcess.IItemAdapter tagItemAdapter;
        private readonly Data.Aggregate.Port.Adapter.In.InProcess.IItemAdapter aggregateItemAdapter;
        private readonly Data.Mirror.Port.Adapter.In.InProcess.IItemAdapter mirrorItemAdapter;
        private readonly INetworkTransactionData transactionData;
        private readonly INetworkDictionary<CacheKey> readWriteCache;

        public NetworkTransactionService(
            neurUL.Cortex.Port.Adapter.In.InProcess.INeuronAdapter neuronAdapter,
            neurUL.Cortex.Port.Adapter.In.InProcess.ITerminalAdapter terminalAdapter,
            ei8.Data.Tag.Port.Adapter.In.InProcess.IItemAdapter tagItemAdapter,
            ei8.Data.Aggregate.Port.Adapter.In.InProcess.IItemAdapter aggregateItemAdapter,
            ei8.Data.Mirror.Port.Adapter.In.InProcess.IItemAdapter mirrorItemAdapter,
            INetworkTransactionData transactionData,
            INetworkDictionary<CacheKey> readWriteCache
        )
        {
            this.neuronAdapter = neuronAdapter;
            this.terminalAdapter = terminalAdapter;
            this.tagItemAdapter = tagItemAdapter;
            this.aggregateItemAdapter = aggregateItemAdapter;
            this.mirrorItemAdapter = mirrorItemAdapter;
            this.transactionData = transactionData;
            this.readWriteCache = readWriteCache;
        }

        public async Task SaveAsync(
           ITransaction transaction,
           Network network
        )
        {
            var transientItems = network.GetItems().Where(ei => ei.IsTransient);
            NetworkTransactionService.LogTransientItems(transientItems);
            foreach (var ei in transientItems)
            {
                await NetworkTransactionService.SaveItemAsync(
                    transaction,
                    ei,
                    this.neuronAdapter,
                    this.terminalAdapter,
                    this.tagItemAdapter,
                    this.aggregateItemAdapter,
                    this.mirrorItemAdapter,
                    this.readWriteCache[CacheKey.Write]
                );
                
                this.transactionData.AddSavedTransient(ei);
            }
        }

        [Conditional("TRANLOG")]
        private static void LogTransientItems(IEnumerable<INetworkItem> transientItems)
        {
            foreach (var ti in transientItems)
                Debug.WriteLine($"Saving transient {ti.GetType().Name} '{ti.Id}'");
        }

        private static async Task SaveItemAsync(
           ITransaction transaction,
           INetworkItem item,
           neurUL.Cortex.Port.Adapter.In.InProcess.INeuronAdapter neuronAdapter,
           neurUL.Cortex.Port.Adapter.In.InProcess.ITerminalAdapter terminalAdapter,
           ei8.Data.Tag.Port.Adapter.In.InProcess.IItemAdapter tagItemAdapter,
           ei8.Data.Aggregate.Port.Adapter.In.InProcess.IItemAdapter aggregateItemAdapter,
           ei8.Data.Mirror.Port.Adapter.In.InProcess.IItemAdapter mirrorItemAdapter,
           Network writeCache
        )
        {
            // This unusedAuthorId is unused because the Transaction object uses two eventstores.
            // The adapter methods use temporary eventstores whose authorIds,
            // which are set during Transaction.BeginAsync, are never persisted
            var unusedAuthorId = Guid.NewGuid();
            if (item is Coding.Terminal terminal)
            {
                await transaction.InvokeAdapterAsync(
                    terminal.Id,
                    typeof(TerminalCreated).Assembly.GetEventTypes(),
                    async (ev) => await terminalAdapter.CreateTerminal(
                        terminal.Id,
                        terminal.PresynapticNeuronId,
                        terminal.PostsynapticNeuronId,
                        (neurUL.Cortex.Common.NeurotransmitterEffect)Enum.Parse(typeof(neurUL.Cortex.Common.NeurotransmitterEffect), terminal.Effect.ToString()),
                        terminal.Strength,
                        unusedAuthorId
                    )
                );
            }
            else if (item is Coding.Neuron neuron)
            {
                #region Create instance neuron
                int expectedVersion = await transaction.InvokeAdapterAsync(
                        neuron.Id,
                        typeof(NeuronCreated).Assembly.GetEventTypes(),
                        async (ev) => await neuronAdapter.CreateNeuron(
                            neuron.Id,
                            unusedAuthorId)
                        );

                // assign tag value
                if (!string.IsNullOrWhiteSpace(neuron.Tag))
                {
                    expectedVersion = await transaction.InvokeAdapterAsync(
                        neuron.Id,
                        typeof(ei8.Data.Tag.Domain.Model.TagChanged).Assembly.GetEventTypes(),
                        async (ev) => await tagItemAdapter.ChangeTag(
                            neuron.Id,
                            neuron.Tag,
                            unusedAuthorId,
                            ev
                        ),
                        expectedVersion
                        );
                }

                bool hasCacheNeuronValue = false;
                if (
                    (
                        hasCacheNeuronValue = NetworkTransactionService.TryGetCacheNeuronHasValue(
                            writeCache, 
                            neuron.Id, 
                            n => n.RegionId.HasValue, 
                            n => n.RegionId.Value,
                            out Guid regionId
                        ) 
                    ) ||
                    neuron.RegionId.HasValue
                )
                {
                    // assign region value to id
                    expectedVersion = await transaction.InvokeAdapterAsync(
                        neuron.Id,
                        typeof(ei8.Data.Aggregate.Domain.Model.AggregateChanged).Assembly.GetEventTypes(),
                        async (ev) => await aggregateItemAdapter.ChangeAggregate(
                            neuron.Id,
                            hasCacheNeuronValue ?
                                regionId.ToString() : 
                                neuron.RegionId.ToString(),
                            unusedAuthorId,
                            ev
                        ),
                        expectedVersion
                    );
                }

                if (
                    (
                        hasCacheNeuronValue = NetworkTransactionService.TryGetCacheNeuronHasValue(
                            writeCache, 
                            neuron.Id, 
                            n => !string.IsNullOrWhiteSpace(n.MirrorUrl), 
                            n => n.MirrorUrl,
                            out string mirrorUrl
                        )
                    ) || 
                    !string.IsNullOrWhiteSpace(neuron.MirrorUrl)
                )
                {
                    expectedVersion = await transaction.InvokeAdapterAsync(
                        neuron.Id,
                        typeof(ei8.Data.Mirror.Domain.Model.UrlChanged).Assembly.GetEventTypes(),
                        async (ev) => await mirrorItemAdapter.ChangeUrl(
                            neuron.Id,
                            hasCacheNeuronValue ?
                                mirrorUrl :
                                neuron.MirrorUrl,
                            unusedAuthorId,
                            ev
                        ),
                        expectedVersion
                        );
                }
                #endregion
            }
        }

        private static bool TryGetCacheNeuronHasValue<T>(
            Network network,
            Guid id,
            Predicate<Neuron> hasValueChecker,
            Func<Neuron, T> valueRetriever,
            out T result
        )
        {
            bool bResult = network.TryGetById(id, out Neuron neuron) && hasValueChecker(neuron);

            if (bResult)
                result = valueRetriever(neuron);
            else
                result = default;

            return bResult;
        }
    }
}
