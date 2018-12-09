using System;
using System.Collections.Generic;
using System.Threading;
using FluentAssertions;
using Xunit;

namespace WeakEvent.Tests
{
    public class WeakEventSourceTests
    {
        [Fact]
        public void Instance_Handlers_Are_Called()
        {
            var pub = new Publisher();
            var calledSubscribers = new List<int>();
            var sub1 = new InstanceSubscriber(1, calledSubscribers.Add);
            sub1.Subscribe(pub);
            var sub2 = new InstanceSubscriber(2, calledSubscribers.Add);
            sub2.Subscribe(pub);

            pub.Raise();
            calledSubscribers.Should().Equal(1, 2);

            // Make sure subscribers are not collected before the end of the test
            GC.KeepAlive(sub1);
            GC.KeepAlive(sub2);
        }

        [Fact]
        public void Static_Handlers_Are_Called()
        {
            var pub = new Publisher();
            StaticSubscriber.FooWasRaised = false;
            StaticSubscriber.Subscribe(pub);

            pub.Raise();

            StaticSubscriber.FooWasRaised.Should().BeTrue();
        }

        [Fact]
        public void Multicast_Handlers_Are_Called()
        {
            var pub = new Publisher();
            var calledSubscribers = new List<int>();
            EventHandler<EventArgs> handler = null;
            handler += (sender, e) => calledSubscribers.Add(1);
            handler += (sender, e) => calledSubscribers.Add(2);
            handler += (sender, e) => calledSubscribers.Add(3);

            pub.Foo += handler;
            pub.Raise();

            calledSubscribers.Should().Equal(1, 2, 3);
        }

        [Fact]
        public void Handlers_Can_Be_Unsubscribed()
        {
            var pub = new Publisher();
            var calledSubscribers = new List<int>();
            var sub1 = new InstanceSubscriber(1, calledSubscribers.Add);
            sub1.Subscribe(pub);
            var sub2 = new InstanceSubscriber(2, calledSubscribers.Add);
            sub2.Subscribe(pub);
            StaticSubscriber.FooWasRaised = false;
            StaticSubscriber.Subscribe(pub);

            // Make sure they really were subscribed
            pub.Raise();
            calledSubscribers.Should().Equal(1, 2);
            StaticSubscriber.FooWasRaised.Should().BeTrue();

            calledSubscribers.Clear();
            sub1.Unsubscribe(pub);
            pub.Raise();
            calledSubscribers.Should().Equal(2);

            StaticSubscriber.FooWasRaised = false;
            StaticSubscriber.Unsubscribe(pub);
            pub.Raise();
            StaticSubscriber.FooWasRaised.Should().BeFalse();

            calledSubscribers.Clear();
            sub2.Unsubscribe(pub);
            pub.Raise();
            calledSubscribers.Should().BeEmpty();

            // Make sure subscribers are not collected before the end of the test
            GC.KeepAlive(sub1);
            GC.KeepAlive(sub2);
        }

        [Fact]
        public void Subscribers_Can_Be_Garbage_Collected()
        {
            var pub = new Publisher();
            var calledSubscribers = new List<int>();
            var sub1 = new InstanceSubscriber(1, calledSubscribers.Add);
            sub1.Subscribe(pub);
            var sub2 = new InstanceSubscriber(2, calledSubscribers.Add);
            sub2.Subscribe(pub);
            var weakSub1 = new WeakReference(sub1);
            var weakSub2 = new WeakReference(sub2);

            // ReSharper disable once RedundantAssignment
            sub2 = null; // only necessary in Debug
            GC.Collect();
            GC.WaitForPendingFinalizers();

            weakSub1.IsAlive.Should().BeTrue("because it is explicitly kept alive (sanity check)");
            weakSub2.IsAlive.Should().BeFalse("because it should have been collected");

            pub.Raise();
            calledSubscribers.Should().Equal(1);

            // Make sure sub1 is not collected before the end of the test (sub2 can be collected)
            GC.KeepAlive(sub1);
        }

