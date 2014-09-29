using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Hardcodet.Util.Dependencies.Testing.TestModel;
using NUnit.Framework;

namespace Hardcodet.Util.Dependencies.Testing.Lambda_Binding_Tests
{
  public class Given_One_Way_Binding : BindingTestBase
  {

    [Test]
    public void Updating_Source_Property_Should_Update_Target()
    {
      //the students city is different
      Assert.AreNotEqual("Sin City", Student.SchoolCity);
      
      //change the school's property
      Student.School.Address.City = "Sin City";
      
      //make sure the student's property was updated, too
      Assert.AreEqual("Sin City", Student.SchoolCity);     
    }



    [Test]
    public void Updating_Intermediary_Object_Should_Update_Target()
    {
      Student.School.Address.City = "Paradise City";
      Assert.AreEqual("Paradise City", Student.SchoolCity);
      
      //create a different school
      School school = new School();
      school.Address = new Address {City = "Sin City"};

      //assign the school to the student
      Student.School = school;

      //replacing the school triggered the binding
      Assert.AreEqual("Sin City", Student.SchoolCity); 
    }



    [Test]
    public void Breaking_The_Source_Chain_Resets_The_Target()
    {
      //keep copies
      Student student = this.Student;
      School school = Student.School;

      Student.School.Address.City = "Sin City";
      Student.School = null;
      Assert.IsNull(Student.SchoolCity);

      //reassign school
      Student.School = school;
      Assert.AreEqual("Sin City", Student.SchoolCity);

      //clear student as a whole -> resets the city as
      //the source event listener was registered first
      Student = null;
      Assert.AreEqual(null, student.SchoolCity);

      //change while being "offline"
      student.School.Address.City = "Paradise City";
      Assert.AreEqual(null, student.SchoolCity);

      //reassign city -> does not trigger update as the source
      //event is triggered first and the target dependency still
      //regards the chain as being broken.
      Student = student;
      Assert.AreEqual(null, student.SchoolCity);

      //re-trigger event - this one works
      Student = student;
      Assert.AreEqual("Paradise City", student.SchoolCity);
    }


    //a local field to be updated
    private string schoolCity;

    [Test]
    public void Breaking_The_Chain_Should_Assign_Default_Value_To_Target_If_Specified()
    {
      ResetBinding();
      LambdaBinding.BindOneWay(() => Student.School.Address.City, () => schoolCity, "[No City]");

      Student.School = null;
      Assert.AreEqual("[No City]", schoolCity);
    }



    [Test]
    public void One_Way_Binding_Should_Use_Converter_If_Specified()
    {
      Student student = new Student();
      int intValue = 0;

      LambdaBinding.BindOneWay(() => student.Name, () => intValue, name => int.Parse(name));

      student.Name = "451";
      Assert.AreEqual(451, intValue);
    }
  }
}
