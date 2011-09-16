using System;

using System.Collections.Generic;

using System.Linq;

using System.Text;

using System.Diagnostics;



namespace HedgeHog {

  public class Regression {
    static void LinearRegression(double[] values, out double a, out double b) {
      double xAvg = 0;
      double yAvg = 0;
      for (int x = 0; x < values.Length; x++) {
        xAvg += x;
        yAvg += values[x];
      }
      xAvg = xAvg / values.Length;
      yAvg = yAvg / values.Length;
      double v1 = 0;
      double v2 = 0;
      for (int x = 0; x < values.Length; x++) {
        v1 += (x - xAvg) * (values[x] - yAvg);
        v2 += Math.Pow(x - xAvg, 2);
      }
      a = v1 / v2;
      b = yAvg - a * xAvg;
      //Console.WriteLine("y = ax + b");
      //Console.WriteLine("a = {0}, the slope of the trend line.", Math.Round(a, 2));
      //Console.WriteLine("b = {0}, the intercept of the trend line.", Math.Round(b, 2));

    }

    public static double[] Regress(double[] dY, int polyOrder) {
      return Regress(dY.Select((y, i) => (double)i).ToArray(), dY, polyOrder);
    }
    public static double[] Regress(double[] dX,double[] dY, int polyOrder) {
      int nPolyOrder = polyOrder;
      double[,] dZ = new double[dY.Length, nPolyOrder + 1];

      for (int i = 0; i < dY.Length; i++) {
        for (int j = 0; j < nPolyOrder + 1; j++) {
          dZ[i, j] = Math.Pow(dX[i], (double)j);
        }
      } 
      return MatrixNumeric.Regress(dZ, dY);

    }
  }
  public class MatrixNumeric {

    private int m_nNumRows = 3;

    private int m_nNumColumns = 3;

    private double[,] m_dValues;



    public MatrixNumeric() {

      m_dValues = new double[m_nNumRows, m_nNumColumns];

    }



    public MatrixNumeric(int nNumRows, int nNumColumns) {

      m_nNumRows = nNumRows;

      m_nNumColumns = nNumColumns;

      m_dValues = new double[m_nNumRows, m_nNumColumns];

    }



    public double this[int nRow, int nColumn] {

      get { return m_dValues[nRow, nColumn]; }

      set { m_dValues[nRow, nColumn] = value; }

    }



    public int NumRows {

      get { return m_nNumRows; }

    }



    public int NumColumns {

      get { return m_nNumColumns; }

    }

    public static double[] Regress(double[,] dZ, double[] dY) {

      //y=a0 z1 + a1 z1 +a2 z2 + a3 z3 +...

      //Z is the functional values.

      //Z index 0 is a row, the variables go across index 1.

      //Y is the summed value.

      //returns the coefficients.

      System.Diagnostics.Debug.Assert(dZ != null && dY != null);

      System.Diagnostics.Debug.Assert(dZ.GetLength(0) == dY.GetLength(0));



      MatrixNumeric mZ = dZ;

      MatrixNumeric mZTran = mZ.Transpose();

      MatrixNumeric mLHS = mZTran * mZ;

      MatrixNumeric mRHS = mZTran * dY;

      MatrixNumeric mCoefs = mLHS.SolveFor(mRHS);



      return mCoefs;

    }



    public MatrixNumeric Clone() {

      MatrixNumeric mRet = new MatrixNumeric(m_nNumRows, m_nNumColumns);

      for (int i = 0; i < m_nNumRows; i++) {

        for (int j = 0; j < m_nNumColumns; j++) {

          mRet[i, j] = this[i, j];

        }

      }

      return mRet;

    }



    public static MatrixNumeric Identity(int nSize) {

      MatrixNumeric mRet = new MatrixNumeric(nSize, nSize);

      for (int i = 0; i < nSize; i++) {

        for (int j = 0; j < nSize; j++) {

          mRet[i, j] = (i == j) ? 1.0 : 0.0;

        }

      }

      return mRet;

    }



