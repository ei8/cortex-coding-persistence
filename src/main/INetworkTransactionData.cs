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

        /// <summary>
        /// Tries to obtain a Network from the Transaction Data containing a Neuron with the specified tag and postsynapticIds.
        /// </summary>
        /// <param name="tag"></param>
        /// <param name="postsynapticIds"></param>
        /// <param name="result"></param>
        /// <returns></returns>
        bool TryGetIdenticalNeuron(string tag, IEnumerable<Guid> postsynapticIds, out Network result);
    }
}
