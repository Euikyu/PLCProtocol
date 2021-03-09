using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace PLCProtocol_TestApp
{
    /// <summary>
    /// MainWindow.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            DataContext = new MainWindow_ViewModel();
        }
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            (DataContext as MainWindow_ViewModel).OnLoad();
        }
        private void Connect_button_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as MainWindow_ViewModel).Connect_button_Click();
        }

        private void Write_button_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as MainWindow_ViewModel).Write_button_Click();
        }

        private void Read_button_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as MainWindow_ViewModel).Read_button_Click();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            (DataContext as MainWindow_ViewModel).Window_Closing();
        }

        private void NewWriteCommand_button_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as MainWindow_ViewModel).NewWriteCommand_button_Click();
        }

        private void DeleteWriteCommand_Button_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as MainWindow_ViewModel).DeleteWriteCommand_button_Click(write_dataGrid.SelectedIndex);
        }

        private void NewReadCommand_button_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as MainWindow_ViewModel).NewReadCommand_button_Click();
        }

        private void DeleteReadCommand_Button_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as MainWindow_ViewModel).DeleteReadCommand_button_Click(read_dataGrid.SelectedIndex);
        }
    }
}
