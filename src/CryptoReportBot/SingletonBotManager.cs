using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace CryptoReportBot
{
    /// <summary>
    /// Ensures only one instance of the bot can run at a time using file-based locking
    /// </summary>
    public class SingletonBotManager : IDisposable
    {
        private readonly ILogger<SingletonBotManager> _logger;
        private readonly string _lockFilePath;
        private FileStream? _lockFileStream;
        private bool _disposed = false;

        public SingletonBotManager(ILogger<SingletonBotManager> logger)
        {
            _logger = logger;
            // Use a lock file in the temp directory
            _lockFilePath = Path.Combine(Path.GetTempPath(), "cryptoreportbot.lock");
        }

        /// <summary>
        /// Attempts to acquire exclusive lock for the bot instance
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if lock acquired successfully, false if another instance is running</returns>
        public async Task<bool> TryAcquireLockAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Attempting to acquire singleton lock at: {LockFilePath}", _lockFilePath);

                // Try to create and lock the file exclusively
                _lockFileStream = new FileStream(
                    _lockFilePath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None, // Exclusive access
                    bufferSize: 1,
                    FileOptions.DeleteOnClose); // Auto-cleanup when disposed

                // Write process info to the lock file
                var processInfo = $"PID: {Environment.ProcessId}, Started: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC, Machine: {Environment.MachineName}";
                var processInfoBytes = System.Text.Encoding.UTF8.GetBytes(processInfo);
                await _lockFileStream.WriteAsync(processInfoBytes, 0, processInfoBytes.Length, cancellationToken);
                await _lockFileStream.FlushAsync(cancellationToken);

                _logger.LogInformation("✅ Singleton lock acquired successfully. Process info: {ProcessInfo}", processInfo);
                return true;
            }
            catch (IOException ex) when (ex.Message.Contains("being used by another process") || 
                                        ex.Message.Contains("sharing violation"))
            {
                _logger.LogWarning("❌ Another bot instance is already running (lock file in use): {Error}", ex.Message);
                
                // Try to read the lock file to see what process is using it
                try
                {
                    await Task.Delay(100, cancellationToken); // Brief delay before reading
                    var lockFileContent = await File.ReadAllTextAsync(_lockFilePath, cancellationToken);
                    _logger.LogInformation("Existing lock file content: {LockFileContent}", lockFileContent);
                }
                catch (Exception readEx)
                {
                    _logger.LogWarning("Could not read lock file content: {ReadError}", readEx.Message);
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while acquiring singleton lock");
                return false;
            }
        }

        /// <summary>
        /// Waits for any existing instance to release the lock, then acquires it
        /// </summary>
        /// <param name="maxWaitTime">Maximum time to wait for lock</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if lock acquired, false if timeout</returns>
        public async Task<bool> WaitAndAcquireLockAsync(TimeSpan maxWaitTime, CancellationToken cancellationToken = default)
        {
            var endTime = DateTime.UtcNow.Add(maxWaitTime);
            var retryDelay = TimeSpan.FromSeconds(2);

            _logger.LogInformation("Waiting up to {MaxWaitTime} seconds for singleton lock...", maxWaitTime.TotalSeconds);

            while (DateTime.UtcNow < endTime && !cancellationToken.IsCancellationRequested)
            {
                if (await TryAcquireLockAsync(cancellationToken))
                {
                    return true;
                }

                _logger.LogInformation("Lock not available, retrying in {RetryDelay} seconds...", retryDelay.TotalSeconds);
                await Task.Delay(retryDelay, cancellationToken);
            }

            _logger.LogError("❌ Timeout waiting for singleton lock after {MaxWaitTime} seconds", maxWaitTime.TotalSeconds);
            return false;
        }

        /// <summary>
        /// Forces cleanup of any stale lock files (use with caution)
        /// </summary>
        public void ForceCleanup()
        {
            try
            {
                if (File.Exists(_lockFilePath))
                {
                    File.Delete(_lockFilePath);
                    _logger.LogWarning("🧹 Force cleaned up stale lock file: {LockFilePath}", _lockFilePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to force cleanup lock file");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                _lockFileStream?.Dispose(); // This will delete the file due to DeleteOnClose
                _logger.LogInformation("🔓 Singleton lock released");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing singleton lock");
            }

            _disposed = true;
        }
    }
}
