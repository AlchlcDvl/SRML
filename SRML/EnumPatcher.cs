using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SRML.SR;
using SRML.Utils;

namespace SRML
{
    /// <summary>
    /// Allows adding values to any Enum
    /// </summary>
    public static class EnumPatcher
    {
        public delegate object AlternateEnumRegister(object value, string name);

        private static readonly Dictionary<Type, AlternateEnumRegister> BANNED_ENUMS = new Dictionary<Type, AlternateEnumRegister>()
        {
            { typeof(Identifiable.Id), (x,y) => IdentifiableRegistry.CreateIdentifiableId(x,y) },
            { typeof(Gadget.Id), (x,y) => GadgetRegistry.CreateGadgetId(x,y) },
            { typeof(PlayerState.Upgrade), (x,y) => PersonalUpgradeRegistry.CreatePersonalUpgrade(x,y) },
            { typeof(PediaDirector.Id), (x,y) => PediaRegistry.CreatePediaId(x,y) },
            { typeof(LandPlot.Id), (x,y) => LandPlotRegistry.CreateLandPlotId(x,y) },
            { typeof(RanchDirector.Palette), (x,y) => ChromaRegistry.CreatePalette(x,y) },
            { typeof(RanchDirector.PaletteType), (x,y) => ChromaRegistry.CreatePaletteType(x,y) }
        };

        public static void RegisterAlternate<T>(AlternateEnumRegister del) where T : Enum => RegisterAlternate(typeof(T), del);

        public static void RegisterAlternate(Type type, AlternateEnumRegister del)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (!type.IsEnum) throw new Exception($"The given type {type} isn't an enum");

            BANNED_ENUMS.Add(type, del);
        }

        private static readonly FieldInfo cache;

        private static readonly Dictionary<Type, EnumPatch> patches = new Dictionary<Type, EnumPatch>();

        static EnumPatcher()
        {
            var t = AccessTools.TypeByName("System.RuntimeType");
            cache = AccessTools.Field(t, "GenericCache");
        }

        /// <summary>
        /// Add a new enum value to the given <typeparamref name="T"/> with the first free value
        /// </summary>
        /// <typeparam name="T">Type of enum to add the value to</typeparam>
        /// <param name="name">Name of the new enum value</param>
        /// <returns>The new enum value</returns>
        public static T AddEnumValue<T>(string name) where T : Enum => (T)AddEnumValue(typeof(T), name);

        /// <summary>
        /// Add a new enum value to the given <paramref name="enumType"/> with the first free value
        /// </summary>
        /// <param name="enumType">Type of enum to add the value to</param>
        /// <param name="name">Name of the new enum value</param>
        /// <returns>The new enum value</returns>
        public static object AddEnumValue(Type enumType, string name)
        {
            var newVal = GetFirstFreeValue(enumType);
            AddEnumValue(enumType, newVal, name);
            return newVal;
        }

        /// <summary>
        /// Add a new value to the given <typeparamref name="T"/>
        /// </summary>
        /// <typeparam name="T">Type of enum to add the value to</typeparam>
        /// <param name="value">Value to add to the enum</param>
        /// <param name="name">The name of the new value</param>
        public static void AddEnumValue<T>(object value, string name) => AddEnumValue(typeof(T), value, name);

        /// <summary>
        /// Add a new value to the given <paramref name="enumType"/>
        /// </summary>
        /// <param name="enumType">Enum to add the new value to</param>
        /// <param name="value">Value to add to the enum</param>
        /// <param name="name">The name of the new value</param>
        public static void AddEnumValue(Type enumType, object value, string name)
        {
            if (enumType == null) throw new ArgumentNullException(nameof(enumType));
            if (!enumType.IsEnum) throw new Exception($"{enumType} is not a valid Enum!");
            if (SRModLoader.GetModForAssembly(Assembly.GetCallingAssembly()) != null && BANNED_ENUMS.ContainsKey(enumType)) throw new Exception($"Patching {enumType} through EnumPatcher is not supported!");
            if (AlreadyHasName(enumType, name) || EnumUtils.HasEnumValue(enumType, name)) throw new Exception($"The enum ({enumType.FullName}) already has a value with the name \"{name}\"");

            value = (ulong)Convert.ToInt64(value, CultureInfo.InvariantCulture);

            if (!patches.TryGetValue(enumType, out var patch))
            {
                patch = new EnumPatch();
                patches.Add(enumType, patch);
            }

            ClearEnumCache(enumType);

            patch.AddValue((ulong)value, name);
        }

