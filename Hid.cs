using System;
using HidSharp;
using HidSharp.Platform.Linux;

class Program
{
    static void Main()
    {
        var devices = DeviceList.Local.GetHidDevices();
        foreach (var device in devices)
        {
            Console.WriteLine($"{device.VendorID:X4}:{device.ProductID:X4} - {device.GetManufacturer()} - {device.GetProductName()} - {device.DevicePath}");
        }
    }
}
