using System;

namespace CoCoFlow.Editor.Core
{
    internal enum AddressablesVersionCompatibility
    {
        Unknown = 0,
        BelowMinimum = 1,
        Supported = 2,
        AtOrAboveMaximum = 3
    }

    internal enum CoCoUniTaskInstallForm
    {
        None = 0,
        UpmRegistered = 1,
        AssemblyOnly = 2
    }

    internal enum CoCoUniTaskVersionCompatibility
    {
        Unknown = 0,
        BelowMinimum = 1,
        Supported = 2,
        AtOrAboveMaximum = 3
    }

    internal static class CoCoUniTaskVersionPolicy
    {
        internal const string MinimumVersion = "2.5.11";
        internal const string MaximumExclusiveVersion = "3.0.0";
        internal const string SupportedRange = "[2.5.11,3.0.0)";

        internal static CoCoUniTaskVersionCompatibility Evaluate(string dependency)
        {
            string version = ExtractVersion(dependency);
            if (version == null)
                return CoCoUniTaskVersionCompatibility.Unknown;

            if (Compare(version, MinimumVersion) < 0)
                return CoCoUniTaskVersionCompatibility.BelowMinimum;

            return Compare(version, MaximumExclusiveVersion) >= 0
                ? CoCoUniTaskVersionCompatibility.AtOrAboveMaximum
                : CoCoUniTaskVersionCompatibility.Supported;
        }

        // 接受 "2.5.11" 或 git URL 尾缀 "...#2.5.11"；其余（file: 路径等）返回 null → Unknown，
        // 交由 Unity versionDefines 机制自行评估 resolved 版本。
        internal static string ExtractVersion(string dependency)
        {
            if (string.IsNullOrEmpty(dependency))
                return null;

            var token = dependency.Trim();
            var hashIndex = token.LastIndexOf('#');
            if (hashIndex >= 0)
                token = token.Substring(hashIndex + 1);

            var parts = token.Split('.');
            if (parts.Length != 3)
                return null;

            foreach (var part in parts)
            {
                if (!int.TryParse(part, out _))
                    return null;
            }

            return token;
        }

        private static int Compare(string left, string right)
        {
            var leftParts = left.Split('.');
            var rightParts = right.Split('.');

            for (var index = 0; index < 3; index++)
            {
                var comparison = int.Parse(leftParts[index]).CompareTo(int.Parse(rightParts[index]));
                if (comparison != 0)
                    return comparison;
            }

            return 0;
        }
    }

    internal static class AddressablesVersionPolicy
    {
        internal const string MinimumVersion = "2.9.1";
        internal const string MaximumExclusiveVersion = "3.0.0";
        internal const string SupportedRange = "[2.9.1,3.0.0)";

        private static readonly SemanticVersion Minimum =
            SemanticVersion.ParseRequired(MinimumVersion);

        private static readonly SemanticVersion MaximumExclusive =
            SemanticVersion.ParseRequired(MaximumExclusiveVersion);

        internal static AddressablesVersionCompatibility Evaluate(string version)
        {
            if (!SemanticVersion.TryParse(version, out SemanticVersion parsed))
            {
                return AddressablesVersionCompatibility.Unknown;
            }

            if (parsed.CompareTo(Minimum) < 0)
            {
                return AddressablesVersionCompatibility.BelowMinimum;
            }

            return parsed.CompareTo(MaximumExclusive) >= 0
                ? AddressablesVersionCompatibility.AtOrAboveMaximum
                : AddressablesVersionCompatibility.Supported;
        }

