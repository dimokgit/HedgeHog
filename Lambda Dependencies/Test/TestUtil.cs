using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Hardcodet.Util.Dependencies.Testing.TestModel;

namespace Hardcodet.Util.Dependencies.Testing
{
  public static class TestUtil
  {

    public static Student CreateTestStudent()
    {
      return CreateTestStudent("Redmond");
    }


    public static Student CreateTestStudent(string city)
    {
      //create student to run tests against
      Address address = CreateAddress(city);
      School school = new School { Address = address };
      return new Student { School = school };
    }

    public static Address CreateAddress(string city)
    {
      return new Address { City = city };
    }

    public static void ResetFinalizationCounters()
    {
      Student.FinalizeCounter = School.FinalizeCounter = Address.FinalizeCounter = 0;
    }
  }
}
