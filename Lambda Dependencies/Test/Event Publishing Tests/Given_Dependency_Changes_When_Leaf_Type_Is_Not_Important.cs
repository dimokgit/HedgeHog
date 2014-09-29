using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Hardcodet.Util.Dependencies.Testing.Event_Publishing_Tests
{
  public class Given_Dependency_Changes_When_Leaf_Type_Is_Not_Important : StudentTestBase
  {
    private IDependencyChangeInfo lastEventInfo;

    /// <summary>
    /// 
    /// </summary>
    [Test]
    public void Registering_Type_Unaware_Handler_Supports_Different_Properties ()
    {
      //deregister default listener and register a new one
      Dependency.DependencyChanged -= OnDependencyChanged;  
      Dependency.DependencyChanged += OnAnyDependencyChanged;

      var schoolDependency = DependencyNode.Create(() => Student.School.SchoolId);
      schoolDependency.DependencyChanged += OnAnyDependencyChanged;

      //change int value
      Student.School.SchoolId = 999;
      Assert.AreEqual("SchoolId", lastEventInfo.ChangedMemberName);
      Assert.AreEqual(999, lastEventInfo.TryGetLeafValue(0));

      //change string
      Student.School.Address.City = "Philadelphia";
      Assert.AreEqual("City", lastEventInfo.ChangedMemberName);
      Assert.AreEqual("Philadelphia", lastEventInfo.TryGetLeafValue(""));
    }



    private void OnAnyDependencyChanged(object sender, IDependencyChangeInfo e)
    {
      lastEventInfo = e;
      ChangeEventCount++;
    }
  }
}
