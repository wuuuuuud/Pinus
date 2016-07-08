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
using System.Windows.Controls.Ribbon;
using System.Windows.Threading;
using Microsoft.VisualBasic;
using NCalc;
using System.Windows.Markup;
using System.Timers;
using System.Threading;
using System.Diagnostics;
using OxyPlot;
using OxyPlot.Axes;
using SharpVectors.Net;
using SharpVectors.Converters;
using SharpVectors.Runtime;
using SharpVectors.Renderers.Wpf;
using System.IO;
using System.Collections.Concurrent;
using System.Runtime.Serialization.Formatters.Binary;
using CyUSB;
using System.Data;
using System.Runtime.InteropServices;
using System.ComponentModel;
using Microsoft.Win32;
using System.Globalization;
using MathWorks.MATLAB.NET.Arrays;
using sharpfilters;
using System.Drawing;
using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;
using DBrushes = System.Drawing.Brushes;
using System.Configuration;
using MathNet.Filtering;
using IllusoryStudios.Wpf.LostControls;
using Xceed.Wpf.AvalonDock.Layout;
using Xceed.Wpf.Toolkit;
using MessageBox = Xceed.Wpf.Toolkit.MessageBox;
using Color = System.Drawing.Color;



namespace Pinus
{
    /// <summary>
    /// Main.xaml 的交互逻辑
    /// </summary>
    public partial class Main : Window
    {
        public Logging wLogging;
        public static int phyChannel1;
        public static int nDeviceCount;
        public static int nFailCount;
        public List<VirtualChannel> Channels;
        public List<RibbonSplitButton> ChannelControls;
        public List<PhysicalChannel> PhysicalChannels;
        public SynchronizationContext _context;
        public System.Timers.Timer THardwareRoutine;
        public Stopwatch SwGlobal;
        public FileSvgReader s;
        public static IDictionary<String, DrawingImage> ImageSet;
        public IDictionary<string, object> publicDictionary;
        public ConcurrentQueue<byte[]> dataQueue;
        public ConcurrentQueue<DataAcquireOrder> dataThreadQueue;
        public ConcurrentQueue<Int32[,]> unpackedNumberQueue;
        public ConcurrentQueue<Double[,]> fftNumberQueue;
        public Thread dataAcquireThread, dataSaveThread, dataProcessThread;
        public IDictionary<string, object> globalConfig;
        public OscGraph OscGraphWindow;
        public string log;
        public BackgroundWorker bgw;
        public Int32[,] BufferedUnpackedValue;
        public double[,] BufferedCalculatedValue;
        public double[,] BufferedFFTValue;
        public string savePath;
        public object lpfilter1;
        public filters matlab;
        public Double[,] processedData;
        public List<USBChannel> USBChannels;
        public string filename;
        public KeyValueConfigurationCollection AppConfig;
        public List<UserControl1> AnchorablePlots;
        public EventHandler<EventArgs> OnUSBStatusChanged;
        public EventHandler<EventArgs> OnRenderStatusChanged;

        public enum RenderMode { Hold, New, Reset };

