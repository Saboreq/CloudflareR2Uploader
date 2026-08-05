using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CloudflareR2Uploader.Services
{
    internal sealed class SemanticVersion : IComparable<SemanticVersion>
    {
        private static readonly Regex Pattern = new Regex(
            @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private SemanticVersion(int major, int minor, int patch, string prerelease)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            Prerelease = prerelease ?? string.Empty;
        }

        public int Major { get; private set; }
        public int Minor { get; private set; }
        public int Patch { get; private set; }
        public string Prerelease { get; private set; }

        public static bool TryParse(string value, out SemanticVersion version)
        {
            version = null;
            Match match = Pattern.Match((value ?? string.Empty).Trim());
            if (!match.Success) return false;
            int major;
            int minor;
            int patch;
            if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out major) ||
                !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out minor) ||
                !int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out patch)) return false;
            version = new SemanticVersion(major, minor, patch, match.Groups[4].Value);
            return true;
        }

        public int CompareTo(SemanticVersion other)
        {
            if (other == null) return 1;
            int result = Major.CompareTo(other.Major);
            if (result != 0) return result;
            result = Minor.CompareTo(other.Minor);
            if (result != 0) return result;
            result = Patch.CompareTo(other.Patch);
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
                long leftNumber;
                long rightNumber;
                bool leftNumeric = long.TryParse(left[i], NumberStyles.None, CultureInfo.InvariantCulture, out leftNumber);
                bool rightNumeric = long.TryParse(right[i], NumberStyles.None, CultureInfo.InvariantCulture, out rightNumber);
                if (leftNumeric && rightNumeric) result = leftNumber.CompareTo(rightNumber);
                else if (leftNumeric != rightNumeric) result = leftNumeric ? -1 : 1;
                else result = string.CompareOrdinal(left[i], right[i]);
                if (result != 0) return result;
            }
            return left.Length.CompareTo(right.Length);
        }
    }
}
