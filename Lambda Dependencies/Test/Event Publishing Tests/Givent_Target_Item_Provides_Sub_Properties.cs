using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Xml;
using Hardcodet.Util.Dependencies.Testing.TestModel;
using NUnit.Framework;


namespace Hardcodet.Util.Dependencies.Testing.Event_Publishing_Tests
{
  /// <summary>
  /// 
  /// </summary>
  [TestFixture]
  public class Givent_Target_Item_Provides_Sub_Properties : StudentTestBase
  {
    private DependencyNode<IAddress> addressDependency;
    private int changeCounter;

    protected override void Init()
    {
      base.Init();
      changeCounter = 0;
      addressDependency = DependencyNode.Create(() => Student.School.Address);
      addressDependency.DependencyChanged += (node, e) => changeCounter++;

      //reset the base class dependency (not used here)
      ResetDependency();
    }


    /// <summary>
    /// 
    /// </summary>
    [Test]
    public void Sub_Properties_Should_Cause_Change_Events_By_Default()
    {
      //create dependency on address object
      var dep1 = DependencyNode.Create(() => Student.School.Address);

      //output the changed property name and the change reason
      dep1.DependencyChanged += (node, e) =>
            {
              Assert.AreEqual(DependencyChangeSource.SubValueChanged, e.Reason);
              Assert.AreEqual("City", e.ChangedMemberName);
            };

      //change the city
      Student.School.Address.City = "Ethon";
      Assert.AreEqual(1, changeCounter);
    }


    /// <summary>
    /// 
    /// </summary>
    [Test]
    public void Sub_Property_Tracking_Can_Be_Disabled_Through_Settings()
    {
      //create settings class
      var settings = new DependencyNodeSettings<IAddress>(() => Student.School.Address);
      
      //configure settings to ignore sub item changes
      settings.ObserveSubValueChanges = false;
      
      //dispose old dependency in order to get rid of event listener!
      addressDependency.Dispose();
      addressDependency = DependencyBuilder.CreateDependency(settings);

      //does not cause a change event:
      Student.School.Address.City = "Ethon";
      Assert.AreEqual(0, changeCounter);
    }
  }


}
