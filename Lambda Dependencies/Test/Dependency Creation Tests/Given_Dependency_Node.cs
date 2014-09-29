using NUnit.Framework;


namespace Hardcodet.Util.Dependencies.Testing.Dependency_Creation_Tests
{
  /// <summary>
  /// 
  /// </summary>
  public class Given_Dependency_Node : StudentTestBase
  {

    /// <summary>
    /// 
    /// </summary>
    [Test]
    public void Returned_Dependency_Should_Be_Root_Node()
    {
      Assert.IsTrue(Dependency.IsRootNode);
      Assert.AreSame(Dependency, Dependency.RootNode);
    }


    /// <summary>
    /// 
    /// </summary>
    [Test]
    public void Root_Node_Should_Not_Provide_Parent_Or_Parent_Member()
    {
      Assert.IsNull(Dependency.ParentNode);
      Assert.IsNull(Dependency.ParentMember);
    }


    /// <summary>
    /// 
    /// </summary>
    [Test]
    public void Chain_Should_Contain_Nodes_For_Every_Member()
    {
      //get student item
      var studentNode = Dependency.FindNode(() => Student);
      Assert.AreSame(studentNode.ParentNode, Dependency);
      Assert.AreSame(Dependency, studentNode.RootNode);
      Assert.AreSame(studentNode, Dependency.ChildNode);
      Assert.IsFalse(studentNode.IsLeafNode);
      Assert.IsFalse(studentNode.IsRootNode);

      //get school item
      var schoolNode = Dependency.FindNode(() => Student.School);
      Assert.AreSame(studentNode, schoolNode.ParentNode);
      Assert.AreSame(Dependency, schoolNode.RootNode);
      Assert.AreSame(schoolNode, studentNode.ChildNode);
      Assert.IsFalse(schoolNode.IsLeafNode);
      Assert.IsFalse(schoolNode.IsRootNode);

      //get address item
      var adressNode = Dependency.FindNode(() => Student.School.Address);
      Assert.AreSame(schoolNode, adressNode.ParentNode);
      Assert.AreSame(Dependency, adressNode.RootNode);
      Assert.AreSame(adressNode, schoolNode.ChildNode);
      Assert.IsFalse(adressNode.IsLeafNode);
      Assert.IsFalse(adressNode.IsRootNode);

      //get city item
      var cityNode = Dependency.FindNode(() => Student.School.Address.City);
      Assert.AreSame(adressNode, cityNode.ParentNode);
      Assert.AreSame(Dependency, cityNode.RootNode);
      Assert.AreSame(cityNode, adressNode.ChildNode);
      Assert.IsTrue(cityNode.IsLeafNode);
      Assert.IsFalse(cityNode.IsRootNode);
      Assert.IsNull(cityNode.ChildNode);


      Assert.AreSame(cityNode, Dependency.LeafNode);
      Assert.AreEqual("City", Dependency.LeafMemberName);
      Assert.AreSame(cityNode, studentNode.LeafNode);
      Assert.AreEqual("City", studentNode.LeafMemberName);
      Assert.AreSame(cityNode, schoolNode.LeafNode);
      Assert.AreEqual("City", schoolNode.LeafMemberName);
      Assert.AreSame(cityNode, adressNode.LeafNode);
      Assert.AreEqual("City", adressNode.LeafMemberName);
      Assert.AreSame(cityNode, cityNode.LeafNode);
      Assert.AreEqual("City", cityNode.LeafMemberName);

    }


    /// <summary>
    /// 
    /// </summary>
    [Test]
    public void Setting_An_Intermediary_To_Null_Breaks_The_Chain()
    {
      Assert.IsFalse(Dependency.IsChainBroken);
      Student.School = null;
      Assert.IsTrue(Dependency.IsChainBroken);
    }
  }
}
