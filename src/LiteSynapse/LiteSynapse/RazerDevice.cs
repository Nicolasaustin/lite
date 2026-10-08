using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace LiteSynapse;

internal class RazerDevice
{
	private struct HIDP_CAPS
	{
		public ushort Usage;

		public ushort UsagePage;

		public ushort InputReportByteLength;

		public ushort OutputReportByteLength;

		public ushort FeatureReportByteLength;

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
		public ushort[] Reserved;

		public ushort NumberLinkCollectionNodes;

		public ushort NumberInputButtonCaps;

		public ushort NumberInputValueCaps;

		public ushort NumberInputDataIndices;

		public ushort NumberOutputButtonCaps;

		public ushort NumberOutputValueCaps;

		public ushort NumberOutputDataIndices;

		public ushort NumberFeatureButtonCaps;

		public ushort NumberFeatureValueCaps;

		public ushort NumberFeatureDataIndices;
	}

	private struct SP_DEVICE_INTERFACE_DATA
	{
		public int cbSize;

		public Guid InterfaceClassGuid;

		public int Flags;

		public nint Reserved;
	}

	private readonly string path;

	private byte txn = byte.MaxValue;

	private readonly object gate = new object();

	public string Name { get; }

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern SafeFileHandle CreateFile(string n, uint access, uint share, nint sa, uint disp, uint flags, nint t);

	[DllImport("hid.dll", SetLastError = true)]
	private static extern bool HidD_SetFeature(SafeFileHandle h, byte[] b, int len);

	[DllImport("hid.dll", SetLastError = true)]
	private static extern bool HidD_GetFeature(SafeFileHandle h, byte[] b, int len);

	private RazerDevice(string name, string path)
	{
		Name = name;
		this.path = path;
	}

	[DllImport("hid.dll")]
	private static extern void HidD_GetHidGuid(out Guid g);

	[DllImport("hid.dll")]
	private static extern bool HidD_GetPreparsedData(SafeFileHandle h, out nint data);

	[DllImport("hid.dll")]
	private static extern bool HidD_FreePreparsedData(nint data);

	[DllImport("hid.dll")]
	private static extern int HidP_GetCaps(nint preparsed, out HIDP_CAPS caps);

	[DllImport("setupapi.dll", SetLastError = true)]
	private static extern nint SetupDiGetClassDevs(ref Guid g, nint enumerator, nint hwnd, uint flags);

	[DllImport("setupapi.dll", SetLastError = true)]
	private static extern bool SetupDiEnumDeviceInterfaces(nint set, nint info, ref Guid g, uint index, ref SP_DEVICE_INTERFACE_DATA data);

