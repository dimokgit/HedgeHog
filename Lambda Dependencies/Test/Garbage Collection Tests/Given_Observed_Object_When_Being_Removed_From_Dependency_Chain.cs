using System;
using Hardcodet.Util.Dependencies.Testing.TestModel;
using NUnit.Framework;
using NUnit.Framework.SyntaxHelpers;


namespace Hardcodet.Util.Dependencies.Testing.Garbage_Collection_Tests
{
  /// <summary>
  /// 
  /// </summary>
  public class Given_Observed_Object_When_Being_Removed_From_Dependency_Chain : StudentTestBase
  {

    /// <summary>
    /// 
    /// </summary>
    [Test]
    public void Cleared_Leaf_Object_Should_Be_Finalizable()
    {
      Assert.That(Student.School.Address, Is.Not.Null);
      Assert.That(Address.FinalizeCounter, Is.EqualTo(0));
      Student.School.Address = null;
      Assert.That(Address.FinalizeCounter, Is.EqualTo(0));
      GC.Collect();
      GC.WaitForPendingFinalizers();
      Assert.That(Address.FinalizeCounter, Is.EqualTo(1));
    }


    /// <summary>
    /// 
    /// </summary>
    [Test]
    public void Cleared_Intermediary_Should_Be_Finalizable()
    {
      Assert.That(School.FinalizeCounter, Is.EqualTo(0));
      Assert.That(Address.FinalizeCounter, Is.EqualTo(0));
      Student.School = null;
      
      GC.Collect();
      GC.WaitForPendingFinalizers();

      Assert.That(Student.FinalizeCounter, Is.EqualTo(0));
      Assert.That(School.FinalizeCounter, Is.EqualTo(1));
      Assert.That(Address.FinalizeCounter, Is.EqualTo(1));
    }



    /// <summary>
    /// Tests setting the <see cref="StudentTestBase.Student"/> field to null.
    /// As its a field, the dependency graph does not get this
    /// change without a manual update, so the dependency nodes
    /// still hold weak references. These should, however, not
    /// prevent any nodes from being garbage collected.
    /// </summary>
    [Test]
    public void Cleared_Root_Should_Be_Finalizable_Even_If_Reference_Is_Still_In_Place()
    {
      Assert.That(Student.FinalizeCounter, Is.EqualTo(0));
      Assert.That(School.FinalizeCounter, Is.EqualTo(0));
      Assert.That(Address.FinalizeCounter, Is.EqualTo(0));
      
      //clear finalization
      Assert.AreSame(Dependency.ChildNode.NodeValue, Student);
      Student = null;

      GC.Collect();
      GC.WaitForPendingFinalizers();

      Assert.That(Student.FinalizeCounter, Is.EqualTo(1));
      Assert.That(School.FinalizeCounter, Is.EqualTo(1));
      Assert.That(Address.FinalizeCounter, Is.EqualTo(1));
    }


  }
}