    public MatrixNumeric Transpose() {

      MatrixNumeric mRet = new MatrixNumeric(m_nNumColumns, m_nNumRows);

      for (int i = 0; i < m_nNumRows; i++) {

        for (int j = 0; j < m_nNumColumns; j++) {

          mRet[j, i] = this[i, j];

        }

      }

      return mRet;

    }



    public static MatrixNumeric FromArray(double[] dLeft) {

      int nLength = dLeft.Length;

      MatrixNumeric mRet = new MatrixNumeric(nLength, 1);

      for (int i = 0; i < nLength; i++) {

        mRet[i, 0] = dLeft[i];

      }

      return mRet;

    }



    public static implicit operator MatrixNumeric(double[] dLeft) {

      return FromArray(dLeft);

    }



    public static double[] ToArray(MatrixNumeric mLeft) {

      Debug.Assert((mLeft.NumColumns == 1 && mLeft.NumRows >= 1) || (mLeft.NumRows == 1 && mLeft.NumColumns >= 1));



      double[] dRet = null;

      if (mLeft.NumColumns > 1) {

        int nNumElements = mLeft.NumColumns;

        dRet = new double[nNumElements];

        for (int i = 0; i < nNumElements; i++) {

          dRet[i] = mLeft[0, i];

        }

      } else {

        int nNumElements = mLeft.NumRows;

        dRet = new double[nNumElements];

        for (int i = 0; i < nNumElements; i++) {

          dRet[i] = mLeft[i, 0];

        }

      }

      return dRet;

    }



    public static implicit operator double[](MatrixNumeric mLeft) {

      return ToArray(mLeft);

    }



    public static MatrixNumeric FromDoubleArray(double[,] dLeft) {

      int nLength0 = dLeft.GetLength(0);

      int nLength1 = dLeft.GetLength(1);

      MatrixNumeric mRet = new MatrixNumeric(nLength0, nLength1);

      for (int i = 0; i < nLength0; i++) {

        for (int j = 0; j < nLength1; j++) {

          mRet[i, j] = dLeft[i, j];

        }

      }

      return mRet;

    }


    //[CLSCompliant(false)]
    public static implicit operator MatrixNumeric(double[,] dLeft) {

      return FromDoubleArray(dLeft);

    }



    public static double[,] ToDoubleArray(MatrixNumeric mLeft) {

      double[,] dRet = new double[mLeft.NumRows, mLeft.NumColumns];

      for (int i = 0; i < mLeft.NumRows; i++) {

        for (int j = 0; j < mLeft.NumColumns; j++) {

          dRet[i, j] = mLeft[i, j];

        }

      }

      return dRet;

    }



    public static implicit operator double[,](MatrixNumeric mLeft) {

      return ToDoubleArray(mLeft);

    }



    public static MatrixNumeric Add(MatrixNumeric mLeft, MatrixNumeric mRight) {

      Debug.Assert(mLeft.NumColumns == mRight.NumColumns);

      Debug.Assert(mLeft.NumRows == mRight.NumRows);



      MatrixNumeric mRet = new MatrixNumeric(mLeft.NumRows, mRight.NumColumns);

      for (int i = 0; i < mLeft.NumRows; i++) {

        for (int j = 0; j < mLeft.NumColumns; j++) {

          mRet[i, j] = mLeft[i, j] + mRight[i, j];

        }

      }

      return mRet;

    }



    public static MatrixNumeric operator +(MatrixNumeric mLeft, MatrixNumeric mRight) {

      return MatrixNumeric.Add(mLeft, mRight);

    }



    public static MatrixNumeric Subtract(MatrixNumeric mLeft, MatrixNumeric mRight) {

      Debug.Assert(mLeft.NumColumns == mRight.NumColumns);

      Debug.Assert(mLeft.NumRows == mRight.NumRows);

      MatrixNumeric mRet = new MatrixNumeric(mLeft.NumRows, mRight.NumColumns);

      for (int i = 0; i < mLeft.NumRows; i++) {

        for (int j = 0; j < mLeft.NumColumns; j++) {

          mRet[i, j] = mLeft[i, j] - mRight[i, j];

        }

      }

      return mRet;

    }



