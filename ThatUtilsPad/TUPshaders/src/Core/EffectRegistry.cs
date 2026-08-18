using System;
using System.Collections.Generic;

namespace TUPshaders.Core
{
    /// <summary>
    /// Ordered registry of <see cref="IEffect"/> instances. Priority ascending = earlier in the chain.
    /// </summary>
    public sealed class EffectRegistry
    {
        private readonly List<IEffect> _effects = new(16);
        private readonly Dictionary<string, IEffect> _byId = new(StringComparer.OrdinalIgnoreCase);
        private bool _dirty = true;
        private IEffect[] _sortedCache = Array.Empty<IEffect>();

        public int Count => _effects.Count;
        public IReadOnlyList<IEffect> All => _effects;

        public IReadOnlyList<IEffect> Sorted
        {
            get
            {
                if (_dirty)
                {
                    _sortedCache = _effects.ToArray();
                    Array.Sort(_sortedCache, (a, b) => a.Priority.CompareTo(b.Priority));
                    _dirty = false;
                }
                return _sortedCache;
            }
        }

        public void Add(IEffect effect)
        {
            if (_byId.ContainsKey(effect.Id))
                throw new InvalidOperationException($"Effect already registered: {effect.Id}");
            _effects.Add(effect);
            _byId[effect.Id] = effect;
            _dirty = true;
        }

        public bool TryGet(string id, out IEffect effect) => _byId.TryGetValue(id, out effect!);

        public IEffect? Get(string id) => _byId.TryGetValue(id, out var e) ? e : null;

        public IEnumerable<IEffect> ByCategory(string category)
        {
            foreach (var e in _effects)
            {
                if (string.Equals(e.Category, category, StringComparison.OrdinalIgnoreCase))
                    yield return e;
            }
        }
    }
}
