using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace Vendor.Common.Security
{
    /// <summary>
    /// Vendor-agnostic source-IP allowlisting for inbound webhooks.
    ///
    /// Given a remote IP and a set of allowed entries, decides whether the request
    /// may proceed. Entries may be:
    ///   - a plain IPv4 or IPv6 address  (e.g. "203.0.113.7", "2001:db8::1")
    ///   - a CIDR range                  (e.g. "52.10.0.0/16", "2001:db8::/32")
    ///
    /// POLICY:
    ///   - Empty or null allowlist => ALLOW ALL. The feature is opt-in per vendor;
    ///     a vendor with no configured IPs is simply not IP-restricted.
    ///   - Non-empty allowlist => the remote IP must match at least one entry, else deny.
    ///
    /// ROBUSTNESS:
    ///   - Never throws. A malformed allowlist entry is skipped (treated as non-matching).
    ///   - A malformed/blank remote IP against a non-empty allowlist fails CLOSED (deny).
    ///   - IPv4-mapped IPv6 remotes (::ffff:a.b.c.d) are normalized to IPv4 before compare,
    ///     so an IPv4 allowlist still matches a remote that arrived mapped.
    /// </summary>
    public static class IpAllowlist
    {
        /// <summary>
        /// Returns true if <paramref name="remoteIp"/> is permitted by <paramref name="allowed"/>.
        /// Empty/null allowlist allows all. Never throws.
        /// </summary>
        public static bool IsAllowed(string remoteIp, IEnumerable<string> allowed)
        {
            // No allowlist configured => feature off => allow all.
            if (allowed == null) return true;

            var entries = allowed.Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
            if (entries.Count == 0) return true;

            // Allowlist present but remote IP unparseable => fail closed.
            if (!TryParseIp(remoteIp, out var remote)) return false;

            foreach (var entry in entries)
            {
                try
                {
                    if (MatchesEntry(remote, entry.Trim())) return true;
                }
                catch
                {
                    // Malformed entry — skip it, keep checking the rest.
                }
            }

            return false;
        }

        // ─── Matching ──────────────────────────────────────────────────────

        private static bool MatchesEntry(IPAddress remote, string entry)
        {
            int slash = entry.IndexOf('/');
            if (slash < 0)
            {
                // Plain address entry — exact match.
                return TryParseIp(entry, out var single)
                    && AddressesEqual(remote, single);
            }

            // CIDR entry — "network/prefixLength".
            var networkPart = entry.Substring(0, slash);
            var prefixPart = entry.Substring(slash + 1);

            if (!TryParseIp(networkPart, out var network)) return false;
            if (!int.TryParse(prefixPart, out var prefixLen)) return false;

            // Families must match (don't test an IPv4 remote against an IPv6 range).
            if (remote.AddressFamily != network.AddressFamily) return false;

            var remoteBytes = remote.GetAddressBytes();
            var networkBytes = network.GetAddressBytes();
            if (remoteBytes.Length != networkBytes.Length) return false;

            int totalBits = remoteBytes.Length * 8;
            if (prefixLen < 0 || prefixLen > totalBits) return false;

            return PrefixMatches(remoteBytes, networkBytes, prefixLen);
        }

        /// <summary>Compares the first <paramref name="prefixLen"/> bits of two equal-length addresses.</summary>
        private static bool PrefixMatches(byte[] a, byte[] b, int prefixLen)
        {
            int fullBytes = prefixLen / 8;
            int remainderBits = prefixLen % 8;

            for (int i = 0; i < fullBytes; i++)
                if (a[i] != b[i]) return false;

            if (remainderBits == 0) return true;

            int mask = (byte)(0xFF << (8 - remainderBits));
            return (a[fullBytes] & mask) == (b[fullBytes] & mask);
        }

        private static bool AddressesEqual(IPAddress x, IPAddress y)
        {
            if (x.AddressFamily != y.AddressFamily) return false;
            var bx = x.GetAddressBytes();
            var by = y.GetAddressBytes();
            if (bx.Length != by.Length) return false;
            for (int i = 0; i < bx.Length; i++)
                if (bx[i] != by[i]) return false;
            return true;
        }

        // ─── Parsing ───────────────────────────────────────────────────────

        /// <summary>
        /// Parses an IP, normalizing IPv4-mapped IPv6 (::ffff:a.b.c.d) down to plain IPv4
        /// so an IPv4 allowlist matches a mapped remote.
        /// </summary>
        private static bool TryParseIp(string value, out IPAddress address)
        {
            address = null;
            if (string.IsNullOrWhiteSpace(value)) return false;

            // Strip an optional :port only for unambiguous IPv4 "a.b.c.d:port".
            // (Leave IPv6 untouched; bracketed forms are uncommon for remote-addr.)
            var trimmed = value.Trim();

            if (!IPAddress.TryParse(trimmed, out var parsed)) return false;

            if (parsed.AddressFamily == AddressFamily.InterNetworkV6 && parsed.IsIPv4MappedToIPv6)
            {
                try { parsed = parsed.MapToIPv4(); }
                catch { /* keep original on failure */ }
            }

            address = parsed;
            return true;
        }
    }
}
