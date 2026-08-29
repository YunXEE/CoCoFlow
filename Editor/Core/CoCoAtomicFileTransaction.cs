#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;

namespace CoCoFlow.Editor.Core
{
    internal static class CoCoAtomicFileTransaction
    {
        internal static bool TryReplaceUtf8(
            string path,
            string content,
            Func<string, bool> validator,
            out string backupPath,
            out string error)
        {
            backupPath = string.Empty;
            error = string.Empty;
            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(path) ||
                string.IsNullOrEmpty(directory) ||
                validator == null)
            {
                error = "Atomic file replacement requires a path, directory, and validator.";
                return false;
            }

            Directory.CreateDirectory(directory);
            string token = Guid.NewGuid().ToString("N");
            string temporaryPath = Path.Combine(
                directory,
                Path.GetFileName(path) + "." + token + ".tmp");
            string requestedBackupPath = Path.Combine(
                directory,
                Path.GetFileName(path) + "." + token + ".bak");

            try
            {
                using (var stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None))
                using (var writer = new StreamWriter(
                           stream,
                           new UTF8Encoding(false)))
                {
                    writer.Write(content ?? string.Empty);
                }

                string verified = File.ReadAllText(temporaryPath, Encoding.UTF8);
                if (!validator(verified))
                {
                    error = "The staged replacement failed validation.";
                    return false;
                }

                if (File.Exists(path))
                {
                    File.Replace(temporaryPath, path, requestedBackupPath);
                    backupPath = requestedBackupPath;
                }
                else
                {
                    File.Move(temporaryPath, path);
                }

                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }
}
#endif
