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
using System.Windows.Shapes;

namespace Pinus
{
    /// <summary>
    /// HardwareConfig.xaml 的交互逻辑
    /// </summary>
    public partial class HardwareConfig : Window
    {
        public IList<String> VariantName;
        public HardwareConfig()
        {
            InitializeComponent();
        }
        public HardwareConfig(ref IList<String> sVariantName, int nDevice, int nChannel)
        {
            InitializeComponent();
            VariantName = sVariantName;
            tbVariantName.Text = VariantName.First();
            lbDeviceNumber.Content = nDevice.ToString();
            lbChannelNumber.Content = nChannel.ToString();
        }

        private void btnConfirm_Click(object sender, RoutedEventArgs e)
        {
            VariantName[0] = tbVariantName.Text;             
            this.Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            VariantName[0] = "";
            this.Close();
        }
    }
}
