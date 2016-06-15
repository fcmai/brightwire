﻿using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BrightWire.Models
{
    [ProtoContract]
    public class HMMStateTransition<T>
    {
        [ProtoMember(1)]
        public T NextState { get; set; }

        [ProtoMember(2)]
        public float Probability { get; set; }
    }

    [ProtoContract]
    public class HMMObservation2<T>
    {
        [ProtoMember(1)]
        public T Item1 { get; set; }

        [ProtoMember(2)]
        public T Item2 { get; set; }

        [ProtoMember(3)]
        public List<HMMStateTransition<T>> Transition { get; set; }

        public HMMObservation2() { }
        public HMMObservation2(T item1, T item2, List<HMMStateTransition<T>> transition)
        {
            Item1 = item1;
            Item2 = item2;
            Transition = transition;
        }
    }

    [ProtoContract]
    public class HMMObservation3<T>
    {
        [ProtoMember(1)]
        public T Item1 { get; set; }

        [ProtoMember(2)]
        public T Item2 { get; set; }

        [ProtoMember(3)]
        public T Item3 { get; set; }

        [ProtoMember(4)]
        public List<HMMStateTransition<T>> Transition { get; set; }

        public HMMObservation3() { }
        public HMMObservation3(T item1, T item2, T item3, List<HMMStateTransition<T>> transition)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
            Transition = transition;
        }
    }

    public static class HMMHelper
    {
        static bool _Compare<T>(T x, T y, IEqualityComparer<T> comparer)
        {
            return (comparer ?? EqualityComparer<T>.Default).Equals(x, y);
        }

        public static IReadOnlyList<HMMStateTransition<T>> Find<T>(this IReadOnlyList<HMMObservation3<T>> data, T item1, T item2, T item3, IEqualityComparer<T> comparer = null)
        {
            return data
                .Where(d => _Compare(d.Item1, item1, comparer) && _Compare(d.Item2, item2, comparer) && _Compare(d.Item3, item3, comparer))
                .FirstOrDefault()?.Transition
            ;
        }

        public static IReadOnlyList<HMMStateTransition<T>> Find<T>(this IReadOnlyList<HMMObservation2<T>> data, T item1, T item2, IEqualityComparer<T> comparer = null)
        {
            return data
                .Where(d => _Compare(d.Item1, item1, comparer) && _Compare(d.Item2, item2, comparer))
                .FirstOrDefault()?.Transition
            ;
        }
    }
}