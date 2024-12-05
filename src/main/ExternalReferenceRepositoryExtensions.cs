using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace ei8.Cortex.Coding.Persistence
{
    public static class ExternalReferenceRepositoryExtensions
    {
        private static string ConvertKey(object value)
        {
            var result = value as string;
            if (value is MemberInfo)
                result = ExternalReference.ToKeyString((MemberInfo)value);
            else if (value is Enum)
                result = ExternalReference.ToKeyString((Enum)value);

            return result;
        }

        public static async Task<IEnumerable<ExternalReference>> GetAllMissingAsync(
            this IExternalReferenceRepository externalReferenceRepository,
            IEnumerable<object> keys
        )
            => await externalReferenceRepository.GetAllMissingAsync(
                keys.Select(t => ExternalReferenceRepositoryExtensions.ConvertKey(t)).ToArray()
            );

        public static async Task<Neuron> GetByKeyAsync(
            this IExternalReferenceRepository externalReferenceRepository,
            object key
        ) =>
            (await externalReferenceRepository.GetByKeysAsync(new[] { key })).Values.SingleOrDefault();

        public static async Task<IDictionary<object, Neuron>> GetByKeysAsync(
            this IExternalReferenceRepository externalReferenceRepository,
            IEnumerable<object> keys
            )
        {
            var origDict = await externalReferenceRepository.GetByKeysAsync(
                keys.Select(t => ExternalReferenceRepositoryExtensions.ConvertKey(t)).ToArray()
            );
            return origDict.ToDictionary(
                kvpK => keys.Single(t => ExternalReferenceRepositoryExtensions.ConvertKey(t) == kvpK.Key), 
                kvpE => kvpE.Value
            );
        }
    }
}
