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

        public bool TryGetIdenticalNeuron(string tag, IEnumerable<Guid> postsynapticIds, out Network result)
        {
            bool bResult = false;
            result = null;

            var resultTerminals = Enumerable.Empty<Terminal>();
            // get neurons with... 
            var resultNeurons = this.savedTransients.Values.OfType<Neuron>().Where(
                stn => {
                    var stnTerminals = this.savedTransients.Values.OfType<Terminal>().Where(t => t.PresynapticNeuronId  == stn.Id);

                    // ... same tag and postsynaptic Ids
                    var whereResult = stn.Tag == tag && stnTerminals.Select(stt => stt.PostsynapticNeuronId).HasSameElementsAs(postsynapticIds);

                    if (whereResult)
                        resultTerminals = stnTerminals;

                    return whereResult;
                }
            );

            if (resultNeurons.Any())
            { 
                var distinctPresynapticIds = resultTerminals.Select(rt => rt.PresynapticNeuronId).Distinct();
                AssertionConcern.AssertStateTrue(
                    distinctPresynapticIds.Count() <= 1,
                    $"Redundant Neurons with postsynaptic Neurons '{string.Join(", ", postsynapticIds)}' encountered: {string.Join(", ", distinctPresynapticIds)}"
                );

                var tempResult = new Network();
                if (distinctPresynapticIds.Any())
                    tempResult.AddReplace(resultNeurons.Single(rn => rn.Id == distinctPresynapticIds.Single()));
                else if (resultNeurons.Any())
                    tempResult.AddReplace(resultNeurons.Single());
                resultTerminals.ToList().ForEach(rt => tempResult.AddReplace(rt));
                result = tempResult;
                bResult = tempResult.GetItems().Any();
            }

            return bResult;
        }

        public IEnumerable<Neuron> SavedTransientNeurons => 
            this.savedTransients.Values.OfType<Neuron>().ToList();
    }
}
