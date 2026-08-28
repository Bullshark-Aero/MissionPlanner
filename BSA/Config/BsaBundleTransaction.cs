using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using MissionPlanner.BSA.Core;
using MissionPlanner.Warnings;
using Newtonsoft.Json;

namespace MissionPlanner.BSA.Config
{
    public enum BsaTransactionStatus { Prepared, Applying, PendingRestart, Verified, Committed, RolledBack }

    public class BsaTransactionJournal
    {
        public string TransactionId { get; set; }
        public string PackageId { get; set; }
        public string PackageVersion { get; set; }
        public string PackageSha256 { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public BsaTransactionStatus Status { get; set; }
        public List<BsaTransactionFile> Files { get; set; } = new List<BsaTransactionFile>();
        public string SettingsBackupPath { get; set; }
        public string SettingsFilePath { get; set; }
        public string SettingsFileBackupPath { get; set; }
        public bool SettingsFileExisted { get; set; }
        public string InstallStatePath { get; set; }
        public Dictionary<string, string> ExpectedHashes { get; set; } = new Dictionary<string, string>();
        public string Failure { get; set; }
    }

    public class BsaTransactionFile
    {
        public string TargetPath { get; set; }
        public bool Existed { get; set; }
        public string BackupPath { get; set; }
        public bool DeleteOnCommit { get; set; }
    }

    public class BsaBundleApplyOptions
    {
        public bool InstallChecklist { get; set; }
        public bool InstallKeyPolicy { get; set; }
        public bool InstallLockPolicy { get; set; }
    }

    public class BsaBundleApplyResult
    {
        public string TransactionId { get; set; }
        public string TransactionDirectory { get; set; }
        public BsaTransactionStatus Status { get; set; }
        public IReadOnlyList<string> ChangedSettings { get; set; }
        public int PreservedUnrelatedWarnings { get; set; }
        public bool RestartRequired { get; set; }
        public bool NoChangesRequired { get; set; }
    }

    /// <summary>Stages, snapshots, commits, and compensates every bundle-owned state change.</summary>
    public static class BsaBundleTransaction
    {
        const string JournalName = "journal.json";

        public static BsaBundleApplyResult Apply(ConfigPackageContents package,
            IDictionary<string, string> liveConfig, IEnumerable<string> approvedKeys, KeyPolicyConfig policy,
            IEnumerable<CustomWarning> liveWarnings, Action saveSettings, string warningPath,
            string bsaConfigDirectory, string transactionsDirectory, string pluginDirectory,
            BsaBundleApplyOptions options, string settingsFilePath = null, Action<string> checkpoint = null)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            if (liveConfig == null) throw new ArgumentNullException(nameof(liveConfig));
            if (saveSettings == null) throw new ArgumentNullException(nameof(saveSettings));
            if (package.HasCompleteCoreProfile == false && !package.IsLegacy)
                throw new InvalidDataException("A schema-v2 operational bundle must contain the complete core profile.");

            var existing = FindCommittedInstallation(package, transactionsDirectory,
                Path.Combine(Path.GetDirectoryName(bsaConfigDirectory), "install-state.json"));
            if (existing != null)
                return new BsaBundleApplyResult
                {
                    TransactionId = existing.TransactionId,
                    TransactionDirectory = Path.GetDirectoryName(existing.JournalPath),
                    Status = BsaTransactionStatus.Committed,
                    ChangedSettings = new List<string>(),
                    RestartRequired = false,
                    NoChangesRequired = true
                };

            var transactionId = DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var root = Path.Combine(transactionsDirectory, transactionId);
            var staged = Path.Combine(root, "staged");
            var backups = Path.Combine(root, "backups");
            Directory.CreateDirectory(staged);
            Directory.CreateDirectory(backups);

            var journal = new BsaTransactionJournal
            {
                TransactionId = transactionId,
                PackageId = package.Manifest.PackageId,
                PackageVersion = package.Manifest.PackageVersion,
                PackageSha256 = package.PackageSha256,
                CreatedAtUtc = DateTime.UtcNow,
                Status = BsaTransactionStatus.Prepared,
                SettingsBackupPath = Path.Combine(backups, "settings.json"),
                SettingsFilePath = settingsFilePath,
                SettingsFileBackupPath = Path.Combine(backups, "settings-file.bin")
            };
            var beforeSettings = new Dictionary<string, string>(liveConfig, StringComparer.Ordinal);
            File.WriteAllText(journal.SettingsBackupPath, JsonConvert.SerializeObject(beforeSettings, Formatting.Indented));
            if (!string.IsNullOrWhiteSpace(settingsFilePath))
            {
                journal.SettingsFileExisted = File.Exists(settingsFilePath);
                if (journal.SettingsFileExisted) File.Copy(settingsFilePath, journal.SettingsFileBackupPath, false);
            }

