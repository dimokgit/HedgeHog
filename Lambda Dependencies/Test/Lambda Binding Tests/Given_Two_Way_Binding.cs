using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Hardcodet.Util.Dependencies.Testing.TestModel;
using NUnit.Framework;

namespace Hardcodet.Util.Dependencies.Testing.Lambda_Binding_Tests
{
  public class Given_Two_Way_Binding : BindingTestBase
  {
    #region FirstStudent

    /// <summary>
    /// The first student.
    /// </summary>
    private Student firstStudent;


    /// <summary>
    /// The first student.
    /// </summary>
    public Student FirstStudent
    {
      get { return firstStudent; }
      set
      {
        //ignore if values are equal
        if (value == firstStudent) return;

        firstStudent = value;
        OnPropertyChanged("FirstStudent");
      }
    }

    #endregion

    #region SecondStudent

    /// <summary>
    /// The second student.
    /// </summary>
    private Student secondStudent;


    /// <summary>
    /// The second student.
    /// </summary>
    public Student SecondStudent
    {
      get { return secondStudent; }
      set
      {
        //ignore if values are equal
        if (value == secondStudent) return;

        secondStudent = value;
        OnPropertyChanged("SecondStudent");
      }
    }

    #endregion

    protected override void Init()
    {
      FirstStudent = TestUtil.CreateTestStudent();
      SecondStudent = TestUtil.CreateTestStudent();

      base.Init();
    }


    protected override void CreateBinding()
    {
      //Binding = LambdaBinding.BindTwoWay(() => FirstStudent.Name, () => SecondStudent.Name);
    }


    [Test]
    public void Updates_Should_Work_Both_Ways()
    {
      var binding = LambdaBinding.BindTwoWay(
        () => FirstStudent.Name,
        () => SecondStudent.Name);

      FirstStudent.Name = "Peter";
      Assert.AreEqual("Peter", SecondStudent.Name);

      SecondStudent.Name = "Parker";
      Assert.AreEqual("Parker", FirstStudent.Name);
    }


    [Test]
    public void Two_Way_Value_Conversion_Should_Be_Applied()
    {
      var binding = LambdaBinding.BindTwoWay(
          () => FirstStudent.Age,
          () => SecondStudent.Name,
          i => i.ToString(),
          n => int.Parse(n));

      FirstStudent.Age = 25;
      Assert.AreEqual("25", SecondStudent.Name);

      SecondStudent.Name = "30";
      Assert.AreEqual(30, FirstStudent.Age);
    }

  }
}
