using System;
using System.Threading.Tasks;

namespace ImplicitSave.Storage
{
    /// <summary>
    /// Runs every write off the main thread, one at a time, in the order they were asked for.
    /// </summary>
    /// <remarks>
    /// Disk IO is the expensive part of saving, and doing it on the main thread is a visible hitch.
    /// Doing it on several threads at once is worse: two writes racing on the same file is exactly
    /// how a save gets corrupted. So the writes go to a background worker, but only ever one at a
    /// time.
    /// <para>
    /// Serialization stays on the main thread and is not this class's business. Save instances may
    /// hold Unity types and the game may mutate them from anywhere, so serializing off-thread would
    /// be a data race.
    /// </para>
    /// </remarks>
    public sealed class SaveWriteQueue
    {
        private readonly ISaveStorage _storage;
        private readonly object _gate = new object();

        private Task _tail = Task.CompletedTask;
        private int _pending;

        /// <param name="storage">Where the bytes go.</param>
        public SaveWriteQueue(ISaveStorage storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        }

        /// <summary>Raised when a queued write fails, on the thread that noticed.</summary>
        public event Action<SaveException> Failed;

        /// <summary>How many writes are queued or running.</summary>
        public int Pending
        {
            get
            {
                lock (_gate)
                {
                    return _pending;
                }
            }
        }

        /// <summary>Queues a write. Returns immediately.</summary>
        public void Enqueue(int profileId, string saveId, byte[] content)
        {
            lock (_gate)
            {
                _pending++;

                // Chaining onto the previous task is what makes this a queue of one worker: the
                // next write cannot start until this one is finished.
                _tail = _tail.ContinueWith(
                    _ => Run(profileId, saveId, content),
                    TaskContinuationOptions.ExecuteSynchronously);
            }
        }

        /// <summary>
        /// Blocks until everything queued has been written.
        /// </summary>
        /// <remarks>
        /// Used by the pause, focus and quit hooks. It blocks rather than awaits on purpose: on
        /// quit there may be no later frame to resume on, and an await that never resumes is the
        /// save that never happened.
        /// </remarks>
        /// <param name="timeout">How long to wait before giving up.</param>
        /// <returns><c>true</c> if the queue drained in time.</returns>
        public bool Drain(TimeSpan timeout)
        {
            Task tail;

            lock (_gate)
            {
                tail = _tail;
            }

            try
            {
                return tail.Wait(timeout);
            }
            catch (AggregateException e)
            {
                ImplicitSaveLog.Error("A queued save failed: " + e.InnerException?.Message);
                return true;
            }
        }

        private void Run(int profileId, string saveId, byte[] content)
        {
            try
            {
                _storage.Write(profileId, saveId, content);
            }
            catch (SaveException e)
            {
                ImplicitSaveLog.Error($"Failed to write '{saveId}' of profile {profileId}: {e.Message}");
                Failed?.Invoke(e);
            }
            catch (Exception e)
            {
                // An unexpected failure here must not kill the worker, or every later save in the
                // session would silently never happen.
                ImplicitSaveLog.Error($"Unexpected failure writing '{saveId}': {e.Message}");
                Failed?.Invoke(new SaveStorageException($"Could not write '{saveId}'.", e));
            }
            finally
            {
                lock (_gate)
                {
                    _pending--;
                }
            }
        }
    }
}