            var targets = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            BsaWarningMergeResult warningMerge = null;
            if (package.Warnings != null)
            {
                var ownershipPath = Path.Combine(bsaConfigDirectory, "warning-ownership.json");
                warningMerge = BsaWarningProfileAdapter.Merge(liveWarnings, package.Warnings,
                    BsaWarningProfileAdapter.ReadOwnership(ownershipPath));
                if (warningMerge.Conflicts.Count > 0)
                    throw new InvalidDataException(string.Join(" ", warningMerge.Conflicts));
                targets[warningPath] = BsaWarningProfileAdapter.SerializeWarnings(warningMerge.Warnings);
                targets[ownershipPath] = BsaWarningProfileAdapter.SerializeOwnership(warningMerge.Ownership);
                targets[Path.Combine(bsaConfigDirectory, "active-health-rules.json")] = Utf8Json(package.HealthRules);
            }
            AddOptionalTarget(targets, package.ChecklistJson, options?.InstallChecklist == true,
                Path.Combine(bsaConfigDirectory, BsaConfigInstaller.ChecklistFileName));
            AddOptionalTarget(targets, package.KeyPolicyJson, options?.InstallKeyPolicy == true,
                Path.Combine(bsaConfigDirectory, BsaConfigInstaller.KeyPolicyFileName));
            AddOptionalTarget(targets, package.LockPolicyJson, options?.InstallLockPolicy == true,
                Path.Combine(bsaConfigDirectory, BsaConfigInstaller.LockPolicyFileName));
            StagePluginTargets(package, targets, pluginDirectory);

            var deleteTargets = new List<string>();
            if (options?.InstallLockPolicy == true)
                deleteTargets.Add(Path.Combine(bsaConfigDirectory, BsaConfigInstaller.LockPolicyFileName) + ".hash");

            var installStatePath = Path.Combine(Path.GetDirectoryName(bsaConfigDirectory), "install-state.json");
            journal.InstallStatePath = installStatePath;
            targets[installStatePath] = Utf8Json(new
            {
                packageId = package.Manifest.PackageId,
                packageVersion = package.Manifest.PackageVersion,
                packageSha256 = package.PackageSha256,
                transactionId,
                installedAtUtc = DateTime.UtcNow,
                verification = "pending-restart"
            });

            foreach (var target in targets)
            {
                var backup = Path.Combine(backups, journal.Files.Count.ToString("D3") + ".bin");
                var existed = File.Exists(target.Key);
                if (existed) File.Copy(target.Key, backup, false);
                journal.Files.Add(new BsaTransactionFile { TargetPath = target.Key, Existed = existed, BackupPath = backup });
                var stagePath = Path.Combine(staged, journal.Files.Count.ToString("D3") + ".bin");
                File.WriteAllBytes(stagePath, target.Value);
                journal.ExpectedHashes[target.Key] = BsaHash.ComputeSha256Hex(target.Value);
            }
            foreach (var target in deleteTargets)
            {
                var backup = Path.Combine(backups, journal.Files.Count.ToString("D3") + ".bin");
                var existed = File.Exists(target);
                if (existed) File.Copy(target, backup, false);
                journal.Files.Add(new BsaTransactionFile
                {
                    TargetPath = target,
                    Existed = existed,
                    BackupPath = backup,
                    DeleteOnCommit = true
                });
            }
            WriteJournal(root, journal);

