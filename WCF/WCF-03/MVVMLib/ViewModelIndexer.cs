using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;

namespace MVVMLib
{
    public class ViewModelIndexer
    {
        public ViewModelIndexer()
        {
            CompositionInitializer.SatisfyImports(this);
            IsShared = false;
        }

        [ImportMany("ViewModel", AllowRecomposition = true)]
        public IEnumerable<Lazy<object, IViewModelMetadata>> ViewModelsLazy { get; set; }

        [ImportMany("ViewModel", AllowRecomposition = true)]
        public IEnumerable<ExportFactory<object, IViewModelMetadata>> ViewModelsFactories { get; set; }

        private object GetViewModel(string viewModel)
        {
            //MessageBox.Show("dude");
            //Debug.WriteLine("just used me again");

            if (IsShared)
            {
                return ViewModelsLazy.Single(v => v.Metadata.Name.Equals(viewModel)).Value;
            }

            var context = ViewModelsFactories.Single(v => v.Metadata.Name.Equals(viewModel)).CreateExport();
            return context.Value;
        }

        public bool IsShared { get; set; }

        public object this[string viewModel]
        {
            get { return GetViewModel(viewModel); }
        }

    }
}