        public static void AddEnumValueWithAlternatives<T>(object value, string name) where T : Enum => AddEnumValueWithAlternatives(typeof(T), value, name);

        public static void AddEnumValueWithAlternatives(Type enumType, object value, string name)
        {
            if (BANNED_ENUMS.TryGetValue(enumType, out var alternate)) alternate(value, name);
            else AddEnumValue(enumType, value, name);
        }

        internal static bool TryAsNumber(this object value, Type type, out object result)
        {
            if (type.IsSubclassOf(typeof(IConvertible)))
                throw new ArgumentException("The type must inherit the IConvertible interface", nameof(type));

            result = null;

            if (type.IsInstanceOfType(value))
            {
                result = value;
                return true;
            }

            if (value is IConvertible)
            {
                if (type.IsEnum)
                {
                    result = Enum.ToObject(type, value);
                    return true;
                }

                var format = NumberFormatInfo.CurrentInfo;
                result = (value as IConvertible).ToType(type, format);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Get first undefined value in an enum
        /// </summary>
        /// <typeparam name="T">Type of enum</typeparam>
        /// <returns>The first undefined enum value</returns>
        public static T GetFirstFreeValue<T>() => (T)GetFirstFreeValue(typeof(T));

        /// <summary>
        /// Get first undefined value in an enum
        /// </summary>
        /// <param name="enumType"></param>
        /// <returns>The first undefined enum value</returns>
        public static object GetFirstFreeValue(Type enumType)
        {
            if (enumType == null) throw new ArgumentNullException(nameof(enumType));
            if (!enumType.IsEnum) throw new ArgumentException($"{enumType} is not a valid Enum!");

            var vals = Enum.GetValues(enumType);
            long l = 0;
            for (ulong i = 0; i <= ulong.MaxValue; i++)
            {
                if (!i.TryAsNumber(enumType, out var v))
                    break;
                for (; l < vals.Length; l++)
                    if (vals.GetValue(l).Equals(v))
                        goto skip;
                return v;
                skip:;
            }
            for (long i = -1; i >= long.MinValue; i--)
            {
                if (!i.TryAsNumber(enumType, out var v))
                    break;
                for (; l < vals.Length; l++)
                    if (vals.GetValue(l).Equals(v))
                        goto skip;
                return v;
                skip:;
            }
            throw new Exception("No unused values in enum " + enumType.FullName);
        }

        public static void AddFormerAliases(Type enumType, object value, IEnumerable<string> aliases)
        {
            if (enumType == null) throw new ArgumentNullException(nameof(enumType));
            if (!enumType.IsEnum) throw new Exception($"{enumType} is not a valid Enum!");
            if (!patches.TryGetValue(enumType, out var patch)) throw new ArgumentException($"Can't register aliases in {enumType} for an unregistered value!");

            ClearEnumCache(enumType);
            patch.AddAliases((ulong)value, aliases);
        }

        public static void ClearEnumCache(Type enumType)
            => cache.SetValue(enumType, null);

        internal static bool TryGetRawPatch(Type enumType, out EnumPatch patch)
            => patches.TryGetValue(enumType, out patch);

        internal static bool AlreadyHasName(Type enumType, string name)
            => TryGetRawPatch(enumType, out EnumPatch patch) && patch.HasName(name);

        internal class EnumPatch
        {
            private readonly Dictionary<ulong, HashSet<string>> values = new Dictionary<ulong, HashSet<string>>();
            internal bool signed;

            public void AddValue(ulong enumValue, string name)
            {
                if (!values.TryGetValue(enumValue, out var list))
                    values[enumValue] = list = new HashSet<string>();

                list.Add(name);
            }

            public void AddAliases(ulong enumValue, IEnumerable<string> aliases)
            {
                if (!values.TryGetValue(enumValue, out var list))
                    values[enumValue] = list = new HashSet<string>();

                list.UnionWith(aliases);
            }

            public List<KeyValuePair<ulong, string>> GetPairs()
            {
                List<KeyValuePair<ulong, string>> pairs = new List<KeyValuePair<ulong, string>>();

                foreach (KeyValuePair<ulong, HashSet<string>> pair in values)
                {
                    foreach (string value in pair.Value)
                        pairs.Add(new KeyValuePair<ulong, string>(pair.Key, value));
                }

                return pairs;
            }

            public bool HasName(string name)
            {
                foreach (var names in values.Values)
                {
                    if (names.Contains(name))
                        return true;
                }

                return false;
            }
        }
    }
}