            List<string> changed = null;
            try
            {
                journal.Status = BsaTransactionStatus.Applying;
                WriteJournal(root, journal);
                changed = BsaConfigImporter.Apply(liveConfig, package, approvedKeys ?? Enumerable.Empty<string>(), policy);
                saveSettings();
                checkpoint?.Invoke("settings-saved");
                if (!string.IsNullOrWhiteSpace(settingsFilePath) && File.Exists(settingsFilePath))
                    journal.ExpectedHashes[settingsFilePath] = BsaHash.HashFile(settingsFilePath);

                var stageIndex = 1;
                for (var index = 0; index < journal.Files.Count; index++)
                {
                    var file = journal.Files[index];
                    if (file.DeleteOnCommit)
                    {
                        if (File.Exists(file.TargetPath)) File.Delete(file.TargetPath);
                    }
                    else
                    {
                        AtomicWrite(file.TargetPath, File.ReadAllBytes(Path.Combine(staged, stageIndex.ToString("D3") + ".bin")));
                        stageIndex++;
                    }
                    checkpoint?.Invoke("file-committed:" + index);
                }
                VerifyHashes(journal);
                journal.Status = BsaTransactionStatus.PendingRestart;
                WriteJournal(root, journal);
                return new BsaBundleApplyResult
                {
                    TransactionId = transactionId,
                    TransactionDirectory = root,
                    Status = journal.Status,
                    ChangedSettings = changed,
                    PreservedUnrelatedWarnings = warningMerge?.PreservedUnrelatedCount ?? 0,
                    RestartRequired = true
                };
            }
            catch (Exception ex)
            {
                journal.Failure = ex.Message;
                try
                {
                    RestoreFiles(journal);
                    RestoreSettings(journal, liveConfig, saveSettings);
                    journal.Status = BsaTransactionStatus.RolledBack;
                    WriteJournal(root, journal);
                }
                catch (Exception rollback)
                {
                    journal.Failure += " Rollback also failed: " + rollback.Message;
                    WriteJournal(root, journal);
                    throw new AggregateException("Bundle apply and rollback both failed.", ex, rollback);
                }
                throw new InvalidOperationException("Bundle transaction rolled back after apply failed: " + ex.Message, ex);
            }
        }

        public static void RecoverAndVerify(string transactionsDirectory, IDictionary<string, string> liveConfig, Action saveSettings)
        {
            if (!Directory.Exists(transactionsDirectory)) return;
            foreach (var journalPath in Directory.GetFiles(transactionsDirectory, JournalName, SearchOption.AllDirectories))
            {
                var journal = JsonConvert.DeserializeObject<BsaTransactionJournal>(File.ReadAllText(journalPath));
                if (journal == null) continue;
                if (journal.Status == BsaTransactionStatus.Applying)
                {
                    RestoreFiles(journal);
                    RestoreSettings(journal, liveConfig, saveSettings);
                    journal.Status = BsaTransactionStatus.RolledBack;
                    journal.Failure = "Recovered an interrupted apply at startup.";
                    WriteJournal(Path.GetDirectoryName(journalPath), journal);
                }
                else if (journal.Status == BsaTransactionStatus.PendingRestart)
                {
                    try
                    {
                        VerifyHashes(journal);
                        journal.Status = BsaTransactionStatus.Verified;
                        WriteJournal(Path.GetDirectoryName(journalPath), journal);
                        MarkInstallStateCommitted(journal);
                        journal.Status = BsaTransactionStatus.Committed;
                        WriteJournal(Path.GetDirectoryName(journalPath), journal);
                    }
                    catch (Exception ex)
                    {
                        RestoreFiles(journal);
                        RestoreSettings(journal, liveConfig, saveSettings);
                        journal.Status = BsaTransactionStatus.RolledBack;
                        journal.Failure = "Startup verification failed: " + ex.Message;
                        WriteJournal(Path.GetDirectoryName(journalPath), journal);
                    }
                }
            }
        }

        static void RestoreSettings(BsaTransactionJournal journal, IDictionary<string, string> liveConfig, Action saveSettings)
        {
            if (liveConfig == null || saveSettings == null || !File.Exists(journal.SettingsBackupPath)) return;
            var before = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(journal.SettingsBackupPath));
            liveConfig.Clear();
            foreach (var item in before) liveConfig[item.Key] = item.Value;
            saveSettings();
            RestoreExactSettingsFile(journal);
        }

        static void StagePluginTargets(ConfigPackageContents package, IDictionary<string, byte[]> targets, string pluginDirectory)
        {
            if (package.Plugins.Count == 0) return;
            using (var archive = ZipFile.OpenRead(package.SourcePath))
            {
                foreach (var plugin in package.Plugins)
                {
                    var entry = archive.GetEntry(plugin.PayloadPath) ?? throw new InvalidDataException("Plugin payload is missing.");
                    using (var input = entry.Open())
                    using (var output = new MemoryStream())
                    {
                        input.CopyTo(output);
                        targets[Path.Combine(pluginDirectory, plugin.PluginId + ".dll")] = output.ToArray();
                    }
                }
            }
        }

