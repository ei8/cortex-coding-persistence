using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace ei8.Cortex.Coding.Persistence
{
    // TODO:1 transfer elsewhere so Key formulation can be dialect specific (ie. code below can be specific to d#)
    public static class MirrorRepositoryExtensions
    {
        private static string ConvertKey(object value)
        {
            var result = value as string;
            if (value is MemberInfo)
                result = MirrorConfig.ToKeyString((MemberInfo)value);
            else if (value is Enum)
                result = MirrorConfig.ToKeyString((Enum)value);

            return result;
        }

        public static async Task<IEnumerable<MirrorConfig>> GetAllMissingAsync(
            this IMirrorRepository mirrorRepository,
            IEnumerable<object> keys
        )
            => await mirrorRepository.GetAllMissingAsync(
                MirrorRepositoryExtensions.ConvertToStringKeys(keys).ToArray()
            );

        public static IEnumerable<string> ConvertToStringKeys(IEnumerable<object> keys) =>
            keys.Select(t => MirrorRepositoryExtensions.ConvertKey(t)
            );

        public static async Task<Neuron> GetByKeyAsync(
            this IMirrorRepository mirrorRepository,
            object key,
            bool throwErrorIfMissing = true
        ) =>
            (await mirrorRepository.GetByKeysAsync(
                new[] { key },
                throwErrorIfMissing
             )).Values.SingleOrDefault();

        public static async Task<IDictionary<object, Neuron>> GetByKeysAsync(
            this IMirrorRepository mirrorRepository,
            IEnumerable<object> keys,
            bool throwErrorIfMissing = true
        )
        {
            var stringKeys = MirrorRepositoryExtensions.ConvertToStringKeys(keys);
            var origDict = await mirrorRepository.GetByKeysAsync(
                stringKeys,
                throwErrorIfMissing
            );

            return origDict.ToDictionary(
                kvpK => keys.Single(t => MirrorRepositoryExtensions.ConvertKey(t) == kvpK.Key),
                kvpE => kvpE.Value
            );
        }
    }
}
