/* License
 * 
 * This software is provided "as is" - without any warranty, without even the implied warranty of
 * merchantability or fitness for a particular purpose. I do not accept liability for any 
 * harm or data or financial loss due to the use of the software.
 * 
 * You use this software at your own risk.
 * 
 * This software may be redistributed providing that
 * 
 * (1) the software will not be sold for profit without the author's written consent,
 * (2) all copyright notices remain unmodified,
 * (3) the source code remains unmodified.
 * 
 * 
 * Please send an E-Mail to v.thieme_(at)_t-online.de if you are planning to use this software.
 * 
 * Copyright Dr. Volker Thieme, Leipzig, Germany, 2006 - 2007
 */
using System;
using System.Collections.Generic;
using System.Collections;

namespace VTClasses
{
  /// <summary>
  /// A generic structure storing cumulative frequencies
  /// </summary>
  public struct CumulativeFrequencyTableEntry<T>
  {
    /// <summary>
    /// Initializes a new instance
    /// </summary>
    /// <param name="value">Value counted</param>
    /// <param name="CumRelativeFrequency">Cumulative relative frequency</param>
    /// <param name="CumAbsoluteFrequency">Cumulative absolute frequency</param>
    public CumulativeFrequencyTableEntry(T value, double CumRelativeFrequency, int CumAbsoluteFrequency)
    {
      Value = value;
      CumulativeRelativeFrequency = CumRelativeFrequency;
      CumulativeAbsoluteFrequency = CumAbsoluteFrequency;
    }
    /// <summary>
    /// Value
    /// </summary>
    public T Value;
    /// <summary>
    /// Cumulative relative frequency
    /// </summary>
    public double CumulativeRelativeFrequency;
    /// <summary>
    /// Cumulative absolute frequency
    /// </summary>
    public int CumulativeAbsoluteFrequency;
  }
  /// <summary>
  /// Enumeration defining the sort order
  /// </summary>
  public enum FrequencyTableSortOrder
  {
    /// <summary>
    /// Sort by value ascending
    /// </summary>
    Value_Ascending,
    /// <summary>
    /// Sort by value descending
    /// </summary>
    Value_Descending,
    /// <summary>
    /// Sort by frequency ascending
    /// </summary>
    Frequency_Ascending,
    /// <summary>
    /// Sort by frequency descending
    /// </summary>
    Frequency_Descending,
    /// <summary>
    /// Do not sort
    /// </summary>
    None
  }

  /// <summary>
  /// Enumeration defining the mode of literal frequency analysis
  /// </summary>
  public enum TextAnalyzeMode
  {
    /// <summary>
    /// Analyze all characters
    /// </summary>
    AllCharacters,
    /// <summary>
    /// Do not include numerals
    /// </summary>
    NoNumerals,
    /// <summary>
    /// Do not include special characters
    /// </summary>
    NoSpecialCharacters,
    /// <summary>
    /// Analyze letters only
    /// </summary>
    LettersOnly,
    /// <summary>
    /// Analyze numerals only
    /// </summary>
    NumeralsOnly,
    /// <summary>
    /// Analyze special characters only
    /// </summary>
    SpecialCharactersOnly
  }

  /// <summary>
  /// Eunumeration defining the format of a CumulativeFrequencyTable
  /// </summary>
  public enum CumulativeFrequencyTableFormat
  {
    /// <summary>
    /// Each datapoint
    /// </summary>
    EachDatapoint,
    /// <summary>
    /// Each datapoint once
    /// </summary>
    EachDatapointOnce
  }

  /// <summary>
  /// A generic structure storing the frequency information for each value
  /// </summary>
  public struct FrequencyTableEntry<T> where T : IComparable<T>
  {
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="val">The value counted</param>
    /// <param name="absFreq">The absolute frequency</param>
    /// <param name="relFreq">The relative frequency</param>
    /// <param name="percentage">The percentage</param>
    public FrequencyTableEntry(T val, int absFreq, double relFreq, double percentage)
    {
      Value = val;
      AbsoluteFreq = absFreq;
      RelativeFreq = relFreq;
      Percentage = percentage;
    }
    /// <summary>
    /// Counted value
    /// </summary>
    public T Value;
    /// <summary>
    /// Absolute Frequency
    /// </summary>
    public int AbsoluteFreq;
    /// <summary>
    /// Relative Frequency
    /// </summary>
    public double RelativeFreq;
    /// <summary>
    /// Percentage
    /// </summary>
    public double Percentage;
    ///// <summary>
    ///// stores the input order of the added value (i.e. the position of the value inside the "input array"
    ///// </summary>
    //public List<int> Position;
  }

