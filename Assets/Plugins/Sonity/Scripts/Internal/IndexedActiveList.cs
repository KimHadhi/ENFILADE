// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

using System.Collections.Generic;

namespace Sonity.Internal {

    public sealed class IndexedActiveList<T> where T : notnull {

        // A lot of these functions might change active list length
        // So when iterating and activating/deactivating, make sure to think about the indexes change

        private readonly List<T> items = new List<T>();
        private readonly Dictionary<T, int> indices = new Dictionary<T, int>();

        public int CountActive { get; private set; }
        public int CountInactive => CountTotal - CountActive;
        public int CountTotal => items.Count;

        public T this[int index] => items[index];

        public bool TryGetActivatedItem(out T item) {
            if (CountActive >= items.Count) {
                item = default!;
                return false;
            }

            item = items[CountActive];
            CountActive++;
            return true;
        }

        public void AddActiveItem(T item) {
            if (indices.ContainsKey(item))
                return;

            int index = items.Count;
            items.Add(item);
            indices[item] = index;
            SwapAtIndex(index, CountActive);
            CountActive++;
        }

        public void AddInactiveItem(T item) {
            if (indices.ContainsKey(item))
                return;

            items.Add(item);
            indices[item] = items.Count - 1;
        }

        public void DeactivateItem(T item) {
            if (indices.TryGetValue(item, out int index)) {
                DeactivateAtIndex(index);
            }
        }

        public void ActivateItem(T item) {
            if (indices.TryGetValue(item, out int index)) {
                ActivateAtIndex(index);
            }
        }

        public void DeactivateAtIndex(int index) {
            if (index < 0 || index >= CountActive) {
                return;
            }
            SwapAtIndex(index, --CountActive);
        }

        public void ActivateAtIndex(int index) {
            if (index < CountActive || index >= items.Count) {
                return;
            }
            SwapAtIndex(index, CountActive++);
        }

        public void RemoveCompletelyItem(T item) {
            if (!indices.TryGetValue(item, out int index)) {
                return;
            }
            RemoveCompletelyAtIndex(index);
        }

        public void RemoveCompletelyAtIndex(int index) {
            if (index < 0 || index >= items.Count)
                return;

            if (index < CountActive) {
                SwapAtIndex(index, --CountActive);
                index = CountActive;
            }

            int lastIndex = items.Count - 1;
            SwapAtIndex(index, lastIndex);

            indices.Remove(items[lastIndex]);
            items.RemoveAt(lastIndex);
        }

        private void SwapAtIndex(int a, int b) {
            if (a == b) {
                return;
            }

            T itemA = items[a];
            T itemB = items[b];

            items[a] = itemB;
            items[b] = itemA;

            indices[itemA] = b;
            indices[itemB] = a;
        }
    }
}