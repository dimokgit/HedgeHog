using System;
using System.Collections.Generic;
using VTClasses;

namespace TableTest
{
	class Program
	{

		static void Main(string[] args)
		{
			int[] rint = new int[10000];
			Console.WriteLine("Initializing pseudorandom integer array - 10000 values");
			Random ra = new Random(1001);
			for (int i = 0; i < rint.Length; i++)
			{
				rint[i] = (int) (ra.NextDouble() * 10.0);
			}
			Console.WriteLine("Initializing frequency table");
			FrequencyTable<int> rtable = new FrequencyTable<int>(rint);
			Console.WriteLine("Get data in input-order");
			int[] br = rtable.GetData(true);
			for (int i = 0; i < br.Length; i++)
			{
				Console.Write("Input array: {1}  Output array: {1}", rint[i].ToString(), br[i].ToString());
				if (br[i] != rint[i])
					Console.WriteLine(" --> NOT EQUAL", i.ToString());
				else
					Console.WriteLine(" --> EQUAL");
			}





			#region data
			double[] ddd = new double[] 
						{ 5, 15, 15, 15, 25, 25, 25, 25, 25, 25, 25, 25, 
						35, 35, 35, 35, 35, 35, 35, 35, 35, 35, 35, 35, 35, 35, 35, 35, 35, 35,
						45, 45, 45, 45, 45, 45, 45, 45, 45, 45, 45, 45, 45, 45, 45, 45, 45, 45, 45, 45, 45, 45, 45, 45,
						55, 55, 55, 55, 55, 55, 55, 55, 55, 55, 55, 55, 55, 55, 55, 55, 55, 55, 55, 55, 55, 55,
						65, 65, 65, 65, 65, 65, 65, 65, 65, 65, 65, 65, 65, 65, 65,
						75, 75, 75, 75, 75, 75, 75, 75,
						95};
			#endregion
			FrequencyTable<double> table = new FrequencyTable<double>(ddd);
			// display descriptive statistics
			Console.WriteLine("Sample size = {0}", table.SampleSize);
			Console.WriteLine("Mean = {0}", table.Mean);
			Console.WriteLine("Median = {0}", table.Median);
			Console.WriteLine("Sample variance = {0}", table.VarianceSample);
			Console.WriteLine("Population variance = {0}", table.VariancePop);
			Console.WriteLine("Standard deviation (Sample) = {0}", table.StandardDevSample);
			Console.WriteLine("Standard deviation (Population) = {0}", table.StandardDevPop);
			Console.WriteLine("Standard error of the mean = {0}", table.StandardError);
			Console.WriteLine("Minimum = {0}", table.Minimum);
			Console.WriteLine("Maximum = {0}", table.Maximum);
			Console.WriteLine("Range = {0}", table.Range);
			Console.WriteLine("Skewness = {0}", table.Skewness);
			Console.WriteLine("Kurtosis = {0}", table.Kurtosis);
			Console.WriteLine("Kurtosis excess = {0}", table.KurtosisExcess);
			Console.WriteLine("Highest Frequency = {0}", table.HighestFrequency);
			Console.WriteLine("Mode = {0}", table.Mode);
			Console.WriteLine("Smallest frequency = {0}", table.SmallestFrequency);
			Console.WriteLine("Scarcest value = {0}", table.ScarcestValue);
			Console.WriteLine("Sum over all values = {0}", table.Sum);
			if(table.IsGaussian)
				Console.WriteLine("Date are normally distributed. p = {0}", table.P_Value);
			else
				Console.WriteLine("Date are NOT normally distributed. p = {0}", table.P_Value);
			Console.WriteLine("Descriptive: Done");
			Console.WriteLine("Press ENTER to continue");
			Console.ReadLine();
			CumulativeFrequencyTableEntry<double>[] sorted = table.GetCumulativeFrequencyTable(CumulativeFrequencyTableFormat.EachDatapoint);
			FrequencyTableEntry<double>[] r = table.GetTableAsArray(FrequencyTableSortOrder.None);
			FrequencyTableEntry<double>[] r1 = table.GetTableAsArray(FrequencyTableSortOrder.Value_Ascending);
			FrequencyTableEntry<double>[] r2 = table.GetTableAsArray(FrequencyTableSortOrder.Value_Descending);
			FrequencyTableEntry<double>[] r3 = table.GetTableAsArray(FrequencyTableSortOrder.Frequency_Ascending);
			FrequencyTableEntry<double>[] r4 = table.GetTableAsArray(FrequencyTableSortOrder.Frequency_Descending);
			Console.Clear();
			Console.WriteLine("Table unsorted:");
			Console.WriteLine("***************************************************");
			foreach (FrequencyTableEntry<double> f in r)
			{
				Console.WriteLine("{0}  {1}   {2}   {3}", f.Value, f.AbsoluteFreq, f.RelativeFreq, Math.Round(f.Percentage, 2));
			}
			Console.WriteLine("***************************************************");
			Console.WriteLine();
			Console.WriteLine("Table sorted by value - ascending:");
			Console.WriteLine("***************************************************");
			foreach (FrequencyTableEntry<double> f in r1)
			{
				Console.WriteLine("{0}  {1}   {2}   {3}", f.Value, f.AbsoluteFreq, f.RelativeFreq, Math.Round(f.Percentage, 2));
			}
			Console.WriteLine("***************************************************");
			Console.WriteLine();
			Console.WriteLine("Table sorted by value - descending:");
			Console.WriteLine("***************************************************");
			foreach (FrequencyTableEntry<double> f in r2)
			{
				Console.WriteLine("{0}  {1}   {2}   {3}", f.Value, f.AbsoluteFreq, f.RelativeFreq, Math.Round(f.Percentage, 2));
			}
			Console.WriteLine("***************************************************");
			Console.WriteLine();
			Console.WriteLine("Table sorted by frequency - ascending:");
			Console.WriteLine("***************************************************");
			foreach (FrequencyTableEntry<double> f in r3)
			{
				Console.WriteLine("{0}  {1}   {2}   {3}", f.Value, f.AbsoluteFreq, f.RelativeFreq, Math.Round(f.Percentage, 2));
			}
			Console.WriteLine("***************************************************");
			Console.WriteLine("Scarcest Value:\t{0}\tFrequency: {1}", table.ScarcestValue, table.SmallestFrequency);
			Console.WriteLine("Mode:\t\t{0}\tFrequency: {1}", table.Mode, table.HighestFrequency);
			Console.WriteLine();
			Console.WriteLine("Table sorted by frequency - descending:");
			Console.WriteLine("***************************************************");
			foreach (FrequencyTableEntry<double> f in r4)
			{
				Console.WriteLine("{0}  {1}   {2}   {3}", f.Value, f.AbsoluteFreq, f.RelativeFreq, Math.Round(f.Percentage, 2));
			}
			Console.WriteLine("***************************************************");
			Console.WriteLine("Press ENTER to display cumulative frequencies!");
			Console.ReadLine();
			CumulativeFrequencyTableEntry<double>[] cf = table.GetCumulativeFrequencyTable(CumulativeFrequencyTableFormat.EachDatapoint);
			Console.WriteLine("Cumulative frequencies - the cumulative density function:");
			Console.WriteLine("***************************************************");
			foreach (CumulativeFrequencyTableEntry<double> f in cf)
			{
				Console.WriteLine("{0}  {1}   {2}", f.Value, f.CumulativeAbsoluteFrequency, f.CumulativeRelativeFrequency);
			}
			Console.WriteLine("***************************************************");

			/* now test the class with integers
			 * initialize a new frequency table using an integer array*/
			int[] test = new int[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2 };
			FrequencyTable<int> table1 = new FrequencyTable<int>(test);
			Console.WriteLine();
			Console.WriteLine("Integer table unsorted:");
			Console.WriteLine("***************************************************");
			foreach (FrequencyTableEntry<int> f in table1)
			{
				Console.WriteLine("{0}  {1}   {2}   {3}", f.Value, f.AbsoluteFreq, f.RelativeFreq, Math.Round(f.Percentage, 2));
			}

			/* now test the class using a string */
			FrequencyTable<string> testString = new FrequencyTable<string>("NON NOBIS DOMINE, NON NOBIS, SED NOMINI TUO DA GLORIAM", TextAnalyzeMode.LettersOnly);
			FrequencyTableEntry<string>[] stringArray = testString.GetTableAsArray(FrequencyTableSortOrder.Frequency_Descending);
			Console.WriteLine();
			Console.WriteLine("Character table sorted by frequency - descending:");
			Console.WriteLine("***************************************************");
			foreach (FrequencyTableEntry<string> f in stringArray)
			{
				Console.WriteLine("{0}  {1}   {2}   {3}", f.Value, f.AbsoluteFreq, f.RelativeFreq, Math.Round(f.Percentage, 2));
			}
			CumulativeFrequencyTableEntry<string>[] scf = testString.GetCumulativeFrequencyTable(CumulativeFrequencyTableFormat.EachDatapoint);
			Console.WriteLine("Cumulative frequencies - the cumulative density function:");
			Console.WriteLine("***************************************************");
			foreach (CumulativeFrequencyTableEntry<string> f in scf)
			{
				Console.WriteLine("{0}  {1}   {2}", f.Value, f.CumulativeAbsoluteFrequency, f.CumulativeRelativeFrequency);
			}
			Console.WriteLine("***************************************************");
			Console.Write("Press any key to exit");
			Console.ReadKey();
		}
	}
}