        private readonly struct SemanticVersion : IComparable<SemanticVersion>
        {
            private SemanticVersion(
                int major,
                int minor,
                int patch,
                string[] prereleaseIdentifiers)
            {
                Major = major;
                Minor = minor;
                Patch = patch;
                PrereleaseIdentifiers = prereleaseIdentifiers;
            }

            private int Major { get; }
            private int Minor { get; }
            private int Patch { get; }
            private string[] PrereleaseIdentifiers { get; }

            public int CompareTo(SemanticVersion other)
            {
                int comparison = Major.CompareTo(other.Major);
                if (comparison != 0) return comparison;

                comparison = Minor.CompareTo(other.Minor);
                if (comparison != 0) return comparison;

                comparison = Patch.CompareTo(other.Patch);
                if (comparison != 0) return comparison;

                bool hasPrerelease = PrereleaseIdentifiers.Length != 0;
                bool otherHasPrerelease = other.PrereleaseIdentifiers.Length != 0;
                if (!hasPrerelease || !otherHasPrerelease)
                {
                    if (hasPrerelease == otherHasPrerelease) return 0;
                    return hasPrerelease ? -1 : 1;
                }

                int count = Math.Min(
                    PrereleaseIdentifiers.Length,
                    other.PrereleaseIdentifiers.Length);
                for (int index = 0; index < count; index++)
                {
                    comparison = CompareIdentifier(
                        PrereleaseIdentifiers[index],
                        other.PrereleaseIdentifiers[index]);
                    if (comparison != 0) return comparison;
                }

                return PrereleaseIdentifiers.Length.CompareTo(
                    other.PrereleaseIdentifiers.Length);
            }

            internal static SemanticVersion ParseRequired(string version)
            {
                if (TryParse(version, out SemanticVersion parsed))
                {
                    return parsed;
                }

                throw new InvalidOperationException(
                    "The frozen Addressables version boundary is invalid: " + version);
            }

            internal static bool TryParse(
                string version,
                out SemanticVersion parsed)
            {
                parsed = default;
                if (string.IsNullOrWhiteSpace(version)) return false;

                string value = version.Trim();
                string buildMetadata = string.Empty;
                int buildIndex = value.IndexOf('+');
                if (buildIndex >= 0)
                {
                    buildMetadata = value.Substring(buildIndex + 1);
                    value = value.Substring(0, buildIndex);
                    if (!HasValidIdentifiers(buildMetadata))
                    {
                        return false;
                    }
                }

                string prerelease = string.Empty;
                int prereleaseIndex = value.IndexOf('-');
                if (prereleaseIndex >= 0)
                {
                    prerelease = value.Substring(prereleaseIndex + 1);
                    value = value.Substring(0, prereleaseIndex);
                    if (string.IsNullOrEmpty(prerelease)) return false;
                }

                string[] core = value.Split('.');
                if (core.Length != 3) return false;

                var parts = new int[3];
                for (int index = 0; index < core.Length; index++)
                {
                    if (!TryParseCorePart(core[index], out parts[index]))
                    {
                        return false;
                    }
                }

                string[] identifiers = string.IsNullOrEmpty(prerelease)
                    ? Array.Empty<string>()
                    : prerelease.Split('.');
                foreach (string identifier in identifiers)
                {
                    if (!IsValidIdentifier(identifier)) return false;
                    if (IsNumeric(identifier) &&
                        identifier.Length > 1 &&
                        identifier[0] == '0')
                    {
                        return false;
                    }
                }

                parsed = new SemanticVersion(
                    parts[0],
                    parts[1],
                    parts[2],
                    identifiers);
                return true;
            }

            private static bool TryParseCorePart(string value, out int part)
            {
                part = 0;
                if (string.IsNullOrEmpty(value)) return false;
                if (value.Length > 1 && value[0] == '0') return false;
                foreach (char character in value)
                {
                    if (character < '0' || character > '9') return false;
                }

                return int.TryParse(value, out part) && part >= 0;
            }

            private static bool IsValidIdentifier(string value)
            {
                if (string.IsNullOrEmpty(value)) return false;
                foreach (char character in value)
                {
                    bool asciiDigit = character >= '0' && character <= '9';
                    bool asciiUpper = character >= 'A' && character <= 'Z';
                    bool asciiLower = character >= 'a' && character <= 'z';
                    if (!asciiDigit &&
                        !asciiUpper &&
                        !asciiLower &&
                        character != '-')
                    {
                        return false;
                    }
                }

                return true;
            }

            private static bool HasValidIdentifiers(string value)
            {
                if (string.IsNullOrEmpty(value)) return false;
                foreach (string identifier in value.Split('.'))
                {
                    if (!IsValidIdentifier(identifier)) return false;
                }

                return true;
            }

            private static bool IsNumeric(string value)
            {
                foreach (char character in value)
                {
                    if (character < '0' || character > '9') return false;
                }

                return value.Length != 0;
            }

            private static int CompareIdentifier(string left, string right)
            {
                bool leftNumeric = IsNumeric(left);
                bool rightNumeric = IsNumeric(right);
                if (leftNumeric && rightNumeric)
                {
                    int lengthComparison = left.Length.CompareTo(right.Length);
                    return lengthComparison != 0
                        ? lengthComparison
                        : string.CompareOrdinal(left, right);
                }

                if (leftNumeric != rightNumeric)
                {
                    return leftNumeric ? -1 : 1;
                }

                return string.CompareOrdinal(left, right);
            }
        }
    }
}
