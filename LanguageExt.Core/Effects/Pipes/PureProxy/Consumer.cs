//
// This file contains a number of micro-free-monads that allow for creation of pure producers, consumers, and pipes.  
// They're used to facilitate the building of Proxy derived types without the need for typing the generic arguments endlessly
// The Haskell original could auto-infer the generic parameter types, the system here tries to replicate manually what
// Haskell can do automatically.  Hence why there are so many implementations of SelectMany!
//

using System;
using LanguageExt.Effects.Traits;
using LanguageExt.Common;

namespace LanguageExt.Pipes;

public abstract class Consumer<IN, A>
{
    public abstract Consumer<IN, B> Select<B>(Func<A, B> f);
    
    public abstract Consumer<IN, B> Bind<B>(Func<A, Consumer<IN, B>> f);
    public abstract Consumer<RT, IN, B> Bind<RT, B>(Func<A, Consumer<RT, IN, B>> f) where RT : HasIO<RT, Error>;
    public abstract Pipe<IN, OUT, B> Bind<OUT, B>(Func<A, Producer<OUT, B>> f);
    
    public Consumer<IN, B> Bind<B>(Func<A, Pure<B>> f) =>
        Map(x => f(x).Value);
 
    public Consumer<IN, B> Bind<B>(Func<A, Transducer<Unit, B>> f) =>
        Bind(a => f(a).Map(Sum<Error, B>.Right));
 
    public Consumer<IN, B> Bind<B>(Func<A, Transducer<Unit, Sum<Error, B>>> f) =>
        Bind(a => new Consumer<IN, B>.Lift<B>(f(a), PureProxy.ConsumerPure<IN, B>));
 
    public Consumer<RT, IN, B> Bind<RT, B>(Func<A, Transducer<RT, B>> f) 
        where RT : HasIO<RT, Error> =>
        Interpret<RT>().Bind(f);

    public Consumer<RT, IN, B> Bind<RT, B>(Func<A, Transducer<RT, Sum<Error, B>>> f) 
        where RT : HasIO<RT, Error> =>
        Interpret<RT>().Bind(f);
 
    public abstract Consumer<RT, IN, A> Interpret<RT>() where RT : HasIO<RT, Error>;
    public abstract Pipe<IN, OUT, A> ToPipe<OUT>();
        
    public Consumer<IN, B> Map<B>(Func<A, B> f) => Select(f);
        
    public Consumer<IN, C> SelectMany<B, C>(Func<A, Pure<B>> f, Func<A, B, C> project) =>
        Map(x => project(x, f(x).Value));
 
    public Consumer<IN, C> SelectMany<B, C>(Func<A, Transducer<Unit, B>> f, Func<A, B, C> project) =>
        Bind(a => f(a).Map(b => project(a, b)));
 
    public Consumer<IN, C> SelectMany<B, C>(Func<A, Transducer<Unit, Sum<Error, B>>> f, Func<A, B, C> project) =>
        Bind(a => f(a).Map(mb => mb.Map(b => project(a, b))));
 
    public Consumer<RT, IN, C> SelectMany<RT, B, C>(Func<A, Transducer<RT, B>> f, Func<A, B, C> project)
        where RT : HasIO<RT, Error> =>
        Bind(a => f(a).Map(b => project(a, b)));
 
    public Consumer<RT, IN, C> SelectMany<RT, B, C>(Func<A, Transducer<RT, Sum<Error, B>>> f, Func<A, B, C> project)
        where RT : HasIO<RT, Error> =>
        Bind(a => f(a).Map(mb => mb.Map(b => project(a, b))));
 
    public Consumer<IN, C> SelectMany<B, C>(Func<A, Consumer<IN, B>> f, Func<A, B, C> project) =>
        Bind(a => f(a).Select(b => project(a, b)));
        
    public Consumer<RT, IN, C> SelectMany<RT, B, C>(Func<A, Consumer<RT, IN, B>> f, Func<A, B, C> project) where RT : HasIO<RT, Error> =>
        Bind(a => f(a).Select(b => project(a, b)));
        
    public Pipe<IN, OUT, C> SelectMany<OUT, B, C>(Func<A, Producer<OUT, B>> f, Func<A, B, C> project) =>
        Bind(a => f(a).Select(b => project(a, b)));
       