    public static MatrixNumeric operator -(MatrixNumeric mLeft, MatrixNumeric mRight) {

      return MatrixNumeric.Subtract(mLeft, mRight);

    }



    public static MatrixNumeric Multiply(MatrixNumeric mLeft, MatrixNumeric mRight) {

      Debug.Assert(mLeft.NumColumns == mRight.NumRows);

      MatrixNumeric mRet = new MatrixNumeric(mLeft.NumRows, mRight.NumColumns);

      for (int i = 0; i < mRight.NumColumns; i++) {

        for (int j = 0; j < mLeft.NumRows; j++) {

          double dValue = 0.0;

          for (int k = 0; k < mRight.NumRows; k++) {

            dValue += mLeft[j, k] * mRight[k, i];

          }

          mRet[j, i] = dValue;

        }

      }

      return mRet;

    }



    public static MatrixNumeric operator *(MatrixNumeric mLeft, MatrixNumeric mRight) {

      return MatrixNumeric.Multiply(mLeft, mRight);

    }



    public static MatrixNumeric Multiply(double dLeft, MatrixNumeric mRight) {

      MatrixNumeric mRet = new MatrixNumeric(mRight.NumRows, mRight.NumColumns);

      for (int i = 0; i < mRight.NumRows; i++) {

        for (int j = 0; j < mRight.NumColumns; j++) {

          mRet[i, j] = dLeft * mRight[i, j];

        }

      }

      return mRet;

    }



    public static MatrixNumeric operator *(double dLeft, MatrixNumeric mRight) {

      return MatrixNumeric.Multiply(dLeft, mRight);

    }



    public static MatrixNumeric Multiply(MatrixNumeric mLeft, double dRight) {

      MatrixNumeric mRet = new MatrixNumeric(mLeft.NumRows, mLeft.NumColumns);

      for (int i = 0; i < mLeft.NumRows; i++) {

        for (int j = 0; j < mLeft.NumColumns; j++) {

          mRet[i, j] = mLeft[i, j] * dRight;

        }

      }

      return mRet;

    }



    public static MatrixNumeric operator *(MatrixNumeric mLeft, double dRight) {

      return MatrixNumeric.Multiply(mLeft, dRight);

    }



    public static MatrixNumeric Divide(MatrixNumeric mLeft, double dRight) {

      MatrixNumeric mRet = new MatrixNumeric(mLeft.NumRows, mLeft.NumColumns);

      for (int i = 0; i < mLeft.NumRows; i++) {

        for (int j = 0; j < mLeft.NumColumns; j++) {

          mRet[i, j] = mLeft[i, j] / dRight;

        }

      }

      return mRet;

    }



    public static MatrixNumeric operator /(MatrixNumeric mLeft, double dRight) {

      return MatrixNumeric.Divide(mLeft, dRight);

    }



