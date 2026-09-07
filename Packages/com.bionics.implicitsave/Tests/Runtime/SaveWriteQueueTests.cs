using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using ImplicitSave.Storage;
using NUnit.Framework;

namespace ImplicitSave.Tests
{
    /// <summary>
    /// Phase 5's stress criterion: no two writes to the same file at once, however many are queued.
    /// </summary>
    public class SaveWriteQueueTests
    {
        [Test]
        public void WritesHappenOneAtATime()
        {
            // Two writes overlapping on one file is how a save gets corrupted, so the queue has to
            // serialise them no matter how fast they arrive.
            var storage = new ConcurrencyProbeStorage();
            var queue = new SaveWriteQueue(storage);

            for (var i = 0; i < 200; i++)
            {
                queue.Enqueue(0, "items", Encoding.UTF8.GetBytes("write " + i));
            }

            Assert.That(queue.Drain(TimeSpan.FromSeconds(30)), Is.True, "The queue should drain.");
            Assert.That(storage.MaxConcurrent, Is.EqualTo(1),
                $"Saw {storage.MaxConcurrent} writes running at once.");
            Assert.That(storage.Writes, Has.Count.EqualTo(200));
        }

        [Test]
        public void WritesKeepTheirOrder()
        {
            // The last write has to be the one that ends up on disk.
            var storage = new ConcurrencyProbeStorage();
            var queue = new SaveWriteQueue(storage);

            for (var i = 0; i < 50; i++)
            {
                queue.Enqueue(0, "items", Encoding.UTF8.GetBytes(i.ToString()));
            }

            queue.Drain(TimeSpan.FromSeconds(30));

            for (var i = 0; i < 50; i++)
            {
                Assert.That(storage.Writes[i], Is.EqualTo(i.ToString()));
            }
        }

        [Test]
        public void Drain_BlocksUntilEverythingIsWritten()
        {
            var storage = new ConcurrencyProbeStorage { DelayMilliseconds = 2 };
            var queue = new SaveWriteQueue(storage);

            for (var i = 0; i < 20; i++)
            {
                queue.Enqueue(0, "items", Encoding.UTF8.GetBytes("x"));
            }

            queue.Drain(TimeSpan.FromSeconds(30));

            Assert.That(storage.Writes, Has.Count.EqualTo(20),
                "After Drain returns, the data is on disk - that is what the quit hook relies on.");
            Assert.That(queue.Pending, Is.Zero);
        }

        [Test]
        public void AFailedWriteDoesNotStopTheOnesAfterIt()
        {
            var storage = new ConcurrencyProbeStorage { FailOnWrite = 2 };
            var queue = new SaveWriteQueue(storage);
            var failures = new List<SaveException>();
            queue.Failed += failures.Add;

            // A falha simulada e reportada como error, que reprovaria o teste sozinho.
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;

            for (var i = 0; i < 5; i++)
            {
                queue.Enqueue(0, "items", Encoding.UTF8.GetBytes(i.ToString()));
            }

            queue.Drain(TimeSpan.FromSeconds(30));

            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;

            Assert.That(failures, Has.Count.EqualTo(1));
            Assert.That(storage.Writes, Has.Count.EqualTo(4),
                "One write failing must not silently end saving for the rest of the session.");
        }

        /// <summary>Storage that notices whether two writes ever overlap.</summary>
        private sealed class ConcurrencyProbeStorage : ISaveStorage
        {
            private readonly object _gate = new object();
            private int _concurrent;

            internal readonly List<string> Writes = new List<string>();
            internal int MaxConcurrent;
            internal int DelayMilliseconds;
            internal int FailOnWrite = -1;

            private int _attempts;

            public void Write(int profileId, string saveId, byte[] content)
            {
                var running = Interlocked.Increment(ref _concurrent);

                lock (_gate)
                {
                    if (running > MaxConcurrent)
                    {
                        MaxConcurrent = running;
                    }
                }

                try
                {
                    if (DelayMilliseconds > 0)
                    {
                        Thread.Sleep(DelayMilliseconds);
                    }

                    lock (_gate)
                    {
                        // Counted by attempt, not by successful writes: keying off Writes.Count
                        // would make every later write fail too, since the count never advances.
                        if (_attempts++ == FailOnWrite)
                        {
                            throw new SaveStorageException("simulated failure", null);
                        }

                        Writes.Add(Encoding.UTF8.GetString(content));
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref _concurrent);
                }
            }

            public bool Exists(int profileId, string saveId) => false;
            public byte[] Read(int profileId, string saveId) => throw new NotSupportedException();
            public bool TryReadBackup(int profileId, string saveId, out byte[] content)
            {
                content = null;
                return false;
            }

            public string Quarantine(int profileId, string saveId) => null;
            public void Delete(int profileId, string saveId) { }
            public IReadOnlyList<string> ListSaveIds(int profileId) => Array.Empty<string>();
            public bool IndexExists() => false;
            public byte[] ReadIndex() => throw new NotSupportedException();
            public void WriteIndex(byte[] content) { }
            public string QuarantineIndex() => null;
            public IReadOnlyList<int> ListProfileIds() => Array.Empty<int>();
            public bool ProfileExists(int profileId) => false;
            public void DeleteProfile(int profileId) { }
            public void CopyProfile(int sourceProfileId, int destinationProfileId) { }
        }
    }
}
