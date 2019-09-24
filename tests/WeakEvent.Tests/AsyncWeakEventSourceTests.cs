using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace WeakEvent.Tests
{
    public class AsyncWeakEventSourceTests
    {
        [Fact]
        public async Task Instance_Handlers_Are_Called()
        {
            var pub = new Publisher();
            var calledSubscribers = new List<int>();
            var sub1 = new InstanceSubscriber(1, i => AddAsync(calledSubscribers, i));
            sub1.Subscribe(pub);
            var sub2 = new InstanceSubscriber(2, i => AddAsync(calledSubscribers, i));
            sub2.Subscribe(pub);

            await pub.Raise();
            calledSubscribers.Should().Equal(1, 2);

            // Make sure subscribers are not collected before the end of the test
            GC.KeepAlive(sub1);
            GC.KeepAlive(sub2);
        }

        [Fact]
        public async Task Static_Handlers_Are_Called()
        {
            var pub = new Publisher();
            StaticSubscriber.CallCount = 0;
            StaticSubscriber.Subscribe(pub);

            await pub.Raise();

            StaticSubscriber.CallCount.Should().Be(1);
        }

        [Fact]
        public async Task Multicast_Handlers_Are_Called()
        {
            var pub = new Publisher();
            var calledSubscribers = new List<int>();
            AsyncEventHandler<EventArgs> handler = null;
            handler += (sender, e) => AddAsync(calledSubscribers, 1);
            handler += (sender, e) => AddAsync(calledSubscribers, 2);
            handler += (sender, e) => AddAsync(calledSubscribers, 3);

            pub.Foo += handler;
            await pub.Raise();

            calledSubscribers.Should().Equal(1, 2, 3);
        }

        [Fact]
        public async Task Handlers_Can_Be_Unsubscribed()
        {
            var pub = new Publisher();
            var calledSubscribers = new List<int>();
            var sub1 = new InstanceSubscriber(1, i => AddAsync(calledSubscribers, i));
            sub1.Subscribe(pub);
            var sub2 = new InstanceSubscriber(2, i => AddAsync(calledSubscribers, i));
            sub2.Subscribe(pub);
            StaticSubscriber.CallCount = 0;
            StaticSubscriber.Subscribe(pub);

            // Make sure they really were subscribed
            await pub.Raise();
            calledSubscribers.Should().Equal(1, 2);
            StaticSubscriber.CallCount.Should().Be(1);

            calledSubscribers.Clear();
            sub1.Unsubscribe(pub);
            await pub.Raise();
            calledSubscribers.Should().Equal(2);

            StaticSubscriber.CallCount = 0;
            StaticSubscriber.Unsubscribe(pub);
            await pub.Raise();
            StaticSubscriber.CallCount.Should().Be(0);

            calledSubscribers.Clear();
            sub2.Unsubscribe(pub);
            await pub.Raise();
            calledSubscribers.Should().BeEmpty();

            // Make sure subscribers are not collected before the end of the test
            GC.KeepAlive(sub1);
            GC.KeepAlive(sub2);
        }

        [Fact]
        public async Task Only_The_Last_Matching_Handler_Is_Unsubscribed()
        {
            // Subscribe the same handlers multiple times
            var pub = new Publisher();
            var calledSubscribers = new List<int>();
            var sub1 = new InstanceSubscriber(1, i => AddAsync(calledSubscribers, i));
            sub1.Subscribe(pub);
            sub1.Subscribe(pub);
            StaticSubscriber.CallCount = 0;
            StaticSubscriber.Subscribe(pub);
            StaticSubscriber.Subscribe(pub);

            // Make sure they really were subscribed
            await pub.Raise();
            calledSubscribers.Should().Equal(1, 1);
            StaticSubscriber.CallCount.Should().Be(2);

            // Unsubscribe one instance handler
            calledSubscribers.Clear();
            sub1.Unsubscribe(pub);
            await pub.Raise();
            //calledSubscribers.Should().Equal(1);

            // Unsubscribe one static handler
            StaticSubscriber.CallCount = 0;
            StaticSubscriber.Unsubscribe(pub);
            await pub.Raise();
            StaticSubscriber.CallCount.Should().Be(1);

            // Make sure subscribers are not collected before the end of the test
            GC.KeepAlive(sub1);
        }

        [Fact]
        public async Task Subscribers_Can_Be_Garbage_Collected()
        {
            var pub = new Publisher();
            var calledSubscribers = new List<int>();
            var sub1 = new InstanceSubscriber(1, i =>  AddAsync(calledSubscribers, i));
            sub1.Subscribe(pub);
            var sub2 = new InstanceSubscriber(2, i => AddAsync(calledSubscribers, i));
            sub2.Subscribe(pub);
            var weakSub1 = new WeakReference(sub1);
            var weakSub2 = new WeakReference(sub2);

            // ReSharper disable once RedundantAssignment
            sub2 = null; // only necessary in Debug
            GC.Collect();
            GC.WaitForPendingFinalizers();

            weakSub1.IsAlive.Should().BeTrue("because it is explicitly kept alive (sanity check)");
            weakSub2.IsAlive.Should().BeFalse("because it should have been collected");

            await pub.Raise();
            calledSubscribers.Should().Equal(1);

            // Make sure sub1 is not collected before the end of the test (sub2 can be collected)
            GC.KeepAlive(sub1);
        }

        [Fact]
        public async Task Reentrant_Subscribers_Dont_Fire_Immediately()
        {
            var pub = new Publisher();
            var calledSubscribers = new List<int>();
            var sub2 = new InstanceSubscriber(2, i => AddAsync(calledSubscribers, i));
            var sub1 = new InstanceSubscriber(1, async i =>
            {
                await AddAsync(calledSubscribers, i);

                // This listener should not receive the event during the first round of notifications
                sub2.Subscribe(pub);

                // Make sure subscribers are not collected before the end of the test
                GC.KeepAlive(sub2);
            });
            sub1.Subscribe(pub);

            await pub.Raise();
            calledSubscribers.Should().Equal(1);

            // Make sure subscribers are not collected before the end of the test
            GC.KeepAlive(sub1);
        }

        [Theory]
        // Match
        [InlineData("abc", "abccbca", "bc", "abcca")]
        [InlineData("abc", "abccbca", "abc", "cbca")]
        [InlineData("abc", "abccbca", "c", "abccba")]
        [InlineData("abc", "abccbca", "ccb", "abca")]
        [InlineData("abc", "abccbca", "ca", "abccb")]
        [InlineData("abc", "abccbca", "abccb", "ca")]
        [InlineData("abc", "aacabcbcbc", "abcb", "aaccbc")]
        // No match
        [InlineData("abcd", "abccbca", "d", "abccbca")]
        [InlineData("abc", "abccbca", "ccc", "abccbca")]
        [InlineData("abc", "abccbca", "cba", "abccbca")]
        public async Task Multicast_Handlers_Are_Correctly_Unsubscribed(string ids, string toSubscribe, string toUnsubscribe, string expected)
        {
            var pub = new Publisher();
            var calledSubscribers = new StringBuilder();
            var handlers = new Dictionary<char, AsyncEventHandler<EventArgs>>(ids.Length);
            foreach (char c in ids)
            {
                char subscriberId = c;
                handlers[subscriberId] = async (sender, e) =>
                {
                    await Task.Yield();
                    calledSubscribers.Append(subscriberId);
                };
            }

            foreach (char c in toSubscribe)
            {
                pub.Foo += handlers[c];
            }

            await pub.Raise();
            calledSubscribers.ToString().Should().Be(toSubscribe);

            calledSubscribers.Clear();

            AsyncEventHandler<EventArgs> handlerToRemove = null;
            foreach (char c in toUnsubscribe)
            {
                handlerToRemove += handlers[c];
            }
            pub.Foo -= handlerToRemove;

            await pub.Raise();
            calledSubscribers.ToString().Should().Be(expected);
        }

        [Fact]
        public async Task Different_Multicast_Handler_Is_Not_Unsubscribed()
        {
            var pub = new Publisher();
            var calledSubscribers = new List<int>();

            AsyncEventHandler<EventArgs> handler1 = (sender, e) => AddAsync(calledSubscribers, 1);
            AsyncEventHandler<EventArgs> handler2 = (sender, e) => AddAsync(calledSubscribers, 2);
            AsyncEventHandler<EventArgs> handler3 = (sender, e) => AddAsync(calledSubscribers, 3);

            var multicastHandler = handler1 + handler3;

            pub.Foo += handler1;
            pub.Foo += (handler2 + handler3);
            await pub.Raise();

            calledSubscribers.Clear();

            // Different order
            pub.Foo -= (handler3 + handler2);
            await pub.Raise();

            calledSubscribers.Should().Equal(1, 2, 3);
        }

        [Fact]
        public async Task Can_Subscribe_While_Another_Thread_Is_Invoking()
        {
            var sub1CanFinish = new ManualResetEvent(false);
            var sub2CanSubscribe = new ManualResetEvent(false);

            var pub = new Publisher();
            var sub1 = new InstanceSubscriber(1, async i =>
            {
                sub2CanSubscribe.Set();
                await sub1CanFinish.WaitOneAsync();
            });
            var sub2 = new InstanceSubscriber(2, async i => await Task.Yield());
            sub1.Subscribe(pub);

            var task1 = Task.Run(async () =>
            {
                await pub.Raise();
            });

            var task2 = Task.Run(async () =>
            {
                await sub2CanSubscribe.WaitOneAsync();
                sub2.Subscribe(pub);
            });

            bool subscribeFinished = await Task.WhenAny(task2, Task.Delay(500)) == task2;
            sub1CanFinish.Set();
            await task1;

            subscribeFinished.Should().BeTrue();

            GC.KeepAlive(sub1);
            GC.KeepAlive(sub2);
        }

        [Fact]
        public async Task Can_Raise_Even_If_Delegates_List_Is_Unclean()
        {
            var pub = new Publisher();

            var sub1 = new InstanceSubscriber(1,async i => await Task.Yield());
            sub1.Subscribe(pub);
            var sub2 = new InstanceSubscriber(1, async i => await Task.Yield());
            sub2.Subscribe(pub);
            var sub3 = new InstanceSubscriber(1, async i => await Task.Yield());
            sub3.Subscribe(pub);
            var sub4 = new InstanceSubscriber(1, async i => await Task.Yield());
            sub4.Subscribe(pub);
            var sub5 = new InstanceSubscriber(1, async i => await Task.Yield());
            sub5.Subscribe(pub);
            var sub6 = new InstanceSubscriber(1, async i => await Task.Yield());
            sub6.Subscribe(pub);
            var sub7 = new InstanceSubscriber(1, async i => await Task.Yield());
            sub7.Subscribe(pub);
            var sub8 = new InstanceSubscriber(1, async i => await Task.Yield());
            sub8.Subscribe(pub);

            sub8.Unsubscribe(pub);

            await pub.Raise();
        }

        [Fact]
        public async Task Subscriber_Stays_Alive_If_Lifetime_Object_Is_Alive()
        {
            bool handlerWasCalled = false;
            object lifetime = new object();
            var source = new AsyncWeakEventSource<EventArgs>();
            source.Subscribe(lifetime, new InstanceSubscriber(1, async i =>
            {
                handlerWasCalled = true;
                await Task.Yield();
            }).OnFoo);

            GC.Collect();
            GC.WaitForPendingFinalizers();

            await source.RaiseAsync(this, EventArgs.Empty);
            handlerWasCalled.Should().BeTrue();

            GC.KeepAlive(lifetime);
        }

        [Fact]
        public async Task Subscriber_Dies_If_Lifetime_Object_Is_Dead()
        {
            bool handlerWasCalled = false;
            var source = new AsyncWeakEventSource<EventArgs>();
            object lifetime = new object();
            source.Subscribe(lifetime, new InstanceSubscriber(1, async i => 
            {
                handlerWasCalled = true;
                await Task.Yield();
            }).OnFoo);
            lifetime = null;

            GC.Collect();
            GC.WaitForPendingFinalizers();

            await source.RaiseAsync(this, EventArgs.Empty);
            handlerWasCalled.Should().BeFalse();
        }

        #region Test subjects

        private async Task AddAsync(List<int> list, int value)
        {
            list.Add(value);
            await Task.Yield();
        }

        class Publisher
        {
            private readonly AsyncWeakEventSource<EventArgs> _fooEventSource = new AsyncWeakEventSource<EventArgs>();
            public event AsyncEventHandler<EventArgs> Foo
            {
                add => _fooEventSource.Subscribe(value);
                remove => _fooEventSource.Unsubscribe(value);
            }

            public Task Raise()
            {
                return _fooEventSource.RaiseAsync(this, EventArgs.Empty);
            }
        }

        class InstanceSubscriber
        {
            private readonly int _id;
            private readonly Func<int, Task> _onFoo;

            public InstanceSubscriber(int id, Func<int, Task> onFoo)
            {
                _id = id;
                _onFoo = onFoo;
            }

            public async Task OnFoo(object sender, EventArgs e)
            {
                await _onFoo(_id);
            }

            public void Subscribe(Publisher pub)
            {
                pub.Foo += OnFoo;
            }

            public void Unsubscribe(Publisher pub)
            {
                pub.Foo -= OnFoo;
            }
        }

        static class StaticSubscriber
        {
            public static int CallCount { get; set; }

            public static void Subscribe(Publisher pub)
            {
                pub.Foo += OnFoo;
            }

            private static async Task OnFoo(object sender, EventArgs e)
            {
                CallCount++;
                await Task.Yield();
            }

            public static void Unsubscribe(Publisher pub)
            {
                pub.Foo -= OnFoo;
            }
        }

        #endregion
    }
}