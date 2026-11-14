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

        const int FileMagic = 0x48444346; // "FCDH"
        const int FileVersion = 1;

        /// <summary>
        /// Writes every series (oldest sample first) and the time of the newest sample, so the history survives a restart.
        /// </summary>
        public void Save(System.IO.Stream stream, DateTime newestUtc)
        {
            var w = new System.IO.BinaryWriter(stream);
            w.Write(FileMagic); w.Write(FileVersion); w.Write(newestUtc.ToBinary()); w.Write(_count); w.Write(_series.Count);
            foreach (var kv in _series)
            {
                w.Write(kv.Key);
                foreach (var v in Read(kv.Key, _count)) w.Write(v);
            }
            w.Flush();
        }

        /// <summary>
        /// Restores a saved history into this (empty) buffer: the saved samples first, then a gap of NaN for the seconds the
        /// dashboard was not running (one sample per second). Returns false and changes nothing for unknown, damaged or too old files.
        /// </summary>
        public bool Load(System.IO.Stream stream, DateTime nowUtc)
        {
            try
            {
                var r = new System.IO.BinaryReader(stream);
                if (r.ReadInt32() != FileMagic || r.ReadInt32() != FileVersion) return false;
                var newest = DateTime.FromBinary(r.ReadInt64());
                int count = r.ReadInt32(), series = r.ReadInt32();
                if (count < 0 || count > _capacity || series < 0 || series > 256) return false;
                double gap = (nowUtc - newest).TotalSeconds;
                if (gap < 0 || gap >= _capacity) return false;
                var data = new Dictionary<string, float[]>();
                for (int s = 0; s < series; s++)
                {
                    string key = r.ReadString();
                    var values = new float[count];
                    for (int i = 0; i < count; i++) values[i] = r.ReadSingle();
                    data[key] = values;
                }
                foreach (var key in data.Keys) Ensure(key);
                var sample = new Dictionary<string, float>();
                for (int i = 0; i < count; i++)
                {
                    sample.Clear();
                    foreach (var kv in data) sample[kv.Key] = kv.Value[i];
                    Push(sample);
                }
                for (int i = 0; i < (int)gap; i++) Push(null);
                return true;
            }
            catch (System.IO.IOException) { return false; }
            catch (ArgumentException) { return false; }
        }

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
