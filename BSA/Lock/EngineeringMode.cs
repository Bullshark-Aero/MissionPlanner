using System;
using System.Text;
using MissionPlanner.Utilities;

namespace MissionPlanner.BSA.Lock
{
    /// <summary>
    /// Gates lock-policy editing and (if a future policy ever uses the class) AUTHORISE-class actions.
    /// Reuses the salted-hash primitives from the app's existing password gate
    /// (ExtLibs/Utilities/Password.cs's GenerateSaltedHash/CompareByteArrays - both already
    /// key-agnostic, taking arbitrary bytes) but stores under its own dedicated Settings keys, not
    /// Password.cs's hardcoded "password" key - user-approved decision: knowing the app's general
    /// config-screen password must never also grant override authority over BSA safety BLOCKs.
    /// </summary>
    public static class EngineeringMode
    {
        const string PasswordSettingKey = "bsa_engineering_password";
        const string PasswordSetSettingKey = "bsa_engineering_password_set";
        static readonly byte[] Salt = { (byte)'B', (byte)'S', (byte)'A' };

        public static bool IsConfigured => Settings.Instance.GetBoolean(PasswordSetSettingKey, false);

        public static void SetPassphrase(string passphrase)
        {
            var hash = Password.GenerateSaltedHash(Encoding.UTF8.GetBytes(passphrase ?? ""), Salt);
            Settings.Instance[PasswordSettingKey] = Convert.ToBase64String(hash);
            Settings.Instance[PasswordSetSettingKey] = true.ToString();
        }

        public static bool Verify(string passphrase)
        {
            if (!IsConfigured)
                return false;

            var stored = StoredHash();
            if (stored == null)
                return false;

            var candidate = Password.GenerateSaltedHash(Encoding.UTF8.GetBytes(passphrase ?? ""), Salt);
            return Password.CompareByteArrays(candidate, stored);
        }

        /// <summary>Byte-for-byte the same salted hash Verify() compares candidates against - reused
        /// by LockPolicyIntegrity as HMAC key material so a lock-policy approval stamp requires having
        /// known the Engineering passphrase at some point, not just knowing the hashing algorithm.
        /// Null until a passphrase has been configured at least once. This never exposes (or requires
        /// recovering) the plaintext passphrase - it's already a one-way derivation of it, and it's the
        /// same value this class already persists for its own authentication purpose.</summary>
        public static byte[] DerivedIntegrityKey => IsConfigured ? StoredHash() : null;

        static byte[] StoredHash()
        {
            try
            {
                return Convert.FromBase64String(Settings.Instance[PasswordSettingKey] ?? "");
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
}