  /// <summary>
  /// A generic frequency table
  /// </summary>
  /// <typeparam name="T">Type of values to count (must implement IComparable)</typeparam>
  public class FrequencyTable<T> : IEnumerable<FrequencyTableEntry<T>> where T : IComparable<T>
  {
    #region privates
    Dictionary<T, List<int>> _positions;
    // stores the relative frequencies
    private Hashtable _relFrequencies;
    // store the values and frequencies
    // it is possible to use a dictionary like this:
    // private Dictionary<T, FrequencyTableEntry<T>>
    private Dictionary<T, int> _entries;
    // number of elements in _entries 
    private int _length;
    // number of elements counted (actually the sample size)
    private int _count;
    // store the user-defined tag
    private object _tag;
    // store the description
    private string _description;
    // highest frequency
    private int _high;
    // mode
    private T _mode;
    // double sum over all added values
    private double _dblSum;
    // mean
    private double _mean;
    // alpha value used bei KS test
    double _alpha;
    // stores the p-value computed by KS_Test
    double _p;
    #endregion
    #region constructors
    /// <summary>
    /// Default constructor
    /// </summary>
    public FrequencyTable()
    {
      _positions = new Dictionary<T, List<int>>();
      _entries = new Dictionary<T, int>();
      _relFrequencies = new Hashtable();
      _length = 0;
      _count = 0;
      _description = "";
      _tag = null;
      _high = 0;
      _dblSum = 0.0;
      _mean = 0.0;
      _alpha = 0.05;
      _p = double.NaN;
    }
    /// <summary>
    /// Constructor - the created instance analyzes the frequency of characters in a given string
    /// </summary>
    /// <param name="Text">String to analyze</param>
    /// <param name="mode">Mode</param>
    public FrequencyTable(T Text, TextAnalyzeMode mode)
    {
      _positions = new Dictionary<T, List<int>>();
      // if T is not string -> Exception
      if (!(Text is string))
        throw new ArgumentException();
      // the table itself
      _entries = new Dictionary<T, int>();
      _relFrequencies = new Hashtable();
      // number of entries in _entries
      _length = 0;
      // sample size
      _count = 0;
      // description of the table
      _description = "";
      // a user defined tag
      _tag = 0;
      // the highest frequency
      _high = 0;
      _dblSum = double.NaN;
      _mean = double.NaN;
      _alpha = double.NaN;
      AnalyzeString(Text, mode);
      _p = double.NaN;
    }
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="initialCapacity">The expected number of entries</param>
    public FrequencyTable(int initialCapacity)
    {
      _positions = new Dictionary<T, List<int>>();
      _entries = new Dictionary<T, int>(initialCapacity);
      _relFrequencies = new Hashtable();
      _length = initialCapacity;
      _count = 0;
      _description = "";
      _tag = null;
      _high = 0;
      _dblSum = 0.0;
      _mean = 0.0;
      _alpha = 0.05;
    }
    /// <summary>
    /// Initializes a new instance using an array
    /// </summary>
    /// <param name="Sample">Array of values to explore</param>
    public FrequencyTable(T[] Sample)
    {
      _positions = new Dictionary<T, List<int>>();
      _entries = new Dictionary<T, int>();
      _relFrequencies = new Hashtable();
      _length = 0;
      _count = 0;
      _description = "";
      _tag = 0;
      _high = 0;
      _dblSum = 0.0;
      _mean = 0.0;
      foreach (T v in Sample)
        Add(v);
      _alpha = 0.05;
      _p = double.NaN;
    }

