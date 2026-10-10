using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LiteSynapse;

// One Razer device that speaks the 90-byte "protocol 2.5" feature reports (91 bytes with the report ID).
//
// Transport rules (from Synapse's own host and OpenRazer):
//  - one handle per device, kept open and reopened only when a call fails or the device changed;
//  - a fixed transaction id per device (Naga Left-Handed 0x1F, BlackWidow Lite 0x3F), with a one-time
//    probe of the other common ids if the device does not accept it;
//  - after the SET, wait ~5 ms, then GET the status; while the device says "new" (0) or "busy" (1),
//    wait a little longer and GET again. The SET is never sent twice. 2 = success, 3/4/5 = failure;
//  - one lock per device, so the device worker, the UI and the CLI tests can share an instance.
internal sealed class RazerDevice : IDisposable
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

	public const int StatusNew = StatusPoll.New;

	public const int StatusBusy = StatusPoll.Busy;

	public const int StatusOk = StatusPoll.Ok;

	public const int StatusFailed = StatusPoll.Failed;

	public const int StatusTimeout = StatusPoll.Timeout;

	public const int StatusNotSupported = StatusPoll.NotSupported;

	// Returned instead of a device status when the device could not be reached at all.
	public const int IoError = -1;

	private const int ReportLength = RazerReport.Length;

	private static readonly byte[] ProbeIds = new byte[4] { 0x1F, 0x3F, 0xFF, 0x00 };

	private readonly string path;

	private readonly object gate = new object();

	private readonly byte[] request = new byte[ReportLength];

	private readonly byte[] response = new byte[ReportLength];

	private readonly Native.PreciseTimer timer = new Native.PreciseTimer();

	private SafeFileHandle handle;

	private byte txn;

	// The transaction id is settled: it worked once, or the one-time probe already ran.
	private bool probed;

	// The device has answered with its own class/command echoed (every Razer device does). Only once that was seen
	// is a final reply for another command taken as another program's (Synapse, a second LiteSynapse) and skipped.
	private bool echoSeen;

	private bool foreignLogged;

	private bool disposed;

	// Last values this device confirmed (status 2) or reported. null = unknown, ask/send again.
	private int? dpi;

	private int? polling;

	private int? slot;

	private int? brightness;

	public string Name { get; }

	public int ProductId { get; }

	// Transaction id used by Command/Send (not by Raw, whose caller picks one).
	public byte TransactionId
	{
		get
		{
			lock (gate)
			{
				return txn;
			}
		}
	}

	// How many times the handle had to be (re)opened after the first one; for the self-test.
	public int Reopens { get; private set; }

	// Number of SET/GET transactions sent, and the slowest one in milliseconds; for the self-test.
	public int Transactions { get; private set; }

	public double SlowestMs { get; private set; }

	public double TotalMs { get; private set; }

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern SafeFileHandle CreateFile(string n, uint access, uint share, nint sa, uint disp, uint flags, nint t);

	[DllImport("hid.dll", SetLastError = true)]
	private static extern bool HidD_SetFeature(SafeFileHandle h, byte[] b, int len);

	[DllImport("hid.dll", SetLastError = true)]
	private static extern bool HidD_GetFeature(SafeFileHandle h, byte[] b, int len);

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

	private RazerDevice(string name, int pid, string path, SafeFileHandle open)
	{
		Name = name;
		ProductId = pid;
		this.path = path;
		handle = open;
		txn = DefaultTransactionId(pid);
	}

	// OpenRazer: Naga Left-Handed 2020 uses 0x1F, BlackWidow Lite 0x3F; other Razer devices mostly 0xFF.
	internal static byte DefaultTransactionId(int pid)
	{
		return pid switch
		{
			0x008D => 0x1F,
			0x0235 => 0x3F,
			_ => 0xFF,
		};
	}

	private static SafeFileHandle Open(string p)
	{
		// Access 0 is enough for feature reports and works while other programs hold the device.
		return CreateFile(p, 0u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
	}

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
					string p = Marshal.PtrToStringUni(buf + 4);
					if (p == null || !p.Contains(want, StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
					SafeFileHandle h = Open(p);
					bool keep = false;
					try
					{
						if (h.IsInvalid || !HidD_GetPreparsedData(h, out var pre))
						{
							continue;
						}
						try
						{
							if (HidP_GetCaps(pre, out var caps) >= 0 && caps.FeatureReportByteLength == ReportLength)
							{
								keep = true;
								return new RazerDevice(name, pid, p, h);
							}
						}
						finally
						{
							HidD_FreePreparsedData(pre);
						}
					}
					finally
					{
						if (!keep)
						{
							h.Dispose();
						}
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

	// True while the device's interface still exists (checked with a separate, short-lived handle).
	public bool Alive()
	{
		using SafeFileHandle h = Open(path);
		return !h.IsInvalid;
	}

	// Forget the open handle and everything cached about the device (after a plug/unplug, resume, ...).
	// The next command reopens the handle.
	public void Reset()
	{
		lock (gate)
		{
			CloseHandle();
			InvalidateCacheLocked();
		}
	}

	// Forget the cached DPI/polling/slot/brightness so the next setter sends its value even if it looks
	// unchanged (the user may have changed it with a button on the device).
	public void InvalidateCache()
	{
		lock (gate)
		{
			InvalidateCacheLocked();
		}
	}

	private void InvalidateCacheLocked()
	{
		dpi = null;
		polling = null;
		slot = null;
		brightness = null;
	}

	public void Dispose()
	{
		lock (gate)
		{
			disposed = true;
			CloseHandle();
			timer.Dispose();
		}
	}

	private void CloseHandle()
	{
		handle?.Dispose();
		handle = null;
	}

	private SafeFileHandle Handle()
	{
		if (handle != null && !handle.IsInvalid && !handle.IsClosed)
		{
			return handle;
		}
		CloseHandle();
		if (disposed)
		{
			return null;
		}
		SafeFileHandle h = Open(path);
		if (h.IsInvalid)
		{
			int err = Marshal.GetLastWin32Error();
			h.Dispose();
			Cfg.Log($"{Name}: open failed {err}");
			return null;
		}
		handle = h;
		Reopens++;
		// A new handle can mean a re-plugged device: nothing cached is trusted any more.
		InvalidateCacheLocked();
		return h;
	}

	// One SET + status poll. Caller holds the lock. Returns the device status, or IoError.
	// reply (optional) receives the 80 argument bytes of a successful answer.
	private int Transact(byte txnId, byte cls, byte id, byte size, ReadOnlySpan<byte> args, byte[] reply)
	{
		for (int attempt = 0; attempt < 2; attempt++)
		{
			SafeFileHandle h = Handle();
			if (h == null)
			{
				return IoError;
			}
			RazerReport.Build(request, txnId, cls, id, size, args);
			long start = Stopwatch.GetTimestamp();
			if (!HidD_SetFeature(h, request, ReportLength))
			{
				// Nothing reached the device (unplugged, re-plugged, sleeping): reopen once and send again.
				CloseHandle();
				continue;
			}
			int status = Poll(h, reply);
			Transactions++;
			double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
			TotalMs += ms;
			if (ms > SlowestMs)
			{
				SlowestMs = ms;
			}
			if (status == IoError)
			{
				// The SET went out, so it is not sent again; the handle is reopened next time.
				CloseHandle();
			}
			return status;
		}
		return IoError;
	}

	// Reads the status until the device gives a final answer (see StatusPoll). The SET is never repeated here.
	private int Poll(SafeFileHandle h, byte[] reply)
	{
		int status = StatusNew;
		for (int read = 0; ; read++)
		{
			int wait = StatusPoll.WaitBefore(read, status);
			if (wait < 0)
			{
				return status;
			}
			timer.Sleep(wait);
			Array.Clear(response);
			if (!HidD_GetFeature(h, response, ReportLength))
			{
				return IoError;
			}
			status = response[1];
			if (StatusPoll.IsFinal(status))
			{
				if (RazerReport.Answers(request, response))
				{
					echoSeen = true;
				}
				else if (echoSeen)
				{
					// The answer to another program's command on the same device: not ours, keep reading.
					if (!foreignLogged)
					{
						foreignLogged = true;
						Cfg.Log($"{Name}: skipped an answer to another program's command ({response[7]:X2}/{response[8]:X2}); is Razer Synapse running?");
					}
					status = StatusNew;
					continue;
				}
			}
			if (status == StatusOk && reply != null)
			{
				Array.Copy(response, RazerReport.ArgsOffset, reply, 0, Math.Min(reply.Length, RazerReport.ArgsLength));
			}
		}
	}

	// Sends with this device's transaction id. If the id has never worked yet and a command fails, tries the
	// other usual ids once and keeps the one that works. Once a command succeeded the id is known to be right,
	// so a later failure (e.g. an LED the device does not have) never starts a probe.
	private int TransactAuto(byte cls, byte id, byte size, ReadOnlySpan<byte> args, byte[] reply)
	{
		int status = Transact(txn, cls, id, size, args, reply);
		if (status == StatusOk)
		{
			probed = true;
			return status;
		}
		if (status == IoError || probed)
		{
			return status;
		}
		probed = true;
		foreach (byte t in ProbeIds)
		{
			if (t == txn)
			{
				continue;
			}
			int s = Transact(t, cls, id, size, args, reply);
			if (s == StatusOk)
			{
				Cfg.Log($"{Name}: transaction id 0x{txn:X2} not accepted (status {status}), using 0x{t:X2}");
				txn = t;
				return s;
			}
			if (s == IoError)
			{
				return s;
			}
		}
		return status;
	}

	// Sends one command with a caller-chosen transaction id and returns the device status
	// (2 = success, 1 = still busy after polling, 3/4/5 = failure, -1 = device not reachable).
	public int Raw(byte txnId, byte cls, byte id, byte size, byte[] args)
	{
		lock (gate)
		{
			return Transact(txnId, cls, id, size, args ?? Array.Empty<byte>(), null);
		}
	}

	// Status-returning command for new code. reply (optional, 80 bytes) receives the answer's arguments.
	public int Send(byte cls, byte id, byte size, byte[] args, byte[] reply = null)
	{
		lock (gate)
		{
			return TransactAuto(cls, id, size, args ?? Array.Empty<byte>(), reply);
		}
	}

	// Returns the 80 argument bytes of the answer, or null if the device did not answer with success.
	public byte[] Command(byte cls, byte id, byte size, params byte[] args)
	{
		byte[] reply = new byte[80];
		int status;
		lock (gate)
		{
			status = TransactAuto(cls, id, size, args ?? Array.Empty<byte>(), reply);
		}
		if (status != StatusOk)
		{
			Cfg.Log($"{Name}: command {cls:X2}/{id:X2} failed (status {status})");
			return null;
		}
		return reply;
	}

	// The storage byte that addresses the profile the mouse is running now. On the Naga it is the on-board slot
	// number (Synapse writes each slot's DPI and lighting with its slot number); 0x01 (VARSTORE) only reaches
	// slot 1. Asks the mouse for its slot when it is not known yet; 0x01 if it does not say.
	public byte ActiveStorage()
	{
		lock (gate)
		{
			return ActiveStorageLocked();
		}
	}

	private byte ActiveStorageLocked()
	{
		if (!slot.HasValue)
		{
			byte[] a = new byte[RazerReport.ArgsLength];
			if (TransactAuto(5, 132, 1, ReadOnlySpan<byte>.Empty, a) == StatusOk)
			{
				slot = a[0];
			}
		}
		return (slot.HasValue && slot.Value >= 1 && slot.Value <= 5) ? (byte)slot.Value : (byte)1;
	}

	public (int x, int y)? GetDpi(int? slot = null)
	{
		lock (gate)
		{
			byte[] a = new byte[80];
			byte store = ActiveStorageLocked();
			int st = TransactAuto(4, 133, 7, stackalloc byte[1] { store }, a);
			if (st != StatusOk && store != 1)
			{
				st = TransactAuto(4, 133, 7, stackalloc byte[1] { 1 }, a);
			}
			if (st != StatusOk)
			{
				dpi = null;
				return null;
			}
			int x = (a[1] << 8) | a[2];
			int y = (a[3] << 8) | a[4];
			dpi = (x == y) ? x : null;
			return (x, y);
		}
	}

	// Records a DPI the device is known to have without sending anything: after a slot switch or a slot write,
	// the slot's own stored DPI is in effect and the profile's write already put it there.
	public void SeedDpi(int value)
	{
		lock (gate)
		{
			dpi = value;
		}
	}

	public bool SetDpi(int dpi, int? slot = null)
	{
		lock (gate)
		{
			if (this.dpi == dpi)
			{
				return true;
			}
			// To the profile the mouse is running now (its slot), falling back to 0x01 if the mouse refuses that.
			byte store = ActiveStorageLocked();
			int s = TransactAuto(4, 5, 7, stackalloc byte[5]
			{
				store,
				(byte)(dpi >> 8),
				(byte)dpi,
				(byte)(dpi >> 8),
				(byte)dpi
			}, null);
			if (s != StatusOk && store != 1)
			{
				Cfg.Log($"{Name}: DPI for slot {store} refused (status {s}), using 0x01");
				s = TransactAuto(4, 5, 7, stackalloc byte[5]
				{
					1,
					(byte)(dpi >> 8),
					(byte)dpi,
					(byte)(dpi >> 8),
					(byte)dpi
				}, null);
			}
			this.dpi = (s == StatusOk) ? dpi : null;
			return s == StatusOk;
		}
	}

	public int? GetPolling()
	{
		lock (gate)
		{
			byte[] a = new byte[80];
			if (TransactAuto(0, 133, 1, ReadOnlySpan<byte>.Empty, a) != StatusOk)
			{
				polling = null;
				return null;
			}
			polling = a[0] switch
			{
				1 => 1000,
				2 => 500,
				8 => 125,
				_ => null,
			};
			return polling;
		}
	}

	public bool SetPolling(int hz)
	{
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
		lock (gate)
		{
			if (polling == hz)
			{
				return true;
			}
			int s = TransactAuto(0, 5, 1, stackalloc byte[1] { v }, null);
			polling = (s == StatusOk) ? hz : null;
			return s == StatusOk;
		}
	}

	public int? GetActiveSlot()
	{
		lock (gate)
		{
			byte[] a = new byte[80];
			if (TransactAuto(5, 132, 1, ReadOnlySpan<byte>.Empty, a) != StatusOk)
			{
				slot = null;
				return null;
			}
			slot = a[0];
			return slot;
		}
	}

	public bool SetActiveSlot(int slot)
	{
		lock (gate)
		{
			if (this.slot == slot)
			{
				return true;
			}
			// Same bytes (including transaction id 0x41) as the switch that was verified on the mouse.
			int s = Transact(65, 5, 4, 1, stackalloc byte[1] { (byte)slot }, null);
			// A slot switch loads that slot's own DPI, so what was last sent no longer holds. The polling rate is
			// not part of a slot: its command (00/05) and its read (00/85) take no slot byte, and an onboard write
			// never stores one, so the rate stays as it was and its cache is still good.
			dpi = null;
			this.slot = null;
			bool ok = s == StatusOk;
			if (!ok && s != IoError)
			{
				// The previous transport ignored this command's status and read the slot back; do the same
				// whenever the answer is not a clean success.
				byte[] a = new byte[RazerReport.ArgsLength];
				ok = TransactAuto(5, 132, 1, ReadOnlySpan<byte>.Empty, a) == StatusOk && a[0] == slot;
				if (ok)
				{
					Cfg.Log($"{Name}: slot switch answered status {s}, read-back shows slot {slot}");
				}
			}
			if (ok)
			{
				this.slot = slot;
			}
			return ok;
		}
	}

	public int? GetBrightness()
	{
		lock (gate)
		{
			byte[] a = new byte[80];
			if (TransactAuto(15, 132, 3, stackalloc byte[2] { 1, 5 }, a) != StatusOk)
			{
				brightness = null;
				return null;
			}
			brightness = (int)Math.Round((double)(int)a[2] * 100.0 / 255.0);
			return brightness;
		}
	}

	public bool SetBrightness(int percent)
	{
		percent = Math.Clamp(percent, 0, 100);
		byte v = (byte)Math.Round((double)(percent * 255) / 100.0);
		lock (gate)
		{
			if (brightness.HasValue && Math.Abs(brightness.Value - percent) <= 1)
			{
				return true;
			}
			int s = TransactAuto(15, 4, 3, stackalloc byte[3] { 1, 5, v }, null);
			brightness = (s == StatusOk) ? percent : null;
			return s == StatusOk;
		}
	}
}