        public Main()
        {
            var configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            AppConfig = configFile.AppSettings.Settings;
            AppConfig.Add("SaveData", "true");
            configFile.Save();
            matlab = new filters();
            lpfilter1 = matlab.createfilter((MWArray)10, (MWArray)0.001);
            savePath = "Data\\";
            BufferedUnpackedValue = null;
            processedData = null;
            dataQueue = new ConcurrentQueue<byte[]>();
            dataThreadQueue = new ConcurrentQueue<DataAcquireOrder>();
            fftNumberQueue = new ConcurrentQueue<Double[,]>();
            
            // Set default config values
            globalConfig = new ConcurrentDictionary<string, object>();
            globalConfig.Add("DisplayLength", 0);//In MegaBytes, 1KB:=1024B
            globalConfig.Add("SampleRate", 2.0);
            globalConfig.Add("SaveLength", 32.0);
            unpackedNumberQueue = new ConcurrentQueue<Int32[,]>();
            globalConfig.Add("SaveData", true);
            for (int i = 1; i <= 4; i++)
            {
                globalConfig.Add("showChannel" + i.ToString(), true);
            }

            publicDictionary = new Dictionary<string, object>();
            publicDictionary["wavelength"] = 632.991354;
            publicDictionary["compensation"] = 0.99972871;

            log = "";
            bgw = new BackgroundWorker();
            bgw.WorkerReportsProgress = true;
            bgw.WorkerSupportsCancellation = true;
            bgw.DoWork += delegate(object s, DoWorkEventArgs args)
            {
                var self = s as BackgroundWorker;
                bool success = false;
                Int32[,] number;

                while (self.CancellationPending == false)
                {
                    success = unpackedNumberQueue.TryDequeue(out number);
                    if (success)
                    {

                        BufferedUnpackedValue = number;
                        int k = 0;
                        //foreach (RibbonSplitButton rsb in ChannelControls)
                        //{
                        //    USBChannel channel = USBChannels[k];
                        //    if (channel.IsDisplacement)
                        //    {

                        //    }
                        //    k++;
                        //}

                        BufferedCalculatedValue = new double[ChannelControls.Count, BufferedUnpackedValue.GetLength(1)];
                        BufferedFFTValue = new double[BufferedUnpackedValue.GetLength(0), BufferedUnpackedValue.GetLength(1)];
                        k = 0;
                        foreach (RibbonSplitButton rsb in ChannelControls)
                        {
                            USBChannel channel = USBChannels[k];
                            
                            if (channel.IsVelocity)
                            {
                                MWArray filter;
                                if (channel.normalizedHF < channel.normalizedLF )
                                {
                                    if ( channel.normalizedHF !=0.0)
                                        filter = matlab.createfilter((MWArray)channel.filterOrder,
                                            (MWNumericArray)(new double[2,1]{{channel.normalizedHF},{channel.normalizedLF}}));
                                    else
                                        filter = matlab.createfilter((MWArray)channel.filterOrder,
                                            (MWNumericArray)channel.normalizedLF);
                                }
                                else
                                {
                                    continue;
                                }
                                int length = BufferedUnpackedValue.GetLength(1);
                                double[] data = new double[length];
                                for (int ii = 0; ii < length;ii++ )
                                {
                                    data[ii] = BufferedUnpackedValue[k, ii];
                                }
                                //Array.Copy(BuffedUnpackedValue, k * length, data, 0, length);
                                MWNumericArray result = matlab.lpfilter((MWNumericArray)data, (MWArray)filter) as MWNumericArray;
                                MWNumericArray fftresult = matlab.ffta((MWArray)result) as MWNumericArray;
                                double[,] Rdata = result.ToArray() as double[,];
                                double[,] FData = fftresult.ToArray() as double[,];
                                Array.Copy(Rdata, 0, BufferedCalculatedValue, k * length, length);
                                #region Convert to correct velocity
                                //The code here needs to be more generic and flexible.
                                for (int l = 0; l < length; l++)
                                {
                                    BufferedCalculatedValue[k, l] = BufferedCalculatedValue[k, l] * 0.6328 / (2 * Math.PI) / 8192 / 2 * ((double)globalConfig["SampleRate"] * 1e6);
                                }
                                #endregion
                                Array.Copy(FData,0,BufferedFFTValue,k*length,FData.GetLength(1));
                            }
                            else if (channel.IsDisplacement)
                            {
                                int length = BufferedUnpackedValue.GetLength(1);
                                double[] data = new double[length];
                                for (int ii = 0; ii < length; ii++) // Refactor the data for FFT
                                {
                                    data[ii] = BufferedUnpackedValue[k, ii];
                                }
                                MWNumericArray fftresult = matlab.ffta((MWNumericArray)data) as MWNumericArray;
                                double[,] FData = fftresult.ToArray(MWArrayComponent.Real) as double[,];
                                Array.Copy(FData, 0, BufferedFFTValue, k * length, FData.GetLength(1));
                                Array.Copy(BufferedUnpackedValue, k*length, BufferedCalculatedValue, k * length, length);
                                #region Convert to correct distance
                                //The code here needs to be more generic and flexible.
                                for (int l =0;l<length;l++)
                                {
                                    BufferedCalculatedValue[k, l] = BufferedCalculatedValue[k, l] * 0.6328 / (2 * Math.PI) / 8192 / 2;
                                }
                                #endregion



                            }
                            k++;
                        }
                        BufferedFFTValue = ResizeArray(BufferedFFTValue,new int[2]{BufferedFFTValue.GetLength(0),BufferedFFTValue.GetLength(1)/2}) as double[,];
                        
                        //if (fftNumberQueue.Count < 1) fftNumberQueue.Enqueue(BufferedFFTValue);
                        
                        //var md = new PlotModel();
                        ////md.Updated += MainPlotUpdated;
                        //List<int> usedChannel = new List<int>();
                        //md.LegendPlacement = LegendPlacement.Inside;
                        //md.IsLegendVisible = true;
                        //for (int i = 0; i < 4; i++)
                        //{
                        //    if ((bool)globalConfig["showChannel" + (i + 1).ToString()])
                        //    {
                        //        md.Series.Add(new OxyPlot.Series.LineSeries());
                        //        usedChannel.Add(i + 1);
                        //    }

                        //}
                        //int k = 0;
                        //foreach (OxyPlot.Series.LineSeries ls in md.Series)
                        //{
                        //    ls.Smooth = false;
                        //    ls.LineJoin = LineJoin.Round;
                        //    ls.StrokeThickness = 1;
                        //    int channelNumber = usedChannel[k] - 1;
                        //    ls.Title = "通道" + usedChannel[k].ToString();
                        //    for (int j = 0; j < number.GetLength(1) / 1; j = j + 50)
                        //    {
                        //        ls.Points.Add(new DataPoint(j, number[channelNumber, j]));

                        //    }
                        //    k++;
                        //}

                        self.ReportProgress(0, null);
                        Thread.Sleep(200);

                    }
                    Thread.Sleep(10);
                }
                args.Cancel = true;
            };

            bgw.ProgressChanged += delegate(object s, ProgressChangedEventArgs args)
            {
                if (true)
                {
                    //var md = args.UserState as PlotModel;
                    //opMainPlot.DisconnectCanvasWhileUpdating = true;
                    //foreach (OxyPlot.Series.LineSeries series in opMainPlot.Model.Series)
                    //{
                    //    series.Points.Clear();
                    //}
                    //opMainPlot.Model.Series.Clear();
                    //this.opMainPlot.Model = md;
                    //GC.Collect();
                    this.OnUSBStatusChanged("数据处理结束", null);
                    foreach(var uc in AnchorablePlots)
                    {
                        Rerender(uc, RenderMode.Reset);
                    }



                }



            };

            OnUSBStatusChanged += delegate(object sender, EventArgs e)
            {
                TbBottomIndicator1.Text=((string)sender);
                ;
            };

            OnRenderStatusChanged += delegate(object sender, EventArgs e)
            {
                TbBottomIndicator2.Text = ((string)sender);
                ;
            };

            USB.Initialize();
            this.DataContext = new MainViewModel(this);

            AnchorablePlots = new List<UserControl1>();

            


            InitializeComponent();

            AddAnchorablePlot();
            var uc1 = AddAnchorablePlot();
            uc1.sourceIndex = 1;

            NCalc.Expression.CacheEnabled = true;
            ribMenu.ApplicationMenu = new RibbonApplicationMenu();
            ribMenu.ApplicationMenu.Visibility = Visibility.Collapsed;
            wLogging = new Logging();
            nFailCount = 0;
            _context = SynchronizationContext.Current;
            THardwareRoutine = new System.Timers.Timer();
            THardwareRoutine.Interval = 1;

            THardwareRoutine.Elapsed += new ElapsedEventHandler(TEHHardwareRoutine);
            
            ;
            //ChannelTemplate.FindName("LbChannelName");
            ChannelTemplate.Visibility = Visibility.Collapsed;


            #region USBChannel Initialize
            USBChannels = new List<USBChannel>();
            for(int i =0;i<4;i++)
            {

                USBChannels.Add(new USBChannel(i));
                        
            }
            sbCH1.DataContext = USBChannels[0];
            sbCH2.DataContext = USBChannels[1];
            sbCH3.DataContext = USBChannels[2];
            sbCH4.DataContext = USBChannels[3];

            List<System.Drawing.Color> USBChannelColorList = new List<Color>() { Color.PaleVioletRed, Color.LightGreen, Color.Orange, Color.FromArgb(0xff, 0x66, 0xcc, 0xff) };
            int j = -1;
            ChannelControls = new List<RibbonSplitButton>() { sbCH1, sbCH2, sbCH3,sbCH4};
            foreach (RibbonSplitButton rsb in ChannelControls)
            {
                j++;
                ((USBChannel)rsb.DataContext).channelColor = USBChannelColorList[j];
                System.Drawing.Color color = ((USBChannel)rsb.DataContext).channelColor;
                ((ColorPicker)rsb.Items[1]).SelectedColor = System.Windows.Media.Color.FromArgb(color.A,color.R,color.G,color.B);
            }
            #endregion

            Channels = new List<VirtualChannel>();
            Channels.Add(new VirtualChannel("CH0", ChannelTemplate, "channel0", DPMain));
            Channels.Add(new VirtualChannel("CH1", ChannelTemplate, "channel1", DPMain));
            Channels.Add(new VirtualChannel("CH2", ChannelTemplate, "channel2", DPMain));
            Channels.Add(new VirtualChannel("CH3", ChannelTemplate, "channel3", DPMain));
            Channels.Add(new VirtualChannel("CH3-CH2", ChannelTemplate, "channel4", DPMain));

            dataAcquireThread = new Thread(new ThreadStart(DataAcquireA));

            dataSaveThread = new Thread(new ThreadStart(DataSave));
            //dataThreadQueue.Enqueue(DataAcquireOrder.RUN);
            dataAcquireThread.Start();
            dataSaveThread.Start();

            ;




        }

        private void ribMenu_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private void btnShowLog_Click(object sender, RoutedEventArgs e)
        {
            if (wLogging.Visibility != Visibility.Visible) wLogging.Show();

        }

        private void RibbonWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            THardwareRoutine.Enabled = false;
            dataAcquireThread.Abort();
            dataSaveThread.Abort();
            USB.DeInitialize();
            wLogging.bReadyToClose = true;
            wLogging.Close();
            bgw.CancelAsync();
            if (OscGraphWindow != null)
            {
                OscGraphWindow.toClose = true;
                OscGraphWindow.Close();
            }

        }



        public void TEHHardwareRoutine(object sender, ElapsedEventArgs e)
        {
            var _stopwatch = new Stopwatch();
            _stopwatch.Start();//pause timer, no need for a second instance.
            ((System.Timers.Timer)sender).Enabled = false;
            nDeviceCount = USB.getUSBDeviceCount();
            _context.Send(status => TbBottomIndicator1.Text = "已找到设备数量：" + nDeviceCount.ToString(), null);

            if (nDeviceCount > 0)
            {
                nFailCount = 0;
                var result = 0;//USB.getData(0);
                _context.Post(status => TbBottomIndicator2.Text = "Loop time0:" + _stopwatch.ElapsedMilliseconds.ToString() + "ms", null);






            }
            else
            {
                nFailCount++;
                _context.Post(status => TbBottomIndicator2.Text = "", null);
                for (int i = 0; i < Channels.Count; i++)
                {
                    _context.Send(status => Channels[i].UIElement.Background = System.Windows.Media.Brushes.Red, null);
                }
            }
            _stopwatch.Stop();
            _context.Post(status => TbBottomIndicator2.Text += "Loop time:" + (_stopwatch.ElapsedTicks * 1e3 / Stopwatch.Frequency).ToString() + "ms", null);
            ((System.Timers.Timer)sender).Enabled = true;//Resume timer

        }