    public MatrixNumeric SolveFor(MatrixNumeric mRight) {

      Debug.Assert(mRight.NumRows == m_nNumColumns);

      Debug.Assert(m_nNumColumns == m_nNumRows);

      MatrixNumeric mRet = new MatrixNumeric(m_nNumColumns, mRight.NumColumns);



      LUDecompositionResults resDecomp = LUDecompose();

      int[] nP = resDecomp.PivotArray;

      MatrixNumeric mL = resDecomp.L;

      MatrixNumeric mU = resDecomp.U;



      double dSum = 0.0;



      for (int k = 0; k < mRight.NumColumns; k++) {

        //Solve for the corresponding d Matrix from Ld=Pb

        MatrixNumeric D = new MatrixNumeric(m_nNumRows, 1);

        D[0, 0] = mRight[nP[0], k] / mL[0, 0];

        for (int i = 1; i < m_nNumRows; i++) {

          dSum = 0.0;

          for (int j = 0; j < i; j++) {

            dSum += mL[i, j] * D[j, 0];

          }

          D[i, 0] = (mRight[nP[i], k] - dSum) / mL[i, i];

        }



        //Solve for x using Ux = d

        //DMatrix X = new DMatrix(m_nNumRows, 1);

        mRet[m_nNumRows - 1, k] = D[m_nNumRows - 1, 0];

        for (int i = m_nNumRows - 2; i >= 0; i--) {

          dSum = 0.0;

          for (int j = i + 1; j < m_nNumRows; j++) {

            dSum += mU[i, j] * mRet[j, k];

          }

          mRet[i, k] = D[i, 0] - dSum;

        }

      }



      return mRet;

    }



    private LUDecompositionResults LUDecompose() {

      Debug.Assert(m_nNumColumns == m_nNumRows);

      // Using Crout Decomp with P

      //

      //  Ax = b   //By definition of problem variables.

      //

      //  LU = PA   //By definition of L, U, and P.

      //

      //  LUx = Pb  //By substition for PA.

      //

      //  Ux = d   //By definition of d

      //

      //  Ld = Pb   //By subsitition for d.

      //



      //For 4x4 with P = I



      //  [l11 0   0   0  ]  [1 u12 u13 u14]   [a11 a12 a13 a14]

      //  [l21 l22 0   0  ]  [0 1   u23 u24] = [a21 a22 a23 a24]

      //  [l31 l32 l33 0  ]  [0 0   1   u34]   [a31 a32 a33 a34] 

      //  [l41 l42 l43 l44]  [0 0   0   1  ]   [a41 a42 a43 a44] 



      LUDecompositionResults resRet = new LUDecompositionResults();

      int[] nP = new int[m_nNumRows]; //Pivot matrix.

      MatrixNumeric mU = new MatrixNumeric(m_nNumRows, m_nNumColumns);

      MatrixNumeric mL = new MatrixNumeric(m_nNumRows, m_nNumColumns);

      MatrixNumeric mUWorking = Clone();

      MatrixNumeric mLWorking = new MatrixNumeric(m_nNumRows, m_nNumColumns);

      for (int i = 0; i < m_nNumRows; i++) {

        nP[i] = i;

      }



      //Iterate down the number of rows in the U matrix.

      for (int i = 0; i < m_nNumRows; i++) {

        //Do pivots first.

        //I want to make the matrix diagnolaly dominate.



        //Initialize the variables used to determine the pivot row.

        double dMaxRowRatio = double.NegativeInfinity;

        int nMaxRow = -1;

        int nMaxPosition = -1;

        //Check all of the rows below and including the current row

        //to determine which row should be pivoted to the working row position.

        //The pivot row will be set to the row with the maximum ratio

        //of the absolute value of the first column element divided by the 

        //sum of the absolute values of the elements in that row.



        for (int j = i; j < m_nNumRows; j++) {

          //Store the sum of the absolute values of the row elements in

          //dRowSum.  Clear it out now because I am checking a new row.

          double dRowSum = 0.0;

          //Go across the columns, add the absolute values of the elements in

          //that column to dRowSum.

          for (int k = i; k < m_nNumColumns; k++) {

            dRowSum += Math.Abs(mUWorking[nP[j], k]);

          }



          //Check to see if the absolute value of the ratio of the lead

          //element over the sum of the absolute values of the elements is larger

          //that the ratio for preceding rows.  If it is, then the current row

          //becomes the new pivot candidate.

          if (Math.Abs(mUWorking[nP[j], i] / dRowSum) > dMaxRowRatio) {

            dMaxRowRatio = Math.Abs(mUWorking[nP[j], i] / dRowSum);

            nMaxRow = nP[j];

            nMaxPosition = j;

          }

        }



        //If the pivot candidate isn't the current row, update the

        //pivot array to swap the current row with the pivot row.

        if (nMaxRow != nP[i]) {

          int nHold = nP[i];

          nP[i] = nMaxRow;

          nP[nMaxPosition] = nHold;

        }



        //Store the value of the left most element in the working U

        //matrix in dRowFirstElementValue.

        double dRowFirstElementValue = mUWorking[nP[i], i];

        //Update the columns of the working row.  j is the column index.

        for (int j = 0; j < m_nNumRows; j++) {

          if (j < i) {

            //If j<1, then the U matrix element value is 0.

            mUWorking[nP[i], j] = 0.0;

          } else if (j == i) {

            //If i == j, the L matrix value is the value of the

            //element in the working U matrix.

            mLWorking[nP[i], j] = dRowFirstElementValue;

            //The value of the U matrix for i == j is 1

            mUWorking[nP[i], j] = 1.0;

          } else // j>i

                    {

            //Divide each element in the current row of the U matrix by the 

            //value of the first element in the row

            mUWorking[nP[i], j] /= dRowFirstElementValue;

            //The element value of the L matrix for j>i is 0

            mLWorking[nP[i], j] = 0.0;

          }

        }



        //For the working U matrix, subtract the ratioed active row from the rows below it.

        //Update the columns of the rows below the working row.  k is the row index.

        for (int k = i + 1; k < m_nNumRows; k++) {

          //Store the value of the first element in the working row

          //of the U matrix.

          dRowFirstElementValue = mUWorking[nP[k], i];

          //Go accross the columns of row k.

          for (int j = 0; j < m_nNumRows; j++) {

            if (j < i) {

              //If j<1, then the U matrix element value is 0.

              mUWorking[nP[k], j] = 0.0;

            } else if (j == i) {

              //If i == j, the L matrix value is the value of the

              //element in the working U matrix.

              mLWorking[nP[k], j] = dRowFirstElementValue;

              //The element value of the L matrix for j>i is 0

              mUWorking[nP[k], j] = 0.0;

            } else //j>i

                        {

              mUWorking[nP[k], j] = mUWorking[nP[k], j] - dRowFirstElementValue * mUWorking[nP[i], j];

            }

          }

        }

      }



      for (int i = 0; i < m_nNumRows; i++) {

        for (int j = 0; j < m_nNumRows; j++) {

          mU[i, j] = mUWorking[nP[i], j];

          mL[i, j] = mLWorking[nP[i], j];

        }

      }



      resRet.U = mU;

      resRet.L = mL;

      resRet.PivotArray = nP;



      return resRet;

    }



