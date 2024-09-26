using ei8.EventSourcing.Client;
using neurUL.Cortex.Domain.Model.Neurons;
using neurUL.Cortex.Port.Adapter.In.InProcess;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ei8.Cortex.Coding.Persistence
{
    public class EnsembleTransactionService : IEnsembleTransactionService
    {
        private readonly INeuronAdapter neuronAdapter;
        private readonly ITerminalAdapter terminalAdapter;
        private readonly Data.Tag.Port.Adapter.In.InProcess.IItemAdapter tagItemAdapter;
        private readonly Data.Aggregate.Port.Adapter.In.InProcess.IItemAdapter aggregateItemAdapter;
        private readonly Data.ExternalReference.Port.Adapter.In.InProcess.IItemAdapter externalReferenceItemAdapter;

        public EnsembleTransactionService(
            neurUL.Cortex.Port.Adapter.In.InProcess.INeuronAdapter neuronAdapter,
            neurUL.Cortex.Port.Adapter.In.InProcess.ITerminalAdapter terminalAdapter,
            ei8.Data.Tag.Port.Adapter.In.InProcess.IItemAdapter tagItemAdapter,
            ei8.Data.Aggregate.Port.Adapter.In.InProcess.IItemAdapter aggregateItemAdapter,
            ei8.Data.ExternalReference.Port.Adapter.In.InProcess.IItemAdapter externalReferenceItemAdapter
        )
        {
            this.neuronAdapter = neuronAdapter;
            this.terminalAdapter = terminalAdapter;
            this.tagItemAdapter = tagItemAdapter;
            this.aggregateItemAdapter = aggregateItemAdapter;
            this.externalReferenceItemAdapter = externalReferenceItemAdapter;
        }

        public async Task SaveAsync(
           ITransaction transaction,
           Ensemble ensemble,
           Guid authorId
        )
        {
            var transientItems = ensemble.GetItems().Where(ei => ei.IsTransient);
            foreach (var ei in transientItems)
                await EnsembleTransactionService.SaveItemAsync(
                    transaction,
                    ei,
                    authorId,
                    this.neuronAdapter,
                    this.terminalAdapter,
                    this.tagItemAdapter,
                    this.aggregateItemAdapter,
                    this.externalReferenceItemAdapter
                );
        }

        private static async Task SaveItemAsync(
           ITransaction transaction,
           IEnsembleItem item,
           Guid authorId,
           neurUL.Cortex.Port.Adapter.In.InProcess.INeuronAdapter neuronAdapter,
           neurUL.Cortex.Port.Adapter.In.InProcess.ITerminalAdapter terminalAdapter,
           ei8.Data.Tag.Port.Adapter.In.InProcess.IItemAdapter tagItemAdapter,
           ei8.Data.Aggregate.Port.Adapter.In.InProcess.IItemAdapter aggregateItemAdapter,
           ei8.Data.ExternalReference.Port.Adapter.In.InProcess.IItemAdapter externalReferenceItemAdapter
           )
        {
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
                        authorId
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
                            authorId)
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
                            authorId,
                            ev
                        ),
                        expectedVersion
                        );
                }

                if (neuron.RegionId.HasValue)
                {
                    // assign region value to id
                    expectedVersion = await transaction.InvokeAdapterAsync(
                        neuron.Id,
                        typeof(ei8.Data.Aggregate.Domain.Model.AggregateChanged).Assembly.GetEventTypes(),
                        async (ev) => await aggregateItemAdapter.ChangeAggregate(
                            neuron.Id,
                            neuron.RegionId.ToString(),
                            authorId,
                            ev
                        ),
                        expectedVersion
                    );
                }

                if (!string.IsNullOrWhiteSpace(neuron.ExternalReferenceUrl))
                {
                    expectedVersion = await transaction.InvokeAdapterAsync(
                        neuron.Id,
                        typeof(ei8.Data.ExternalReference.Domain.Model.UrlChanged).Assembly.GetEventTypes(),
                        async (ev) => await externalReferenceItemAdapter.ChangeUrl(
                            neuron.Id,
                            neuron.ExternalReferenceUrl,
                            authorId,
                            ev
                        ),
                        expectedVersion
                        );
                }
                #endregion
            }
        }
    }
}