        [Fact]
        public void Reentrant_Subscribers_Dont_Fire_Immediately()
        {
            var pub = new Publisher();
            var calledSubscribers = new List<int>();
            var sub2 = new InstanceSubscriber(2, calledSubscribers.Add);
            var sub1 = new InstanceSubscriber(1, i =>
            {
                calledSubscribers.Add(i);

                // This listener should not receive the event during the first round of notifications
                sub2.Subscribe(pub);

                // Make sure subscribers are not collected before the end of the test
                GC.KeepAlive(sub2);
            });
            sub1.Subscribe(pub);

            pub.Raise();
            calledSubscribers.Should().Equal(1);

            // Make sure subscribers are not collected before the end of the test
            GC.KeepAlive(sub1);
        }

        [Fact]
        public void Multicast_Handlers_Are_Correctly_Unsubscribed()
        {
            var pub = new Publisher();
            var calledSubscribers = new List<int>();
            EventHandler<EventArgs> handler = null;
            handler += (sender, e) => calledSubscribers.Add(1);
            handler += (sender, e) => calledSubscribers.Add(2);

            pub.Foo += handler;
            pub.Raise();

            pub.Foo -= handler;
            pub.Raise();

            calledSubscribers.Should().Equal(1, 2);
        }

        [Fact]
        public void Can_Subscribe_While_Another_Thread_Is_Invoking()
        {
            var sub1CanFinish = new ManualResetEvent(false);
            var sub2CanSubscribe = new ManualResetEvent(false);

            var pub = new Publisher();
            var sub1 = new InstanceSubscriber(1, i =>
            {
                sub2CanSubscribe.Set();
                sub1CanFinish.WaitOne();
            });
            var sub2 = new InstanceSubscriber(2, i => { });
            sub1.Subscribe(pub);

            var thread1 = new Thread(() =>
            {
                pub.Raise();
            });

            var thread2 = new Thread(() =>
            {
                sub2CanSubscribe.WaitOne();
                sub2.Subscribe(pub);
            });

            thread1.Start();
            thread2.Start();
            bool subscribeFinished = thread2.Join(500);
            sub1CanFinish.Set();
            thread1.Join();

            subscribeFinished.Should().BeTrue();

            GC.KeepAlive(sub1);
            GC.KeepAlive(sub2);
        }

        [Fact]
        public void Can_Raise_Even_If_Delegates_List_Is_Unclean()
        {
            var pub = new Publisher();
            
            var sub1 = new InstanceSubscriber(1, i =>{});
            sub1.Subscribe(pub);
            var sub2 = new InstanceSubscriber(1, i =>{});
            sub2.Subscribe(pub);
            var sub3 = new InstanceSubscriber(1, i =>{});
            sub3.Subscribe(pub);
            var sub4 = new InstanceSubscriber(1, i =>{});
            sub4.Subscribe(pub);
            var sub5 = new InstanceSubscriber(1, i =>{});
            sub5.Subscribe(pub);
            var sub6 = new InstanceSubscriber(1, i =>{});
            sub6.Subscribe(pub);
            var sub7 = new InstanceSubscriber(1, i =>{});
            sub7.Subscribe(pub);
            var sub8 = new InstanceSubscriber(1, i =>{});
            sub8.Subscribe(pub);

            sub8.Unsubscribe(pub);

            pub.Raise();
        }

        #region Test subjects

        class Publisher
        {
            private readonly WeakEventSource<EventArgs> _fooEventSource = new WeakEventSource<EventArgs>();
            public event EventHandler<EventArgs> Foo
            {
                add => _fooEventSource.Subscribe(value);
                remove => _fooEventSource.Unsubscribe(value);
            }

            public void Raise()
            {
                _fooEventSource.Raise(this, EventArgs.Empty);
            }
        }

        class InstanceSubscriber
        {
            private readonly int _id;
            private readonly Action<int> _onFoo;

            public InstanceSubscriber(int id, Action<int> onFoo)
            {
                _id = id;
                _onFoo = onFoo;
            }

            private void OnFoo(object sender, EventArgs e)
            {
                _onFoo(_id);
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

            private static void OnFoo(object sender, EventArgs e)
            {
                FooWasRaised = true;
            }

            public static void Unsubscribe(Publisher pub)
            {
                pub.Foo -= OnFoo;
            }
        }

        #endregion
    }
}
