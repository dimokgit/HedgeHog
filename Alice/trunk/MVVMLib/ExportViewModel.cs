using System;
using System.ComponentModel.Composition;

namespace MVVMLib
{
    [MetadataAttribute]
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public class ExportViewModel : ExportAttribute
    {
        public string Name { get; private set; }

        public ExportViewModel(string name) : base("ViewModel")
        {
            Name = name;
        }
    }
}