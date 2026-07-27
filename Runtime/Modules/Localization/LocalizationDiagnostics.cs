using System;

namespace CoCoFlow.Runtime.Modules.Localization
{
    public enum LocalizationDiagnosticCode
    {
        None = 0,
        MissingTextTarget = 1,
        MissingLocalizedString = 2,
        InvalidTableOrEntry = 3,
        LoadFailed = 4,
        EmptyResult = 5
    }

    public readonly struct LocalizationDiagnostic :
        IEquatable<LocalizationDiagnostic>
    {
        public LocalizationDiagnostic(
            LocalizationDiagnosticCode code,
            string message)
        {
            Code = code;
            Message = message ?? string.Empty;
        }

        public LocalizationDiagnosticCode Code { get; }
        public string Message { get; }
        public bool IsNone => Code == LocalizationDiagnosticCode.None;
        public bool IsError => !IsNone;

        public bool Equals(LocalizationDiagnostic other) =>
            Code == other.Code &&
            string.Equals(Message, other.Message, StringComparison.Ordinal);

        public override bool Equals(object obj) =>
            obj is LocalizationDiagnostic other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)Code * 397) ^ (Message?.GetHashCode() ?? 0);
            }
        }

        public override string ToString() =>
            IsNone ? "None" : $"{Code}: {Message}";

        public static bool operator ==(
            LocalizationDiagnostic left,
            LocalizationDiagnostic right) => left.Equals(right);

        public static bool operator !=(
            LocalizationDiagnostic left,
            LocalizationDiagnostic right) => !left.Equals(right);

        public static LocalizationDiagnostic None => default;
    }
}