        private void btnChannelConfig_Click(object sender, RoutedEventArgs e)
        {
            var me = (Button)sender;
            ((Label)((Grid)me.Parent).FindName("LbChannelName")).Content = DateAndTime.Now.ToString();
        }

        private void btnStop1_Click(object sender, RoutedEventArgs e)
        {
            TEHHardwareRoutine(null, null);
        }

        public void btnHardwareStatus_Click(object sender, RoutedEventArgs e)
        {

            IList<String> newVariantName = new List<String>();
            PhysicalChannel phychannel = (PhysicalChannel)((RibbonButton)sender).Resources["PhysicalChannel"];
            newVariantName.Add(phychannel.VariantName);
            var win = new HardwareConfig(ref newVariantName, phychannel.nDevice, phychannel.nChannel);
            win.ShowDialog();
            if (newVariantName[0] != "") phychannel.VariantName = newVariantName[0];

            return;
        }

        private void RibbonWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var s = new FileSvgReader(new WpfDrawingSettings());
            s.Read("open-iconic/svg/x_red.svg");
            ImageSet = new Dictionary<String, DrawingImage>();
            ImageSet.Add("HardwareInvalid", new DrawingImage(s.Drawing.Children.First()));
            s.Read("open-iconic/svg/reload_green.svg");
            ImageSet.Add("HardwareRunning", new DrawingImage(s.Drawing.Children.First()));



            SwGlobal = new Stopwatch();
            SwGlobal.Start();
            
            //opMainPlot.Controller.BindKeyDown(OxyKey.R,OxyModifierKeys.Control,PlotCommands.ZoomIn


