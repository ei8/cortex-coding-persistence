using neurUL.Common.Domain.Model;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ei8.Cortex.Coding.Persistence
{
    public class NetworkTransactionData : INetworkTransactionData
    {
        private readonly IDictionary<Guid, INetworkItem> savedTransients;
        private readonly IDictionary<Guid, Neuron> replacedNeurons;

        public NetworkTransactionData()
        {
            this.savedTransients = new Dictionary<Guid, INetworkItem>();
            this.replacedNeurons = new Dictionary<Guid, Neuron>();
        }

        public void AddSavedTransient(INetworkItem value) => 
            this.savedTransients.Add(value.Id, 
                value is Neuron ne ?
                (INetworkItem) Neuron.CloneAsPersistent(ne) : 
                Terminal.CloneAsPersistent((Terminal) value)
            );

        public void AddReplacedNeuron(Guid originalId, Neuron value) => 
            this.replacedNeurons.Add(originalId, value);

        public bool TryGetReplacedNeuron(Guid originalId, out Neuron value) => 
            this.replacedNeurons.TryGetValue(originalId, out value);

        public bool IsReplaced(Guid value) => this.replacedNeurons.ContainsKey(value);

        public Guid GetReplacementIdIfExists(Guid originalId) =>
            this.TryGetReplacedNeuron(originalId, out Neuron replacement) ?
                replacement.Id :
                originalId;

        public bool TryGetSavedTransient(string tag, IEnumerable<Guid> currentPostsynapticIds, out Network result)
        {
            bool bResult = false;
            result = null;

            var resultNeurons = this.savedTransients.Values.OfType<Neuron>().Where(
                stn => stn.Tag == tag
            );

            var resultTerminals = this.savedTransients.Values.OfType<Terminal>().Where(
                stt => 
                    resultNeurons.Select(rn => rn.Id).Contains(stt.PresynapticNeuronId) && 
                    currentPostsynapticIds.Contains(stt.PostsynapticNeuronId)
            );

            if (
                resultTerminals.Count() == currentPostsynapticIds.Count() &&
                resultTerminals.Select(rt => rt.PostsynapticNeuronId).HasSameElementsAs(currentPostsynapticIds)
            )
            { 
                var distinctPresynapticIds = resultTerminals.Select(rt => rt.PresynapticNeuronId).Distinct();
                AssertionConcern.AssertStateTrue(
                    distinctPresynapticIds.Count() == 1,
                    $"Redundant Neurons with postsynaptic Neurons '{string.Join(", ", currentPostsynapticIds)}' encountered: {string.Join(", ", distinctPresynapticIds)}"
                );

                var tempResult = new Network();
                tempResult.AddReplace(resultNeurons.Single(rn => rn.Id == distinctPresynapticIds.Single()));
                resultTerminals.ToList().ForEach(rt => tempResult.AddReplace(rt));
                result = tempResult;
                bResult = true;
            }

            return bResult;
        }

        public IEnumerable<Neuron> SavedTransientNeurons => 
            this.savedTransients.Values.OfType<Neuron>();
    }
}
