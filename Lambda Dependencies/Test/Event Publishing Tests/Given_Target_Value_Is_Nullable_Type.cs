using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Hardcodet.Util.Dependencies.Testing.TestModel;
using NUnit.Framework;

namespace Hardcodet.Util.Dependencies.Testing.Event_Publishing_Tests
{
  [TestFixture]
  public class Given_Target_Value_Is_Nullable_Type
  {
    Course c = new Course() { IsFull = null };
    int changeCounter = 0;
    
    
    [Test]
    public void Observing_Nullable_Types_Should_Work()
    {
      var basket = new {Course = c};
      DependencyBuilder.CreateDependency(() => basket.Course.IsFull, OnCourseChanged);

      c.IsFull = true;
      c.IsFull = null;
      c.IsFull = false;
      c.IsFull = true;

      Assert.AreEqual(4, changeCounter);
    }

    private void OnCourseChanged(object sender, DependencyChangeEventArgs<bool?> e)
    {
      changeCounter++;
    }
  }
}
