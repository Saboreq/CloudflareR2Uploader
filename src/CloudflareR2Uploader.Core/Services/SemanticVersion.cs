using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CloudflareR2Uploader.Services
{
    internal sealed class SemanticVersion : IComparable<SemanticVersion>
    {
        private static readonly Regex Pattern = new Regex(
            @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?![\s\S])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly string _major;
        private readonly string _minor;
        private readonly string _patch;

        private SemanticVersion(string major, string minor, string patch, string prerelease)
        {
            _major = major;
            _minor = minor;
            _patch = patch;
            Prerelease = prerelease ?? string.Empty;
        }

        public string Prerelease { get; private set; }

        public static bool TryParse(string value, out SemanticVersion version)
        {
            version = null;
            Match match = Pattern.Match(value ?? string.Empty);
            if (!match.Success) return false;

            string prerelease = match.Groups[4].Value;
            if (prerelease.Length > 0)
            {
                foreach (string identifier in prerelease.Split('.'))
                {
                    if (identifier.Length > 1 && identifier[0] == '0' && IsNumeric(identifier))
                        return false;
                }
            }

            version = new SemanticVersion(
                match.Groups[1].Value,
                match.Groups[2].Value,
                match.Groups[3].Value,
                prerelease);
            return true;
        }

        public bool TryGetFileVersion(out int major, out int minor, out int patch)
        {
            bool validMajor = TryGetFileVersionComponent(_major, out major);
            bool validMinor = TryGetFileVersionComponent(_minor, out minor);
            bool validPatch = TryGetFileVersionComponent(_patch, out patch);
            return validMajor && validMinor && validPatch;
        }

        public int CompareTo(SemanticVersion other)
        {
            if (other == null) return 1;
            int result = CompareNumeric(_major, other._major);
            if (result != 0) return result;
            result = CompareNumeric(_minor, other._minor);
            if (result != 0) return result;
            result = CompareNumeric(_patch, other._patch);
            if (result != 0) return result;
            bool thisRelease = Prerelease.Length == 0;
            bool otherRelease = other.Prerelease.Length == 0;
            if (thisRelease != otherRelease) return thisRelease ? 1 : -1;
            if (thisRelease) return 0;
            string[] left = Prerelease.Split('.');
            string[] right = other.Prerelease.Split('.');
            int count = Math.Min(left.Length, right.Length);
            for (int i = 0; i < count; i++)
            {
                bool leftNumeric = IsNumeric(left[i]);
                bool rightNumeric = IsNumeric(right[i]);
                if (leftNumeric && rightNumeric) result = CompareNumeric(left[i], right[i]);
                else if (leftNumeric != rightNumeric) result = leftNumeric ? -1 : 1;
                else result = string.CompareOrdinal(left[i], right[i]);
                if (result != 0) return result;
            }
            return left.Length.CompareTo(right.Length);
        }

        private static int CompareNumeric(string left, string right)
        {
            int length = left.Length.CompareTo(right.Length);
            return length != 0 ? length : string.CompareOrdinal(left, right);
        }

        private static bool IsNumeric(string value)
        {
            for (int index = 0; index < value.Length; index++)
            {
                if (value[index] < '0' || value[index] > '9') return false;
            }

            return value.Length > 0;
        }

        private static bool TryGetFileVersionComponent(string value, out int component)
        {
            return int.TryParse(
                    value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out component) &&
                component <= ushort.MaxValue;
        }
    }
}
