using System;

namespace ei8.Cortex.Coding.Persistence.Wrappers
{
    public interface IValueWrapper<T>
    {
        Guid Id { get; set; }

        T Value { get; set; }
    }
}
