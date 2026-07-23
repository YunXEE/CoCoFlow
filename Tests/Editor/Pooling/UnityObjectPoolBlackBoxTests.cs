using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.Pool;

namespace CoCoFlow.Runtime.Pooling.Tests
{
    public sealed class UnityObjectPoolBlackBoxTests
    {
        [Test]
        public void DefaultCapacityDoesNotCreateOrPrewarm()
        {
            int createCount = 0;
            using (var pool = new ObjectPool<Token>(
                       () =>
                       {
                           createCount++;
                           return new Token(createCount);
                       },
                       defaultCapacity: 16,
                       maxSize: 16))
            {
                Assert.That(createCount, Is.Zero);
                Assert.That(pool.CountAll, Is.Zero);
                Assert.That(pool.CountInactive, Is.Zero);
            }
        }

        [Test]
        public void MaxSizeDoesNotLimitConcurrentGets()
        {
            int createCount = 0;
            using (var pool = new ObjectPool<Token>(
                       () => new Token(++createCount),
                       maxSize: 1))
            {
                Token first = pool.Get();
                Token second = pool.Get();

                Assert.That(first, Is.Not.SameAs(second));
                Assert.That(createCount, Is.EqualTo(2));
                Assert.That(pool.CountActive, Is.EqualTo(2));

                pool.Release(first);
                pool.Release(second);
            }
        }

        [Test]
        public void GetRunsActionOnGetAfterCreateOrPop()
        {
            var events = new List<string>();
            using (var pool = new ObjectPool<Token>(
                       () =>
                       {
                           events.Add("create");
                           return new Token(1);
                       },
                       token => events.Add("get:" + token.Id),
                       token => events.Add("release:" + token.Id),
                       token => events.Add("destroy:" + token.Id),
                       maxSize: 1))
            {
                Token first = pool.Get();
                pool.Release(first);
                Token second = pool.Get();

                Assert.That(second, Is.SameAs(first));
                CollectionAssert.AreEqual(
                    new[]
                    {
                        "create",
                        "get:1",
                        "release:1",
                        "get:1"
                    },
                    events);
                pool.Release(second);
            }
        }

        [Test]
        public void OverflowRunsReleaseBeforeDestroy()
        {
            var events = new List<string>();
            int createCount = 0;
            using (var pool = new ObjectPool<Token>(
                       () => new Token(++createCount),
                       actionOnRelease: token => events.Add("release:" + token.Id),
                       actionOnDestroy: token => events.Add("destroy:" + token.Id),
                       maxSize: 1))
            {
                Token first = pool.Get();
                Token overflow = pool.Get();
                pool.Release(first);
                pool.Release(overflow);

                CollectionAssert.AreEqual(
                    new[]
                    {
                        "release:1",
                        "release:2",
                        "destroy:2"
                    },
                    events);
                Assert.That(pool.CountInactive, Is.EqualTo(1));
            }
        }

        [Test]
        public void ClearDestroysIdleButResetsAggregateCountsWhileBorrowedTokenLives()
        {
            var destroyed = new List<int>();
            int createCount = 0;
            using (var pool = new ObjectPool<Token>(
                       () => new Token(++createCount),
                       actionOnDestroy: token => destroyed.Add(token.Id),
                       maxSize: 4))
            {
                Token retained = pool.Get();
                Token borrowed = pool.Get();
                pool.Release(retained);

                pool.Clear();

                CollectionAssert.AreEqual(new[] { retained.Id }, destroyed);
                Assert.That(pool.CountInactive, Is.Zero);
                // Unity resets aggregate telemetry even though the borrowed token
                // remains alive. CoCoFlow must keep its own ownership ledger.
                Assert.That(pool.CountAll, Is.Zero);
                Assert.That(pool.CountActive, Is.Zero);
                CollectionAssert.DoesNotContain(destroyed, borrowed.Id);

                Assert.DoesNotThrow(() => pool.Release(borrowed));
                CollectionAssert.DoesNotContain(destroyed, borrowed.Id);
            }
        }

        [Test]
        public void DisposeUsesClearSemantics()
        {
            var destroyed = new List<int>();
            int createCount = 0;
            var pool = new ObjectPool<Token>(
                () => new Token(++createCount),
                actionOnDestroy: token => destroyed.Add(token.Id),
                maxSize: 2);
            Token retained = pool.Get();
            Token borrowed = pool.Get();
            pool.Release(retained);

            pool.Dispose();

            CollectionAssert.AreEqual(new[] { retained.Id }, destroyed);
            CollectionAssert.DoesNotContain(destroyed, borrowed.Id);
        }

        private sealed class Token
        {
            internal Token(int id)
            {
                Id = id;
            }

            internal int Id { get; }
        }
    }
}
