# WeakEvent

Available on NuGet: [ThomasLevesque.WeakEvent](https://www.nuget.org/packages/ThomasLevesque.WeakEvent/)

Events are the most common source of memory leaks in .NET apps: the lifetime of the subscriber is extended to that of the publisher,
unless you unsubscribe from the event. That's because the publisher maintains a strong reference to the subscriber, via the delegate,
which prevents garbage collection of the subscriber.

This library provides a generic weak event source that can be used to publish events without affecting the lifetime of the subscriber.
In other words, if there is no other reference to the subscriber, the fact that it has subscribed to the event doesn't prevent it
from being garbage collected.

## How to use it

Instead of declaring your event like this:

```csharp
public event EventHandler<MyEventArgs> MyEvent;
```

Declare it like this:

```csharp
private readonly WeakEventSource<MyEventArgs> _myEventSource = new WeakEventSource<MyEventArgs>();
public event EventHandler<MyEventArgs> MyEvent
{
    add { _myEventSource.Subscribe(value); }
    remove { _myEventSource.Unsubscribe(value); }
}
```

And raise it like this:

```csharp
private void OnMyEvent(MyEventArgs e)
{
    _myEventSource.Raise(this, e);
}
```

That's it, you have a weak event! Client code can subscribe to it as usual, this is completely transparent from the subscriber's
point of view.

## How does it work

A delegate is made of two things:
- the method that will be called when the delegate is invoked
- the target on which the method will be called (null for static methods)

If we store the delegate directly, we end up storing a strong reference to the target. So, instead, we store a "weak delegate",
which is made of these things:
- the method that will be called when the delegate is invoked
- a weak reference to the target, so that we can access it when needed without preventing its garbage collection

So far, so good.

Now, how do we invoke this "weak delegate"? We could use `MethodInfo.Invoke` to call the method on the target through reflection,
but it's very slow compared to a direct delegate call. So instead we take advantage of a little known feature of .NET: open-instance
delegates. Basically, an open-instance delegate is a delegate that is bound to an instance method, but doesn't have a target. The
signature of an open-instance delegate is the same as the equivalent normal delegate, with an extra parameter for the target (the
`this` parameter). An example will make things clearer:

```csharp
public delegate void     FooEventHandler(               object sender, FooEventArgs e);
public delegate void OpenFooEventHandler(object target, object sender, FooEventArgs e);
```

So, when someone subscribes to our weak event by passing a normal delegate, we create a weak delegate that wraps a weak reference to
the target, and an open-instance delegate that is bound to the original delegate's method. When we need to invoke the weak delegate,
we check if the target is still alive, and if it is, we invoke the open-instance delegate on it.
