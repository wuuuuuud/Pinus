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
using NCalc;
using System.Data;
using System.Collections.ObjectModel;
using System.Diagnostics;


namespace Pinus
{
    /// <summary>
    /// WChannelConfig.xaml 的交互逻辑
    /// </summary>
    public partial class WChannelConfig : Window
    {
        public VirtualChannel VChannel;
        public ObservableCollection<KVP> OCD;
        public enum CCTYPE {create =1,modify};//Channel config type
        public WChannelConfig()
        {
            InitializeComponent();
            TbExpression.TextChanged += TbExpression_TextChanged;
        }
        public WChannelConfig(VirtualChannel vchannel,CCTYPE type)
        {
            InitializeComponent();
            TbExpression.TextChanged += TbExpression_TextChanged;
            OCD = new ObservableCollection<KVP>();
            foreach (var kvp in vchannel.privateDictionary)
            {
                OCD.Add(new KVP(kvp.Key, kvp.Value.ToString()));
            }
            DGVariables.DataContext = OCD;
            switch (type)
            {
                case CCTYPE.modify:
                    {
                        VChannel = vchannel;
                        break;
                    }
                case CCTYPE.create:
                    {
                        VChannel = vchannel;
                        break;
                    }
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            VChannel.config["name"] = "";
            this.Close();
        }

        private void TbExpression_TextChanged(object sender, TextChangedEventArgs e)
        {

            try
            {

                var expr = new NCalc.Expression(((TextBox)sender).Text);
                if (expr.HasErrors())
                {
                    BdValidIndicator.Background = Brushes.Red;
                    TbErrorMessage.Text = expr.Error;

                }
                {
                    BdValidIndicator.Background = Brushes.Green;
                }
            }
            catch (Exception ex)
            {
                BdValidIndicator.Background = Brushes.Red;
            }
            
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (BdValidIndicator.Background == Brushes.Green)
            {
                VChannel.expression = new NCalc.Expression(TbExpression.Text);
                VChannel.config.Add("name", TbChannelName.Text);
                foreach (var kvp in OCD)
                {
                    VChannel.privateDictionary[kvp.first] = kvp.second;
                }
                this.Close();
            }
            else
            {
                MessageBox.Show("Invalid expression, please check again.");

            }
        }


    }
    public class KVP
    {
        public string first { set; get; }
        public string second { set; get; }
        public KVP(string _1,string _2)
        {
            first=_1;
            second=_2;
            
        }
        public KVP()
        {
            first = "";
            second = "";
            
        }
     }
}
