using System;
using System.Collections.Generic;
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
            var sub1 = new InstanceSubscriber(1,i => AsyncAdd(calledSubscribers, i));
            sub1.Subscribe(pub);
            var sub2 = new InstanceSubscriber(2,i => AsyncAdd(calledSubscribers, i));
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
            StaticSubscriber.FooWasRaised = false;
            StaticSubscriber.Subscribe(pub);

            await pub.Raise();

            StaticSubscriber.FooWasRaised.Should().BeTrue();
        }

        [Fact]
        public async Task Multicast_Handlers_Are_Called()
        {
            var pub = new Publisher();
            var calledSubscribers = new List<int>();
            AsyncEventHandler<EventArgs> handler = null;
            handler += (sender, e) => AsyncAdd(calledSubscribers, 1);
            handler += (sender, e) => AsyncAdd(calledSubscribers, 2);
            handler += (sender, e) => AsyncAdd(calledSubscribers, 3);

            pub.Foo += handler;
            await pub.Raise();

            calledSubscribers.Should().Equal(1, 2, 3);
        }

        [Fact]
        public async Task Handlers_Can_Be_Unsubscribed()
        {
            var pub = new Publisher();
            var calledSubscribers = new List<int>();
            var sub1 = new InstanceSubscriber(1, i => AsyncAdd(calledSubscribers, i));
            sub1.Subscribe(pub);
            var sub2 = new InstanceSubscriber(2, i => AsyncAdd(calledSubscribers, i));
            sub2.Subscribe(pub);
            StaticSubscriber.FooWasRaised = false;
            StaticSubscriber.Subscribe(pub);

            // Make sure they really were subscribed
            await pub.Raise();
            calledSubscribers.Should().Equal(1, 2);
            StaticSubscriber.FooWasRaised.Should().BeTrue();

            calledSubscribers.Clear();
            sub1.Unsubscribe(pub);
            await pub.Raise();
            calledSubscribers.Should().Equal(2);

            StaticSubscriber.FooWasRaised = false;
            StaticSubscriber.Unsubscribe(pub);
            await pub.Raise();
            StaticSubscriber.FooWasRaised.Should().BeFalse();

            calledSubscribers.Clear();
            sub2.Unsubscribe(pub);
            await pub.Raise();
            calledSubscribers.Should().BeEmpty();

            // Make sure subscribers are not collected before the end of the test
            GC.KeepAlive(sub1);
            GC.KeepAlive(sub2);
        }

        [Fact]
        public async Task Subscribers_Can_Be_Garbage_Collected()
        {
            var pub = new Publisher();
            var calledSubscribers = new List<int>();
            var sub1 = new InstanceSubscriber(1,i =>  AsyncAdd(calledSubscribers, i));
            sub1.Subscribe(pub);
            var sub2 = new InstanceSubscriber(2,i => AsyncAdd(calledSubscribers, i));
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
            var sub2 = new InstanceSubscriber(2, i => AsyncAdd(calledSubscribers, i));
            var sub1 = new InstanceSubscriber(1, async i =>
            {
                await AsyncAdd(calledSubscribers, i);

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

        [Fact]
        public async Task Multicast_Handlers_Are_Correctly_Unsubscribed()
        {
            var pub = new Publisher();
            var calledSubscribers = new List<int>();
            AsyncEventHandler<EventArgs> handler = null;
            handler += (sender, e) => AsyncAdd(calledSubscribers, 1);
            handler += (sender, e) => AsyncAdd(calledSubscribers, 2);

            pub.Foo += handler;
            await pub.Raise();

            pub.Foo -= handler;
            await pub.Raise();

            calledSubscribers.Should().Equal(1, 2);
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
                sub1CanFinish.WaitOne();
                await Task.Yield();
            });
            var sub2 = new InstanceSubscriber(2, async i => await Task.Yield());
            sub1.Subscribe(pub);

            var task1 = Task.Run(async () => { await pub.Raise(); });
            var task2 = Task.Run(() =>
            {
                sub2CanSubscribe.WaitOne();
                sub2.Subscribe(pub);
            });
            
            if (await Task.WhenAny(task2, Task.Delay(500)) != task2) {
                throw new Exception("timed out");
            }

            sub1CanFinish.Set();

            await task1;
            
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

        #region Test subjects

        private async Task AsyncAdd(List<int> list, int value)
        {
            // This method is just to keep the test code more terse.
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

            private async Task OnFoo(object sender, EventArgs e)
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
            public static bool FooWasRaised { get; set; }

            public static void Subscribe(Publisher pub)
            {
                pub.Foo += OnFoo;
            }

            private static async Task OnFoo(object sender, EventArgs e)
            {
                FooWasRaised = true;
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