    public static implicit operator Consumer<IN, A>(Pure<A> ma) =>
        new Pure(ma.Value);
       
    public static implicit operator Consumer<IN, A>(Transducer<Unit, A> ma) =>
        new Lift<A>(ma.Map(Sum<Error, A>.Right), PureProxy.ConsumerPure<IN, A>);
       
    public static implicit operator Consumer<IN, A>(Transducer<Unit, Sum<Error, A>> ma) =>
        new Lift<A>(ma, PureProxy.ConsumerPure<IN, A>);
        
    public static Consumer<IN, A> operator &(
        Consumer<IN, A> lhs,
        Consumer<IN, A> rhs) =>
        lhs.Bind(_ => rhs);

    public class Pure(A Value) : Consumer<IN, A>
    {
        public override Consumer<IN, B> Select<B>(Func<A, B> f) =>
            new Consumer<IN, B>.Pure(f(Value));

        public override Consumer<IN, B> Bind<B>(Func<A, Consumer<IN, B>> f) =>
            f(Value);

        public override Consumer<RT, IN, B> Bind<RT, B>(Func<A, Consumer<RT, IN, B>> f) =>
            f(Value);

        public override Pipe<IN, OUT, B> Bind<OUT, B>(Func<A, Producer<OUT, B>> f) =>
            f(Value).ToPipe<IN>();

        public override Consumer<RT, IN, A> Interpret<RT>() =>
            Consumer.Pure<RT, IN, A>(Value);

        public override Pipe<IN, OUT, A> ToPipe<OUT>() =>
            new Pipe<IN, OUT, A>.Pure(Value);
    }

    public class Lift<X>(Transducer<Unit, Sum<Error, X>> Morphism, Func<X, Consumer<IN, A>> Next) : Consumer<IN, A>
    {
        public override Consumer<IN, B> Select<B>(Func<A, B> f) => 
            new Consumer<IN, B>.Lift<X>(Morphism, x => Next(x).Select(f));

        public override Consumer<IN, B> Bind<B>(Func<A, Consumer<IN, B>> f) => 
            new Consumer<IN, B>.Lift<X>(Morphism, x => Next(x).Bind(f));

        public override Consumer<RT, IN, B> Bind<RT, B>(Func<A, Consumer<RT, IN, B>> f) =>
            Consumer.lift<RT, IN, X>(Morphism).SelectMany(x => Next(x).Bind(f)).ToConsumer();

        public override Pipe<IN, OUT, B> Bind<OUT, B>(Func<A, Producer<OUT, B>> f) => 
            new Pipe<IN, OUT, B>.Lift<X>(Morphism, x => Next(x).Bind(f));

        public override Consumer<RT, IN, A> Interpret<RT>() =>
            Consumer.lift<RT, IN, X>(Morphism).Bind(x => Next(x).Interpret<RT>());

        public override Pipe<IN, OUT, A> ToPipe<OUT>() => 
            new Pipe<IN, OUT, A>.Lift<X>(Morphism, x => Next(x).ToPipe<OUT>());
    }

    public class Await : Consumer<IN, A>
    {
        public readonly Func<IN, Consumer<IN, A>> Next;
        public Await(Func<IN, Consumer<IN, A>> next) =>
            Next = next;

        public override Consumer<IN, B> Select<B>(Func<A, B> f) =>
            new Consumer<IN, B>.Await(x => Next(x).Select(f));

        public override Consumer<IN, B> Bind<B>(Func<A, Consumer<IN, B>> f) =>
            new Consumer<IN, B>.Await(x => Next(x).Bind(f));

        public override Consumer<RT, IN, B> Bind<RT, B>(Func<A, Consumer<RT, IN, B>> f) =>
            Interpret<RT>().Bind(f).ToConsumer();

        public override Pipe<IN, OUT, B> Bind<OUT, B>(Func<A, Producer<OUT, B>> f) =>
            new Pipe<IN, OUT, B>.Await(x => Next(x).Bind(f));

        public override Consumer<RT, IN, A> Interpret<RT>() =>
            Consumer.awaiting<RT, IN>().Bind(x => Next(x).Interpret<RT>()).ToConsumer();

        public override Pipe<IN, OUT, A> ToPipe<OUT>() =>
            new Pipe<IN, OUT, A>.Await(x => Next(x).ToPipe<OUT>());
    }
}
