using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog {
  public static partial class IEnumerableCore {
    static int FuzzyFind(this IList<double> sortedList, double value) {
      return FuzzyFind(sortedList, value, 0, sortedList.Count - 1);
    }
    static int FuzzyFind(IList<double> sortedList, double value, int indexLeft, int indexRight) {
      if (!value.Between(sortedList[0], sortedList[sortedList.Count - 1])) return -1;
      if (indexLeft == indexRight) return indexRight;
      if (value == sortedList[indexLeft]) return indexLeft;
      if (value == sortedList[indexRight]) return indexRight;
      var middle = indexLeft + (indexRight - indexLeft) / 2;
      if (value.Between(sortedList[indexLeft], sortedList[middle]))
        return FuzzyFind(sortedList, value, indexLeft, middle);
      return FuzzyFind(sortedList, value, middle + 1, indexRight);
    }
    public static int FuzzyFind<T, U>(this IList<T> sortedList, U value, Func<U, T, T, bool> isBetween) {
      return sortedList.FuzzyIndex(value, isBetween).First();
    }
    public static int[] FuzzyIndex<T, U>(this IList<T> sortedList, U value, Func<U, T, T, bool> isBetween) {
      return sortedList.FuzzyIndex(value, isBetween, (v, list, left, right) => right);
    }
    public static int[] FuzzyIndex<T, U>(this IList<T> sortedList, U value, Func<U, T, T, bool> isBetween, Func<U, IList<T>, int, int, int> chooseBetween) {
      return sortedList.FuzzyIndex(value, 0, sortedList.Count - 1, isBetween, chooseBetween);
    }
    static int[] FuzzyIndex<T, U>(this IList<T> sortedList, U value, int indexLeft, int indexRight, Func<U, T, T, bool> isBetween, Func<U, IList<T>, int, int, int> chooseBetween) {
      if (!isBetween(value, sortedList[0], sortedList[sortedList.Count - 1])) return new int[0];
      if (indexLeft == indexRight) return new[] { indexRight };
      if (isBetween(value, sortedList[indexLeft], sortedList[indexLeft])) return new[] { indexLeft };
      if (isBetween(value, sortedList[indexRight], sortedList[indexRight])) return new[] { indexRight };
      var middle = (indexLeft + indexRight) / 2;
      if (isBetween(value, sortedList[indexLeft], sortedList[middle]))
        return FuzzyIndex<T, U>(sortedList, value, indexLeft, middle, isBetween, chooseBetween);
      if (middle == indexLeft) return new[] { chooseBetween(value, sortedList, indexLeft, indexRight) };
      return FuzzyIndex<T, U>(sortedList, value, middle, indexRight, isBetween, chooseBetween);
    }
  }
}