        static void AddOptionalTarget(IDictionary<string, byte[]> targets, string content, bool selected, string path)
        {
            if (selected && content != null) targets[path] = Encoding.UTF8.GetBytes(content);
        }

        static byte[] Utf8Json(object value) => Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value, Formatting.Indented));

        static void AtomicWrite(string target, byte[] bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            var temp = target + ".bsa-" + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temp, bytes);
            try
            {
                if (File.Exists(target)) File.Replace(temp, target, null);
                else File.Move(temp, target);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        static void RestoreFiles(BsaTransactionJournal journal)
        {
            foreach (var file in journal.Files.AsEnumerable().Reverse())
            {
                if (file.Existed) AtomicWrite(file.TargetPath, File.ReadAllBytes(file.BackupPath));
                else if (File.Exists(file.TargetPath)) File.Delete(file.TargetPath);
            }
        }

        static void RestoreExactSettingsFile(BsaTransactionJournal journal)
        {
            if (string.IsNullOrWhiteSpace(journal.SettingsFilePath)) return;
            if (journal.SettingsFileExisted)
                AtomicWrite(journal.SettingsFilePath, File.ReadAllBytes(journal.SettingsFileBackupPath));
            else if (File.Exists(journal.SettingsFilePath))
                File.Delete(journal.SettingsFilePath);
        }

        static void MarkInstallStateCommitted(BsaTransactionJournal journal)
        {
            if (string.IsNullOrWhiteSpace(journal.InstallStatePath) || !File.Exists(journal.InstallStatePath))
                throw new InvalidDataException("Bundle install state is missing.");
            var state = JsonConvert.DeserializeObject<BsaInstallState>(File.ReadAllText(journal.InstallStatePath));
            if (state == null || state.TransactionId != journal.TransactionId)
                throw new InvalidDataException("Bundle install state does not match its transaction.");
            state.Verification = "committed";
            state.VerifiedAtUtc = DateTime.UtcNow;
            var bytes = Utf8Json(state);
            AtomicWrite(journal.InstallStatePath, bytes);
            journal.ExpectedHashes[journal.InstallStatePath] = BsaHash.ComputeSha256Hex(bytes);
        }

        sealed class BsaInstallState
        {
            public string PackageId { get; set; }
            public string PackageVersion { get; set; }
            public string PackageSha256 { get; set; }
            public string TransactionId { get; set; }
            public DateTime InstalledAtUtc { get; set; }
            public string Verification { get; set; }
            public DateTime? VerifiedAtUtc { get; set; }
        }

        sealed class CommittedInstallation
        {
            public string TransactionId { get; set; }
            public string JournalPath { get; set; }
        }

        static CommittedInstallation FindCommittedInstallation(ConfigPackageContents package,
            string transactionsDirectory, string installStatePath)
        {
            if (string.IsNullOrWhiteSpace(package.PackageSha256) || !File.Exists(installStatePath)) return null;
            try
            {
                var state = JsonConvert.DeserializeObject<BsaInstallState>(File.ReadAllText(installStatePath));
                if (state == null || state.Verification != "committed" || state.PackageSha256 != package.PackageSha256)
                    return null;
                var journalPath = Path.Combine(transactionsDirectory, state.TransactionId, JournalName);
                if (!File.Exists(journalPath)) return null;
                var journal = JsonConvert.DeserializeObject<BsaTransactionJournal>(File.ReadAllText(journalPath));
                if (journal == null || journal.Status != BsaTransactionStatus.Committed) return null;
                VerifyHashes(journal);
                return new CommittedInstallation { TransactionId = state.TransactionId, JournalPath = journalPath };
            }
            catch
            {
                return null;
            }
        }

        static void VerifyHashes(BsaTransactionJournal journal)
        {
            foreach (var expected in journal.ExpectedHashes)
                if (!File.Exists(expected.Key) || BsaHash.HashFile(expected.Key) != expected.Value)
                    throw new InvalidDataException("Installed file failed verification: " + expected.Key);
        }

        static void WriteJournal(string root, BsaTransactionJournal journal)
        {
            Directory.CreateDirectory(root);
            AtomicWrite(Path.Combine(root, JournalName), Utf8Json(journal));
        }
    }
}
