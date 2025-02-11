using System;
using System.Collections.Generic;

namespace ei8.Cortex.Coding.Persistence
{
    public interface INetworkTransactionData
    {
        void AddSavedTransient(INetworkItem value);

        IEnumerable<Neuron> SavedTransientNeurons { get; }

        void AddReplacedNeuron(Guid originalId, Neuron value);

        bool TryGetReplacedNeuron(Guid originalId, out Neuron value);

        Guid GetReplacementIdIfExists(Guid originalId);

        bool IsReplaced(Guid value);

        bool TryGetSavedTransient(string tag, IEnumerable<Guid> currentPostsynapticIds, out Network result);
    }
}