            bgw.RunWorkerAsync();
            //OscGraphWindow = new OscGraph(fftNumberQueue);
            //OscGraphWindow.Show();
            THardwareRoutine.Enabled = false;
        }

        private void btnHardware_Click(object sender, RoutedEventArgs e)
        {
            (new HardwareConnect()).ShowDialog();
        }

        private void btnStart_Click(object sender, RoutedEventArgs e)
        {
            dataThreadQueue.Enqueue(DataAcquireOrder.INITIAL);
        }

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            DataAcquireOrder dao;
            dataThreadQueue.TryDequeue(out dao);
            dataThreadQueue.Enqueue(DataAcquireOrder.STOP);
            filename = DateTime.Now.ToString("yyyy_MM_dd#HH_mm_ss_ff") + ".dat";
        }

        public void DataAcquire()
        {
            int buffLength = USB.BUFF_LENGTH;
            DataAcquireOrder dao = new DataAcquireOrder();
            byte[] rawData = new byte[buffLength];
            CyUSBDevice device;// = USB.USBDevices[0] as CyUSBDevice;
            CyBulkEndPoint endpt = null;
            bool success = false;
            while (true)
            {
                success = dataThreadQueue.TryDequeue(out dao);
                if (success && dao == DataAcquireOrder.INITIAL)
                {

                    dataThreadQueue.Enqueue(DataAcquireOrder.RUN);
                    device = USB.USBDevices[0] as CyUSBDevice;
                    endpt = device.BulkInEndPt;
                    rawData = new byte[buffLength];
                    //endpt.BeginDataXfer()
                }
                else if (success && dao == DataAcquireOrder.RUN)
                {
                    rawData = new byte[buffLength];
                    USB.getData(0, ref rawData);
                    dataQueue.Enqueue(rawData);
                    dataThreadQueue.Enqueue(DataAcquireOrder.RUN);
                }
                else
                {
                    while (!dataThreadQueue.IsEmpty) dataThreadQueue.TryDequeue(out dao); //clean the queue
                    Thread.Sleep(5);
                }
            }
        }

        public void DataAcquireF()
        {
            int buffLength = USB.BUFF_LENGTH * 8;
            DataAcquireOrder dao = new DataAcquireOrder();
            byte[] rawData = new byte[buffLength];
            bool success = false;
            while (true)
            {
                success = dataThreadQueue.TryDequeue(out dao);
                if (success && dao == DataAcquireOrder.INITIAL)
                {

                    dataThreadQueue.Enqueue(DataAcquireOrder.RUN);
                }
                else if (success && dao == DataAcquireOrder.RUN)
                {
                    this.OnUSBStatusChanged("文件读取中...", null);
                    rawData = new byte[buffLength];
                    FileStream fs = new FileStream("2015_07_21#19_44_19_20.dat", FileMode.Open);
                    var r = new Random();
                    //fs.Position = (long)Math.Round(r.NextDouble() * 10.0 * 1024.0 * 1024.0);
                    fs.Read(rawData, 0, buffLength);
                    fs.Close();

                    dataQueue.Enqueue(rawData);
                    dataThreadQueue.Enqueue(DataAcquireOrder.RUN);
                    Thread.Sleep(1000);
                }
                else
                {
                    while (!dataThreadQueue.IsEmpty) dataThreadQueue.TryDequeue(out dao); //clean the queue
                    Thread.Sleep(5);
                }
            }
        }

        public unsafe void DataAcquireA()
        {
            DataAcquireOrder dao = new DataAcquireOrder();
            UInt16 nQueueSize = 4;
            bool success = false;
            bool hasInitialized = false;
            byte[][] rawData = new byte[nQueueSize][];
            byte[][] cmdBuff = new byte[nQueueSize][];
            byte[][] ovBuff = new byte[nQueueSize][];
            int buffLength = USB.BUFF_LENGTH;
            CyUSBDevice device = null;
            CyBulkEndPoint endpt = null;
            bool result = false;
            // All these dirty codes are used to get a managed array with a pinned memory space.
            // The official driver needs pointer like objects but asks for managed objects.
            // Special cares must be taken to avoid those memories are modified by GC.
            // In fact, IntPtr is perfect for the job, while the driver accepts byte[] only. 
            GCHandle[] rawDataHandle = null, cmdBuffHandle = null, ovBuffHandle = null;
            GCHandle cmdBufferHandle = new GCHandle();
            GCHandle xFerBufferHandle = new GCHandle();
            GCHandle overlapDataHandle = new GCHandle();

            while (true)
            {

                success = dataThreadQueue.TryDequeue(out dao);
                if (success && dao == DataAcquireOrder.INITIAL)
                {
                    device = USB.USBDevices[0] as CyUSBDevice;
                    #region Initialize USB device
                    CyControlEndPoint ctlendpt = device.ControlEndPt;
                    ctlendpt.Target = CyConst.TGT_INTFC;
                    ctlendpt.ReqType = CyConst.REQ_CLASS;
                    ctlendpt.Value = 0;
                    ctlendpt.Index = 0;
                    ctlendpt.Direction = 0;
                    ctlendpt.ReqCode = 1;
                    byte[] code={0,0,0};
                    int ctllen = 1;
                    bool bInitialResult=ctlendpt.Write(ref code, ref ctllen);
                    //ctlendpt.

                    #endregion
                    endpt = device.BulkInEndPt;
                    endpt.Abort();
                    endpt.Reset();

                    cmdBufferHandle = GCHandle.Alloc(cmdBuff[0], GCHandleType.Pinned);
                    xFerBufferHandle = GCHandle.Alloc(rawData[0], GCHandleType.Pinned);
                    overlapDataHandle = GCHandle.Alloc(ovBuff[0], GCHandleType.Pinned);

                    rawDataHandle = new GCHandle[nQueueSize];
                    cmdBuffHandle = new GCHandle[nQueueSize];
                    ovBuffHandle = new GCHandle[nQueueSize];

                    for (int i = 0; i < nQueueSize; i++)
                    {
                        

                        //Begin data transfer
                        rawData[i] = new Byte[buffLength];
                        cmdBuff[i] = new Byte[CyConst.SINGLE_XFER_LEN + ((endpt.XferMode == XMODE.BUFFERED) ? buffLength : 0)];
                        int ovSize = Math.Max(CyConst.OverlapSignalAllocSize, sizeof(CyUSB.OVERLAPPED));
                        ovBuff[i] = new Byte[ovSize];

                        rawDataHandle[i] = GCHandle.Alloc(rawData[i], GCHandleType.Pinned);
                        cmdBuffHandle[i] = GCHandle.Alloc(cmdBuff[i], GCHandleType.Pinned);
                        ovBuffHandle[i] = GCHandle.Alloc(ovBuff[i], GCHandleType.Pinned);

                        unsafe
                        {
                            CyUSB.OVERLAPPED ovLapStatus = new CyUSB.OVERLAPPED();
                            ovLapStatus = (CyUSB.OVERLAPPED)Marshal.PtrToStructure(ovBuffHandle[i].AddrOfPinnedObject(), typeof(CyUSB.OVERLAPPED));
                            ovLapStatus.hEvent = (IntPtr)PInvoke.CreateEvent(0, 0, 0, 0);
                            Marshal.StructureToPtr(ovLapStatus, ovBuffHandle[i].AddrOfPinnedObject(), true);

                            // Pre-load the queue with a request
                            int len = buffLength;
                            endpt.BeginDataXfer(ref cmdBuff[i], ref rawData[i], ref len, ref ovBuff[i]);
                        }
                    }
                    dataThreadQueue.Enqueue(DataAcquireOrder.RUN);
                    hasInitialized = true;

                }
                else if (success && dao == DataAcquireOrder.RUN)
                {
                    for (int i = 0; i < nQueueSize; i++)
                    {
                        bool bWait = false;
                        unsafe
                        {
                            CyUSB.OVERLAPPED ovLapStatus = new CyUSB.OVERLAPPED();
                            ovLapStatus = (CyUSB.OVERLAPPED)Marshal.PtrToStructure(ovBuffHandle[i].AddrOfPinnedObject(), typeof(CyUSB.OVERLAPPED));
                            bWait = endpt.WaitForXfer(ovLapStatus.hEvent, 1000);
                            if (!bWait)
                            {
                                endpt.Abort();
                                PInvoke.WaitForSingleObject(ovLapStatus.hEvent, 1000);
                            }
                        }
                        int len = buffLength;
                        if (bWait && endpt.FinishDataXfer(ref cmdBuff[i], ref rawData[i], ref len, ref ovBuff[i]))
                        {
                            byte[] dataBuff = (byte[])rawData[i].Clone();
                            dataQueue.Enqueue(dataBuff);
                        }
                        len = buffLength;
                        endpt.BeginDataXfer(ref cmdBuff[i], ref rawData[i], ref len, ref ovBuff[i]);


                    }
                    success = dataThreadQueue.TryDequeue(out dao);
                    if (!success) dataThreadQueue.Enqueue(DataAcquireOrder.RUN);
                    else dataThreadQueue.Enqueue(DataAcquireOrder.STOP);
                    
                    Thread.Sleep(1);
                }
                else if (success && dao == DataAcquireOrder.STOP)
                {
                    if (endpt != null) endpt.Abort();
                    while (dataThreadQueue.Count > 0) dataThreadQueue.TryDequeue(out dao);
                    if (hasInitialized)
                    {
                        hasInitialized = false;
                        for (int i = 0; i < nQueueSize; i++)
                        {
                            unsafe
                            {
                                OVERLAPPED ov = new OVERLAPPED();
                                ov = (OVERLAPPED)Marshal.PtrToStructure(ovBuffHandle[i].AddrOfPinnedObject(), typeof(OVERLAPPED));
                                PInvoke.CloseHandle(ov.hEvent);
                            }

                            rawDataHandle[i].Free();
                            ovBuffHandle[i].Free();
                            cmdBuffHandle[i].Free();

                            rawData[i] = null;
                            cmdBuff[i] = null;
                            ovBuff[i] = null;
                        }
                        cmdBufferHandle.Free();
                        xFerBufferHandle.Free();
                        overlapDataHandle.Free();
                    }
                }
                else
                {
                    Thread.Sleep(1);
                }


            }
        }
        public void DataSave()
        {
            byte[] rawData;
            byte[] rawDataBuff=new byte[(int)globalConfig["DisplayLength"]];
            Int64 storedData = 0;
            bool success = false;
            filename = DateTime.Now.ToString("yyyy_MM_dd#HH_mm_ss_ff") + ".dat";
            Int32[,] number;
            List<Tuple<int, int>> pack = new List<Tuple<int, int>>();
            while (true)
            {

                success = dataQueue.TryDequeue(out rawData);
                if (success)
                {
                    int displayLength = (int)globalConfig["DisplayLength"]>0?(int)globalConfig["DisplayLength"]*1024*1024:rawData.Length;
                    if (rawDataBuff.Length != displayLength) //Ensure no buff overflow;
                    {
                        rawDataBuff = new byte[displayLength];
                        storedData = 0;
                    }
                    Array.Copy(rawData, 0, rawDataBuff, storedData, Math.Min(rawDataBuff.Length,rawData.Length)); //Buff data
                    storedData += rawData.Length; // Record length
                    if (storedData>=displayLength) //Process when stored enough data;
                    {
                        storedData = 0;
                        Array.Resize(ref rawDataBuff, displayLength);
                        number = new Int32[4, displayLength / 16];
                        pack.Clear();
                        var saveCount = Unpack.Run(ref rawDataBuff, ref number, ref pack);
                        number = (Int32[,])ResizeArray(number, new Int32[] { 4, saveCount - 1 });

                        if (unpackedNumberQueue.Count < 1) unpackedNumberQueue.Enqueue(number);
                    }
                    
                    
                    
                    
                    
                    if ((bool)globalConfig["SaveData"] == true)
                    {
                        string filePath = savePath + filename;
                        if (!System.IO.Directory.Exists(savePath)) Directory.CreateDirectory(savePath);
                        var fileinfo = new System.IO.FileInfo(filePath);
                        if (fileinfo.Exists && fileinfo.Length >= (double)globalConfig["SaveLength"] * 1024 * 1024) filename = DateTime.Now.ToString("yyyy_MM_dd#HH_mm_ss_ff") + ".dat";
                        filePath = savePath + filename;
                        FileStream fs = new FileStream(filePath, FileMode.Append);
                        fs.Write(rawData, 0, rawData.Length);
                        fs.Close();

                    }
                    else
                    {
                        filename=DateTime.Now.ToString("yyyy_MM_dd#HH_mm_ss_ff") + ".dat";
                    }
                }
                else
                {
                    Thread.Sleep(3);
                }
            }
        }
        ~Main()
        {
            dataAcquireThread.Abort();
            dataSaveThread.Abort();
        }

        private void btnOsc_Click(object sender, RoutedEventArgs e)
        {
            //if (OscGraphWindow == null) OscGraphWindow = new OscGraph(unpackedNumberQueue);
            //OscGraphWindow.Show();
        }

        private void btnOpen_Click(object sender, RoutedEventArgs e)
        {
            dataThreadQueue.Enqueue(DataAcquireOrder.STOP);

            OpenFileDialog OFD = new OpenFileDialog();
            if (!(bool)OFD.ShowDialog()) return;
            this.OnRenderStatusChanged("文件读取中...", null);
            FileStream fs = OFD.OpenFile() as FileStream;

            byte[] buff = new byte[fs.Length];
            fs.Read(buff, 0, (int)fs.Length);
            dataQueue.Enqueue(buff);
        }

        public void MainPlotUpdated(object sender, EventArgs e)
        {
            PlotModel me = sender as PlotModel;
            OxyPlot.Axes.Axis XAxis = me.Axes[0];
            OxyPlot.Series.LineSeries LS0 = me.Series[0] as OxyPlot.Series.LineSeries;
            int points = (int)Math.Round((XAxis.ActualMaximum - XAxis.ActualMinimum) / (LS0.Points[LS0.Points.Count() - 1].X - LS0.Points[0].X) * LS0.Points.Count());
            int currentJump = (int)(XAxis.ActualMaximum - XAxis.ActualMinimum) / points;
            if (points < 40000 && currentJump > 1.1)
            {
                int jump = (int)Math.Floor((XAxis.ActualMaximum - XAxis.ActualMinimum) / 40000);
                if (jump < 1) jump = 1;
                me.Series.Clear();
                List<int> usedChannel = new List<int>();
                for (int i = 0; i < 4; i++)
                {
                    if ((bool)globalConfig["showChannel" + (i + 1).ToString()])
                    {
                        me.Series.Add(new OxyPlot.Series.LineSeries());
                        usedChannel.Add(i + 1);
                    }

                }
                int k = 0;
                foreach (OxyPlot.Series.LineSeries ls in me.Series)
                {
                    ls.Smooth = false;
                    ls.LineJoin = LineJoin.Round;
                    ls.StrokeThickness = 1;
                    int channelNumber = usedChannel[k] - 1;
                    for (int j = (int)XAxis.ActualMinimum; j < XAxis.ActualMaximum / 1; j = j + jump)
                    {
                        ls.Points.Add(new DataPoint(j, BufferedUnpackedValue[channelNumber, j]));

                    }
                    k++;
                }
                me.PlotView.InvalidatePlot(true);

            }
        }

        private void opMainPlot_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            Rerender((UserControl1)sender);


        }

        public void Rerender(OxyPlot.Wpf.PlotView pv,double XMin=double.NaN,double XMax=double.NaN)
        {
            if (BufferedCalculatedValue==null) return;
            var self = pv;
            PlotModel me = self.Model;
            me.IsLegendVisible = true;
            if (me.Equals(null)) return;
            OxyPlot.Axes.Axis XAxis = me.Axes[0];
            if (!double.IsNaN(XMin)) XAxis.Minimum = XMin;
            if (!double.IsNaN(XMax)) { XAxis.Maximum = XMax; self.InvalidatePlot(); }
            //OxyPlot.Series.LineSeries LS0 = me.Series[0] as OxyPlot.Series.LineSeries;
            //int points = (int)Math.Round((XAxis.ActualMaximum - XAxis.ActualMinimum) / (LS0.Points[LS0.Points.Count() - 1].X - LS0.Points[0].X) * LS0.Points.Count());
            //int currentJump = (int)(XAxis.ActualMaximum - XAxis.ActualMinimum) / points;

            int jump = (int)Math.Floor((XAxis.ActualMaximum - XAxis.ActualMinimum) *((double)globalConfig["SampleRate"]*1e6)/ 40000);
            if (jump < 1) jump = 1;
            me.Series.Clear();
            List<int> usedChannel = new List<int>();
            for (int i = 0; i < 4; i++)
            {
                if (((USBChannel)ChannelControls[i].DataContext).enable)
                {
                    me.Series.Add(new OxyPlot.Series.LineSeries());
                    usedChannel.Add(i + 1);
                }

            }
            int k = 0;
            foreach (OxyPlot.Series.LineSeries ls in me.Series)
            {
                ls.Smooth = false;
                ls.LineJoin = LineJoin.Round;
                ls.StrokeThickness = 1;
                int channelNumber = usedChannel[k] - 1;
                ls.Title = "通道" + usedChannel[k].ToString();
                System.Drawing.Color color = ((USBChannel)ChannelControls[channelNumber].DataContext).channelColor;
                ls.Color = OxyPlot.OxyColor.FromArgb(color.A,color.R,color.G,color.B);
                for (int j = (int)Math.Max(0, XAxis.ActualMinimum * (double)globalConfig["SampleRate"] * 1e6); j < Math.Min(XAxis.ActualMaximum*(double)globalConfig["SampleRate"] * 1e6, BufferedCalculatedValue.GetLength(1)); j = j + jump)
                {
                    ls.Points.Add(new DataPoint(j/(double)globalConfig["SampleRate"]/1e6, BufferedCalculatedValue[channelNumber, j]));

                }
                k++;
            }
            me.PlotView.InvalidatePlot(true);
            
            return;
        }

        
        public void Rerender(UserControl1 uc, RenderMode renderMode=RenderMode.Hold,bool forceRedraw=true,double XMin = double.NaN, double XMax = double.NaN)
        {
            if (BufferedCalculatedValue == null) return;
            if (!forceRedraw)
            {
                if (!uc.needRefresh) { uc.needRefresh = true; return; }
                else { uc.needRefresh = false; }
            }
            var pv = uc.opMainPlot;
            var self = pv;
            PlotModel me = self.Model;
            me.IsLegendVisible = true;
            if (me.Equals(null)) return;
            if (me.Axes.Count<1) return;
            OxyPlot.Axes.Axis XAxis = me.Axes[0];
            
            me.Series.Clear();
            GC.Collect();
            List<int> usedChannel = new List<int>();
            for (int i = 0; i < 4; i++)
            {
                if (((USBChannel)ChannelControls[i].DataContext).enable && (bool)uc.enabledChannel[i])
                {
                    me.Series.Add(new OxyPlot.Series.LineSeries());
                    usedChannel.Add(i + 1);
                }

            }
            int k = 0;
            foreach (OxyPlot.Series.LineSeries ls in me.Series)
            {
                ls.Smooth = false;
                ls.LineJoin = LineJoin.Round;
                ls.StrokeThickness = 1;
                int channelNumber = usedChannel[k] - 1;
                ls.Title = "通道" + usedChannel[k].ToString();
                System.Drawing.Color color = ((USBChannel)ChannelControls[channelNumber].DataContext).channelColor;
                ls.Color = OxyPlot.OxyColor.FromArgb(color.A, color.R, color.G, color.B);
                
                if (uc.sourceIndex==0)
                {
                    if (renderMode == RenderMode.New)
                    {
                        if (!double.IsNaN(XMin)) XAxis.Minimum = XMin;
                        if (!double.IsNaN(XMax)) { XAxis.Maximum = XMax; }
                        //pv.InvalidatePlot();
                    }
                    else if (renderMode == RenderMode.Reset)
                    {
                        XAxis.Minimum = 0;
                        XAxis.Maximum = BufferedCalculatedValue.GetLength(1) / ((double)globalConfig["SampleRate"] * 1e6);
                        XAxis.CoerceActualMaxMin();
                        
                        //pv.InvalidatePlot();
                        
                    }
                    else if (renderMode == RenderMode.Hold)
                    {
                        XAxis.Minimum = XAxis.ActualMinimum;
                        XAxis.Maximum = XAxis.ActualMaximum;
                    }
                    
                    //OxyPlot.Series.LineSeries LS0 = me.Series[0] as OxyPlot.Series.LineSeries;
                    //int points = (int)Math.Round((XAxis.ActualMaximum - XAxis.ActualMinimum) / (LS0.Points[LS0.Points.Count() - 1].X - LS0.Points[0].X) * LS0.Points.Count());
                    //int currentJump = (int)(XAxis.ActualMaximum - XAxis.ActualMinimum) / points;

                    int jump = (int)Math.Floor((XAxis.Maximum - XAxis.Minimum) / (3 * pv.ActualWidth) * ((double)globalConfig["SampleRate"] * 1e6));
                    if (jump < 1) jump = 1;
                    

                    for (int j = (int)Math.Max(0, XAxis.Minimum * (double)globalConfig["SampleRate"] * 1e6); j>=0 && j < Math.Min(XAxis.Maximum * (double)globalConfig["SampleRate"] * 1e6, BufferedCalculatedValue.GetLength(1)); j = j + jump)
	                {
	                    ls.Points.Add(new DataPoint(j / ((double)globalConfig["SampleRate"] * 1e6), BufferedCalculatedValue[channelNumber, j]));
	
	                }
                } 
                else if(uc.sourceIndex==1)
                {
                    var number = BufferedFFTValue;
                    var sampleRate = (double)globalConfig["SampleRate"] * 1e6;
                    if (renderMode==RenderMode.Reset)
                    {
                        XAxis.Minimum = 0;
                        XAxis.Maximum = 220e3;
                        XAxis.CoerceActualMaxMin();
                        XAxis.Reset();
                    }
                    else if (renderMode == RenderMode.New)
                    {
                        if (!double.IsNaN(XMin)) XAxis.Minimum = XMin;
                        if (!double.IsNaN(XMax)) { XAxis.Maximum = XMax; }
                        
                    }
                    else if (renderMode == RenderMode.Hold)
                    {
                        XAxis.Minimum = XAxis.ActualMinimum;
                        XAxis.Maximum = XAxis.ActualMaximum;
                    }
                    int actualPoints = (int)((XAxis.Maximum - XAxis.Minimum) / (1 / (number.GetLength(1) * 2 / sampleRate)));
                    int expectedPoints = (int)(3 * pv.ActualWidth);
                    int factor = actualPoints /expectedPoints;
                    if (factor < 1) factor = 1;
                    
                    
                    for (int j = (int)(Math.Max(10,XAxis.Minimum)/(1 / (number.GetLength(1) * 2 / sampleRate))); j < number.GetLength(1) / 1 && (j * (1 / (number.GetLength(1) * 2 / sampleRate))) < Math.Min(500e3,XAxis.Maximum); j = j + factor)
                    {
                        double temp = 0;
                        for (int l = 0; l < factor; l++)
                        {
                            temp += number[channelNumber, j+l];
                        }
                        temp /= factor;
                        ls.Points.Add(new DataPoint((double)j * (1 / (number.GetLength(1) * 2 / sampleRate)), temp));

                    }
                    
                }
                k++;
            }
            uc.holding = true;
            me.PlotView.InvalidatePlot(true);
            if (renderMode==RenderMode.Reset) me.ResetAllAxes();
            uc.holding = false;

            return;
        }

        private void btnHelp_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("平移：鼠标右键/Alt+鼠标左键");
            MessageBox.Show("缩放：滚轮/PgUp/Down\r\n精细缩放：Ctrl+缩放");
            MessageBox.Show("区域放大：Ctrl+Alt+鼠标左键");
            MessageBox.Show("显示全部：A");
            MessageBox.Show("重绘：鼠标左键双击");


        }

        private void btnSavePath_Click(object sender, RoutedEventArgs e)
        {
            FolderBrowserDialog FBD = new FolderBrowserDialog();
            FBD.SelectedPath = savePath;
            FBD.ShowNewFolderButton = true;
            FBD.RootFolder = Environment.SpecialFolder.Desktop;
            FBD.Description = "选择数据文件保存路径";
            
            if (!(FBD.ShowDialog()==System.Windows.Forms.DialogResult.OK)) return;
            savePath = FBD.SelectedPath;
            if (savePath.Last() != '\\') savePath += "\\";
        }

        private static Array ResizeArray(Array arr, int[] newSizes)
        {
            if (newSizes.Length != arr.Rank)
                throw new ArgumentException("arr must have the same number of dimensions " +
                                            "as there are elements in newSizes", "newSizes");

            var temp = Array.CreateInstance(arr.GetType().GetElementType(), newSizes);
            int length = temp.GetLength(temp.Rank-1);
            int srclength = arr.GetLength(temp.Rank - 1);
            int dstlength = length;
            int[] totalElement = new int[temp.Rank-1];
            totalElement[0] = temp.GetLength(temp.Rank - 2);
            for (int i=1;i<temp.Rank-1;i++)
            {
                
                totalElement[i]=totalElement[i-1]*temp.GetLength(temp.Rank-2-i);
            }
            int[] iter = new int[temp.Rank - 1];
            
            for (int i = 0; i < totalElement[temp.Rank-2];i++ )
            {
                int srcOffset = 0;
                int dstOffset = 0;
                for (int j = 0; j < temp.Rank - 1;j++ )
                {
                    iter[j] = i % totalElement[j];
                    srcOffset += iter[j] * arr.GetLength(arr.Rank - 1 - j);
                    dstOffset += iter[j] * temp.GetLength(arr.Rank - 1 - j);
                    
                }
                Array.ConstrainedCopy(arr, srcOffset, temp, dstOffset, Math.Min(srclength,dstlength));
            }
            
            return temp;
        }

        private void ChannelControl_CheckChanged(object sender, RoutedEventArgs e)
        {
            RibbonSplitButton rsb = (RibbonSplitButton)((RibbonSplitMenuItem)sender).Parent;
            var channel = (USBChannel) rsb.DataContext;
            rsb.LargeImageSource = channel.CreateStateIcon();
            rsb.SmallImageSource = rsb.LargeImageSource;
            foreach (var uc in AnchorablePlots)
            {Rerender(uc);}
        }

        private void ChannelControl_Click(object sender, RoutedEventArgs e)
        {
            if (sender==e.OriginalSource)
            {
                RibbonSplitButton self = (RibbonSplitButton)sender;
                RibbonSplitMenuItem rsmi = (RibbonSplitMenuItem)self.Items[0];
                rsmi.IsChecked = !rsmi.IsChecked;
                rsmi.RaiseEvent(new RoutedEventArgs(RibbonCheckBox.CheckedEvent));
            }
        }

        private void ChannelControlColor_Click(object sender, RoutedEventArgs e)
        {

        }

        private void ColorPicker_SelectedColorChanged(object sender, RoutedPropertyChangedEventArgs<System.Windows.Media.Color?> e)
        {
            var self = (Xceed.Wpf.Toolkit.ColorPicker)sender;
            var rsb = (RibbonSplitButton)((Xceed.Wpf.Toolkit.ColorPicker)sender).Parent;
            var chn = (USBChannel)rsb.DataContext;
            chn.channelColor = System.Drawing.Color.FromArgb(self.SelectedColor.Value.A,
                self.SelectedColor.Value.R,
                self.SelectedColor.Value.G,
                self.SelectedColor.Value.B);
            rsb.LargeImageSource = chn.CreateStateIcon();
            rsb.SmallImageSource = rsb.LargeImageSource;
            foreach(var uc in AnchorablePlots)
            {

                Rerender(uc);
            }
        }
        public void TryFilter()
        {
            int len = BufferedUnpackedValue.GetLength(1);
            int[] temp = new int[len];
            for (int i = 0; i < temp.Length;i++ )
            {
                temp[i] = BufferedUnpackedValue[2, i];
            }
            MWNumericArray temp3 = new MWNumericArray((Array)temp);
            MWArray temp2 = matlab.lpfilter((MWArray)temp3,(MWArray)lpfilter1) ;
            double[,] temp4 = temp2.ToArray() as double[,];
            var _d = temp2.Dimensions;
            for (int i = 0; i < temp.Length; i++)
            {
                BufferedUnpackedValue[2, i] = (int)temp4[0,i];
            }
        }



        private void btnHelp_MouseDoubleClick(object sender, RoutedEventArgs e)
        {
            //lpfilter1 = matlab.createfilter((MWArray)50, (MWArray)Convert.ToDouble(rtbLowPassFrequency.Text));
            TryFilter();
        }

        private void btnSaveAs_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog sfd = new SaveFileDialog();
            sfd.AddExtension = true;
            sfd.DefaultExt = ".csv";
            bool? success = sfd.ShowDialog();
            if (success == true && BufferedUnpackedValue != null)
            {
                FileStream fs = sfd.OpenFile() as FileStream;
                string buff="";
                byte[] byteStream = null;
                for (int i = 0; i < BufferedUnpackedValue.GetLength(1); i++)
                {
                    for (int j = 0; j < BufferedUnpackedValue.GetLength(0); j++)
                    {
                        buff = buff + BufferedUnpackedValue[j, i].ToString("d");
                        if (j == BufferedUnpackedValue.GetLength(0) - 1)
                            buff = buff + "\r\n";
                        else
                            buff = buff + ",";

                        if (i%50==0)
                        {
                            byteStream = System.Text.Encoding.ASCII.GetBytes(buff);
                            fs.Write(byteStream, 0, byteStream.Length);
                            buff = "";
                        }

                    }
                }
                byteStream = System.Text.Encoding.ASCII.GetBytes(buff);
                fs.Write(byteStream, 0, byteStream.Length);
                fs.Close();
                
                
            }
        }

        private void btnShowOsc_Click(object sender, RoutedEventArgs e)
        {
            //if (OscGraphWindow != null)
            //{
            //    OscGraphWindow.Show();
            //}
        }

        private void rbSampleRate_Click(object sender, RoutedEventArgs e)
        {
            var input = new InputBox(
                "当前值："+globalConfig["SampleRate"].ToString()+"MHz",//Message
                "采样速率",//Title
                "请输入新的采样速率（MHz）：");//Field(Indicator)
            bool confirmed = input.Show();
            if (confirmed)
            {
                try
                {
                    globalConfig["SampleRate"] = Convert.ToDouble(input.Fields[0].Value);
                    ((RibbonButton)sender).Label = globalConfig["SampleRate"].ToString() + " MHz";
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show("修改失败，请确认输入的是有效的数字。");
                    
                }
                
                
            }
            
        }

        private void StatusBar_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            return;
        }

        public void AnchorablePlot_ConfigChanged(object sender, EventArgs e)
        {
            Rerender((UserControl1)sender);
        }
        public void AnchorablePlot_Hiding(object sender, CancelEventArgs e)
        {
            //AnchorablePlots.Remove((UserControl1)sender);
            e.Cancel = true;
            ((UserControl1)sender).Close();

        }

        public void AnchorablePlot_Closed(object sender, EventArgs e)
        {
            AnchorablePlots.Remove((UserControl1)sender);
            
        }



        private void btnAddPlot_Click(object sender, RoutedEventArgs e)
        {
            AddAnchorablePlot();
        }
        public UserControl1 AddAnchorablePlot()
        {
            var uc1 = new UserControl1();
            uc1.ConfigChanged += AnchorablePlot_ConfigChanged;
            uc1.Hiding += AnchorablePlot_Hiding;
            uc1.Closed += AnchorablePlot_Closed;
            uc1.ViewChanged += delegate(object sender, EventArgs e)
            {
                Rerender((UserControl1)sender, RenderMode.Hold,false);
                ;
            };
            uc1.opMainPlot.Model.Updating += delegate(object sender, EventArgs e)
            {
                this.OnRenderStatusChanged("图表更新中，请稍等...", null);
            };
            uc1.opMainPlot.Model.Updated += delegate(object sender, EventArgs e)
            {
                if (!uc1.holding) Rerender(uc1, RenderMode.Hold);
                this.OnRenderStatusChanged("图表更新结束", null);
            };
            uc1.ViewReset += delegate(object sender, EventArgs e)
            {
                Rerender((UserControl1)sender, RenderMode.Reset);
            };
            var uc2 = new LayoutAnchorablePane(uc1);
            layout.Children.Add(uc2);
            AnchorablePlots.Add(uc1);
            //uc1.opMainPlot.InvalidatePlot();
            //uc1.opMainPlot.Model.Axes[0].AxisChanged += delegate(object sender, AxisChangedEventArgs e)
            //{
            //    Rerender(uc1, RenderMode.Hold);
            //};
            return uc1;
        }

        private void rbSaveLength_Click(object sender, RoutedEventArgs e)
        {
            var input = new InputBox(
    "当前值：" + globalConfig["SaveLength"].ToString() + "MB",//Message
    "储存长度",//Title
    "请输入新的储存长度（MB）：");//Field(Indicator)
            bool confirmed = input.Show();
            if (confirmed)
            {
                try
                {
                    globalConfig["SaveLength"] = Convert.ToDouble(input.Fields[0].Value);
                    ((RibbonButton)sender).Label = globalConfig["SaveLength"].ToString() + " MB";
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show("修改失败，请确认输入的是有效的数字。");

                }


            }
        }




    }
    public class VirtualChannel
    {
        public NCalc.Expression expression;
        public Dictionary<string, string> config;
        public Grid UIElement;
        public List<Tuple<long, double>> history;
        public Main context;
        public Dictionary<string, object> privateDictionary;
        public VirtualChannel()
        {
            config = new Dictionary<string, string>();
            history = new List<Tuple<long, double>>();
            privateDictionary = new Dictionary<string, object>();
            expression = new NCalc.Expression("1");
        }
        public VirtualChannel(String Expr, Grid Template, String ChannelName, DockPanel ParentContainer)
        {
            privateDictionary = new Dictionary<string, object>();
            UIElement = CreateUIElementFromTemplate(Template, ChannelName, ParentContainer);
            config = new Dictionary<string, string>();
            config.Add("name", ChannelName);
            expression = new NCalc.Expression(Expr);
            history = new List<Tuple<long, double>>();

        }
        public void RedrawUI(Grid Template, DockPanel ParentContainer)
        {
            if (null != UIElement)
            {
                ((Label)UIElement.FindName("LbChannelName")).Content = config["name"];

            }
            else
            {
                UIElement = CreateUIElementFromTemplate(Template, config["name"], ParentContainer);
            }
        }
        public Grid CreateUIElementFromTemplate(Grid Template, String ChannelName, DockPanel ParentContainer)
        {
            string gridString = XamlWriter.Save(Template);
            var newChannel = (Grid)XamlReader.Parse(gridString);
            ParentContainer.Children.Add(newChannel);
            newChannel.Visibility = Visibility.Visible;
            //newChannel.Name = ChannelName;
            ((Label)newChannel.FindName("LbChannelName")).Content = ChannelName;
            newChannel.Resources.Add("VirtualChannel", this);
            return newChannel;
        }
    }

    public class PhysicalChannel
    {
        public System.Windows.UIElement UIObject;
        public int nChannel;
        public int nDevice;
        public string VariantName;
        public Main context;
        public PhysicalChannel(int _nChannel, int _nDevice, string _VariantName, System.Windows.UIElement _Template, System.Windows.UIElement _ParentContainer, Main _context)
        {
            nChannel = _nChannel;
            nDevice = _nDevice;
            VariantName = _VariantName;
            context = _context;
            UIObject = CreateUIObjectFromTemplate(_Template, _ParentContainer);

        }

        public System.Windows.UIElement CreateUIObjectFromTemplate(System.Windows.UIElement _Template, System.Windows.UIElement _ParentContainer)
        {
            string UIString = XamlWriter.Save(_Template);
            var newButton = (RibbonButton)XamlReader.Parse(UIString);
            ((RibbonGroup)_ParentContainer).Items.Add(newButton);
            newButton.Visibility = Visibility.Visible;
            newButton.Name = "rbHardwareD" + nDevice.ToString() + "CH" + nChannel.ToString();
            newButton.Label = "通道" + nChannel.ToString();
            newButton.LargeImageSource = Main.ImageSet["HardwareInvalid"];
            if (newButton.Resources != null) newButton.Resources = new ResourceDictionary();
            newButton.Resources.Add("PhysicalChannel", this);
            newButton.Click += context.btnHardwareStatus_Click;
            return newButton;
        }
    }

    public class USBChannel : INotifyPropertyChanged
    {
        public enum ChannelType {DISPLACEMENT,VELOCITY};
        public enum SpeedFilterType {NONE,LP1K,LP10K,LP200K};

        public event PropertyChangedEventHandler PropertyChanged;

        public ChannelType channelType;
        public SpeedFilterType speedFilterType;
        public double sampleRate;
        public double lpFrequency;//Low pass frequency
        public string lpFrequencyString
        {
            get { return lpFrequency.ToString(); }
            set { lpFrequency = Convert.ToDouble(value); }
        }
        public string hpFrequencyString
        {
            get { return hpFrequency.ToString(); }
            set { hpFrequency = Convert.ToDouble(value); }
        }
        public double hpFrequency;//High pass frequency
        public int filterOrder;
        public double normalizedLF
        {
            get { return lpFrequency / (sampleRate / 2); }
        }
        public double normalizedHF
        {
            get { return hpFrequency / (sampleRate / 2); }
        }
        public bool enable
        {
            get;
            set;
        }
        public bool IsVelocity
        {
            get { return channelType == ChannelType.VELOCITY; }
            set { if (value == true) { channelType = ChannelType.VELOCITY; OnPropertyChanged("IsDisplacement"); } }
        }
        public bool IsDisplacement
        {
            get { return channelType == ChannelType.DISPLACEMENT; }
            set { if (value == true) { channelType = ChannelType.DISPLACEMENT; OnPropertyChanged("IsVelocity"); } }
        }

        public System.Drawing.Color channelColor;
        int channelId;

        protected void OnPropertyChanged(string p)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(p));
            }
        }

        public USBChannel(int id)
        {
            channelId = id;
            channelType = ChannelType.DISPLACEMENT;
            speedFilterType = SpeedFilterType.NONE;
            channelColor = System.Drawing.Color.FromArgb(0x66,0xcc,0xff);
            enable = true;
            sampleRate = 2e6;
            lpFrequency = 1e3;
            //hpFrequnecy = double.Epsilon;
            filterOrder = 50;
            
        }
        public BitmapImage CreateStateIcon()
        {
            Bitmap bm = new Bitmap(128,128);
            Graphics _g = Graphics.FromImage(bm);
            _g.Clear(System.Drawing.Color.FromArgb(255, 255, 255, 255));
            if (enable)
            {
                _g.FillRectangle(new System.Drawing.SolidBrush(channelColor), 0, 0, 128, 128);
            }
            else
            {
                _g.DrawRectangle(new System.Drawing.Pen(channelColor, 20), 0, 0, 128, 128);
            }
            
            return CreateBIFromBitmap(bm);
               
        }
        public BitmapImage CreateBIFromBitmap(Bitmap bitmap)
        {
            MemoryStream ms = new MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            BitmapImage bi = new BitmapImage();
            bi.BeginInit();
            ms.Seek(0, SeekOrigin.Begin);
            bi.StreamSource = ms;
            bi.EndInit();
            return bi;
        }
        
    };

    public class MainViewModel
    {
        public MainViewModel(Main context)
        {
            this.context = context;
            this.saveData = false;
            this.Title = "";
            this.Points = new List<DataPoint>
                              {
                                  new DataPoint(0, 4),
                                  new DataPoint(10, 13),
                                  new DataPoint(20, 15),
                                  new DataPoint(30, 16),
                                  new DataPoint(40, 12),
                                  new DataPoint(50, 12)
                              };

        }

        public string Title { get; private set; }

        public IList<DataPoint> Points { get; private set; }

        public Main context { get; private set; }

        public bool saveData
        {
            get
            {
                return (bool)context.globalConfig["SaveData"];
            }
            set
            {
                context.globalConfig["SaveData"] = value;
            }
        }

        //public bool showChannel1
        //{
        //    get
        //    {
        //        return (bool)context.globalConfig["showChannel1"];
        //    }
        //    set
        //    {
        //        context.globalConfig["showChannel1"] = value;
        //    }
        //}
        //public bool showChannel2
        //{
        //    get
        //    {
        //        return (bool)context.globalConfig["showChannel2"];
        //    }
        //    set
        //    {
        //        context.globalConfig["showChannel2"] = value;
        //    }
        //}
        //public bool showChannel3
        //{
        //    get
        //    {
        //        return (bool)context.globalConfig["showChannel3"];
        //    }
        //    set
        //    {
        //        context.globalConfig["showChannel3"] = value;
        //    }
        //}
        //public bool showChannel4
        //{
        //    get
        //    {
        //        return (bool)context.globalConfig["showChannel4"];
        //    }
        //    set
        //    {
        //        context.globalConfig["showChannel4"] = value;
        //    }
        //}





    }


    public enum DataAcquireOrder { STOP, RUN, INITIAL };

    public class Unpack
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="data">raw data</param>
        /// <param name="number">unpacked data, stored in separate channel</param>
        /// <param name="pack">start offset and end offset for each packet</param>
        /// <returns></returns>
        public static Int32 Run(ref byte[] data, ref Int32[,] number, ref List<Tuple<Int32, Int32>> pack)
        {
            Int32 lastStartOffset = -1;
            Int32 saveCount = 0;
            bool begin = false;
            int chn = 0;
            UInt32 numberOffset = 0;
            for (int i = 0; i < data.Length - 100; i++)
            {
                if (data[i] == 0xAA)
                {
                    if (data[i + 1] == 0xAA && data[i + 2] == 0xAA && data[i + 3] == 0xAA
                        && data[i + 4] == 0xDD && data[i + 5] == 0xDD && data[i + 6] == 0xDD && data[i + 7] == 0xDD)
                    {
                        if (lastStartOffset < 0)
                        {
                            lastStartOffset = i;
                        }
                        else
                        {
                            pack.Add(new Tuple<int, int>(lastStartOffset, i - 1));
                            lastStartOffset = i;
                        }
                        byte[] lenbuff = new byte[4];
                        Array.Copy(data,i+8,lenbuff,0,4);
                        Array.Reverse(lenbuff);
                        int len = BitConverter.ToInt32(lenbuff, 0);
                        i = i + 31;
                        begin = true;
                        chn = 0;
                        continue;
                    }
                }

                if (begin)
                {
                    byte[] byteBuff = new byte[4];
                    for (int j = 0; j < 4; j++)
                    {
                        byteBuff[j] = data[i + j];
                    }
                    if (!BitConverter.IsLittleEndian) Array.Reverse(byteBuff);
                    number[chn, numberOffset] = BitConverter.ToInt32(byteBuff,0);
                    i = i + 3;
                    chn++;
                    if (chn == 4)
                    {
                        chn = 0;
                        numberOffset++;
                        saveCount++;
                    }
                }

            }
            return saveCount;
        }
    }
}
