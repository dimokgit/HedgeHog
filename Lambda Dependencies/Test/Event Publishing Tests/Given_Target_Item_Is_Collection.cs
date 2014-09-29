using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Xml;
using Hardcodet.Util.Dependencies.Testing.TestModel;
using NUnit.Framework;


namespace Hardcodet.Util.Dependencies.Testing.Event_Publishing_Tests
{
  /// <summary>
  /// 
  /// </summary>
  [TestFixture]
  public class Given_Target_Item_Is_Collection
  {
    private Course course;
    private DependencyNode<ObservableCollection<Student>> dependency;
    private int eventCounter = 0;
    private DependencyChangeEventArgs<ObservableCollection<Student>> lastEventArgs;

    #region setup / teardown

    [SetUp]
    public void Init()
    {
      eventCounter = 0;
      lastEventArgs = null;

      course = new Course();
      for (int i = 0; i < 10; i++)
      {
        var student = TestUtil.CreateTestStudent();
        student.Name = "Student " + i;
        course.Students.Add(student);
      }

      dependency = DependencyNode.Create(() => course.Students);
      dependency.DependencyChanged += OnDependencyChanged;
    }

    private void OnDependencyChanged(object sender, DependencyChangeEventArgs<ObservableCollection<Student>> e)
    {
      Console.Out.WriteLine("e.Reason = {0}", e.Reason);
      Console.Out.WriteLine("e.ChangedMemberName = {0}", e.ChangedMemberName);

      eventCounter++;
      lastEventArgs = e;
    }

    #endregion


    /// <summary>
    /// 
    /// </summary>
    [Test]
    public void Collection_Changes_Should_Fire_Event_By_Default()
    {
      Assert.AreEqual(0, eventCounter);      
      //add student to collection
      course.Students.Add(TestUtil.CreateTestStudent());
      Assert.AreEqual(1, eventCounter);
      Assert.AreEqual(DependencyChangeSource.TargetCollectionChange, lastEventArgs.Reason);
      
      //remove student from collection
      course.Students.Remove(course.Students[7]);
      Assert.AreEqual(2, eventCounter);
      Assert.AreEqual(DependencyChangeSource.TargetCollectionChange, lastEventArgs.Reason);
    }


    /// <summary>
    /// 
    /// </summary>
    [Test]
    public void Replacing_Whole_Collection_Should_Fire_Regular_Event()
    {
      Assert.AreEqual(0, eventCounter);
      course.Students = new ObservableCollection<Student>();
      Assert.AreEqual(1, eventCounter);
      Assert.AreEqual(DependencyChangeSource.TargetValueChange, lastEventArgs.Reason);
    }


    /// <summary>
    /// 
    /// </summary>
    [Test]
    public void Disabling_Collection_Observation_Should_Suppress_Event()
    {
      //clear existing dependency and let the garbage collector take care of it
      //(works thanks to weak event listeners). If we didn't change listener remained active
      dependency = null;
      GC.Collect();
      GC.WaitForPendingFinalizers();

      //create settings and disable observations
      DependencyNodeSettings<ObservableCollection<Student>> settings;
      settings = new DependencyNodeSettings<ObservableCollection<Student>>(() => course.Students);
      settings.ChangeHandler = OnDependencyChanged;
      settings.ObserveSubValueChanges = false;
      
      dependency = DependencyBuilder.CreateDependency(settings);

      //add student to collection
      course.Students.Add(TestUtil.CreateTestStudent());
      Assert.AreEqual(0, eventCounter);

      //remove student from collection
      course.Students.Remove(course.Students[7]);
      Assert.AreEqual(0, eventCounter);
    }


    /// <summary>
    /// 
    /// </summary>
    [Test]
    public void Monitoring_A_Collection_Item_Should_Work()
    {
      //this is currently not supported
      var dep = DependencyNode.Create(() => course.Students.FirstOrDefault(s => s.Age > 20));
    }

  }
}
