using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;
using System.Xml;
using Hardcodet.Util.Dependencies.Testing.TestModel;
using NUnit.Framework;


namespace Hardcodet.Util.Dependencies.Testing.Event_Publishing_Tests
{
  /// <summary>
  /// Tests cases where a dependency includes a field. In this
  /// case, changes are not picked up, and need to be maintained
  /// manually.
  /// </summary>
  public class Given_Dependency_Graph_Contains_Fields : StudentTestBase
  {

    protected override void CreateDependency()
    {
      //register field rather than property
      Dependency = DependencyNode.Create(() => Student.School.address.City);
    }

    /// <summary>
    /// 
    /// </summary>
    [Test]
    public void Change_Of_The_Target_Value_Should_Produce_Event()
    {
      Assert.AreEqual(0, ChangeEventCount);
      Student.School.Address.City = "Change 1";
      Assert.AreEqual(1, ChangeEventCount);
      Student.School.Address.City = null;
      Assert.AreEqual(2, ChangeEventCount);
      Student.School.Address.City = "Change 2";
      Assert.AreEqual(3, ChangeEventCount);
    }


    /// <summary>
    /// 
    /// </summary>
    [Test]
    public void Change_Of_The_Field_Should_Not_Produce_An_Event_And_Still_Track_Old_Reference()
    {
      //cache the old address, then replace
      var oldAddress = Student.School.Address;
      Student.School.Address = TestUtil.CreateAddress("Rome");

      //still has the old instance?
      Assert.AreNotSame(Student.School.Address, Dependency.LeafNode.ParentNode.NodeValue);
      Assert.AreSame(oldAddress, Dependency.LeafNode.ParentNode.NodeValue);
      Assert.AreEqual(0, ChangeEventCount);

      //updating the old address should fire event (not desirable at all...)
      oldAddress.City = "Berlin";
      Assert.AreEqual(1, ChangeEventCount);
      Assert.AreEqual("Berlin", Dependency.LeafValue);
    }


    /// <summary>
    /// 
    /// </summary>
    [Test]
    public void Manually_Updating_The_Field_Value_Should_Update_The_Graph_And_Fire_Change_Event()
    {
      //cache the old address, then replace
      var oldAddress = Student.School.Address;
      var newAddress = TestUtil.CreateAddress("Paris");
      Student.School.Address = newAddress;

      //still has the old instance?
      Assert.AreEqual(oldAddress.City, Dependency.LeafValue);
      Assert.AreNotSame(newAddress, Dependency.LeafNode.ParentNode.NodeValue);
      Assert.AreSame(oldAddress, Dependency.LeafNode.ParentNode.NodeValue);
      Assert.AreEqual(0, ChangeEventCount);

      //update the node value manually
      Dependency.LeafNode.ParentNode.SetNodeValue(newAddress, true);

      //update should have been performed, and the event should have fired
      Assert.AreEqual("Paris", Dependency.LeafValue);
      Assert.AreSame(newAddress, Dependency.LeafNode.ParentNode.NodeValue);
      Assert.AreSame(newAddress, Student.School.Address);
      Assert.AreEqual(1, ChangeEventCount);
      Assert.AreEqual("Paris", LastEventArgs.TryGetLeafValue(""));
      //the announced member name should return the field name
      Assert.AreEqual("address", LastEventArgs.ChangedMemberName);
      //we exchanged a member in the graph
      Assert.AreEqual(DependencyChangeSource.DependencyGraphChange, LastEventArgs.Reason);
    }



    /// <summary>
    /// 
    /// </summary>
    [Test]
    public void Change_On_The_Fields_Parent_Should_Update_The_Graph_Including_The_Field()
    {
      var school = TestUtil.CreateTestStudent("Harvard").School;
      Student.School = school;

      Assert.AreEqual(1, ChangeEventCount);
      Assert.AreEqual(DependencyChangeSource.DependencyGraphChange, LastEventArgs.Reason);

      //changed reference for field and field's City property?
      Assert.AreSame(school.Address, Dependency.LeafNode.ParentNode.NodeValue);
      Assert.AreEqual("Harvard", Dependency.LeafValue);

      //update dependency target
      school.Address.City = "New York";
      Assert.AreEqual(2, ChangeEventCount);
      Assert.AreEqual("New York", Dependency.LeafValue);
      Assert.AreEqual("New York", LastEventArgs.TryGetLeafValue(""));
    }


    
    public void SetFieldDependency()
    {
      Student = TestUtil.CreateTestStudent("XXXXX");

      //create dependency on the address field rather than the Address property
      var dep = DependencyNode.Create(() => Student.School);
      dep.DependencyChanged += 
          (node, e) => Console.WriteLine("City: " + e.TryGetLeafValue());
      
      //exchange address
      var newAddress = new Address {City = "Stockholm"};
      Student.School.address = newAddress;
      
      //get the node that represents the address
      var addressFieldNode = dep.FindNode(() => Student.School);
      
      //set the node value manually and trigger change event
      addressFieldNode.SetNodeValue(newAddress, true);

      //make sure the item is garbage collected
      TestUtil.ResetFinalizationCounters();
      dep.Dispose();
      dep = null;

      Student = null;
      newAddress = new Address();
      newAddress.city = "XX";

      GC.Collect();
      GC.WaitForPendingFinalizers();

      Assert.AreEqual(1, Student.FinalizeCounter);
      Assert.AreEqual(1, School.FinalizeCounter);
      Assert.AreEqual(1, Address.FinalizeCounter);
    }


    /// <summary>
    /// 
    /// </summary>
    [Test]
    public void Manually_Setting_Leaf_Value_Should_Reflect_Proper_Reason()
    {
      //change the field
      string granada = "Granada";
      ((Address)Student.School.Address).city = granada;
      Assert.AreEqual(0, ChangeEventCount);
      Assert.AreNotEqual(granada, Dependency.LeafValue);

      Assert.IsNull(LastEventArgs);

      //run update
      Dependency.LeafNode.SetNodeValue(granada, true);
      Assert.AreEqual(granada, Dependency.LeafValue);
      Assert.AreEqual("City", LastEventArgs.ChangedMemberName);
      Assert.AreEqual(DependencyChangeSource.TargetValueChange, LastEventArgs.Reason);
    }

  }
}
