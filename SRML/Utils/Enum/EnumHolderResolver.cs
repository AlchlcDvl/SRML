using SRML.SR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine.Serialization;

namespace SRML.Utils.Enum
{
    internal static class EnumHolderResolver
    {
        public static void RegisterAllEnums(Module module)
        {
            SRMod.ForceModContext(SRModLoader.GetModForAssembly(module.Assembly));

            foreach (var type in module.GetTypes())
            {
                EnumHolderAttribute enumHolder = type.GetCustomAttribute<EnumHolderAttribute>();

                if (enumHolder == null)
                    continue;

                foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!field.FieldType.IsEnum) continue;

                    if (Convert.ToInt64(field.GetValue(null)) == 0)
                    {
                        var newVal = EnumPatcher.GetFirstFreeValue(field.FieldType);
                        EnumPatcher.AddEnumValueWithAlternatives(field.FieldType, newVal, field.Name);
                        field.SetValue(null, newVal);
                    }
                    else
                    {
                        EnumPatcher.AddEnumValueWithAlternatives(field.FieldType, field.GetValue(null), field.Name);
                    }

                    var formerNames = field.GetCustomAttributes<FormerlySerializedAsAttribute>();

                    if (formerNames != null)
                        EnumPatcher.AddFormerAliases(field.FieldType, field.GetValue(null), formerNames.Select(x => x.oldName));

                    CategorizeIds(field, enumHolder.shouldCategorize, IdentifiableCategorization.doNotAutoCategorize, IdentifiableCategorize, GetIdentifiableRules);
                    CategorizeIds(field, enumHolder.shouldCategorize, GadgetCategorization.doNotAutoCategorize, GadgetCategorize, GetGadgetRules);
                }
            }

            SRMod.ClearModContext();
        }

        private static readonly Action<Gadget.Id, GadgetCategorization.Rule> GadgetCategorize = (id, rules) => id.Categorize(rules);
        private static readonly Action<Identifiable.Id, IdentifiableCategorization.Rule> IdentifiableCategorize = (id, rules) => id.Categorize(rules);

        private static readonly Func<GadgetCategorization, GadgetCategorization.Rule> GetGadgetRules = categorization => categorization.rules;
        private static readonly Func<IdentifiableCategorization, IdentifiableCategorization.Rule> GetIdentifiableRules = categorization => categorization.rules;

        private static void CategorizeIds<TEnum, TCategorization, TCategorizationAttribute>(FieldInfo field, bool shouldCategorize, List<TEnum> doNotCategorizeList, Action<TEnum, TCategorization> categorize, Func<TCategorizationAttribute, TCategorization> rules)
            where TEnum : struct, System.Enum
            where TCategorization : struct, System.Enum
            where TCategorizationAttribute : Attribute
        {
            if (field.FieldType != typeof(TEnum))
                return;

            var value = (TEnum)field.GetValue(null);

            if (shouldCategorize)
            {
                var att = field.GetCustomAttribute<TCategorizationAttribute>();

                if (att != null)
                    categorize(value, rules(att));
            }
            else
            {
                doNotCategorizeList.Add(value);
            }
        }
    }
}
