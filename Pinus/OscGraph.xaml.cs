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
using System.IO;
using OxyPlot;
using OxyPlot.Series;

using System.Threading;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.ComponentModel;
using LineSeries = OxyPlot.Series.LineSeries;
using MathWorks.MATLAB.NET.Arrays;
using sharpfilters;
namespace Pinus
{
    /// <summary>
    /// OscGraph.xaml 的交互逻辑
    /// </summary>
    public partial class OscGraph : Window
    {
        public bool toClose;
        public BackgroundWorker bgw;
        public ConcurrentQueue<double[,]> queue;
        public FileStream fs;
        public Stopwatch sw1;
        public int activeChannel;
        public double[,] currentData;
        public bool isTrackingPeak
        {
            get;
            set;
        }
        filters matlab;
        public OscGraph(object _queue)
        {
            toClose = false;
            activeChannel = 0;//which is channel 1
            bgw = new BackgroundWorker();
            queue = _queue as ConcurrentQueue<double[,]>;
            fs = new FileStream("log.txt", FileMode.Append);
            sw1 = new Stopwatch();
            sw1.Start();
            matlab= new filters();
            this.DataContext = this;

            bgw.WorkerReportsProgress = true;
            bgw.WorkerSupportsCancellation = true;
            bgw.DoWork += delegate(object s, DoWorkEventArgs args)
            {
                var self = s as BackgroundWorker;
                bool success = false;
                double[,] number;

                while (self.CancellationPending == false)
                {
                    success = queue.TryDequeue(out number);
                    if (success & number!=null)
                    {
                        currentData = number;

                        var md = createPMFromFFTData(number, activeChannel, 2e6) as PlotModel;
                  


                        
                        self.ReportProgress(0, md);
                        Thread.Sleep(5);
                        
                    }




                    Thread.Sleep(20);
                }
                args.Cancel = true;
            };

            bgw.ProgressChanged += delegate(object s, ProgressChangedEventArgs args)
            {
                if (true || opMainPlot.IsRendering == false)
                {
                    var md = args.UserState as PlotModel;
                    opMainPlot.DisconnectCanvasWhileUpdating = true;
                    foreach (OxyPlot.Series.LineSeries series in opMainPlot.Model.Series)
                    {
                        series.Points.Clear();
                    }
                    opMainPlot.Model.Series.Clear();
                    this.opMainPlot.Model = md;
                    if (isTrackingPeak)
                   {
	                        MWNumericArray mresult = matlab.findpeak((MWNumericArray)currentData, (MWArray)(activeChannel+1)) as MWNumericArray;
	                        double[,] result = mresult.ToArray() as double[,];
	                        LineSeries ls = md.Series[0] as LineSeries;
                            ls.XAxis.Minimum = result[0,2] * (ls.Points[1].X - ls.Points[0].X);
                            ls.XAxis.Maximum = result[0,3] * (ls.Points[1].X - ls.Points[0].X);
                            opMainPlot.InvalidatePlot(true);

                        }
                    GC.Collect();

                    //this.opMainPlot.InvalidateFlag++;
                    sw1.Restart();
                    //opMainPlot.IsRendering = true;
                    //opMainPlot.UpdateLayout();
                    

                    sw1.Stop();
                    string buf = sw1.ElapsedMilliseconds + "\r\n";
                }



            };

            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            opMainPlot.Model = new OxyPlot.PlotModel();
            for (int i = 0; i < 4; i++)
            {
                opMainPlot.Model.Series.Add(new OxyPlot.Series.LineSeries());
            }
            opMainPlot.IsRendering = false;
            //opMainPlot.LayoutUpdated += delegate(object sender0, EventArgs e0)
            //{
            //    var self = opMainPlot as PlotView;
            //    self.IsRendering = false;
            //};
            bgw.RunWorkerAsync();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!toClose)
            {
                this.Hide();
                e.Cancel = true;
            }
            else
            {
                bgw.CancelAsync();
            }
        }

        private void cmbActivechannel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            activeChannel = ((ComboBox)sender).SelectedIndex;
            queue.Enqueue(currentData);
        }

        public PlotModel createPMFromFFTData(double[,] number, int channel, double sampleRate)
        {
            var md = new PlotModel();
            md.Series.Add(new OxyPlot.Series.LineSeries());// Only one channel for spectrum display.

            int k = channel;
            foreach (OxyPlot.Series.LineSeries ls in md.Series)
            {
                ls.Smooth = false;
                ls.LineJoin = LineJoin.Round;
                ls.StrokeThickness = 1;

                for (int j = 10; j < number.GetLength(1) / 1 && (j * (1 / (number.GetLength(1) * 2 / sampleRate))) < sampleRate/2; j = j + 1)
                {
                    ls.Points.Add(new DataPoint((double)j * (1 / (number.GetLength(1) * 2 / sampleRate)), number[k, j]));

                }

            }
            return md;
        }



    }
}
