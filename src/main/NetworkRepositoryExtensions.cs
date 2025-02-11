using neurUL.Common.Domain.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace ei8.Cortex.Coding.Persistence
{
    public static class NetworkRepositoryExtensions
    {
        public static async Task UniquifyAsync(
            this INetworkRepository NetworkRepository, 
            Network Network,
            INetworkTransactionData transactionData = null,
            IDictionary<string, Network> cache = null
        )
        {
            await NetworkRepositoryExtensions.UniquifyNeuronsAsync(
                NetworkRepository, 
                Network, 
                transactionData,
                cache
            );
            await NetworkRepositoryExtensions.UniquifyTerminalsAsync(
                NetworkRepository,
                Network
            );           
        }

        private static async Task UniquifyNeuronsAsync(
            INetworkRepository NetworkRepository, 
            Network Network, 
            INetworkTransactionData transactionData = null,
            IDictionary<string, Network> cache = null
        )
        {
            var currentNeuronIds = Network.GetItems<Neuron>()
                .Where(n => NetworkRepositoryExtensions.IsTransientNeuronWithPersistentPostsynaptics(Network, n))
                .Select(n => n.Id);
            var nextNeuronIds = new List<Guid>();
            var processedNeuronIds = new List<Guid>();

            transactionData = transactionData ?? new NetworkTransactionData();

            while (currentNeuronIds.Any())
            {
                nextNeuronIds.Clear();
                foreach (var currentNeuronId in currentNeuronIds.ToArray())
                {
                    NetworkRepositoryExtensions.Log($"Optimizing '{currentNeuronId}'...");
                    if (transactionData.IsReplaced(currentNeuronId))
                    {
                        NetworkRepositoryExtensions.Log($"> Neuron replaced - skipped.");
                        continue;
                    }

                    AssertionConcern.AssertStateTrue(
                        Network.TryGetById(currentNeuronId, out Neuron currentNeuron),
                        $"'currentNeuron' '{currentNeuronId}' must exist in Network."
                    );

                    NetworkRepositoryExtensions.Log($"Tag: '{currentNeuron.Tag}'");

                    var postsynaptics = Network.GetPostsynapticNeurons(currentNeuronId);
                    if (NetworkRepositoryExtensions.ContainsTransientUnprocessed(postsynaptics, processedNeuronIds))
                    {
                        NetworkRepositoryExtensions.Log($"> Transient unprocessed postsynaptic found - processing deferred.");
                        nextNeuronIds.Add(currentNeuronId);
                        continue;
                    }
                    else if (processedNeuronIds.Contains(currentNeuronId))
                    {
                        NetworkRepositoryExtensions.Log($"> Already processed - skipped.");
                        continue;
                    }

                    var nextPostsynapticId = Guid.Empty;

                    if (currentNeuron.IsTransient)
                    {
                        NetworkRepositoryExtensions.Log($"> Neuron marked as transient. Retrieving persistent identical granny with postsynaptics " +
                            $"'{string.Join(", ", postsynaptics.Select(n => n.Id))}'.");
                        var identical = await NetworkRepositoryExtensions.GetPersistentIdentical(
                            NetworkRepository,
                            postsynaptics.Select(n => n.Id),
                            currentNeuron.Tag,
                            transactionData,
                            cache
                        );

                        if (identical != null)
                        {
                            NetworkRepositoryExtensions.Log($"> Persistent identical granny found - updating presynaptics and containing Network.");
                            NetworkRepositoryExtensions.UpdateDendrites(
                                Network,
                                currentNeuronId,
                                identical.Id
                            );
                            NetworkRepositoryExtensions.RemoveTerminals(
                                Network,
                                currentNeuronId
                            );
                            Network.AddReplace(identical);
                            Network.Remove(currentNeuronId);
                            transactionData.AddReplacedNeuron(currentNeuronId, identical);
                            NetworkRepositoryExtensions.Log($"> Neuron replaced and removed.");
                            nextPostsynapticId = identical.Id;
                        }
                        else
                        {
                            NetworkRepositoryExtensions.Log($"> Persistent identical granny was NOT found.");
                            nextPostsynapticId = currentNeuronId;
                        }
                    }
                    else
                    {
                        NetworkRepositoryExtensions.Log($"> Neuron NOT marked as transient.");
                        nextPostsynapticId = currentNeuronId;
                    }

                    processedNeuronIds.Add(nextPostsynapticId);
                    var presynaptics = Network.GetPresynapticNeurons(nextPostsynapticId);
                    presynaptics.ToList().ForEach(n =>
                    {
                        NetworkRepositoryExtensions.Log($"> Adding presynaptic '{n.Id}' to nextNeuronIds.");
                        nextNeuronIds.Add(n.Id);
                    });
                }
                NetworkRepositoryExtensions.Log($"Setting next batch of {nextNeuronIds.Count()} ids.");
                currentNeuronIds = nextNeuronIds.ToArray();
            }
        }

        [Conditional("UNIQLOG")]
        private static void Log(string value)
        {
            Debug.WriteLine(value);
        }

        private static async Task UniquifyTerminalsAsync(
            INetworkRepository NetworkRepository,
            Network Network
        )
        {
            var terminalIds = Network.GetItems<Terminal>()
                .Where(t => NetworkRepositoryExtensions.IsTransientTerminalLinkingPersistentNeurons(Network, t))
                .Select(t => t.Id);

            foreach(var tId in terminalIds)
            {
                if(
                    Network.TryGetById(tId, out Terminal currentTerminal) &&
                    await NetworkRepositoryExtensions.HasPersistentIdentical(
                        NetworkRepository,
                        currentTerminal.PresynapticNeuronId,
                        currentTerminal.PostsynapticNeuronId
                    )
                )
                Network.Remove(tId);
            }
        }

        private static async Task<bool> HasPersistentIdentical(
            INetworkRepository NetworkRepository, 
            Guid presynapticNeuronId, 
            Guid postsynapticNeuronId
        )
        {
            var queryResult = await NetworkRepository.GetByQueryAsync(
                    new Library.Common.NeuronQuery()
                    {
                        Id = new string[] { presynapticNeuronId.ToString() },
                        Postsynaptic = new string[] { postsynapticNeuronId.ToString() },
                        // TODO: how should this be handled
                        NeuronActiveValues = Library.Common.ActiveValues.All,
                        TerminalActiveValues = Library.Common.ActiveValues.All
                    },
                    false
                );

            return queryResult.Network.GetItems<Neuron>().Any();
        }

        private static bool IsTransientTerminalLinkingPersistentNeurons(Network Network, Terminal t)
        {
            return t.IsTransient &&
                Network.TryGetById(t.PresynapticNeuronId, out Neuron pre) &&
                Network.TryGetById(t.PostsynapticNeuronId, out Neuron post) &&
                !pre.IsTransient &&
                !post.IsTransient;
        }

        private static bool IsTransientNeuronWithPersistentPostsynaptics(Network Network, Neuron neuron) =>
            neuron.IsTransient && Network.GetPostsynapticNeurons(neuron.Id).All(postn => !postn.IsTransient);

        private static void UpdateDendrites(Network result, Guid oldPostsynapticId, Guid newPostsynapticId)
        {
            var currentDendrites = result.GetDendrites(oldPostsynapticId).ToArray();
            foreach (var currentDendrite in currentDendrites)
            {
                result.AddReplace(
                    new Terminal(
                        currentDendrite.Id,
                        currentDendrite.IsTransient,
                        currentDendrite.PresynapticNeuronId,
                        newPostsynapticId,
                        currentDendrite.Effect,
                        currentDendrite.Strength
                    )
                );
            }
        }

        private static void RemoveTerminals(Network result, Guid presynapticId)
        {
            var terminals = result.GetTerminals(presynapticId).ToArray();
            foreach (var terminal in terminals)
                result.Remove(terminal.Id);
        }

        private static bool ContainsTransientUnprocessed(
            IEnumerable<Neuron> posts,
            IEnumerable<Guid> processedNeuronIds
        ) => posts.Any(n => n.IsTransient && !processedNeuronIds.Contains(n.Id));

        private static async Task<Neuron> GetPersistentIdentical(
            INetworkRepository NetworkRepository,
            IEnumerable<Guid> currentPostsynapticIds,
            string currentTag,
            INetworkTransactionData transactionData,
            IDictionary<string, Network> cache = null
        )
        {
            Neuron result = null;

            var similarGrannyFromCacheOrDb = await NetworkRepositoryExtensions.GetNetwork(
                NetworkRepository,
                cache,
                transactionData,
                currentTag,
                currentPostsynapticIds
            );

            if (similarGrannyFromCacheOrDb != null)
            {
                var similarGrannyFromDbNeuron =
                    similarGrannyFromCacheOrDb.GetItems<Neuron>()
                    .Where(n => !currentPostsynapticIds.Any(pn => pn == n.Id));

                AssertionConcern.AssertStateTrue(
                    similarGrannyFromDbNeuron.Count() < 2,
                        $"Redundant Neurons with postsynaptic Neurons '{string.Join(", ", currentPostsynapticIds)}' encountered: { string.Join(", ", similarGrannyFromDbNeuron.Select(n => n.Id.ToString()))}"
                    );
                if (similarGrannyFromDbNeuron.Any())
                {
                    var resultTerminalCount = similarGrannyFromCacheOrDb.GetTerminals(similarGrannyFromDbNeuron.Single().Id).Count();
                    AssertionConcern.AssertStateTrue(
                        resultTerminalCount == currentPostsynapticIds.Count(),
                        $"A correct identical match should have '{currentPostsynapticIds.Count()} terminals. Result has {resultTerminalCount}'."
                    );
                }

                result = similarGrannyFromDbNeuron.SingleOrDefault();
            }

            return result;
        }

        private static async Task<Network> GetNetwork(
            INetworkRepository NetworkRepository, 
            IDictionary<string, Network> cache,
            INetworkTransactionData NetworkTransactionData,
            string currentTag,
            IEnumerable<Guid> currentPostsynapticIds
        )
        {
            string cacheId = currentTag + string.Join(string.Empty, currentPostsynapticIds.OrderBy(g => g));
            if (
                (
                    cache == null || 
                    !cache.TryGetValue(cacheId, out Network result)
                ) &&
                !NetworkTransactionData.TryGetSavedTransient(currentTag, currentPostsynapticIds, out result)
                )
            {
                var tempResult = await NetworkRepository.GetByQueryAsync(
                    new Library.Common.NeuronQuery()
                    {
                        Tag = !string.IsNullOrEmpty(currentTag) ? new string[] { currentTag } : null,
                        Postsynaptic = currentPostsynapticIds.Select(pi => pi.ToString()),
                        DirectionValues = Library.Common.DirectionValues.Outbound,
                        Depth = 1
                    }
                );

                if (tempResult.Network.GetItems().Count() > 0)
                {
                    result = tempResult.Network;

                    if (cache != null)
                        cache.Add(cacheId, result);
                }
                else
                    result = null;
            }

            return result;
        }
    }
}
