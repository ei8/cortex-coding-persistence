using ei8.Cortex.Coding.Properties;
using neurUL.Common.Domain.Model;
using System;

namespace ei8.Cortex.Coding.Persistence.Wrappers
{
    [neurULKey("System.String")]
    public class StringWrapper : IValueWrapper<string>
    {
        // TODO:0 update so it is saved like an InstanceValue
        public StringWrapper() : this(null)
        {            
        }

        public StringWrapper(string value) : this(Guid.NewGuid(), value)
        {            
        }

        public StringWrapper(Guid id, string value)
        {
            AssertionConcern.AssertArgumentValid(i => i != Guid.Empty, id, $"Id cannot be equal to '{Guid.Empty}'.", nameof(id));

            this.Id = id;
            this.Value = value;
        }

        // TODO: should allow for null values getting persisted into and retrieved from brain
        [neurULNeuronProperty(nameof(Neuron.Tag))]
        public string Value { get; set; }

        [neurULNeuronProperty]
        public Guid Id { get; set; }
    }
}