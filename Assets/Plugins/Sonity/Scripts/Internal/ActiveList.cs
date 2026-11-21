// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

using System;
using System.Collections.Generic;

namespace Sonity.Internal {

    public sealed class ActiveList<T> {

        // Active items are stored in indexes [0, CountActive).
        // Inactive items are stored in indexes [CountActive, CountTotal).
        //
        // Many functions can change active list length.
        // When iterating and activating/deactivating, remember that indexes may change.

        private readonly List<T> items = new List<T>();

        public int CountActive { get; private set; }
        public int CountInactive => CountTotal - CountActive;
        public int CountTotal => items.Count;

        public T this[int index] => items[index];

        public bool TryGetActiveItem(out T item) {
            if (CountActive >= items.Count) {
                item = default!;
                return false;
            }

            item = items[CountActive];
            CountActive++;
            return true;
        }

        public void AddActiveItem(T item) {
            items.Add(item);

            int newIndex = items.Count - 1;

            // Move the new item into the active region.
            SwapAtIndex(newIndex, CountActive);

            CountActive++;
        }

        public void AddInactiveItem(T item) {
            items.Add(item);
        }

        public void DeactivateAtIndex(int index) {
            if (index < 0 || index >= CountActive) {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            // Move the item to the end of the active region,
            // then shrink the active region.
            CountActive--;
            SwapAtIndex(index, CountActive);
        }

        public void ActivateAtIndex(int index) {
            if (index < CountActive || index >= items.Count) {
                return; // Already active or invalid.
            }

            // Move the inactive item to the first inactive slot,
            // then grow the active region.
            SwapAtIndex(index, CountActive);
            CountActive++;
        }

        public void Clear() {
            items.Clear();
            CountActive = 0;
        }

        public void RemoveCompletelyAtIndex(int index) {
            if (index < 0 || index >= items.Count) {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            int lastIndex = items.Count - 1;

            if (index < CountActive) {
                // Removing an active item.
                CountActive--;

                // Fill removed active slot with the last active item.
                if (index != CountActive) {
                    items[index] = items[CountActive];
                }

                // Fill the hole at the active/inactive boundary with the last item.
                if (CountActive != lastIndex) {
                    items[CountActive] = items[lastIndex];
                }

                items.RemoveAt(lastIndex);
            } else {
                // Removing an inactive item.
                SwapAtIndex(index, lastIndex);
                items.RemoveAt(lastIndex);
            }
        }

        private void SwapAtIndex(int a, int b) {
            if (a == b) {
                return;
            }

            T temp = items[a];
            items[a] = items[b];
            items[b] = temp;
        }
    }
}