using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.Concurrent;
using System.Threading;

namespace HedgeHog {

  // A static range partitioner for sources that require
  // a linear increase in processing time for each succeeding element.
  // The range sizes are calculated based on the rate of increase
  // with the first partition getting the most elements and the 
  // last partition getting the least.
  class StaticRangePartitioer : Partitioner<int> {


    int[] source;
    double rateOfIncrease = 0;
    bool _isDebug = false;

    public StaticRangePartitioer(int[] source, double rate) {
      this.source = source;
      rateOfIncrease = rate;
    }

    public override IEnumerable<int> GetDynamicPartitions() {
      throw new NotImplementedException();
    }

    // Not consumable from Parallel.ForEach.
    public override bool SupportsDynamicPartitions {
      get {
        return false;
      }
    }


    public override IList<IEnumerator<int>> GetPartitions(int partitionCount) {
      List<IEnumerator<int>> _list = new List<IEnumerator<int>>();
      int end = 0;
      int start = 0;
      int[] nums = CalculatePartitions(partitionCount, source.Length);

      for (int i = 0; i < nums.Length; i++) {
        start = nums[i];
        if (i < nums.Length - 1)
          end = nums[i + 1];
        else
          end = source.Length;

        _list.Add(GetItemsForPartition(start, end));

        // For demonstratation.
        if (_isDebug)
          Console.WriteLine("start = {0} b (end) = {1}", start, end);
      }
      return (IList<IEnumerator<int>>)_list;
    }
    /*
     * 
     * 
     *                                                               B
      // Model increasing workloads as a right triangle           /  |
         divided into equal areas along vertical lines.         / |  |
         Each partition  is taller and skinnier               /   |  |
         than the last.                                     / |   |  |
                                                          /   |   |  |
                                                        /     |   |  |
                                                      /  |    |   |  |
                                                    /    |    |   |  |
                                            A     /______|____|___|__| C
     */
    private int[] CalculatePartitions(int partitionCount, int sourceLength) {
      // Corresponds to the opposite side of angle A, which corresponds
      // to an index into the source array.
      int[] partitionLimits = new int[partitionCount];
      partitionLimits[0] = 0;

      // Represent total work as rectangle of source length times "most expensive element"
      // Note: RateOfIncrease can be factored out of equation.
      double totalWork = sourceLength * (sourceLength * rateOfIncrease);
      // Divide by two to get the triangle whose slope goes from zero on the left to "most"
      // on the right. Then divide by number of partitions to get area of each partition.
      totalWork /= 2;
      double partitionArea = totalWork / partitionCount;

      // Draw the next partitionLimit on the vertical coordinate that gives
      // an area of partitionArea * currentPartition. 
      for (int i = 1; i < partitionLimits.Length; i++) {
        double area = partitionArea * i;

        // Solve for base given the area and the slope of the hypotenuse.
        partitionLimits[i] = (int)Math.Floor(Math.Sqrt((2 * area) / rateOfIncrease));
      }
      return partitionLimits;
    }


    IEnumerator<int> GetItemsForPartition(int start, int end) {
      // For demonstration purpsoes. Each thread receives its own enumerator.
      if (_isDebug)
        Console.WriteLine("called on thread {0}", Thread.CurrentThread.ManagedThreadId);
      for (int i = start; i < end; i++)
        yield return source[i];
    }
  }

  class StaticRangePartitioner : Partitioner<Tuple<int,int>> {


    int[] source;
    double rateOfIncrease = 0;
    bool _isDebug = false;

    public StaticRangePartitioner(int[] source, double rate) : this(source, rate, false) { }
    public StaticRangePartitioner(int[] source, double rate,bool isDebug) {
      this.source = source;
      rateOfIncrease = rate;
      _isDebug = isDebug;
    }

    public override IEnumerable<Tuple<int, int>> GetDynamicPartitions() {
      throw new NotImplementedException();
    }

    // Not consumable from Parallel.ForEach.
    public override bool SupportsDynamicPartitions {
      get {
        return false;
      }
    }

    public override IList<IEnumerator<Tuple<int, int>>> GetPartitions(int partitionCount) {
      var _list = new List<IEnumerator<Tuple<int, int>>>();
      int end = 0;
      int start = 0;
      int[] nums = CalculatePartitions(partitionCount, source.Length);

      for (int i = 0; i < nums.Length; i++) {
        start = nums[i];
        if (i < nums.Length - 1)
          end = nums[i + 1];
        else
          end = source.Length;

        _list.Add(GetItemsForPartition(start, end));

        // For demonstratation.
        if (_isDebug)
          Console.WriteLine("start = {0} b (end) = {1}", start, end);
      }
      return _list;
    }
    /*
     * 
     * 
     *                                                               B
      // Model increasing workloads as a right triangle           /  |
         divided into equal areas along vertical lines.         / |  |
         Each partition  is taller and skinnier               /   |  |
         than the last.                                     / |   |  |
                                                          /   |   |  |
                                                        /     |   |  |
                                                      /  |    |   |  |
                                                    /    |    |   |  |
                                            A     /______|____|___|__| C
     */
    private int[] CalculatePartitions(int partitionCount, int sourceLength) {
      // Corresponds to the opposite side of angle A, which corresponds
      // to an index into the source array.
      int[] partitionLimits = new int[partitionCount];
      partitionLimits[0] = 0;

      // Represent total work as rectangle of source length times "most expensive element"
      // Note: RateOfIncrease can be factored out of equation.
      double totalWork = sourceLength * (sourceLength * rateOfIncrease);
      // Divide by two to get the triangle whose slope goes from zero on the left to "most"
      // on the right. Then divide by number of partitions to get area of each partition.
      totalWork /= 2;
      double partitionArea = totalWork / partitionCount;

      // Draw the next partitionLimit on the vertical coordinate that gives
      // an area of partitionArea * currentPartition. 
      for (int i = 1; i < partitionLimits.Length; i++) {
        double area = partitionArea * i;

        // Solve for base given the area and the slope of the hypotenuse.
        partitionLimits[i] = (int)Math.Floor(Math.Sqrt((2 * area) / rateOfIncrease));
      }
      return partitionLimits;
    }


    IEnumerator<Tuple<int,int>> GetItemsForPartition(int start, int end) {
      // For demonstration purpsoes. Each thread receives its own enumerator.
      if (_isDebug)
        Console.WriteLine("called on thread {0}", Thread.CurrentThread.ManagedThreadId);
      yield return new Tuple<int, int>(start, end);
    }
  }
}
