using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace WPGDemoApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class Window2 : Window
    {
        public Window2()
        {            
            InitializeComponent();            
        }

        private void button_Click(object sender, RoutedEventArgs e)
        {
            myGrid.Instance = myButton;
        }

        private void myCheckbox_Click(object sender, RoutedEventArgs e)
        {
            myGrid.Instance = myCheckbox;
        }

        private void myProgressBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            myGrid.Instance = myProgressBar;
        }

        private void myButton_MouseDown(object sender, MouseButtonEventArgs e)
        {
            myGrid.Instance = myButton;
        }
       

        System.Windows.Forms.Button mybutt;

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {

            System.Windows.Forms.Integration.WindowsFormsHost myhost = new System.Windows.Forms.Integration.WindowsFormsHost();
            mybutt = new System.Windows.Forms.Button();
            mybutt.Width = 180;
            mybutt.Height = 20;
            mybutt.Text = "Winforms Button";
            mybutt.Name = "mybutt";
            myhost.Child = mybutt;
            mybutt.Click += new EventHandler(mybutt_Click);
            myCanvasHost.Children.Add(myhost);
        }

        void mybutt_Click(object sender, EventArgs e)
        {
            myGrid.Instance = mybutt;
        }
    }
}
