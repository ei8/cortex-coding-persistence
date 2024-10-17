using neurUL.Common.Domain.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace ei8.Cortex.Coding.Persistence
{
    public static class EnsembleRepositoryExtensions
    {
        public static async Task<Neuron> GetExternalReferenceAsync(
            this IEnsembleRepository ensembleRepository,
            object key
        ) =>
            (await ensembleRepository.GetExternalReferencesAsync(new[] { key })).Values.SingleOrDefault();

        public static async Task<IDictionary<object, Neuron>> GetExternalReferencesAsync(
            this IEnsembleRepository ensembleRepository,
            IEnumerable<object> keys       
            )
        {
            var keyConverter = new Func<object, string>(o =>
            {
                var result = o as string;
                if (o is MemberInfo)
                    result = ExternalReference.ToKeyString((MemberInfo)o);
                else if (o is Enum)
                    result = ExternalReference.ToKeyString((Enum)o);

                return result;
            });
            var origDict = await ensembleRepository.GetExternalReferencesAsync(
                keys.Select(t => keyConverter(t)).ToArray()
            );
            return origDict.ToDictionary(kvpK => keys.Single(t => keyConverter(t) == kvpK.Key), kvpE => kvpE.Value);
        }

        public static async Task UniquifyAsync(
            this IEnsembleRepository ensembleRepository, 
            Ensemble ensemble,
            IEnsembleTransactionData transactionData = null,
            IDictionary<string, Ensemble> cache = null
        )
        {
            await EnsembleRepositoryExtensions.UniquifyNeuronsAsync(
                ensembleRepository, 
                ensemble, 
                transactionData,
                cache
            );
            await EnsembleRepositoryExtensions.UniquifyTerminalsAsync(
                ensembleRepository,
                ensemble
            );           
        }

        private static async Task UniquifyNeuronsAsync(
            IEnsembleRepository ensembleRepository, 
            Ensemble ensemble, 
            IEnsembleTransactionData transactionData = null,
            IDictionary<string, Ensemble> cache = null
        )
        {
            var currentNeuronIds = ensemble.GetItems<Neuron>()
                .Where(n => EnsembleRepositoryExtensions.IsTransientNeuronWithPersistentPostsynaptics(ensemble, n))
                .Select(n => n.Id);
            var nextNeuronIds = new List<Guid>();
            var processedNeuronIds = new List<Guid>();

            transactionData = transactionData ?? new EnsembleTransactionData();

            while (currentNeuronIds.Any())
            {
                nextNeuronIds.Clear();
                foreach (var currentNeuronId in currentNeuronIds.ToArray())
                {
                    Debug.WriteLine($"Optimizing '{currentNeuronId}'...");
                    if (transactionData.IsReplaced(currentNeuronId))
                    {
                        Debug.WriteLine($"> Neuron replaced - skipped.");
                        continue;
                    }

                    AssertionConcern.AssertStateTrue(
                        ensemble.TryGetById(currentNeuronId, out Neuron currentNeuron),
                        $"'currentNeuron' '{currentNeuronId}' must exist in ensemble."
                    );

                    Debug.WriteLine($"Tag: '{currentNeuron.Tag}'");

                    var postsynaptics = ensemble.GetPostsynapticNeurons(currentNeuronId);
                    if (EnsembleRepositoryExtensions.ContainsTransientUnprocessed(postsynaptics, processedNeuronIds))
                    {
                        Debug.WriteLine($"> Transient unprocessed postsynaptic found - processing deferred.");
                        nextNeuronIds.Add(currentNeuronId);
                        continue;
                    }
                    else if (processedNeuronIds.Contains(currentNeuronId))
                    {
                        Debug.WriteLine($"> Already processed - skipped.");
                        continue;
                    }

                    var nextPostsynapticId = Guid.Empty;

                    if (currentNeuron.IsTransient)
                    {
                        Debug.WriteLine($"> Neuron marked as transient. Retrieving persistent identical granny with postsynaptics " +
                            $"'{string.Join(", ", postsynaptics.Select(n => n.Id))}'.");
                        var identical = await EnsembleRepositoryExtensions.GetPersistentIdentical(
                            ensembleRepository,
                            postsynaptics.Select(n => n.Id),
                            currentNeuron.Tag,
                            transactionData,
                            cache
                        );

                        if (identical != null)
                        {
                            Debug.WriteLine($"> Persistent identical granny found - updating presynaptics and containing ensemble.");
                            EnsembleRepositoryExtensions.UpdateDendrites(
                                ensemble,
                                currentNeuronId,
                                identical.Id
                            );
                            EnsembleRepositoryExtensions.RemoveTerminals(
                                ensemble,
                                currentNeuronId
                            );
                            ensemble.AddReplace(identical);
                            ensemble.Remove(currentNeuronId);
                            transactionData.AddReplacedNeuron(currentNeuronId, identical);
                            Debug.WriteLine($"> Neuron replaced and removed.");
                            nextPostsynapticId = identical.Id;
                        }
                        else
                        {
                            Debug.WriteLine($"> Persistent identical granny was NOT found.");
                            nextPostsynapticId = currentNeuronId;
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"> Neuron NOT marked as transient.");
                        nextPostsynapticId = currentNeuronId;
                    }

                    processedNeuronIds.Add(nextPostsynapticId);
                    var presynaptics = ensemble.GetPresynapticNeurons(nextPostsynapticId);
                    presynaptics.ToList().ForEach(n =>
                    {
                        Debug.WriteLine($"> Adding presynaptic '{n.Id}' to nextNeuronIds.");
                        nextNeuronIds.Add(n.Id);
                    });
                }
                Debug.WriteLine($"Setting next batch of {nextNeuronIds.Count()} ids.");
                currentNeuronIds = nextNeuronIds.ToArray();
            }
        }

        private static async Task UniquifyTerminalsAsync(
            IEnsembleRepository ensembleRepository,
            Ensemble ensemble
        )
        {
            var terminalIds = ensemble.GetItems<Terminal>()
                .Where(t => EnsembleRepositoryExtensions.IsTransientTerminalLinkingPersistentNeurons(ensemble, t))
                .Select(t => t.Id);

            foreach(var tId in terminalIds)
            {
                if(
                    ensemble.TryGetById(tId, out Terminal currentTerminal) &&
                    await EnsembleRepositoryExtensions.HasPersistentIdentical(
                        ensembleRepository,
                        currentTerminal.PresynapticNeuronId,
                        currentTerminal.PostsynapticNeuronId
                    )
                )
                ensemble.Remove(tId);
            }
        }

        private static async Task<bool> HasPersistentIdentical(
            IEnsembleRepository ensembleRepository, 
            Guid presynapticNeuronId, 
            Guid postsynapticNeuronId
        )
        {
            var queryResult = await ensembleRepository.GetByQueryAsync(
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

            return queryResult.Ensemble.GetItems<Neuron>().Any();
        }

        private static bool IsTransientTerminalLinkingPersistentNeurons(Ensemble ensemble, Terminal t)
        {
            return t.IsTransient &&
                ensemble.TryGetById(t.PresynapticNeuronId, out Neuron pre) &&
                ensemble.TryGetById(t.PostsynapticNeuronId, out Neuron post) &&
                !pre.IsTransient &&
                !post.IsTransient;
        }

        private static bool IsTransientNeuronWithPersistentPostsynaptics(Ensemble ensemble, Neuron neuron) =>
            neuron.IsTransient && ensemble.GetPostsynapticNeurons(neuron.Id).All(postn => !postn.IsTransient);

        private static void UpdateDendrites(Ensemble result, Guid oldPostsynapticId, Guid newPostsynapticId)
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

        private static void RemoveTerminals(Ensemble result, Guid presynapticId)
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
            IEnsembleRepository ensembleRepository,
            IEnumerable<Guid> currentPostsynapticIds,
            string currentTag,
            IEnsembleTransactionData transactionData,
            IDictionary<string, Ensemble> cache = null
        )
        {
            Neuron result = null;

            var similarGrannyFromCacheOrDb = await EnsembleRepositoryExtensions.GetEnsemble(
                ensembleRepository,
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

        private static async Task<Ensemble> GetEnsemble(
            IEnsembleRepository ensembleRepository, 
            IDictionary<string, Ensemble> cache,
            IEnsembleTransactionData ensembleTransactionData,
            string currentTag,
            IEnumerable<Guid> currentPostsynapticIds
        )
        {
            string cacheId = currentTag + string.Join(string.Empty, currentPostsynapticIds.OrderBy(g => g));
            if (
                (
                    cache == null || 
                    !cache.TryGetValue(cacheId, out Ensemble result)
                ) &&
                !ensembleTransactionData.TryGetSavedTransient(currentTag, currentPostsynapticIds, out result)
                )
            {
                var tempResult = await ensembleRepository.GetByQueryAsync(
                    new Library.Common.NeuronQuery()
                    {
                        Tag = !string.IsNullOrEmpty(currentTag) ? new string[] { currentTag } : null,
                        Postsynaptic = currentPostsynapticIds.Select(pi => pi.ToString()),
                        DirectionValues = Library.Common.DirectionValues.Outbound,
                        Depth = 1
                    }
                );

                if (tempResult.Ensemble.GetItems().Count() > 0)
                {
                    result = tempResult.Ensemble;

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
