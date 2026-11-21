// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

using System.Collections.Generic;

namespace Sonity.Internal {

    public static class ListUtilities {

        // If you use: for (int i = list.Count - 1; i >= 0; i--) { 
        // You can just run it, count will be correct

        // If you use: for (int i = 0; i < list.Count; i++) {
        // You DO need to increment the count

        // Like this:
        // // If count > 0 increment the index because swapback moves the value to the top
        // if (list.Count > 0) {
        //     i++;
        // }

        // Moves the index item to the last index and removes it
        // Better performance for list remove
        public static void RemoveAtSwapBack<T>(this List<T> list, int index) {
            int lastIndex = list.Count - 1;
            list[index] = list[lastIndex];
            list.RemoveAt(lastIndex);
        }
    }
}