	[DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool SetupDiGetDeviceInterfaceDetail(nint set, ref SP_DEVICE_INTERFACE_DATA data, nint detail, int size, out int needed, nint info);

	[DllImport("setupapi.dll")]
	private static extern bool SetupDiDestroyDeviceInfoList(nint set);

	public static RazerDevice Find(int pid, string name)
	{
		string want = $"vid_1532&pid_{pid:x4}";
		HidD_GetHidGuid(out var hid);
		nint set = SetupDiGetClassDevs(ref hid, IntPtr.Zero, IntPtr.Zero, 18u);
		if (set == new IntPtr(-1))
		{
			return null;
		}
		try
		{
			SP_DEVICE_INTERFACE_DATA data = new SP_DEVICE_INTERFACE_DATA
			{
				cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>()
			};
			for (uint i = 0u; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hid, i, ref data); i++)
			{
				SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out var need, IntPtr.Zero);
				nint buf = Marshal.AllocHGlobal(need);
				try
				{
					Marshal.WriteInt32(buf, (IntPtr.Size == 8) ? 8 : 6);
					if (!SetupDiGetDeviceInterfaceDetail(set, ref data, buf, need, out var _, IntPtr.Zero))
					{
						continue;
					}
					string path = Marshal.PtrToStringUni(buf + 4);
					if (path == null || !path.Contains(want, StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
					using SafeFileHandle h = CreateFile(path, 0u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
					if (h.IsInvalid || !HidD_GetPreparsedData(h, out var pre))
					{
						continue;
					}
					try
					{
						if (HidP_GetCaps(pre, out var caps) >= 0 && caps.FeatureReportByteLength == 91)
						{
							return new RazerDevice(name, path);
						}
					}
					finally
					{
						HidD_FreePreparsedData(pre);
					}
				}
				finally
				{
					Marshal.FreeHGlobal(buf);
				}
			}
		}
		finally
		{
			SetupDiDestroyDeviceInfoList(set);
		}
		return null;
	}

	public bool Alive()
	{
		using SafeFileHandle h = CreateFile(path, 0u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
		return !h.IsInvalid;
	}

	public int Raw(byte txnId, byte cls, byte id, byte size, byte[] args)
	{
		lock (gate)
		{
			using SafeFileHandle h = CreateFile(path, 0u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
			if (h.IsInvalid)
			{
				return -1;
			}
			for (int tries = 0; tries < 12; tries++)
			{
				byte[] r = new byte[91];
				r[2] = txnId;
				r[6] = size;
				r[7] = cls;
				r[8] = id;
				Array.Copy(args, 0, r, 9, Math.Min(args.Length, 80));
				byte crc = 0;
				for (int i = 3; i < 89; i++)
				{
					crc ^= r[i];
				}
				r[89] = crc;
				if (!HidD_SetFeature(h, r, 91))
				{
					return -1;
				}
				Thread.Sleep(20);
				byte[] resp = new byte[91];
				if (!HidD_GetFeature(h, resp, 91))
				{
					return -1;
				}
				if (resp[1] != 1)
				{
					return resp[1];
				}
				Thread.Sleep(80);
			}
			return 1;
		}
	}

	public byte[] Command(byte cls, byte id, byte size, params byte[] args)
	{
		lock (gate)
		{
			using SafeFileHandle h = CreateFile(path, 0u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
			if (h.IsInvalid)
			{
				Cfg.Log($"{Name}: open failed {Marshal.GetLastWin32Error()}");
				return null;
			}
			byte[] array = new byte[4] { 0, 255, 31, 63 };
			array[0] = txn;
			foreach (byte t in array.Distinct())
			{
				byte[] r = new byte[91];
				r[2] = t;
				r[6] = size;
				r[7] = cls;
				r[8] = id;
				Array.Copy(args, 0, r, 9, Math.Min(args.Length, 80));
				byte crc = 0;
				for (int i = 3; i < 89; i++)
				{
					crc ^= r[i];
				}
				r[89] = crc;
				if (HidD_SetFeature(h, r, 91))
				{
					Thread.Sleep(30);
					byte[] resp = new byte[91];
					if (HidD_GetFeature(h, resp, 91) && resp[1] == 2)
					{
						txn = t;
						return resp[9..89];
					}
				}
			}
			Cfg.Log($"{Name}: command {cls:X2}/{id:X2} failed");
			return null;
		}
	}

	public (int x, int y)? GetDpi(int? slot = null)
	{
		byte[] a = Command(4, 133, 7, 1);
		if (a != null)
		{
			return ((a[1] << 8) | a[2], (a[3] << 8) | a[4]);
		}
		return null;
	}

	public bool SetDpi(int dpi, int? slot = null)
	{
		(int, int)? dpi2 = GetDpi();
		if (dpi2.HasValue)
		{
			(int, int) cur = dpi2.GetValueOrDefault();
			if (cur.Item1 == dpi && cur.Item2 == dpi)
			{
				return true;
			}
		}
		Command(4, 5, 7, 1, (byte)(dpi >> 8), (byte)dpi, (byte)(dpi >> 8), (byte)dpi);
		dpi2 = GetDpi();
		if (dpi2.HasValue)
		{
			return dpi2.GetValueOrDefault().Item1 == dpi;
		}
		return false;
	}

	public int? GetPolling()
	{
		byte[] a = Command(0, 133, 1);
		if (a == null)
		{
			return null;
		}
		return a[0] switch
		{
			1 => (int?)1000, 
			2 => 500, 
			8 => 125, 
			_ => null, 
		};
	}

	public bool SetPolling(int hz)
	{
		if (GetPolling() == hz)
		{
			return true;
		}
		byte v = hz switch
		{
			1000 => (byte)1, 
			500 => (byte)2, 
			125 => (byte)8, 
			_ => (byte)0, 
		};
		if (v == 0)
		{
			return false;
		}
		Command(0, 5, 1, v);
		return GetPolling() == hz;
	}

	public int? GetActiveSlot()
	{
		byte[] a = Command(5, 132, 1);
		if (a != null)
		{
			return a[0];
		}
		return null;
	}

	public bool SetActiveSlot(int slot)
	{
		if (GetActiveSlot() == slot)
		{
			return true;
		}
		Raw(65, 5, 4, 1, new byte[1] { (byte)slot });
		return GetActiveSlot() == slot;
	}

	public int? GetBrightness()
	{
		byte[] a = Command(15, 132, 3, 1, 5);
		if (a != null)
		{
			return (int)Math.Round((double)(int)a[2] * 100.0 / 255.0);
		}
		return null;
	}

	public bool SetBrightness(int percent)
	{
		byte v = (byte)Math.Round((double)(Math.Clamp(percent, 0, 100) * 255) / 100.0);
		int? brightness = GetBrightness();
		if (brightness.HasValue)
		{
			int cur = brightness.GetValueOrDefault();
			if (Math.Abs(cur - percent) <= 1)
			{
				return true;
			}
		}
		Command(15, 4, 3, 1, 5, v);
		brightness = GetBrightness();
		if (brightness.HasValue)
		{
			int n = brightness.GetValueOrDefault();
			return Math.Abs(n - percent) <= 1;
		}
		return false;
	}
}
