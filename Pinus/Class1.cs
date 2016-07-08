using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CyUSB;

namespace Pinus
{
    class USB
    {
        public static USBDeviceList USBDevices;
        public static readonly int BUFF_LENGTH = 4*1024*1024;
        public static readonly int NSTATE = 1;
        public static readonly int FVALUE = 0;
        public static void Initialize()
        {
            USBDevices = new USBDeviceList(CyConst.DEVICES_CYUSB);
        }
        public static void DeInitialize()
        {
            if (USBDevices != null) USBDevices.Dispose();
        }
        public static int getUSBDeviceCount()
        {
            return USBDevices.Count;
        }
        public static Dictionary<int, int[]> enumUSBDevice()
        {
            Dictionary<int, int[]> deviceList = new Dictionary<int, int[]>();
            int i = 0;
            int[] k = { 1, 2 };
            foreach (USBDevice device in USBDevices)
            {

                deviceList.Add(i, new int[2] { device.VendorID, device.ProductID });
                i++;
            }
            return deviceList;
        }
        public static int[] findDevice(int VID, int PID)
        {
            int count = 0;
            int[] list;
            try
            {
                list = new int[USBDevices.Count + 1];
            }
            catch (Exception ex)
            {
                return new int[1];
            }
            int i = 1;
            foreach (USBDevice device in USBDevices)
            {

                if (device.ProductID == PID && device.VendorID == VID)
                {
                    count++;
                    list.Concat(new int[1]{i});
                    i++;
                }
            }
            list[0] = count;
            return list;
        }
        /// <summary>
        ///   读取归一化相位
        /// </summary>
        public static double[,] getNormalizedPhase(int id)
        {
            double[,] result = new double[2, 4];
            double[,] dataBuff = new double[4, 1024];
            CyUSBDevice device = USBDevices[id] as CyUSBDevice;
            if (device.BulkInEndPt != null)
            {
                byte[] buf = new byte[BUFF_LENGTH+512];
                int buffLength = BUFF_LENGTH+512;
                device.BulkInEndPt.XferData(ref buf, ref buffLength);
                //Check phase
                int p = 240;
                int offset = 0;
                while (((buf[p + offset] + 0x40u) & 0xc0u) != (buf[p + 12 + offset] & 0xc0u) & offset < 12)
                {
                    offset += 2;
                }
                for (int i = offset, j = 0, k; i < (buffLength-512+offset); i += 12, j++)
                {
                    k = j / 4;

                    byte high = (byte)(buf[i] & 0x0fu);
                    byte polar = (byte)(buf[i] & 0x10u);
                    byte dummy = (byte)(buf[i] & 0x20u);
                    byte addr = (byte)(buf[i] & 0xc0u);

                    int nChannel = addr >> 6;

                    if (dummy != 0)
                    {
                        dataBuff[nChannel, k] = 0;
                        result[NSTATE, nChannel] = 2;
                        
                    }
                    else
                    {
                        int nInteger = ((int)high << 24) + ((int)buf[i + 1] << 16) +
                                                ((int)buf[i + 2] << 8) + (int)buf[i + 3] - 0x8000000;

                        double fDecimal1 = (double)(((int)buf[i + 4] << 8) + (int)buf[i + 5]) /
                            (double)(((int)buf[i + 4] << 8) + (int)buf[i + 5] + ((int)buf[i + 6] << 8) + (int)buf[i + 7]);

                        double fDecimal2 = (double)(((int)buf[i + 8] << 8) + (int)buf[i + 9]) /
                            (double)(((int)buf[i + 8] << 8) + (int)buf[i + 9] + ((int)buf[i + 10] << 8) + (int)buf[i + 11]);

                        double fDecimal = 4 * (fDecimal1 + fDecimal2);
                        if (polar == 1)
                            fDecimal = -fDecimal;

                        int nWhole = (int)fDecimal;
                        double x = (double)(nInteger - nWhole) / 16;
                        if (x < 0)
                            x = (int)(x - 0.5);
                        else
                            x = (int)(x + 0.5);

                        dataBuff[nChannel, k] = 16 * x + fDecimal;
                        result[NSTATE, nChannel] = 1;
                    }
                }

                for (int nChannel = 0; nChannel < 4; nChannel++)
                {
                    if (result[NSTATE, nChannel] == 1)
                    {
                        double fSum = 0;
                        for (int i = 0; i < 1024; i++)
                            fSum += dataBuff[nChannel, i];

                        result[FVALUE, nChannel] = fSum / 1024;
                        
                    }
                }

            }
            for (int k = 0; k < 4; k++)
            {
                if (Double.IsNaN(result[FVALUE, k])) result[NSTATE, k] = 3;
            }
            return result;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public static byte[] getData(int index,ref byte[] buf)
        {
            CyUSBDevice device = USBDevices[index] as CyUSBDevice;
            
            if (device.BulkInEndPt != null)
            {
                
                int buffLength = BUFF_LENGTH;
                device.BulkInEndPt.XferData(ref buf, ref buffLength);
                return buf;
            }
            else
            {
                throw new Exception("no bulk-in endpoint found!");
            }
            //device.Dispose();
        }
        public static USBSTATE getUSBState()
        {
            if (USBDevices.Count < 1)
            {
                return USBSTATE.NONE;
            }
            else
            {
                var device = USBDevices[0] as CyUSBDevice;
                if (device.bSuperSpeed) return USBSTATE.SUPERSPEED;
                else return USBSTATE.HIGHSPEED;
            }
        }
    }
    enum USBSTATE {NONE,HIGHSPEED,SUPERSPEED};
}
