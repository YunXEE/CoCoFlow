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

        TSection GetSection<TSection>(CoCoContextSectionRequirement requirement)
            where TSection : class, ICoCoContextSection;
    }

    public readonly struct CoCoContextSectionRequirement : IEquatable<CoCoContextSectionRequirement>
    {
        private const BindingFlags DeclaredSurfaceFlags =
            BindingFlags.Public |
            BindingFlags.NonPublic |
            BindingFlags.Instance |
            BindingFlags.Static |
            BindingFlags.DeclaredOnly;

        private CoCoContextSectionRequirement(Type sectionType)
        {
            SectionType = sectionType;
        }

        public Type SectionType { get; }
        public bool IsValid => SectionType != null;

        public static CoCoContextSectionRequirement For<TSection>()
            where TSection : class, ICoCoContextSection
        {
            return RequirementCache<TSection>.Value;
        }

        public bool Matches<TSection>()
            where TSection : class, ICoCoContextSection
        {
            return IsValid && SectionType == typeof(TSection);
        }

        public bool Equals(CoCoContextSectionRequirement other) => SectionType == other.SectionType;
        public override bool Equals(object obj) => obj is CoCoContextSectionRequirement other && Equals(other);
        public override int GetHashCode() => SectionType?.GetHashCode() ?? 0;

        public static bool operator ==(
            CoCoContextSectionRequirement left,
            CoCoContextSectionRequirement right) => left.Equals(right);

        public static bool operator !=(
            CoCoContextSectionRequirement left,
            CoCoContextSectionRequirement right) => !left.Equals(right);

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
            PropertyInfo[] properties = sectionType.GetProperties(DeclaredSurfaceFlags);
            for (int index = 0; index < properties.Length; index++)
            {
                PropertyInfo property = properties[index];
                MethodInfo getter = property.GetMethod;
                if (!property.CanRead ||
                    property.CanWrite ||
                    getter == null ||
                    !getter.IsPublic ||
                    !getter.IsAbstract ||
                    getter.IsStatic ||
                    property.GetIndexParameters().Length != 0 ||
                    getter.GetParameters().Length != 0 ||
                    getter.ReturnParameter.ParameterType.IsByRef ||
                    !IsTopLevelFactType(property.PropertyType))
                {
                    return false;
                }
            }

            if (sectionType.GetFields(DeclaredSurfaceFlags).Length != 0 ||
                sectionType.GetEvents(DeclaredSurfaceFlags).Length != 0)
            {
                return false;
            }

            MethodInfo[] methods = sectionType.GetMethods(DeclaredSurfaceFlags);
            for (int index = 0; index < methods.Length; index++)
            {
                if (!IsDeclaredPropertyGetter(methods[index], properties))
                {
                    return false;
                }
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

        private static bool IsDeclaredPropertyGetter(
            MethodInfo method,
            PropertyInfo[] properties)
        {
            for (int index = 0; index < properties.Length; index++)
            {
                if (properties[index].GetMethod == method)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsTopLevelFactType(Type factType)
        {
            return factType == typeof(string) || IsReferenceFreeValueType(factType);
        }

        private static bool IsReferenceFreeValueType(Type factType)
        {
            if (factType == null ||
                factType.IsByRef ||
                factType.IsPointer ||
                factType.IsByRefLike ||
                factType == typeof(IntPtr) ||
                factType == typeof(UIntPtr))
            {
                return false;
            }

            if (factType.IsPrimitive || factType.IsEnum)
            {
                return true;
            }

            if (!factType.IsValueType)
            {
                return false;
            }

            FieldInfo[] fields = factType.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int index = 0; index < fields.Length; index++)
            {
                if (!IsReferenceFreeValueType(fields[index].FieldType))
                {
                    return false;
                }
            }

            return true;
        }

        private static class RequirementCache<TSection>
            where TSection : class, ICoCoContextSection
        {
            public static readonly CoCoContextSectionRequirement Value =
                IsSectionInterface(typeof(TSection))
                    ? new CoCoContextSectionRequirement(typeof(TSection))
                    : default;
        }
    }
}
