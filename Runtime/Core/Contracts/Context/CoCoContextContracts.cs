using System;
using System.Globalization;
using System.Reflection;

namespace CoCoFlow.Runtime.Core
{
    public readonly struct CoCoContextRevision : IEquatable<CoCoContextRevision>
    {
        public CoCoContextRevision(ulong value)
        {
            Value = value;
        }

        public ulong Value { get; }

        public bool Equals(CoCoContextRevision other) => Value == other.Value;
        public override bool Equals(object obj) => obj is CoCoContextRevision other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(CoCoContextRevision left, CoCoContextRevision right) => left.Equals(right);
        public static bool operator !=(CoCoContextRevision left, CoCoContextRevision right) => !left.Equals(right);
    }

    public interface ICoCoContextSection
    {
    }

    public interface ICoCoContextFrame
    {
        CoCoContextRevision Revision { get; }

        TSection GetSection<TSection>(CoCoContextRequirement requirement)
            where TSection : class, ICoCoContextSection;
    }

    public readonly struct CoCoContextRequirement : IEquatable<CoCoContextRequirement>
    {
        private CoCoContextRequirement(Type sectionType)
        {
            SectionType = sectionType;
        }

        public Type SectionType { get; }
        public bool IsValid => SectionType != null;

        public static CoCoContextRequirement For<TSection>()
            where TSection : class, ICoCoContextSection
        {
            return RequirementCache<TSection>.Value;
        }

        public bool Matches<TSection>()
            where TSection : class, ICoCoContextSection
        {
            return IsValid && SectionType == typeof(TSection);
        }

        public bool Equals(CoCoContextRequirement other) => SectionType == other.SectionType;
        public override bool Equals(object obj) => obj is CoCoContextRequirement other && Equals(other);
        public override int GetHashCode() => SectionType?.GetHashCode() ?? 0;

        public static bool operator ==(CoCoContextRequirement left, CoCoContextRequirement right) => left.Equals(right);
        public static bool operator !=(CoCoContextRequirement left, CoCoContextRequirement right) => !left.Equals(right);

        private static bool IsSectionInterface(Type sectionType)
        {
            return sectionType != null &&
                   sectionType.IsInterface &&
                   sectionType != typeof(ICoCoContextSection) &&
                   typeof(ICoCoContextSection).IsAssignableFrom(sectionType) &&
                   HasReadOnlySurface(sectionType);
        }

        private static bool HasReadOnlySurface(Type sectionType)
        {
            PropertyInfo[] properties = sectionType.GetProperties();
            for (int index = 0; index < properties.Length; index++)
            {
                PropertyInfo property = properties[index];
                MethodInfo getter = property.GetMethod;
                if (!property.CanRead ||
                    property.CanWrite ||
                    getter == null ||
                    getter.ReturnParameter.ParameterType.IsByRef ||
                    !IsFrozenFactType(property.PropertyType))
                {
                    return false;
                }
            }

            MethodInfo[] methods = sectionType.GetMethods();
            for (int index = 0; index < methods.Length; index++)
            {
                if (!methods[index].IsSpecialName ||
                    !methods[index].Name.StartsWith("get_", StringComparison.Ordinal))
                {
                    return false;
                }
            }

            if (sectionType.GetEvents().Length != 0)
            {
                return false;
            }

            Type[] inheritedInterfaces = sectionType.GetInterfaces();
            for (int index = 0; index < inheritedInterfaces.Length; index++)
            {
                Type inheritedInterface = inheritedInterfaces[index];
                if (inheritedInterface != typeof(ICoCoContextSection) &&
                    !HasReadOnlySurface(inheritedInterface))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsFrozenFactType(Type factType)
        {
            if (factType == typeof(string) || factType.IsPrimitive || factType.IsEnum)
            {
                return true;
            }

            if (!factType.IsValueType ||
                factType.IsByRef ||
                factType.IsPointer ||
                factType == typeof(IntPtr) ||
                factType == typeof(UIntPtr))
            {
                return false;
            }

            FieldInfo[] fields = factType.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int index = 0; index < fields.Length; index++)
            {
                if (!IsFrozenFactType(fields[index].FieldType))
                {
                    return false;
                }
            }

            return true;
        }

        private static class RequirementCache<TSection>
            where TSection : class, ICoCoContextSection
        {
            public static readonly CoCoContextRequirement Value =
                IsSectionInterface(typeof(TSection))
                    ? new CoCoContextRequirement(typeof(TSection))
                    : default;
        }
    }
}
