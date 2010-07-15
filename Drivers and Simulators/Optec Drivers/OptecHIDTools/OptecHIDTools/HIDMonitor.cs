﻿/* Class Use Instructions:
 * This class provides access to HID communication.
 * When using this class:
 * 1. Make sure the Windows Forms application sends the
 *    DeviceAttached and DeviceRemoved messages to this class.
 *    This class is incapable of receiveing the messages directly so 
 *    they must be passed in through the ProcessWindowsMessage method.
 *    Also be sure to handle the DeviceAttached and DeviceRemoved events 
 *    of this class to update the UI. Make sure to handle them quickly.
 * 2.
 * 
 * 
 * 
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.ComponentModel;


namespace OptecHIDTools
{
    [Guid("9E81350A-CCD5-47bd-8E08-FAB6103C4403")]
    public class HIDMonitor 
    {

        #region Fields
        private static TextWriterTraceListener LogFileListener;
        private static System.Guid HID_Guid = System.Guid.Empty;
        private static List<HID> _DetectedHIDs = new List<HID>();
        public static event EventHandler HIDAttached;
        public static event EventHandler HIDRemoved;
        #endregion     

        #region Properties

        public static List<HID> DetectedHIDs
        {
            get { return _DetectedHIDs; }
        }

        #endregion 

        #region methods

        static HIDMonitor()
        {
            try
            {
                //Get the GUID for the HID CLASS
                DetermineHID_GUID();
                //Get the list of all the attached devices
                BuildDeviceList(false);

                //Start the invisible form that receives the windows messages
                DeviceChangeNotifier.Start(HID_Guid);
                //Register for device change notifications
                DeviceChangeNotifier.RegisterForNotifications();

                // Setup a log file
                string logPath = @"C:\Program Files\Optec\Logs\HID\" +
                    DateTime.Now.Month.ToString().PadLeft(2, '0') +
                    DateTime.Now.Day.ToString().PadLeft(2, '0') +
                    DateTime.Now.Year.ToString() + ".txt";
                System.IO.DirectoryInfo dir = new System.IO.DirectoryInfo(@"C:\Program Files");
                dir.CreateSubdirectory(@"Optec\Logs\HID");
                
                if (!System.IO.File.Exists(logPath))
                {
                   LogFileListener = new TextWriterTraceListener(System.IO.File.CreateText(logPath));
                }
                else
                {
                    LogFileListener = new TextWriterTraceListener(logPath);
                }
                Trace.AutoFlush = true;
                //Debug.Listeners.Add(LogFileListener);
                Trace.Listeners.Add(LogFileListener);
                Trace.WriteLine("**************Optec HID Tools Log File Started at " + DateTime.Now.ToString());
                Trace.WriteLine("*******************************************************************");
                Trace.WriteLine("**************OptecHIDTools API IS IN USE**************************");
                
            }
            catch (Exception ex)
            {
                Trace.WriteLine("HID Monitor constructor threw: " + ex.ToString());
                throw ex;
            }
        }

        static void LogAnEvent(string Message, string Category)
        {
            Trace.WriteLine(Message, Category);
        }

        private static void DetermineHID_GUID()
        {
            try
            {
                HID_API_Wrapers.HidD_GetHidGuid(ref HID_Guid);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("Exception Thrown while getting HidGuid: " + ex.ToString());
                throw new ApplicationException("Error retrieving HID class GUID", ex);
            }
        }

        internal static void BuildDeviceList(bool TriggerEvents)
        {
            try
            {
                // Create an empty HID object to hold the device info
                HID NewDevice = new HID();
                // Obtain a pointer to a Device Information Set*****************************************
                // This points to an array containing all of the HID Devices
                // The pointer is returned in deviceInfoSet
                // Be sure to call SetupDiDestroyInfoList to free the resources when finished
                Int32 DIGCF_PRESENT = 2;
                Int32 DIGCF_DEVICEINTERFACE = 0x10;
                IntPtr deviceInfoSet;
                deviceInfoSet = Setup_API_Wrappers.SetupDiGetClassDevs(ref HID_Guid, IntPtr.Zero, IntPtr.Zero,
                    DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
                // Loop through the devices and get information about them. Then add them to the list.
                Int32 memberIndex = 0;
                while (true)
                {
                    // Obtain a pointer to a specific device interface
                    Setup_API_Wrappers.SP_DEVICE_INTERFACE_DATA MyDeviceInterfaceData =
                        new Setup_API_Wrappers.SP_DEVICE_INTERFACE_DATA();
                    Boolean Success = false;
                    MyDeviceInterfaceData.cbSize = Marshal.SizeOf(MyDeviceInterfaceData);

                    Success = Setup_API_Wrappers.SetupDiEnumDeviceInterfaces(
                        deviceInfoSet,
                        IntPtr.Zero,
                        ref HID_Guid,
                        memberIndex,
                        ref MyDeviceInterfaceData);
                    if (Success == false)
                    {
                        // This means there are no more valid devices so we are done
                        break;
                    }
                    else
                    {
                        // Increment so that we look at the next device next time around the looop
                        memberIndex++;
                    }
                    // Request a Structure with the Device Path Name
                    Int32 bufferSize = 0;
                    IntPtr detailDataBuffer;
                    Success = false;

                    Success = Setup_API_Wrappers.SetupDiGetDeviceInterfaceDetail
                        (deviceInfoSet,
                        ref MyDeviceInterfaceData,
                        IntPtr.Zero,
                        0,
                        ref bufferSize,
                        IntPtr.Zero);

                    detailDataBuffer = Marshal.AllocHGlobal(bufferSize);

                    Marshal.WriteInt32(detailDataBuffer, (IntPtr.Size == 4) ? (4 + Marshal.SystemDefaultCharSize) : 8);

                    Success = Setup_API_Wrappers.SetupDiGetDeviceInterfaceDetail
                        (deviceInfoSet,
                        ref MyDeviceInterfaceData,
                        detailDataBuffer,
                        bufferSize,
                        ref bufferSize,
                        IntPtr.Zero);
                    // Check for success
                    if (!Success)
                    {
                        Trace.WriteLine("An error occured while attempting to obtain the Device Interface Detail. " +
                            "This device will not be added to the list.");
                        continue;   // Goto next itteration of the loop
                    }
                    //Extract the device path name
                    String devicePathName = "";
                    IntPtr pDevicePathName = new IntPtr(detailDataBuffer.ToInt32() + 4);
                    devicePathName = Marshal.PtrToStringAuto(pDevicePathName);
                    NewDevice.Path = devicePathName;
                    Trace.WriteLine("Found Device with Path:" + devicePathName);
                    //Open a handle to the device
                    SafeFileHandle deviceHandle;
                    deviceHandle = ReadWrite_API_Wrappers.CreateFile(
                        devicePathName,
                        (ReadWrite_API_Wrappers.GENERIC_READ | ReadWrite_API_Wrappers.GENERIC_WRITE),
                        ReadWrite_API_Wrappers.FILE_SHARE_READ | ReadWrite_API_Wrappers.FILE_SHARE_WRITE,
                        IntPtr.Zero,
                        ReadWrite_API_Wrappers.OPEN_EXISTING,
                        ReadWrite_API_Wrappers.FILE_ATTRIBUTE_NORMAL | ReadWrite_API_Wrappers.FILE_FLAG_OVERLAPPED,
                        0);
                    // Get the device attributes (VID, PID, etc)
                    HID_API_Wrapers.HIDD_ATTRIBUTES DeviceAttributes = new HID_API_Wrapers.HIDD_ATTRIBUTES();
                    DeviceAttributes.Size = Marshal.SizeOf(DeviceAttributes);
                    Success = HID_API_Wrapers.HidD_GetAttributes(deviceHandle, ref DeviceAttributes);

                    // Get all of the devices capabilities if succeeded previously. 
                    // This requires two steps, getting a pointer to the caps, and then reading them.
                    if (Success)
                    {
                        // Get the PID and VID
                        NewDevice.PID_Hex = Convert.ToInt32(DeviceAttributes.ProductID).ToString("X");
                        NewDevice.VID_Hex = Convert.ToInt32(DeviceAttributes.VendorID).ToString("X");
                        // Check if the PID and VID are valid
                        if ((NewDevice.PID_Hex.Length != 4) || (NewDevice.VID_Hex.Length != 4))
                        {
                            // There was a problem finding the PID or VID
                            Trace.WriteLine("Error finding PID or VID for attached device. " +
                                "PID=[" + NewDevice.PID_Hex + "] VID=[" + NewDevice.VID_Hex + "]");
                            Trace.WriteLine("Device with path [" + NewDevice.Path
                                + "] will not be added to the attached device list");
                            continue;   // Go to the next itteration of the loop
                        }
                        // Obtain a pointer to the Device Capabilities...
                        IntPtr preparsedData = new IntPtr();
                        Success = false;
                        Success = HID_API_Wrapers.HidD_GetPreparsedData(deviceHandle, ref preparsedData);

                        // Now Obtain the capabilities

                        HID_API_Wrapers.HIDP_CAPS Capabilities = new HID_API_Wrapers.HIDP_CAPS();
                        Int32 result = 0;
                        result = HID_API_Wrapers.HidP_GetCaps(preparsedData, ref Capabilities);
                        NewDevice.OutputReportSize = Convert.ToInt32(Capabilities.OutputReportByteLength);
                        NewDevice.InputReportSize = Convert.ToInt32(Capabilities.InputReportByteLength);
                        NewDevice.FeatureReportSize = Convert.ToInt32(Capabilities.FeatureReportByteLength);

                        //Get the more device info...
                        int retries = 10;
                    TryAgain:
                        NewDevice.SerialNumber = GetSerialNumberString(deviceHandle);
                        NewDevice.Manufacturer = GetManufacturerString(deviceHandle);
                        NewDevice.ProductDescription = GetProductString(deviceHandle);

                        if ((NewDevice.SerialNumber == "Unknown") || (NewDevice.Manufacturer == "Unknown") ||
                            (NewDevice.ProductDescription == "Unknown"))
                        {
                            System.Threading.Thread.Sleep(50);
                            Trace.WriteLine("Failed to get Device Info Properly, Trying agian...");
                            if (retries == 0)
                            {
                                Trace.WriteLine("ERROR: Could not obtain SerialNumber, Manufacturer and Desctiption properly" +
                                    " after multiple attempts. Divice is not being added to list");
                                return;
                            }
                            retries--;
                            goto TryAgain;
                        }
                        // Mark the device at attached
                        NewDevice.DeviceIsAttached = true;
                        //Now check if the device is a new one or being reattached.
                        bool MatchFound = false;
                        // Add the device to the list
                        _DetectedHIDs.Add(NewDevice);
                        Trace.WriteLine("New Device Added To List: " + NewDevice.PID_Hex + " " + NewDevice.SerialNumber);
                        NewDevice = new HID();    //Finally clear the temp device for the next itteration   
                    }
                    else
                    {
                        // Failed to get device attributes
                        Trace.WriteLine("Failed to get device attributes properly. Device will not be added to the list.");
                    }
                    //Close the handle to the device 
                    if (!deviceHandle.IsClosed) deviceHandle.Close();
                    Marshal.FreeHGlobal(detailDataBuffer);
                    NewDevice = new HID();
                }
                //Release the resources of the DeviceInfoSet from the first step
                Setup_API_Wrappers.SetupDiDestroyDeviceInfoList(deviceInfoSet);

                if (NewDevice != null) NewDevice = null;
            }
            catch (Exception ex)
            {
                Trace.WriteLine("An error occured while attempting to populate the device list. Error text = " +
                    ex.ToString());
            }
        }

        internal static void AttachDevice(string devicePathName)
        {
            try
            {
                Trace.WriteLine("Adding a device that has been attached");
                Trace.WriteLine("Using new device path:" + devicePathName);

                // Create a HID object to store the new device info
                HID NewDevice = new HID();
                NewDevice.Path = devicePathName;
                bool Success = false;
                // Obtain a handle for talking to the device
                SafeFileHandle deviceHandle;
                deviceHandle = ReadWrite_API_Wrappers.CreateFile(
                    devicePathName,
                    (ReadWrite_API_Wrappers.GENERIC_READ | ReadWrite_API_Wrappers.GENERIC_WRITE),
                    ReadWrite_API_Wrappers.FILE_SHARE_READ | ReadWrite_API_Wrappers.FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    ReadWrite_API_Wrappers.OPEN_EXISTING,
                    ReadWrite_API_Wrappers.FILE_ATTRIBUTE_NORMAL | ReadWrite_API_Wrappers.FILE_FLAG_OVERLAPPED,
                    0);
                //Get the device attributes (VID, PID, and Serial Number)
                HID_API_Wrapers.HIDD_ATTRIBUTES DeviceAttributes = new HID_API_Wrapers.HIDD_ATTRIBUTES();
                Success = false;
                DeviceAttributes.Size = Marshal.SizeOf(DeviceAttributes);
                Success = HID_API_Wrapers.HidD_GetAttributes(deviceHandle, ref DeviceAttributes);
                if (Success)
                {
                    // Get the PID and VID
                    NewDevice.PID_Hex = Convert.ToInt32(DeviceAttributes.ProductID).ToString("X");
                    NewDevice.VID_Hex = Convert.ToInt32(DeviceAttributes.VendorID).ToString("X");
                    // Check if the PID and VID are valid
                    if ((NewDevice.PID_Hex.Length != 4) || (NewDevice.VID_Hex.Length != 4))
                    {
                        // There was a problem finding the PID or VID
                        Trace.WriteLine("Error finding PID or VID for attached device. " +
                            "PID=[" + NewDevice.PID_Hex + "] VID=[" + NewDevice.VID_Hex + "]");
                        Trace.WriteLine("Device with path [" + NewDevice.Path
                            + "] will not be added to the attached device list");
                        return;
                    }
                    // Obtain a pointer to the Device Capabilities
                    IntPtr preparsedData = new IntPtr();
                    Success = HID_API_Wrapers.HidD_GetPreparsedData(deviceHandle, ref preparsedData);
                    if (!Success)
                    {
                        Trace.WriteLine("Error obtaining device capabilities for device with path [" +
                            NewDevice.Path + "]. This device will not be added to the list");
                        return;
                    }
                    // Now Obtain the capabilities
                    HID_API_Wrapers.HIDP_CAPS Capabilities = new HID_API_Wrapers.HIDP_CAPS();
                    Int32 result = 0;
                    result = HID_API_Wrapers.HidP_GetCaps(preparsedData, ref Capabilities);
                    NewDevice.OutputReportSize = Convert.ToInt32(Capabilities.OutputReportByteLength);
                    NewDevice.InputReportSize = Convert.ToInt32(Capabilities.InputReportByteLength);
                    NewDevice.FeatureReportSize = Convert.ToInt32(Capabilities.FeatureReportByteLength);
                    //Get more device info...
                    short retries = 10;
                TryAgain:
                    NewDevice.SerialNumber = GetSerialNumberString(deviceHandle);
                    NewDevice.Manufacturer = GetManufacturerString(deviceHandle);
                    NewDevice.ProductDescription = GetProductString(deviceHandle);
                    // Check if device info was found properly
                    if ((NewDevice.SerialNumber == "Unknown") || (NewDevice.Manufacturer == "Unknown") ||
                        (NewDevice.ProductDescription == "Unknown"))
                    {
                        System.Threading.Thread.Sleep(50);  //pause for a bit
                        Trace.WriteLine("Failed to get Device Info Properly, Trying agian...");
                        if (retries == 0)
                        {
                            Trace.WriteLine("ERROR: Could not obtain SerialNumber, Manufacturer and Desctiption properly" +
                                " after multiple attempts. Divice is not being added to list");
                            return;
                        }
                        retries--;
                        goto TryAgain;
                    }
                    // Mark the new device as attached
                    NewDevice.DeviceIsAttached = true;
                    //Now check if the device is new to the list.
                    bool MatchFound = false;
                    for (int i = 0; i < _DetectedHIDs.Count; i++)
                    {
                        //Check if VID, PID and Serial numbers match.
                        if ((_DetectedHIDs[i].PID_Hex == NewDevice.PID_Hex) &&
                            (_DetectedHIDs[i].VID_Hex == NewDevice.VID_Hex) &&
                            (_DetectedHIDs[i].SerialNumber == NewDevice.SerialNumber))
                        {
                            // Replace the old device's info with the new info
                            _DetectedHIDs[i] = NewDevice;
                            // Trigger the device's attached event
                            _DetectedHIDs[i].TriggerDeviceAttached();
                            // Trigger the Monitor's attached event
                            DeviceListChangedArgs e = new DeviceListChangedArgs(NewDevice.PID_Hex,
                            NewDevice.VID_Hex, NewDevice.SerialNumber);
                            TriggerHIDAttached(e);
                            MatchFound = true;
                            Trace.WriteLine("Device Reconnected: " + NewDevice.PID_Hex + " " + NewDevice.SerialNumber);
                        }
                    }
                    if (!MatchFound)
                    {
                        //Add the device to the list
                        _DetectedHIDs.Add(NewDevice);
                        DeviceListChangedArgs x = new DeviceListChangedArgs(NewDevice.PID_Hex,
                            NewDevice.VID_Hex, NewDevice.SerialNumber);
                        TriggerHIDAttached(x);
                        Trace.WriteLine("New Device Added To List: " + NewDevice.PID_Hex + " " + NewDevice.SerialNumber);
                    }
                    //Finally clear the device 
                    NewDevice = null;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine("AN EXCEPTION OCCURRED WHILE TRYING TO ADD AN ATTACHED DEVICE.\n" +
                    "METHOD NAME = HIDMonitor.AttachDevice\n" + "Exception data = " + ex.ToString());
            }
        }

        internal static void SetDeviceDisconnected(string devicePathName)
        {
            for (int i = 0; i < _DetectedHIDs.Count; i++)
            {
                // Search for the device with a matching path
                if (String.Compare(_DetectedHIDs[i].Path.ToUpper(), devicePathName.ToUpper()) == 0)
                {
                    Trace.WriteLine("Setting Device NotAttached:" + _DetectedHIDs[i].Path + 
                        " Serial Number =" + _DetectedHIDs[i].SerialNumber);
                    // Mark the device in the list as disconnected
                    _DetectedHIDs[i].DeviceIsAttached = false;
                    // Trigger the devices Removed event
                    _DetectedHIDs[i].TriggerDeviceRemoved();
                    // Trigger the Monitor's Removed event
                    DeviceListChangedArgs e = new DeviceListChangedArgs(_DetectedHIDs[i].PID_Hex,
                        _DetectedHIDs[i].VID_Hex, _DetectedHIDs[i].SerialNumber);
                    TriggerHIDRemoved(e);
                    return;
                }
            }
            Trace.WriteLine("WARNING: A device was removed but a match was not found in the device list. " +
                "This should not happen but it may not cause a problem.");
        }

        private static string GetManufacturerString(SafeFileHandle sfh)
        {
            try
            {
                Byte[] rawBytes = new Byte[(126 + 1) * 2];
                bool success = HID_API_Wrapers.HidD_GetManufacturerString(sfh, rawBytes, rawBytes.Length);
                if (!success) return "Unknown";
                string result = System.Text.Encoding.Unicode.GetString(rawBytes);
                result = result.TrimEnd("\0".ToCharArray());
                return result;
            }
            catch (Exception ex)
            {
                Trace.WriteLine("ERROR: An Exception was thrown while attempting to retrieve the Device "
                    + "Manufacturer string.\nException Data = " + ex.ToString());
                return "Unknown";
            }
        }

        private static string GetSerialNumberString(SafeFileHandle sfh)
        {
            try
            {
                Byte[] rawBytes = new Byte[(126 + 1) * 2];
                bool success = HID_API_Wrapers.HidD_GetSerialNumberString(sfh, rawBytes, rawBytes.Length);
                if (!success)
                {
                    Debug.Write(HID.ParseWin32Error("GetSerialNumberString - Invalid=" + sfh.IsInvalid.ToString()));
                    return "Unknown";
                }
                string result = System.Text.Encoding.Unicode.GetString(rawBytes);
                if (result.Contains("OPTEC"))
                {
                    Byte[] temp = new Byte[] { rawBytes[13], rawBytes[12], rawBytes[11], rawBytes[10] };
                    int sn = BitConverter.ToInt32(temp, 0);
                    result = sn.ToString();

                }
                result = result.TrimEnd("\0".ToCharArray());
                return result;
            }
            catch (Exception ex)
            {
                Trace.WriteLine("ERROR: An Exception was thrown while attempting to retrieve the Device "
                    + "Serial Number string.\nException Data = " + ex.ToString());
                return "Unknown";
            }
        }

        private static string GetProductString(SafeFileHandle sfh)
        {
            try
            {
                Byte[] rawBytes = new Byte[(126 + 1) * 2];
                bool success = HID_API_Wrapers.HidD_GetProductString(sfh, rawBytes, rawBytes.Length);
                if (!success) return "Unknown";
                string result = System.Text.Encoding.Unicode.GetString(rawBytes);
                result = result.TrimEnd("\0".ToCharArray());
                return result;
            }
            catch (Exception ex)
            {
                Trace.WriteLine("ERROR: An Exception was thrown while attempting to retrieve the Device "
                    + "Product string.\nException Data = " + ex.ToString());
                return "Unknown";
            }
        }

        private static void TriggerHIDAttached(DeviceListChangedArgs HID)
        {
            TriggerAnEvent(HIDAttached, HID);
        }

        private static void TriggerHIDRemoved(DeviceListChangedArgs HID)
        {
            TriggerAnEvent(HIDRemoved, HID);
        }
       
        private static void TriggerAnEvent(EventHandler EH, DeviceListChangedArgs e)
        {
            if (EH == null) return;
            var EventListeners = EH.GetInvocationList();
            if (EventListeners != null)
            {
                for (int index = 0; index < EventListeners.Count(); index++)
                {
                    var methodToInvoke = (EventHandler)EventListeners[index];
                    object x = new object[]{};
                    methodToInvoke.BeginInvoke(x, e, EndAsyncEvent, new object {});
                }
            }

        }

        private static void EndAsyncEvent(IAsyncResult iar)
        {
            var ar = (System.Runtime.Remoting.Messaging.AsyncResult)iar;
            var invokedMethod = (EventHandler)ar.AsyncDelegate;

            try
            {
                invokedMethod.EndInvoke(iar);
            }
            catch
            {
                // Handle any exceptions that were thrown by the invoked method
                Trace.WriteLine("An event listener went kaboom!");
            }
        }

        #endregion 
    }
}
