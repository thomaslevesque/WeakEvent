using System;
using System.Collections.Generic;
using System.Text;
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
            StaticSubscriber.CallCount = 0;
            StaticSubscriber.Subscribe(pub);

            pub.Raise();

            StaticSubscriber.CallCount.Should().Be(1);
        }

        [Fact]
        public void Multicast_Handlers_Are_Called()
        {
            var pub = new Publisher();
            var calledSubscribers = new List<int>();
            EventHandler<EventArgs>? handler = null;
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
            StaticSubscriber.CallCount = 0;
            StaticSubscriber.Subscribe(pub);

            // Make sure they really were subscribed
            pub.Raise();
            calledSubscribers.Should().Equal(1, 2);
            StaticSubscriber.CallCount.Should().Be(1);

            calledSubscribers.Clear();
            sub1.Unsubscribe(pub);
            pub.Raise();
            calledSubscribers.Should().Equal(2);

            StaticSubscriber.CallCount = 0;
            StaticSubscriber.Unsubscribe(pub);
            pub.Raise();
            StaticSubscriber.CallCount.Should().Be(0);

            calledSubscribers.Clear();
            sub2.Unsubscribe(pub);
            pub.Raise();
            calledSubscribers.Should().BeEmpty();

            // Make sure subscribers are not collected before the end of the test
            GC.KeepAlive(sub1);
            GC.KeepAlive(sub2);
        }

        [Fact]
        public void Only_The_Last_Matching_Handler_Is_Unsubscribed()
        {
            // Subscribe the same handlers multiple times
            var pub = new Publisher();
            var calledSubscribers = new List<int>();
            var sub1 = new InstanceSubscriber(1, calledSubscribers.Add);
            sub1.Subscribe(pub);
            sub1.Subscribe(pub);
            StaticSubscriber.CallCount = 0;
            StaticSubscriber.Subscribe(pub);
            StaticSubscriber.Subscribe(pub);

            // Make sure they really were subscribed
            pub.Raise();
            calledSubscribers.Should().Equal(1, 1);
            StaticSubscriber.CallCount.Should().Be(2);

            // Unsubscribe one instance handler
            calledSubscribers.Clear();
            sub1.Unsubscribe(pub);
            pub.Raise();
            //calledSubscribers.Should().Equal(1);

            // Unsubscribe one static handler
            StaticSubscriber.CallCount = 0;
            StaticSubscriber.Unsubscribe(pub);
            pub.Raise();
            StaticSubscriber.CallCount.Should().Be(1);

            // Make sure subscribers are not collected before the end of the test
            GC.KeepAlive(sub1);
        }

        [Fact]
        public void Subscribers_Can_Be_Garbage_Collected()
        {
            var pub = new Publisher();
            var calledSubscribers = new List<int>();
            var sub1 = new InstanceSubscriber(1, calledSubscribers.Add);
            sub1.Subscribe(pub);
            InstanceSubscriber? sub2 = new InstanceSubscriber(2, calledSubscribers.Add);
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
        public void Multicast_Handlers_Are_Correctly_Unsubscribed(string ids, string toSubscribe, string toUnsubscribe, string expected)
        {
            var pub = new Publisher();
            var calledSubscribers = new StringBuilder();
            var handlers = new Dictionary<char, EventHandler<EventArgs>>(ids.Length);
            foreach (char c in ids)
            {
                char subscriberId = c;
                handlers[subscriberId] = (sender, e) => calledSubscribers.Append(subscriberId);
            }

            foreach (char c in toSubscribe)
            {
                pub.Foo += handlers[c];
            }

            pub.Raise();
            calledSubscribers.ToString().Should().Be(toSubscribe);

            calledSubscribers.Clear();

            EventHandler<EventArgs>? handlerToRemove = null;
            foreach (char c in toUnsubscribe)
            {
                handlerToRemove += handlers[c];
            }
            pub.Foo -= handlerToRemove;

            pub.Raise();
            calledSubscribers.ToString().Should().Be(expected);
        }

        [Fact]
        public void Different_Multicast_Handler_Is_Not_Unsubscribed()
        {
            var pub = new Publisher();
            var calledSubscribers = new List<int>();

            EventHandler<EventArgs> handler1 = (sender, e) => calledSubscribers.Add(1);
            EventHandler<EventArgs> handler2 = (sender, e) => calledSubscribers.Add(2);
            EventHandler<EventArgs> handler3 = (sender, e) => calledSubscribers.Add(3);

            var multicastHandler = handler1 + handler3;

            pub.Foo += handler1;
            pub.Foo += (handler2 + handler3);
            pub.Raise();

            calledSubscribers.Clear();

            // Different order
            pub.Foo -= (handler3 + handler2);
            pub.Raise();

            calledSubscribers.Should().Equal(1, 2, 3);
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

        [Fact]
        public void Subscriber_Stays_Alive_If_Lifetime_Object_Is_Alive()
        {
            bool handlerWasCalled = false;
            object lifetime = new object();
            var source = new WeakEventSource<EventArgs>();
            source.Subscribe(lifetime, new InstanceSubscriber(1, i => handlerWasCalled = true).OnFoo);

            GC.Collect();
            GC.WaitForPendingFinalizers();

            source.Raise(this, EventArgs.Empty);
            handlerWasCalled.Should().BeTrue();

            GC.KeepAlive(lifetime);
        }

        [Fact]
        public void Subscriber_Dies_If_Lifetime_Object_Is_Dead()
        {
            bool handlerWasCalled = false;
            var source = new WeakEventSource<EventArgs>();
            object? lifetime = new object();
            source.Subscribe(lifetime, new InstanceSubscriber(1, i => handlerWasCalled = true).OnFoo);
            lifetime = null;

            GC.Collect();
            GC.WaitForPendingFinalizers();

            source.Raise(this, EventArgs.Empty);
            handlerWasCalled.Should().BeFalse();
        }

        [Fact]
        public void Second_Handler_With_Same_Lifetime_Stays_Alive_If_First_Handler_Is_Removed()
        {
            var source = new WeakEventSource<EventArgs>();
            object lifetime = new object();

            var handlerCalls = new List<int>();

            EventHandler<EventArgs>? handler1 = (sender, e) => handlerCalls.Add(1);
            EventHandler<EventArgs>? handler2 = (sender, e) => handlerCalls.Add(2);

            source.Subscribe(lifetime, handler1);
            source.Subscribe(lifetime, handler2);
            source.Raise(this, EventArgs.Empty);
            handlerCalls.Should().Equal(1, 2);

            handlerCalls.Clear();
            source.Unsubscribe(handler1);
            handler1 = null;
            handler2 = null;

            GC.Collect();
            GC.WaitForPendingFinalizers();

            source.Raise(this, EventArgs.Empty);
            handlerCalls.Should().Equal(2);

            GC.KeepAlive(lifetime);
        }

        [Fact]
        public void Handler_List_Is_Compacted_Even_If_Raise_Or_Unsubscribe_Isnt_Called()
        {
            var source = new WeakEventSource<EventArgs>();

            // Add many handlers (more than 50)
            for (int i = 0; i < 120; i++)
            {
                source.Subscribe(new InstanceSubscriber(i, _ => {}).OnFoo);
                
                // Run GC every now and then
                if (i % 25 is 0)
                {
                    GC.Collect();
                }
            }

            source._handlers?.Count.Should().BeLessThan(50);
        }

        [Fact]
        public void Exception_Is_Not_Swallowed_If_ExceptionHandler_Returns_False()
        {
            var source = new WeakEventSource<EventArgs>();
            bool throwingHandlerCalled = false;
            bool nonThrowingHandlerCalled = false;
            source.Subscribe(ThrowingHandler);
            source.Subscribe(NonThrowingHandler);
            Action raise = () => source.Raise(this, EventArgs.Empty, ExceptionHandler);
            raise.Should().Throw<Exception>().WithMessage("Oops");
            throwingHandlerCalled.Should().BeTrue();
            nonThrowingHandlerCalled.Should().BeFalse();

            void ThrowingHandler(object sender, EventArgs e)
            {
                throwingHandlerCalled = true;
                throw new Exception("Oops");
            }

            void NonThrowingHandler(object sender, EventArgs e)
            {
                nonThrowingHandlerCalled = true;
            }

            static bool ExceptionHandler(Exception _) => false;
        }

        [Fact]
        public void Exception_Is_Swallowed_If_ExceptionHandler_Returns_True()
        {
            var source = new WeakEventSource<EventArgs>();
            bool throwingHandlerCalled = false;
            bool nonThrowingHandlerCalled = false;

            source.Subscribe(ThrowingHandler);
            source.Subscribe(NonThrowingHandler);
            Action raise = () => source.Raise(this, EventArgs.Empty, ExceptionHandler);
            raise.Should().NotThrow();
            throwingHandlerCalled.Should().BeTrue();
            nonThrowingHandlerCalled.Should().BeTrue();

            void ThrowingHandler(object sender, EventArgs e)
            {
                throwingHandlerCalled = true;
                throw new Exception("Oops");
            }

            void NonThrowingHandler(object sender, EventArgs e)
            {
                nonThrowingHandlerCalled = true;
            }

            static bool ExceptionHandler(Exception _) => true;
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

            public void OnFoo(object sender, EventArgs e)
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
            public static int CallCount { get; set; }

            public static void Subscribe(Publisher pub)
            {
                pub.Foo += OnFoo;
            }

            private static void OnFoo(object sender, EventArgs e)
            {
                CallCount++;
            }

            public static void Unsubscribe(Publisher pub)
            {
                pub.Foo -= OnFoo;
            }
        }

        #endregion
    }
}
