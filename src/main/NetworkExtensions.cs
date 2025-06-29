﻿using neurUL.Common.Domain.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace ei8.Cortex.Coding.Persistence
{
    public static class NetworkExtensions
    {
        public static async Task UniquifyAsync(
            this Network network,
            INetworkRepository networkRepository = null,
            INetworkTransactionData transactionData = null,
            IDictionary<string, Network> cache = null
        )
        {
            await NetworkExtensions.UniquifyNeuronsAsync(
                network,
                networkRepository,
                transactionData,
                cache
            );
            await NetworkExtensions.UniquifyTerminalsAsync(
                network,
                networkRepository
            );
        }

        private static async Task UniquifyNeuronsAsync(
            Network network,
            INetworkRepository networkRepository = null,
            INetworkTransactionData transactionData = null,
            IDictionary<string, Network> cache = null
        )
        {
            var currentNeuronIds = network.GetItems<Neuron>()
                .Where(n => NetworkExtensions.IsTransientNeuronEdge(network, n))
                .Select(n => n.Id);
            var nextNeuronIds = new List<Guid>();
            var processedNeuronIds = new List<Guid>();

            transactionData = transactionData ?? new NetworkTransactionData();

            while (currentNeuronIds.Any())
            {
                nextNeuronIds.Clear();
                foreach (var currentNeuronId in currentNeuronIds.ToArray())
                {
                    NetworkExtensions.Log($"Optimizing '{currentNeuronId}'...");
                    if (transactionData.IsReplaced(currentNeuronId))
                    {
                        NetworkExtensions.Log($"> Neuron replaced - skipped.");
                        continue;
                    }

                    AssertionConcern.AssertStateTrue(
                        network.TryGetById(currentNeuronId, out Neuron currentNeuron),
                        $"'currentNeuron' '{currentNeuronId}' must exist in network."
                    );

                    NetworkExtensions.Log($"> Tag: '{currentNeuron.Tag}'");

                    var postsynaptics = network.GetPostsynapticNeurons(currentNeuronId);
                    var transientUnprocessedPostsynaptics = NetworkExtensions.GetTransientUnprocessed(postsynaptics, processedNeuronIds);
                    if (transientUnprocessedPostsynaptics.Any())
                    {
                        NetworkExtensions.Log(
                            $"> Transient unprocessed postsynaptics found - processing deferred: " +
                            $"{string.Join(", ", transientUnprocessedPostsynaptics.Select(n => n.Id))}"
                        );
                        NetworkExtensions.AddIfNotExists(currentNeuronId, nextNeuronIds);
                        continue;
                    }
                    else if (processedNeuronIds.Contains(currentNeuronId))
                    {
                        NetworkExtensions.Log($"> Already processed - skipped.");
                        continue;
                    }

                    var nextPostsynapticId = Guid.Empty;

                    if (currentNeuron.IsTransient)
                    {
                        NetworkExtensions.Log($"> Neuron marked as transient. Retrieving persistent identical granny with postsynaptics " +
                            $"'{string.Join(", ", postsynaptics.Select(n => n.Id))}'.");
                        var identical = await NetworkExtensions.GetPersistentIdentical(
                            postsynaptics.Select(n => n.Id),
                            currentNeuron.Tag,
                            transactionData,
                            networkRepository,
                            cache
                        );

                        if (identical != null)
                        {
                            NetworkExtensions.Log($"> Persistent identical granny found - updating presynaptics and containing network.");
                            NetworkExtensions.ReplaceWithIdentical(network, transactionData, currentNeuronId, identical);
                            NetworkExtensions.Log($"> Neuron removed and replaced with '{identical.Id}'");
                            nextPostsynapticId = identical.Id;
                        }
                        else
                        {
                            NetworkExtensions.Log($"> Persistent identical granny was NOT found.");
                            nextPostsynapticId = currentNeuronId;
                        }
                    }
                    else
                    {
                        NetworkExtensions.Log($"> Neuron NOT marked as transient.");
                        nextPostsynapticId = currentNeuronId;
                    }

                    processedNeuronIds.Add(nextPostsynapticId);
                    var presynaptics = network.GetPresynapticNeurons(nextPostsynapticId);
                    presynaptics.ToList().ForEach(n =>
                    {
                        NetworkExtensions.Log($"> Adding presynaptic '{n.Id}' of '{nextPostsynapticId}' to nextNeuronIds...");
                        NetworkExtensions.AddIfNotExists(n.Id, nextNeuronIds);
                    });
                }
                NetworkExtensions.Log($"Setting next batch of {nextNeuronIds.Count()} ids.");
                currentNeuronIds = nextNeuronIds.ToArray();
            }
        }

        public static void ReplaceWithIdentical(
            Network network, 
            INetworkTransactionData transactionData, 
            Guid currentNeuronId, 
            Neuron identical)
        {
            if (currentNeuronId != identical.Id)
            {
                NetworkExtensions.UpdateDendrites(
                    network,
                    currentNeuronId,
                    identical.Id
                );
                NetworkExtensions.RemoveTerminals(
                    network,
                    currentNeuronId
                );
            }
            network.AddReplace(identical);

            if (currentNeuronId != identical.Id)
                network.Remove(currentNeuronId);

            transactionData.AddReplacedNeuron(currentNeuronId, identical);
        }

        private static void AddIfNotExists(Guid neuronId, List<Guid> nextNeuronIds)
        {
            if (!nextNeuronIds.Contains(neuronId))
            {
                nextNeuronIds.Add(neuronId);
                NetworkExtensions.Log($"> DONE.");
            }
            else
            {
                NetworkExtensions.Log($"> Already added - IGNORED.");
            }
        }

        [Conditional("UNIQLOG")]
        private static void Log(string value)
        {
            Debug.WriteLine(value);
        }

        private static async Task UniquifyTerminalsAsync(
            Network network,
            INetworkRepository networkRepository = null
        )
        {
            var terminalIds = network.GetItems<Terminal>()
                .Where(t => NetworkExtensions.IsTransientTerminalLinkingPersistentNeurons(network, t))
                .Select(t => t.Id);

            foreach(var tId in terminalIds)
            {
                if(
                    network.TryGetById(tId, out Terminal currentTerminal) &&
                    await NetworkExtensions.HasPersistentIdentical(
                        networkRepository,
                        currentTerminal.PresynapticNeuronId,
                        currentTerminal.PostsynapticNeuronId
                    )
                )
                    network.Remove(tId);
            }
        }

        private static async Task<bool> HasPersistentIdentical(
            INetworkRepository networkRepository, 
            Guid presynapticNeuronId, 
            Guid postsynapticNeuronId
        )
        {
            var result = false;

            if (networkRepository != null)
            {
                var queryResult = await networkRepository.GetByQueryAsync(
                        new Library.Common.NeuronQuery()
                        {
                            Id = new[] { presynapticNeuronId.ToString() },
                            Postsynaptic = new[] { postsynapticNeuronId.ToString() },
                            // TODO: how should this be handled
                            NeuronActiveValues = Library.Common.ActiveValues.All,
                            TerminalActiveValues = Library.Common.ActiveValues.All
                        },
                        false
                    );

                result = queryResult.Network.GetItems<Neuron>().Any();
            }

            return result;
        }

        private static bool IsTransientTerminalLinkingPersistentNeurons(Network network, Terminal t)
        {
            return t.IsTransient &&
                network.TryGetById(t.PresynapticNeuronId, out Neuron pre) &&
                network.TryGetById(t.PostsynapticNeuronId, out Neuron post) &&
                !pre.IsTransient &&
                !post.IsTransient;
        }

        private static bool IsTransientNeuronEdge(Network network, Neuron neuron)
        {
            var result = neuron.IsTransient;

            if (result)
            {
                var posts = network.GetPostsynapticNeurons(neuron.Id);
                result &= (!posts.Any() || posts.All(postn => !postn.IsTransient));
            }

            return result;
        }

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

        private static IEnumerable<Neuron> GetTransientUnprocessed(
            IEnumerable<Neuron> posts,
            IEnumerable<Guid> processedNeuronIds
        ) => posts.Where(n => n.IsTransient && !processedNeuronIds.Contains(n.Id));

        private static async Task<Neuron> GetPersistentIdentical(
            IEnumerable<Guid> currentPostsynapticIds,
            string currentTag,
            INetworkTransactionData transactionData,
            INetworkRepository networkRepository = null,
            IDictionary<string, Network> cache = null
        )
        {
            Neuron result = null;

            var similarGrannyFromCacheOrDb = await NetworkExtensions.GetNetwork(
                cache,
                transactionData,
                currentTag,
                currentPostsynapticIds,
                networkRepository
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
            IDictionary<string, Network> cache,
            INetworkTransactionData networkTransactionData,
            string currentTag,
            IEnumerable<Guid> currentPostsynapticIds,
            INetworkRepository networkRepository = null
        )
        {
            string cacheId = currentTag + string.Join(string.Empty, currentPostsynapticIds.OrderBy(g => g));
            if (
                (
                    cache == null || 
                    !cache.TryGetValue(cacheId, out Network result)
                ) &&
                !networkTransactionData.TryGetSavedTransient(currentTag, currentPostsynapticIds, out result) &&
                networkRepository != null
            )
            {
                var tempResult = await networkRepository.GetByQueryAsync(
                    new Library.Common.NeuronQuery()
                    {
                        Tag = !string.IsNullOrEmpty(currentTag) ? new[] { currentTag } : null,
                        Postsynaptic = currentPostsynapticIds.Select(pi => pi.ToString()),
                        DirectionValues = Library.Common.DirectionValues.Outbound,
                        Depth = 1
                    }
                );

                if (tempResult.Network.GetItems().Any())
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
