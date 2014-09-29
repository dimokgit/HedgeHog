using Hardcodet.Util.Dependencies.Testing.TestModel;
using NUnit.Framework;

namespace Hardcodet.Util.Dependencies.Testing.Event_Publishing_Tests
{
  public class Given_Node_Value_When_Is_Changed : StudentTestBase
  {

    /// <summary>
    /// 
    /// </summary>
    [Test]
    public void Changing_Leaf_Value_Should_Announce_Target_Value_Change()
    {
      Student.School.Address.City = "Something else";
      Assert.AreEqual(1, ChangeEventCount);
      Assert.AreEqual(DependencyChangeSource.TargetValueChange, LastEventArgs.Reason);
    }


    /// <summary>
    /// 
    /// </summary>
    [Test]
    public void Setting_Leaf_Value_To_Null_Should_Announce_Target_Value_Change()
    {
      Student.School.Address.City = null;
      Assert.AreEqual(1, ChangeEventCount);
      Assert.AreEqual(DependencyChangeSource.TargetValueChange, LastEventArgs.Reason);
    }


    [Test]
    public void Setting_Intermediary_Node_To_Null_Should_Announce_Broken_Chain()
    {
      Student.School.Address = null;
      Assert.AreEqual(1, ChangeEventCount);
      Assert.AreEqual(DependencyChangeSource.ChainBroken, LastEventArgs.Reason);

      Student.School = null;
      Assert.AreEqual(2, ChangeEventCount);
      Assert.AreEqual(DependencyChangeSource.ChainBroken, LastEventArgs.Reason);

      //setting student does not trigger change event
      Student = null;
      Assert.AreEqual(2, ChangeEventCount);
      //set value manually
      Dependency.FindNode(() => Student).SetNodeValue(null, true);
      Assert.AreEqual(3, ChangeEventCount);
      Assert.AreEqual(DependencyChangeSource.ChainBroken, LastEventArgs.Reason);
    }


    /// <summary>
    /// 
    /// </summary>
    [Test]
    public void Setting_Intermediary_Without_Initialized_Sub_Graph_Should_Announce_Broken_Chain()
    {
      var school = TestUtil.CreateTestStudent("Munich").School;
      school.Address = null;

      Student.School = school;
      Assert.IsTrue(Dependency.IsChainBroken);
      Assert.AreEqual(1, ChangeEventCount);
      Assert.AreEqual(DependencyChangeSource.ChainBroken, LastEventArgs.Reason);
    }


    /// <summary>
    /// 
    /// </summary>
    [Test]
    public void Setting_Intermediary_Without_Null_Target_Value_Should_Announce_Changed_Graph_Value()
    {
      var school = TestUtil.CreateTestStudent("Munich").School;
      school.Address.City = null;
      Student.School = school;

      Assert.AreEqual(1, ChangeEventCount);
      Assert.AreEqual(DependencyChangeSource.DependencyGraphChange, LastEventArgs.Reason);
      Assert.AreEqual(null, Dependency.LeafValue);
    }



    /// <summary>
    /// 
    /// </summary>
    [Test]
    public void Changing_Intermediary_Should_Announce_Graph_Change()
    {
      Student.School.Address = TestUtil.CreateAddress("Amsterdam");
      Assert.AreEqual(1, ChangeEventCount);
      Assert.AreEqual(DependencyChangeSource.DependencyGraphChange, LastEventArgs.Reason);
      Assert.AreEqual("Amsterdam", Dependency.LeafValue);

      Student.School = TestUtil.CreateTestStudent("Moskau").School;
      Assert.AreEqual(2, ChangeEventCount);
      Assert.AreEqual(DependencyChangeSource.DependencyGraphChange, LastEventArgs.Reason);

      //setting student does not trigger change event
      Student = TestUtil.CreateTestStudent("Warshaw");
      Assert.AreEqual(2, ChangeEventCount);
      //set value manually
      Dependency.FindNode(() => Student).SetNodeValue(null, true);
      Assert.AreEqual(3, ChangeEventCount);
      Assert.AreEqual(DependencyChangeSource.ChainBroken, LastEventArgs.Reason);
    }
  }
}