    #endregion
    #region methods
    /// <summary>
    /// Analyzes a given string
    /// </summary>
    /// <param name="Text">String to analyze</param>
    /// <param name="mode">Analyze mode <see cref="TextAnalyzeMode"/></param>
    private void AnalyzeString(T Text, TextAnalyzeMode mode)
    {
      // character strings
      string str_specialChars = @"""!§$%&/()=?@€<>|µ,.;:-_#'*+~²³ ";
      string str_Numbers = "0123456789";
      // Add entries according to mode
      switch (mode)
      {
        case TextAnalyzeMode.AllCharacters:
          foreach (char v in Text.ToString())
            Add((T)Convert.ChangeType((object)v, Text.GetType()));
          break;
        case TextAnalyzeMode.LettersOnly:
          foreach (char v in Text.ToString())
          {
            if ((str_specialChars.IndexOf(v) == -1) & (str_Numbers.IndexOf(v) == -1))
              Add((T)Convert.ChangeType((object)v, Text.GetType()));
          }
          break;
        case TextAnalyzeMode.NoNumerals:
          foreach (char v in Text.ToString())
          {
            if (str_Numbers.IndexOf(v) == -1)
              Add((T)Convert.ChangeType((object)v, Text.GetType()));
          }
          break;
        case TextAnalyzeMode.NoSpecialCharacters:
          foreach (char v in Text.ToString())
          {
            if (str_specialChars.IndexOf(v) == -1)
              Add((T)Convert.ChangeType((object)v, Text.GetType()));
          }
          break;
        case TextAnalyzeMode.NumeralsOnly:
          foreach (char v in Text.ToString())
          {
            if (str_Numbers.IndexOf(v) != -1)
              Add((T)Convert.ChangeType((object)v, Text.GetType()));
          }
          break;
        case TextAnalyzeMode.SpecialCharactersOnly:
          foreach (char v in Text.ToString())
          {
            if (str_specialChars.IndexOf(v) != -1)
              Add((T)Convert.ChangeType((object)v, Text.GetType()));
          }
          break;
      }
    }
    /// <summary>
    /// Adds a new entry.
    /// </summary>
    /// <param name="value">Value to count</param>
    public void Add(T value)
    {
      List<int> _tempPos;
      if (_entries.ContainsKey(value))
      {
        // update the frequency
        _entries[value]++;
        // update mode and highest frequency
        if (_entries[value] > _high)
        {
          _high = _entries[value];
          _mode = value;
        }
        // add 1 to sample size
        _count++;
        foreach (T key in _entries.Keys)
        {
          _relFrequencies[key] = (double)_entries[key] / _count;
        }
        UpdateSumAndMean(value);
        // store the actual position of the entry in the dataset
        _positions.TryGetValue(value, out _tempPos);
        // the position is equal to _count
        _tempPos.Add(_count);
        // remove old entry
        _positions.Remove(value);
        // store new entry
        _positions.Add(value, _tempPos);
      }
      else
      {
        // if the highest frequency is still zero, set it to one
        if (_high < 1)
        {
          _high = 1;
          _mode = value;
        }
        // add a new entry - frequency is one
        _entries.Add(value, 1);
        // add 1 to table length
        _length++;
        // add 1 to sample size
        _count++;
        // update relative frequencies
        _relFrequencies.Add(value, 0.0);
        foreach (T key in _entries.Keys)
        {
          _relFrequencies[key] = (double)_entries[key] / _count;
        }
        UpdateSumAndMean(value);
        // create a new entry and set position to _count
        _tempPos = new List<int>();
        _tempPos.Add(_count);
        // store it
        _positions.Add(value, _tempPos);
      }
    }
    /// <summary>
    /// Special method to add a string
    /// </summary>
    /// <param name="Text">String to add</param>
    /// <param name="mode"></param>
    public void Add(T Text, TextAnalyzeMode mode)
    {
      if (!(Text is string))
        throw new ArgumentException();
      AnalyzeString(Text, mode);
    }
    /// <summary>
    /// Removes an entry
    /// </summary>
    /// <param name="value">Value to remove</param>
    public void Remove(T value)
    {
      if (_entries.ContainsKey(value))
      {
        // Update length and sample size
        _count = _count - _entries[value];
        _length--;
        // Remove the entry
        _entries.Remove(value);
      }
      else
        throw new InvalidOperationException();
    }

    /// <summary>
    /// Updates sum and mean over all values
    /// </summary>
    /// <param name="value">Value</param>
    private void UpdateSumAndMean(T value)
    {
      // error handling is a bit circuitous (but safe IMHO)
      if ((value is string) || (value is char))
      {
        _dblSum = double.NaN;
        _mean = double.NaN;
      }
      else
      {
        // if value is not a numerical type, the thrown exception will be suppressed
        try
        {
          _dblSum += (double)Convert.ChangeType(value, TypeCode.Double);
          _mean = _dblSum / (double)SampleSize;
        }
        catch
        {
          _dblSum = double.NaN;
          _mean = double.NaN;
        }
      }
    }
    /// <summary>
    /// Computes the r<sup>th</sup> moment about the mean (the r<sup>th</sup> central moment).
    /// </summary>
    /// <param name="r">r</param>
    /// <returns>The r<sup>th</sup> moment. If data are not numerical, double.NaN will be returned</returns>
    private double ComputeMoment(int r)
    {
      double _result = 0.0;
      try
      {
        foreach (FrequencyTableEntry<T> entry in this)
        {
          _result += Math.Pow((double)Convert.ChangeType(entry.Value, TypeCode.Double) - _mean, r) * (double)entry.AbsoluteFreq;
        }
      }
      catch
      {
        _result = double.NaN;
      }
      finally
      {
        _result = _result / (double)SampleSize;
      }
      return _result;
    }
    /// <summary>
    /// Returns an generic enumerator object 
    /// </summary>
    /// <returns>IEnumerator"/></returns>
    public IEnumerator<FrequencyTableEntry<T>> GetEnumerator()
    {
      // the structure to return
      FrequencyTableEntry<T> _output;
      // the frequency
      int int_f;
      foreach (T key in _entries.Keys)
      {
        int_f = _entries[key];
        // fill the structure
        _output = new FrequencyTableEntry<T>(key, int_f, (double)_relFrequencies[key], (double)_relFrequencies[key] * 100.0);
        // yielding - cool thing that
        yield return _output;
      }
    }
    /// <summary>
    /// The "standard" enumerator
    /// </summary>
    /// <returns>The enumerator</returns>
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
    {
      return GetEnumerator();
    }
    /// <summary>
    /// Returns an array of table entries
    /// </summary>
    /// <returns>Table entries</returns>
    public FrequencyTableEntry<T>[] GetTableAsArray()
    {
      FrequencyTableEntry<T>[] _output = new FrequencyTableEntry<T>[_length];
      int i = 0;
      foreach (FrequencyTableEntry<T> entry in this)
      {
        _output[i] = entry;
        i++;
      }
      return _output;
    }
    /// <summary>
    /// Returns a sorted/unsorted array of table entries
    /// </summary>
    /// <param name="order">Sorting order</param>
    /// <returns>Table entries</returns>
    public FrequencyTableEntry<T>[] GetTableAsArray(FrequencyTableSortOrder order)
    {
      FrequencyTableEntry<T>[] _output = null;
      switch (order)
      {
        case FrequencyTableSortOrder.None:
          _output = GetTableAsArray();
          break;
        case FrequencyTableSortOrder.Frequency_Ascending:
          _output = SortFrequencyTable<T>.SortTable(GetTableAsArray(), FrequencyTableSortOrder.Frequency_Ascending);
          break;
        case FrequencyTableSortOrder.Frequency_Descending:
          _output = SortFrequencyTable<T>.SortTable(GetTableAsArray(), FrequencyTableSortOrder.Frequency_Descending);
          break;
        case FrequencyTableSortOrder.Value_Ascending:
          _output = SortFrequencyTable<T>.SortTable(GetTableAsArray(), FrequencyTableSortOrder.Value_Ascending);
          break;
        case FrequencyTableSortOrder.Value_Descending:
          _output = SortFrequencyTable<T>.SortTable(GetTableAsArray(), FrequencyTableSortOrder.Value_Descending);
          break;
      }
      return _output;
    }
    /// <summary>
    /// Returns the data as an array
    /// </summary>
    /// <returns>Data</returns>
    /// <param name="Pristine">If true, GetData returns the array in chronological order (as entered)</param>
    public T[] GetData(bool Pristine)
    {
      T[] result = new T[SampleSize];
      if (!Pristine)
      {
        CumulativeFrequencyTableEntry<T>[] cf = GetCumulativeFrequencyTable(CumulativeFrequencyTableFormat.EachDatapoint);
        for (int i = 0; i < SampleSize; i++)
          result[i] = cf[i].Value;
      }
      else
      {
        List<int> l;
        foreach (T key in _positions.Keys)
        {
          _positions.TryGetValue(key, out l);
          foreach (int k in l)
          {
            result[k - 1] = key;
          }
        }
      }
      return result;
    }
    /// <summary>
    /// Returns the relative frequency of a particular value
    /// </summary>
    /// <param name="value">Value</param>
    /// <param name="relFreq">Relative frequency</param>
    /// <returns>True, if value exists</returns>
    public bool GetRelativeFrequency(T value, out double relFreq)
    {
      if (_relFrequencies.ContainsKey(value))
      {
        relFreq = (double)_relFrequencies[value];
        return true;
      }
      else
      {
        relFreq = double.NaN;
        return false;
      }
    }

    /// <summary>
    /// Returns the cumulated frequencies
    /// </summary>
    /// <returns>Array: CumulativeFrequencyTableEntry&lt;T&gt;[]</returns>
    public CumulativeFrequencyTableEntry<T>[] GetCumulativeFrequencyTable(CumulativeFrequencyTableFormat Format)
    {
      CumulativeFrequencyTableEntry<T>[] _output = null;
      // get the frequency table as array for easier processing
      FrequencyTableEntry<T>[] _freqTable = GetTableAsArray(FrequencyTableSortOrder.Value_Ascending);
      // temporary values
      double tempCumRelFreq = 0.0;
      int tempCumAbsFreq = 0;
      int i, k;
      switch (Format)
      {
        case CumulativeFrequencyTableFormat.EachDatapoint:
          // initialize the result
          _output = new CumulativeFrequencyTableEntry<T>[SampleSize];
          for (i = 0; i < _freqTable.Length; i++)
          {
            // update the cumulative frequency - relative and absolute
            tempCumAbsFreq += _freqTable[i].AbsoluteFreq;
            tempCumRelFreq += _freqTable[i].RelativeFreq;
            // fill the array
            for (k = tempCumAbsFreq - _freqTable[i].AbsoluteFreq; k < tempCumAbsFreq; k++)
            {
              _output[k] = new CumulativeFrequencyTableEntry<T>(_freqTable[i].Value, tempCumRelFreq, tempCumAbsFreq);
            }
          }
          break;
        case CumulativeFrequencyTableFormat.EachDatapointOnce:
          // initialize the result
          _output = new CumulativeFrequencyTableEntry<T>[Length];
          for (i = 0; i < _freqTable.Length; i++)
          {
            // update the cumulative frequency - relative and absolute
            tempCumAbsFreq += _freqTable[i].AbsoluteFreq;
            tempCumRelFreq += _freqTable[i].RelativeFreq;
            // fill the array
            _output[i] = new CumulativeFrequencyTableEntry<T>(_freqTable[i].Value, tempCumRelFreq, tempCumAbsFreq);
          }
          break;
      }
      // done
      return _output;
    }
    /// <summary>
    /// Performs the Kolmogorov-Smirnov test.
    /// </summary>
    /// <param name="p">The p-value</param>
    /// <returns>True, if the test is applicable, false if not.</returns>
    /// <remarks>
    /// The Kolmogorov-Smirnov test is a Goodness-Of-Fit (GOF) test. 
    /// This test ist often used to test if a given set of (experimental) data is 
    /// Gaussian-distributed. Especially in case of few and unclassified data
    /// this test is very robust.
    /// Alternatively one can use the D'Agostino-Pearson test.
    /// </remarks>
    private bool KS_Test(out double p)
    {
      // D-statistics
      double D = double.NaN;
      CumulativeFrequencyTableEntry<T>[] empCDF = GetCumulativeFrequencyTable(CumulativeFrequencyTableFormat.EachDatapointOnce);
      // store the test CDF
            double testCDF;
      // array to store datapoints
      double[] data = new double[empCDF.Length];
      FrequencyTableEntry<T>[] table = GetTableAsArray(FrequencyTableSortOrder.Value_Ascending);
      int i = 0;
      // prevent exceptions if T is not numerical
      try
      {
        foreach (FrequencyTableEntry<T> entry in table)
        {
          data[i] = (double)Convert.ChangeType(entry.Value, TypeCode.Double);
          i++;
        }
      }
      catch
      {
        p = double.NaN;
        return false;
      }
      // estimate the parameters of the expected Gaussian distribution
      // first: compute the mean
      double mean = Mean;
      // compute the bias-corrected variance
      // as an estimator for the population variance
      double _sqrt_var = Math.Sqrt(VariancePop);
      // now we have to determine the greatest difference between the
      // sample cumulative distribution function (empCDF) and
      // the distribution function to test (testCDF)
      double _sqrt2 = Math.Sqrt(2.0);
      double _erf;
            double max1 = 0.0;
            double max2 = 0.0;
            double _temp;
      for (i = 0; i < empCDF.Length; i++)
      {
        // compute the expected distribution using the error function
        _erf = Erf(((data[i] - mean) / _sqrt_var) / _sqrt2);
        testCDF = 0.5 * (1.0 + _erf);
                _temp = Math.Abs(empCDF[i].CumulativeRelativeFrequency - testCDF);
                if (_temp > max1)
                    max1 = _temp;
        if (i > 0)
                    _temp = Math.Abs(empCDF[i - 1].CumulativeRelativeFrequency - testCDF);
                else
                    _temp = testCDF;
                if (_temp > max2)
                    max2 = _temp;
      }
      // the statistics to use is
      // max{diff1,diff2}
      D = max1 > max2 ? max1 : max2;
      // now compute the p-value using a z-transformation
      if (!Double.IsNaN(D))
      {
        double z = Math.Sqrt((double)SampleSize) * D;
        p = KS_Prob_Smirnov(z);
      }
      else
        p = double.NaN;
      return true;
    }
    /// <summary>
    /// Returns the largest value of a given array
    /// </summary>
    /// <param name="data">Array</param>
    /// <returns>Maximum</returns>
    private static double Max(double[] data)
    {
      Array.Sort(data);
      return data[data.Length - 1];
    }
    /// <summary>
    /// Calculation of the p-value according to Smirnov
    /// </summary>
    /// <param name="z">The transformed D-statistic</param>
    /// <returns>The p-value</returns>
    private static double KS_Prob_Smirnov(double z)
    {
      double result = 0.0;
      double q;
      if ((z >= 0) & (z < 0.27))
        result = 1.0;
      if ((z >= 0.27) & (z < 1.0))
      {
        q = Math.Exp(-1.233701 * Math.Pow(z, -2.0));
        result = 1.0 - ((2.506628 * (q + Math.Pow(q, 9.0) + Math.Pow(q, 25))) / z);
      }
      if ((z >= 1.0) & (z < 3.1))
      {
        q = Math.Exp(-2.0 * Math.Pow(z, 2.0));
        result = 2.0 * (q - Math.Pow(q, 4.0) + Math.Pow(q, 9.0) - Math.Pow(q, 16));
      }
      if (z > 3.1)
        result = 0.0;
      return (result);
    }
    #region CodeProject SpecialFunction by Stampar
    /*
     * this region contains methods needed to compute Gaussian CDF
     * taken from http://www.codeproject.com/useritems/SpecialFunction.asp
     * Thanks to Miroslav stampar
     */
    /// <summary>
    /// Computes the error function
    /// </summary>
    /// <param name="x">Value</param>
    /// <returns>Erf(x)</returns>
    /// <remarks>
    /// Copyright (C) 1984 Stephen L. Moshier (original C version - Cephes Math Library)<BR />
    /// Copyright (C) 1996 Leigh Brookshaw	(Java version)<BR />
    /// Copyright (C) 2005 Miroslav Stampar
    /// </remarks>
    private static double Erf(double x)
    {
      double y, z;
      double[] T = {
             9.60497373987051638749E0,
             9.00260197203842689217E1,
             2.23200534594684319226E3,
             7.00332514112805075473E3,
             5.55923013010394962768E4
           };
      double[] U = {
             3.35617141647503099647E1,
             5.21357949780152679795E2,
             4.59432382970980127987E3,
             2.26290000613890934246E4,
             4.92673942608635921086E4
           };

      if (Math.Abs(x) > 1.0) return (1.0 - erfc(x));
      z = x * x;
      y = x * polevl(z, T, 4) / p1evl(z, U, 5);
      return y;
    }
    /// <summary>
    /// Evaluates polynomial of degree N with assumption that coef[N] = 1.0
    /// </summary>
    /// <param name="x">Value</param>
    /// <param name="coef">Array of coefficients</param>
    /// <param name="N">Degree</param>
    /// <returns>Evaluated polynomial</returns>
    /// <remarks>
    /// Copyright (C) 1984 Stephen L. Moshier (original C version - Cephes Math Library)<BR />
    /// Copyright (C) 1996 Leigh Brookshaw	(Java version)<BR />
    /// Copyright (C) 2005 Miroslav Stampar
    /// </remarks>
    private static double p1evl(double x, double[] coef, int N)
    {
      double ans;

      ans = x + coef[0];

      for (int i = 1; i < N; i++)
      {
        ans = ans * x + coef[i];
      }

      return ans;
    }
    private const double MAXLOG = 7.09782712893383996732E2;

    /// <summary>
    /// Computes the complementary error function
    /// </summary>
    /// <param name="a"></param>
    /// <returns></returns>
    /// <remarks>
    /// Copyright (C) 1984 Stephen L. Moshier (original C version - Cephes Math Library)<BR />
    /// Copyright (C) 1996 Leigh Brookshaw	(Java version)<BR />
    /// Copyright (C) 2005 Miroslav Stampar
    /// </remarks>
    private static double erfc(double a)
    {
      double x, y, z, p, q;

      double[] P = {
             2.46196981473530512524E-10,
             5.64189564831068821977E-1,
             7.46321056442269912687E0,
             4.86371970985681366614E1,
             1.96520832956077098242E2,
             5.26445194995477358631E2,
             9.34528527171957607540E2,
             1.02755188689515710272E3,
             5.57535335369399327526E2
           };
      double[] Q = {
             //1.0
             1.32281951154744992508E1,
             8.67072140885989742329E1,
             3.54937778887819891062E2,
             9.75708501743205489753E2,
             1.82390916687909736289E3,
             2.24633760818710981792E3,
             1.65666309194161350182E3,
             5.57535340817727675546E2
           };

      double[] R = {
             5.64189583547755073984E-1,
             1.27536670759978104416E0,
             5.01905042251180477414E0,
             6.16021097993053585195E0,
             7.40974269950448939160E0,
             2.97886665372100240670E0
           };
      double[] S = {
             //1.00000000000000000000E0, 
             2.26052863220117276590E0,
             9.39603524938001434673E0,
             1.20489539808096656605E1,
             1.70814450747565897222E1,
             9.60896809063285878198E0,
             3.36907645100081516050E0
           };

      if (a < 0.0) x = -a;
      else x = a;

      if (x < 1.0) return 1.0 - Erf(a);

      z = -a * a;

      if (z < -MAXLOG)
      {
        if (a < 0) return (2.0);
        else return (0.0);
      }

      z = Math.Exp(z);

      if (x < 8.0)
      {
        p = polevl(x, P, 8);
        q = p1evl(x, Q, 8);
      }
      else
      {
        p = polevl(x, R, 5);
        q = p1evl(x, S, 6);
      }

      y = (z * p) / q;

      if (a < 0) y = 2.0 - y;

      if (y == 0.0)
      {
        if (a < 0) return 2.0;
        else return (0.0);
      }


      return y;
    }
    /// <summary>
    /// Evalutaes a polynomial of degree N
    /// </summary>
    /// <param name="x">Value</param>
    /// <param name="coef">Coefficients</param>
    /// <param name="N">Degree</param>
    /// <returns>Evaluated polynomial</returns>
    /// <remarks>
    /// Copyright (C) 1984 Stephen L. Moshier (original C version - Cephes Math Library)<BR />
    /// Copyright (C) 1996 Leigh Brookshaw	(Java version)<BR />
    /// Copyright (C) 2005 Miroslav Stampar
    /// </remarks>
    private static double polevl(double x, double[] coef, int N)
    {
      double ans;

      ans = coef[0];

      for (int i = 1; i <= N; i++)
      {
        ans = ans * x + coef[i];
      }

      return ans;
    }
    #endregion
    /// <summary>
    /// Determines if the given value exists
    /// </summary>
    /// <param name="value">Value to check</param>
    /// <returns>True, if value exists</returns>
    public bool ContainsValue(T value)
    {
      return _entries.ContainsKey(value);
    }
    #endregion
    #region properties
    /// <summary>
    /// The actual number of entries
    /// </summary>
    public int Length
    {
      get
      {
        return _length;
      }
    }
    /// <summary>
    /// The sample size
    /// </summary>
    public int SampleSize
    {
      get
      {
        return _count;
      }
    }
    /// <summary>
    /// A user-defined tag
    /// </summary>
    public object Tag
    {
      get
      {
        return _tag;
      }
      set
      {
        _tag = value;
      }
    }
    /// <summary>
    /// A description text
    /// </summary>
    public string Description
    {
      get
      {
        return _description;
      }
      set
      {
        _description = value;
      }
    }
    /// <summary>
    /// Returns the scarcest value (actually the first occurence of the lowest frequency is considered)
    /// </summary>
    public T ScarcestValue
    {
      get
      {
        // the largest possible frequency is _count
        int f = _count + 1;
        T v = default(T);
        foreach (T value in _entries.Keys)
        {
          if (_entries[value] < f)
          {
            v = value;
            f = _entries[value];
          }
        }
        return v;
      }
    }
    /// <summary>
    /// Returns the most frequent value
    /// </summary>
    public T Mode
    {
      get
      {
        return _mode;
      }
    }


    /// <summary>
    /// Returns the highest observed frequency
    /// </summary>
    public int HighestFrequency
    {
      get
      {
        return _high;
      }
    }
    /// <summary>
    /// The arithmetic mean. If the data are not numerical, double.NaN will be returned.
    /// </summary>
    public double Mean
    {
      get
      {
        return _mean;
      }
    }
    /// <summary>
    /// Returns the median. If the data are not numerical, double.NaN will be returned.
    /// </summary>
    public double Median
    {
      get
      {
        if (!Double.IsNaN(_mean))
        {
          T[] _data = GetData(false);
          if ((SampleSize % 2) == 0)
            return ((double)Convert.ChangeType(_data[(SampleSize - 1) / 2 + 1], TypeCode.Double) + (double)Convert.ChangeType(_data[(SampleSize - 1) / 2], TypeCode.Double)) / 2.0;
          else
            return (double)Convert.ChangeType(_data[(SampleSize - 1) / 2], TypeCode.Double);
        }
        else
          return double.NaN;
      }
    }

    /// <summary>
    /// The sum over all datapoints. If the data are not numerical, double.NaN will be returned.
    /// </summary>
    public double Sum
    {
      get
      {
        return _dblSum;
      }
    }
    /// <summary>
    /// Returns the lowest observed frequency
    /// </summary>
    public int SmallestFrequency
    {
      get
      {
        FrequencyTableEntry<T>[] f = GetTableAsArray(FrequencyTableSortOrder.Frequency_Ascending);
        return f[0].AbsoluteFreq;
      }
    }
    /// <summary>
    /// Returns the unbiased estimator for the population variance. If data are not numerical, double.NaN will be returned.
    /// </summary>
    /// <remarks>Checked using Mathematica 5.0</remarks>
    public double VariancePop
    {
      get
      {
        try
        {
          double temp;
          double s = 0.0;
          FrequencyTableEntry<T>[] table = GetTableAsArray(FrequencyTableSortOrder.Value_Ascending);
          foreach (FrequencyTableEntry<T> entry in table)
          {
            temp = (double)Convert.ChangeType(entry.Value, TypeCode.Double);
            s += (double)entry.AbsoluteFreq * ((temp - _mean) * (temp - _mean));
          }
          return s / ((double)SampleSize - 1.0);
        }
        catch
        {
          return double.NaN;
        }
      }
    }
    /// <summary>
    /// Returns the sample variance (NOT bias corrected). If data are not numerical, double.NaN will be returned.
    /// </summary>
    /// <remarks>Checked using Mathematica 5.0</remarks>
    public double VarianceSample
    {
      get
      {
        try
        {
          double temp;
          double s = 0.0;
          FrequencyTableEntry<T>[] table = GetTableAsArray(FrequencyTableSortOrder.Value_Ascending);
          foreach (FrequencyTableEntry<T> entry in table)
          {
            temp = (double)Convert.ChangeType(entry.Value, TypeCode.Double);
            s += (double)entry.AbsoluteFreq * ((temp - _mean) * (temp - _mean));
          }
          return s / (double)SampleSize;
        }
        catch
        {
          return double.NaN;
        }
      }
    }
    /// <summary>
    /// Returns the unbiased estimator for the poulation standard deviation. If data are not numerical, double.NaN will be returned.
    /// </summary>
    /// <remarks>Checked using Mathematica 5.0</remarks>
    public double StandardDevPop
    {
      get
      {
        if (double.IsNaN(VariancePop))
          return double.NaN;
        else
          return Math.Sqrt(VariancePop);
      }
    }
    /// <summary>
    /// Returns the bias-corrected standard deviation for the sample. If data are not numerical, double.NaN will be returned.
    /// </summary>
    /// <remarks>Checked using Mathematica 5.0</remarks>
    public double StandardDevSample
    {
      get
      {
        if (double.IsNaN(VarianceSample))
          return double.NaN;
        else
          return Math.Sqrt(VarianceSample);
      }
    }
    /// <summary>
    /// The standard error. If the data are not numerical, double.NaN will be returned
    /// </summary>
    /// <remarks>Checked using Mathematica 5.0</remarks>
    public double StandardError
    {
      get
      {
        if (!Double.IsNaN(StandardDevPop))
          return StandardDevPop / Math.Sqrt(SampleSize);
        else
          return double.NaN;
      }
    }
    /// <summary>
    /// Returns the kurtosis excess of the distribution. If the data are not numerical, double.NaN will be returned
    /// </summary>
    /// <remarks>Checked using Mathematica 5.0</remarks>
    public double KurtosisExcess
    {
      get
      {
        if (double.IsNaN(StandardDevPop) && (SampleSize > 2))
          return double.NaN;
        else
        {
          double _4m = ComputeMoment(4);
          return (_4m / Math.Pow(StandardDevSample, 4.0) - 3.0);
        }
      }
    }
    /// <summary>
    /// Returns the relative kurtosis of the distribution. If the data are not numerical, double.NaN will be returned
    /// </summary>
    /// <remarks>Checked using Mathematica 5.0</remarks>
    public double Kurtosis
    {
      get
      {
        double _k = KurtosisExcess;
        if (!Double.IsNaN(_k))
        {
          return _k + 3.0;
        }
        else
          return double.NaN;
      }
    }
    /// <summary>
    /// Returns the skewness of the distribution. If the data are not numerical, double.NaN will be returned
    /// </summary>
    /// <remarks>Checked using Mathematica 5.0</remarks>
    public double Skewness
    {
      get
      {
        if (double.IsNaN(_mean) && (SampleSize <= 2))
          return double.NaN;
        else
        {
          double _3m = ComputeMoment(3);
          return _3m / Math.Pow(StandardDevSample, 3.0);
        }
      }
    }
    /// <summary>
    /// Returns the largest value
    /// </summary>
    public T Maximum
    {
      get
      {
        T[] _data = GetData(false);
        return _data[_data.Length - 1];
      }
    }
    /// <summary>
    /// Returns the lowest value
    /// </summary>
    public T Minimum
    {
      get
      {
        T[] _data = GetData(false);
        return _data[0];
      }
    }
    /// <summary>
    /// Returns the range. If data are not numerical, double.NaN will be returned.
    /// </summary>
    public double Range
    {
      get
      {
        T[] _data = GetData(false);
        if (!Double.IsNaN(_dblSum))
        {
          return ((double)Convert.ChangeType(Maximum, TypeCode.Double) - (double)Convert.ChangeType(Minimum, TypeCode.Double));
        }
        else
          return double.NaN;
      }
    }
    /// <summary>
    /// The alpha value used by <see cref="IsGaussian"/>
    /// </summary>
    /// <remarks>Default: 0.05</remarks>
    public double Alpha
    {
      get
      {
        return _alpha;
      }
      set
      {
        _alpha = value;
      }
    }
    /// <summary>
    /// Returns true, if the given datapoints are normally distributed. If the data are not numerical,
    /// false will be returned.
    /// </summary>
    /// <remarks>
    /// The <seealso cref="Alpha"/>-value should be greater than set to greater than 0.3 to prevent
    /// an excessive beta error.
    /// </remarks>
    public bool IsGaussian
    {
      get
      {
        if (KS_Test(out _p))
        {
          if (_p < _alpha)
            return false;
          else
            return true;
        }
        else
        {
          _p = double.NaN;
          return false;
        }
      }
    }
    /// <summary>
    /// Returns the p-value of the Kolmogorov-Smirnov Test
    /// </summary>
    public double P_Value
    {
      get
      {
        KS_Test(out _p);
        return _p;
      }
    }
    #endregion
    #region Sort
    /// <summary>
    /// Implements the Quicksort-Algorithm
    /// </summary>
    private class SortFrequencyTable<K> where K : IComparable<K>
    {
      /// <summary>
      /// the array to sort
      /// </summary>
      private static FrequencyTableEntry<K>[] a;
      /// <summary>
      /// Sorts a FrequencyTableEntry-Array using a quicksort algorithm
      /// </summary>
      /// <param name="input">The Array to sort</param>
      /// <param name="order">The sort order</param>
      /// <returns>The sorted array</returns>
      public static FrequencyTableEntry<K>[] SortTable(FrequencyTableEntry<K>[] input, FrequencyTableSortOrder order)
      {
        a = new FrequencyTableEntry<K>[input.Length];
        input.CopyTo(a, 0);
        switch (order)
        {
          case FrequencyTableSortOrder.Value_Ascending:
            SortByValueAscending(0, a.Length - 1);
            break;
          case FrequencyTableSortOrder.Value_Descending:
            SortByValueDescending(0, a.Length - 1);
            break;
          case FrequencyTableSortOrder.Frequency_Ascending:
            SortByFrequencyAscending(0, a.Length - 1);
            break;
          case FrequencyTableSortOrder.Frequency_Descending:
            SortByFrequencyDescending(0, a.Length - 1);
            break;
          case FrequencyTableSortOrder.None:
            break;
        }
        return a;
      }
      /// <summary>
      /// The Quicksort-Method
      /// </summary>
      /// <param name="l">lower bound</param>
      /// <param name="u">upper bound</param>
      private static void SortByValueAscending(int l, int u)
      {
        int i = l;
        int j = u;
        K v = a[(l + u) / 2].Value;
        while (i <= j)
        {
          while (a[i].Value.CompareTo(v) < 0)
            i++;
          while (a[j].Value.CompareTo(v) > 0)
            j--;
          if (i <= j)
          {
            Swap(i, j);
            i++;
            j--;
          }
        }
        if (l < j)
          SortByValueAscending(l, j);
        if (i < u)
          SortByValueAscending(i, u);
      }
      /// <summary>
      /// The Quicksort-Method
      /// </summary>
      /// <param name="l">lower bound</param>
      /// <param name="u">upper bound</param>
      private static void SortByValueDescending(int l, int u)
      {
        int i = l;
        int j = u;
        K v = a[(l + u) / 2].Value;
        while (i <= j)
        {
          while (a[i].Value.CompareTo(v) > 0)
            i++;
          while (a[j].Value.CompareTo(v) < 0)
            j--;
          if (i <= j)
          {
            Swap(i, j);
            i++;
            j--;
          }
        }
        if (l < j)
          SortByValueDescending(l, j);
        if (i < u)
          SortByValueDescending(i, u);
      }
      /// <summary>
      /// The Quicksort-Method
      /// </summary>
      /// <param name="l">lower bound</param>
      /// <param name="u">upper bound</param>
      private static void SortByFrequencyAscending(int l, int u)
      {
        int i = l;
        int j = u;
        int v = a[(l + u) / 2].AbsoluteFreq;
        while (i <= j)
        {
          while (a[i].AbsoluteFreq < v)
            i++;
          while (a[j].AbsoluteFreq > v)
            j--;
          if (i <= j)
          {
            Swap(i, j);
            i++;
            j--;
          }
        }
        if (l < j)
          SortByFrequencyAscending(l, j);
        if (i < u)
          SortByFrequencyAscending(i, u);
      }
      /// <summary>
      /// The Quicksort-Method
      /// </summary>
      /// <param name="l">lower bound</param>
      /// <param name="u">upper bound</param>
      private static void SortByFrequencyDescending(int l, int u)
      {
        int i = l;
        int j = u;
        int v = a[(l + u) / 2].AbsoluteFreq;
        while (i <= j)
        {
          while (a[i].AbsoluteFreq > v)
            i++;
          while (a[j].AbsoluteFreq < v)
            j--;
          if (i <= j)
          {
            Swap(i, j);
            i++;
            j--;
          }
        }
        if (l < j)
          SortByFrequencyDescending(l, j);
        if (i < u)
          SortByFrequencyDescending(i, u);
      }
      /// <summary>
      /// Swaps two array-elements
      /// </summary>
      /// <param name="i">First element</param>
      /// <param name="j">Second element</param>
      private static void Swap(int i, int j)
      {
        FrequencyTableEntry<K> temp = a[i];
        a[i] = a[j];
        a[j] = temp;
      }
    }
    #endregion
  }
}