using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Hardcodet.Util.Dependencies.Testing.TestModel;
using NUnit.Framework;

namespace Hardcodet.Util.Dependencies.Testing
{
  [TestFixture]
  public abstract class StudentTestBase
  {
    protected Student Student { get; set; }
    protected DependencyNode<string> Dependency { get; set; }

    protected int ChangeEventCount { get; set; }
    protected DependencyChangeEventArgs<string> LastEventArgs { get; set; }



    [SetUp]
    public void BaseInit()
    {
      LastEventArgs = null;
      ChangeEventCount = 0;
      Student = TestUtil.CreateTestStudent();

      //include the address field rather than the property!
      CreateDependency();
      Dependency.DependencyChanged += OnDependencyChanged;
      Init();

      //reset finalization counters
      TestUtil.ResetFinalizationCounters();
    }


    /// <summary>
    /// Inits the <see cref="Dependency"/> property. The default implementation
    /// registers a dependency on the school's city.
    /// </summary>
    protected virtual void CreateDependency()
    {
      Dependency = DependencyNode.Create(() => Student.School.Address.City);
    }


    [TearDown]
    public void BaseCleanup()
    {
      Student = null;
      ResetDependency();

      CleanUp();

      //force garbage collection
      GC.Collect();
      GC.WaitForPendingFinalizers();
    }


    /// <summary>
    /// Clears the <see cref="Dependency"/> property and deregisters
    /// the <see cref="OnDependencyChanged"/> event listener.
    /// </summary>
    protected void ResetDependency()
    {
      if (Dependency != null)
      {
        Dependency.DependencyChanged -= OnDependencyChanged;
        Dependency.Dispose();
      }
      Dependency = null;
    }


    /// <summary>
    /// Init method which can be overridden to
    /// provide custom initialization logic.
    /// </summary>
    protected virtual void Init()
    {
      
    }
    
    /// <summary>
    /// Default cleanup method - can be overridden to provide
    /// custom cleanup logic.
    /// </summary>
    protected virtual void CleanUp()
    {
        
    }



    /// <summary>
    /// Change event handler for the <see cref="DependencyNode{T}.DependencyChanged"/>
    /// event of the test's <see cref="Dependency"/> property.
    /// </summary>
    protected virtual void OnDependencyChanged(object sender, DependencyChangeEventArgs<string> e)
    {
      ChangeEventCount++;
      LastEventArgs = e;
    }

  }
}
