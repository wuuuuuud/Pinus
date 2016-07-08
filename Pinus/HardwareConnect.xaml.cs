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
using System.Windows.Shapes;
using CyUSB;
using OxyPlot.Wpf;
using OxyPlot.Series;
using System.ComponentModel;
using System.Threading;
using System.Diagnostics;
namespace Pinus
{
    /// <summary>
    /// HardwareConnect.xaml 的交互逻辑
    /// </summary>
    public partial class HardwareConnect : Window
    {
        public BackgroundWorker bgw;
        public Stopwatch sw1;
        public HardwareConnect()
        {
            InitializeComponent();
            btnRefresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            sw1 = new Stopwatch();
            sw1.Start();
        }

        private void btnRefresh_Click(object sender, RoutedEventArgs e)
        {
            cmbDeviceList.Items.Clear();
            foreach (USBDevice item in USB.USBDevices)
            {
                cmbDeviceList.Items.Add("Index:" + item.USBAddress.ToString()
                    + " " + item.Name);
            }
            if (cmbDeviceList.Items.Count > 0)
            {
                cmbDeviceList.SelectedItem = cmbDeviceList.Items[0];
            }
        }

        private void cmbDeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count <= 0) return;
            int index = ((ComboBox)sender).Items.IndexOf(e.AddedItems[0]);
            if (index < 0) return;
            CyUSBDevice device = USB.USBDevices[index] as CyUSBDevice;
            lbType.Content = (device.bSuperSpeed) ? "SuperSpeed USB3.0设备已连接" :
                (device.bHighSpeed) ? "HighSpeed USB2.0设备已连接" : "FullSpeed USB1.0设备已连接";
            lbVID.Content = "制造商编号:" + device.VendorID.ToString();
            lbPID.Content = "产品编号:" + device.ProductID.ToString();

        }

        private void btnDownload_Click(object sender, RoutedEventArgs e)
        {
            int index = (cmbDeviceList.SelectedIndex);
            if (index < 0) return;
            CyFX3Device device = USB.USBDevices[index] as CyFX3Device;
            FX3_FWDWNLOAD_ERROR_CODE error = device.DownloadFw(tbFirmwarePath.Text, FX3_FWDWNLOAD_MEDIA_TYPE.RAM);
            if (error == 0)
            {
                MessageBox.Show("Success!");
            }
            else
            {
                MessageBox.Show("An error occurs, error code:" + error.ToString());
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            
            

        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            
        }
    }


}
