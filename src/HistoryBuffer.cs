using System;
using System.Collections.Generic;

namespace FanControl.Dashboard
{
    /// <summary>
    /// Fixed-size ring buffers, one per series, all advanced together once per sample.
    /// Not thread-safe: the sampler guards it with its own lock.
    /// </summary>
    sealed class HistoryBuffer
    {
        readonly Dictionary<string, float[]> _series = new Dictionary<string, float[]>();
        readonly int _capacity;
        int _head, _count;

        public HistoryBuffer(IEnumerable<string> keys, int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException("capacity");
            _capacity = capacity;
            foreach (var k in keys) _series[k] = Fill(new float[capacity]);
        }

        public int Capacity { get { return _capacity; } }

        /// <summary>Number of samples recorded so far (at most <see cref="Capacity"/>).</summary>
        public int Count { get { return _count; } }

        public bool Has(string key) { return key != null && _series.ContainsKey(key); }

        /// <summary>Adds a series (e.g. a sensor the user just added); its past reads as NaN.</summary>
        public void Ensure(string key)
        {
            if (key == null || _series.ContainsKey(key)) return;
            _series[key] = Fill(new float[_capacity]);
        }

        /// <summary>Appends one sample for every series; series missing from <paramref name="values"/> get NaN.</summary>
        public void Push(IDictionary<string, float> values)
        {
            foreach (var kv in _series)
            {
                float v;
                kv.Value[_head] = values != null && values.TryGetValue(kv.Key, out v) ? v : float.NaN;
            }
            _head = (_head + 1) % _capacity;
            if (_count < _capacity) _count++;
        }

        /// <summary>The last <paramref name="points"/> samples of a series, oldest first. NaN marks "no data".</summary>
        public float[] Read(string key, int points)
        {
            points = Math.Max(1, Math.Min(points, _capacity));
            var result = new float[points];
            float[] buf;
            if (key == null || !_series.TryGetValue(key, out buf)) return Fill(result);
            for (int i = 0; i < points; i++)
            {
                int age = points - 1 - i; // 0 = newest
                if (age >= _count) { result[i] = float.NaN; continue; }
                int idx = ((_head - 1 - age) % _capacity + _capacity) % _capacity;
                result[i] = buf[idx];
            }
            return result;
        }

        static float[] Fill(float[] a) { for (int i = 0; i < a.Length; i++) a[i] = float.NaN; return a; }
    }
}
