using System;
using System.Threading;
using Hardcodet.Util.Dependencies.Testing.TestModel;
using NUnit.Framework;

namespace Hardcodet.Util.Dependencies.Testing
{
  [TestFixture]
  public class Given_Node_When_Maintaining_Circular_Dependencies
  {
    private ParentChildItem parent;
    private ParentChildItem child;

    [SetUp]
    public void Init()
    {
      parent = new ParentChildItem("parent");
      child = new ParentChildItem("child");

      parent.Child = child;
      child.Parent = parent;
    }


    [Test]
    public void Replacing_Child_Should_Not_Cause_Events_In_The_Disposed_Child()
    {
      parent.Child = new ParentChildItem("child2") { Parent = parent};
      child = null;

      //performing GC causes the child item to get lost, but the dependency has
      //not been removed yet - this causes the node's weak references to loose
      //their targets, but everything else is still in place
      Console.Out.WriteLine("Doing garbage collection...");
      GC.Collect();

      //set a new child -> causes an event that is caugt by the GC'd dependency's listener
      parent.Child = null;
      return;
    }

  }
}