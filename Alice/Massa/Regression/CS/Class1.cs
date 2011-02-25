using System.Diagnostics;
using System;
using System.Xml.Linq;
using System.Collections;
using Microsoft.VisualBasic;
using System.Data;
using System.Collections.Generic;
using System.Linq;

namespace Regression
{
	public class Class1
	{
		
		
		private const long MaxO = 25;
		private long GlobalO; //"Ordnung" = degree of the polynom expected
		private bool Finished;
		
		private double[] SumX = new double[2 * MaxO + 1];
		private double[] SumYX = new double[MaxO + 1];
		private double[,] M = new double[MaxO + 1, MaxO + 1 + 1];
		private double[] C = new double[MaxO + 1]; //coefficients in: Y = C(0)*X^0 + C(1)*X^1 + C(2)*X^2 + ...
		
		private void GaussSolve(long O)
		{
			//gauss algorithm implementation,
			//following R.Sedgewick's "Algorithms in C", Addison-Wesley, with minor modifications
			long i;
			long j;
			long k;
			long iMax;
			double T;
			long O1;
			O1 = O + 1;
			//first triangulize the matrix
			for (i = 0; i <= O; i++)
			{
				iMax = i;
				
				T = Math.Abs(M(iMax, i));
				for (j = i + 1; j <= O; j++) //find the line with the largest absvalue in this row
				{
					if (T < Math.Abs(M(j, i)))
					{
						iMax = j;
					}
					T = Math.Abs(M(iMax, i));
				}
				if (i < iMax) //exchange the two lines
				{
					for (k = i; k <= O1; k++)
					{
						T = M(i, k);
						M(i, k) = M(iMax, k);
						M(iMax, k) = T;
					}
				}
				for (j = i + 1; j <= O; j++) //scale all following lines to have a leading zero
				{
					T = M(j, i) / M(i, i);
					M(j, i) = 0.0;
					for (k = i + 1; k <= O1; k++)
					{
						M(j, k) = M(j, k) - M(i, k) * T;
					}
				}
			}
			//then substitute the coefficients
			for (j = O; j >= 0; j--)
			{
				T = M(j, O1);
				for (k = j + 1; k <= O; k++)
				{
					T = T - M(j, k) * C(k);
				}
				C(j) = T / M(j, j);
			}
			Finished = true;
		}
		
		private void BuildMatrix(long O)
		{
			long i;
			long k;
			long O1;
			O1 = O + 1;
			for (i = 0; i <= O; i++)
			{
				for (k = 0; k <= O; k++)
				{
					M(i, k) = SumX(i + k);
				}
				M(i, O1) = SumYX(i);
			}
		}
		
		private void FinalizeMatrix(long O)
		{
			long i;
			long O1;
			O1 = O + 1;
			for (i = 0; i <= O; i++)
			{
				M(i, O1) = SumYX(i);
			}
		}
		
		private void Solve()
		{
			long O;
			O = GlobalO;
			if (XYCount <= O)
			{
				O = XYCount - 1;
			}
			if (O < 0)
			{
				return;
			}
			BuildMatrix(O);
			try
			{
				GaussSolve(O);
				while ((Information.Err().Number != 0) && (1 < O))
				{
					Information.Err().Clear();
					C(0) = 0.0;
					O--;
					FinalizeMatrix(O);
				}
			}
			catch
			{
			}
		}
		
		private Class1()
		{
			Init();
			GlobalO = 2;
		}
		
		public void Init()
		{
			long i;
			Finished = false;
			for (i = 0; i <= MaxO; i++)
			{
				SumX(i) = 0.0;
				SumX(i + MaxO) = 0.0;
				SumYX(i) = 0.0;
				C(i) = 0.0;
			}
		}
		
		long Ex;
		long O;
		if (! Finished)
		{
			Solve;
		}
		Ex = Abs(Exponent);
		O = GlobalO;
		if (XYCount <= O)
		{
			O = XYCount - 1;
		}
		if (O < Ex)
		{
			Coeff = 0;
		}
		else
		{
			Coeff = C(Ex);
		}
		
		public long Get
		{
			Degree = GlobalO;
		}
		if (NewVal < 0 || MaxO < NewVal)
		{
			Err.Raise (6000, "RegressionObject", NewVal + " is an invalid property value! Use 0<= Degree <= " + MaxO);
			return;
		}
		Init();
		GlobalO = NewVal;
		
		public long Get
		{
			XYCount = System.Convert.ToInt32(SumX(0));
		}
		
		public void XYAdd(double NewX, double NewY)
		{
			long i;
			long j;
			double TX;
			long Max2O;
			Finished = false;
			Max2O = 2 * GlobalO;
			TX = 1.0;
			SumX(0) = SumX(0) + 1;
			SumYX(0) = SumYX(0) + NewY;
			for (i = 1; i <= GlobalO; i++)
			{
				TX = TX * NewX;
				SumX(i) = SumX(i) + TX;
				SumYX(i) = SumYX(i) + NewY * TX;
			}
			for (i = GlobalO + 1; i <= Max2O; i++)
			{
				TX = TX * NewX;
				SumX(i) = SumX(i) + TX;
			}
		}
		
		public double RegVal()
		{
			long i;
			long O;
			double rv;
			if (! Finished)
			{
				Solve();
			}
			rv = 0.0;
			O = GlobalO;
			if (XYCount <= O)
			{
				O = XYCount - 1;
			}
			for (i = 0; i <= O; i++)
			{
				rv = RegVal() + C(i) *Math.Pow(X, i);
			}
			return rv;
		}
		
	}
	
}
