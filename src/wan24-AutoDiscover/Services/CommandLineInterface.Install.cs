using System.Runtime.InteropServices;
using wan24.Core;

namespace wan24.AutoDiscover.Services
{
    // Installation of an extracted update
    public sealed partial class CommandLineInterface
    {
        /// <summary>
        /// File extension of the backup of an overwritten file (the backup is stored next to the source file of the update)
        /// </summary>
        internal const string BACKUP_EXTENSION = ".backup";

        /// <summary>
        /// Install the files of an extracted update (an existing target file is backed up to <c>&lt;source file&gt;</c> +
        /// <see cref="BACKUP_EXTENSION"/> before it's replaced; files are replaced by renaming a copy, so loaded assemblies of the running process
        /// stay intact; on any error all installed files are rolled back and the error is re-thrown)
        /// </summary>
        /// <param name="sourceFolder">Folder which contains the extracted update (receives the backup files)</param>
        /// <param name="targetFolder">Installation folder</param>
        /// <param name="cancellationToken">Cancellation token</param>
        internal static void InstallFiles(string sourceFolder, string targetFolder, CancellationToken cancellationToken = default)
        {
            // The file list is enumerated completely before any backup file is created in the source folder (Directory.GetFiles returns an array)
            string[] files = Directory.GetFiles(sourceFolder, "*", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.Ordinal);
            // Installed target files (paired with their backup file, if a file was overwritten) and created folders are stored for the rollback
            List<(string Target, string? Backup)> installed = new(files.Length);
            List<string> createdFolders = [];
            try
            {
                foreach (string file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string targetFile = Path.Join(targetFolder, Path.GetRelativePath(sourceFolder, file)),
                        targetDir = Path.GetDirectoryName(targetFile)!;
                    if (!Directory.Exists(targetDir))
                    {
                        // Remember the top-most missing folder, which removes all folders created for this file during a rollback
                        string topMostMissing = targetDir;
                        for (string? parent = Path.GetDirectoryName(targetDir); parent is not null && !Directory.Exists(parent); parent = Path.GetDirectoryName(parent))
                            topMostMissing = parent;
                        if (Logging.Trace)
                            Logging.WriteTrace($"Create folder \"{targetDir}\"");
                        Directory.CreateDirectory(targetDir);
                        createdFolders.Add(topMostMissing);
                    }
                    if (File.Exists(targetFile))
                    {
                        string backupFile = file + BACKUP_EXTENSION;
                        if (Logging.Trace)
                            Logging.WriteTrace($"Create backup of existing file \"{targetFile}\" to \"{backupFile}\"");
                        File.Copy(targetFile, backupFile, overwrite: false);
                        installed.Add((targetFile, backupFile));
                    }
                    else
                    {
                        installed.Add((targetFile, null));
                    }
                    if (Logging.Trace)
                        Logging.WriteTrace($"Copy file \"{file}\" to \"{targetFile}\"");
                    ReplaceFile(file, targetFile);
                }
            }
            catch
            {
                RollbackInstallation(CollectionsMarshal.AsSpan(installed), CollectionsMarshal.AsSpan(createdFolders));
                throw;
            }
        }

        /// <summary>
        /// Roll back an installation (restores overwritten files from their backup, deletes created files and folders; errors are logged and don't
        /// stop the rollback)
        /// </summary>
        /// <param name="installed">Installed target files and their backup files (borrowed for the call)</param>
        /// <param name="createdFolders">Created folders (borrowed for the call)</param>
        private static void RollbackInstallation(ReadOnlySpan<(string Target, string? Backup)> installed, ReadOnlySpan<string> createdFolders)
        {
            Logging.WriteWarning("Restoring the previous installation during rollback of a failed upgrade");
            for (int i = installed.Length - 1; i >= 0; i--)
            {
                (string target, string? backup) = installed[i];
                try
                {
                    if (backup is not null)
                    {
                        if (File.Exists(backup))
                        {
                            Logging.WriteWarning($"Restoring \"{target}\" from backup \"{backup}\"");
                            ReplaceFile(backup, target);
                        }
                        else
                        {
                            Logging.WriteWarning($"Backup \"{backup}\" of \"{target}\" not found");
                        }
                    }
                    else if (File.Exists(target))
                    {
                        Logging.WriteWarning($"Deleting installed file \"{target}\"");
                        File.Delete(target);
                    }
                }
                catch (Exception ex)
                {
                    Logging.WriteError($"Failed to restore \"{target}\" during rollback: {ex}");
                }
            }
            for (int i = createdFolders.Length - 1; i >= 0; i--)
            {
                string folder = createdFolders[i];
                try
                {
                    if (Directory.Exists(folder))
                    {
                        Logging.WriteWarning($"Deleting created folder \"{folder}\"");
                        Directory.Delete(folder, recursive: true);
                    }
                }
                catch (Exception ex)
                {
                    Logging.WriteError($"Failed to delete folder \"{folder}\" during rollback: {ex}");
                }
            }
            Logging.WriteInfo("Rollback of the failed upgrade done");
        }

        /// <summary>
        /// Replace or create a file (the content is copied to a temporary file next to the target, which is then renamed to the target name: a
        /// process which has the previous file opened or mapped - like the running upgrader with its loaded assemblies - keeps the previous content)
        /// </summary>
        /// <param name="source">Source file path</param>
        /// <param name="target">Target file path</param>
        private static void ReplaceFile(string source, string target)
        {
            // The temporary file is in the target folder, so the rename doesn't cross file systems
            string temp = $"{target}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.Copy(source, temp, overwrite: false);
                File.Move(temp, target, overwrite: true);
            }
            catch
            {
                try
                {
                    File.Delete(temp);
                }
                catch (Exception ex)
                {
                    Logging.WriteWarning($"Failed to delete temporary file \"{temp}\": {ex}");
                }
                throw;
            }
        }
    }
}
