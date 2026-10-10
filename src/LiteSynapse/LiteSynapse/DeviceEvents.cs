using System;
using System.Runtime.InteropServices;

namespace LiteSynapse;

internal static class DeviceEvents
{
	private const int DBT_DEVICEARRIVAL = 32768;

	private const int DBT_DEVICEREMOVECOMPLETE = 32772;

	private const int DBT_DEVTYP_DEVICEINTERFACE = 5;

	private static readonly Guid HidClass = new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");

	[DllImport("user32", SetLastError = true)]
	private static extern nint RegisterDeviceNotification(nint hRecipient, nint filter, uint flags);

	[DllImport("user32")]
	private static extern bool UnregisterDeviceNotification(nint handle);

	public static nint Register(nint hwnd)
	{
		nint filter = Marshal.AllocHGlobal(32);
		try
		{
			for (int i = 0; i < 32; i += 8)
			{
				Marshal.WriteInt64(filter, i, 0L);
			}
			Marshal.WriteInt32(filter, 0, 32);
			Marshal.WriteInt32(filter, 4, 5);
			Marshal.Copy(HidClass.ToByteArray(), 0, filter + 12, 16);
			nint num = RegisterDeviceNotification(hwnd, filter, 0u);
			Cfg.Log((num != IntPtr.Zero) ? "watching for Razer device changes (no polling)" : "device notifications unavailable");
			return num;
		}
		finally
		{
			Marshal.FreeHGlobal(filter);
		}
	}

	public static void Unregister(nint h)
	{
		if (h != IntPtr.Zero)
		{
			UnregisterDeviceNotification(h);
		}
	}

	public static bool IsRazerChange(nint wParam, nint lParam)
	{
		int what = (int)wParam;
		if (what != 32768 && what != 32772)
		{
			return false;
		}
		if (lParam == IntPtr.Zero || Marshal.ReadInt32(lParam, 4) != 5)
		{
			return false;
		}
		// Only the Naga and the BlackWidow Lite: a Razer headset, dongle or dock coming and going must not reset
		// and re-send everything to two devices that never left, mid-match.
		return DeviceTuning.IsOurDevice(Marshal.PtrToStringUni(lParam + 28) ?? "");
	}
}
