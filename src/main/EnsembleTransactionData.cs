using System;
using System.Collections.Generic;

namespace ei8.Cortex.Coding.Persistence
{
    public class EnsembleTransactionData : IEnsembleTransactionData
    {
        private readonly IDictionary<Guid, Neuron> savedTransientNeurons;
        private readonly IDictionary<Guid, Neuron> replacedNeurons;

        public EnsembleTransactionData()
        {
            this.savedTransientNeurons = new Dictionary<Guid, Neuron>();
            this.replacedNeurons = new Dictionary<Guid, Neuron>();
        }

        public void AddSavedTransientNeuron(Neuron value) => 
            this.savedTransientNeurons.Add(value.Id, value);

        public void AddReplacedNeuron(Guid originalId, Neuron value) => 
            this.replacedNeurons.Add(originalId, value);

        public bool TryGetReplacedNeuron(Guid originalId, out Neuron value) => 
            this.replacedNeurons.TryGetValue(originalId, out value);

        public bool IsReplaced(Guid value) => this.replacedNeurons.ContainsKey(value);

        public Guid GetReplacementIdIfExists(Guid originalId) =>
            this.TryGetReplacedNeuron(originalId, out Neuron replacement) ?
                replacement.Id :
                originalId;

        public IEnumerable<Neuron> SavedTransientNeurons => 
            this.savedTransientNeurons.Values;
    }
}
