﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace HedgeHog {
  public static partial class Lib {
    public static IEnumerable<T[]> Chunk<T>(T[] rates, int chunksLength, int chunksCount) {
      if(chunksCount>0) throw new NotSupportedException();
      var hops = Enumerable.Range(0, chunksCount).ToArray();
      var chunk = chunksLength / chunksCount;
      var chunks = hops.Select(hop => hop * chunk).ToArray();
      return chunks.Select(start => rates.CopyToArray(start, chunk));
    }

    public static IEnumerable<IEnumerable<T>> Chop<T>(this IList<T> source, int numberOfChunks) {
      var size = source.Count / numberOfChunks;
      return source.Take(size * numberOfChunks).Clump(size);
    }
    public static IEnumerable<IEnumerable<T>> ClumpToSameSize<T>(this IList<T> source, int size) {
      return source.Take((source.Count / size) * size).Clump(size);
    }
    /// <summary>
    /// Clumps items into same size lots.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="source">The source list of items.</param>
    /// <param name="size">The maximum size of the clumps to make.</param>
    /// <returns>A list of list of items, where each list of items is no bigger than the size given.</returns>
    public static IEnumerable<IEnumerable<T>> Clump<T>(this IEnumerable<T> source, int size) {
      if (source == null)
        throw new ArgumentNullException("source");
      if (size < 1)
        throw new ArgumentOutOfRangeException("size", "size must be greater than 0");

      return ClumpIterator<T>(source, size);
    }

    private static IEnumerable<IEnumerable<T>> ClumpIterator<T>(IEnumerable<T> source, int size) {
      Debug.Assert(source != null, "source is null.");

      T[] items = new T[size];
      int count = 0;
      foreach (var item in source) {
        items[count] = item;
        count++;

        if (count == size) {
          yield return items;
          items = new T[size];
          count = 0;
        }
      }
      if (count > 0) {
        if (count == size)
          yield return items;
        else {
          T[] tempItems = new T[count];
          Array.Copy(items, tempItems, count);
          yield return tempItems;
        }
      }
    }
  }
}
