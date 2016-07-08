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
using Xceed.Wpf.AvalonDock.Layout;
using System.Collections.ObjectModel;
using OxyPlot.Series;

namespace Pinus
{
    /// <summary>
    /// UserControl1.xaml 的交互逻辑
    /// </summary>
    public partial class UserControl1 : LayoutAnchorable
    {
        public EventHandler ConfigChanged;
        public EventHandler ViewChanged;
        public EventHandler ViewReset;
        public bool needRefresh,holding;
        public UserControl1()
        {
            InitializeComponent();
            mainGrid.DataContext = this;
            opMainPlot.Model = new OxyPlot.PlotModel();
            opMainPlot.Model.Series.Add(new LineSeries());
            opMainPlot.InvalidatePlot();
            needRefresh = true;
            holding = false;
            //sourceIndex = 0;
        }
        public int sourceIndex
        {
            get;
            set;
        }

        public ObservableCollection<bool?> enabledChannel = 
            new ObservableCollection<bool?> { true, true, true, true };
        public ObservableCollection<bool?> EnabledChannel
        {
            get { return enabledChannel; }
            set { enabledChannel = value;}
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sourceIndex == 0) this.Title="波形";
            else if (sourceIndex == 1) this.Title="频谱";
            else this.Title= "";
            if (ViewReset != null) ViewReset(this, e);
        }

        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (ConfigChanged != null) ConfigChanged(this, e);
        }

        private void opMainPlot_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ViewChanged != null) ViewChanged(this, e);
        }

        private void btnReset_Click(object sender, RoutedEventArgs e)
        {
            if (ViewReset != null) ViewReset(this, e);
        }



    }
}
