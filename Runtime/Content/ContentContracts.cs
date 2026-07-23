using System;
using UnityEngine;

namespace CoCoFlow.Runtime.Content
{
    public enum ContentKind
    {
        Asset = 0,
        PrefabSource = 1,
        AdditiveScene = 2
    }

    public enum ContentSourceKind
    {
        Direct = 0,
        Addressables = 1
    }

    [Serializable]
    public struct ContentId : IEquatable<ContentId>
    {
        [SerializeField] private string value;

        private ContentId(string value)
        {
            this.value = value;
        }

        public string Value => value ?? string.Empty;
        public bool IsValid =>
            !string.IsNullOrWhiteSpace(value) &&
            string.Equals(value, value.Trim(), StringComparison.Ordinal);

        public static bool TryCreate(string value, out ContentId id)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                id = default;
                return false;
            }

            id = new ContentId(value.Trim());
            return true;
        }

        public bool Equals(ContentId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is ContentId other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value;

        public static bool operator ==(ContentId left, ContentId right) => left.Equals(right);
        public static bool operator !=(ContentId left, ContentId right) => !left.Equals(right);
    }

    [Serializable]
    public struct ContentOwnerId : IEquatable<ContentOwnerId>
    {
        [SerializeField] private string value;

        private ContentOwnerId(string value)
        {
            this.value = value;
        }

        public string Value => value ?? string.Empty;
        public bool IsValid =>
            !string.IsNullOrWhiteSpace(value) &&
            string.Equals(value, value.Trim(), StringComparison.Ordinal);

        public static bool TryCreate(string value, out ContentOwnerId id)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                id = default;
                return false;
            }

            id = new ContentOwnerId(value.Trim());
            return true;
        }

        public bool Equals(ContentOwnerId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is ContentOwnerId other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value;

        public static bool operator ==(ContentOwnerId left, ContentOwnerId right) => left.Equals(right);
        public static bool operator !=(ContentOwnerId left, ContentOwnerId right) => !left.Equals(right);
    }

    public readonly struct ContentBackendId : IEquatable<ContentBackendId>
    {
        private readonly string value;

        private ContentBackendId(string value)
        {
            this.value = value;
        }

        public string Value => value ?? string.Empty;
        public bool IsValid => !string.IsNullOrWhiteSpace(value);

        public static bool TryCreate(string value, out ContentBackendId id)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                id = default;
                return false;
            }

            id = new ContentBackendId(value.Trim());
            return true;
        }

        public bool Equals(ContentBackendId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is ContentBackendId other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value;

        public static bool operator ==(ContentBackendId left, ContentBackendId right) => left.Equals(right);
        public static bool operator !=(ContentBackendId left, ContentBackendId right) => !left.Equals(right);
    }

    [Serializable]
    public struct ContentReference : IEquatable<ContentReference>
    {
        [SerializeField] private ContentId id;
        [SerializeField] private ContentKind kind;
        [SerializeField] private ContentSourceKind sourceKind;
        [SerializeField] private UnityEngine.Object directObject;
        [SerializeField] private string location;

        private ContentReference(
            ContentId id,
            ContentKind kind,
            ContentSourceKind sourceKind,
            UnityEngine.Object directObject,
            string location)
        {
            this.id = id;
            this.kind = kind;
            this.sourceKind = sourceKind;
            this.directObject = directObject;
            this.location = location;
        }

        public ContentId Id => id;
        public ContentKind Kind => kind;
        public ContentSourceKind SourceKind => sourceKind;
        public UnityEngine.Object DirectObject => directObject;
        public string Location => location ?? string.Empty;

        public bool IsValid
        {
            get
            {
                if (!id.IsValid || !Enum.IsDefined(typeof(ContentKind), kind) ||
                    !Enum.IsDefined(typeof(ContentSourceKind), sourceKind))
                {
                    return false;
                }

                if (sourceKind == ContentSourceKind.Direct)
                {
                    return kind == ContentKind.AdditiveScene
                        ? !string.IsNullOrWhiteSpace(location) && directObject == null
                        : directObject != null && string.IsNullOrEmpty(location);
                }

                return !string.IsNullOrWhiteSpace(location) && directObject == null;
            }
        }

        public static bool TryCreateDirectAsset(
            ContentId id,
            UnityEngine.Object asset,
            out ContentReference reference) =>
            TryCreate(id, ContentKind.Asset, ContentSourceKind.Direct, asset, string.Empty, out reference);

        public static bool TryCreateDirectPrefabSource(
            ContentId id,
            GameObject prefab,
            out ContentReference reference) =>
            TryCreate(id, ContentKind.PrefabSource, ContentSourceKind.Direct, prefab, string.Empty, out reference);

        public static bool TryCreateDirectAdditiveScene(
            ContentId id,
            string scenePathOrName,
            out ContentReference reference) =>
            TryCreate(
                id,
                ContentKind.AdditiveScene,
                ContentSourceKind.Direct,
                null,
                scenePathOrName,
                out reference);

        public static bool TryCreateAddressableAsset(
            ContentId id,
            string address,
            out ContentReference reference) =>
            TryCreate(id, ContentKind.Asset, ContentSourceKind.Addressables, null, address, out reference);

        public static bool TryCreateAddressablePrefabSource(
            ContentId id,
            string address,
            out ContentReference reference) =>
            TryCreate(id, ContentKind.PrefabSource, ContentSourceKind.Addressables, null, address, out reference);

        public static bool TryCreateAddressableAdditiveScene(
            ContentId id,
            string address,
            out ContentReference reference) =>
            TryCreate(
                id,
                ContentKind.AdditiveScene,
                ContentSourceKind.Addressables,
                null,
                address,
                out reference);

        private static bool TryCreate(
            ContentId id,
            ContentKind kind,
            ContentSourceKind sourceKind,
            UnityEngine.Object directObject,
            string location,
            out ContentReference reference)
        {
            reference = new ContentReference(
                id,
                kind,
                sourceKind,
                directObject,
                location == null ? string.Empty : location.Trim());
            if (reference.IsValid) return true;

            reference = default;
            return false;
        }

        public bool Equals(ContentReference other) =>
            id.Equals(other.id) &&
            kind == other.kind &&
            sourceKind == other.sourceKind &&
            directObject == other.directObject &&
            string.Equals(Location, other.Location, StringComparison.Ordinal);

        public override bool Equals(object obj) =>
            obj is ContentReference other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = id.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)kind;
                hashCode = (hashCode * 397) ^ (int)sourceKind;
                hashCode = (hashCode * 397) ^ (directObject == null ? 0 : directObject.GetHashCode());
                hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(Location);
                return hashCode;
            }
        }

        public static bool operator ==(ContentReference left, ContentReference right) => left.Equals(right);
        public static bool operator !=(ContentReference left, ContentReference right) => !left.Equals(right);
    }
}
