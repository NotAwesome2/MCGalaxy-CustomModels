using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;

namespace MCGalaxy {
    public sealed partial class CustomModelsPlugin {
        class Memoizer1<TKey, TValue> {
            private Func<TKey, TValue> fetch;
            private TimeSpan? cacheLifeTime;
            private Func<Exception, TValue> exceptionHandler;

            public Memoizer1(
                Func<TKey, TValue> fetch,
                TimeSpan? cacheLifeTime = null,
                Func<Exception, TValue> exceptionHandler = null
            ) {
                this.fetch = fetch;
                this.cacheLifeTime = cacheLifeTime;
                this.exceptionHandler = exceptionHandler;
            }

            private struct CacheEntry {
                public TValue value;
                public System.Timers.Timer deathTimer;
            }

            // we want to lock per key instead of for all access on the cache directory
            private ConcurrentDictionary<TKey, object> cacheLocks = new ConcurrentDictionary<TKey, object>();
            private object GetCacheLock(TKey key) {
                return cacheLocks.GetOrAdd(key, (_) => new object());
            }

            private ConcurrentDictionary<TKey, CacheEntry> cache = new ConcurrentDictionary<TKey, CacheEntry>();
            public TValue Get(TKey key) {
                lock (GetCacheLock(key)) {
                    CacheEntry entry;
                    if (cache.TryGetValue(key, out entry)) {
                        Debug("Memoizer1 Hit {0}", key);
                        return entry.value;
                    }

                    TValue value = Fetch(key);
                    entry.value = value;
                    if (cacheLifeTime.HasValue) {
                        var timer = new System.Timers.Timer(cacheLifeTime.Value.TotalMilliseconds);
                        timer.AutoReset = false;
                        timer.Elapsed += (obj, elapsedEventArgs) => {
                            timer.Stop();
                            timer.Dispose();

                            Debug("Memoizer1 Removing {0}", key);
                            lock (GetCacheLock(key)) {
                                cache.TryRemove(key, out _);
                            }
                        };
                        timer.Start();
                        entry.deathTimer = timer;
                    }
                    cache.TryAdd(key, entry);

                    return value;
                }
            }

            public bool GetCached(TKey key, out TValue value) {
                CacheEntry entry;
                if (cache.TryGetValue(key, out entry)) {
                    value = entry.value;
                    return true;
                }
                value = default(TValue);
                return false;
            }

            public TValue Fetch(TKey key) {
                TValue ret;
                bool threw = false;
                var stopwatch = Stopwatch.StartNew();
                try {
                    ret = this.fetch.Invoke(key);
                    stopwatch.Stop();
                } catch (Exception ex) {
                    stopwatch.Stop();
                    threw = true;
                    if (this.exceptionHandler != null) {
                        ret = this.exceptionHandler.Invoke(ex);
                    } else {
                        throw ex;
                    }
                } finally {
                    Debug("Memoizer1 Fetch {0} took {1}s" + (threw ? " (threw)" : ""), key, stopwatch.Elapsed.TotalSeconds);
                }

                return ret;
            }

            public void InvalidateAll() {
                foreach (var key in cache.Keys.ToArray()) {
                    Invalidate(key);
                }
            }

            public void Invalidate(TKey key) {
                lock (GetCacheLock(key)) {
                    CacheEntry entry;
                    if (cache.TryRemove(key, out entry)) {
                        if (entry.deathTimer != null) {
                            entry.deathTimer.Stop();
                            entry.deathTimer.Dispose();
                        }
                    }
                }
            }
        }

    } // class CustomModelsPlugin
} // namespace MCGalaxy
