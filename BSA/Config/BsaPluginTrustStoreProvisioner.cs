using System;
using System.IO;

namespace MissionPlanner.BSA.Config
{
    /// <summary>
    /// Seeds the BSA executable-plugin trust store on first use. Existing operator-managed trust
    /// state is authoritative and is never replaced by a shipped default.
    /// </summary>
    public static class BsaPluginTrustStoreProvisioner
    {
        public static string ProvisionFromShippedDefault(string shippedPath, string userPath)
        {
            if (string.IsNullOrWhiteSpace(shippedPath))
                throw new ArgumentException("A shipped plugin trust-store path is required.", nameof(shippedPath));
            if (string.IsNullOrWhiteSpace(userPath))
                throw new ArgumentException("A user plugin trust-store path is required.", nameof(userPath));
            if (File.Exists(userPath))
                return userPath;
            if (!File.Exists(shippedPath))
                throw new FileNotFoundException("The shipped BSA plugin trust store is missing.", shippedPath);

            var directory = Path.GetDirectoryName(userPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            try
            {
                File.Copy(shippedPath, userPath, false);
            }
            catch (IOException) when (File.Exists(userPath))
            {
                // Another process or import window won the first-use race. Its file remains authoritative.
            }

            return userPath;
        }
    }
}