    public MatrixNumeric Invert() {

      Debug.Assert(m_nNumRows == m_nNumColumns);

      return SolveFor(Identity(m_nNumRows));

    }



  }



  public class LUDecompositionResults {

    private MatrixNumeric m_matL;

    private MatrixNumeric m_matU;

    private int[] m_nPivotArray;

    private LUDecompositionResultStatus m_enuStatus = LUDecompositionResultStatus.OK;



    public LUDecompositionResults() {

    }



    public LUDecompositionResults(MatrixNumeric matL, MatrixNumeric matU, int[] nPivotArray, LUDecompositionResultStatus enuStatus) {

      m_matL = matL;

      m_matU = matU;

      m_nPivotArray = nPivotArray;

      m_enuStatus = enuStatus;

    }



    public MatrixNumeric L {

      get { return m_matL; }

      set { m_matL = value; }

    }



    public MatrixNumeric U {

      get { return m_matU; }

      set { m_matU = value; }

    }



    public int[] PivotArray {

      get { return m_nPivotArray; }

      set { m_nPivotArray = value; }

    }



    public LUDecompositionResultStatus Status {

      get { return m_enuStatus; }

      set { m_enuStatus = value; }

    }

  }



  public enum LUDecompositionResultStatus {

    OK = 0,

    LinearlyDependent = 1

  }



}
