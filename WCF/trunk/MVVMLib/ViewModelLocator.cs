namespace MVVMLib
{
    public class ViewModelLocator
    {
        public ViewModelIndexer Find { get; private set; }
        public ViewModelIndexer FindShared { get; private set; }

        public ViewModelLocator()
        {
            Find = new ViewModelIndexer(){IsShared = false};
            FindShared = new ViewModelIndexer(){IsShared = true};
        }
    }

}
