using System;
using System.Collections.Generic;
using System.Linq;

namespace FODevManager.WinUI.ViewModel
{
    public sealed class ModelSelection<T> where T : class
    {
        private readonly HashSet<T> _selected = new();
        private T? _anchor;

        public IReadOnlyCollection<T> Selected => _selected;

        public void Select(T item, List<T> visibleItems, bool extend, bool toggle)
        {
            var index = visibleItems.IndexOf(item);
            if (index < 0)
                return;

            var anchorIndex = _anchor == null ? -1 : visibleItems.IndexOf(_anchor);
            if (extend && anchorIndex >= 0)
            {
                if (!toggle)
                    _selected.Clear();

                for (var i = Math.Min(anchorIndex, index); i <= Math.Max(anchorIndex, index); i++)
                    _selected.Add(visibleItems[i]);
            }
            else
            {
                if (!toggle)
                    _selected.Clear();

                if (!toggle || !_selected.Remove(item))
                    _selected.Add(item);

                _anchor = item;
            }
        }

        public void Clear()
        {
            _selected.Clear();
            _anchor = null;
        }

        public void Reconcile(List<T> items, Func<T, T, bool> matches)
        {
            var replacements = items.Where(item => _selected.Any(selected => matches(selected, item))).ToList();
            _anchor = _anchor == null ? null : items.FirstOrDefault(item => matches(_anchor, item));
            _selected.Clear();
            _selected.UnionWith(replacements);
        }
    }
}
